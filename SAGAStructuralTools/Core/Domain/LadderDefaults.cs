namespace SAGAStructuralTools.Core.Domain
{
    /// <summary>
    /// Valores iniciais da escada marinheiro, alinhados à prática normativa
    /// brasileira (NR-11/NBR): largura útil 400–600 mm, passo ≤ 300 mm,
    /// afastamento mínimo da parede 150–200 mm, gaiola recomendada acima de
    /// 3,50 m, anéis a cada ≤ 1,50 m e prolongamento ≥ 1,10 m no desembarque.
    /// </summary>
    public static class LadderDefaults
    {
        // Globais (mm)
        public const double TopLevelOffset    = 0.0;
        public const double BaseOffset        = 0.0;
        public const double WallOffset        = 200.0;

        // Corpo (mm)
        public const double Width             = 450.0;
        public const double RungSpacing       = 300.0;

        // Suportes (mm)
        public const double SupportMaxSpacing = 2000.0;

        // Gaiola (mm)
        public const double CageStartHeight   = 2200.0;
        public const double CageProjection    = 700.0;
        public const double RingSpacing       = 1500.0;
        public const int    StrapCount        = 5;

        // Desembarque (mm)
        public const double ExtensionHeight   = 1100.0;
        public const double ExitFlare         = 150.0;

        // Limites normativos usados nos avisos do preview (mm)
        public const double MinWidth          = 400.0;
        public const double MaxWidth          = 600.0;
        public const double MaxRungSpacing    = 300.0;
        public const double MinWallOffset     = 150.0;
        public const double CageRequiredAbove = 3500.0;
        public const double MaxRingSpacing    = 1500.0;
        public const double MinExtension      = 1100.0;
    }
}
