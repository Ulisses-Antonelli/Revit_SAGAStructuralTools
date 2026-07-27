using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Strap.Application
{
    public sealed class SourceTrace
    {
        public SourceTrace(
            string fileName,
            int? pageNumber,
            int? tableNumber,
            int lineNumber)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("O arquivo de origem é obrigatório.", nameof(fileName));
            if (pageNumber.HasValue && pageNumber.Value < 1)
                throw new ArgumentOutOfRangeException(nameof(pageNumber));
            if (tableNumber.HasValue && tableNumber.Value < 1)
                throw new ArgumentOutOfRangeException(nameof(tableNumber));
            if (lineNumber < 1)
                throw new ArgumentOutOfRangeException(nameof(lineNumber));

            FileName = fileName;
            PageNumber = pageNumber;
            TableNumber = tableNumber;
            LineNumber = lineNumber;
        }

        public string FileName { get; }
        public int? PageNumber { get; }
        public int? TableNumber { get; }
        public int LineNumber { get; }
    }

    public sealed class ExtractedLine
    {
        public ExtractedLine(string rawText, SourceTrace trace)
        {
            RawText = rawText ?? string.Empty;
            Trace = trace ?? throw new ArgumentNullException(nameof(trace));
        }

        public string RawText { get; }
        public SourceTrace Trace { get; }
    }

    public sealed class ExtractedDocument
    {
        public ExtractedDocument(string fileName, IEnumerable<ExtractedLine> lines)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("O arquivo de origem é obrigatório.", nameof(fileName));
            if (lines == null)
                throw new ArgumentNullException(nameof(lines));

            FileName = fileName;
            Lines = lines.ToArray();
        }

        public string FileName { get; }
        public IReadOnlyCollection<ExtractedLine> Lines { get; }
    }
}
