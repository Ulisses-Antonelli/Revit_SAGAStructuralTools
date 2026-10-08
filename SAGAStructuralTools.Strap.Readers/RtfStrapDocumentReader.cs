using System;
using System.IO;
using System.Text;
using SAGAStructuralTools.Strap.Application;
using SAGAStructuralTools.Strap.Readers.Rtf;
using SAGAStructuralTools.Strap.Readers.Text;

namespace SAGAStructuralTools.Strap.Readers
{
    public sealed class RtfStrapDocumentReader : IStrapDocumentReader
    {
        private readonly IRtfTextExtractor _extractor;

        public RtfStrapDocumentReader(IRtfTextExtractor extractor = null)
        {
            _extractor = extractor ?? new WpfRtfTextExtractor();
        }

        public DocumentReadResult Read(string filePath)
        {
            DocumentReadResult invalid = ReaderGuard.ValidateFile(filePath);
            if (invalid != null)
                return invalid;

            try
            {
                if (!HasRtfHeader(filePath))
                    return ReaderGuard.Corrupt(filePath, "RTF");

                string text = _extractor.Extract(filePath);
                if (string.IsNullOrWhiteSpace(text))
                    return ReaderGuard.Corrupt(filePath, "RTF");

                return new DocumentReadResult(new ExtractedDocument(
                    filePath,
                    ExtractedLineBuilder.FromText(text, filePath)));
            }
            catch (Exception)
            {
                return ReaderGuard.Corrupt(filePath, "RTF");
            }
        }

        private static bool HasRtfHeader(string filePath)
        {
            byte[] header = new byte[5];
            using (FileStream stream = File.OpenRead(filePath))
            {
                if (stream.Read(header, 0, header.Length) != header.Length)
                    return false;
            }
            return string.Equals(
                Encoding.ASCII.GetString(header),
                @"{\rtf",
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
