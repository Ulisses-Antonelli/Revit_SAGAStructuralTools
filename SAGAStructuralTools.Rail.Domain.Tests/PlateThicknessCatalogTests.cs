using SAGAStructuralTools.BasePlate.Domain;
using Xunit;

namespace SAGAStructuralTools.Rail.Domain.Tests
{
    public class PlateThicknessCatalogTests
    {
        [Fact]
        public void Options_ShouldContainBasePlateThicknesses()
        {
            Assert.Collection(
                PlateThicknessCatalog.Options,
                option => Assert.Equal(6.35, option.ThicknessMm),
                option => Assert.Equal(8.00, option.ThicknessMm),
                option => Assert.Equal(9.50, option.ThicknessMm),
                option => Assert.Equal(12.70, option.ThicknessMm),
                option => Assert.Equal(16.00, option.ThicknessMm),
                option => Assert.Equal(19.00, option.ThicknessMm),
                option => Assert.Equal(22.00, option.ThicknessMm),
                option => Assert.Equal(25.40, option.ThicknessMm));
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
