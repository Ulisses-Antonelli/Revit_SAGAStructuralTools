using SAGAStructuralTools.Core.Domain;

namespace SAGAStructuralTools.Core.Models
{
    /// <summary>Configuração do quantitativo de perfis metálicos.</summary>
    public class QuantitativoConfig
    {
        /// <summary>Margem de perda somada ao peso total (%). Ex.: 10 = +10%.</summary>
        public double MarginPercent { get; set; } = QuantitativoDefaults.MarginPercent;
    }
}
