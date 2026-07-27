using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Strap.Application.Tests
{
    internal static class TestDocument
    {
        public static ExtractedDocument Create(params string[] lines)
        {
            IEnumerable<ExtractedLine> extracted = lines.Select(
                (text, index) => new ExtractedLine(
                    text,
                    new SourceTrace("strap.txt", 2, 3, index + 1)));
            return new ExtractedDocument("strap.txt", extracted);
        }

        public static string[] ValidLines(
            string maximumLabel = "Máx",
            string minimumLabel = "Mín")
            => new[]
            {
                "Relatório de reações de apoio",
                string.Empty,
                "Unidades: kN / kN.m",
                "Nó 101",
                "FX FY FZ MX MY MZ",
                $"{maximumLabel} 0,136 0,375 3,143 0,083 -0,104 -0,004",
                $"{minimumLabel}\t-2.056\t0.239\t-3.432\t0.073\t-0.781\t0.381"
            };
    }
}
