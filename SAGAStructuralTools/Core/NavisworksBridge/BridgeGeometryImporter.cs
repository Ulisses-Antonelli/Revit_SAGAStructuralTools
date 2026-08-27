using Autodesk.Revit.DB;
using SAGAStructuralTools.Core;
using SAGAStructuralTools.Core.Models;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SAGAStructuralTools.Core.NavisworksBridge
{
    /// <summary>
    /// Lê o arquivo-ponte (.json) exportado pelo plugin do Navisworks e recria
    /// cada item como um DirectShape — cópia burra de malha, sem inteligência
    /// paramétrica, só para locação (fixação de equipamentos).
    /// </summary>
    internal static class BridgeGeometryImporter
    {
        public class CreatedShapeInfo
        {
            public long ElementId;
            public string Name;
        }

        public class ImportResult
        {
            public int ItemCount;
            public int ShapeCount;
            public int TriangleCount;
            public int SkippedTriangleCount;
            public List<string> Warnings = new List<string>();
            public List<CreatedShapeInfo> CreatedShapes = new List<CreatedShapeInfo>();
        }

        public static ImportResult Import(Document doc, string jsonPath)
        {
            var result = new ImportResult();

            string json = File.ReadAllText(jsonPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var items = JsonSerializer.Deserialize<List<BridgeItem>>(json, options) ?? new List<BridgeItem>();
            result.ItemCount = items.Count;

            // O Navisworks federa modelos de disciplinas diferentes por
            // coordenadas compartilhadas — por isso os pontos do bridge são
            // tratados como coordenadas compartilhadas deste documento, e
            // precisam ser convertidos pra coordenadas internas antes de
            // desenhar (senão a malha nasce deslocada sempre que o Ponto Base
            // do Projeto e o Ponto Base de Levantamento não coincidirem).
            Transform sharedToInternal = doc.ActiveProjectLocation.GetTotalTransform().Inverse;

            var categoryId = new ElementId(BuiltInCategory.OST_GenericModel);

            foreach (var item in items)
            {
                if (item.malha == null || item.malha.Count == 0)
                {
                    result.Warnings.Add($"'{item.item}': sem triângulos — ignorado.");
                    continue;
                }

                var builder = new TessellatedShapeBuilder();
                builder.OpenConnectedFaceSet(false);

                int added = 0;
                XYZ firstRawFt = null;
                XYZ firstInternal = null;
                foreach (var tri in item.malha)
                {
                    XYZ p0 = ToInternalPoint(tri.v0, sharedToInternal);
                    XYZ p1 = ToInternalPoint(tri.v1, sharedToInternal);
                    XYZ p2 = ToInternalPoint(tri.v2, sharedToInternal);

                    if (firstInternal == null)
                    {
                        firstInternal = p0;
                        firstRawFt = new XYZ(
                            UnitUtils.ConvertToInternalUnits(tri.v0[0], UnitTypeId.Millimeters),
                            UnitUtils.ConvertToInternalUnits(tri.v0[1], UnitTypeId.Millimeters),
                            UnitUtils.ConvertToInternalUnits(tri.v0[2], UnitTypeId.Millimeters));
                    }

                    if (IsDegenerate(p0, p1, p2))
                    {
                        result.SkippedTriangleCount++;
                        continue;
                    }

                    var face = new TessellatedFace(new List<XYZ> { p0, p1, p2 }, ElementId.InvalidElementId);
                    builder.AddFace(face);
                    added++;
                }
                builder.CloseConnectedFaceSet();

                if (added == 0)
                {
                    result.Warnings.Add($"'{item.item}': todos os triângulos eram degenerados — ignorado.");
                    continue;
                }

                builder.Target = TessellatedShapeBuilderTarget.AnyGeometry;
                builder.Fallback = TessellatedShapeBuilderFallback.Mesh;
                builder.Build();
                var buildResult = builder.GetBuildResult();

                var shape = DirectShape.CreateElement(doc, categoryId);
                shape.SetShape(buildResult.GetGeometricalObjects());
                shape.Name = string.IsNullOrWhiteSpace(item.item) ? "SAGA - Malha Navisworks" : item.item;

                result.ShapeCount++;
                result.TriangleCount += added;
                result.CreatedShapes.Add(new CreatedShapeInfo { ElementId = shape.Id.GetId(), Name = shape.Name });

                double distMeters = firstInternal.DistanceTo(XYZ.Zero) * 0.3048;
                SagaLog.Write(
                    $"'{item.item}' -> ElementId {shape.Id.GetId()}, {added} triângulo(s). " +
                    $"1º vértice: bruto(pés, sem transform)={firstRawFt}, final(pés, interno)={firstInternal}, " +
                    $"distância da Origem Interna = {distMeters:F1} m.");
            }

            return result;
        }

        private static XYZ ToInternalPoint(double[] mm, Transform sharedToInternal)
        {
            var sharedPoint = new XYZ(
                UnitUtils.ConvertToInternalUnits(mm[0], UnitTypeId.Millimeters),
                UnitUtils.ConvertToInternalUnits(mm[1], UnitTypeId.Millimeters),
                UnitUtils.ConvertToInternalUnits(mm[2], UnitTypeId.Millimeters));
            return sharedToInternal.OfPoint(sharedPoint);
        }

        private static bool IsDegenerate(XYZ p0, XYZ p1, XYZ p2)
        {
            return (p1 - p0).CrossProduct(p2 - p0).GetLength() < 1e-9;
        }
    }
}
