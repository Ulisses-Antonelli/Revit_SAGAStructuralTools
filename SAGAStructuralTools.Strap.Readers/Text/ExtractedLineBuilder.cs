using System;
using System.Collections.Generic;
using SAGAStructuralTools.Strap.Application;

namespace SAGAStructuralTools.Strap.Readers.Text
{
    internal static class ExtractedLineBuilder
    {
        public static IReadOnlyCollection<ExtractedLine> FromText(
            string text,
            string filePath,
            int? pageNumber = null,
            int? tableNumber = null,
            int lineOffset = 0)
        {
            string normalized = (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
            string[] values = normalized.Split(new[] { '\n' }, StringSplitOptions.None);
            var lines = new List<ExtractedLine>(values.Length);
            for (int index = 0; index < values.Length; index++)
            {
                lines.Add(new ExtractedLine(
                    values[index],
                    new SourceTrace(
                        filePath,
                        pageNumber,
                        tableNumber,
                        lineOffset + index + 1)));
            }
            return lines;
        }
    }
}
