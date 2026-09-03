using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.BasePlate.Domain
{
    public class BasePlateCalculationResult
    {
        public double MinimumPlateThicknessMm { get; set; }
        public double MinimumStiffenerThicknessMm { get; set; }
        public double ConcretePressureTfM2 { get; set; }
        public double ConcreteResistanceTfM2 { get; set; }
        public double AnchorTensionTf { get; set; }
        public double AnchorShearTf { get; set; }
        public double CorrodedAnchorDiameterMm { get; set; }
        public double AnchorConcreteShearResistanceTf { get; set; }
        public double AnchorConcreteTensionResistanceTf { get; set; }
        public double AnchorSteelShearResistanceTf { get; set; }
        public double AnchorSteelTensionResistanceTf { get; set; }
        public double AnchorConcreteUtilization { get; set; }
        public double AnchorSteelUtilization1 { get; set; }
        public double AnchorSteelUtilization2 { get; set; }
        public IList<VerificationResult> Verifications { get; } =
            new List<VerificationResult>();

        public bool IsApproved =>
            Verifications.All(v => v.Status == VerificationStatus.Passed);
    }
}
