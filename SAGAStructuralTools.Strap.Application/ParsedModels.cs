using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Strap.Application
{
    public enum ReportCaseKind
    {
        Maximum,
        Minimum
    }

    public enum EffortComponent
    {
        Fx,
        Fy,
        Fz,
        Mx,
        My,
        Mz
    }

    public sealed class ParsedEffort
    {
        public ParsedEffort(
            EffortComponent component,
            decimal value,
            string rawValue,
            string combinationId,
            string rawCombination)
        {
            Component = component;
            Value = value;
            RawValue = rawValue ?? throw new ArgumentNullException(nameof(rawValue));
            CombinationId = combinationId;
            RawCombination = rawCombination;
        }

        public EffortComponent Component { get; }
        public decimal Value { get; }
        public string RawValue { get; }
        public string CombinationId { get; }
        public string RawCombination { get; }
    }

    public sealed class ParsedReactionRow
    {
        public ParsedReactionRow(
            ReportCaseKind caseKind,
            string rawCaseLabel,
            IEnumerable<ParsedEffort> efforts,
            string unit,
            ExtractedLine source)
        {
            CaseKind = caseKind;
            RawCaseLabel = rawCaseLabel;
            Efforts = (efforts ?? throw new ArgumentNullException(nameof(efforts))).ToArray();
            Unit = unit;
            Source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public ReportCaseKind CaseKind { get; }
        public string RawCaseLabel { get; }
        public IReadOnlyCollection<ParsedEffort> Efforts { get; }
        public string Unit { get; }
        public ExtractedLine Source { get; }
    }

    public sealed class RecognizedNode
    {
        public RecognizedNode(
            string nodeId,
            ExtractedLine declaration,
            IEnumerable<ParsedReactionRow> rows)
        {
            NodeId = nodeId;
            Declaration = declaration;
            Rows = (rows ?? throw new ArgumentNullException(nameof(rows))).ToArray();
        }

        public string NodeId { get; }
        public ExtractedLine Declaration { get; }
        public IReadOnlyCollection<ParsedReactionRow> Rows { get; }
    }

    public sealed class StrapParseResult
    {
        public StrapParseResult(
            IEnumerable<RecognizedNode> nodes,
            IEnumerable<string> units,
            IEnumerable<ImportDiagnostic> diagnostics)
        {
            Nodes = nodes.ToArray();
            Units = units.ToArray();
            Diagnostics = diagnostics.ToArray();
        }

        public IReadOnlyCollection<RecognizedNode> Nodes { get; }
        public IReadOnlyCollection<string> Units { get; }
        public IReadOnlyCollection<ImportDiagnostic> Diagnostics { get; }
    }
}
