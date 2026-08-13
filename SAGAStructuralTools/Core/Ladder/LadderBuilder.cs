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
                {
                    var dBar = CreateBeam(ringSym, Line.CreateBound(At(posA, z), At(posB, z)),
                        $"anel +{elevMm:F0} (barra horizontal)", createdIds);
                    LogAndOrientRing(dBar, $"anel +{elevMm:F0} (barra D)");
                }
            }

            // Anel de entrada (topo do prolongamento): MESMO centro e raio do anel
            // intermediário logo abaixo (mesmo "eixo do anel" — alinhamento vertical e
            // horizontal preservado), mas varrendo só 180° (meia-circunferência de
            // verdade) em vez do laço quase completo dos anéis intermediários. Uma
            // meia-lua de raio R tem corda = 2R, quase sempre mais larga que o vão
            // recuado do anel de baixo — a perna reta cobre essa sobra até o montante
            // alargado (flaredA/flaredB).
            if (hasTopClosure && referenceArc != null)
            {
                var topCenter = new XYZ(referenceArc.Center.X, referenceArc.Center.Y, topOfMontanteZ);
                var entryArc = Arc.Create(topCenter, referenceArc.Radius,
                    -Math.PI / 2.0, Math.PI / 2.0,
                    referenceArc.XDirection, referenceArc.YDirection);

                var arcEnd0 = entryArc.GetEndPoint(0);
                var arcEnd1 = entryArc.GetEndPoint(1);
                var posA3 = At(posA, topOfMontanteZ);
                var posB3 = At(posB, topOfMontanteZ);
                var flaredAAtTop = At(flaredA, topOfMontanteZ);
                var flaredBAtTop = At(flaredB, topOfMontanteZ);

                // Casa cada ponta do arco com o montante (A ou B) mais próximo, por
                // proximidade — não por ordem de índice, que pode variar com a geometria.
                var endForA = arcEnd0.DistanceTo(posA3) <= arcEnd0.DistanceTo(posB3) ? arcEnd0 : arcEnd1;
                var endForB = endForA.IsAlmostEqualTo(arcEnd0) ? arcEnd1 : arcEnd0;

                Log($"[gaiola (anel de entrada)] geometria: centro=({entryArc.Center.X:F2},{entryArc.Center.Y:F2}) " +
                    $"raioMm={entryArc.Radius * 304.8:F0} pontaA=({endForA.X:F2},{endForA.Y:F2}) " +
                    $"pontaB=({endForB.X:F2},{endForB.Y:F2}) pernaAMm={flaredAAtTop.DistanceTo(endForA) * 304.8:F0} " +
                    $"pernaBMm={flaredBAtTop.DistanceTo(endForB) * 304.8:F0}");

                var legA = CreateBeam(ringSym, Line.CreateBound(flaredAAtTop, endForA), "gaiola (perna A de entrada)", createdIds);
                LogAndOrientRing(legA, "gaiola (perna A de entrada)");
                var legB = CreateBeam(ringSym, Line.CreateBound(flaredBAtTop, endForB), "gaiola (perna B de entrada)", createdIds);
                LogAndOrientRing(legB, "gaiola (perna B de entrada)");

                var entryArcInst = CreateBeam(ringSym, entryArc, "gaiola (anel de entrada)", createdIds);
                LogAndOrientRing(entryArcInst, "gaiola (anel de entrada)");
            }

            // Tiras verticais distribuídas ao longo do arco (excluindo as extremidades,
            // onde já estão os montantes), do primeiro ao último anel — posicionadas
            // por ângulo (StrapAngleStepDeg), não por contagem fixa.
            if (referenceArc == null || def.RingElevations.Count < 2) return;

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
            // Regra: não desenhar tira além de onde o anel realmente existe. O anel de
            // entrada é uma meia-lua (só ±90° a partir do ápice) — bem mais estreita
            // que o laço quase completo dos anéis intermediários. Uma tira fora desses
            // ±90° não tem anel de entrada pra alcançar, então para na cota do último
            // anel intermediário em vez de subir até o topo do prolongamento.
            double zLastWithinEntry  = hasTopClosure ? topOfMontanteZ : baseZ + def.RingElevations.Last() / 304.8;
            double zLastBeyondEntry  = baseZ + def.RingElevations.Last() / 304.8;
            const double entryHalfSweepRad = Math.PI / 2.0;

            var arcCenter = new XYZ(referenceArc.Center.X, referenceArc.Center.Y, 0);
            var xVec = referenceArc.XDirection;
            var yVec = referenceArc.YDirection;
            double radius = referenceArc.Radius;
            double ringHalfSweepRad = (referenceArc.GetEndParameter(1) - referenceArc.GetEndParameter(0)) / 2.0;
            double stepRad = Math.Max(config.StrapAngleStepDeg, 1.0) * Math.PI / 180.0;
            int maxCount = Math.Max(config.StrapCount, 0);

            // Ângulo controla o espaçamento, StrapCount controla o teto — nunca passa
            // da quantidade pedida, mesmo que a varredura do anel comportasse mais.
            // Ímpar: uma barra no eixo (0°) + pares saindo a cada passo inteiro dali.
            // Par: nenhuma barra no eixo — o par mais interno fica a meio passo de
            // cada lado (o eixo passa ENTRE as duas barras centrais), e os próximos
            // pares continuam a partir daí a cada passo inteiro.
            bool isOdd = maxCount % 2 == 1;
            var angles = new List<double>();
            if (isOdd && maxCount > 0 && 0.0 < ringHalfSweepRad - 1e-6)
                angles.Add(0.0);

            double pairStart = isOdd ? stepRad : stepRad / 2.0;
            int pairsNeeded = isOdd ? (maxCount - 1) / 2 : maxCount / 2;
            for (int k = 0; k < pairsNeeded; k++)
            {
                double a = pairStart + k * stepRad;
                if (a >= ringHalfSweepRad - 1e-6) break;
                angles.Add(-a);
                if (angles.Count < maxCount) angles.Add(a);
            }
            angles.Sort();
            Log($"gaiola (tiras): tetoPedido={maxCount} paridade={(isOdd ? "ímpar (eixo)" : "par (deslocado)")} " +
                $"tetoFinal={angles.Count} passoGraus={config.StrapAngleStepDeg:F1}");

            for (int i = 0; i < angles.Count; i++)
            {
                double angle = angles[i];
                var xy = arcCenter + radius * (Math.Cos(angle) * xVec + Math.Sin(angle) * yVec);
                double zLast = Math.Abs(angle) <= entryHalfSweepRad + 1e-6 ? zLastWithinEntry : zLastBeyondEntry;

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
                Log($"[tira {i + 1}] anguloGraus={angle * 180.0 / Math.PI:F1} xy=({xy.X:F2},{xy.Y:F2}) " +
                    $"arcCenter=({arcCenter.X:F2},{arcCenter.Y:F2}) zLastMm={zLast * 304.8:F0} " +
                    $"radial=({radial.X:F3},{radial.Y:F3},{radial.Z:F3})");
                if (strapInst != null)
                {
                    var before = strapInst.GetTransform();
                    Log($"  [tira {i + 1}] ANTES: BasisX=({before.BasisX.X:F3},{before.BasisX.Y:F3},{before.BasisX.Z:F3}) " +
                        $"BasisY=({before.BasisY.X:F3},{before.BasisY.Y:F3},{before.BasisY.Z:F3}) " +
                        $"BasisZ=({before.BasisZ.X:F3},{before.BasisZ.Y:F3},{before.BasisZ.Z:F3})");
                }
                // A face larga precisa "olhar" para o centro do anel — alvo é o
                // sentido inverso do radial (que aponta do centro pra fora). O alvo
                // sozinho alinha o eixo da LARGURA ao centro (a face fina é que olha
                // pra lá); +90° extra troca pra alinhar o eixo da ESPESSURA ao centro,
                // fazendo a face larga apontar pra lá.
                if (radial.GetLength() > 1e-6)
                    OrientCrossSectionByTangent(strapInst, radial.Normalize().Negate(), -Math.PI / 2.0);
                if (strapInst != null)
                {
                    _doc.Regenerate();
                    var after = strapInst.GetTransform();
                    var angleParam = strapInst.get_Parameter(BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE);
                    Log($"  [tira {i + 1}] DEPOIS: BasisX=({after.BasisX.X:F3},{after.BasisX.Y:F3},{after.BasisX.Z:F3}) " +
                        $"BasisY=({after.BasisY.X:F3},{after.BasisY.Y:F3},{after.BasisY.Z:F3}) " +
                        $"BasisZ=({after.BasisZ.X:F3},{after.BasisZ.Y:F3},{after.BasisZ.Z:F3}) " +
                        $"anguloGraus={(angleParam?.AsDouble() ?? 0) * 180.0 / Math.PI:F1}");
                }
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
            var fullArc = BuildDomeArc(posA, posB, bulge, z);
            if (fullArc == null)
            {
                var degenerate = Arc.Create(At(posA, z), At(posB, z), At(bulge, z));
                var degenerateInst = CreateBeam(ringSym, degenerate, tag, createdIds);
                LogAndOrientRing(degenerateInst, $"{tag} (arco completo)");
                return degenerate;
            }

            var mid2d = new XYZ((posA.X + posB.X) / 2.0, (posA.Y + posB.Y) / 2.0, 0);
            double sagittaFt = bulge.DistanceTo(mid2d);
            double sweepGraus = (fullArc.GetEndParameter(1) - fullArc.GetEndParameter(0)) * 180.0 / Math.PI;
            Log($"[{tag}] geometria: posA=({posA.X:F2},{posA.Y:F2}) posB=({posB.X:F2},{posB.Y:F2}) " +
                $"bulge=({bulge.X:F2},{bulge.Y:F2}) z={z * 304.8:F0}mm setbackMm={setbackFt * 304.8:F1} " +
                $"raio={fullArc.Radius * 304.8:F0}mm sagittaMm={sagittaFt * 304.8:F0} " +
                $"varreduraGraus={sweepGraus:F0} centro=({fullArc.Center.X:F2},{fullArc.Center.Y:F2})");

            // Recuo maior ou igual à projeção não tem solução geométrica (a corda
            // deslocada ultrapassaria o ápice) — cai pro arco completo, sem recuo.
            if (setbackFt < 0.001 || setbackFt >= sagittaFt - 0.001)
            {
                var full = CreateBeam(ringSym, fullArc, tag, createdIds);
                LogAndOrientRing(full, $"{tag} (arco completo)");
                return fullArc;
            }

            // Desloca posA/posB na direção perpendicular ao montante (lateral, rumo ao
            // ápice), preservando a distância entre eles — a largura da escada não
            // muda — e reconstrói o arco a partir dessa corda deslocada, com o MESMO
            // ápice de referência. Isso garante que a barra de afastamento (trecho)
            // seja sempre reta e ortogonal ao montante (ela é literalmente posA→recessedA,
            // uma translação pura), em vez de "andar por ângulo" na curva original, o
            // que a inclinava em direção ao arco.
            var lateralDir = (bulge - mid2d).Normalize();
            var recessedA = posA + lateralDir * setbackFt;
            var recessedB = posB + lateralDir * setbackFt;
            var ringArc = BuildDomeArc(recessedA, recessedB, bulge, z);
            if (ringArc == null)
            {
                var fallback = CreateBeam(ringSym, fullArc, tag, createdIds);
                LogAndOrientRing(fallback, $"{tag} (arco completo, recuo inválido)");
                return fullArc;
            }

            Log($"[{tag}] recuo: recessedA=({recessedA.X:F2},{recessedA.Y:F2}) " +
                $"recessedB=({recessedB.X:F2},{recessedB.Y:F2}) raioRecuoMm={ringArc.Radius * 304.8:F0}mm");

            var arcInst = CreateBeam(ringSym, ringArc, tag, createdIds);
            LogAndOrientRing(arcInst, $"{tag} (arco)");

            var trechoA = CreateBeam(ringSym, Line.CreateBound(At(posA, z), At(recessedA, z)), $"{tag} (trecho A)", createdIds);
            LogAndOrientRing(trechoA, $"{tag} (trecho A)");

            var trechoB = CreateBeam(ringSym, Line.CreateBound(At(posB, z), At(recessedB, z)), $"{tag} (trecho B)", createdIds);
            LogAndOrientRing(trechoB, $"{tag} (trecho B)");

            return ringArc;
        }

        /// <summary>
        /// Constrói um arco "dome" (calota) entre <paramref name="p0"/> e
        /// <paramref name="p1"/>, passando por <paramref name="bulge"/> — centro+raio
        /// explícitos pela fórmula da sagitta, não Arc.Create(p0,p1,p2) por 3 pontos
        /// (ambíguo quando a sagitta é maior que o raio, caso normal de anel de gaiola
        /// com diâmetro maior que o vão entre montantes). Retorna null se a corda ou a
        /// sagitta forem degeneradas.
        /// </summary>
        private static Arc BuildDomeArc(XYZ p0, XYZ p1, XYZ bulge, double z)
        {
            var mid2d = new XYZ((p0.X + p1.X) / 2.0, (p0.Y + p1.Y) / 2.0, 0);
            double halfChord = p0.DistanceTo(p1) / 2.0;
            double sagittaFt = bulge.DistanceTo(mid2d);
            if (halfChord < 1e-6 || sagittaFt < 1e-6) return null;

            var xVec = (bulge - mid2d).Normalize();
            var yVec = XYZ.BasisZ.CrossProduct(xVec).Normalize();
            double radius = (halfChord * halfChord + sagittaFt * sagittaFt) / (2.0 * sagittaFt);
            var center = At(mid2d - xVec * (radius - sagittaFt), z);

            var p0z = At(p0, z);
            var p1z = At(p1, z);
            double angA = Math.Atan2((p0z - center).DotProduct(yVec), (p0z - center).DotProduct(xVec));
            double angB = Math.Atan2((p1z - center).DotProduct(yVec), (p1z - center).DotProduct(xVec));
            double startAngle = Math.Min(angA, angB);
            double endAngle   = Math.Max(angA, angB);

            return Arc.Create(center, radius, startAngle, endAngle, xVec, yVec);
        }

        /// <summary>
        /// Orienta um elemento do anel (arco ou trecho reto) com a face larga voltada
        /// pra cima — convenção real de barra chata dobrada "pelo lado fácil" numa
        /// gaiola de escada marinheiro (face larga no plano horizontal do próprio
        /// anel, espessura na vertical). Loga o transform antes/depois pra permitir
        /// investigar visualmente se o Revit mantém o frame consistente ao longo do
        /// arco (sem torção) ou não.
        /// </summary>
        private void LogAndOrientRing(FamilyInstance inst, string tag)
        {
            if (inst == null) return;
            _doc.Regenerate();

            var before = inst.GetTransform();
            Log($"  [{tag}] ANTES: BasisX=({before.BasisX.X:F3},{before.BasisX.Y:F3},{before.BasisX.Z:F3}) " +
                $"BasisY=({before.BasisY.X:F3},{before.BasisY.Y:F3},{before.BasisY.Z:F3}) " +
                $"BasisZ=({before.BasisZ.X:F3},{before.BasisZ.Y:F3},{before.BasisZ.Z:F3})");

            OrientCrossSectionByTangent(inst, XYZ.BasisZ);
            _doc.Regenerate();

            var after = inst.GetTransform();
            var angleParam = inst.get_Parameter(BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE);
            Log($"  [{tag}] DEPOIS: BasisX=({after.BasisX.X:F3},{after.BasisX.Y:F3},{after.BasisX.Z:F3}) " +
                $"BasisY=({after.BasisY.X:F3},{after.BasisY.Y:F3},{after.BasisY.Z:F3}) " +
                $"BasisZ=({after.BasisZ.X:F3},{after.BasisZ.Y:F3},{after.BasisZ.Z:F3}) " +
                $"anguloGraus={(angleParam?.AsDouble() ?? 0) * 180.0 / Math.PI:F1}");
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

        /// <summary>
        /// Mesma ideia de <see cref="OrientCrossSection"/>, mas com o eixo/referência
        /// TROCADOS: log confirmou (barra D do anel e tiras verticais) que
        /// GetTransform().BasisX — não BasisZ — é o eixo real da barra pra essas
        /// instâncias (BasisZ vinha sempre (0,0,1) mesmo pra barras horizontais,
        /// batendo com "referência de cima" fixa, não com a tangente). Usada só pra
        /// anel e tiras, que são onde isso foi confirmado — montante/prolongamento
        /// continuam com <see cref="OrientCrossSection"/> por já estarem corretos.
        /// </summary>
        private static void OrientCrossSectionByTangent(FamilyInstance inst, XYZ targetDirection, double extraRotationRad = 0)
        {
            if (inst == null || targetDirection == null) return;

            var transform = inst.GetTransform();
            var axis    = transform.BasisX;
            var current = transform.BasisZ;
            if (current.GetLength() < 1e-9) return;
            current = current.Normalize();

            var target = targetDirection - axis * targetDirection.DotProduct(axis);
            if (target.GetLength() < 1e-9) return;
            target = target.Normalize();

            double dot   = current.DotProduct(target);
            double cross = axis.DotProduct(current.CrossProduct(target));
            double angle = Math.Atan2(cross, dot);

            // Confirmado por log: o STRUCTURAL_BEND_DIR_ANGLE do Revit gira no sentido
            // OPOSTO ao da fórmula padrão (Rodrigues/mão direita) usada aqui — sem essa
            // inversão, o resultado final sai espelhado (mesma componente ao longo do
            // eixo, sinal invertido na perpendicular), não alinhado ao alvo.
            var p = inst.get_Parameter(BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE);
            if (p != null && !p.IsReadOnly) p.Set(-angle + extraRotationRad);
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

            ZeroOutExtensionParameters(inst, tag);

            createdIds?.Add(inst.Id);
            Log($"  [{tag}] OK");
            return inst;
        }

        /// <summary>
        /// O join automático do Revit (calculado na criação, antes de
        /// DisallowJoinAtEnd rodar) pode deixar um valor de "Start/End Extension"
        /// gravado na instância mesmo depois do join ser desabilitado — a peça fica
        /// fisicamente maior/deslocada do que a curva pedida. Zera qualquer parâmetro
        /// de extensão de ponta encontrado (nome contém "extens", cobre inglês e
        /// português) e loga o que achou, pra confirmar se é essa a causa.
        /// </summary>
        private void ZeroOutExtensionParameters(FamilyInstance inst, string tag)
        {
            if (inst == null) return;
            foreach (Parameter p in inst.Parameters)
            {
                var name = p.Definition?.Name;
                if (string.IsNullOrEmpty(name) || name.IndexOf("extens", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                double before = p.StorageType == StorageType.Double ? p.AsDouble() : 0;
                Log($"  [{tag}] parâmetro de extensão encontrado: \"{name}\" valorMm={before * 304.8:F1} readOnly={p.IsReadOnly}");

                if (p.StorageType == StorageType.Double && !p.IsReadOnly && Math.Abs(before) > 1e-9)
                {
                    p.Set(0.0);
                    Log($"  [{tag}] \"{name}\" zerado (era {before * 304.8:F1}mm)");
                }
            }
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
