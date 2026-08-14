using Autodesk.Revit.DB;
using SAGAStructuralTools.Core.Rail;
using System;

namespace SAGAStructuralTools.Core.Alignment
{
    /// <summary>
    /// Estica a extremidade mais próxima de um terceiro elemento até o ponto de
    /// trabalho (work point) de duas vigas/pilares que se cruzam — sem transladar
    /// a peça inteira. Uso típico: alinhar uma cantoneira de contraventamento ao
    /// cruzamento de duas vigas, mantendo o eixo dela intacto na outra ponta.
    /// </summary>
    internal static class WorkPointAlignmentService
    {
        private const double MillimetersPerFoot = 304.8;
        private const double AxisIntersectionToleranceMm = 0.5;

        internal static void Align(Document document, ElementId firstId, ElementId secondId, ElementId targetId)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (firstId.Equals(secondId) || firstId.Equals(targetId) || secondId.Equals(targetId))
                throw new InvalidOperationException("Selecione três elementos diferentes.");

            var first  = RoundedCornerMember.Get(document, firstId, "primeiro");
            var second = RoundedCornerMember.Get(document, secondId, "segundo");
            var target = RoundedCornerMember.Get(document, targetId, "elemento a alinhar");

            var workPoint = ComputeWorkPoint(first.GetAxis(), second.GetAxis());

            var targetAxis = target.GetAxis();
            int end = NearestEnd(targetAxis, workPoint);

            target.EnsureCornerEndEditable(end);
            target.DisallowJoinAtEnd(end);
            target.SetCornerEndpoint(end, workPoint);
        }

        /// <summary>
        /// Ponto médio entre os pontos mais próximos das duas retas infinitas dos eixos.
        /// Lança erro claro se os eixos estiverem afastados além da tolerância (não se cruzam de fato).
        /// </summary>
        private static XYZ ComputeWorkPoint(Line first, Line second)
        {
            var p0 = first.GetEndPoint(0);
            var d1 = first.Direction;
            var q0 = second.GetEndPoint(0);
            var d2 = second.Direction;

            double b = d1.DotProduct(d2);
            double denom = 1.0 - b * b;
            var r = p0 - q0;
            double d = d1.DotProduct(r);
            double e = d2.DotProduct(r);

            if (Math.Abs(denom) < 1e-9)
                throw new InvalidOperationException(
                    "Os eixos do primeiro e do segundo elemento são paralelos e não formam um ponto de trabalho.");

            double t = (b * e - d) / denom;
            double s = (e - b * d) / denom;
            var pointOnFirst  = p0 + d1 * t;
            var pointOnSecond = q0 + d2 * s;

            double distanceFt = pointOnFirst.DistanceTo(pointOnSecond);
            double toleranceFt = AxisIntersectionToleranceMm / MillimetersPerFoot;
            if (distanceFt > toleranceFt)
                throw new InvalidOperationException(
                    $"Os eixos do primeiro e do segundo elemento não se encontram " +
                    $"({distanceFt * MillimetersPerFoot:F1} mm de afastamento). Selecione elementos que se cruzem.");

            return (pointOnFirst + pointOnSecond) * 0.5;
        }

        private static int NearestEnd(Line line, XYZ point)
        {
            return line.GetEndPoint(0).DistanceTo(point) <= line.GetEndPoint(1).DistanceTo(point) ? 0 : 1;
        }
    }
}
