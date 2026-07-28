using System;
using System.IO;
using System.Linq;
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

        [Fact]
        public void RestoresLiteralWindows1252TextWhenCodePageIsNotDeclared()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "strap-anonymous-" + Guid.NewGuid().ToString("N") + ".rtf");
            string synthetic =
                @"{\rtf1\ansi\uc1 REAÇÕES (Unids: tf, tf*metro)\par " +
                @"nº cmb X1 X2 X3 X4 X5 X6\par " +
                @"1 Máx 1 2 3 4 5 6\par Comb 1 2 3 4 5 6\par " +
                @"Mín -1 -2 -3 -4 -5 -6\par Comb 7 8 9 10 11 12}";
            try
            {
                File.WriteAllBytes(
                    path,
                    synthetic.Select(character => checked((byte)character)).ToArray());

                DocumentReadResult result = new RtfStrapDocumentReader().Read(path);

                Assert.False(result.IsBlocked);
                Assert.Contains(result.Document.Lines, line => line.RawText.Contains("REAÇÕES"));
                Assert.Contains(result.Document.Lines, line => line.RawText.Contains("Máx"));
                Assert.Contains(result.Document.Lines, line => line.RawText.Contains("Mín"));
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }
}
