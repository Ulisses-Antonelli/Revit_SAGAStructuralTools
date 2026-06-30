using SAGAStructuralTools.Core.Models;

namespace SAGAStructuralTools.Core.Domain
{
    /// <summary>
    /// Calcula patamares (extremidades e intermediário).
    /// Sem dependência de Revit.
    /// </summary>
    public static class LandingCalculator
    {
        /// <summary>
        /// Calcula a profundidade dos patamares inferior e superior.
        /// Centraliza a escada quando configurado.
        /// </summary>
        public static (double lower, double upper) EndLandings(
            double totalRun, double beamDistance, StairConfig config)
        {
            var gap = beamDistance - totalRun;
            if (gap <= 0) return (0, 0);

            if (config.CenterStair)
            {
                var each = gap / 2.0;
                return (each, each);
            }
            return (gap, 0); // patamar apenas na base
        }

        /// <summary>
        /// Verifica se a altura total exige patamar intermediário conforme norma.
        /// </summary>
        public static bool NeedsIntermediate(double totalRise, StairConfig config)
        {
            if (config.IntermediateLanding == LandingMode.Always) return true;
            if (config.IntermediateLanding == LandingMode.Never)  return false;
            return totalRise > config.MaxHeightWithoutLanding; // Auto
        }

        /// <summary>
        /// Posição do patamar intermediário (a partir da base, em mm).
        /// Posicionado o mais próximo possível do meio.
        /// </summary>
        public static double IntermediatePosition(int stepCount, double riserHeight)
        {
            var halfSteps = stepCount / 2;
            return halfSteps * riserHeight;
        }
    }
}
