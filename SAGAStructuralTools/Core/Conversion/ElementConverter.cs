using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SAGAStructuralTools.Core;
using SAGAStructuralTools.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Core.Conversion
{
    /// <summary>
    /// Substitui elementos IFC por famílias nativas Gerdau (FT04).
    /// Cada elemento é processado em sua própria Transaction para garantir
    /// que falhas individuais não interrompam o lote (rollback por elemento).
    /// </summary>
    public class ElementConverter
    {
        private readonly Document  _hostDoc;
        private readonly Document  _sourceDoc;
        private readonly Transform _linkTransform;

        public ElementConverter(Document hostDoc, Document sourceDoc = null, Transform linkTransform = null)
        {
            _hostDoc       = hostDoc;
            _sourceDoc     = sourceDoc ?? hostDoc;
            _linkTransform = linkTransform ?? Transform.Identity;
        }

        public List<ConversionResult> ConvertAll(
            IEnumerable<ElementId> elementIds,
            Func<string, bool, ProfileMapping> matchFunc,
            IProgress<string> progress)
        {
            var results = new List<ConversionResult>();

            foreach (var id in elementIds)
            {
                var element = _sourceDoc.GetElement(id);
                if (element == null) continue;

                var ifcName = element.Name;
                var catId   = element.Category?.Id.GetId();
                // OST_Columns = "Colunas" (como IfcColumn aparece em IFC vinculado no Revit)
                // OST_StructuralColumns = pilares estruturais nativos
                var isColumn = catId == (int)BuiltInCategory.OST_StructuralColumns
                            || catId == (int)BuiltInCategory.OST_Columns;
                var mapping  = matchFunc(ifcName, isColumn);

                if (mapping == null)
                {
                    results.Add(new ConversionResult
                    {
                        ElementId    = id.GetId(),
                        OriginalName = ifcName,
                        Status       = ConversionStatus.NotFound,
                        Message      = $"Perfil '{ifcName}' não encontrado no catálogo Gerdau."
                    });
                    progress?.Report($"[SKIP] {ifcName} — sem correspondência no catálogo");
                    continue;
                }

                using (var tx = new Transaction(_hostDoc, $"Converter {ifcName}"))
                {
                    try
                    {
                        // Suprime avisos geométricos do Revit (ex: "viga fora da linha central")
                        var failOpts = tx.GetFailureHandlingOptions();
                        failOpts.SetFailuresPreprocessor(new SuppressRevitWarnings());
                        tx.SetFailureHandlingOptions(failOpts);

                        tx.Start();
                        ConvertElement(element, mapping, isColumn);
                        tx.Commit();

                        results.Add(new ConversionResult
                        {
                            ElementId    = id.GetId(),
                            OriginalName = ifcName,
                            Status       = ConversionStatus.Success,
                            Message      = $"Convertido → '{mapping.GerdauName}'"
                        });
                        progress?.Report($"[OK] {ifcName} → {mapping.GerdauName}");
                    }
                    catch (Exception ex)
                    {
                        if (tx.GetStatus() == TransactionStatus.Started)
                            tx.RollBack();

                        results.Add(new ConversionResult
                        {
                            ElementId    = id.GetId(),
                            OriginalName = ifcName,
                            Status       = ConversionStatus.GeometryError,
                            Message      = ex.Message
                        });
                        progress?.Report($"[ERRO] {ifcName} — {ex.Message}");
                    }
                }
            }

            return results;
        }

        private void ConvertElement(Element source, ProfileMapping mapping, bool isColumn)
        {
            // Extrai o eixo do elemento (LocationCurve para nativos, bounding box para DirectShape do IFC)
            var curveInSourceSpace = GetElementCurve(source);

            // Converte do espaço do documento vinculado para o espaço do host
            var curve = _linkTransform.IsIdentity
                ? curveInSourceSpace
                : curveInSourceSpace.CreateTransformed(_linkTransform);

            var symbol = GetOrLoadFamilySymbol(mapping, isColumn);
            if (!symbol.IsActive)
                symbol.Activate();

            // Pilares exigem que a curva vá de base (Z menor) para topo (Z maior)
            if (isColumn)
            {
                var p0 = curve.GetEndPoint(0);
                var p1 = curve.GetEndPoint(1);
                if (p0.Z > p1.Z)
                    curve = Line.CreateBound(p1, p0);
            }

            var level      = GetElementLevel(source);
            var structType = isColumn ? StructuralType.Column : StructuralType.Beam;
            _hostDoc.Create.NewFamilyInstance(curve, symbol, level, structType);

            // Elementos de IFC vinculado não podem ser ocultados diretamente no host.
            // Para elementos nativos no host doc, oculta normalmente.
            if (ReferenceEquals(_sourceDoc, _hostDoc))
                _hostDoc.ActiveView.HideElements(new List<ElementId> { source.Id });
        }

        private static Curve GetElementCurve(Element source)
        {
            // Caso 1: elementos nativos do Revit possuem LocationCurve
            if (source.Location is LocationCurve lc)
                return lc.Curve;

            // Caso 2: DirectShape do IFC — extrai o eixo real pela geometria sólida.
            // Para uma extrusão prismática (qualquer inclinação), as duas faces com menor
            // área são as faces de extremidade. O centroide de cada uma dá início e fim reais.
            var solidCurve = TryGetCurveFromSolid(source);
            if (solidCurve != null) return solidCurve;

            // Caso 3: fallback — bounding box (só correto para membros axis-aligned)
            return GetCurveFromBoundingBox(source);
        }

        private static Curve TryGetCurveFromSolid(Element source)
        {
            try
            {
                var options = new Options
                {
                    ComputeReferences = false,
                    DetailLevel       = ViewDetailLevel.Medium
                };

                Solid solid = null;
                foreach (var obj in source.get_Geometry(options))
                {
                    if (obj is Solid s && s.Volume > 0)
                    {
                        solid = s;
                        break;
                    }
                    if (obj is GeometryInstance gi)
                    {
                        foreach (var inner in gi.GetInstanceGeometry())
                        {
                            if (inner is Solid s2 && s2.Volume > 0)
                            {
                                solid = s2;
                                break;
                            }
                        }
                        if (solid != null) break;
                    }
                }

                if (solid == null) return null;

                // Ordena as faces por área e pega as 2 menores: são as faces de extremidade
                var endFaces = solid.Faces
                    .Cast<Face>()
                    .OrderBy(f => f.Area)
                    .Take(2)
                    .ToList();

                if (endFaces.Count < 2) return null;

                var p1 = FaceCentroid(endFaces[0]);
                var p2 = FaceCentroid(endFaces[1]);

                if (p1.DistanceTo(p2) < 1e-6) return null;

                return Line.CreateBound(p1, p2);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Calcula o centroide de uma face usando os pontos iniciais de cada aresta do loop externo.
        /// Para perfis estruturais (faces planas com arestas retas), é equivalente à média dos vértices.
        /// </summary>
        private static XYZ FaceCentroid(Face face)
        {
            var loops = face.GetEdgesAsCurveLoops();
            if (loops == null || loops.Count == 0) return XYZ.Zero;

            var sum   = XYZ.Zero;
            int count = 0;

            // Usa apenas o loop externo (índice 0) — loops internos seriam furos
            foreach (var curve in loops[0])
            {
                sum += curve.GetEndPoint(0);
                count++;
            }

            return count > 0 ? sum * (1.0 / count) : XYZ.Zero;
        }

        private static Curve GetCurveFromBoundingBox(Element source)
        {
            var bb = source.get_BoundingBox(null);
            if (bb == null)
                throw new InvalidOperationException("Elemento sem geometria válida.");

            var min    = bb.Min;
            var max    = bb.Max;
            var center = (min + max) * 0.5;

            double dx = max.X - min.X;
            double dy = max.Y - min.Y;
            double dz = max.Z - min.Z;

            XYZ start, end;
            if (dx >= dy && dx >= dz)
            {
                start = new XYZ(min.X, center.Y, center.Z);
                end   = new XYZ(max.X, center.Y, center.Z);
            }
            else if (dy >= dx && dy >= dz)
            {
                start = new XYZ(center.X, min.Y, center.Z);
                end   = new XYZ(center.X, max.Y, center.Z);
            }
            else
            {
                start = new XYZ(center.X, center.Y, min.Z);
                end   = new XYZ(center.X, center.Y, max.Z);
            }

            if (start.DistanceTo(end) < 1e-6)
                throw new InvalidOperationException("Bounding box degenerada.");

            return Line.CreateBound(start, end);
        }

        private FamilySymbol GetOrLoadFamilySymbol(ProfileMapping mapping, bool isColumn)
        {
            var typeName     = mapping.FamilyType ?? mapping.GerdauName;
            var searchCat    = isColumn
                ? BuiltInCategory.OST_StructuralColumns
                : BuiltInCategory.OST_StructuralFraming;

            // Precisa ser AND: família correta E tipo específico.
            // OR causava retorno do primeiro tipo qualquer da família (bug de perfil único).
            var existing = new FilteredElementCollector(_hostDoc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(searchCat)
                .Cast<FamilySymbol>()
                .FirstOrDefault(sym =>
                    sym.Family.Name.Equals(mapping.GerdauName, StringComparison.OrdinalIgnoreCase) &&
                    sym.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

            if (existing != null) return existing;

            if (!_hostDoc.LoadFamilySymbol(mapping.FamilyPath, typeName, out var loaded))
                throw new InvalidOperationException($"Falha ao carregar '{mapping.FamilyPath}'.");

            return loaded;
        }

        private Level GetElementLevel(Element source)
        {
            // Tenta obter o nível associado ao próprio elemento
            if (source.LevelId != ElementId.InvalidElementId)
            {
                var level = _hostDoc.GetElement(source.LevelId) as Level;
                if (level != null) return level;
            }

            // Fallback: nível mais baixo do host
            return new FilteredElementCollector(_hostDoc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .FirstOrDefault()
                ?? throw new InvalidOperationException("Nenhum Level encontrado no documento.");
        }
    }

    /// <summary>
    /// Suprime avisos do Revit durante as transações de conversão.
    /// Ex: "viga ligeiramente fora da linha central" — aviso geométrico não-fatal.
    /// </summary>
    internal sealed class SuppressRevitWarnings : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            foreach (var msg in failuresAccessor.GetFailureMessages())
            {
                if (msg.GetSeverity() == FailureSeverity.Warning)
                    failuresAccessor.DeleteWarning(msg);
            }
            return FailureProcessingResult.Continue;
        }
    }
}
