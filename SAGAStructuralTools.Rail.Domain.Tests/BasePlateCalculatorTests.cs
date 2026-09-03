using System.Linq;
using SAGAStructuralTools.BasePlate.Domain;
using Xunit;

namespace SAGAStructuralTools.Rail.Domain.Tests
{
    public class BasePlateCalculatorTests
    {
        [Fact]
        public void ApprovesInitialChecksWhenGeometryIsValid()
        {
            var result = new BasePlateCalculator().Calculate(CreateValidInput());

            Assert.True(result.IsApproved);
            Assert.Contains(result.Verifications, v =>
                v.Name == "lx > bf" &&
                v.Status == VerificationStatus.Passed);
            Assert.Contains(result.Verifications, v =>
                v.Name == "ly > d" &&
                v.Status == VerificationStatus.Passed);
            Assert.Contains(result.Verifications, v =>
                v.Name == "Chumbador-borda" &&
                v.Status == VerificationStatus.Passed);
            Assert.Contains(result.Verifications, v =>
                v.Name == "Chumbador-chumbador" &&
                v.Status == VerificationStatus.Passed);
        }

        [Fact]
        public void FailsWhenPlateDoesNotExceedProfile()
        {
            BasePlateInput input = CreateValidInput();
            input.PlateLengthXmm = input.FlangeWidthMm;
            input.PlateLengthYmm = input.DepthMm;

            var result = new BasePlateCalculator().Calculate(input);

            Assert.False(result.IsApproved);
            Assert.Contains(result.Verifications, v =>
                v.Name == "lx > bf" &&
                v.Status == VerificationStatus.Failed);
            Assert.Contains(result.Verifications, v =>
                v.Name == "ly > d" &&
                v.Status == VerificationStatus.Failed);
        }

        [Fact]
        public void FailsWhenAnchorCountsAreLessThanTwo()
        {
            BasePlateInput input = CreateValidInput();
            input.AnchorsX = 1;
            input.AnchorsY = 0;

            var result = new BasePlateCalculator().Calculate(input);

            Assert.False(result.IsApproved);
            Assert.Contains(result.Verifications, v =>
                v.Name == "AnchorsX" &&
                v.Status == VerificationStatus.Failed);
            Assert.Contains(result.Verifications, v =>
                v.Name == "AnchorsY" &&
                v.Status == VerificationStatus.Failed);
        }

        [Fact]
        public void FailsWhenRequiredPositiveValuesAreInvalid()
        {
            BasePlateInput input = CreateValidInput();
            input.AnchorDiameterMm = 0;
            input.PlateThicknessMm = -1;

            var result = new BasePlateCalculator().Calculate(input);

            Assert.False(result.IsApproved);
            Assert.Contains(result.Verifications, v =>
                v.Name == "AnchorDiameterMm" &&
                v.Status == VerificationStatus.Failed);
            Assert.Contains(result.Verifications, v =>
                v.Name == "PlateThicknessMm" &&
                v.Status == VerificationStatus.Failed);
        }

        [Fact]
        public void ThrowsWhenInputIsNull()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                new BasePlateCalculator().Calculate(null));
        }

        private static BasePlateInput CreateValidInput()
        {
            return new BasePlateInput
            {
                DepthMm = 300,
                FlangeWidthMm = 150,
                WebThicknessMm = 6.3,
                FlangeThicknessMm = 9.5,
                AxialForceTf = 20,
                MomentX_TfM = 1.5,
                MomentY_TfM = 0.8,
                ShearX_Tf = 2,
                ShearY_Tf = 1,
                PlateFyMpa = 250,
                PlateFuMpa = 400,
                ConcreteFckMpa = 30,
                AnchorFyMpa = 250,
                AnchorFuMpa = 400,
                ColumnFyMpa = 250,
                ColumnFuMpa = 400,
                PlateLengthXmm = 300,
                PlateLengthYmm = 450,
                PlateThicknessMm = 19,
                AnchorDiameterMm = 19,
                EmbedmentLengthMm = 250,
                HasHook = true,
                CorrosionAllowanceMm = 0,
                AnchorsX = 2,
                AnchorsY = 2,
                AnchorEdgeDistanceXmm = 60,
                AnchorEdgeDistanceYmm = 70,
                StiffenerThicknessMm = 0,
                StiffenerHeightMm = 0,
                HasMiddleStiffener = false
            };
        }
    }
}
