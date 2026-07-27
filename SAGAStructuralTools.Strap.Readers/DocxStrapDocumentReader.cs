using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SAGAStructuralTools.Strap.Application;

namespace SAGAStructuralTools.Strap.Readers
{
    public sealed class DocxStrapDocumentReader : IStrapDocumentReader
    {
        public DocumentReadResult Read(string filePath)
        {
            DocumentReadResult invalid = ReaderGuard.ValidateFile(filePath);
            if (invalid != null)
                return invalid;

            try
            {
                var lines = new List<ExtractedLine>();
                var tableRows = new List<ExtractedTableRow>();
                int lineNumber = 0;
                int tableNumber = 0;

                using (WordprocessingDocument document =
                    WordprocessingDocument.Open(filePath, false))
                {
                    Body body = document.MainDocumentPart?.Document?.Body;
                    if (body == null)
                        return ReaderGuard.Corrupt(filePath, "DOCX");

                    foreach (OpenXmlElement element in body.Elements())
                    {
                        var paragraph = element as Paragraph;
                        if (paragraph != null)
                        {
                            string text = ReadParagraph(paragraph);
                            AddLine(lines, filePath, text, null, ref lineNumber);
                            continue;
                        }

                        var table = element as Table;
                        if (table == null)
                            continue;

                        tableNumber++;
                        foreach (TableRow row in table.Elements<TableRow>())
                        {
                            string[] cells = row.Elements<TableCell>()
                                .Select(ReadCell)
                                .ToArray();
                            lineNumber++;
                            var trace = new SourceTrace(
                                filePath,
                                null,
                                tableNumber,
                                lineNumber);
                            tableRows.Add(new ExtractedTableRow(cells, trace));
                            lines.Add(new ExtractedLine(string.Join("\t", cells), trace));
                        }
                    }
                }

                return new DocumentReadResult(
                    new ExtractedDocument(filePath, lines),
                    tableRows: tableRows);
            }
            catch (Exception)
            {
                return ReaderGuard.Corrupt(filePath, "DOCX");
            }
        }

        private static void AddLine(
            ICollection<ExtractedLine> lines,
            string filePath,
            string text,
            int? tableNumber,
            ref int lineNumber)
        {
            lineNumber++;
            lines.Add(new ExtractedLine(
                text,
                new SourceTrace(filePath, null, tableNumber, lineNumber)));
        }

        private static string ReadCell(TableCell cell)
        {
            string[] paragraphs = cell.Elements<Paragraph>()
                .Select(ReadParagraph)
                .ToArray();
            return string.Join("\n", paragraphs);
        }

        private static string ReadParagraph(Paragraph paragraph)
        {
            var result = new StringBuilder();
            foreach (OpenXmlElement descendant in paragraph.Descendants())
            {
                var text = descendant as DocumentFormat.OpenXml.Wordprocessing.Text;
                if (text != null)
                {
                    result.Append(text.Text);
                }
                else if (descendant is TabChar)
                {
                    result.Append('\t');
                }
                else if (descendant is Break || descendant is CarriageReturn)
                {
                    result.Append('\n');
                }
            }
            return result.ToString();
        }
    }
}
