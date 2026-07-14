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

        public void Build(RailSegment seg, RailConfig config, XYZ lineStart, XYZ lineEnd, double? railAxisZ = null)
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
            // Preferimos o eixo medido pela BoundingBox do corrimão (railAxisZ); se ausente,
            // fallback para (HandrailHeight − R) lido por parâmetro. Nunca ultrapassa o eixo.
            double limitZ = railAxisZ ??
                            (lineStart.Z + (config.HandrailHeight - GetHandrailHalfHeightMm(config)) / 304.8);
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
                    inst = _doc.Create.NewFamilyInstance(basePt, symbol, level, StructuralType.Column);
                    SetColumnExtents(inst, level, baseZ, topZ);
                }

                ApplyRotation(inst, config.PostRotation, isColumn, basePt);
                created.Add(inst.Id);
            }

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
            if (W > 1e-6 && L > W)
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

            Log($"PostBuilder: {created.Count} montantes | W={W:F1}mm | topZ={(topZ - lineStart.Z) * 304.8:F0}mm | família={Path.GetFileNameWithoutExtension(config.PostFamilyPath)}");
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

        /// <summary>Meia-altura (mm) da seção do corrimão (fallback por parâmetro). 0 se indisponível.</summary>
        private double GetHandrailHalfHeightMm(RailConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.HandrailFamilyPath)) return 0;
            try
            {
                var hs = GetOrLoadSymbol(config.HandrailFamilyPath, config.HandrailFamilyType);
                return SectionSize.SectionHeightMm(hs) / 2.0;
            }
            catch { return 0; }
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
            var topLevel = inst.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
            if (topLevel != null && !topLevel.IsReadOnly) topLevel.Set(level.Id);

            var baseOff = inst.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
            if (baseOff != null && !baseOff.IsReadOnly) baseOff.Set(baseZFt - level.Elevation);

            var topOff = inst.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
            if (topOff != null && !topOff.IsReadOnly) topOff.Set(topZFt - level.Elevation);
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
