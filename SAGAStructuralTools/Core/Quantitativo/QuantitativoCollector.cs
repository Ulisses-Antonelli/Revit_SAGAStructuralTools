using Autodesk.Revit.DB;
using SAGAStructuralTools.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace SAGAStructuralTools.Core.Quantitativo
{
    internal readonly struct QuantitativoEntry
    {
        public QuantitativoEntry(ElementId id, QuantitativoMeasurement measurement)
        {
            Id = id;
            Measurement = measurement;
        }

        public ElementId Id { get; }
        public QuantitativoMeasurement Measurement { get; }
    }

    /// <summary>Resultado da coleta: medições prontas pro cálculo, cada uma com o ElementId de origem.</summary>
    internal class QuantitativoCollectionResult
    {
        public List<QuantitativoEntry> Entries { get; } = new List<QuantitativoEntry>();
        public List<QuantitativoMeasurement> Measurements => Entries.ConvertAll(e => e.Measurement);
        public List<string> Warnings { get; } = new List<string>();
        public int SkippedNonSteelCount { get; set; }
    }

    /// <summary>
    /// Lê um conjunto de ElementId selecionados, filtra pra só vigas/pilares de
    /// aço (via <see cref="SteelMemberReader"/>) e organiza o resultado pro
    /// cálculo e pra escrita posterior de parâmetros por grupo.
    /// </summary>
    internal static class QuantitativoCollector
    {
        public static QuantitativoCollectionResult Collect(Document doc, IEnumerable<ElementId> selectedIds)
        {
            var result = new QuantitativoCollectionResult();
            var ids = (selectedIds ?? Enumerable.Empty<ElementId>()).Distinct().ToList();

            foreach (var id in ids)
            {
                var instance = doc.GetElement(id) as FamilyInstance;
                if (instance == null) { result.SkippedNonSteelCount++; continue; }

                if (!SteelMemberReader.TryRead(instance, out var measurement, out var warning))
                {
                    result.SkippedNonSteelCount++;
                    if (warning != null) result.Warnings.Add(warning);
                    continue;
                }

                if (warning != null) result.Warnings.Add(warning);

                result.Entries.Add(new QuantitativoEntry(id, measurement));
            }

            return result;
        }
    }
}
