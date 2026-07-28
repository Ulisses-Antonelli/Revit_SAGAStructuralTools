using System;
using System.Collections.Generic;
using System.Linq;
using SAGAStructuralTools.Strap.Domain;

namespace SAGAStructuralTools.Strap.Application
{
    public sealed class StrapImportResult
    {
        public StrapImportResult(
            ExtractedDocument source,
            StrapParseResult parseResult,
            IEnumerable<ImportDiagnostic> diagnostics,
            IEnumerable<ConsolidatedReaction> consolidatedReactions)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            ParseResult = parseResult ?? throw new ArgumentNullException(nameof(parseResult));
            Diagnostics = diagnostics.ToArray();
            ConsolidatedReactions = consolidatedReactions.ToArray();
            Combinations = parseResult.Nodes
                .SelectMany(node => node.Rows)
                .SelectMany(row => row.Efforts)
                .Select(effort => effort.CombinationId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public ExtractedDocument Source { get; }
        public IReadOnlyCollection<ExtractedLine> RawLines => Source.Lines;
        public StrapParseResult ParseResult { get; }
        public IReadOnlyCollection<RecognizedNode> Nodes => ParseResult.Nodes;
        public IReadOnlyCollection<string> Units => ParseResult.Units;
        public IReadOnlyCollection<string> ForceUnits => ParseResult.ForceUnits;
        public IReadOnlyCollection<string> MomentUnits => ParseResult.MomentUnits;
        public IReadOnlyCollection<string> Combinations { get; }
        public IReadOnlyCollection<ImportDiagnostic> Diagnostics { get; }
        public IReadOnlyCollection<ImportDiagnostic> Warnings =>
            Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Warning).ToArray();
        public IReadOnlyCollection<ImportDiagnostic> Errors =>
            Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error).ToArray();
        public IReadOnlyCollection<ConsolidatedReaction> ConsolidatedReactions { get; }
        public bool IsBlocked => Errors.Count > 0;
    }
}
