using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Models
{
    /// <summary>
    /// Dimensões calculadas de uma chapa de topo — sem dependência da Revit API,
    /// totalmente testável de forma isolada.
    /// </summary>
    public class EndPlateDefinition
    {
        public bool         IsValid  { get; set; } = true;
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>Largura final da chapa (mm) — direção da largura do perfil (bf).</summary>
        public double WidthMm  { get; set; }

        /// <summary>Altura final da chapa (mm) — direção da altura do perfil (h).</summary>
        public double HeightMm { get; set; }
    }
}
