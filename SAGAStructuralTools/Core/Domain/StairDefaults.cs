namespace SAGAStructuralTools.Core.Domain
{
    public static class StairDefaults
    {
        public const double Width                    = 800;   // mm
        public const double TreadDepth               = 250;   // mm
        public const double TreadThickness           = 40;    // mm
        public const double TargetRiserHeight        = 180;   // mm (ponto de partida para cálculo)
        public const double MaxHeightWithoutLanding  = 3700;  // mm (norma)
        public const bool   ApplyBlondel             = false;
        public const bool   CenterStair              = true;
        public const bool   IncludeTreads            = true;
        public const string LandingMode              = "Auto";
        public const double IntermediateLandingLength = 1200; // mm
    }
}
