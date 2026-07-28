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

    public sealed class ParsedUnitSet
    {
        public ParsedUnitSet(
            string forceUnit,
            string momentUnit,
            string rawText,
            ExtractedLine source)
        {
            ForceUnit = forceUnit;
            MomentUnit = momentUnit;
            RawText = rawText;
            Source = source;
        }

        public string ForceUnit { get; }
        public string MomentUnit { get; }
        public string RawText { get; }
        public ExtractedLine Source { get; }
        public string Combined =>
            string.IsNullOrWhiteSpace(MomentUnit)
                ? ForceUnit
                : ForceUnit + ", " + MomentUnit;
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
            : this(nodes, units, diagnostics, null)
        {
        }

        public StrapParseResult(
            IEnumerable<RecognizedNode> nodes,
            IEnumerable<string> units,
            IEnumerable<ImportDiagnostic> diagnostics,
            IEnumerable<ParsedUnitSet> unitSets)
        {
            Nodes = nodes.ToArray();
            Units = units.ToArray();
            Diagnostics = diagnostics.ToArray();
            UnitSets = (unitSets ?? Array.Empty<ParsedUnitSet>()).ToArray();
        }

        public IReadOnlyCollection<RecognizedNode> Nodes { get; }
        public IReadOnlyCollection<string> Units { get; }
        public IReadOnlyCollection<ImportDiagnostic> Diagnostics { get; }
        public IReadOnlyCollection<ParsedUnitSet> UnitSets { get; }
        public IReadOnlyCollection<string> ForceUnits => UnitSets
            .Select(unit => unit.ForceUnit)
            .Where(unit => !string.IsNullOrWhiteSpace(unit))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        public IReadOnlyCollection<string> MomentUnits => UnitSets
            .Select(unit => unit.MomentUnit)
            .Where(unit => !string.IsNullOrWhiteSpace(unit))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
