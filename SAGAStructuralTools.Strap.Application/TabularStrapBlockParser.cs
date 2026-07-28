using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SAGAStructuralTools.Strap.Application
{
    internal sealed class TabularParseOutput
    {
        public List<RecognizedNode> Nodes { get; } = new List<RecognizedNode>();
        public List<ImportDiagnostic> Diagnostics { get; } =
            new List<ImportDiagnostic>();
        public HashSet<ExtractedLine> HandledLines { get; } =
            new HashSet<ExtractedLine>();
    }

    internal sealed class TabularStrapBlockParser
    {
        private static readonly EffortComponent[] Components =
        {
            EffortComponent.Fx,
            EffortComponent.Fy,
            EffortComponent.Fz,
            EffortComponent.Mx,
            EffortComponent.My,
            EffortComponent.Mz
        };

        private enum BlockState
        {
            None,
            AwaitMaximumCombination,
            AwaitMinimum,
            AwaitMinimumCombination
        }

        private readonly Func<ExtractedLine, string> _unitAtLine;
        private readonly TabularParseOutput _output = new TabularParseOutput();
        private bool _headerActive;
        private int? _tableNumber;
        private BlockState _state;
        private string _nodeId;
        private ExtractedLine _declaration;
        private ParsedReactionRow _maximum;
        private ParsedReactionRow _minimum;

        public TabularStrapBlockParser(Func<ExtractedLine, string> unitAtLine)
        {
            _unitAtLine = unitAtLine;
        }

        public TabularParseOutput Parse(IEnumerable<ExtractedLine> source)
        {
            foreach (ExtractedLine line in source)
            {
                HandleTableBoundary(line);
                string normalized = Normalize(line.RawText);
                if (normalized.Length == 0)
                    continue;

                if (IsTabularHeader(normalized))
                {
                    _output.HandledLines.Add(line);
                    if (_state != BlockState.None)
                    {
                        AddError(
                            DiagnosticCodes.ChangedHeaderInsideBlock,
                            "O cabeçalho foi alterado ou repetido antes da conclusão do bloco.",
                            line);
                        FinalizePartial();
                    }
                    _headerActive = true;
                    continue;
                }

                if (ContainsComponentHeader(normalized))
                {
                    if (_state != BlockState.None)
                    {
                        _output.HandledLines.Add(line);
                        AddError(
                            DiagnosticCodes.ChangedHeaderInsideBlock,
                            "O cabeçalho X1–X6 foi alterado no meio do bloco.",
                            line);
                        FinalizePartial();
                        _headerActive = false;
                    }
                    continue;
                }

                if (!_headerActive)
                {
                    if (ContainsDegradedLabel(normalized))
                    {
                        _output.HandledLines.Add(line);
                        AddError(
                            DiagnosticCodes.DegradedLabelOutsideContext,
                            "Um rótulo degradado foi encontrado fora de um cabeçalho " +
                            "tabular X1–X6 válido.",
                            line);
                    }
                    continue;
                }

                ProcessLine(line, normalized);
            }

            FinishOpenBlockAtEnd();
            return _output;
        }

        private void ProcessLine(ExtractedLine line, string normalized)
        {
            string[] tokens = Tokenize(normalized);
            bool startsNode = tokens.Length > 0 &&
                              int.TryParse(tokens[0], out _);
            bool isCombination = tokens.Length > 0 &&
                                 string.Equals(
                                     tokens[0],
                                     "Comb",
                                     StringComparison.OrdinalIgnoreCase);
            bool isMinimum = tokens.Length > 0 &&
                             IsMinimumLabel(tokens[0], allowDegraded: true);

            if (_state != BlockState.None &&
                startsNode &&
                tokens.Length > 1 &&
                IsMaximumLabel(tokens[1], allowDegraded: true))
            {
                AddError(
                    DiagnosticCodes.NewNodeBeforeBlockCompleted,
                    "Um novo nó foi iniciado antes da conclusão do bloco anterior.",
                    line);
                AddMissingForCurrentState(line);
                FinalizePartial();
            }

            switch (_state)
            {
                case BlockState.None:
                    ProcessWithoutOpenBlock(line, tokens, startsNode, isMinimum);
                    break;

                case BlockState.AwaitMaximumCombination:
                    if (isCombination)
                    {
                        ProcessCombination(line, tokens, ReportCaseKind.Maximum);
                    }
                    else if (isMinimum)
                    {
                        AddError(
                            DiagnosticCodes.MissingCombinationAfterMaximum,
                            "A linha Comb está ausente após a linha Máx.",
                            line);
                        ProcessMinimum(line, tokens);
                    }
                    else if (startsNode)
                    {
                        ProcessWithoutOpenBlock(line, tokens, true, false);
                    }
                    break;

                case BlockState.AwaitMinimum:
                    if (isMinimum)
                    {
                        ProcessMinimum(line, tokens);
                    }
                    else if (startsNode)
                    {
                        ProcessWithoutOpenBlock(line, tokens, true, false);
                    }
                    else if (ContainsDegradedLabel(normalized))
                    {
                        AddError(
                            DiagnosticCodes.DegradedLabelOutsideContext,
                            "O rótulo degradado não ocupa a posição esperada da linha Mín.",
                            line);
                    }
                    break;

                case BlockState.AwaitMinimumCombination:
                    if (isCombination)
                    {
                        ProcessCombination(line, tokens, ReportCaseKind.Minimum);
                    }
                    else if (startsNode)
                    {
                        ProcessWithoutOpenBlock(line, tokens, true, false);
                    }
                    break;
            }
        }

        private void ProcessWithoutOpenBlock(
            ExtractedLine line,
            string[] tokens,
            bool startsNode,
            bool isMinimum)
        {
            if (isMinimum)
            {
                _output.HandledLines.Add(line);
                AddError(
                    DiagnosticCodes.MinimumWithoutCurrentNode,
                    "Foi encontrada uma linha Mín sem nó corrente.",
                    line);
                return;
            }

            if (!startsNode || tokens.Length < 2)
            {
                if (ContainsDegradedLabel(Normalize(line.RawText)))
                {
                    _output.HandledLines.Add(line);
                    AddError(
                        DiagnosticCodes.DegradedLabelOutsideContext,
                        "O rótulo degradado não ocupa a posição estrutural esperada.",
                        line);
                }
                return;
            }

            if (!IsMaximumLabel(tokens[1], allowDegraded: true))
            {
                if (ContainsDegradedLabel(tokens[1]))
                {
                    _output.HandledLines.Add(line);
                    AddError(
                        DiagnosticCodes.DegradedLabelOutsideContext,
                        "O rótulo degradado não corresponde a Máx na posição esperada.",
                        line);
                }
                return;
            }

            _output.HandledLines.Add(line);
            _nodeId = tokens[0];
            _declaration = line;
            _maximum = CreateRow(
                line,
                tokens.Skip(2).ToArray(),
                tokens[1],
                ReportCaseKind.Maximum);
            _minimum = null;
            _state = BlockState.AwaitMaximumCombination;
        }

        private void ProcessMinimum(ExtractedLine line, string[] tokens)
        {
            _output.HandledLines.Add(line);
            if (_nodeId == null)
            {
                AddError(
                    DiagnosticCodes.MinimumWithoutCurrentNode,
                    "Foi encontrada uma linha Mín sem nó corrente.",
                    line);
                return;
            }

            _minimum = CreateRow(
                line,
                tokens.Skip(1).ToArray(),
                tokens[0],
                ReportCaseKind.Minimum);
            _state = BlockState.AwaitMinimumCombination;
        }

        private void ProcessCombination(
            ExtractedLine line,
            string[] tokens,
            ReportCaseKind target)
        {
            _output.HandledLines.Add(line);
            string[] combinations = tokens.Skip(1).ToArray();
            if (combinations.Length != 6 ||
                combinations.Any(value => !int.TryParse(value, out _)))
            {
                AddError(
                    DiagnosticCodes.UnexpectedCombinationCount,
                    "A linha Comb deve conter exatamente seis números de combinação.",
                    line);
            }

            if (target == ReportCaseKind.Maximum)
            {
                _maximum = AttachCombinations(_maximum, combinations);
                _state = BlockState.AwaitMinimum;
            }
            else
            {
                _minimum = AttachCombinations(_minimum, combinations);
                FinalizeComplete();
            }
        }

        private ParsedReactionRow CreateRow(
            ExtractedLine line,
            string[] rawValues,
            string label,
            ReportCaseKind kind)
        {
            if (rawValues.Length != 6)
            {
                AddError(
                    DiagnosticCodes.UnexpectedEffortCount,
                    "A linha tabular deve conter exatamente seis valores numéricos.",
                    line);
            }

            var efforts = new List<ParsedEffort>();
            for (int index = 0;
                 index < rawValues.Length && index < Components.Length;
                 index++)
            {
                decimal value;
                if (!TryParseDecimal(rawValues[index], out value))
                {
                    AddError(
                        DiagnosticCodes.UnexpectedEffortCount,
                        "A linha tabular contém um valor não numérico.",
                        line);
                    continue;
                }
                efforts.Add(new ParsedEffort(
                    Components[index],
                    value,
                    rawValues[index],
                    null,
                    null));
            }

            return new ParsedReactionRow(
                kind,
                label,
                efforts,
                _unitAtLine(line),
                line);
        }

        private ParsedReactionRow AttachCombinations(
            ParsedReactionRow row,
            string[] combinations)
        {
            if (row == null)
                return null;

            ParsedEffort[] efforts = row.Efforts
                .Select((effort, index) => new ParsedEffort(
                    effort.Component,
                    effort.Value,
                    effort.RawValue,
                    index < combinations.Length ? combinations[index] : null,
                    index < combinations.Length ? combinations[index] : null))
                .ToArray();
            return new ParsedReactionRow(
                row.CaseKind,
                row.RawCaseLabel,
                efforts,
                row.Unit,
                row.Source);
        }

        private void HandleTableBoundary(ExtractedLine line)
        {
            int? incoming = line.Trace.TableNumber;
            if (_tableNumber == incoming)
                return;

            if (_tableNumber.HasValue || incoming.HasValue)
            {
                FinishOpenBlockAtEnd();
                _headerActive = false;
            }
            _tableNumber = incoming;
        }

        private void FinishOpenBlockAtEnd()
        {
            if (_state == BlockState.None)
                return;
            AddMissingForCurrentState(_declaration);
            FinalizePartial();
        }

        private void AddMissingForCurrentState(ExtractedLine traceLine)
        {
            switch (_state)
            {
                case BlockState.AwaitMaximumCombination:
                    AddError(
                        DiagnosticCodes.MissingCombinationAfterMaximum,
                        "A linha Comb está ausente após a linha Máx.",
                        traceLine);
                    AddError(
                        DiagnosticCodes.MissingMinimum,
                        "O bloco foi encerrado sem linha Mín.",
                        traceLine);
                    break;
                case BlockState.AwaitMinimum:
                    AddError(
                        DiagnosticCodes.MissingMinimum,
                        "O bloco foi encerrado sem linha Mín.",
                        traceLine);
                    break;
                case BlockState.AwaitMinimumCombination:
                    AddError(
                        DiagnosticCodes.MissingCombinationAfterMinimum,
                        "A linha Comb está ausente após a linha Mín.",
                        traceLine);
                    break;
            }
        }

        private void FinalizeComplete()
        {
            _output.Nodes.Add(new RecognizedNode(
                _nodeId,
                _declaration,
                new[] { _maximum, _minimum }));
            ResetBlock();
        }

        private void FinalizePartial()
        {
            if (_nodeId != null)
            {
                var rows = new[] { _maximum, _minimum }
                    .Where(row => row != null)
                    .ToArray();
                _output.Nodes.Add(new RecognizedNode(
                    _nodeId,
                    _declaration,
                    rows));
            }
            ResetBlock();
        }

        private void ResetBlock()
        {
            _state = BlockState.None;
            _nodeId = null;
            _declaration = null;
            _maximum = null;
            _minimum = null;
        }

        private void AddError(string code, string message, ExtractedLine line)
        {
            _output.Diagnostics.Add(new ImportDiagnostic(
                DiagnosticSeverity.Error,
                code,
                message,
                line?.Trace,
                _nodeId));
        }

        private static bool IsTabularHeader(string normalized)
        {
            string[] tokens = Tokenize(normalized);
            if (tokens.Length < 6 || tokens.Length > 8)
                return false;
            string[] suffix = tokens.Skip(tokens.Length - 6).ToArray();
            return suffix.SequenceEqual(
                new[] { "X1", "X2", "X3", "X4", "X5", "X6" },
                StringComparer.OrdinalIgnoreCase);
        }

        private static bool ContainsComponentHeader(string normalized)
        {
            string[] tokens = Tokenize(normalized);
            return tokens.Any(token => Regex.IsMatch(
                token,
                @"^X[1-6]$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        }

        private static string[] Tokenize(string normalized)
            => Regex.Split(normalized, @"[\s\u00A0]+")
                .Where(token => token.Length > 0)
                .ToArray();

        private static string Normalize(string raw)
            => Regex.Replace(
                (raw ?? string.Empty).Replace('\u00A0', ' ').Trim(),
                @"[ \t]+",
                " ");

        private static bool IsMaximumLabel(string value, bool allowDegraded)
        {
            string normalized = RemoveDiacritics(value).ToUpperInvariant();
            return normalized == "MAX" ||
                   normalized == "MAXIMO" ||
                   normalized == "MAXIMUM" ||
                   (allowDegraded && value == "M\uFFFDx");
        }

        private static bool IsMinimumLabel(string value, bool allowDegraded)
        {
            string normalized = RemoveDiacritics(value).ToUpperInvariant();
            return normalized == "MIN" ||
                   normalized == "MINIMO" ||
                   normalized == "MINIMUM" ||
                   (allowDegraded && value == "M\uFFFDn");
        }

        private static bool ContainsDegradedLabel(string value)
            => value.IndexOf('\uFFFD') >= 0;

        private static bool TryParseDecimal(string raw, out decimal value)
            => decimal.TryParse(
                raw.Replace(',', '.'),
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out value);

        private static string RemoveDiacritics(string value)
        {
            string decomposed = (value ?? string.Empty)
                .Normalize(NormalizationForm.FormD);
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
