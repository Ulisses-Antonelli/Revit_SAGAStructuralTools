using System.Linq;
using Xunit;

namespace SAGAStructuralTools.Strap.Application.Tests
{
    public sealed class StrapImportValidatorTests
    {
        [Fact]
        public void MissingMaximumIsBlocking()
        {
            StrapImportResult result = Import(
                "Unidade: kN",
                "Nó 1",
                "FX FY FZ MX MY MZ",
                "Mín 1 2 3 4 5 6");

            AssertError(result, DiagnosticCodes.MissingMaximum);
        }

        [Fact]
        public void MissingMinimumIsBlocking()
        {
            StrapImportResult result = Import(
                "Unidade: kN",
                "Nó 1",
                "FX FY FZ MX MY MZ",
                "Máx 1 2 3 4 5 6");

            AssertError(result, DiagnosticCodes.MissingMinimum);
        }

        [Fact]
        public void EffortCountDifferentFromSixIsBlocking()
        {
            StrapImportResult result = Import(
                "Unidade: kN",
                "Nó 1",
                "FX FY FZ MX MY MZ",
                "Máx 1 2 3 4 5",
                "Mín -1 -2 -3 -4 -5 -6");

            AssertError(result, DiagnosticCodes.UnexpectedEffortCount);
        }

        [Fact]
        public void IdenticalDuplicateNodeProducesWarningWithoutBlocking()
        {
            StrapImportResult result = Import(
                "Unidade: kN",
                "Nó 1",
                "Máx 1 2 3 4 5 6",
                "Mín -1 -2 -3 -4 -5 -6",
                "Nó 1",
                "Máx 1 2 3 4 5 6",
                "Mín -1 -2 -3 -4 -5 -6");

            Assert.Contains(
                result.Warnings,
                item => item.Code == DiagnosticCodes.DuplicateNodeIdentical);
            Assert.False(result.IsBlocked);
        }

        [Fact]
        public void ConflictingDuplicateNodeIsBlocking()
        {
            StrapImportResult result = Import(
                "Unidade: kN",
                "Nó 1",
                "Máx 1 2 3 4 5 6",
                "Mín -1 -2 -3 -4 -5 -6",
                "Nó 1",
                "Máx 9 2 3 4 5 6",
                "Mín -1 -2 -3 -4 -5 -6");

            AssertError(result, DiagnosticCodes.DuplicateNodeConflict);
        }

        [Fact]
        public void MissingUnitIsBlocking()
        {
            StrapImportResult result = Import(
                "Nó 1",
                "Máx 1 2 3 4 5 6",
                "Mín -1 -2 -3 -4 -5 -6");

            AssertError(result, DiagnosticCodes.MissingUnit);
        }

        [Fact]
        public void UnitChangeInsideDocumentIsBlocking()
        {
            StrapImportResult result = Import(
                "Unidade: kN",
                "Nó 1",
                "Máx 1 2 3 4 5 6",
                "Mín -1 -2 -3 -4 -5 -6",
                "Unidade: tf",
                "Nó 2",
                "Máx 1 2 3 4 5 6",
                "Mín -1 -2 -3 -4 -5 -6");

            AssertError(result, DiagnosticCodes.InconsistentUnit);
        }

        [Fact]
        public void PartiallyMissingCombinationsAreBlocking()
        {
            StrapImportResult result = Import(
                "Unidade: kN",
                "Nó 1",
                "Máx 1[C1] 2[C2] 3[C3] 4[C4] 5[C5] 6",
                "Mín -1[D1] -2[D2] -3[D3] -4[D4] -5[D5] -6[D6]");

            AssertError(result, DiagnosticCodes.MissingCombination);
        }

        [Fact]
        public void CompletelyAbsentCombinationsAreAllowedForIndividualEnvelope()
        {
            StrapImportResult result = Import(TestDocument.ValidLines());

            Assert.DoesNotContain(
                result.Errors,
                item => item.Code == DiagnosticCodes.MissingCombination);
            Assert.False(result.IsBlocked);
        }

        private static StrapImportResult Import(params string[] lines)
            => new StrapImportService().Import(TestDocument.Create(lines));

        private static void AssertError(StrapImportResult result, string code)
        {
            Assert.True(result.IsBlocked);
            Assert.Contains(result.Errors, item => item.Code == code);
        }
    }
}
