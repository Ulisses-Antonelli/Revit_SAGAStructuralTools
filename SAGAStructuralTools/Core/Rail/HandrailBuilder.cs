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

        public void Build(RailSegment seg, RailConfig config, XYZ lineStart, XYZ lineEnd)
        {
            if (string.IsNullOrWhiteSpace(config.HandrailFamilyPath)) return;

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
            if (vec.GetLength() < 0.001) return;
            var dir     = vec.Normalize();
            var lateral = new XYZ(-dir.Y, dir.X, 0);

            double heightFt  = lineStart.Z + config.HandrailHeight / 304.8;
            double axisOffFt = config.HandrailAxisOffset / 304.8;

            var start = new XYZ(lineStart.X + lateral.X * axisOffFt, lineStart.Y + lateral.Y * axisOffFt, heightFt);
            var end   = new XYZ(lineEnd.X   + lateral.X * axisOffFt, lineEnd.Y   + lateral.Y * axisOffFt, heightFt);

            if (start.DistanceTo(end) < 0.001) return;

            var level = GetNearestLevel(heightFt);
            var line  = Line.CreateBound(start, end);
            _doc.Create.NewFamilyInstance(line, symbol, level, StructuralType.Beam);

            Log($"HandrailBuilder: segmento {seg.Index + 1} | z={heightFt * 304.8:F0}mm | axisOff={axisOffFt * 304.8:F1}mm");
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
