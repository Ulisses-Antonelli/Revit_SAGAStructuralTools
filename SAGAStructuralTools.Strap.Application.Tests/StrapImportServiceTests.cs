using System.Linq;
using Xunit;

namespace SAGAStructuralTools.Strap.Application.Tests
{
    public sealed class StrapImportServiceTests
    {
        [Fact]
        public void ValidDocumentIsConsolidatedByApprovedDomainEngine()
        {
            StrapImportResult result = new StrapImportService().Import(
                TestDocument.Create(TestDocument.ValidLines()));

            var reaction = Assert.Single(result.ConsolidatedReactions);
            Assert.False(result.IsBlocked);
            Assert.Equal(2.056m, reaction.X1);
            Assert.Equal(0.375m, reaction.X2);
            Assert.Equal(3.143m, reaction.X3Max);
            Assert.Equal(-3.432m, reaction.X3Min);
            Assert.Equal(0.083m, reaction.X4);
            Assert.Equal(0.781m, reaction.X5);
            Assert.Equal(0.381m, reaction.X6);
        }

        [Fact]
        public void OptionalRotationIsDelegatedToDomainEngine()
        {
            StrapImportResult result = new StrapImportService().Import(
                TestDocument.Create(TestDocument.ValidLines()),
                rotate90: true);

            var reaction = Assert.Single(result.ConsolidatedReactions);
            Assert.Equal(0.375m, reaction.X1);
            Assert.Equal(2.056m, reaction.X2);
            Assert.Equal(0.781m, reaction.X4);
            Assert.Equal(0.083m, reaction.X5);
        }

        [Fact]
        public void BlockingDiagnosticPreventsAnyConsolidation()
        {
            StrapImportResult result = new StrapImportService().Import(
                TestDocument.Create(
                    "Unidade: kN",
                    "Nó 1",
                    "Máx 1 2 3 4 5 6"));

            Assert.True(result.IsBlocked);
            Assert.Empty(result.ConsolidatedReactions);
        }

        [Fact]
        public void ResultExposesRawDataUnitsCombinationsAndDiagnostics()
        {
            StrapImportResult result = new StrapImportService().Import(
                TestDocument.Create(
                    "Unidade: kN",
                    "Nó 1",
                    "Máx 1[C1] 2[C2] 3[C3] 4[C4] 5[C5] 6[C6]",
                    "Mín -1[D1] -2[D2] -3[D3] -4[D4] -5[D5] -6[D6]"));

            Assert.Equal(4, result.RawLines.Count);
            Assert.Equal("kN", Assert.Single(result.Units));
            Assert.Equal(12, result.Combinations.Count);
            Assert.Empty(result.Errors);
            Assert.NotEmpty(result.Nodes.SelectMany(node => node.Rows));
        }
    }
}
