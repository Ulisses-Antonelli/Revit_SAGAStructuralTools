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
    public class PostBuilder
    {
        private readonly Document _doc;
        private static readonly string LogPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "SAGA_RailLog.txt");

        public PostBuilder(Document doc) => _doc = doc;

        public void Build(RailSegment seg, RailConfig config, XYZ lineStart, XYZ lineEnd,
                          double? railAxisZ = null, ICollection<ElementId> createdIds = null)
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
            double baseZ     = lineStart.Z - config.PostBaseOffset / 304.8;   // nasce na linha selecionada
            var    level     = GetNearestLevel(lineStart.Z);

            // ── Parte 3: restrição de altura (topo no EIXO CENTRAL do corrimão) ──
            // HandrailHeight representa diretamente o eixo central do corrimão.
            double limitZ = railAxisZ ??
                            (lineStart.Z + config.HandrailHeight / 304.8);
            double topZ   = Math.Min(limitZ + config.PostTopOffset / 304.8, limitZ);

            // ── Cria todos os montantes nas posições brutas (PostOffsets) ─────────
            var created = new List<ElementId>();
            foreach (double offsetMm in seg.PostOffsets)
            {
                double offsetFt = offsetMm / 304.8;
                var    center   = lineStart + dir * offsetFt + lateral * axisOffFt;
                var    basePt   = new XYZ(center.X, center.Y, baseZ);

                FamilyInstance inst;
                if (isFraming)
                {
                    var topPt = new XYZ(center.X, center.Y, topZ);
                    inst = _doc.Create.NewFamilyInstance(Line.CreateBound(basePt, topPt), symbol, level, StructuralType.Beam);

                    // Anula o cutback automático de junta: se o montante encostar em outro
                    // membro estrutural (corrimão, laje, viga de apoio), o Revit recua a
                    // extremidade até a FACE do elemento juntado, fazendo a extrusão nascer
                    // fora do Z esperado. Mesmo fix já aplicado em InfillBuilder.CreateBeam.
                    try
                    {
                        StructuralFramingUtils.DisallowJoinAtEnd(inst, 0);
                        StructuralFramingUtils.DisallowJoinAtEnd(inst, 1);
                    }
                    catch { /* nem toda família/categoria suporta join; ignorar */ }
                }
                else
                {
                    // Famílias de Structural Columns são baseadas em nível. Criá-las já
                    // em baseZ e depois aplicar (baseZ - nível) como offset pode somar a
                    // elevação duas vezes em algumas famílias. O ponto nasce no plano do
                    // nível; a cota absoluta fica controlada somente pelos offsets.
                    var levelPt = new XYZ(basePt.X, basePt.Y, level.ProjectElevation);
                    inst = _doc.Create.NewFamilyInstance(levelPt, symbol, level, StructuralType.Column);
                    SetColumnExtents(inst, level, baseZ, topZ);
                }

                ApplyRotation(inst, config.PostRotation, isColumn, basePt);
                created.Add(inst.Id);
                createdIds?.Add(inst.Id);
            }

            // BoundingBox é adequada para vigas, mas não para Structural Columns:
            // algumas famílias de pilar incluem planos/simbologia com extensão enorme.
            // Colunas são posicionadas exclusivamente por nível e offsets em
            // SetColumnExtents; usar a caixa nelas pode deslocá-las centenas de metros.
            double maxBaseCorrectionMm = isFraming
                ? AlignPhysicalBases(created, baseZ)
                : 0.0;

            // ── Parte 2: recuo W/2 nas pontas (distribuição em L_eixos = L − W) ───
            // Mede a largura REAL da seção pela BoundingBox (independe de parâmetro) e move
            // cada montante do offset bruto para o final, alinhando as faces externas.
            double W = 0;
            if (created.Count > 0)
            {
                _doc.Regenerate();
                // Largura na DIREÇÃO da linha (projetada) — exata em diagonais e sob rotação.
                W = GeometryMeasure.ExtentAlongMm(_doc, created[0], dir);
            }

            double L = seg.Length;
            var axisOffsets = new List<double>(seg.PostOffsets);
            // Com recuo configurado, PostOffsets já contém os eixos finais exatos.
            // O ajuste automático por W/2 é mantido apenas no modo legado (recuo zero).
            if (config.EndPostInset <= 1e-6 && W > 1e-6 && L > W)
            {
                double halfW = W / 2.0, lEixos = L - W;
                for (int i = 0; i < created.Count; i++)
                {
                    double newOff  = halfW + seg.PostOffsets[i] * lEixos / L;
                    axisOffsets[i] = newOff;
                    double deltaFt = (newOff - seg.PostOffsets[i]) / 304.8;
                    if (Math.Abs(deltaFt) > 1e-9)
                        ElementTransformUtils.MoveElement(_doc, created[i], dir * deltaFt);
                }
            }
            seg.AxisOffsets = axisOffsets;   // InfillBuilder alinha travessas/quadros por estes eixos
            seg.PostWidthMm = W;             // largura na direção da linha (para faces das cantoneiras)

            string columnPosition = isColumn && created.Count > 0
                ? DescribeColumnPosition(_doc.GetElement(created[0]) as FamilyInstance, level)
                : "";

            Log($"PostBuilder: {created.Count} montantes | W={W:F1}mm | topZ={(topZ - lineStart.Z) * 304.8:F0}mm | " +
                $"correçãoZ={maxBaseCorrectionMm:F1}mm | categoria={(isColumn ? "Column" : "Framing")} | " +
                $"família={Path.GetFileNameWithoutExtension(config.PostFamilyPath)}{columnPosition}");
        }

        /// <summary>
        /// Move cada instância verticalmente para que o menor Z da geometria física
        /// coincida com a cota de base desejada. A medição após Regenerate considera
        /// origem interna da família, justificação e eventuais recuos automáticos.
        /// </summary>
        private double AlignPhysicalBases(IReadOnlyCollection<ElementId> ids, double targetBaseZFt)
        {
            if (ids == null || ids.Count == 0) return 0;

            _doc.Regenerate();
            double maxCorrectionFt = 0;

            foreach (var id in ids)
            {
                var element = _doc.GetElement(id);
                var box = element?.get_BoundingBox(null);
                if (box == null) continue;

                double deltaZ = targetBaseZFt - box.Min.Z;
                maxCorrectionFt = Math.Max(maxCorrectionFt, Math.Abs(deltaZ));
                if (Math.Abs(deltaZ) > 1e-9)
                    ElementTransformUtils.MoveElement(_doc, id, XYZ.BasisZ * deltaZ);
            }

            if (maxCorrectionFt > 1e-9) _doc.Regenerate();
            return maxCorrectionFt * 304.8;
        }

        /// <summary>
        /// Aplica rotação (graus) ao montante em torno do seu eixo vertical.
        ///   Coluna → gira o elemento em torno da vertical pela base (RotateElement).
        ///   Viga   → parâmetro "Rotação do corte transversal" (STRUCTURAL_BEND_DIR_ANGLE).
        /// </summary>
        private void ApplyRotation(FamilyInstance inst, double degrees, bool isColumn, XYZ basePt)
        {
            if (inst == null || Math.Abs(degrees) < 1e-9) return;
            double rad = degrees * Math.PI / 180.0;

            if (isColumn)
            {
                var axis = Line.CreateBound(basePt, basePt + XYZ.BasisZ);
                ElementTransformUtils.RotateElement(_doc, inst.Id, axis, rad);
            }
            else
            {
                var p = inst.get_Parameter(BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE)
                        ?? inst.LookupParameter("Rotação do corte transversal");
                if (p != null && !p.IsReadOnly) p.Set(rad);
            }
        }

        /// <summary>
        /// Ajusta a extensão vertical de uma COLUNA. Colunas são controladas por
        /// Nível base/topo + deslocamentos — não por Z absoluto. Por padrão o Revit
        /// ancora o topo no PRÓXIMO nível acima (ex.: Nível 2), fazendo a coluna ir de
        /// nível a nível. Aqui forçamos o Nível superior = Nível base e definimos os dois
        /// deslocamentos relativos a esse nível, para o topo parar exatamente em topZ.
        /// </summary>
        private static void SetColumnExtents(FamilyInstance inst, Level level, double baseZFt, double topZFt)
        {
            var baseLevel = inst.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
            if (baseLevel != null && !baseLevel.IsReadOnly) baseLevel.Set(level.Id);

            var topLevel = inst.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
            if (topLevel != null && !topLevel.IsReadOnly) topLevel.Set(level.Id);

            var baseOff = inst.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
            if (baseOff != null && !baseOff.IsReadOnly) baseOff.Set(baseZFt - level.ProjectElevation);

            var topOff = inst.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
            if (topOff != null && !topOff.IsReadOnly) topOff.Set(topZFt - level.ProjectElevation);
        }

        private static string DescribeColumnPosition(FamilyInstance inst, Level level)
        {
            if (inst == null) return "";

            var point = inst.Location as LocationPoint;
            double pointZ = point?.Point.Z ?? double.NaN;
            double baseOff = inst.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM)?.AsDouble() ?? double.NaN;
            double topOff = inst.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM)?.AsDouble() ?? double.NaN;

            return $" | levelProjectZ={level.ProjectElevation * 304.8:F0}mm" +
                   $" | levelElevation={level.Elevation * 304.8:F0}mm" +
                   $" | pointZ={pointZ * 304.8:F0}mm" +
                   $" | baseOffset={baseOff * 304.8:F0}mm" +
                   $" | topOffset={topOff * 304.8:F0}mm";
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
