using System.Collections.Generic;
using System.Linq;
using SAGAStructuralTools.Strap.Domain;
using Xunit;

namespace SAGAStructuralTools.Strap.Application.Tests
{
    public sealed class TabularStrapReportParserTests
    {
        [Fact]
        public void ParsesCompleteTabularLayoutAndEmbeddedUnits()
        {
            StrapParseResult result = Parse(CompleteBlock("1", "Máx", "Mín"));

            RecognizedNode node = Assert.Single(result.Nodes);
            Assert.Equal("1", node.NodeId);
            Assert.Equal("tf", Assert.Single(result.ForceUnits));
            Assert.Equal("tf*metro", Assert.Single(result.MomentUnits));
            Assert.Equal(new[] { "90", "86", "84", "101", "90", "97" },
                node.Rows.Single(r => r.CaseKind == ReportCaseKind.Maximum)
                    .Efforts.Select(e => e.CombinationId));
            Assert.Equal(new[] { "97", "101", "103", "86", "97", "82" },
                node.Rows.Single(r => r.CaseKind == ReportCaseKind.Minimum)
                    .Efforts.Select(e => e.CombinationId));
        }

        [Fact]
        public void NormalizesTabsAndNonBreakingSpacesButPreservesRawText()
        {
            string maximum = "7\u00A0Máx\t1 2 3 4 5 6";
            StrapParseResult result = Parse(
                "Unids: tf, tf*m",
                "nº cmb X1 X2 X3 X4 X5 X6",
                maximum,
                "Comb 1 2 3 4 5 6",
                "Mín\u00A0-1 -2 -3 -4 -5 -6",
                "Comb 7 8 9 10 11 12");

            RecognizedNode node = Assert.Single(result.Nodes);
            Assert.Equal(maximum, node.Declaration.RawText);
            Assert.Equal("7", node.NodeId);
        }

        [Fact]
        public void AcceptsDegradedLabelsOnlyInsideValidBlockStructure()
        {
            StrapParseResult accepted = Parse(CompleteBlock("1", "M�x", "M�n"));
            Assert.Single(accepted.Nodes);
            Assert.Empty(accepted.Diagnostics);

            StrapParseResult rejected = Parse(
                "texto � fora do bloco",
                "1 M�x 1 2 3 4 5 6");
            Assert.Contains(rejected.Diagnostics,
                d => d.Code == DiagnosticCodes.DegradedLabelOutsideContext);
            Assert.Empty(rejected.Nodes);
        }

        [Theory]
        [InlineData("nº cmb X1 X2 X3 X4 X5 X6|1 Máx 1 2 3 4 5 6",
            DiagnosticCodes.MissingCombinationAfterMaximum)]
        [InlineData("nº cmb X1 X2 X3 X4 X5 X6|1 Máx 1 2 3 4 5 6|Comb 1 2 3 4 5 6",
            DiagnosticCodes.MissingMinimum)]
        [InlineData("nº cmb X1 X2 X3 X4 X5 X6|1 Máx 1 2 3 4 5 6|Comb 1 2 3 4 5 6|Mín -1 -2 -3 -4 -5 -6",
            DiagnosticCodes.MissingCombinationAfterMinimum)]
        public void ReportsTruncatedBlocks(string joined, string expectedCode)
        {
            StrapParseResult result = Parse(joined.Split('|'));
            Assert.Contains(result.Diagnostics, d => d.Code == expectedCode);
        }

        [Fact]
        public void ReportsMalformedRowsAndIllegalStateTransitions()
        {
            StrapParseResult result = Parse(
                "nº cmb X1 X2 X3 X4 X5 X6",
                "Mín 1 2 3 4 5 6",
                "1 Máx 1 2 3 4 5",
                "Comb 1 2 3 4 5",
                "2 Máx 1 2 3 4 5 6",
                "nº cmb X1 X2 X3 X4 X6 X5");

            Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.MinimumWithoutCurrentNode);
            Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.UnexpectedEffortCount);
            Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.UnexpectedCombinationCount);
            Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.NewNodeBeforeBlockCompleted);
            Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.ChangedHeaderInsideBlock);
        }

        [Fact]
        public void ParsesTwoConsecutiveBlocksAndTwentyFourSyntheticBlocks()
        {
            var lines = new List<string> { "Relatório (Unids: tf, tf*metro)", "nº cmb X1 X2 X3 X4 X5 X6" };
            for (int node = 1; node <= 24; node++)
                lines.AddRange(BlockRows(node.ToString(), "Máx", "Mín"));

            StrapImportResult result = new StrapImportService().Import(
                TestDocument.Create(lines.ToArray()),
                rotate90: false);

            Assert.False(result.IsBlocked);
            Assert.Equal(24, result.Nodes.Count);
            Assert.Equal(24, result.ConsolidatedReactions.Count);
            Assert.All(result.ConsolidatedReactions, reaction =>
            {
                Assert.True(reaction.X3Max >= reaction.X3Min);
                Assert.True(reaction.X1 >= 0m);
                Assert.True(reaction.X2 >= 0m);
                Assert.True(reaction.X4 >= 0m);
                Assert.True(reaction.X5 >= 0m);
                Assert.True(reaction.X6 >= 0m);
            });
        }

        [Fact]
        public void NewAndLegacyLayoutsAreSemanticallyEquivalent()
        {
            StrapImportResult legacy = new StrapImportService().Import(
                TestDocument.Create(TestDocument.ValidLines()));
            StrapImportResult tabular = new StrapImportService().Import(
                TestDocument.Create(
                    "Unids: kN / kN.m, kN / kN.m",
                    "nº cmb X1 X2 X3 X4 X5 X6",
                    "101 Máx 0,136 0,375 3,143 0,083 -0,104 -0,004",
                    "Comb 1 2 3 4 5 6",
                    "Mín -2.056 0.239 -3.432 0.073 -0.781 0.381",
                    "Comb 7 8 9 10 11 12"));

            ConsolidatedReaction expected = legacy.ConsolidatedReactions.Single();
            ConsolidatedReaction actual = tabular.ConsolidatedReactions.Single();
            Assert.Equal(expected.NodeId, actual.NodeId);
            Assert.Equal(
                new[] { expected.X1, expected.X2, expected.X3Max, expected.X3Min,
                    expected.X4, expected.X5, expected.X6 },
                new[] { actual.X1, actual.X2, actual.X3Max, actual.X3Min,
                    actual.X4, actual.X5, actual.X6 });
        }

        private static StrapParseResult Parse(params string[] lines)
            => new StrapReportParser().Parse(TestDocument.Create(lines));

        private static string[] CompleteBlock(string node, string maximum, string minimum)
            => new[]
            {
                "REAÇÕES (Unids: tf, tf*metro)",
                "nº cmb X1 X2 X3 X4 X5 X6"
            }.Concat(BlockRows(node, maximum, minimum)).ToArray();

        private static string[] BlockRows(string node, string maximum, string minimum)
            => new[]
            {
                $"{node} {maximum} 0.015 1.903 7.182 -0.009 0.007 0.000",
                "Comb 90 86 84 101 90 97",
                $"{minimum} -0.015 0.540 2.527 -0.063 -0.007 0.000",
                "Comb 97 101 103 86 97 82"
            };
    }
}
