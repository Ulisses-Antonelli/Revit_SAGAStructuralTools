using SAGAStructuralTools.Core.Domain;

namespace SAGAStructuralTools.Core.Models
{
    /// <summary>
    /// Configuração de uma chapa de topo (end plate) na extremidade de um perfil
    /// W. A chapa é sempre um retângulo: envelope da seção (h × bf) mais margens.
    /// </summary>
    public class EndPlateConfig
    {
        public double PlateThickness    { get; set; } = EndPlateDefaults.PlateThickness;    // mm
        public double MarginVertical    { get; set; } = EndPlateDefaults.MarginVertical;    // mm além das mesas (topo/fundo)
        public double MarginHorizontal  { get; set; } = EndPlateDefaults.MarginHorizontal;  // mm além das abas (laterais)

        /// <summary>
        /// Só se aplica no Modo 2 (duas peças): true = cada peça recebe sua
        /// própria chapa na interface de contato; false = só a primeira peça
        /// selecionada recebe chapa.
        /// </summary>
        public bool GenerateBothMembers { get; set; } = true;

        /// <summary>
        /// Só se aplica no Modo 2 com as duas chapas habilitadas: afastamento
        /// (mm) entre as duas chapas na interface. 0 = encostadas, faceando uma
        /// a outra.
        /// </summary>
        public double GapBetweenPlates { get; set; } = EndPlateDefaults.GapBetweenPlates;
    }
}
