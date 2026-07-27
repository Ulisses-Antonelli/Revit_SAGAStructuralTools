using System;
using System.Collections.Generic;
using System.Linq;
using SAGAStructuralTools.Strap.Application;
using SAGAStructuralTools.Strap.Readers.Pdf;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace SAGAStructuralTools.Strap.Readers
{
    public sealed class TextPdfStrapDocumentReader : IStrapDocumentReader
    {
        public DocumentReadResult Read(string filePath)
        {
            DocumentReadResult invalid = ReaderGuard.ValidateFile(filePath);
            if (invalid != null)
                return invalid;

            try
            {
                var extractedLines = new List<ExtractedLine>();
                var allWords = new List<string>();
                int globalLine = 0;

                using (PdfDocument document = PdfDocument.Open(filePath))
                {
                    foreach (Page page in document.GetPages())
                    {
                        Word[] words = page
                            .GetWords(NearestNeighbourWordExtractor.Instance)
                            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
                            .ToArray();
                        allWords.AddRange(words.Select(word => word.Text));

                        IReadOnlyCollection<string> pageLines =
                            DeterministicPdfLineBuilder.Build(words);
                        int pageLine = 0;
                        foreach (string line in pageLines)
                        {
                            pageLine++;
                            globalLine++;
                            extractedLines.Add(new ExtractedLine(
                                line,
                                new SourceTrace(
                                    filePath,
                                    page.Number,
                                    null,
                                    pageLine)));
                        }
                    }
                }

                string[] reconstructed = extractedLines
                    .Select(line => line.RawText)
                    .ToArray();
                ReaderDiagnostic quality = PdfTextQualityEvaluator.Evaluate(
                    allWords,
                    reconstructed);
                var documentResult = new ExtractedDocument(filePath, extractedLines);
                return quality == null
                    ? new DocumentReadResult(documentResult)
                    : new DocumentReadResult(documentResult, new[] { quality });
            }
            catch (Exception)
            {
                return ReaderGuard.Corrupt(filePath, "PDF");
            }
        }
    }
}
