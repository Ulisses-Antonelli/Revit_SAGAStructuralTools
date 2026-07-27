using System.Linq;
using Xunit;

namespace SAGAStructuralTools.Strap.Readers.Tests
{
    public sealed class TextPdfStrapDocumentReaderTests
    {
        [Fact]
        public void ReadsSinglePagePdfWithPageAndReconstructedLineTrace()
        {
            DocumentReadResult result = new TextPdfStrapDocumentReader()
                .Read(FixturePaths.Get("reactions-single-page.pdf"));

            Assert.False(result.IsBlocked);
            Assert.All(result.Document.Lines, line => Assert.Equal(1, line.Trace.PageNumber));
            Assert.Equal(
                Enumerable.Range(1, result.Document.Lines.Count),
                result.Document.Lines.Select(line => line.Trace.LineNumber));
        }

        [Fact]
        public void ReadsMultipagePdfAndRestartsLineNumberOnEachPage()
        {
            DocumentReadResult result = new TextPdfStrapDocumentReader()
                .Read(FixturePaths.Get("reactions-multi-page.pdf"));

            Assert.False(result.IsBlocked);
            Assert.Contains(result.Document.Lines, line => line.Trace.PageNumber == 1);
            Assert.Contains(result.Document.Lines, line => line.Trace.PageNumber == 2);
            Assert.Equal(
                1,
                result.Document.Lines
                    .First(line => line.Trace.PageNumber == 2)
                    .Trace.LineNumber);
        }

        [Fact]
        public void PdfWithoutTextRequestsOcrSpecifically()
        {
            DocumentReadResult result = new TextPdfStrapDocumentReader()
                .Read(FixturePaths.Get("reactions-no-text.pdf"));

            ReaderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(ReaderDiagnosticCodes.PdfNoText, diagnostic.Code);
            Assert.True(diagnostic.RequiresOcr);
        }

        [Fact]
        public void PdfWithInsufficientTextIsDistinguishedFromNoText()
        {
            DocumentReadResult result = new TextPdfStrapDocumentReader()
                .Read(FixturePaths.Get("reactions-insufficient-text.pdf"));

            ReaderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(ReaderDiagnosticCodes.PdfInsufficientText, diagnostic.Code);
            Assert.True(diagnostic.RequiresOcr);
        }

        [Fact]
        public void PdfWithTextButUnrecognizableStructureDoesNotClaimScannedFile()
        {
            DocumentReadResult result = new TextPdfStrapDocumentReader()
                .Read(FixturePaths.Get("reactions-unstructured.pdf"));

            ReaderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(ReaderDiagnosticCodes.PdfUnrecognizableStructure, diagnostic.Code);
            Assert.False(diagnostic.RequiresOcr);
        }

        [Fact]
        public void CorruptPdfIsRejected()
        {
            DocumentReadResult result = new TextPdfStrapDocumentReader()
                .Read(FixturePaths.Get("corrupt.pdf"));

            Assert.True(result.IsBlocked);
            Assert.Contains(
                result.Diagnostics,
                item => item.Code == ReaderDiagnosticCodes.CorruptDocument);
        }
    }
}
