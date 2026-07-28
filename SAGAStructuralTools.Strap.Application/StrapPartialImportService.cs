using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SAGAStructuralTools.Strap.Domain;

namespace SAGAStructuralTools.Strap.Application
{
    public interface IStrapPartialImportService
    {
        StrapPartialImportResult Import(ExtractedDocument document);
    }

    public sealed class StrapPartialImportService : IStrapPartialImportService
    {
        private readonly IStrapReportParser _parser;
        private readonly IStrapImportValidator _validator;

        public StrapPartialImportService(
            IStrapReportParser parser = null,
            IStrapImportValidator validator = null)
        {
            _parser = parser ?? new StrapReportParser();
            _validator = validator ?? new StrapImportValidator();
        }

        public StrapPartialImportResult Import(ExtractedDocument document)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            StrapParseResult parsed = _parser.Parse(document);
            ImportDiagnostic[] diagnostics = parsed.Diagnostics
                .Concat(_validator.Validate(parsed))
                .Concat(ValidateUnits(parsed))
                .ToArray();
            ImportDiagnostic[] globals = diagnostics
                .Where(item => string.IsNullOrWhiteSpace(item.NodeId))
                .ToArray();
            var nodeResults = new List<StrapNodeImportResult>();

            foreach (RecognizedNode node in parsed.Nodes)
            {
                ImportDiagnostic[] nodeDiagnostics = diagnostics
                    .Where(item => string.Equals(
                        item.NodeId,
                        node.NodeId,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                bool invalid = nodeDiagnostics.Any(item =>
                    item.Severity == DiagnosticSeverity.Error ||
                    item.Code == DiagnosticCodes.DuplicateNodeIdentical ||
                    item.Code == DiagnosticCodes.DuplicateNodeConflict);
                ConsolidatedReaction reaction = null;
                if (!invalid && !globals.Any(item => item.Severity == DiagnosticSeverity.Error))
                {
                    try
                    {
                        ParsedReactionRow maximum = node.Rows.Single(
                            row => row.CaseKind == ReportCaseKind.Maximum);
                        ParsedReactionRow minimum = node.Rows.Single(
                            row => row.CaseKind == ReportCaseKind.Minimum);
                        reaction = ReactionConsolidator.ConsolidateNode(
                            node.NodeId,
                            ToForceMoment(maximum),
                            ToForceMoment(minimum),
                            rotate90: false);
                    }
                    catch (Exception ex)
                    {
                        nodeDiagnostics = nodeDiagnostics.Concat(new[]
                        {
                            new ImportDiagnostic(
                                DiagnosticSeverity.Error,
                                DiagnosticCodes.UnrecognizedReactionRow,
                                ex.Message,
                                node.Declaration?.Trace,
                                node.NodeId)
                        }).ToArray();
                    }
                }
                nodeResults.Add(new StrapNodeImportResult(
                    node,
                    reaction,
                    nodeDiagnostics));
            }

            return new StrapPartialImportResult(document, parsed, globals, nodeResults);
        }

        private static IEnumerable<ImportDiagnostic> ValidateUnits(StrapParseResult parsed)
        {
            if (parsed.ForceUnits.Count != 1 ||
                !IsForceUnit(parsed.ForceUnits.SingleOrDefault()) ||
                parsed.MomentUnits.Count != 1 ||
                !IsMomentUnit(parsed.MomentUnits.SingleOrDefault()))
            {
                yield return new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.InconsistentUnit,
                    "A primeira versão aceita somente forças em tf e momentos em tf·m.");
            }
        }

        public static bool IsForceUnit(string value)
            => NormalizeUnit(value) == "tf";

        public static bool IsMomentUnit(string value)
        {
            string normalized = NormalizeUnit(value)
                .Replace("metros", "m")
                .Replace("metro", "m")
                .Replace("×", string.Empty)
                .Replace("·", string.Empty)
                .Replace("*", string.Empty)
                .Replace("x", string.Empty)
                .Replace(".", string.Empty);
            return normalized == "tfm";
        }

        private static string NormalizeUnit(string value)
        {
            string decomposed = (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Normalize(NormalizationForm.FormD);
            var result = new StringBuilder();
            foreach (char character in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) !=
                    UnicodeCategory.NonSpacingMark &&
                    !char.IsWhiteSpace(character))
                {
                    result.Append(character);
                }
            }
            return result.ToString().Normalize(NormalizationForm.FormC);
        }

        private static ForceMoment ToForceMoment(ParsedReactionRow row)
        {
            decimal Get(EffortComponent component)
                => row.Efforts.Single(effort => effort.Component == component).Value;
            return new ForceMoment(
                Get(EffortComponent.Fx),
                Get(EffortComponent.Fy),
                Get(EffortComponent.Fz),
                Get(EffortComponent.Mx),
                Get(EffortComponent.My),
                Get(EffortComponent.Mz));
        }
    }
}
