using Xunit;

namespace SAGAStructuralTools.Strap.Readers.Tests
{
    public sealed class RtfStrapDocumentReaderTests
    {
        [Fact]
        public void ReadsRtfThroughIsolatedWpfAdapter()
        {
            DocumentReadResult result = new RtfStrapDocumentReader()
                .Read(FixturePaths.Get("reactions.rtf"));

            Assert.False(result.IsBlocked);
            Assert.Contains(
                result.Document.Lines,
                line => line.RawText.Contains("FX") && line.RawText.Contains("MZ"));
        }

        [Fact]
        public void CorruptRtfIsRejected()
        {
            DocumentReadResult result = new RtfStrapDocumentReader()
                .Read(FixturePaths.Get("corrupt.rtf"));

            Assert.True(result.IsBlocked);
            Assert.Contains(
                result.Diagnostics,
                item => item.Code == ReaderDiagnosticCodes.CorruptDocument);
        }
    }
}
