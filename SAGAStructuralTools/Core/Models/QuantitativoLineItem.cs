namespace SAGAStructuralTools.Core.Models
{
    /// <summary>
    /// Uma linha agrupada da lista de perfis — um perfil+material, somado.
    /// Sem dependência da Revit API.
    /// </summary>
    public class QuantitativoLineItem
    {
        public string Description   { get; set; }
        public string Material      { get; set; }
        public int    ElementCount  { get; set; }
        public double TotalLengthM  { get; set; }
        public double? UnitWeightKgM          { get; set; } // null = sem peso nominal no tipo
        public double? TotalWeightKg          { get; set; }
        public double? TotalWeightWithMarginKg { get; set; }
    }
}
