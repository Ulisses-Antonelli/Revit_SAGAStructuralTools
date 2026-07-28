using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SAGAStructuralTools.Strap.Application
{
    public interface IStrapReportParser
    {
        StrapParseResult Parse(ExtractedDocument document);
    }

    public sealed class StrapReportParser : IStrapReportParser
    {
        private static readonly EffortComponent[] CanonicalOrder =
        {
            EffortComponent.Fx,
            EffortComponent.Fy,
            EffortComponent.Fz,
            EffortComponent.Mx,
            EffortComponent.My,
            EffortComponent.Mz
        };

        private static readonly Regex NodeRegex = new Regex(
            @"^\s*(?:N[ÓO]|NODE)\s*[:#-]?\s*(?<id>[A-Za-z0-9_.-]+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex UnitRegex = new Regex(
            @"^\s*UNIDADES?\s*[:=-]\s*(?<unit>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex EmbeddedUnitsRegex = new Regex(
            @"\bUNIDS?\s*:\s*(?<force>[^,;)]+?)\s*,\s*(?<moment>[^;)]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex CaseRegex = new Regex(
            @"^\s*(?<label>M[ÁA]X(?:IMO)?|MAX(?:IMUM)?|M[ÍI]N(?:IMO)?|MIN(?:IMUM)?)\b(?<body>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex LabeledEffortRegex = new Regex(
            @"(?<name>FX|FY|FZ|MX|MY|MZ|X[1-6])\s*[:=]\s*" +
            @"(?<value>[+-]?(?:\d+(?:[.,]\d+)?|[.,]\d+))" +
            @"(?:\s*(?:/|\(|\[)\s*(?<comb>[A-Za-z0-9_.-]+)\s*(?:\)|\])?)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex PairRegex = new Regex(
            @"(?<value>[+-]?(?:\d+(?:[.,]\d+)?|[.,]\d+))" +
            @"\s*(?:/|\(|\[)\s*(?<comb>[A-Za-z0-9_.-]+)\s*(?:\)|\])?",
            RegexOptions.CultureInvariant);

        private static readonly Regex NumberRegex = new Regex(
            @"[+-]?(?:\d+(?:[.,]\d+)?|[.,]\d+)",
            RegexOptions.CultureInvariant);

        public StrapParseResult Parse(ExtractedDocument document)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            ExtractedLine[] documentLines = document.Lines.ToArray();
            var nodes = new List<RecognizedNode>();
            var diagnostics = new List<ImportDiagnostic>();
            var units = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unitSets = new List<ParsedUnitSet>();
            var unitAtLine = new Dictionary<ExtractedLine, string>();
            string currentUnit = null;

            foreach (ExtractedLine line in documentLines)
            {
                string text = line.RawText ?? string.Empty;
                Match embedded = EmbeddedUnitsRegex.Match(text);
                Match legacy = UnitRegex.Match(text);
                if (embedded.Success)
                {
                    string force = NormalizeWhitespace(embedded.Groups["force"].Value);
                    string moment = NormalizeWhitespace(embedded.Groups["moment"].Value);
                    var unitSet = new ParsedUnitSet(force, moment, embedded.Value, line);
                    currentUnit = unitSet.Combined;
                    units.Add(currentUnit);
                    unitSets.Add(unitSet);
                }
                else if (legacy.Success)
                {
                    currentUnit = NormalizeWhitespace(legacy.Groups["unit"].Value);
                    units.Add(currentUnit);
                    unitSets.Add(new ParsedUnitSet(currentUnit, null, legacy.Value, line));
                }
                unitAtLine[line] = currentUnit;
            }

            var tabular = new TabularStrapBlockParser(
                line => unitAtLine.TryGetValue(line, out string value) ? value : null)
                .Parse(documentLines);
            nodes.AddRange(tabular.Nodes);
            diagnostics.AddRange(tabular.Diagnostics);

            var rows = new List<ParsedReactionRow>();
            string currentNodeId = null;
            ExtractedLine currentDeclaration = null;
            EffortComponent[] currentColumns = null;

            Action flushNode = () =>
            {
                if (currentNodeId == null)
                    return;
                nodes.Add(new RecognizedNode(currentNodeId, currentDeclaration, rows.ToArray()));
                rows.Clear();
            };

            foreach (ExtractedLine line in documentLines)
            {
                string text = line.RawText ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (EmbeddedUnitsRegex.IsMatch(text) || UnitRegex.IsMatch(text))
                {
                    continue;
                }

                if (tabular.HandledLines.Contains(line))
                    continue;

                currentUnit = unitAtLine[line];

                Match nodeMatch = NodeRegex.Match(text);
                if (nodeMatch.Success)
                {
                    flushNode();
                    currentNodeId = nodeMatch.Groups["id"].Value;
                    currentDeclaration = line;
                    continue;
                }

                EffortComponent[] columns;
                if (TryParseHeader(text, out columns))
                {
                    currentColumns = columns;
                    continue;
                }

                Match caseMatch = CaseRegex.Match(text);
                if (!caseMatch.Success)
                    continue;

                if (currentNodeId == null)
                {
                    diagnostics.Add(new ImportDiagnostic(
                        DiagnosticSeverity.Error,
                        DiagnosticCodes.MissingNode,
                        "Foi encontrada uma linha Máx/Mín sem declaração de nó.",
                        line.Trace));
                    continue;
                }

                ParsedReactionRow parsedRow;
                ImportDiagnostic rowError;
                if (TryParseReactionRow(
                    caseMatch,
                    currentColumns,
                    currentUnit,
                    line,
                    currentNodeId,
                    out parsedRow,
                    out rowError))
                {
                    rows.Add(parsedRow);
                }
                else
                {
                    diagnostics.Add(rowError);
                }
            }

            flushNode();
            return new StrapParseResult(nodes, units, diagnostics, unitSets);
        }

        private static bool TryParseReactionRow(
            Match caseMatch,
            EffortComponent[] columns,
            string unit,
            ExtractedLine source,
            string nodeId,
            out ParsedReactionRow row,
            out ImportDiagnostic error)
        {
            string label = caseMatch.Groups["label"].Value;
            string body = caseMatch.Groups["body"].Value;
            ReportCaseKind kind = IsMaximum(label)
                ? ReportCaseKind.Maximum
                : ReportCaseKind.Minimum;

            MatchCollection labeledMatches = LabeledEffortRegex.Matches(body);
            if (labeledMatches.Count > 0)
            {
                var labeled = new List<ParsedEffort>();
                foreach (Match match in labeledMatches)
                {
                    decimal value;
                    if (!TryParseDecimal(match.Groups["value"].Value, out value))
                        continue;
                    string combination = NullIfEmpty(match.Groups["comb"].Value);
                    labeled.Add(new ParsedEffort(
                        ParseComponent(match.Groups["name"].Value),
                        value,
                        match.Groups["value"].Value,
                        combination,
                        combination));
                }

                row = new ParsedReactionRow(kind, label, labeled, unit, source);
                error = null;
                return true;
            }

            MatchCollection pairMatches = PairRegex.Matches(body);
            if (pairMatches.Count > 0)
            {
                EffortComponent[] order = columns ?? CanonicalOrder;
                var paired = new List<ParsedEffort>();
                for (int index = 0; index < pairMatches.Count; index++)
                {
                    Match match = pairMatches[index];
                    decimal value;
                    if (!TryParseDecimal(match.Groups["value"].Value, out value))
                        continue;
                    EffortComponent component = index < order.Length
                        ? order[index]
                        : CanonicalOrder[index % CanonicalOrder.Length];
                    string combination = match.Groups["comb"].Value;
                    paired.Add(new ParsedEffort(
                        component,
                        value,
                        match.Groups["value"].Value,
                        combination,
                        combination));
                }

                string unmatchedBody = PairRegex.Replace(body, " ");
                MatchCollection unmatchedNumbers = NumberRegex.Matches(unmatchedBody);
                foreach (Match unmatched in unmatchedNumbers)
                {
                    decimal value;
                    if (!TryParseDecimal(unmatched.Value, out value))
                        continue;
                    int index = paired.Count;
                    EffortComponent component = index < order.Length
                        ? order[index]
                        : CanonicalOrder[index % CanonicalOrder.Length];
                    paired.Add(new ParsedEffort(
                        component,
                        value,
                        unmatched.Value,
                        null,
                        null));
                }

                row = new ParsedReactionRow(kind, label, paired, unit, source);
                error = null;
                return true;
            }

            MatchCollection numbers = NumberRegex.Matches(body);
            if (numbers.Count > 6)
            {
                row = null;
                error = new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.AmbiguousValueCombination,
                    "Não foi possível distinguir valores de esforços e números de combinações.",
                    source.Trace,
                    nodeId);
                return false;
            }

            EffortComponent[] positionalOrder = columns ?? CanonicalOrder;
            var positional = new List<ParsedEffort>();
            for (int index = 0; index < numbers.Count; index++)
            {
                decimal value;
                if (!TryParseDecimal(numbers[index].Value, out value))
                    continue;
                EffortComponent component = index < positionalOrder.Length
                    ? positionalOrder[index]
                    : CanonicalOrder[index % CanonicalOrder.Length];
                positional.Add(new ParsedEffort(
                    component,
                    value,
                    numbers[index].Value,
                    null,
                    null));
            }

            row = new ParsedReactionRow(kind, label, positional, unit, source);
            error = null;
            return true;
        }

        private static bool TryParseHeader(string text, out EffortComponent[] columns)
        {
            string[] tokens = Regex.Split(
                    text.Replace('\u00A0', ' ').Trim(),
                    @"[\s;\t|]+")
                .Where(token => token.Length > 0)
                .ToArray();
            if (tokens.Length != 6 || tokens.Any(token => !IsComponent(token)))
            {
                columns = null;
                return false;
            }

            columns = tokens.Select(ParseComponent).ToArray();
            return columns.Distinct().Count() == 6;
        }

        private static bool IsComponent(string value)
            => Regex.IsMatch(
                value,
                @"^(?:FX|FY|FZ|MX|MY|MZ|X[1-6])$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static EffortComponent ParseComponent(string value)
        {
            switch (value.ToUpperInvariant())
            {
                case "FX":
                case "X1":
                    return EffortComponent.Fx;
                case "FY":
                case "X2":
                    return EffortComponent.Fy;
                case "FZ":
                case "X3":
                    return EffortComponent.Fz;
                case "MX":
                case "X4":
                    return EffortComponent.Mx;
                case "MY":
                case "X5":
                    return EffortComponent.My;
                case "MZ":
                case "X6":
                    return EffortComponent.Mz;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value));
            }
        }

        private static bool IsMaximum(string label)
        {
            string normalized = RemoveDiacritics(label).ToUpperInvariant();
            return normalized.StartsWith("MAX", StringComparison.Ordinal);
        }

        private static bool TryParseDecimal(string raw, out decimal value)
        {
            string normalized = raw.Replace(',', '.');
            return decimal.TryParse(
                normalized,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out value);
        }

        private static string NormalizeWhitespace(string value)
            => Regex.Replace(value.Trim(), @"\s+", " ");

        private static string NullIfEmpty(string value)
            => string.IsNullOrWhiteSpace(value) ? null : value;

        private static string RemoveDiacritics(string value)
        {
            string decomposed = value.Normalize(NormalizationForm.FormD);
            var result = new StringBuilder();
            foreach (char character in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) !=
                    UnicodeCategory.NonSpacingMark)
                {
                    result.Append(character);
                }
            }
            return result.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
