using SAGAStructuralTools.Core.Domain;

namespace SAGAStructuralTools.Core.Models
{
    /// <summary>
    /// Configuração de uma nervura (chapa de reforço) inserida no vão livre de um
    /// perfil W, entre as mesas, encostada na alma.
    /// </summary>
    public class StiffenerConfig
    {
        public double PlateThickness  { get; set; } = StiffenerDefaults.PlateThickness;  // mm
        public double ChamferOverride { get; set; } = 0;                                 // mm — 0 = calcular a partir do perfil
        public bool   Symmetric       { get; set; } = true;                              // true = ambos os lados da alma (espelhado)

        /// <summary>
        /// Só se aplica com referência de alinhamento: inverte pra qual lado a
        /// chapa nasce (dentro/fora) em relação à face de referência. Ajuste
        /// manual pra quando a heurística de sinal escolhe o lado errado.
        /// </summary>
        public bool   FlipAlignmentSide { get; set; } = false;
    }
}
