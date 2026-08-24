using Autodesk.Revit.DB;
using SAGAStructuralTools.Core.Rail;
using System;

namespace SAGAStructuralTools.Core.Alignment
{
    /// <summary>
    /// Estende de verdade o eixo de um componente (LocationCurve, ou nível/offset em
    /// pilares por ponto — via RoundedCornerMember) até uma referência-alvo: face plana,
    /// linha/eixo de outro elemento, ou linha de modelo/referência.
    ///
    /// Diferente do Alinhar nativo do Revit: quando o componente está juntado (join
    /// automático) a outro membro, arrastar a seta de extensão só ajusta o recuo visual
    /// da junta — o eixo real (LocationCurve) não muda. Aqui o eixo é recalculado e
    /// aplicado de fato, e a junta automática é desligada na ponta afetada.
    /// </summary>
    internal static class RealAlignmentService
    {
        private const double MillimetersPerFoot = 304.8;
        private const double MinNormalAlignment = 0.9; // ~25,8° de tolerância

        internal static void Align(Document document, ElementId targetId, ElementId sourceId)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (targetId.Equals(sourceId))
                throw new InvalidOperationException("Selecione a referência e o componente separadamente.");

            var source = RoundedCornerMember.Get(document, sourceId, "componente");
            var sourceAxis = source.GetAxis();

            var targetElement = document.GetElement(targetId);
            if (targetElement == null)
                throw new InvalidOperationException("A referência selecionada não existe mais no documento.");

            // Estimativa grosseira de qual ponta se move, usada só para desempatar
            // entre faces candidatas quando o alvo tem mais de uma face perpendicular
            // ao eixo (ex.: as duas faces de topo de uma laje).
            var roughTargetPoint = RoughCenter(targetElement);
            int movingEnd = NearestEnd(sourceAxis, roughTargetPoint);
            var movingPoint = sourceAxis.GetEndPoint(movingEnd);

            XYZ newPoint = ResolveNewPoint(document, targetElement, sourceAxis, movingPoint);

            source.EnsureCornerEndEditable(movingEnd);
            source.DisallowJoinAtEnd(movingEnd);
            source.SetCornerEndpoint(movingEnd, newPoint);
            document.Regenerate();

            double toleranceFt = Math.Max(0.1 / MillimetersPerFoot, document.Application.VertexTolerance);
            double differenceFt = source.GetAxis().GetEndPoint(movingEnd).DistanceTo(newPoint);
            if (differenceFt > toleranceFt)
                throw new InvalidOperationException(
                    $"O Revit não posicionou a extremidade na referência " +
                    $"({differenceFt * MillimetersPerFoot:F1} mm de diferença). Verifique as restrições do elemento.");
        }

        private static XYZ ResolveNewPoint(Document document, Element targetElement, Line sourceAxis, XYZ movingPoint)
        {
            // 1) Linha de modelo/referência
            if (targetElement is CurveElement curveElement)
            {
                if (curveElement.GeometryCurve is Line targetLine)
                    return ClosestPointOnLineToLine(sourceAxis, targetLine);
                throw new InvalidOperationException(
                    "A linha de referência selecionada não é reta. Use uma linha reta.");
            }

            // 2) Eixo de outra viga/pilar (reaproveita RoundedCornerMember, que já
            // reconstrói o eixo de pilares por nível/offset quando necessário).
            if (RoundedCornerMember.IsSelectable(targetElement))
            {
                var targetMember = RoundedCornerMember.Get(document, targetElement.Id, "referência");
                return ClosestPointOnLineToLine(sourceAxis, targetMember.GetAxis());
            }

            // 3) Face plana mais perpendicular ao eixo, mais próxima da ponta que se move
            var face = FindBestStopFace(targetElement, sourceAxis, movingPoint)
                ?? throw new InvalidOperationException(
                    "Não há uma face plana utilizável (perpendicular ao eixo do componente) " +
                    "no elemento de referência selecionado.");

            var plane = Plane.CreateByNormalAndOrigin(face.FaceNormal, face.Origin);
            return IntersectLineWithPlane(sourceAxis, plane)
                ?? throw new InvalidOperationException(
                    "O eixo do componente é paralelo à face de referência — não há onde estender.");
        }

        internal static PlanarFace FindBestStopFace(Element element, Line axis, XYZ movingPoint)
        {
            var options = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
            var geometry = element.get_Geometry(options);
            if (geometry == null) return null;

            PlanarFace best = null;
            double bestDistance = double.MaxValue;

            foreach (var solid in Solids(geometry))
            {
                foreach (Face face in solid.Faces)
                {
                    if (!(face is PlanarFace planar)) continue;

                    double alignment = Math.Abs(planar.FaceNormal.Normalize().DotProduct(axis.Direction));
                    if (alignment < MinNormalAlignment) continue;

                    var intersection = IntersectLineWithPlane(
                        axis, Plane.CreateByNormalAndOrigin(planar.FaceNormal, planar.Origin));
                    if (intersection == null) continue;

                    double distance = intersection.DistanceTo(movingPoint);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = planar;
                    }
                }
            }

            return best;
        }

        private static System.Collections.Generic.IEnumerable<Solid> Solids(GeometryElement geometry)
        {
            foreach (var obj in geometry)
            {
                if (obj is Solid solid && solid.Volume > 1e-9)
                {
                    yield return solid;
                }
                else if (obj is GeometryInstance gi)
                {
                    foreach (var inner in Solids(gi.GetInstanceGeometry()))
                        yield return inner;
                }
            }
        }

        internal static XYZ IntersectLineWithPlane(Line line, Plane plane)
        {
            double denom = plane.Normal.DotProduct(line.Direction);
            if (Math.Abs(denom) < 1e-9) return null;

            double t = plane.Normal.DotProduct(plane.Origin - line.GetEndPoint(0)) / denom;
            return line.GetEndPoint(0) + line.Direction * t;
        }

        /// <summary>Ponto no eixo <paramref name="line"/> (reta infinita) mais próximo da reta infinita <paramref name="other"/>.</summary>
        private static XYZ ClosestPointOnLineToLine(Line line, Line other)
        {
            var p0 = line.GetEndPoint(0);
            var d1 = line.Direction;
            var q0 = other.GetEndPoint(0);
            var d2 = other.Direction;

            double b = d1.DotProduct(d2);
            double denom = 1.0 - b * b;
            var r = p0 - q0;
            double d = d1.DotProduct(r);
            double e = d2.DotProduct(r);

            if (Math.Abs(denom) < 1e-9)
            {
                // Eixos paralelos: usa a projeção de q0 sobre a reta de origem.
                return p0 - d1 * d;
            }

            double t = (b * e - d) / denom;
            return p0 + d1 * t;
        }

        private static XYZ RoughCenter(Element element)
        {
            if (element.Location is LocationCurve lc)
                return (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) * 0.5;
            if (element.Location is LocationPoint lp)
                return lp.Point;

            var bb = element.get_BoundingBox(null);
            if (bb != null) return (bb.Min + bb.Max) * 0.5;

            throw new InvalidOperationException("Não foi possível localizar a geometria da referência selecionada.");
        }

        private static int NearestEnd(Line line, XYZ point)
        {
            return line.GetEndPoint(0).DistanceTo(point) <= line.GetEndPoint(1).DistanceTo(point) ? 0 : 1;
        }
    }
}
