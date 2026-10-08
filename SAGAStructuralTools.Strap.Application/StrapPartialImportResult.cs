using System;
using System.Collections.Generic;
using System.Linq;
using SAGAStructuralTools.Strap.Domain;

namespace SAGAStructuralTools.Strap.Application
{
    public sealed class StrapNodeImportResult
    {
        public StrapNodeImportResult(
            RecognizedNode node,
            ConsolidatedReaction reaction,
            IEnumerable<ImportDiagnostic> diagnostics)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            Reaction = reaction;
            Diagnostics = (diagnostics ?? Array.Empty<ImportDiagnostic>()).ToArray();
        }

        public RecognizedNode Node { get; }
        public string NodeId => Node.NodeId;
        public ConsolidatedReaction Reaction { get; }
        public IReadOnlyCollection<ImportDiagnostic> Diagnostics { get; }
        public bool IsValid =>
            Reaction != null &&
            !Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error) &&
            !Diagnostics.Any(item =>
                item.Code == DiagnosticCodes.DuplicateNodeIdentical ||
                item.Code == DiagnosticCodes.DuplicateNodeConflict);
    }

    public sealed class StrapPartialImportResult
    {
        public StrapPartialImportResult(
            ExtractedDocument source,
            StrapParseResult parseResult,
            IEnumerable<ImportDiagnostic> globalDiagnostics,
            IEnumerable<StrapNodeImportResult> nodes)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            ParseResult = parseResult ?? throw new ArgumentNullException(nameof(parseResult));
            GlobalDiagnostics = (globalDiagnostics ?? Array.Empty<ImportDiagnostic>()).ToArray();
            Nodes = (nodes ?? Array.Empty<StrapNodeImportResult>()).ToArray();
        }

        public ExtractedDocument Source { get; }
        public StrapParseResult ParseResult { get; }
        public IReadOnlyCollection<ImportDiagnostic> GlobalDiagnostics { get; }
        public IReadOnlyCollection<StrapNodeImportResult> Nodes { get; }
        public bool IsGloballyBlocked =>
            GlobalDiagnostics.Any(item => item.Severity == DiagnosticSeverity.Error);
        public int ValidNodeCount => Nodes.Count(item => item.IsValid);
        public int InvalidNodeCount => Nodes.Count - ValidNodeCount;
    }
}
