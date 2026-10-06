namespace SAGAStructuralTools.BasePlate.Domain
{
    public class VerificationResult
    {
        public string Name { get; set; }
        public VerificationStatus Status { get; set; }
        public string Message { get; set; }
        public double CalculatedValue { get; set; }
        public double RequiredValue { get; set; }
        public string Unit { get; set; }
    }
}
