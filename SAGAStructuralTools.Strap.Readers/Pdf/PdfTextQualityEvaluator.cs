using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Strap.Readers.Pdf
{
    internal static class PdfTextQualityEvaluator
    {
        public static ReaderDiagnostic Evaluate(
            IReadOnlyCollection<string> words,
            IReadOnlyCollection<string> lines)
        {
            if (words.Count == 0)
            {
                return new ReaderDiagnostic(
                    ReaderDiagnosticSeverity.Error,
                    ReaderDiagnosticCodes.PdfNoText,
                    "O PDF não contém texto extraível. OCR necessário.",
                    requiresOcr: true);
            }

            int usefulCharacters = words
                .SelectMany(word => word)
                .Count(character => char.IsLetterOrDigit(character));
            if (words.Count < 4 || usefulCharacters < 20)
            {
                return new ReaderDiagnostic(
                    ReaderDiagnosticSeverity.Error,
                    ReaderDiagnosticCodes.PdfInsufficientText,
                    "O PDF contém texto insuficiente para uma extração confiável. " +
                    "OCR necessário.",
                    requiresOcr: true);
            }

            int totalCharacters = words.Sum(word => word.Length);
            int invalidCharacters = words
                .SelectMany(word => word)
                .Count(character =>
                    char.IsControl(character) ||
                    character == '\uFFFD');
            if (lines.Count < 3 ||
                (totalCharacters > 0 && invalidCharacters * 10 > totalCharacters))
            {
                return new ReaderDiagnostic(
                    ReaderDiagnosticSeverity.Error,
                    ReaderDiagnosticCodes.PdfUnrecognizableStructure,
                    "O PDF contém texto, mas não foi possível reconstruir uma estrutura " +
                    "de linhas reconhecível. Revise a origem ou exporte novamente o relatório.");
            }

            return null;
        }
    }
}
