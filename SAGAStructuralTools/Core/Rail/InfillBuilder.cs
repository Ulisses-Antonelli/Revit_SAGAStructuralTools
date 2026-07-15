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
        private ICollection<ElementId> _createdIds;

        private static readonly string LogPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "SAGA_RailLog.txt");

        public InfillBuilder(Document doc) => _doc = doc;

        public void Build(RailSegment seg, RailConfig config, RailRunGeometry run,
                          RailRunGeometry railAxisRun = null, ICollection<ElementId> createdIds = null)
        {
            _createdIds = createdIds;
            try
            {
                if (config.InfillMode == InfillMode.HorizontalBars)
                    BuildHorizontalBars(seg, config, run, railAxisRun);
                else
                {
                    if (run.IsInclined)
                        throw new InvalidOperationException(
                            "O fechamento em quadros ainda não é compatível com guarda-corpo inclinado. " +
                            "Selecione o fechamento com barras horizontais para este trecho.");

                    BuildFramePanel(seg, config, run.Start, run.Direction, run.Lateral);
                }
            }
            finally
            {
                _createdIds = null;
            }
        }

        // ── Barras horizontais + rodapé ──────────────────────────────────
        private void BuildHorizontalBars(
            RailSegment seg,
            RailConfig config,
            RailRunGeometry run,
            RailRunGeometry railAxisRun)
        {
            // Distribuição equidistante vai da base até o EIXO do corrimão (não o topo).
            // A distância é vertical global em qualquer ponto do percurso inclinado.
            double railVerticalOffsetFt = railAxisRun != null
                ? railAxisRun.Start.Z - run.Start.Z
                : config.HandrailHeight / 304.8;
            double lateralOffFt = config.PostAxisOffset / 304.8;   // mesmo eixo dos montantes

            // As travessas nascem do eixo do 1º e do último montante — usa AxisOffsets
            // (posições reais após o recuo de W/2), com fallback para PostOffsets/linha.
            var offsets  = (seg.AxisOffsets != null && seg.AxisOffsets.Count > 0) ? seg.AxisOffsets : seg.PostOffsets;
            bool hasPosts = offsets != null && offsets.Count > 0;
            double startMm = hasPosts ? offsets[0] : 0.0;
            double endMm = hasPosts ? offsets[offsets.Count - 1] : run.LengthMm;

            XYZ Pt(double alongMm, double verticalOffsetFt, double extraLateralFt = 0) =>
                run.PointAtDistanceMm(
                    alongMm,
                    verticalOffsetFt,
                    lateralOffFt + extraLateralFt);

            // Rodapé (toe board) — barra próxima à base
            if (!string.IsNullOrWhiteSpace(config.Rodape?.FamilyPath))
            {
                var rodapeSym = GetSymbol(config.Rodape.FamilyPath, config.Rodape.FamilyType);
                if (rodapeSym != null)
                {
                    double verticalOffsetFt = config.Rodape.Distance / 304.8;
                    double extraLateralFt = config.Rodape.LateralOffset / 304.8;
                    CreateBeam(
                        rodapeSym,
                        Pt(startMm, verticalOffsetFt, extraLateralFt),
                        Pt(endMm, verticalOffsetFt, extraLateralFt),
                        "rodapé");
                    Log($"  [rodapé] offset horizontal adicional={config.Rodape.LateralOffset:F1}mm | " +
                        $"offset vertical={config.Rodape.Distance:F1}mm");
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

                double verticalOffsetFt = config.EquidistantBars
                    // distribui uniformemente entre a base e o EIXO do corrimão
                    ? railVerticalOffsetFt * (i + 1) / (bars.Count + 1)
                    : (barCfg?.Distance ?? 0) / 304.8;

                CreateBeam(
                    sym,
                    Pt(startMm, verticalOffsetFt),
                    Pt(endMm, verticalOffsetFt),
                    $"travessa {i + 1}");
            }
        }

        // ── Quadro (frame) entre montantes ───────────────────────────────
        private void BuildFramePanel(RailSegment seg, RailConfig config, XYZ lineStart, XYZ dir, XYZ lateral)
        {
            var offsets = (seg.AxisOffsets != null && seg.AxisOffsets.Count > 0) ? seg.AxisOffsets : seg.PostOffsets;
            if (offsets == null || offsets.Count < 2)
            {
                Log("FramePanel: menos de 2 montantes — nada a fechar.");
                return;
            }

            // Perfil HORIZONTAL (topo/base) — obrigatório.
            if (string.IsNullOrWhiteSpace(config.FrameFamilyPath)) return;
            var horizSym = GetSymbol(config.FrameFamilyPath, config.FrameFamilyType);
            if (horizSym == null) return;

            // Perfil VERTICAL (laterais) — só na cantoneira fechada; pode ser família de
            // PILAR ou de VIGA (diferente do horizontal).
            FamilySymbol vertSym = null;
            bool vertIsColumn = false;
            if (config.FrameType == FrameType.AngleIron && !string.IsNullOrWhiteSpace(config.FrameVertFamilyPath))
                vertSym = GetSymbolVertical(config.FrameVertFamilyPath, config.FrameVertFamilyType, out vertIsColumn);

            double lateralOffFt = config.FrameOffset / 304.8;                     // + fora / − dentro
            double bottomZ      = lineStart.Z + config.FrameBaseOffset / 304.8;   // desloc. da base
            double topZ         = bottomZ + config.FrameHeight / 304.8;           // altura do quadro

            XYZ At(double offMm, double z) => new XYZ(
                lineStart.X + dir.X * (offMm / 304.8) + lateral.X * lateralOffFt,
                lineStart.Y + dir.Y * (offMm / 304.8) + lateral.Y * lateralOffFt,
                z);

            // Distância do eixo do montante até a cantoneira: input do usuário ou, se 0,
            // meia-largura MEDIDA do montante (face física — tubo, chato ou quadrado).
            double montHalfMm   = config.FrameFaceOffset > 1e-6
                                ? config.FrameFaceOffset
                                : (seg.PostWidthMm > 1e-6 ? seg.PostWidthMm : 0) / 2.0;
            double lineAngleDeg = Math.Atan2(dir.Y, dir.X) * 180.0 / Math.PI;   // ângulo da linha (p/ colunas)
            double baseRot      = config.FrameRotation;                          // calibração da cantoneira de referência

            // ── Horizontais (topo/base) — entre as FACES dos montantes. A base é o
            //    ESPELHO do topo (plano horizontal pelo próprio eixo). Espelho ≠ girar
            //    180°: girar produz o "S"; espelhar mantém a aba de encosto do mesmo
            //    lado e vira a aba livre para o interior do quadro. ─────────────────
            for (int k = 0; k < offsets.Count - 1; k++)
            {
                double aMm = offsets[k]     + montHalfMm;   // face do montante esquerdo
                double bMm = offsets[k + 1] - montHalfMm;   // face do montante direito
                CreateFrameBeam(horizSym, At(aMm, topZ), At(bMm, topZ), baseRot, $"quadro {k + 1} topo");
                var baseBar = CreateFrameBeam(horizSym, At(aMm, bottomZ), At(bMm, bottomZ), baseRot, $"quadro {k + 1} base");
                MirrorInPlace(baseBar, XYZ.BasisZ, At(aMm, bottomZ), $"quadro {k + 1} base");
            }

            // ── Verticais (cantoneiras face a face) — regra dos vãos ─────────────
            //    Inicial: 1 (abre p/ +dir) | Intermediários: 2 costas-com-costas |
            //    Final: 1 (abre p/ −dir). O lado −dir é o ESPELHO exato do lado +dir
            //    (plano ⊥ linha pelo próprio eixo): aba de encosto tangente à face do
            //    montante, aba livre para o interior do vão — sem "S".
            if (vertSym != null)
            {
                FamilyInstance PlaceVert(double alongMm, string tag)
                {
                    var basePt = At(alongMm, bottomZ);
                    return vertIsColumn
                        ? CreateFrameColumn(vertSym, basePt, topZ, baseRot + lineAngleDeg, tag)
                        : CreateFrameBeam(vertSym, basePt, new XYZ(basePt.X, basePt.Y, topZ), baseRot, tag);
                }
                void PlaceVertMirrored(double alongMm, string tag)
                {
                    var inst = PlaceVert(alongMm, tag);
                    MirrorInPlace(inst, dir, At(alongMm, bottomZ), tag);
                }

                int n = offsets.Count;
                PlaceVert(offsets[0] + montHalfMm, "vert ini");                       // abre p/ +dir
                for (int i = 1; i < n - 1; i++)
                {
                    PlaceVertMirrored(offsets[i] - montHalfMm, $"vert {i} esq");      // espelho: abre p/ −dir
                    PlaceVert(offsets[i] + montHalfMm, $"vert {i} dir");              // abre p/ +dir
                }
                PlaceVertMirrored(offsets[n - 1] - montHalfMm, "vert fim");           // espelho: abre p/ −dir
            }
        }

        /// <summary>
        /// Espelha a instância no próprio lugar: o plano de espelho contém o eixo do
        /// elemento, então a posição não muda — só a geometria vira (flip exato, válido
        /// para qualquer família, com ou sem abas iguais).
        /// </summary>
        private void MirrorInPlace(FamilyInstance inst, XYZ normal, XYZ origin, string tag)
        {
            if (inst == null) return;
            try
            {
                var plane = Plane.CreateByNormalAndOrigin(normal, origin);
                ElementTransformUtils.MirrorElements(_doc, new List<ElementId> { inst.Id }, plane, false);
                Log($"  [{tag}] espelhada OK");
            }
            catch (Exception ex)
            {
                Log($"  [{tag}] espelhamento falhou: {ex.Message}");
            }
        }

        /// <summary>Cria uma barra do quadro como VIGA: centro no eixo, rotação do corte, sem cutback.</summary>
        private FamilyInstance CreateFrameBeam(FamilySymbol sym, XYZ a, XYZ b, double rotDeg, string tag)
        {
            if (a.DistanceTo(b) < 0.001) { Log($"  [{tag}] segmento degenerado ignorado"); return null; }
            var level = GetNearestLevel((a.Z + b.Z) / 2.0);
            var inst  = _doc.Create.NewFamilyInstance(Line.CreateBound(a, b), sym, level, StructuralType.Beam);
            Track(inst);
            CenterJustify(inst);   // eixo no centro da seção → o espelho preserva a posição
            SetCrossSectionRotation(inst, rotDeg);
            try { StructuralFramingUtils.DisallowJoinAtEnd(inst, 0); StructuralFramingUtils.DisallowJoinAtEnd(inst, 1); }
            catch { /* nem toda família suporta join */ }
            Log($"  [{tag}] {Fmt(a)} → {Fmt(b)} rot={rotDeg:F0}°");
            return inst;
        }

        /// <summary>Cria uma vertical do quadro como COLUNA (pilar), girada em torno da vertical.</summary>
        private FamilyInstance CreateFrameColumn(FamilySymbol sym, XYZ basePt, double topZFt, double rotDegWorld, string tag)
        {
            var level = GetNearestLevel((basePt.Z + topZFt) / 2.0);
            var levelPt = new XYZ(basePt.X, basePt.Y, level.ProjectElevation);
            var inst  = _doc.Create.NewFamilyInstance(levelPt, sym, level, StructuralType.Column);
            Track(inst);
            SetColumnExtents(inst, level, basePt.Z, topZFt);
            if (Math.Abs(rotDegWorld) > 1e-9)
            {
                var axis = Line.CreateBound(basePt, basePt + XYZ.BasisZ);
                ElementTransformUtils.RotateElement(_doc, inst.Id, axis, rotDegWorld * Math.PI / 180.0);
            }
            Log($"  [{tag}] coluna {Fmt(basePt)} rot={rotDegWorld:F0}°");
            return inst;
        }

        /// <summary>Rotação do corte transversal (graus → rad) via STRUCTURAL_BEND_DIR_ANGLE.</summary>
        private static void SetCrossSectionRotation(FamilyInstance inst, double degrees)
        {
            if (inst == null || Math.Abs(degrees) < 1e-9) return;
            var p = inst.get_Parameter(BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE)
                    ?? inst.LookupParameter("Rotação do corte transversal");
            if (p != null && !p.IsReadOnly) p.Set(degrees * Math.PI / 180.0);
        }

        /// <summary>Ancora topo e base da coluna no mesmo nível (topo não sobe para o nível acima).</summary>
        private static void SetColumnExtents(FamilyInstance inst, Level level, double baseZFt, double topZFt)
        {
            var topLevel = inst.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
            if (topLevel != null && !topLevel.IsReadOnly) topLevel.Set(level.Id);

            var baseOff = inst.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
            if (baseOff != null && !baseOff.IsReadOnly) baseOff.Set(baseZFt - level.ProjectElevation);

            var topOff = inst.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
            if (topOff != null && !topOff.IsReadOnly) topOff.Set(topZFt - level.ProjectElevation);
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
            Track(inst);
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

        private FamilyInstance Track(FamilyInstance inst)
        {
            if (inst != null) _createdIds?.Add(inst.Id);
            return inst;
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

        /// <summary>
        /// Símbolo do perfil VERTICAL do quadro — aceita Quadro Estrutural (viga) OU
        /// Pilar Estrutural (coluna). Retorna se é coluna para a criação correta.
        /// </summary>
        private FamilySymbol GetSymbolVertical(string path, string typeName, out bool isColumn)
        {
            var familyName = Path.GetFileNameWithoutExtension(path);
            typeName = typeName ?? "";
            string key = "V|" + familyName + "|" + typeName;

            if (_symbolCache.TryGetValue(key, out var cached))
            {
                isColumn = cached.Family.FamilyCategory?.Id.GetId() == (int)BuiltInCategory.OST_StructuralColumns;
                return cached;
            }

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

            var catId  = symbol.Family.FamilyCategory?.Id.GetId();
            bool framing = catId == (int)BuiltInCategory.OST_StructuralFraming;
            isColumn     = catId == (int)BuiltInCategory.OST_StructuralColumns;
            if (!framing && !isColumn)
                throw new InvalidOperationException(
                    $"Perfil vertical '{symbol.Family.Name}' (categoria '{symbol.Family.FamilyCategory?.Name}') não suportado.\n" +
                    "Use uma família de Quadro Estrutural (viga) ou Pilar Estrutural.");

            if (!symbol.IsActive) symbol.Activate();
            _symbolCache[key] = symbol;
            return symbol;
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

        private static string Fmt(XYZ p) =>
            p == null ? "null" : $"({p.X * 304.8:F0}, {p.Y * 304.8:F0}, {p.Z * 304.8:F0})mm";
    }
}
