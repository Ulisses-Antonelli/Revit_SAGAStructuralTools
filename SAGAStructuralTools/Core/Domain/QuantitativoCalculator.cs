using SAGAStructuralTools.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Core.Domain
{
    /// <summary>
    /// Agrupa medições de perfis por Descrição+Material e soma comprimento/peso.
    /// Sem dependência da Revit API — totalmente testável de forma isolada.
    /// </summary>
    public static class QuantitativoCalculator
    {
        public static List<QuantitativoLineItem> Group(
            IEnumerable<QuantitativoMeasurement> measurements, QuantitativoConfig config)
        {
            config = config ?? new QuantitativoConfig();
            double marginFactor = 1.0 + config.MarginPercent / 100.0;

            var groups = (measurements ?? Enumerable.Empty<QuantitativoMeasurement>())
                .GroupBy(m => (m.Description ?? "", m.Material ?? ""))
                .OrderBy(g => g.Key.Item1)
                .ThenBy(g => g.Key.Item2);

            var result = new List<QuantitativoLineItem>();
            foreach (var g in groups)
            {
                double totalLengthM = g.Sum(m => m.LengthM);
                // Todas as instâncias do mesmo tipo compartilham o peso nominal do
                // tipo — usa o primeiro valor não-nulo encontrado no grupo.
                double? unitWeight = g.Select(m => m.UnitWeightKgM).FirstOrDefault(w => w.HasValue);
                double? totalWeight = unitWeight.HasValue ? totalLengthM * unitWeight.Value : (double?)null;
                double? totalWithMargin = totalWeight.HasValue ? totalWeight.Value * marginFactor : (double?)null;

                result.Add(new QuantitativoLineItem
                {
                    Description   = g.Key.Item1,
                    Material      = g.Key.Item2,
                    ElementCount  = g.Count(),
                    TotalLengthM  = totalLengthM,
                    UnitWeightKgM = unitWeight,
                    TotalWeightKg = totalWeight,
                    TotalWeightWithMarginKg = totalWithMargin
                });
            }
            return result;
        }

        public static (double LengthM, double WeightKg, double WeightWithMarginKg) Totals(
            IEnumerable<QuantitativoLineItem> items)
        {
            var list = items?.ToList() ?? new List<QuantitativoLineItem>();
            return (
                list.Sum(i => i.TotalLengthM),
                list.Sum(i => i.TotalWeightKg ?? 0),
                list.Sum(i => i.TotalWeightWithMarginKg ?? 0));
        }
    }
}
