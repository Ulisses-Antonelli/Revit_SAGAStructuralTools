using SAGAStructuralTools.Core.Domain;

namespace SAGAStructuralTools.Core.Models
{
    public enum LandingMode { Auto, Always, Never }

    public class StairConfig
    {
        public double      Width                   { get; set; } = StairDefaults.Width;
        public double      TreadDepth              { get; set; } = StairDefaults.TreadDepth;
        public double      TreadThickness          { get; set; } = StairDefaults.TreadThickness;
        public bool        ApplyBlondel            { get; set; } = StairDefaults.ApplyBlondel;
        public bool        CenterStair             { get; set; } = StairDefaults.CenterStair;
        public LandingMode IntermediateLanding     { get; set; } = LandingMode.Auto;
        public double      MaxHeightWithoutLanding { get; set; } = StairDefaults.MaxHeightWithoutLanding;
        public bool        IncludeTreads           { get; set; } = StairDefaults.IncludeTreads;
        public string      StringerFamilyPath      { get; set; }
        public string      StringerFamilyType      { get; set; }

        // Só relevante para perfis U/Canal.
        // false (padrão): Largura Útil = distância livre entre as faces externas das almas.
        // true           : Largura Útil = distância entre os eixos neutros (centroide-a-centroide).
        public bool        UseAxis                 { get; set; } = false;
        public double      IntermediateLandingLength { get; set; } = StairDefaults.IntermediateLandingLength;
    }
}
