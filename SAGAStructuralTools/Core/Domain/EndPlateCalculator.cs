using SAGAStructuralTools.Core.Models;

namespace SAGAStructuralTools.Core.Domain
{
    /// <summary>
    /// Calcula as dimensões da chapa de topo a partir de h/bf do perfil e das
    /// margens configuradas. Sem dependência da Revit API.
    /// </summary>
    public static class EndPlateCalculator
    {
        public static EndPlateDefinition Calculate(double hMm, double bfMm, EndPlateConfig config)
        {
            var def = new EndPlateDefinition();

            if (config == null)
            {
                def.IsValid = false;
                def.Warnings.Add("Configuração indisponível.");
                return def;
            }

            if (hMm <= 0 || bfMm <= 0)
            {
                def.IsValid = false;
                def.Warnings.Add("Dimensões do perfil (h, bf) não puderam ser lidas.");
                return def;
            }

            double marginV = config.MarginVertical;
            double marginH = config.MarginHorizontal;

            def.HeightMm = hMm + 2 * marginV;
            def.WidthMm  = bfMm + 2 * marginH;

            if (marginV < 0 || marginH < 0)
                def.Warnings.Add("Margem negativa — a chapa fica menor que o envelope da seção.");

            if (def.HeightMm <= 0 || def.WidthMm <= 0)
            {
                def.IsValid = false;
                def.Warnings.Add("Margens negativas grandes demais — a chapa ficaria com dimensão zero ou negativa.");
            }

            if (config.PlateThickness <= 0)
            {
                def.IsValid = false;
                def.Warnings.Add("Espessura da chapa precisa ser maior que zero.");
            }

            return def;
        }
    }
}
