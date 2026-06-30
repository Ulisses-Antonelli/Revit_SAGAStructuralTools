using System;

namespace SAGAStructuralTools.Core.Domain
{
    /// <summary>
    /// Regra de Blondel: 2h + p = 63~64 cm.
    /// Ajusta a pisada para um valor confortável dado a altura do espelho.
    /// Sem dependência de Revit.
    /// </summary>
    public static class BlondelRule
    {
        private const double BlondelMin = 630; // mm
        private const double BlondelMax = 640; // mm

        /// <summary>
        /// Retorna a pisada ideal para satisfazer Blondel dado o espelho calculado.
        /// </summary>
        public static double IdealTread(double riserHeight)
            => 640 - 2 * riserHeight;

        /// <summary>
        /// Verifica se a combinação espelho+pisada satisfaz Blondel.
        /// </summary>
        public static bool Satisfies(double riserHeight, double treadDepth)
        {
            var result = 2 * riserHeight + treadDepth;
            return result >= BlondelMin && result <= BlondelMax;
        }

        /// <summary>
        /// Ajusta o número de degraus para que Blondel seja satisfeito,
        /// mantendo a pisada padrão ou ajustando levemente.
        /// Retorna o melhor par (nDegraus, espelho) encontrado.
        /// </summary>
        public static (int steps, double riser, double tread) BestFit(
            double totalRise, double preferredTread)
        {
            var bestSteps  = (int)Math.Round(totalRise / StairDefaults.TargetRiserHeight);
            var bestRiser  = totalRise / bestSteps;
            var bestTread  = preferredTread;
            var bestDelta  = double.MaxValue;

            // Testa ±3 degraus em torno do valor central
            for (int n = bestSteps - 3; n <= bestSteps + 3; n++)
            {
                if (n < 2) continue;
                var riser = totalRise / n;
                var tread = IdealTread(riser);
                var delta = Math.Abs(tread - preferredTread);

                if (Satisfies(riser, tread) && delta < bestDelta)
                {
                    bestDelta = delta;
                    bestSteps = n;
                    bestRiser = riser;
                    bestTread = tread;
                }
            }

            return (bestSteps, bestRiser, bestTread);
        }
    }
}
