using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SAGAStructuralTools.Core.Rail;
using System;

namespace SAGAStructuralTools.Core.Alignment
{
    /// <summary>
    /// Interrompe uma viga em duas no ponto de interseção com o eixo de uma viga
    /// de referência. A instância original vira a primeira metade (início até o
    /// corte); uma nova instância, com os mesmos parâmetros de posicionamento,
    /// vira a segunda metade (corte até o fim original).
    /// </summary>
    internal static class SplitBeamService
    {
        private const double MillimetersPerFoot = 304.8;
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
            var direction = (end - start).Normalize();
            double alongFt = (splitPoint - start).DotProduct(direction);

            double marginFt = MinEdgeMarginMm / MillimetersPerFoot;
            if (alongFt <= marginFt || alongFt >= lengthFt - marginFt)
                throw new InvalidOperationException(
                    "O ponto de interseção fica fora do vão (ou perto demais da ponta) da viga selecionada.");

            var instance = target.Instance;
            var symbol = instance.Symbol;
            var level = document.GetElement(instance.LevelId) as Level
                ?? throw new InvalidOperationException("Não foi possível identificar o nível da viga selecionada.");

            // Primeira metade: reaproveita a instância original — só a ponta distante
            // (índice 1, o "fim" do eixo) recua até o ponto de corte.
            target.DisallowJoinAtEnd(1);
            target.SetCornerEndpoint(1, splitPoint);

            // Segunda metade: nova instância do corte até a ponta original, com os
            // mesmos parâmetros de justificação/rotação da original.
            var newInstance = document.Create.NewFamilyInstance(
                Line.CreateBound(splitPoint, end), symbol, level, StructuralType.Beam);
            if (newInstance == null)
                throw new InvalidOperationException("O Revit não aceitou criar a segunda metade da viga.");

            RoundedCornerService.CopyPlacementParameters(instance, newInstance);
            try { StructuralFramingUtils.DisallowJoinAtEnd(newInstance, 0); }
            catch { /* nem toda família suporta join */ }
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
