using Xunit;

namespace SAGAStructuralTools.Strap.Domain.Tests
{
    public sealed class ReactionConsolidatorTests
    {
        [Fact]
        public void Case1_MixedFzSigns_UsesAbsoluteModulesAndPreservesFzSigns()
        {
            var maximum = R(0.136m, 0.375m, 3.143m, 0.083m, -0.104m, -0.004m);
            var minimum = R(-2.056m, 0.239m, -3.432m, 0.073m, -0.781m, 0.381m);

            ConsolidatedReaction result =
                ReactionConsolidator.ConsolidateNode("1", maximum, minimum);

            Assert.Equal(2.056m, result.X1);
            Assert.Equal(0.375m, result.X2);
            Assert.Equal(3.143m, result.X3Max);
            Assert.Equal(-3.432m, result.X3Min);
            Assert.Equal(0.083m, result.X4);
            Assert.Equal(0.781m, result.X5);
            Assert.Equal(0.381m, result.X6);
        }

        [Fact]
        public void Case2_MinimumLabelMayContainNumericallyLargestFz()
        {
            ConsolidatedReaction result = ReactionConsolidator.ConsolidateNode(
                "2",
                R(fz: 1.596m),
                R(fz: 7.112m));

            Assert.Equal(7.112m, result.X3Max);
            Assert.Equal(1.596m, result.X3Min);
        }

        [Fact]
        public void Case3_BothFzPositive_AreOrderedNumerically()
        {
            ConsolidatedReaction result = ReactionConsolidator.ConsolidateNode(
                "3",
                R(fz: 20.678m),
                R(fz: 16.306m));

            Assert.Equal(20.678m, result.X3Max);
            Assert.Equal(16.306m, result.X3Min);
        }

        [Fact]
        public void Case4_BothFzNegative_AreOrderedNumerically()
        {
            ConsolidatedReaction result = ReactionConsolidator.ConsolidateNode(
                "4",
                R(fz: -2.100m),
                R(fz: -8.400m));

            Assert.Equal(-2.100m, result.X3Max);
            Assert.Equal(-8.400m, result.X3Min);
        }

        [Fact]
        public void Case5_LargestMyModuleFromNegativeSource_IsPositive()
        {
            ConsolidatedReaction result = ReactionConsolidator.ConsolidateNode(
                "5",
                R(my: 1.184m),
                R(my: -4.139m));

            Assert.Equal(4.139m, result.X5);
        }

        [Fact]
        public void Case6_EqualFxModules_ResultIsPositive()
        {
            ConsolidatedReaction result = ReactionConsolidator.ConsolidateNode(
                "6",
                R(fx: 0.015m),
                R(fx: -0.015m));

            Assert.Equal(0.015m, result.X1);
        }

        [Fact]
        public void Case7_Rotation90_SwapsSpecifiedAxesOnly()
        {
            var original = new ConsolidatedReaction(
                "7", 2m, 5m, 9m, -3m, 0.3m, 1.2m, 0.1m);

            ConsolidatedReaction result = original.Rotate90();

            Assert.Equal(5m, result.X1);
            Assert.Equal(2m, result.X2);
            Assert.Equal(9m, result.X3Max);
            Assert.Equal(-3m, result.X3Min);
            Assert.Equal(1.2m, result.X4);
            Assert.Equal(0.3m, result.X5);
            Assert.Equal(0.1m, result.X6);
        }

        [Fact]
        public void MissingMaximumRow_IsRejected()
        {
            Assert.Throws<DomainValidationException>(
                () => ReactionConsolidator.ConsolidateNode("8", null, R()));
        }

        [Fact]
        public void MissingMinimumRow_IsRejected()
        {
            Assert.Throws<DomainValidationException>(
                () => ReactionConsolidator.ConsolidateNode("8", R(), null));
        }

        private static ForceMoment R(
            decimal fx = 0m,
            decimal fy = 0m,
            decimal fz = 0m,
            decimal mx = 0m,
            decimal my = 0m,
            decimal mz = 0m)
            => new ForceMoment(fx, fy, fz, mx, my, mz);
    }
}
