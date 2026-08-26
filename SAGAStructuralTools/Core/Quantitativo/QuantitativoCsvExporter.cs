using SAGAStructuralTools.Core.Domain;
using SAGAStructuralTools.Core.Models;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SAGAStructuralTools.Core.Quantitativo
{
    /// <summary>
    /// Exporta a lista de perfis em CSV (ponto e vírgula, decimal com vírgula —
    /// abre direto no Excel em português) — mesmo formato da planilha de
    /// referência: Descrição, Material, Quantidade(m), Peso Unit.(kg/m),
    /// Peso(kg), Peso+margem(kg), com linha de TOTAL.
    /// </summary>
    internal static class QuantitativoCsvExporter
    {
        public static void Export(string filePath, List<QuantitativoLineItem> items, QuantitativoConfig config)
        {
            var culture = new CultureInfo("pt-BR");
            var sb = new StringBuilder();

            string marginHeader = $"PESO + {config.MarginPercent:F0}% P/ PERDA (kg)";
            sb.AppendLine(string.Join(";", "DESCRIÇÃO", "MATERIAL", "QUANTIDADE (m)", "PESO UNIT. (kg/m)", "PESO (kg)", marginHeader));

            foreach (var item in items)
            {
                sb.AppendLine(string.Join(";",
                    Csv(item.Description),
                    Csv(item.Material),
                    item.TotalLengthM.ToString("F1", culture),
                    item.UnitWeightKgM.HasValue ? item.UnitWeightKgM.Value.ToString("F2", culture) : "",
                    item.TotalWeightKg.HasValue ? item.TotalWeightKg.Value.ToString("F1", culture) : "",
                    item.TotalWeightWithMarginKg.HasValue ? item.TotalWeightWithMarginKg.Value.ToString("F1", culture) : ""));
            }

            var (lengthM, weightKg, weightMarginKg) = QuantitativoCalculator.Totals(items);
            sb.AppendLine(string.Join(";", "TOTAL", "",
                lengthM.ToString("F1", culture), "",
                weightKg.ToString("F1", culture),
                weightMarginKg.ToString("F1", culture)));

            File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            return value.Contains(";") || value.Contains("\"") || value.Contains("\n")
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }
    }
}
