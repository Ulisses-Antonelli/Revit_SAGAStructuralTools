using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Models
{
    /// <summary>
    /// Uma chapa de nervura individual, em coordenadas locais do plano da seção
    /// (Y = lateral, ao longo da largura do perfil; Z = altura, ao longo da alma).
    /// Origem no eixo/centro geométrico da viga. Cotas em mm.
    /// </summary>
    public class StiffenerPlate
    {
        /// <summary>+1 ou -1 — lado da alma em que a chapa fica.</summary>
        public double Side { get; set; }

        /// <summary>Contorno (Y, Z) em mm, já na ordem de percurso correta para extrusão.</summary>
        public List<(double Y, double Z)> OutlineMm { get; } = new List<(double, double)>();
    }

    /// <summary>
    /// Resultado do cálculo de nervura — sem dependência da Revit API, totalmente
    /// testável de forma isolada. Uma ou duas chapas (conforme simetria).
    /// </summary>
    public class StiffenerDefinition
    {
        public bool         IsValid       { get; set; } = true;
        public List<string> Warnings      { get; } = new List<string>();
        public double        ChamferMm     { get; set; }
        public string        ChamferSource { get; set; }
        public List<StiffenerPlate> Plates { get; } = new List<StiffenerPlate>();
    }
}
