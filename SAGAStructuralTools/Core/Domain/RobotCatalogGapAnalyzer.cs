using SAGAStructuralTools.Core.Mapping;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SAGAStructuralTools.Core.Domain
{
    /// <summary>Um perfil U dobrado que não casou com o catálogo (nem exato, nem por tolerância de
    /// espessura), com a sugestão de linha nova a incluir - de preferência copiada de um peso já
    /// publicado na família irmã (viga/pilar), só caindo pra estimativa geométrica quando não existe
    /// nenhuma referência real em nenhuma das duas famílias.</summary>
    public class UDobradoCatalogGap
    {
        public int BarId;
        public string RawDesignation;
        public bool IsColumn;
        public UDobradoDimensions Dimensions;
        public string TargetCatalogTxtPath;
        public double SuggestedWeightKgPerMeter;
        public bool SuggestionFromSibling;
    }

    public static class RobotCatalogGapAnalyzer
    {
        private const double DimensionToleranceMm = 0.05;
        private const double ThicknessToleranceMm = 0.1;

        /// <summary>Analisa uma barra sem correspondência no catálogo. Devolve null se o perfil não
        /// for um U dobrado reconhecível, ou se a família do papel (viga/pilar) não existir de forma
        /// alguma no catálogo carregado (nesse caso não há nem onde escrever a linha nova).</summary>
        public static UDobradoCatalogGap Analyze(GerdauCatalog catalog, RobotMember member, bool isColumn)
        {
            var dims = UDobradoProfileFormat.TryParse(member.Profile.RawDesignation);
            if (dims == null) return null;

            var sameRoleEntries = isColumn ? catalog.ColumnEntries : catalog.BeamEntries;
            string referenceRfaPath = FindAnyUDobradoFamilyFile(sameRoleEntries);
            if (referenceRfaPath == null) return null;

            var siblingEntries = isColumn ? catalog.BeamEntries : catalog.ColumnEntries;
            var sibling = FindPublishedSibling(siblingEntries, dims);

            // Quando existe uma referência real na família irmã, copia a dimensão EXATA dela (não a
            // do Robot) junto com o peso - senão o mesmo perfil físico fica com duas grafias
            // diferentes entre os catálogos de viga e pilar (ex.: "6.3" vs "6.35" pra mesma bitola).
            var finalDims = sibling?.Dimensions ?? dims;

            return new UDobradoCatalogGap
            {
                BarId = member.BarId,
                RawDesignation = member.Profile.RawDesignation,
                IsColumn = isColumn,
                Dimensions = finalDims,
                TargetCatalogTxtPath = Path.ChangeExtension(referenceRfaPath, ".txt"),
                SuggestedWeightKgPerMeter = sibling?.WeightKgPerMeter ?? UDobradoProfileFormat.EstimateWeightKgPerMeter(dims),
                SuggestionFromSibling = sibling != null
            };
        }

        private static string FindAnyUDobradoFamilyFile(IReadOnlyDictionary<string, (string FamilyPath, string TypeName)> entries)
        {
            foreach (var kv in entries)
                if (UDobradoProfileFormat.TryParse(kv.Key) != null)
                    return kv.Value.FamilyPath;
            return null;
        }

        private sealed class PublishedSibling
        {
            public UDobradoDimensions Dimensions;
            public double WeightKgPerMeter;
        }

        private static PublishedSibling FindPublishedSibling(
            IReadOnlyDictionary<string, (string FamilyPath, string TypeName)> entries,
            UDobradoDimensions target)
        {
            // Tolerância maior aqui (não só "quase igual"): uma barra que já bateu ResolveWithTolerance
            // na própria família nunca chega a esse ponto, então qualquer thickness "próxima" na
            // família irmã já é a mesma bitola comercial, só que arredondada diferente entre exportações.
            foreach (var kv in entries)
            {
                var candidate = UDobradoProfileFormat.TryParse(kv.Key);
                if (candidate == null) continue;
                if (Math.Abs(candidate.HeightMm - target.HeightMm) > DimensionToleranceMm) continue;
                if (Math.Abs(candidate.WidthMm - target.WidthMm) > DimensionToleranceMm) continue;
                if (Math.Abs(candidate.ThicknessMm - target.ThicknessMm) > ThicknessToleranceMm) continue;

                var weight = ReadWeightFromCatalogFile(kv.Value.FamilyPath, kv.Value.TypeName);
                if (weight.HasValue) return new PublishedSibling { Dimensions = candidate, WeightKgPerMeter = weight.Value };
            }
            return null;
        }

        private static double? ReadWeightFromCatalogFile(string rfaPath, string typeName)
        {
            try
            {
                var txtPath = Path.ChangeExtension(rfaPath, ".txt");
                if (!File.Exists(txtPath)) return null;

                foreach (var line in CatalogTextReader.ReadAllLines(txtPath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var fields = line.Split(',');
                    if (fields.Length < 5) continue;

                    var name = fields[0].Trim();
                    if (name.StartsWith("\"") && name.EndsWith("\"") && name.Length >= 2)
                        name = name.Substring(1, name.Length - 2).Replace("\"\"", "\"");

                    if (!name.Equals(typeName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (double.TryParse(fields[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double w))
                        return w;
                }
            }
            catch (Exception) { /* falha de leitura não é fatal - cai pra estimativa geométrica */ }
            return null;
        }
    }
}
