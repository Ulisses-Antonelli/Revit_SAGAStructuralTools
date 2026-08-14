using SAGAStructuralTools.Rail.Domain;
using Xunit;

namespace SAGAStructuralTools.Rail.Domain.Tests
{
    public class EndPostDistributionTests
    {
        [Fact]
        public void RedistributesUniformlyWhenEndIsExtended()
        {
            var result = EndPostDistribution.Create(0, 4000, 5);
            Assert.Equal(new[] { 0d, 1000d, 2000d, 3000d, 4000d }, result.StationsMm);
            Assert.Equal(1000, result.ActualSpacingMm);
        }

        [Fact]
        public void KeepsAscendingStationsWhenStartEndIsMoved()
        {
            var result = EndPostDistribution.Create(3000, -600, 4);
            Assert.Equal(new[] { -600d, 600d, 1800d, 3000d }, result.StationsMm);
        }

        [Fact]
        public void RedistributesUniformlyWhenEndIsReceded()
        {
            var result = EndPostDistribution.Create(0, 2400, 4);
            Assert.Equal(new[] { 0d, 800d, 1600d, 2400d }, result.StationsMm);
        }

        [Fact]
        public void CalculatesAdditionalPostsForMaximumSpacing()
        {
            Assert.Equal(5, EndPostDistribution.RequiredPostCount(4000, 1200));
            Assert.Equal(4, EndPostDistribution.RequiredPostCount(3600, 1200));
        }
    }
}
