using SAGAStructuralTools.BasePlate.Domain;
using Xunit;

namespace SAGAStructuralTools.Rail.Domain.Tests
{
    public class PlateThicknessCatalogTests
    {
        [Fact]
        public void Options_ShouldContainBasePlateThicknesses()
        {
            Assert.Equal(20, PlateThicknessCatalog.Options.Count);
            Assert.Contains(PlateThicknessCatalog.Options, option => option.ThicknessMm == 6.35);
            Assert.Contains(PlateThicknessCatalog.Options, option => option.ThicknessMm == 25.40);
            Assert.Contains(PlateThicknessCatalog.Options, option => option.ThicknessMm == 76.20);
        }

        [Fact]
        public void FindByThickness_ShouldReturnNearestOption()
        {
            PlateThicknessOption option = PlateThicknessCatalog.FindByThickness(19.05);

            Assert.NotNull(option);
            Assert.Equal(19.00, option.ThicknessMm);
            Assert.Equal("3/4\" (19,00 mm)", option.DisplayName);
        }
    }
}
