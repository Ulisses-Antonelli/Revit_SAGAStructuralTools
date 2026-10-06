namespace SAGAStructuralTools.BasePlate.Domain
{
    public class HeavyHexNutOption
    {
        public HeavyHexNutOption(
            string nominalDiameter,
            double diameterMm,
            double sMinMm,
            double sMaxMm,
            double hMinMm,
            double hMaxMm,
            double aMinMm,
            double aMaxMm,
            double bMinMm,
            double cMaxMm,
            double massKgPer100)
        {
            NominalDiameter = nominalDiameter;
            DiameterMm = diameterMm;
            SMinMm = sMinMm;
            SMaxMm = sMaxMm;
            HMinMm = hMinMm;
            HMaxMm = hMaxMm;
            AMinMm = aMinMm;
            AMaxMm = aMaxMm;
            BMinMm = bMinMm;
            CMaxMm = cMaxMm;
            MassKgPer100 = massKgPer100;
        }

        public string NominalDiameter { get; }
        public double DiameterMm { get; }
        public double SMinMm { get; }
        public double SMaxMm { get; }
        public double HMinMm { get; }
        public double HMaxMm { get; }
        public double AMinMm { get; }
        public double AMaxMm { get; }
        public double BMinMm { get; }
        public double CMaxMm { get; }
        public double MassKgPer100 { get; }

        public string DisplayName => NominalDiameter;
    }
}
