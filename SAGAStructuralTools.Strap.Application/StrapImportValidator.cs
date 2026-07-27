using System;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Strap.Application
{
    public interface IStrapImportValidator
    {
        IReadOnlyCollection<ImportDiagnostic> Validate(StrapParseResult parseResult);
    }

    public sealed class StrapImportValidator : IStrapImportValidator
    {
        private static readonly EffortComponent[] Components =
            (EffortComponent[])Enum.GetValues(typeof(EffortComponent));

        public IReadOnlyCollection<ImportDiagnostic> Validate(StrapParseResult parseResult)
        {
            if (parseResult == null)
                throw new ArgumentNullException(nameof(parseResult));

            var diagnostics = new List<ImportDiagnostic>();
            ValidateUnits(parseResult, diagnostics);
            ValidateNodes(parseResult, diagnostics);
            ValidateDuplicates(parseResult, diagnostics);
            return diagnostics;
        }

        private static void ValidateUnits(
            StrapParseResult parseResult,
            ICollection<ImportDiagnostic> diagnostics)
        {
            if (parseResult.Units.Count == 0)
            {
                diagnostics.Add(new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.MissingUnit,
                    "As unidades do relatório não foram encontradas."));
            }
            else if (parseResult.Units.Count > 1)
            {
                diagnostics.Add(new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.InconsistentUnit,
                    "Foram encontradas unidades inconsistentes no documento: " +
                    string.Join(", ", parseResult.Units) + "."));
            }

            foreach (ParsedReactionRow row in parseResult.Nodes.SelectMany(node => node.Rows))
            {
                if (string.IsNullOrWhiteSpace(row.Unit))
                {
                    diagnostics.Add(new ImportDiagnostic(
                        DiagnosticSeverity.Error,
                        DiagnosticCodes.MissingUnit,
                        "A linha de reação não possui unidade associada.",
                        row.Source.Trace));
                }
            }
        }

        private static void ValidateNodes(
            StrapParseResult parseResult,
            ICollection<ImportDiagnostic> diagnostics)
        {
            foreach (RecognizedNode node in parseResult.Nodes)
            {
                ParsedReactionRow[] maximumRows = node.Rows
                    .Where(row => row.CaseKind == ReportCaseKind.Maximum)
                    .ToArray();
                ParsedReactionRow[] minimumRows = node.Rows
                    .Where(row => row.CaseKind == ReportCaseKind.Minimum)
                    .ToArray();

                if (maximumRows.Length != 1)
                {
                    diagnostics.Add(new ImportDiagnostic(
                        DiagnosticSeverity.Error,
                        DiagnosticCodes.MissingMaximum,
                        maximumRows.Length == 0
                            ? "A linha Máx do nó está ausente."
                            : "O nó possui mais de uma linha Máx.",
                        node.Declaration?.Trace,
                        node.NodeId));
                }
                if (minimumRows.Length != 1)
                {
                    diagnostics.Add(new ImportDiagnostic(
                        DiagnosticSeverity.Error,
                        DiagnosticCodes.MissingMinimum,
                        minimumRows.Length == 0
                            ? "A linha Mín do nó está ausente."
                            : "O nó possui mais de uma linha Mín.",
                        node.Declaration?.Trace,
                        node.NodeId));
                }

                foreach (ParsedReactionRow row in node.Rows)
                {
                    ValidateEfforts(node, row, diagnostics);
                }
            }
        }

        private static void ValidateEfforts(
            RecognizedNode node,
            ParsedReactionRow row,
            ICollection<ImportDiagnostic> diagnostics)
        {
            bool hasSixUniqueComponents =
                row.Efforts.Count == 6 &&
                Components.All(component =>
                    row.Efforts.Count(effort => effort.Component == component) == 1);
            if (!hasSixUniqueComponents)
            {
                diagnostics.Add(new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.UnexpectedEffortCount,
                    "A linha deve conter exatamente FX, FY, FZ, MX, MY e MZ uma vez cada.",
                    row.Source.Trace,
                    node.NodeId));
            }

            int combinations = row.Efforts.Count(effort =>
                !string.IsNullOrWhiteSpace(effort.CombinationId));
            if (combinations > 0 && combinations != row.Efforts.Count)
            {
                diagnostics.Add(new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.MissingCombination,
                    "Uma ou mais combinações estão ausentes na linha.",
                    row.Source.Trace,
                    node.NodeId));
            }
        }

        private static void ValidateDuplicates(
            StrapParseResult parseResult,
            ICollection<ImportDiagnostic> diagnostics)
        {
            foreach (IGrouping<string, RecognizedNode> group in parseResult.Nodes
                .GroupBy(node => node.NodeId, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1))
            {
                RecognizedNode first = group.First();
                bool identical = group.Skip(1).All(node => AreEquivalent(first, node));
                diagnostics.Add(new ImportDiagnostic(
                    identical ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
                    identical
                        ? DiagnosticCodes.DuplicateNodeIdentical
                        : DiagnosticCodes.DuplicateNodeConflict,
                    identical
                        ? $"O nó '{group.Key}' aparece repetido com dados idênticos."
                        : $"O nó '{group.Key}' aparece repetido com dados conflitantes.",
                    group.Skip(1).First().Declaration?.Trace,
                    group.Key));
            }
        }

        private static bool AreEquivalent(RecognizedNode left, RecognizedNode right)
        {
            if (left.Rows.Count != right.Rows.Count)
                return false;

            foreach (ReportCaseKind kind in Enum.GetValues(typeof(ReportCaseKind)))
            {
                ParsedReactionRow leftRow = left.Rows.SingleOrDefault(row => row.CaseKind == kind);
                ParsedReactionRow rightRow = right.Rows.SingleOrDefault(row => row.CaseKind == kind);
                if (leftRow == null || rightRow == null || !RowsEquivalent(leftRow, rightRow))
                    return false;
            }
            return true;
        }

        private static bool RowsEquivalent(ParsedReactionRow left, ParsedReactionRow right)
        {
            if (!string.Equals(left.Unit, right.Unit, StringComparison.OrdinalIgnoreCase) ||
                left.Efforts.Count != right.Efforts.Count)
            {
                return false;
            }

            foreach (ParsedEffort effort in left.Efforts)
            {
                ParsedEffort other = right.Efforts.SingleOrDefault(
                    candidate => candidate.Component == effort.Component);
                if (other == null ||
                    other.Value != effort.Value ||
                    !string.Equals(
                        other.CombinationId,
                        effort.CombinationId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
