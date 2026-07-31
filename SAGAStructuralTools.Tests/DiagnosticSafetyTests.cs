using SAGAStructuralTools.Commands.Strap;
using System;
using System.IO;
using Xunit;

namespace SAGAStructuralTools.Tests
{
    public sealed class DiagnosticSafetyTests
    {
        [Fact]
        public void CancelledSelectionIsControlled()
        {
            Assert.Equal(StrapFileSelectionKind.Cancelled,
                StrapFileSelection.Classify(null));
        }

        [Theory]
        [InlineData("report.doc")]
        [InlineData("REPORT.DOC")]
        public void LegacyDocIsRejectedBeforeReader(string path)
        {
            Assert.Equal(StrapFileSelectionKind.LegacyDoc,
                StrapFileSelection.Classify(path));
        }

        [Fact]
        public void CoreExceptionIsConvertedAndReportedWithoutEscaping()
        {
            Exception recorded = null;
            string result = SafeExecutionBoundary.Run<string>(
                () => throw new TypeInitializationException("Reader", new FileLoadException("OpenXml")),
                exception =>
                {
                    recorded = exception;
                    SagaLog.Exception("teste-wrapper", exception);
                    return "failed";
                });

            Assert.Equal("failed", result);
            Assert.IsType<TypeInitializationException>(recorded);
            Assert.IsType<FileLoadException>(recorded.InnerException);
        }

        [Fact]
        public void LoggerPathUsesPersistentLocalApplicationData()
        {
            string expectedRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SAGA", "SAGAStructuralTools", "Logs");
            Assert.StartsWith(expectedRoot, SagaLog.ResolvePrimaryPath(),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
