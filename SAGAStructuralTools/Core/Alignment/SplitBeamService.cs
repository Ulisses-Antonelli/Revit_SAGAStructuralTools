using Autodesk.Revit.DB;
using SAGAStructuralTools.Core.Rail;
using System;

namespace SAGAStructuralTools.Core.Alignment
{
    /// <summary>
    /// Interrompe uma viga em duas no ponto de interseção com o eixo de uma viga
    /// de referência, usando o método nativo FamilyInstance.Split — o Revit cuida
    /// de toda a duplicação de parâmetros e junta internamente, dispensando
    /// reimplementar isso na mão.
    /// </summary>
    internal static class SplitBeamService
    {
        private const double MinEdgeMarginMm = 50.0;

        internal static void Split(Document document, ElementId referenceId, ElementId targetId)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (referenceId.Equals(targetId))
                throw new InvalidOperationException("Selecione a viga de referência e a viga a dividir separadamente.");

            var reference = RoundedCornerMember.Get(document, referenceId, "referência");
            var target = RoundedCornerMember.Get(document, targetId, "viga a dividir");
            if (!target.IsBeam)
                throw new InvalidOperationException("Só é possível dividir vigas estruturais retas (Quadro Estrutural).");

            var referenceAxis = reference.GetAxis();
            var targetAxis = target.GetAxis();

            // O que importa é o plano vertical de cada eixo, não o eixo 3D exato:
            // vigas de alturas diferentes alinhadas pela face superior têm eixos em
            // cotas distintas e nunca coincidem em 3D — só em planta. A cota do corte
            // vem da própria viga alvo, interpolada no eixo real dela (cobre inclinação).
            var splitPoint = IntersectInPlan(targetAxis, referenceAxis);

            var start = targetAxis.GetEndPoint(0);
            var end = targetAxis.GetEndPoint(1);
            double lengthFt = start.DistanceTo(end);
            double alongFt = (splitPoint - start).DotProduct((end - start).Normalize());

            SagaLog.Write(
                $"SplitBeamService: start=({start.X:F2},{start.Y:F2},{start.Z:F2}) " +
                $"end=({end.X:F2},{end.Y:F2},{end.Z:F2}) lengthMm={lengthFt * 304.8:F1} " +
                $"splitPoint=({splitPoint.X:F2},{splitPoint.Y:F2},{splitPoint.Z:F2}) " +
                $"alongMm={alongFt * 304.8:F1}");

            double marginFt = MinEdgeMarginMm / 304.8;
            if (alongFt <= marginFt || alongFt >= lengthFt - marginFt)
                throw new InvalidOperationException(
                    "O ponto de interseção fica fora do vão (ou perto demais da ponta) da viga selecionada.");

            double normalizedParam = alongFt / lengthFt;

            var instance = target.Instance;
            if (!instance.CanSplit)
                throw new InvalidOperationException("O Revit não permite dividir esta viga.");

            var newId = instance.Split(normalizedParam);
            if (newId == null || newId == ElementId.InvalidElementId)
                throw new InvalidOperationException("O Revit não conseguiu dividir a viga.");
        }

        /// <summary>
        /// Ponto de corte na viga alvo, achado pela interseção das PROJEÇÕES EM PLANTA
        /// (X,Y) dos dois eixos — não pelo cruzamento exato em 3D, que normalmente não
        /// existe entre vigas de alturas diferentes alinhadas pela face superior. A
        /// cota (Z) do ponto vem da interpolação ao longo do eixo 3D real da própria
        /// viga alvo, então funciona também se ela for inclinada.
        /// </summary>
        private static XYZ IntersectInPlan(Line targetLine, Line referenceLine)
        {
            var p0 = targetLine.GetEndPoint(0);
            var p1 = targetLine.GetEndPoint(1);
            var q0 = referenceLine.GetEndPoint(0);
            var q1 = referenceLine.GetEndPoint(1);

            double denom = (p0.X - p1.X) * (q0.Y - q1.Y) - (p0.Y - p1.Y) * (q0.X - q1.X);
            if (Math.Abs(denom) < 1e-9)
                throw new InvalidOperationException(
                    "Os eixos das duas vigas são paralelos em planta e não se cruzam.");

            double t = ((p0.X - q0.X) * (q0.Y - q1.Y) - (p0.Y - q0.Y) * (q0.X - q1.X)) / denom;
            return p0 + (p1 - p0) * t;
        }
    }
}
