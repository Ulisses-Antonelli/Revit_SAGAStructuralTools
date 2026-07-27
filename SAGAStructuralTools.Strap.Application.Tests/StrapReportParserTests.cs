using System.Linq;
using Xunit;

namespace SAGAStructuralTools.Strap.Application.Tests
{
    public sealed class StrapReportParserTests
    {
        [Theory]
        [InlineData("Máx", "Mín")]
        [InlineData("Máximo", "Mínimo")]
        [InlineData("Max", "Min")]
        public void RecognizesSupportedMaximumAndMinimumLabels(
            string maximum,
            string minimum)
        {
            StrapParseResult result = Parse(TestDocument.ValidLines(maximum, minimum));

            RecognizedNode node = Assert.Single(result.Nodes);
            Assert.Contains(node.Rows, row => row.CaseKind == ReportCaseKind.Maximum);
            Assert.Contains(node.Rows, row => row.CaseKind == ReportCaseKind.Minimum);
        }

        [Fact]
        public void RecognizesNodeAndPreservesCompleteTrace()
        {
            StrapParseResult result = Parse(TestDocument.ValidLines());

            RecognizedNode node = Assert.Single(result.Nodes);
            Assert.Equal("101", node.NodeId);
            Assert.Equal("strap.txt", node.Declaration.Trace.FileName);
            Assert.Equal(2, node.Declaration.Trace.PageNumber);
            Assert.Equal(3, node.Declaration.Trace.TableNumber);
            Assert.Equal(4, node.Declaration.Trace.LineNumber);
        }

        [Fact]
        public void MapsX1ToX6HeadersToCanonicalComponents()
        {
            StrapParseResult result = Parse(
                "Unidade: kN",
                "Nó: P1",
                "X1 X2 X3 X4 X5 X6",
                "Max 1 2 3 4 5 6",
                "Min -1 -2 -3 -4 -5 -6");

            ParsedReactionRow row = result.Nodes.Single().Rows.First();
            Assert.Equal(1m, Value(row, EffortComponent.Fx));
            Assert.Equal(2m, Value(row, EffortComponent.Fy));
            Assert.Equal(3m, Value(row, EffortComponent.Fz));
            Assert.Equal(4m, Value(row, EffortComponent.Mx));
            Assert.Equal(5m, Value(row, EffortComponent.My));
            Assert.Equal(6m, Value(row, EffortComponent.Mz));
        }

        [Fact]
        public void ParsesDotAndCommaDecimalsTabsAndIgnoresHeadersAndBlankLines()
        {
            StrapParseResult result = Parse(TestDocument.ValidLines());

            RecognizedNode node = Assert.Single(result.Nodes);
            ParsedReactionRow maximum = node.Rows.Single(
                row => row.CaseKind == ReportCaseKind.Maximum);
            ParsedReactionRow minimum = node.Rows.Single(
                row => row.CaseKind == ReportCaseKind.Minimum);
            Assert.Equal(0.136m, Value(maximum, EffortComponent.Fx));
            Assert.Equal(-3.432m, Value(minimum, EffortComponent.Fz));
            Assert.Equal(2, node.Rows.Count);
        }

        [Fact]
        public void ParsesValuesAndCombinationNumbersWithoutLosingRawTokens()
        {
            StrapParseResult result = Parse(
                "Unidades: kN / kN.m",
                "Nó 1",
                "FX FY FZ MX MY MZ",
                "Máx 1,2[C01] 2,3[C02] 3,4[C03] 4,5[C04] 5,6[C05] 6,7[C06]",
                "Mín -1.2(C11) -2.3(C12) -3.4(C13) -4.5(C14) -5.6(C15) -6.7(C16)");

            ParsedEffort effort = result.Nodes.Single().Rows.First().Efforts.First();
            Assert.Equal(1.2m, effort.Value);
            Assert.Equal("1,2", effort.RawValue);
            Assert.Equal("C01", effort.CombinationId);
            Assert.Equal("C01", effort.RawCombination);
        }

        [Fact]
        public void ParsesLabeledEffortsInAnyOrder()
        {
            StrapParseResult result = Parse(
                "Unidade: kN",
                "Nó 1",
                "Máx MZ=6[C6] FZ=3[C3] FX=1[C1] MY=5[C5] FY=2[C2] MX=4[C4]",
                "Mín MZ=-6[D6] FZ=-3[D3] FX=-1[D1] MY=-5[D5] FY=-2[D2] MX=-4[D4]");

            ParsedReactionRow maximum = result.Nodes.Single().Rows.First();
            Assert.Equal(1m, Value(maximum, EffortComponent.Fx));
            Assert.Equal(6m, Value(maximum, EffortComponent.Mz));
        }

        [Fact]
        public void TwelveBareNumbersAreReportedAsAmbiguous()
        {
            StrapParseResult result = Parse(
                "Unidade: kN",
                "Nó 1",
                "Máx 1 101 2 102 3 103 4 104 5 105 6 106",
                "Mín -1 -2 -3 -4 -5 -6");

            ImportDiagnostic error = Assert.Single(result.Diagnostics);
            Assert.Equal(DiagnosticCodes.AmbiguousValueCombination, error.Code);
            Assert.Equal(3, error.Trace.LineNumber);
        }

        private static StrapParseResult Parse(params string[] lines)
            => new StrapReportParser().Parse(TestDocument.Create(lines));

        private static decimal Value(ParsedReactionRow row, EffortComponent component)
            => row.Efforts.Single(effort => effort.Component == component).Value;
    }
}
