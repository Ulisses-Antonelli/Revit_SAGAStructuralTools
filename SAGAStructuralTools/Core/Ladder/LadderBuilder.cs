using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SAGAStructuralTools.Core.Models;
using SAGAStructuralTools.Core.Rail;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SAGAStructuralTools.Core.Ladder
{
    /// <summary>
    /// Cria a geometria da escada marinheiro: montantes, degraus, suportes de fixação,
    /// prolongamento com alargamento na saída, gaiola (anéis em arco + tiras verticais)
    /// e linha de vida.
    ///
    /// Todos os perfis são Quadro Estrutural (viga) criados por Line/Arc.CreateBound —
    /// o prolongamento alargado é inclinado, o que descarta famílias de pilar.
    /// Convenções: `lateral` aponta da viga para a escada; `across` corre ao longo da
    /// viga (direção da largura). O plano dos degraus fica a
    /// (meia-largura da viga + afastamento) da face, medido do eixo da viga.
    /// </summary>
    public class LadderBuilder
    {
        private readonly Document _doc;
        private readonly Dictionary<string, FamilySymbol> _symbolCache =
            new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);

        private static readonly string LogPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "SAGA_LadderLog.txt");

        public LadderBuilder(Document doc) => _doc = doc;

        public void Build(LadderPlacement placement, LadderConfig config,
                          LadderDefinition def, ICollection<ElementId> createdIds)
        {
            Log("=== LadderBuilder.Build ===");

            var stringerSym = GetSymbol(config.StringerFamilyPath, config.StringerFamilyType, "montante");

            // Degrau sempre com família própria — nunca entra nos checks de
            // unificação de perfil (nem "todos", nem "suporte+anel+tira").
            var rungSym = GetSymbol(config.RungFamilyPath, config.RungFamilyType, "degrau");

            var lateral = placement.Lateral;
            var across  = XYZ.BasisZ.CrossProduct(lateral).Normalize();

            double topZ  = placement.TopZFt + config.TopLevelOffset / 304.8;
            double baseZ = placement.BaseZFt + config.BaseOffset / 304.8;
            double standoffFt = (placement.BeamHalfWidthMm + config.WallOffset) / 304.8;
            var center = new XYZ(
                placement.InsertionPoint.X + lateral.X * standoffFt,
                placement.InsertionPoint.Y + lateral.Y * standoffFt,
                0);

            // ── Montantes: cria no eixo provisório (largura útil), mede a seção real
            //    e afasta W/2 para a largura ficar face a face. ─────────────────────
            double halfUsableFt = (config.Width / 2.0) / 304.8;
            var provisionalA = Pos(center, across, -halfUsableFt);

            var stringerA = CreateBeam(stringerSym,
                Line.CreateBound(At(provisionalA, baseZ), At(provisionalA, topZ)),
                "montante A", createdIds);
            OrientCrossSection(stringerA, lateral);

            // Mede a largura só depois de orientar — a rotação pode mudar a
            // extensão projetada em 'across' (seção assimétrica, ex.: cantoneira).
            _doc.Regenerate();
            double stringerWidthMm = stringerA != null
                ? GeometryMeasure.ExtentAlongMm(_doc, stringerA.Id, across)
                : 0.0;
            double axisHalfFt = halfUsableFt + (stringerWidthMm / 2.0) / 304.8;

            if (stringerA != null && stringerWidthMm > 1.0)
                ElementTransformUtils.MoveElement(
                    _doc, stringerA.Id, across * (-(stringerWidthMm / 2.0) / 304.8));

            var posA = Pos(center, across, -axisHalfFt);
            var posB = Pos(center, across, +axisHalfFt);
            var stringerB = CreateBeam(stringerSym,
                Line.CreateBound(At(posB, baseZ), At(posB, topZ)),
                "montante B", createdIds);
            OrientCrossSection(stringerB, lateral);

            Log($"montantes: largura útil={config.Width:F0}mm | seção medida={stringerWidthMm:F1}mm | " +
                $"eixos=±{axisHalfFt * 304.8:F1}mm | h={(topZ - baseZ) * 304.8:F0}mm");

            // ── Prolongamento com alargamento na saída ───────────────────────────
            // Não é uma única diagonal do desembarque ao topo: primeiro um trecho
            // quebrado curto (kinkFt) leva da largura da escada até a largura já
            // alargada da saída; dali, um trecho reto vertical (o restante de extFt)
            // sobe nessa largura alargada até o topo do prolongamento.
            double extFt = Math.Max(config.ExtensionHeight, 0) / 304.8;
            double flareFt = Math.Max(config.ExitFlare, 0) / 304.8;
            var flaredA = Pos(center, across, -(axisHalfFt + flareFt));
            var flaredB = Pos(center, across, +(axisHalfFt + flareFt));
            double topOfMontanteZ = topZ + extFt;

            if (extFt > 0.01)
            {
                double kinkFt  = Math.Min(Math.Max(config.ExitKinkHeight, 0) / 304.8, extFt);
                double kinkZ = topZ + kinkFt;

                var kinkA = CreateBeam(stringerSym, Line.CreateBound(At(posA, topZ), At(flaredA, kinkZ)),
                    "prolongamento A (quebra)", createdIds);
                OrientCrossSection(kinkA, lateral);
                var kinkB = CreateBeam(stringerSym, Line.CreateBound(At(posB, topZ), At(flaredB, kinkZ)),
                    "prolongamento B (quebra)", createdIds);
                OrientCrossSection(kinkB, lateral);

                if (extFt - kinkFt > 0.01)
                {
                    var straightA = CreateBeam(stringerSym, Line.CreateBound(At(flaredA, kinkZ), At(flaredA, topZ + extFt)),
                        "prolongamento A (reto)", createdIds);
                    OrientCrossSection(straightA, lateral);
                    var straightB = CreateBeam(stringerSym, Line.CreateBound(At(flaredB, kinkZ), At(flaredB, topZ + extFt)),
                        "prolongamento B (reto)", createdIds);
                    OrientCrossSection(straightB, lateral);
                }
            }

            // ── Degraus: de eixo a eixo dos montantes, cotas vindas do cálculo ───
            foreach (double elevMm in def.RungElevations)
            {
                double z = baseZ + elevMm / 304.8;
                CreateBeam(rungSym,
                    Line.CreateBound(At(posA, z), At(posB, z)),
                    $"degrau +{elevMm:F0}", createdIds);
            }

            // ── Suportes: pares horizontais do montante de volta ao eixo da viga ─
            string supportPath = config.SameProfileAll ? config.StringerFamilyPath : config.SupportFamilyPath;
            string supportType = config.SameProfileAll ? config.StringerFamilyType : config.SupportFamilyType;
            var supportSym = TryGetSymbol(supportPath, supportType, "suporte");
            if (supportSym != null)
            {
                foreach (double elevMm in def.SupportElevations)
                {
                    double z = baseZ + elevMm / 304.8;
                    foreach (var pos in new[] { posA, posB })
                    {
                        var outer = At(pos, z);
                        var inner = new XYZ(
                            outer.X - lateral.X * standoffFt,
                            outer.Y - lateral.Y * standoffFt,
                            z);
                        CreateBeam(supportSym, Line.CreateBound(inner, outer),
                            $"suporte +{elevMm:F0}", createdIds);
                    }
                }
            }

            // ── Gaiola: anéis em arco + tiras verticais ao longo do arco ─────────
            if (config.HasCage && def.RingElevations.Count > 0)
                BuildCage(config, def, createdIds, posA, posB, center, lateral, baseZ,
                          flaredA, flaredB, topOfMontanteZ);

            // ── Linha de vida: membro vertical central ───────────────────────────
            if (config.HasLifeline)
            {
                var lifelineSym = TryGetSymbol(config.LifelineFamilyPath, config.LifelineFamilyType, "linha de vida");
                if (lifelineSym != null)
                {
                    double z0 = baseZ + def.BottomGapMm / 304.8;
                    double z1 = topZ + extFt;
                    CreateBeam(lifelineSym,
                        Line.CreateBound(At(center, z0), At(center, z1)),
                        "linha de vida", createdIds);
                }
            }

            Log($"=== Build concluído: {createdIds.Count} elementos ===");
        }

        private void BuildCage(LadderConfig config, LadderDefinition def,
                               ICollection<ElementId> createdIds,
                               XYZ posA, XYZ posB, XYZ center, XYZ lateral, double baseZ,
                               XYZ flaredA, XYZ flaredB, double topOfMontanteZ)
        {
            // "Mesmo perfil para todos" tem prioridade; senão, "mesmo perfil para
            // suporte+anel+tira" usa o suporte como fonte; senão, cada um mantém
            // sua própria família configurada.
            string ringPath = config.SameProfileAll ? config.StringerFamilyPath
                            : config.SameProfileCage ? config.SupportFamilyPath
                            : config.RingFamilyPath;
            string ringType = config.SameProfileAll ? config.StringerFamilyType
                            : config.SameProfileCage ? config.SupportFamilyType
                            : config.RingFamilyType;
            var ringSym = TryGetSymbol(ringPath, ringType, "anel da gaiola");
            if (ringSym == null)
            {
                Log("gaiola: sem perfil de anel — anéis não criados.");
                return;
            }

            double projFt = Math.Max(config.CageProjection, 50.0 ) / 304.8;
            var bulge = new XYZ(
                center.X + lateral.X * projFt,
                center.Y + lateral.Y * projFt,
                0);
            double setbackFt = Math.Max(config.RingSetback, 0) / 304.8;

            double lastElevMm = def.RingElevations.Last();
            bool hasTopClosure = topOfMontanteZ - (baseZ + lastElevMm / 304.8) > 0.01;

            Arc referenceArc = null;
            foreach (double elevMm in def.RingElevations)
            {
                double z = baseZ + elevMm / 304.8;
                try
                {
                    var arc = CreateRingWithSetback(ringSym, posA, posB, bulge, z, setbackFt,
                        $"anel +{elevMm:F0}", createdIds);
                    if (referenceArc == null) referenceArc = arc;
                }
                catch (Exception ex)
                {
                    Log($"anel +{elevMm:F0}: falha ao criar arco ({ex.Message})");
                }

                // Barra horizontal (D): fecha o anel por trás, ligando os dois montantes
                // direto. No último anel, se o prolongamento seguir além dele, essa barra
                // some daqui — quem fecha o topo é o trecho maior (E) logo abaixo.
                bool isLastRing = Math.Abs(elevMm - lastElevMm) < 0.01;
                if (!isLastRing || !hasTopClosure)
                    CreateBeam(ringSym, Line.CreateBound(At(posA, z), At(posB, z)),
                        $"anel +{elevMm:F0} (barra horizontal)", createdIds);
            }

            // Trecho superior de fechamento (E): substitui a barra D do último anel
            // quando o prolongamento segue além dele — mais largo (largura já alargada
            // da saída) e na cota onde o montante realmente termina, não na do anel.
            if (hasTopClosure)
                CreateBeam(ringSym, Line.CreateBound(At(flaredA, topOfMontanteZ), At(flaredB, topOfMontanteZ)),
                    "gaiola (trecho superior de fechamento)", createdIds);

            // Tiras verticais distribuídas ao longo do arco (excluindo as extremidades,
            // onde já estão os montantes), do primeiro ao último anel.
            if (def.StrapCount <= 0 || referenceArc == null || def.RingElevations.Count < 2) return;

            string strapPath = config.SameProfileAll ? config.StringerFamilyPath
                              : config.SameProfileCage ? config.SupportFamilyPath
                              : config.StrapFamilyPath;
            string strapType = config.SameProfileAll ? config.StringerFamilyType
                              : config.SameProfileCage ? config.SupportFamilyType
                              : config.StrapFamilyType;
            var strapSym = TryGetSymbol(strapPath, strapType, "barra vertical da gaiola");
            if (strapSym == null)
            {
                Log("gaiola: sem perfil de barra vertical — tiras não criadas.");
                return;
            }

            double zFirst = baseZ + def.RingElevations.First() / 304.8;
            // Se houver fechamento superior (E), a tira acompanha até lá — senão
            // pararia na cota do antigo último anel, deixando um vão até o topo real.
            double zLast  = hasTopClosure ? topOfMontanteZ : baseZ + def.RingElevations.Last() / 304.8;
            var arcCenter = new XYZ(referenceArc.Center.X, referenceArc.Center.Y, 0);

            for (int i = 0; i < def.StrapCount; i++)
            {
                double t = (i + 1) / (double)(def.StrapCount + 1);
                var p = referenceArc.Evaluate(t, true);
                var xy = new XYZ(p.X, p.Y, 0);
                var strapInst = CreateBeam(strapSym,
                    Line.CreateBound(At(xy, zFirst), At(xy, zLast)),
                    $"tira {i + 1}", createdIds);

                // Regenera antes de medir: sem isso, a geometria/transform da
                // instância recém-criada pode não estar finalizada ainda, dando
                // uma leitura instável de tira pra tira.
                _doc.Regenerate();

                // Perpendicular ao centro do anel: cada tira fica radial à curvatura,
                // não com uma rotação fixa igual ao montante.
                var radial = xy - arcCenter;
                if (radial.GetLength() > 1e-6)
                    OrientCrossSection(strapInst, radial.Normalize());
            }
        }

        /// <summary>
        /// Cria o anel entre os montantes. Se <paramref name="setbackFt"/> > 0, o arco
        /// nasce recuado desse tanto em relação ao montante, e um trecho reto horizontal
        /// fecha a lacuna até o eixo do montante — cobrindo-o por completo mesmo com o
        /// anel afastado.
        /// </summary>
        private Arc CreateRingWithSetback(FamilySymbol ringSym, XYZ posA, XYZ posB, XYZ bulge, double z,
                                          double setbackFt, string tag, ICollection<ElementId> createdIds)
        {
            var fullArc = Arc.Create(At(posA, z), At(posB, z), At(bulge, z));
            if (setbackFt < 0.001 || fullArc.Length <= setbackFt * 2 + 0.01)
            {
                CreateBeam(ringSym, fullArc, tag, createdIds);
                return fullArc;
            }

            double t0 = setbackFt / fullArc.Length;
            var insetStart = fullArc.Evaluate(t0, true);
            var insetEnd   = fullArc.Evaluate(1.0 - t0, true);
            var mid        = fullArc.Evaluate(0.5, true);
            var ringArc    = Arc.Create(insetStart, insetEnd, mid);

            CreateBeam(ringSym, ringArc, tag, createdIds);
            CreateBeam(ringSym, Line.CreateBound(insetStart, At(posA, z)), $"{tag} (trecho A)", createdIds);
            CreateBeam(ringSym, Line.CreateBound(insetEnd, At(posB, z)), $"{tag} (trecho B)", createdIds);

            return ringArc;
        }

        // ── Helpers geométricos ───────────────────────────────────────────────

        private static XYZ Pos(XYZ centerXy, XYZ across, double offsetFt) =>
            new XYZ(centerXy.X + across.X * offsetFt, centerXy.Y + across.Y * offsetFt, 0);

        private static XYZ At(XYZ xy, double z) => new XYZ(xy.X, xy.Y, z);

        /// <summary>
        /// Gira a seção de uma barra (STRUCTURAL_BEND_DIR_ANGLE) até ficar com a maior
        /// dimensão do perfil voltada para <paramref name="targetDirection"/> — vale
        /// pra barra vertical ou inclinada (ex.: trecho quebrado do prolongamento).
        /// O Revit não tem uma referência "ângulo zero" previsível pra toda direção de
        /// barra — por isso medimos a orientação real que a instância recebeu (via
        /// GetTransform: BasisZ é o eixo da própria barra, BasisX é a referência atual
        /// da seção) e projetamos o alvo no plano perpendicular a esse eixo antes de
        /// medir o ângulo. Isso funciona no referencial da própria barra, em vez de
        /// supor que a referência do Revit fica no plano horizontal.
        /// </summary>
        private static void OrientCrossSection(FamilyInstance inst, XYZ targetDirection)
        {
            if (inst == null || targetDirection == null) return;

            var transform = inst.GetTransform();
            var axis    = transform.BasisZ;
            var current = transform.BasisX;
            if (current.GetLength() < 1e-9) return;
            current = current.Normalize();

            // Projeta o alvo no plano perpendicular ao eixo da barra.
            var target = targetDirection - axis * targetDirection.DotProduct(axis);
            if (target.GetLength() < 1e-9) return;
            target = target.Normalize();

            double dot   = current.DotProduct(target);
            double cross = axis.DotProduct(current.CrossProduct(target));
            double angle = Math.Atan2(cross, dot);

            var p = inst.get_Parameter(BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE);
            if (p != null && !p.IsReadOnly) p.Set(angle);
        }

        // ── Criação ───────────────────────────────────────────────────────────

        private FamilyInstance CreateBeam(FamilySymbol sym, Curve curve, string tag,
                                          ICollection<ElementId> createdIds)
        {
            if (curve.Length < 0.001)
            {
                Log($"  [{tag}] segmento degenerado ignorado");
                return null;
            }

            var mid   = curve.Evaluate(0.5, true);
            var level = GetNearestLevel(mid.Z);
            var inst  = _doc.Create.NewFamilyInstance(curve, sym, level, StructuralType.Beam);
            CenterJustify(inst);
            try
            {
                StructuralFramingUtils.DisallowJoinAtEnd(inst, 0);
                StructuralFramingUtils.DisallowJoinAtEnd(inst, 1);
            }
            catch { /* nem toda família suporta join */ }

            createdIds?.Add(inst.Id);
            Log($"  [{tag}] OK");
            return inst;
        }

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

        // ── Símbolos ──────────────────────────────────────────────────────────

        /// <summary>Símbolo obrigatório: lança erro claro se ausente ou de categoria errada.</summary>
        private FamilySymbol GetSymbol(string path, string typeName, string component)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException(
                    $"Selecione a família do {component} (.rfa) antes de criar a escada.");
            return Resolve(path, typeName, component);
        }

        /// <summary>Símbolo opcional: retorna null se não configurado.</summary>
        private FamilySymbol TryGetSymbol(string path, string typeName, string component)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            return Resolve(path, typeName, component);
        }

        private FamilySymbol Resolve(string path, string typeName, string component)
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
                        $"Não foi possível carregar '{familyName}' tipo '{typeName}' ({component}).");
            }

            var catId = symbol.Family.FamilyCategory?.Id.GetId();
            if (catId != (int)BuiltInCategory.OST_StructuralFraming)
                throw new InvalidOperationException(
                    $"A família do {component} ('{symbol.Family.Name}') deve ser de Quadro Estrutural (Viga).\n" +
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
            catch { /* log não-crítico */ }
        }
    }
}
