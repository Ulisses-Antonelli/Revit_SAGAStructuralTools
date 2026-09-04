using Autodesk.Revit.DB;
using SAGAStructuralTools.Core.Rail;
using System;

namespace SAGAStructuralTools.Core.Alignment
{
    /// <summary>
    /// Interrompe uma viga ou pilar reto em dois no ponto de interseção com o eixo
    /// de um elemento de referência (viga ou pilar), usando o método nativo
    /// FamilyInstance.Split — o Revit cuida de toda a duplicação de parâmetros e
    /// junta internamente, dispensando reimplementar isso na mão. A API do Revit
    /// já documenta Split como válido para vigas, contraventamentos e pilares
    /// (arquitetônicos e estruturais), então não há razão pra restringir isso
    /// artificialmente a vigas.
    /// </summary>
    internal static class SplitMemberService
    {
        private const double MinEdgeMarginMm = 50.0;

        internal static void Split(Document document, ElementId referenceId, ElementId targetId)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (referenceId.Equals(targetId))
                throw new InvalidOperationException("Selecione o elemento de referência e o elemento a dividir separadamente.");

            var reference = RoundedCornerMember.Get(document, referenceId, "referência");
            var target = RoundedCornerMember.Get(document, targetId, "a dividir");

            var referenceAxis = reference.GetAxis();
            var targetAxis = target.GetAxis();

            // O que importa é o plano vertical de cada eixo, não o eixo 3D exato:
            // vigas de alturas diferentes alinhadas pela face superior têm eixos em
            // cotas distintas e nunca coincidem em 3D — só em planta. A cota do corte
            // vem do próprio eixo real do elemento a dividir, interpolada nele
            // (cobre inclinação e também o caso de um pilar vertical, cujo eixo é
            // vertical e não tem projeção em planta).
            var splitPoint = IntersectAxes(targetAxis, referenceAxis);

            var start = targetAxis.GetEndPoint(0);
            var end = targetAxis.GetEndPoint(1);
            double lengthFt = start.DistanceTo(end);
            double alongFt = (splitPoint - start).DotProduct((end - start).Normalize());

            SagaLog.Write(
                $"SplitMemberService: start=({start.X:F2},{start.Y:F2},{start.Z:F2}) " +
                $"end=({end.X:F2},{end.Y:F2},{end.Z:F2}) lengthMm={lengthFt * 304.8:F1} " +
                $"splitPoint=({splitPoint.X:F2},{splitPoint.Y:F2},{splitPoint.Z:F2}) " +
                $"alongMm={alongFt * 304.8:F1}");

            double marginFt = MinEdgeMarginMm / 304.8;
            if (alongFt <= marginFt || alongFt >= lengthFt - marginFt)
                throw new InvalidOperationException(
                    "O ponto de interseção fica fora do vão (ou perto demais da ponta) do elemento selecionado.");

            double normalizedParam = alongFt / lengthFt;

            var instance = target.Instance;
            if (!instance.CanSplit)
                throw new InvalidOperationException("O Revit não permite dividir este elemento.");

            var newId = instance.Split(normalizedParam);
            if (newId == null || newId == ElementId.InvalidElementId)
                throw new InvalidOperationException("O Revit não conseguiu dividir o elemento.");
        }

        private const double PlanDegenerateToleranceSqFt = 1e-9;

        /// <summary>
        /// Ponto de corte no eixo do elemento-alvo. Normalmente é a interseção das
        /// PROJEÇÕES EM PLANTA (X,Y) dos dois eixos — não o cruzamento exato em 3D,
        /// que geralmente não existe entre vigas de alturas diferentes alinhadas
        /// pela face superior. Mas um pilar vertical (baseado em ponto ou em linha
        /// vertical) projeta em planta como um PONTO, não uma reta — não dá pra
        /// cruzar duas retas quando uma delas é degenerada, então esse caso vira uma
        /// projeção de ponto sobre a reta do outro eixo. Se os dois eixos forem
        /// verticais, não existe um cruzamento em planta bem definido.
        /// </summary>
        private static XYZ IntersectAxes(Line targetLine, Line referenceLine)
        {
            var p0 = targetLine.GetEndPoint(0);
            var p1 = targetLine.GetEndPoint(1);
            var q0 = referenceLine.GetEndPoint(0);
            var q1 = referenceLine.GetEndPoint(1);

            double targetDx = p1.X - p0.X, targetDy = p1.Y - p0.Y;
            double referenceDx = q1.X - q0.X, referenceDy = q1.Y - q0.Y;
            bool targetIsVerticalInPlan = (targetDx * targetDx + targetDy * targetDy) < PlanDegenerateToleranceSqFt;
            bool referenceIsVerticalInPlan = (referenceDx * referenceDx + referenceDy * referenceDy) < PlanDegenerateToleranceSqFt;

            if (targetIsVerticalInPlan && referenceIsVerticalInPlan)
                throw new InvalidOperationException(
                    "Os dois elementos são verticais (pilares) e não têm um cruzamento em planta bem definido.");

            if (targetIsVerticalInPlan)
            {
                // Alvo é um pilar vertical: a posição em planta já é fixa (a do
                // próprio pilar) — falta achar em que altura o eixo de referência
                // passa por ali, projetando o ponto do pilar sobre a reta de
                // referência.
                double s = ProjectPointParameter(p0.X, p0.Y, q0, referenceDx, referenceDy);
                double z = q0.Z + s * (q1.Z - q0.Z);
                return new XYZ(p0.X, p0.Y, z);
            }

            if (referenceIsVerticalInPlan)
            {
                // Referência é um pilar vertical: acha em que ponto do eixo real do
                // alvo (viga ou pilar inclinado) a posição em planta do pilar é
                // cruzada, projetando o ponto do pilar sobre a reta do alvo.
                double t = ProjectPointParameter(q0.X, q0.Y, p0, targetDx, targetDy);
                return p0 + (p1 - p0) * t;
            }

            double denom = targetDx * referenceDy - targetDy * referenceDx;
            if (Math.Abs(denom) < 1e-9)
                throw new InvalidOperationException(
                    "Os eixos dos dois elementos são paralelos em planta e não se cruzam.");

            double tLine = ((q0.X - p0.X) * referenceDy - (q0.Y - p0.Y) * referenceDx) / denom;
            return p0 + (p1 - p0) * tLine;
        }

        /// <summary>Parâmetro (0..1, não limitado) do ponto (px,py) projetado sobre a
        /// reta que passa por origin com direção (dirX,dirY), em planta.</summary>
        private static double ProjectPointParameter(double px, double py, XYZ origin, double dirX, double dirY)
        {
            double denom = dirX * dirX + dirY * dirY;
            return ((px - origin.X) * dirX + (py - origin.Y) * dirY) / denom;
        }
    }
}
