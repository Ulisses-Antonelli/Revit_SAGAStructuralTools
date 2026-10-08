using System;
using System.Collections.Generic;
using System.Linq;
using SAGAStructuralTools.Strap.Application;

namespace SAGAStructuralTools.Strap.Readers
{
    public sealed class ExtractedTableRow
    {
        public ExtractedTableRow(
            IEnumerable<string> cells,
            SourceTrace trace)
        {
            Cells = (cells ?? throw new ArgumentNullException(nameof(cells))).ToArray();
            Trace = trace ?? throw new ArgumentNullException(nameof(trace));
        }

        public IReadOnlyCollection<string> Cells { get; }
        public SourceTrace Trace { get; }
    }

    public sealed class DocumentReadResult
    {
        public DocumentReadResult(
            ExtractedDocument document,
            IEnumerable<ReaderDiagnostic> diagnostics = null,
            IEnumerable<ExtractedTableRow> tableRows = null)
        {
            Document = document;
            Diagnostics = (diagnostics ?? Array.Empty<ReaderDiagnostic>()).ToArray();
            TableRows = (tableRows ?? Array.Empty<ExtractedTableRow>()).ToArray();
        }

        public ExtractedDocument Document { get; }
        public IReadOnlyCollection<ReaderDiagnostic> Diagnostics { get; }
        public IReadOnlyCollection<ExtractedTableRow> TableRows { get; }
        public bool IsBlocked =>
            Diagnostics.Any(item => item.Severity == ReaderDiagnosticSeverity.Error);

        public static DocumentReadResult Failure(ReaderDiagnostic diagnostic)
            => new DocumentReadResult(null, new[] { diagnostic });
    }
}
