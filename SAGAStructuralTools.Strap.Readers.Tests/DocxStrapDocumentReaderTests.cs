using System.Linq;
using Xunit;

namespace SAGAStructuralTools.Strap.Readers.Tests
{
    public sealed class DocxStrapDocumentReaderTests
    {
        [Fact]
        public void ReadsTableRowsAndPreservesCellsSeparately()
        {
            DocumentReadResult result = new DocxStrapDocumentReader()
                .Read(FixturePaths.Get("reactions-table.docx"));

            Assert.False(result.IsBlocked);
            Assert.NotEmpty(result.TableRows);
            ExtractedTableRow header = result.TableRows.Single(
                row => row.Cells.Contains("FX"));
            Assert.Equal(6, header.Cells.Count);
            Assert.Equal(1, header.Trace.TableNumber);
            Assert.Contains(
                result.Document.Lines,
                line => line.RawText == "FX\tFY\tFZ\tMX\tMY\tMZ");
        }

        [Fact]
        public void ReadsTabbedParagraphsOutsideTables()
        {
            DocumentReadResult result = new DocxStrapDocumentReader()
                .Read(FixturePaths.Get("reactions-tabulated.docx"));

            Assert.False(result.IsBlocked);
            Assert.Empty(result.TableRows);
            Assert.Contains(
                result.Document.Lines,
                line => line.RawText == "FX\tFY\tFZ\tMX\tMY\tMZ");
            Assert.All(result.Document.Lines, line => Assert.Null(line.Trace.TableNumber));
        }

        [Fact]
        public void CorruptDocxIsRejected()
        {
            DocumentReadResult result = new DocxStrapDocumentReader()
                .Read(FixturePaths.Get("corrupt.docx"));

            Assert.True(result.IsBlocked);
            Assert.Contains(
                result.Diagnostics,
                item => item.Code == ReaderDiagnosticCodes.CorruptDocument);
        }
    }
}
