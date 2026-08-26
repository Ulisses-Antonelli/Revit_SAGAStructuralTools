namespace SAGAStructuralTools.Core.Models
{
    /// <summary>Uma peça de aço medida — sem dependência da Revit API.</summary>
    public class QuantitativoMeasurement
    {
        public string Description   { get; set; }
        public string Material      { get; set; }
        public double LengthM       { get; set; }
        public double? UnitWeightKgM { get; set; } // null = perfil sem peso nominal cadastrado no tipo
    }
}
