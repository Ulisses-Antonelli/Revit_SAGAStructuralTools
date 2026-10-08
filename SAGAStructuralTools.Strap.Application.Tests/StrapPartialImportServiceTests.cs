using System.Linq;
using Xunit;

namespace SAGAStructuralTools.Strap.Application.Tests
{
    public sealed class StrapPartialImportServiceTests
    {
        [Fact]
        public void KeepsValidNodesWhenAnotherNodeIsInvalid()
        {
            StrapPartialImportResult result = Import(
                "REAÇÕES (Unids: tf, tf*metro)",
                "nº cmb X1 X2 X3 X4 X5 X6",
                "1 Máx 1 2 3 4 5 6",
                "Comb 1 2 3 4 5 6",
                "Mín -1 -2 -3 -4 -5 -6",
                "Comb 7 8 9 10 11 12",
                "2 Máx 1 2 3 4 5",
                "Comb 1 2 3 4 5 6",
                "Mín -1 -2 -3 -4 -5 -6",
                "Comb 7 8 9 10 11 12");

            Assert.False(
                result.IsGloballyBlocked,
                string.Join(" | ", result.GlobalDiagnostics.Select(item => item.Message)));
            Assert.True(result.Nodes.Single(node => node.NodeId == "1").IsValid);
            Assert.False(result.Nodes.Single(node => node.NodeId == "2").IsValid);
        }

        [Theory]
        [InlineData("tf*metro")]
        [InlineData("tf · m")]
        [InlineData("TF.M")]
        [InlineData("tf × metro")]
        [InlineData("tf x m")]
        public void AcceptsEquivalentMomentUnitSpellings(string unit)
        {
            Assert.True(StrapPartialImportService.IsMomentUnit(unit));
        }

        [Fact]
        public void DifferentUnitsBlockTheWholeDocument()
        {
            StrapPartialImportResult result = Import(
                "REAÇÕES (Unids: kN, kN*m)",
                "nº cmb X1 X2 X3 X4 X5 X6",
                "1 Máx 1 2 3 4 5 6",
                "Comb 1 2 3 4 5 6",
                "Mín -1 -2 -3 -4 -5 -6",
                "Comb 7 8 9 10 11 12");

            Assert.True(result.IsGloballyBlocked);
            Assert.Equal(0, result.ValidNodeCount);
        }

        [Fact]
        public void AcceptsLegacyUnidadesLineWithSeparatedForceAndMoment()
        {
            StrapPartialImportResult result = Import(
                "Unidades: tf, tf*metro",
                "Nó 1",
                "X1 X2 X3 X4 X5 X6",
                "Máx 1 2 3 4 5 6",
                "Mín -1 -2 -3 -4 -5 -6");

            Assert.False(
                result.IsGloballyBlocked,
                string.Join(" | ", result.GlobalDiagnostics.Select(item => item.Message)));
            Assert.Equal("tf", Assert.Single(result.ParseResult.ForceUnits));
            Assert.Equal("tf*metro", Assert.Single(result.ParseResult.MomentUnits));
        }

        [Fact]
        public void IdenticalDuplicateNodesAreNotSelectable()
        {
            string[] block =
            {
                "1 Máx 1 2 3 4 5 6",
                "Comb 1 2 3 4 5 6",
                "Mín -1 -2 -3 -4 -5 -6",
                "Comb 7 8 9 10 11 12"
            };
            StrapPartialImportResult result = Import(
                new[] { "REAÇÕES (Unids: tf, tf*metro)", "nº cmb X1 X2 X3 X4 X5 X6" }
                    .Concat(block)
                    .Concat(block)
                    .ToArray());

            Assert.All(result.Nodes, node => Assert.False(node.IsValid));
        }

        private static StrapPartialImportResult Import(params string[] lines)
            => new StrapPartialImportService().Import(TestDocument.Create(lines));
    }
}
