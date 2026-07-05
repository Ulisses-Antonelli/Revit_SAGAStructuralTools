using SAGAStructuralTools.Core.Models;

namespace SAGAStructuralTools.Core.Domain
{
    /// <summary>
    /// Calcula profundidade dos patamares de extremidade.
    /// Sem dependência de Revit.
    /// </summary>
    public static class LandingCalculator
    {
        /// <summary>
        /// Calcula a profundidade dos patamares inferior e superior.
        /// Desconta o comprimento do patamar intermediário (ILL) do espaço disponível.
        /// Centraliza a escada quando configurado.
        /// </summary>
        public static (double lower, double upper) EndLandings(
            double totalRun, double beamDistance, StairConfig config)
        {
            var ill = config.HasIntermediateLanding ? config.IntermediateLandingLength : 0;
            var gap = beamDistance - totalRun - ill;
            if (gap <= 0) return (0, 0);

            if (config.CenterStair)
            {
                var each = gap / 2.0;
                return (each, each);
            }
            return (gap, 0);
        }
    }
}
