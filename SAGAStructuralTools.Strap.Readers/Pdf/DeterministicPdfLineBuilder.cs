using System;
using System.Collections.Generic;
using System.Linq;
using UglyToad.PdfPig.Content;

namespace SAGAStructuralTools.Strap.Readers.Pdf
{
    internal static class DeterministicPdfLineBuilder
    {
        private const double MinimumLineTolerance = 1.5;

        public static IReadOnlyCollection<string> Build(IEnumerable<Word> source)
        {
            Word[] words = source
                .Where(word => word != null && !string.IsNullOrWhiteSpace(word.Text))
                .OrderByDescending(word => word.BoundingBox.Top)
                .ThenBy(word => word.BoundingBox.Left)
                .ThenBy(word => word.Text, StringComparer.Ordinal)
                .ToArray();
            if (words.Length == 0)
                return Array.Empty<string>();

            double medianHeight = words
                .Select(word => Math.Abs(word.BoundingBox.Height))
                .OrderBy(value => value)
                .ElementAt(words.Length / 2);
            double tolerance = Math.Max(MinimumLineTolerance, medianHeight * 0.45);
            var groups = new List<List<Word>>();

            foreach (Word word in words)
            {
                List<Word> line = groups.FirstOrDefault(group =>
                    Math.Abs(group.Average(item => item.BoundingBox.Top) -
                             word.BoundingBox.Top) <= tolerance);
                if (line == null)
                {
                    line = new List<Word>();
                    groups.Add(line);
                }
                line.Add(word);
            }

            return groups
                .OrderByDescending(group => group.Average(word => word.BoundingBox.Top))
                .Select(group => string.Join(
                    " ",
                    group
                        .OrderBy(word => word.BoundingBox.Left)
                        .ThenBy(word => word.Text, StringComparer.Ordinal)
                        .Select(word => word.Text)))
                .ToArray();
        }
    }
}
