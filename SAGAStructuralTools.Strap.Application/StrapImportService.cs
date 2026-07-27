using System;
using System.Collections.Generic;
using System.Linq;
using SAGAStructuralTools.Strap.Domain;

namespace SAGAStructuralTools.Strap.Application
{
    public interface IStrapImportService
    {
        StrapImportResult Import(ExtractedDocument document, bool rotate90 = false);
    }

    public sealed class StrapImportService : IStrapImportService
    {
        private readonly IStrapReportParser _parser;
        private readonly IStrapImportValidator _validator;

        public StrapImportService(
            IStrapReportParser parser = null,
            IStrapImportValidator validator = null)
        {
            _parser = parser ?? new StrapReportParser();
            _validator = validator ?? new StrapImportValidator();
        }

        public StrapImportResult Import(ExtractedDocument document, bool rotate90 = false)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            StrapParseResult parseResult = _parser.Parse(document);
            var diagnostics = parseResult.Diagnostics
                .Concat(_validator.Validate(parseResult))
                .ToList();
            var consolidated = new List<ConsolidatedReaction>();

            if (!diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
            {
                foreach (RecognizedNode node in parseResult.Nodes
                    .GroupBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First()))
                {
                    ParsedReactionRow maximum = node.Rows.Single(
                        row => row.CaseKind == ReportCaseKind.Maximum);
                    ParsedReactionRow minimum = node.Rows.Single(
                        row => row.CaseKind == ReportCaseKind.Minimum);
                    consolidated.Add(ReactionConsolidator.ConsolidateNode(
                        node.NodeId,
                        ToForceMoment(maximum),
                        ToForceMoment(minimum),
                        rotate90));
                }
            }

            return new StrapImportResult(document, parseResult, diagnostics, consolidated);
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
