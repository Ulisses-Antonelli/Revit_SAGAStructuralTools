using System.Collections.Generic;
using System.Linq;
using SAGAStructuralTools.BasePlate.Domain;
using Xunit;

namespace SAGAStructuralTools.Rail.Domain.Tests
{
    public class BoltLayoutServiceTests
    {
        [Theory]
        [InlineData(2, 2, 4)]
        [InlineData(3, 2, 6)]
        [InlineData(2, 3, 6)]
        [InlineData(3, 3, 8)]
        [InlineData(4, 2, 8)]
        [InlineData(2, 4, 8)]
        [InlineData(4, 3, 10)]
        [InlineData(3, 4, 10)]
        [InlineData(4, 4, 12)]
        public void CalculatesPerimeterTotalAnchors(int anchorsX, int anchorsY, int expected)
        {
            Assert.Equal(expected, BoltLayoutService.CalculateTotalAnchors(anchorsX, anchorsY));
        }

        [Fact]
        public void GeneratePerimeterLayoutForThreeByThreeReturnsEightPointsWithoutCenter()
        {
            IReadOnlyList<BoltPoint> points = CreateService().GeneratePerimeterLayout(
                300,
                450,
                60,
                70,
                3,
                3);

            Assert.Equal(8, points.Count);
            Assert.DoesNotContain(points, p => p.X == 0 && p.Y == 0);
        }

        [Fact]
        public void GeneratePerimeterLayoutForFourByFourReturnsTwelvePoints()
        {
            IReadOnlyList<BoltPoint> points = CreateService().GeneratePerimeterLayout(
                300,
                450,
                60,
                70,
                4,
                4);

            Assert.Equal(12, points.Count);
        }

        [Fact]
        public void GeneratePerimeterLayoutDoesNotDuplicateCorners()
        {
            IReadOnlyList<BoltPoint> points = CreateService().GeneratePerimeterLayout(
                300,
                450,
                60,
                70,
                4,
                4);

            int distinctCoordinates = points
                .Select(p => $"{p.X:0.###};{p.Y:0.###}")
                .Distinct()
                .Count();

            Assert.Equal(points.Count, distinctCoordinates);
        }

        [Fact]
        public void CalculatesExpectedSpacings()
        {
            Assert.Equal(90, BoltLayoutService.CalculateSpacingX(300, 60, 3));
            Assert.Equal(155, BoltLayoutService.CalculateSpacingY(450, 70, 3));
        }

        private static BoltLayoutService CreateService()
        {
            return new BoltLayoutService();
        }
    }
}
