namespace SAGAStructuralTools.BasePlate.Domain
{
    public class PlateThicknessOption
    {
        public PlateThicknessOption(string displayName, double thicknessMm)
        {
            DisplayName = displayName;
            ThicknessMm = thicknessMm;
        }

        public string DisplayName { get; }
        public double ThicknessMm { get; }
    }
}
