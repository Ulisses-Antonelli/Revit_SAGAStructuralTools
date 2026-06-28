using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
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
                var catId   = element.Category?.Id.IntegerValue;
                // OST_Columns = "Colunas" (como IfcColumn aparece em IFC vinculado no Revit)
                // OST_StructuralColumns = pilares estruturais nativos
                var isColumn = catId == (int)BuiltInCategory.OST_StructuralColumns
                            || catId == (int)BuiltInCategory.OST_Columns;
                var mapping  = matchFunc(ifcName, isColumn);

                if (mapping == null)
                {
                    results.Add(new ConversionResult
                    {
                        ElementId    = id.IntegerValue,
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
                        tx.Start();
                        ConvertElement(element, mapping, isColumn);
                        tx.Commit();

                        results.Add(new ConversionResult
                        {
                            ElementId    = id.IntegerValue,
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
                            ElementId    = id.IntegerValue,
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

            var level      = GetElementLevel(source);
            // IfcColumn → StructuralType.Column | IfcMember (qualquer inclinação) → StructuralType.Beam
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

            // Caso 2: DirectShape do IFC não tem LocationCurve — extrai o eixo pelo bounding box.
            // Identifica a dimensão mais longa como direção do membro.
            var bb = source.get_BoundingBox(null);
            if (bb == null)
                throw new InvalidOperationException("Elemento sem LocationCurve nem bounding box.");

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
                throw new InvalidOperationException("Bounding box degenerada — elemento muito pequeno.");

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
}
