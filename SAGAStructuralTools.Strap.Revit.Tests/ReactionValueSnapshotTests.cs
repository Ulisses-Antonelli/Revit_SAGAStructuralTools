using SAGAStructuralTools.Revit.Strap;
using Xunit;

public sealed class ReactionValueSnapshotTests
{
    [Theory]
    [InlineData(4.0, 4.0, true)]
    [InlineData(0.0, 0.0000000005, true)]
    [InlineData(0.0, 0.000000001, true)]
    [InlineData(0.0, 0.0000000011, false)]
    [InlineData(-1.0, -1.0000000005, true)]
    [InlineData(-1.0, -1.0000000011, false)]
    public void AbsoluteToleranceHasRequiredBoundary(double current, double desired, bool equivalent)
        => Assert.Equal(equivalent, ReactionNumberComparer.AreEquivalent(current, desired));
}
