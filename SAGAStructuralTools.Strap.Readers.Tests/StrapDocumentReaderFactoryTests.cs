using System;
using Xunit;

namespace SAGAStructuralTools.Strap.Readers.Tests
{
    public sealed class StrapDocumentReaderFactoryTests
    {
        [Theory]
        [InlineData("file.TXT", typeof(TxtStrapDocumentReader))]
        [InlineData("file.docx", typeof(DocxStrapDocumentReader))]
        [InlineData("file.RtF", typeof(RtfStrapDocumentReader))]
        [InlineData("file.pdf", typeof(TextPdfStrapDocumentReader))]
        public void SelectsReaderByExtensionCaseInsensitively(
            string fileName,
            Type expectedType)
        {
            IStrapDocumentReader reader =
                new StrapDocumentReaderFactory().Create(fileName);

            Assert.IsType(expectedType, reader);
        }

        [Fact]
        public void UnsupportedExtensionHasClearMessage()
        {
            NotSupportedException error = Assert.Throws<NotSupportedException>(
                () => new StrapDocumentReaderFactory().Create("report.xls"));

            Assert.Contains(".xls", error.Message);
            Assert.Contains("TXT, DOCX, RTF ou PDF", error.Message);
        }
    }
}
