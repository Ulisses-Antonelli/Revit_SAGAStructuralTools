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

        public static BridgeFile ReadFile(string jsonPath)
        {
            string json = File.ReadAllText(jsonPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<BridgeFile>(json, options) ?? new BridgeFile();
        }

        /// <param name="placement">
        /// Leva o 0,0,0 do arquivo-ponte (o ponto de referência clicado no Navisworks)
        /// para onde ele deve cair nas coordenadas internas do Revit, já com a
        /// rotação em Z aplicada.
        /// </param>
        public static ImportResult BuildAndInsert(Document doc, BridgeFile file, Transform placement)
        {
            var result = new ImportResult();
            var items = file.itens ?? new List<BridgeItem>();
            result.ItemCount = items.Count;

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
                XYZ firstLocalFt = null;
                XYZ firstInternal = null;
                foreach (var tri in item.malha)
                {
                    XYZ p0 = ToInternalPoint(tri.v0, placement);
                    XYZ p1 = ToInternalPoint(tri.v1, placement);
                    XYZ p2 = ToInternalPoint(tri.v2, placement);

                    if (firstInternal == null)
                    {
                        firstInternal = p0;
                        firstLocalFt = ToLocalFeet(tri.v0);
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
                    $"1º vértice: local(pés, relativo à referência)={firstLocalFt}, final(pés, interno)={firstInternal}, " +
                    $"distância da Origem Interna = {distMeters:F1} m.");
            }

            return result;
        }

        private static XYZ ToLocalFeet(double[] mm)
        {
            return new XYZ(
                UnitUtils.ConvertToInternalUnits(mm[0], UnitTypeId.Millimeters),
                UnitUtils.ConvertToInternalUnits(mm[1], UnitTypeId.Millimeters),
                UnitUtils.ConvertToInternalUnits(mm[2], UnitTypeId.Millimeters));
        }

        private static XYZ ToInternalPoint(double[] mm, Transform placement)
        {
            return placement.OfPoint(ToLocalFeet(mm));
        }

        private static bool IsDegenerate(XYZ p0, XYZ p1, XYZ p2)
        {
            return (p1 - p0).CrossProduct(p2 - p0).GetLength() < 1e-9;
        }
    }
}
