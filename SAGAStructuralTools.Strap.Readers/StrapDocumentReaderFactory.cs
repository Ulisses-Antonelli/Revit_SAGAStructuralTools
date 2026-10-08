using System;
using System.Collections.Generic;
using System.IO;

namespace SAGAStructuralTools.Strap.Readers
{
    public sealed class StrapDocumentReaderFactory
    {
        private readonly IReadOnlyDictionary<string, IStrapDocumentReader> _readers;

        public StrapDocumentReaderFactory()
        {
            _readers = new Dictionary<string, IStrapDocumentReader>(
                StringComparer.OrdinalIgnoreCase)
            {
                [".txt"] = new TxtStrapDocumentReader(),
                [".docx"] = new DocxStrapDocumentReader(),
                [".rtf"] = new RtfStrapDocumentReader(),
                [".pdf"] = new TextPdfStrapDocumentReader()
            };
        }

        public IStrapDocumentReader Create(string filePath)
        {
            string extension = Path.GetExtension(filePath ?? string.Empty);
            IStrapDocumentReader reader;
            if (_readers.TryGetValue(extension, out reader))
                return reader;

            throw new NotSupportedException(
                $"A extensão '{extension}' não é suportada. " +
                "Use arquivos TXT, DOCX, RTF ou PDF textual.");
        }
    }
}
