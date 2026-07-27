using System.Linq;
using Xunit;

namespace SAGAStructuralTools.Strap.Readers.Tests
{
    public sealed class TxtStrapDocumentReaderTests
    {
        [Theory]
        [InlineData("reactions-utf8.txt")]
        [InlineData("reactions-windows1252.txt")]
        public void ReadsSupportedEncodingAndPreservesLineOrigin(string fixture)
        {
            DocumentReadResult result =
                new TxtStrapDocumentReader().Read(FixturePaths.Get(fixture));

            Assert.False(result.IsBlocked);
            Assert.NotNull(result.Document);
            Assert.Contains(result.Document.Lines, line => line.RawText.Contains("Máx"));
            Assert.All(
                result.Document.Lines,
                line => Assert.Equal(FixturePaths.Get(fixture), line.Trace.FileName));
            Assert.Equal(
                Enumerable.Range(1, result.Document.Lines.Count),
                result.Document.Lines.Select(line => line.Trace.LineNumber));
        }

        [Fact]
        public void EmptyFileIsRejected()
        {
            DocumentReadResult result =
                new TxtStrapDocumentReader().Read(FixturePaths.Get("empty.txt"));

            Assert.True(result.IsBlocked);
            Assert.Contains(
                result.Diagnostics,
                item => item.Code == ReaderDiagnosticCodes.EmptyFile);
        }

        [Fact]
        public void MissingFileIsRejected()
        {
            DocumentReadResult result =
                new TxtStrapDocumentReader().Read(FixturePaths.Get("missing.txt"));

            Assert.True(result.IsBlocked);
            Assert.Contains(
                result.Diagnostics,
                item => item.Code == ReaderDiagnosticCodes.FileNotFound);
        }
    }
}
