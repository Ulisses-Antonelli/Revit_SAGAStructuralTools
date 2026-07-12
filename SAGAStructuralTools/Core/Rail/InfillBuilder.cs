using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SAGAStructuralTools.Core;
using SAGAStructuralTools.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SAGAStructuralTools.Core.Rail
{
    /// <summary>
    /// Cria o fechamento do guarda-corpo entre montantes e corrimão:
    ///   • InfillMode.HorizontalBars → travessas horizontais + rodapé
    ///   • InfillMode.FramePanel     → quadro (cantoneira fechada ou apenas horizontais)
    ///
    /// Todas as barras são vigas (OST_StructuralFraming) criadas com Line.CreateBound,
    /// mesmo padrão do HandrailBuilder. O símbolo é validado (categoria Quadro Estrutural)
    /// e cacheado por família+tipo para evitar recarregar a cada barra.
    ///
    /// Nota: o alinhamento por face (BarAlignment / FrameAlignment) ainda é tratado como
    /// eixo — o offset lateral é aplicado a partir do eixo da linha selecionada. O offset
    /// de face exige leitura de largura de perfil (ProfileGeometryReader), a refinar em v0.3.
    /// </summary>
    public class InfillBuilder
    {
        private readonly Document _doc;
        private readonly Dictionary<string, FamilySymbol> _symbolCache =
            new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);

        private static readonly string LogPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "SAGA_RailLog.txt");

        public InfillBuilder(Document doc) => _doc = doc;

        public void Build(RailSegment seg, RailConfig config, XYZ lineStart, XYZ lineEnd, double? railAxisZ = null)
        {
            var vec = lineEnd - lineStart;
            if (vec.GetLength() < 0.001) return;
            var dir     = vec.Normalize();
            var lateral = new XYZ(-dir.Y, dir.X, 0);

            if (config.InfillMode == InfillMode.HorizontalBars)
                BuildHorizontalBars(seg, config, lineStart, dir, lateral, railAxisZ);
            else
                BuildFramePanel(seg, config, lineStart, dir, lateral);
        }

        // ── Barras horizontais + rodapé ──────────────────────────────────
        private void BuildHorizontalBars(RailSegment seg, RailConfig config, XYZ lineStart, XYZ dir, XYZ lateral, double? railAxisZ)
        {
            double baseZ        = lineStart.Z;
            // Distribuição equidistante vai da base até o EIXO do corrimão (não o topo).
            // Preferimos o eixo medido pela BoundingBox (railAxisZ); fallback por parâmetro.
            double railAxisZFt  = railAxisZ ??
                                  (lineStart.Z + config.HandrailHeight / 304.8 - HandrailHalfHeightMm(config) / 304.8);
            double lateralOffFt = config.PostAxisOffset / 304.8;   // mesmo eixo dos montantes

            // As travessas nascem do eixo do 1º e do último montante — usa AxisOffsets
            // (posições reais após o recuo de W/2), com fallback para PostOffsets/linha.
            var offsets  = (seg.AxisOffsets != null && seg.AxisOffsets.Count > 0) ? seg.AxisOffsets : seg.PostOffsets;
            bool hasPosts = offsets != null && offsets.Count > 0;
            double startFt = (hasPosts ? offsets[0]                    : 0.0)        / 304.8;
            double endFt   = (hasPosts ? offsets[offsets.Count - 1]    : seg.Length) / 304.8;

            XYZ Pt(double alongFt, double z) => new XYZ(
                lineStart.X + dir.X * alongFt + lateral.X * lateralOffFt,
                lineStart.Y + dir.Y * alongFt + lateral.Y * lateralOffFt,
                z);

            // Rodapé (toe board) — barra próxima à base
            if (!string.IsNullOrWhiteSpace(config.Rodape?.FamilyPath))
            {
                var rodapeSym = GetSymbol(config.Rodape.FamilyPath, config.Rodape.FamilyType);
                if (rodapeSym != null)
                {
                    double z = baseZ + config.Rodape.Distance / 304.8;
                    CreateBeam(rodapeSym, Pt(startFt, z), Pt(endFt, z), "rodapé");
                }
            }

            var bars = config.HorizontalBars ?? new List<BarConfig>();
            if (bars.Count == 0) return;

            for (int i = 0; i < bars.Count; i++)
            {
                var barCfg = bars[i];

                // Perfil: comum (SameProfileAll) ou individual da linha. Posição (Distance)
                // é sempre por linha.
                string famPath = config.SameProfileAll ? config.HorizontalBarCommon?.FamilyPath : barCfg?.FamilyPath;
                string famType = config.SameProfileAll ? config.HorizontalBarCommon?.FamilyType : barCfg?.FamilyType;
                if (string.IsNullOrWhiteSpace(famPath)) continue;

                var sym = GetSymbol(famPath, famType);
                if (sym == null) continue;

                double z = config.EquidistantBars
                    // distribui uniformemente entre a base e o EIXO do corrimão
                    ? baseZ + (railAxisZFt - baseZ) * (i + 1) / (bars.Count + 1)
                    : baseZ + (barCfg?.Distance ?? 0) / 304.8;

                CreateBeam(sym, Pt(startFt, z), Pt(endFt, z), $"travessa {i + 1}");
            }
        }

        // ── Quadro (frame) entre montantes ───────────────────────────────
        private void BuildFramePanel(RailSegment seg, RailConfig config, XYZ lineStart, XYZ dir, XYZ lateral)
        {
            if (string.IsNullOrWhiteSpace(config.FrameFamilyPath)) return;
            var sym = GetSymbol(config.FrameFamilyPath, config.FrameFamilyType);
            if (sym == null) return;

            var offsets = (seg.AxisOffsets != null && seg.AxisOffsets.Count > 0) ? seg.AxisOffsets : seg.PostOffsets;
            if (offsets == null || offsets.Count < 2)
            {
                Log("FramePanel: menos de 2 montantes — nada a fechar.");
                return;
            }

            double lateralOffFt = config.FrameOffset / 304.8;
            double bottomZ      = lineStart.Z;
            double topZ         = lineStart.Z + config.FrameHeight / 304.8;

            XYZ At(double offMm, double z) => new XYZ(
                lineStart.X + dir.X * (offMm / 304.8) + lateral.X * lateralOffFt,
                lineStart.Y + dir.Y * (offMm / 304.8) + lateral.Y * lateralOffFt,
                z);

            // Topo e base de cada quadro (entre montantes consecutivos)
            for (int k = 0; k < offsets.Count - 1; k++)
            {
                CreateBeam(sym, At(offsets[k], topZ),    At(offsets[k + 1], topZ),    $"quadro {k + 1} topo");
                CreateBeam(sym, At(offsets[k], bottomZ), At(offsets[k + 1], bottomZ), $"quadro {k + 1} base");
            }

            // Verticais apenas na cantoneira fechada — uma por montante (evita duplicar
            // arestas compartilhadas entre quadros adjacentes). No modo "Apenas Horizontal"
            // os próprios montantes fazem a lateral do quadro.
            if (config.FrameType == FrameType.AngleIron)
            {
                for (int k = 0; k < offsets.Count; k++)
                    CreateBeam(sym, At(offsets[k], bottomZ), At(offsets[k], topZ), $"vertical {k + 1}");
            }
        }

        // ── Helpers de criação ───────────────────────────────────────────

        private void CreateBeam(FamilySymbol sym, XYZ a, XYZ b, string tag)
        {
            if (a.DistanceTo(b) < 0.001)
            {
                Log($"  [{tag}] segmento degenerado ignorado");
                return;
            }
            var level = GetNearestLevel((a.Z + b.Z) / 2.0);
            var line  = Line.CreateBound(a, b);
            var inst  = _doc.Create.NewFamilyInstance(line, sym, level, StructuralType.Beam);
            CenterJustify(inst);

            // Anula o cutback automático de junta: o Revit preenche "Recuo da junta" com o
            // raio do perfil e encurta a viga até a FACE do montante. Desabilitando a junta
            // nas duas pontas, a geometria vai até os endpoints reais (eixos dos montantes).
            try
            {
                StructuralFramingUtils.DisallowJoinAtEnd(inst, 0);
                StructuralFramingUtils.DisallowJoinAtEnd(inst, 1);
            }
            catch { /* nem toda família/categoria suporta join; ignorar */ }

            Log($"  [{tag}] {Fmt(a)} → {Fmt(b)}");
        }

        /// <summary>
        /// Justificação Y = Centro, Z = Centro: o perfil fica centrado na linha de eixo,
        /// tanto lateral quanto verticalmente (as barras nascem no eixo, não no topo).
        /// </summary>
        private static void CenterJustify(FamilyInstance inst)
        {
            if (inst == null) return;

            var yz = inst.get_Parameter(BuiltInParameter.YZ_JUSTIFICATION);
            if (yz != null && !yz.IsReadOnly) yz.Set(0); // Uniforme

            var y = inst.get_Parameter(BuiltInParameter.Y_JUSTIFICATION);
            if (y != null && !y.IsReadOnly) y.Set((int)YJustification.Center);

            var z = inst.get_Parameter(BuiltInParameter.Z_JUSTIFICATION);
            if (z != null && !z.IsReadOnly) z.Set((int)ZJustification.Center);
        }

        /// <summary>Meia-altura (mm) da seção do corrimão. 0 se não configurado/indisponível.</summary>
        private double HandrailHalfHeightMm(RailConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.HandrailFamilyPath)) return 0;
            try { return SectionSize.SectionHeightMm(GetSymbol(config.HandrailFamilyPath, config.HandrailFamilyType)) / 2.0; }
            catch { return 0; }
        }

        private FamilySymbol GetSymbol(string path, string typeName)
        {
            var familyName = Path.GetFileNameWithoutExtension(path);
            typeName = typeName ?? "";
            string key = familyName + "|" + typeName;
            if (_symbolCache.TryGetValue(key, out var cached)) return cached;

            var symbol = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s =>
                    s.Family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase) &&
                    s.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

            if (symbol == null)
            {
                if (!_doc.LoadFamilySymbol(path, typeName, out symbol) || symbol == null)
                    throw new InvalidOperationException(
                        $"Não foi possível carregar '{familyName}' tipo '{typeName}'.");
            }

            var catId = symbol.Family.FamilyCategory?.Id.GetId();
            if (catId != (int)BuiltInCategory.OST_StructuralFraming)
                throw new InvalidOperationException(
                    $"Família de fechamento '{symbol.Family.Name}' deve ser de Quadro Estrutural (Viga).\n" +
                    $"Categoria atual: {symbol.Family.FamilyCategory?.Name}");

            if (!symbol.IsActive) symbol.Activate();
            _symbolCache[key] = symbol;
            return symbol;
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

        private static string Fmt(XYZ p) =>
            p == null ? "null" : $"({p.X * 304.8:F0}, {p.Y * 304.8:F0}, {p.Z * 304.8:F0})mm";
    }
}
