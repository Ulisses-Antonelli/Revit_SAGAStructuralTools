using SAGAStructuralTools.BasePlate.Domain;
using Xunit;

namespace SAGAStructuralTools.Rail.Domain.Tests
{
    public class ConcreteStrengthCatalogTests
    {
        [Fact]
        public void Options_ShouldContainStrengthsUpToC35()
        {
            Assert.Collection(
                ConcreteStrengthCatalog.Options,
                option =>
                {
                    Assert.Equal("C-20", option.DisplayName);
                    Assert.Equal(20.0, option.FckMpa);
                },
                option =>
                {
                    Assert.Equal("C-25", option.DisplayName);
                    Assert.Equal(25.0, option.FckMpa);
                },
                option =>
                {
                    Assert.Equal("C-30", option.DisplayName);
                    Assert.Equal(30.0, option.FckMpa);
                },
                option =>
                {
                    Assert.Equal("C-35", option.DisplayName);
                    Assert.Equal(35.0, option.FckMpa);
                });
        }

        [Fact]
        public void FindByFck_ShouldReturnNearestOption()
        {
            ConcreteStrengthOption option = ConcreteStrengthCatalog.FindByFck(32.0);

            Assert.NotNull(option);
            Assert.Equal("C-30", option.DisplayName);
            Assert.Equal(30.0, option.FckMpa);
        }
    }
}
