using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SAGAStructuralTools.Core.Models;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SAGAStructuralTools.Core.Stair
{
    /// <summary>
    /// Cria o esqueleto estrutural da escada:
    ///   [Viga inf] ←patamar inf→ [início inclinado] ←longarinas→ [fim inclinado] ←patamar sup→ [Viga sup]
    ///
    /// IMPORTANTE: a família de longarina DEVE ser de Quadro Estrutural (Viga),
    /// não de Pilar/Coluna. NewFamilyInstance com StructuralType.Beam e símbolo
    /// de coluna causa crash fatal no Revit.
    /// </summary>
    public class StringerBuilder
    {
        private readonly Document _doc;
        private static readonly string LogPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "SAGA_StairLog.txt");

        public StringerBuilder(Document doc) => _doc = doc;

        public void Build(StairDefinition def, StairConfig config, XYZ startPt, XYZ endPt)
        {
            Log("=== Build iniciado ===");
            Log($"startPt={Fmt(startPt)}  endPt={Fmt(endPt)}");
            Log($"LLD={def.LowerLandingDepth:F0}mm  ULD={def.UpperLandingDepth:F0}mm  TotalRun={def.TotalRun:F0}mm");

            if (startPt == null || endPt == null)
                throw new InvalidOperationException("Pontos de conexão nulos — selecione as vigas novamente.");

            if (startPt.DistanceTo(endPt) < 0.01)
                throw new InvalidOperationException("Os pontos de conexão estão muito próximos.");

            var symbol = GetOrLoadSymbol(config);
            if (symbol == null)
                throw new InvalidOperationException(
                    "Família de longarina não encontrada.\n" +
                    "Selecione o perfil e confirme o tipo no painel.");

            // ── VALIDAÇÃO CRÍTICA ─────────────────────────────────────────────
            // Longarinas são vigas inclinadas → família DEVE ser OST_StructuralFraming.
            // Usar símbolo de OST_StructuralColumns com StructuralType.Beam causa
            // crash fatal no Revit (erro irrecuperável, sem exceção gerenciável).
            var famCatId = symbol.Family.FamilyCategory?.Id.IntegerValue;
            Log($"Família: '{symbol.Family.Name}' | Tipo: '{symbol.Name}' | Categoria ID: {famCatId}");

            if (famCatId != (int)BuiltInCategory.OST_StructuralFraming)
            {
                throw new InvalidOperationException(
                    $"A família '{symbol.Family.Name}' é de Pilar/Coluna (categoria {symbol.Family.FamilyCategory?.Name}), " +
                    "não de Quadro Estrutural.\n\n" +
                    "Para longarinas de escada, selecione uma família de VIGA " +
                    "(ex: 'Viga W Gerdau', 'Viga_PerfilSimples-U_ArcelorMittal').");
            }

            if (!symbol.IsActive) symbol.Activate();

            // ── Direção e lateral ─────────────────────────────────────────────
            var horizVec = new XYZ(endPt.X - startPt.X, endPt.Y - startPt.Y, 0);
            if (horizVec.GetLength() < 0.001)
                throw new InvalidOperationException(
                    "As vigas estão diretamente acima uma da outra — direção horizontal indeterminada.");

            var horizDir = horizVec.Normalize();
            var lateral  = new XYZ(-horizDir.Y, horizDir.X, 0);
            var halfFt   = (config.Width / 2.0) / 304.8;

            // ── Patamares em pés ──────────────────────────────────────────────
            var lldFt = def.LowerLandingDepth / 304.8;
            var uldFt = def.UpperLandingDepth / 304.8;

            var stringerBottom = new XYZ(
                startPt.X + horizDir.X * lldFt,
                startPt.Y + horizDir.Y * lldFt,
                startPt.Z);

            // stringerTop calculado a partir do stringerBottom + totalRun
            // (NOT de endPt - uldFt, para garantir que a longarina seja reta)
            var totalRunFt = def.TotalRun / 304.8;
            var stringerTop = new XYZ(
                stringerBottom.X + horizDir.X * totalRunFt,
                stringerBottom.Y + horizDir.Y * totalRunFt,
                endPt.Z);

            var lowerLevel = GetNearestLevel(startPt.Z);
            var upperLevel = GetNearestLevel(endPt.Z);

            Log($"stringerBottom={Fmt(stringerBottom)}  stringerTop={Fmt(stringerTop)}");

            // ── Pares esquerdo / direito ───────────────────────────────────────
            foreach (var sign in new[] { -1.0, 1.0 })
            {
                var offset = lateral * (halfFt * sign);

                if (lldFt > 0.001)
                    CreateBeam(startPt + offset, stringerBottom + offset, symbol, lowerLevel, "patamar-inf");

                CreateBeam(stringerBottom + offset, stringerTop + offset, symbol, lowerLevel, "longarina");

                if (uldFt > 0.001)
                    CreateBeam(stringerTop + offset, endPt + offset, symbol, upperLevel, "patamar-sup");
            }

            Log("=== Build concluído ===");
        }

        private void CreateBeam(XYZ start, XYZ end, FamilySymbol symbol, Level level, string tag)
        {
            if (start.DistanceTo(end) < 0.001)
            {
                Log($"  [{tag}] segmento degenerado ignorado");
                return;
            }
            Log($"  [{tag}] {Fmt(start)} → {Fmt(end)}");
            var line = Line.CreateBound(start, end);
            _doc.Create.NewFamilyInstance(line, symbol, level, StructuralType.Beam);
        }

        private FamilySymbol GetOrLoadSymbol(StairConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.StringerFamilyPath)) return null;

            var typeName   = config.StringerFamilyType ?? "";
            var familyName = Path.GetFileNameWithoutExtension(config.StringerFamilyPath);

            // Busca sem filtro de categoria — o símbolo pode estar em qualquer categoria
            var existing = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s =>
                    s.Family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase) &&
                    s.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

            if (existing != null) return existing;

            if (!_doc.LoadFamilySymbol(config.StringerFamilyPath, typeName, out var loaded) || loaded == null)
                throw new InvalidOperationException(
                    $"Não foi possível carregar '{familyName}' tipo '{typeName}'.\n" +
                    "Verifique se o arquivo .rfa e o tipo estão corretos.");

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
            catch { /* log não-crítico */ }
        }

        private static string Fmt(XYZ p) =>
            p == null ? "null" : $"({p.X * 304.8:F0}, {p.Y * 304.8:F0}, {p.Z * 304.8:F0})mm";
    }
}
