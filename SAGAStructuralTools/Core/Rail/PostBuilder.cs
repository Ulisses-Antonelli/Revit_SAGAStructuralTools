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

        public void Build(RailSegment seg, RailConfig config, RailRunGeometry run,
                          RailRunGeometry railAxisRun = null, ICollection<ElementId> createdIds = null)
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

            double axisOffFt = config.PostAxisOffset / 304.8;

            // ── Cria todos os montantes nas posições brutas (PostOffsets) ─────────
            var created = new List<ElementId>();
            var baseById = new Dictionary<ElementId, double>();
            var topById = new Dictionary<ElementId, double>();
            var levelById = new Dictionary<ElementId, Level>();
            foreach (double offsetMm in seg.PostOffsets)
            {
                var baselinePoint = run.PointAtDistanceMm(offsetMm);
                var center = baselinePoint + run.Lateral * axisOffFt;
                double baseZ = baselinePoint.Z - config.PostBaseOffset / 304.8;

                // A altura é globalmente vertical: em cada posição, o topo acompanha
                // a elevação local da linha inclinada e não uma única cota do trecho.
                double limitZ = railAxisRun?.PointAtDistanceMm(offsetMm).Z ??
                                (baselinePoint.Z + config.HandrailHeight / 304.8);
                double topZ = Math.Min(limitZ + config.PostTopOffset / 304.8, limitZ);
                var basePt = new XYZ(center.X, center.Y, baseZ);
                // No fluxo horizontal preserva a ancoragem histórica pelo nível da
                // linha selecionada. No inclinado, cada poste recebe o nível mais
                // adequado à sua própria cota de base.
                var level = GetNearestLevel(run.IsInclined ? baseZ : run.Start.Z);

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
                baseById[inst.Id] = baseZ;
                topById[inst.Id] = topZ;
                levelById[inst.Id] = level;
                createdIds?.Add(inst.Id);
            }

            // BoundingBox é adequada para vigas, mas não para Structural Columns:
            // algumas famílias de pilar incluem planos/simbologia com extensão enorme.
            // Colunas são posicionadas exclusivamente por nível e offsets em
            // SetColumnExtents; usar a caixa nelas pode deslocá-las centenas de metros.
            double maxBaseCorrectionMm = isFraming
                ? AlignPhysicalBases(baseById)
                : 0.0;

            // ── Parte 2: recuo W/2 nas pontas (distribuição em L_eixos = L − W) ───
            // Mede a largura REAL da seção pela BoundingBox (independe de parâmetro) e move
            // cada montante do offset bruto para o final, alinhando as faces externas.
            double W = 0;
            if (created.Count > 0)
            {
                _doc.Regenerate();
                // Mede somente na direção horizontal do percurso. Medir no vetor 3D
                // incluiria a altura do montante na projeção de um trecho inclinado.
                W = GeometryMeasure.ExtentAlongMm(_doc, created[0], run.HorizontalDirection);
            }

            double L = run.LengthMm;
            var axisOffsets = new List<double>(seg.PostOffsets);
            // Com recuo configurado, PostOffsets já contém os eixos finais exatos.
            // O ajuste automático por W/2 é mantido apenas no modo legado (recuo zero).
            double horizontalRatio = run.HorizontalLengthFt / run.LengthFt;
            double axisWidthMm = W / horizontalRatio;
            if (config.EndPostInset <= 1e-6 && axisWidthMm > 1e-6 && L > axisWidthMm)
            {
                double halfW = axisWidthMm / 2.0, lEixos = L - axisWidthMm;
                for (int i = 0; i < created.Count; i++)
                {
                    double newOff  = halfW + seg.PostOffsets[i] * lEixos / L;
                    axisOffsets[i] = newOff;
                    double deltaFt = (newOff - seg.PostOffsets[i]) / 304.8;
                    if (Math.Abs(deltaFt) > 1e-9)
                    {
                        if (isColumn)
                        {
                            // Move o pilar apenas em planta; a nova elevação é controlada
                            // pelos offsets do nível para evitar dupla soma de Z.
                            ElementTransformUtils.MoveElement(
                                _doc,
                                created[i],
                                run.HorizontalDirection * (deltaFt * horizontalRatio));

                            double shiftedBaseZ = baseById[created[i]] + run.Direction.Z * deltaFt;
                            double shiftedTopZ = topById[created[i]] + run.Direction.Z * deltaFt;
                            SetColumnExtents(
                                _doc.GetElement(created[i]) as FamilyInstance,
                                levelById[created[i]],
                                shiftedBaseZ,
                                shiftedTopZ);
                            baseById[created[i]] = shiftedBaseZ;
                            topById[created[i]] = shiftedTopZ;
                        }
                        else
                        {
                            ElementTransformUtils.MoveElement(_doc, created[i], run.Direction * deltaFt);
                        }
                    }
                }
            }
            seg.AxisOffsets = axisOffsets;   // InfillBuilder alinha travessas/quadros por estes eixos
            seg.PostWidthMm = W;             // largura na direção da linha (para faces das cantoneiras)

            string columnPosition = isColumn && created.Count > 0
                ? DescribeColumnPosition(
                    _doc.GetElement(created[0]) as FamilyInstance,
                    levelById[created[0]])
                : "";

            Log($"PostBuilder: {created.Count} montantes | W={W:F1}mm | inclinado={run.IsInclined} | " +
                $"correçãoZ={maxBaseCorrectionMm:F1}mm | categoria={(isColumn ? "Column" : "Framing")} | " +
                $"família={Path.GetFileNameWithoutExtension(config.PostFamilyPath)}{columnPosition}");
        }

        /// <summary>
        /// Move cada instância verticalmente para que o menor Z da geometria física
        /// coincida com a cota de base desejada. A medição após Regenerate considera
        /// origem interna da família, justificação e eventuais recuos automáticos.
        /// </summary>
        private double AlignPhysicalBases(IReadOnlyDictionary<ElementId, double> targetBaseById)
        {
            if (targetBaseById == null || targetBaseById.Count == 0) return 0;

            _doc.Regenerate();
            double maxCorrectionFt = 0;

            foreach (var pair in targetBaseById)
            {
                var element = _doc.GetElement(pair.Key);
                var box = element?.get_BoundingBox(null);
                if (box == null) continue;

                double deltaZ = pair.Value - box.Min.Z;
                maxCorrectionFt = Math.Max(maxCorrectionFt, Math.Abs(deltaZ));
                if (Math.Abs(deltaZ) > 1e-9)
                    ElementTransformUtils.MoveElement(_doc, pair.Key, XYZ.BasisZ * deltaZ);
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
            return RailFamilySymbolResolver.Resolve(_doc, path, typeName);
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
