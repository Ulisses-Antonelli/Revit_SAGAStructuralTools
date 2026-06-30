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
        public string      StringerFamilyPath      { get; set; }
        public string      StringerFamilyType      { get; set; }
    }
}
