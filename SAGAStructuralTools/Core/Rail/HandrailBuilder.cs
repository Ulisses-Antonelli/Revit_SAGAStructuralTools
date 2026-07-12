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

            SetJustification(inst, config.Justification);
            SetCrossSectionRotation(inst, config.HandrailRotation);

            // Mede o eixo central real (Z médio da BoundingBox) após justificar/regenerar.
            _doc.Regenerate();
            double? axisZ = null;
            var bb = inst.get_BoundingBox(null);
            if (bb != null) axisZ = (bb.Min.Z + bb.Max.Z) / 2.0;

            Log($"HandrailBuilder: segmento {seg.Index + 1} | z_topo={heightFt * 304.8:F0}mm | " +
                $"z_eixo={(axisZ.HasValue ? (axisZ.Value - lineStart.Z) * 304.8 : 0):F0}mm | axisOff={axisOffFt * 304.8:F1}mm");
            return axisZ;
        }

        /// <summary>
        /// Justificação do corrimão: Y = escolha do usuário (Esquerda/Centro/Direita),
        /// Z = Topo (perfil pendura abaixo do eixo). YZ = Uniforme para valer nas duas pontas.
        /// </summary>
        private static void SetJustification(FamilyInstance inst, HandrailJustification just)
        {
            if (inst == null) return;

            var yz = inst.get_Parameter(BuiltInParameter.YZ_JUSTIFICATION);
            if (yz != null && !yz.IsReadOnly) yz.Set(0); // 0 = Uniforme

            var yVal = just == HandrailJustification.Left  ? YJustification.Left
                     : just == HandrailJustification.Right ? YJustification.Right
                     :                                       YJustification.Center;
            var y = inst.get_Parameter(BuiltInParameter.Y_JUSTIFICATION);
            if (y != null && !y.IsReadOnly) y.Set((int)yVal);

            var z = inst.get_Parameter(BuiltInParameter.Z_JUSTIFICATION);
            if (z != null && !z.IsReadOnly) z.Set((int)ZJustification.Top);
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
                .OrderBy(l => Math.Abs(l.Elevation - zFt))
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
