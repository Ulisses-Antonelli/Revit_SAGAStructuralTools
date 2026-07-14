using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SAGAStructuralTools.Core;
using SAGAStructuralTools.Core.Models;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SAGAStructuralTools.Core.Rail
{
    public class HandrailBuilder
    {
        private readonly Document _doc;
        private static readonly string LogPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "SAGA_RailLog.txt");

        public HandrailBuilder(Document doc) => _doc = doc;

        /// <summary>
        /// Cria o corrimão e retorna a elevação Z (em pés) do EIXO CENTRAL real da seção,
        /// medida pela BoundingBox da instância (independe de nome de parâmetro). Retorna
        /// null se não há corrimão configurado ou a medição falhar.
        /// </summary>
        public double? Build(RailSegment seg, RailConfig config, XYZ lineStart, XYZ lineEnd)
        {
            if (string.IsNullOrWhiteSpace(config.HandrailFamilyPath)) return null;

            var symbol = GetOrLoadSymbol(config.HandrailFamilyPath, config.HandrailFamilyType);
            if (symbol == null)
                throw new InvalidOperationException("Família de corrimão não encontrada.");

            var catId = symbol.Family.FamilyCategory?.Id.GetId();
            if (catId != (int)BuiltInCategory.OST_StructuralFraming)
                throw new InvalidOperationException(
                    $"Família de corrimão '{symbol.Family.Name}' deve ser de Quadro Estrutural (Viga).\n" +
                    $"Categoria atual: {symbol.Family.FamilyCategory?.Name}");

            if (!symbol.IsActive) symbol.Activate();

            var vec = lineEnd - lineStart;
            if (vec.GetLength() < 0.001) return null;
            var dir     = vec.Normalize();
            var lateral = new XYZ(-dir.Y, dir.X, 0);

            double heightFt  = lineStart.Z + config.HandrailHeight / 304.8;
            double axisOffFt = config.HandrailAxisOffset / 304.8;

            var start = new XYZ(lineStart.X + lateral.X * axisOffFt, lineStart.Y + lateral.Y * axisOffFt, heightFt);
            var end   = new XYZ(lineEnd.X   + lateral.X * axisOffFt, lineEnd.Y   + lateral.Y * axisOffFt, heightFt);

            if (start.DistanceTo(end) < 0.001) return null;

            var level = GetNearestLevel(heightFt);
            var line  = Line.CreateBound(start, end);
            var inst  = _doc.Create.NewFamilyInstance(line, symbol, level, StructuralType.Beam);

            // Estabilidade sob rotação: justificação travada no CENTRO (imune a flip de
            // perfil assimétrico) + posicionamento 100% por vetor global (MoveElement).
            LockCenterJustification(inst);
            SetCrossSectionRotation(inst, config.HandrailRotation);
            _doc.Regenerate();

            // Mede a seção REAL (projetada) já rotacionada: altura (vertical) e largura (lateral).
            double hMm = GeometryMeasure.ExtentAlongMm(_doc, inst.Id, XYZ.BasisZ);
            double wMm = GeometryMeasure.ExtentAlongMm(_doc, inst.Id, lateral);
            double rFt = (hMm / 2.0) / 304.8;

            // Z: com Z=Centro o eixo está em heightFt (topo em heightFt+R). Descemos R para
            //    o TOPO ficar em heightFt (HandrailHeight) e o eixo em heightFt−R.
            // Y: justificativa por movimento lateral ±W/2 (Esquerda/Direita), 0 no Centro.
            double lateralJustFt = config.Justification == HandrailJustification.Left  ? -(wMm / 2.0) / 304.8
                                 : config.Justification == HandrailJustification.Right ? +(wMm / 2.0) / 304.8
                                 :                                                        0.0;
            var move = new XYZ(lateral.X * lateralJustFt, lateral.Y * lateralJustFt, -rFt);
            if (move.GetLength() > 1e-9) ElementTransformUtils.MoveElement(_doc, inst.Id, move);

            double axisZ = heightFt - rFt;   // eixo central após o deslocamento (exato por construção)

            Log($"HandrailBuilder: segmento {seg.Index + 1} | h={hMm:F1}mm w={wMm:F1}mm | " +
                $"z_topo={heightFt * 304.8:F0}mm | z_eixo={(axisZ - lineStart.Z) * 304.8:F0}mm | just={config.Justification}");
            return axisZ;
        }

        /// <summary>Trava a justificação no Centro (Y e Z, YZ Uniforme) — estável sob rotação/flip.</summary>
        private static void LockCenterJustification(FamilyInstance inst)
        {
            if (inst == null) return;

            var yz = inst.get_Parameter(BuiltInParameter.YZ_JUSTIFICATION);
            if (yz != null && !yz.IsReadOnly) yz.Set(0); // 0 = Uniforme

            var y = inst.get_Parameter(BuiltInParameter.Y_JUSTIFICATION);
            if (y != null && !y.IsReadOnly) y.Set((int)YJustification.Center);

            var z = inst.get_Parameter(BuiltInParameter.Z_JUSTIFICATION);
            if (z != null && !z.IsReadOnly) z.Set((int)ZJustification.Center);
        }

        /// <summary>Rotação do corte transversal (graus → radianos) via STRUCTURAL_BEND_DIR_ANGLE.</summary>
        private static void SetCrossSectionRotation(FamilyInstance inst, double degrees)
        {
            if (inst == null || Math.Abs(degrees) < 1e-9) return;
            double rad = degrees * Math.PI / 180.0;
            var p = inst.get_Parameter(BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE)
                    ?? inst.LookupParameter("Rotação do corte transversal");
            if (p != null && !p.IsReadOnly) p.Set(rad);
        }

        private FamilySymbol GetOrLoadSymbol(string path, string typeName)
        {
            var familyName = Path.GetFileNameWithoutExtension(path);
            typeName       = typeName ?? "";

            var existing = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s =>
                    s.Family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase) &&
                    s.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

            if (existing != null) return existing;

            if (!_doc.LoadFamilySymbol(path, typeName, out var loaded) || loaded == null)
                throw new InvalidOperationException($"Não foi possível carregar '{familyName}' tipo '{typeName}'.");

            return loaded;
        }

        private Level GetNearestLevel(double zFt)
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => Math.Abs(l.ProjectElevation - zFt))
                .FirstOrDefault()
                ?? throw new InvalidOperationException("Nenhum Level encontrado no documento.");
        }

        private static void Log(string msg)
        {
            try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); }
            catch { }
        }
    }
}
