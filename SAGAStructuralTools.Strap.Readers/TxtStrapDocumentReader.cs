using System;
using System.IO;
using System.Text;
using SAGAStructuralTools.Strap.Application;
using SAGAStructuralTools.Strap.Readers.Text;

namespace SAGAStructuralTools.Strap.Readers
{
    public sealed class TxtStrapDocumentReader : IStrapDocumentReader
    {
        public DocumentReadResult Read(string filePath)
        {
            DocumentReadResult invalid = ReaderGuard.ValidateFile(filePath);
            if (invalid != null)
                return invalid;

            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                Encoding encoding = TextEncodingDetector.Detect(bytes);
                string text = encoding.GetString(bytes);
                if (text.Length > 0 && text[0] == '\uFEFF')
                    text = text.Substring(1);

                return new DocumentReadResult(new ExtractedDocument(
                    filePath,
                    ExtractedLineBuilder.FromText(text, filePath)));
            }
            catch (Exception)
            {
                return ReaderGuard.Corrupt(filePath, "TXT");
            }
        }
    }
}
