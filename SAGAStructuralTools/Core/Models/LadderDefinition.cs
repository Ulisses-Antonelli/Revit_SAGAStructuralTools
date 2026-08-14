using System.Collections.Generic;

namespace SAGAStructuralTools.Core.Models
{
    /// <summary>
    /// Resultado do cálculo da escada marinheiro (LadderCalculator): posições de
    /// degraus, suportes e anéis medidas a partir da BASE, em milímetros.
    /// Sem dependência da Revit API — totalmente testável de forma isolada.
    /// </summary>
    public class LadderDefinition
    {
        public double TotalHeightMm        { get; set; }             // base → desembarque
        public List<double> RungElevations { get; set; } = new List<double>();
        public double BottomGapMm          { get; set; }             // base → primeiro degrau
        public List<double> SupportElevations { get; set; } = new List<double>();
        public List<double> RingElevations { get; set; } = new List<double>();
        public int    StrapCount           { get; set; }
        public double ExtensionTopMm       { get; set; }             // base → topo do prolongamento

        public int RungCount    => RungElevations?.Count ?? 0;
        public int SupportCount => SupportElevations?.Count ?? 0;
        public int RingCount    => RingElevations?.Count ?? 0;

        public List<string> Warnings { get; set; } = new List<string>();
        public bool         IsValid  { get; set; } = true;
    }
}
