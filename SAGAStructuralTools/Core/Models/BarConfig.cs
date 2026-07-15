namespace SAGAStructuralTools.Core.Models
{
    public enum BarAlignment { Axis, InternalFace, ExternalFace }

    public class BarConfig
    {
        public string       FamilyPath { get; set; }
        public string       FamilyType { get; set; }
        public BarAlignment Alignment  { get; set; } = BarAlignment.Axis;
        public double       Distance   { get; set; } = 0.0;  // mm desde rodapé topo; ignorado quando Equidistante
        public double       LateralOffset { get; set; } = 0.0; // mm adicionais, perpendicular ao trecho
    }
}
