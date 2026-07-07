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
    public class PostBuilder
    {
        private readonly Document _doc;
        private static readonly string LogPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "SAGA_RailLog.txt");

        public PostBuilder(Document doc) => _doc = doc;

        public void Build(RailSegment seg, RailConfig config, XYZ lineStart, XYZ lineEnd)
        {
            if (string.IsNullOrWhiteSpace(config.PostFamilyPath)) return;

            var symbol = GetOrLoadSymbol(config.PostFamilyPath, config.PostFamilyType);
            if (symbol == null)
                throw new InvalidOperationException("Família de montante não encontrada. Verifique o caminho e o tipo.");

            var catId     = symbol.Family.FamilyCategory?.Id.GetId();
            bool isFraming = catId == (int)BuiltInCategory.OST_StructuralFraming;
            bool isColumn  = catId == (int)BuiltInCategory.OST_StructuralColumns;

            if (!isFraming && !isColumn)
                throw new InvalidOperationException(
                    $"Família '{symbol.Family.Name}' (categoria '{symbol.Family.FamilyCategory?.Name}') não é suportada para montantes.\n" +
                    "Use uma família de Quadro Estrutural (viga) ou Pilar Estrutural.");

            if (!symbol.IsActive) symbol.Activate();

            var vec = lineEnd - lineStart;
            if (vec.GetLength() < 0.001) return;
            var dir     = vec.Normalize();
            var lateral = new XYZ(-dir.Y, dir.X, 0);

            double axisOffFt = config.PostAxisOffset / 304.8;
            double baseZ     = lineStart.Z - config.PostBaseOffset / 304.8;
            double topZ      = lineStart.Z + config.HandrailHeight / 304.8 + config.PostTopOffset / 304.8;
            var level        = GetNearestLevel(lineStart.Z);

            Log($"PostBuilder: {seg.PostCount} montantes | família={Path.GetFileNameWithoutExtension(config.PostFamilyPath)}");

            foreach (double offsetMm in seg.PostOffsets)
            {
                double offsetFt = offsetMm / 304.8;
                var    center   = lineStart + dir * offsetFt + lateral * axisOffFt;
                var    basePt   = new XYZ(center.X, center.Y, baseZ);

                if (isFraming)
                {
                    var topPt = new XYZ(center.X, center.Y, topZ);
                    var line  = Line.CreateBound(basePt, topPt);
                    _doc.Create.NewFamilyInstance(line, symbol, level, StructuralType.Beam);
                }
                else
                {
                    var inst = _doc.Create.NewFamilyInstance(basePt, symbol, level, StructuralType.Column);
                    SetColumnTopOffset(inst, topZ, level);
                }
            }
        }

        private void SetColumnTopOffset(FamilyInstance inst, double topZFt, Level baseLevel)
        {
            var topOffParam = inst.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
            if (topOffParam != null && !topOffParam.IsReadOnly)
                topOffParam.Set(topZFt - baseLevel.Elevation);
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
