using SAGAStructuralTools.BasePlate.Domain;
using Xunit;

namespace SAGAStructuralTools.Rail.Domain.Tests
{
    public class HeavyHexNutCatalogTests
    {
        [Fact]
        public void ContainsAnsiHeavyHexNutOptions()
        {
            Assert.Equal("ANSI B 18.2.2", HeavyHexNutCatalog.Standard);
            Assert.Contains(HeavyHexNutCatalog.Options, option =>
                option.NominalDiameter == "1\" (25,40 mm)" &&
                option.DiameterMm == 25.40);
        }

        [Fact]
        public void FindsNearestOptionByDiameter()
        {
            HeavyHexNutOption option = HeavyHexNutCatalog.FindByDiameter(25);

            Assert.NotNull(option);
            Assert.Equal("1\" (25,40 mm)", option.NominalDiameter);
        }
    }
}
