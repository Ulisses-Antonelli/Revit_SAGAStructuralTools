using System.IO;

namespace SAGAStructuralTools.Strap.Readers
{
    internal static class ReaderGuard
    {
        public static DocumentReadResult ValidateFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return DocumentReadResult.Failure(new ReaderDiagnostic(
                    ReaderDiagnosticSeverity.Error,
                    ReaderDiagnosticCodes.FileNotFound,
                    $"O arquivo '{filePath}' não foi encontrado."));
            }

            if (new FileInfo(filePath).Length == 0)
            {
                return DocumentReadResult.Failure(new ReaderDiagnostic(
                    ReaderDiagnosticSeverity.Error,
                    ReaderDiagnosticCodes.EmptyFile,
                    $"O arquivo '{filePath}' está vazio."));
            }

            return null;
        }

        public static DocumentReadResult Corrupt(string filePath, string format)
            => DocumentReadResult.Failure(new ReaderDiagnostic(
                ReaderDiagnosticSeverity.Error,
                ReaderDiagnosticCodes.CorruptDocument,
                $"O arquivo {format} '{filePath}' está corrompido ou não pôde ser lido."));
    }
}
