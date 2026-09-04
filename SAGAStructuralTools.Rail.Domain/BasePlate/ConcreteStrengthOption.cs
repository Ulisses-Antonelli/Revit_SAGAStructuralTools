namespace SAGAStructuralTools.BasePlate.Domain
{
    public class ConcreteStrengthOption
    {
        public ConcreteStrengthOption(string displayName, double fckMpa)
        {
            DisplayName = displayName;
            FckMpa = fckMpa;
        }

        public string DisplayName { get; }
        public double FckMpa { get; }
    }
}
