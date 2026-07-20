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
            var rungSym     = GetSymbol(config.RungFamilyPath, config.RungFamilyType, "degrau");

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
            CreateBeam(stringerSym,
                Line.CreateBound(At(posB, baseZ), At(posB, topZ)),
                "montante B", createdIds);

            Log($"montantes: largura útil={config.Width:F0}mm | seção medida={stringerWidthMm:F1}mm | " +
                $"eixos=±{axisHalfFt * 304.8:F1}mm | h={(topZ - baseZ) * 304.8:F0}mm");

            // ── Prolongamento com alargamento na saída ───────────────────────────
            double extFt = Math.Max(config.ExtensionHeight, 0) / 304.8;
            if (extFt > 0.01)
            {
                double flareFt = Math.Max(config.ExitFlare, 0) / 304.8;
                CreateBeam(stringerSym, Line.CreateBound(
                    At(posA, topZ),
                    At(Pos(center, across, -(axisHalfFt + flareFt)), topZ + extFt)),
                    "prolongamento A", createdIds);
                CreateBeam(stringerSym, Line.CreateBound(
                    At(posB, topZ),
                    At(Pos(center, across, +(axisHalfFt + flareFt)), topZ + extFt)),
                    "prolongamento B", createdIds);
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
            var supportSym = TryGetSymbol(config.SupportFamilyPath, config.SupportFamilyType, "suporte");
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
                BuildCage(config, def, createdIds, posA, posB, center, lateral, baseZ);

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
                               XYZ posA, XYZ posB, XYZ center, XYZ lateral, double baseZ)
        {
            var ringSym = TryGetSymbol(config.RingFamilyPath, config.RingFamilyType, "anel da gaiola");
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

            Arc referenceArc = null;
            foreach (double elevMm in def.RingElevations)
            {
                double z = baseZ + elevMm / 304.8;
                try
                {
                    var arc = Arc.Create(At(posA, z), At(posB, z), At(bulge, z));
                    if (referenceArc == null) referenceArc = arc;
                    CreateBeam(ringSym, arc, $"anel +{elevMm:F0}", createdIds);
                }
                catch (Exception ex)
                {
                    Log($"anel +{elevMm:F0}: falha ao criar arco ({ex.Message})");
                }
            }

            // Tiras verticais distribuídas ao longo do arco (excluindo as extremidades,
            // onde já estão os montantes), do primeiro ao último anel.
            if (def.StrapCount <= 0 || referenceArc == null || def.RingElevations.Count < 2) return;

            var strapSym = TryGetSymbol(config.StrapFamilyPath, config.StrapFamilyType, "barra vertical da gaiola");
            if (strapSym == null)
            {
                Log("gaiola: sem perfil de barra vertical — tiras não criadas.");
                return;
            }

            double zFirst = baseZ + def.RingElevations.First() / 304.8;
            double zLast  = baseZ + def.RingElevations.Last() / 304.8;

            for (int i = 0; i < def.StrapCount; i++)
            {
                double t = (i + 1) / (double)(def.StrapCount + 1);
                var p = referenceArc.Evaluate(t, true);
                var xy = new XYZ(p.X, p.Y, 0);
                CreateBeam(strapSym,
                    Line.CreateBound(At(xy, zFirst), At(xy, zLast)),
                    $"tira {i + 1}", createdIds);
            }
        }

        // ── Helpers geométricos ───────────────────────────────────────────────

        private static XYZ Pos(XYZ centerXy, XYZ across, double offsetFt) =>
            new XYZ(centerXy.X + across.X * offsetFt, centerXy.Y + across.Y * offsetFt, 0);

        private static XYZ At(XYZ xy, double z) => new XYZ(xy.X, xy.Y, z);

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
