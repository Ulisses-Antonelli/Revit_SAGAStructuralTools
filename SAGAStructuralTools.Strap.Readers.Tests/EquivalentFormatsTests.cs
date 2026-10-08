using System.Collections.Generic;
using System.Linq;
using SAGAStructuralTools.Strap.Application;
using SAGAStructuralTools.Strap.Domain;
using Xunit;

namespace SAGAStructuralTools.Strap.Readers.Tests
{
    public sealed class EquivalentFormatsTests
    {
        public static IEnumerable<object[]> EquivalentFixtures()
        {
            yield return new object[] { "reactions-utf8.txt", new TxtStrapDocumentReader() };
            yield return new object[] { "reactions-table.docx", new DocxStrapDocumentReader() };
            yield return new object[] { "reactions-tabulated.docx", new DocxStrapDocumentReader() };
            yield return new object[] { "reactions.rtf", new RtfStrapDocumentReader() };
            yield return new object[] { "reactions-single-page.pdf", new TextPdfStrapDocumentReader() };
            yield return new object[] { "reactions-multi-page.pdf", new TextPdfStrapDocumentReader() };
        }

        [Theory]
        [MemberData(nameof(EquivalentFixtures))]
        public void EquivalentFormatsProduceSameIntermediateSemantics(
            string fixture,
            IStrapDocumentReader reader)
        {
            DocumentReadResult read = reader.Read(FixturePaths.Get(fixture));
            Assert.False(read.IsBlocked);

            StrapImportResult imported = new StrapImportService().Import(read.Document);
            Assert.False(imported.IsBlocked);
            ConsolidatedReaction reaction = Assert.Single(imported.ConsolidatedReactions);

            Assert.Equal("101", reaction.NodeId);
            Assert.Equal(2m, reaction.X1);
            Assert.Equal(4m, reaction.X2);
            Assert.Equal(3m, reaction.X3Max);
            Assert.Equal(-6m, reaction.X3Min);
            Assert.Equal(8m, reaction.X4);
            Assert.Equal(10m, reaction.X5);
            Assert.Equal(12m, reaction.X6);
            Assert.Equal("kN", Assert.Single(imported.Units));
            Assert.Equal(
                new[] { ReportCaseKind.Maximum, ReportCaseKind.Minimum },
                imported.Nodes.Single().Rows.Select(row => row.CaseKind));
        }
    }
}
