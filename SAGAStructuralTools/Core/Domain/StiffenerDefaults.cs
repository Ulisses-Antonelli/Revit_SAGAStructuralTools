namespace SAGAStructuralTools.Core.Domain
{
    public static class StiffenerDefaults
    {
        public const double PlateThickness  = 9.5;  // mm — chapa usual (3/8")
        public const double FallbackChamfer = 15.0;  // mm — usado quando o perfil não expõe raio de concordância
        public const double ChamferMargin   = 2.0;   // mm — folga somada ao raio de concordância lido do perfil
    }
}
