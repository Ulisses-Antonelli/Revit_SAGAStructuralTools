using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using SAGAStructuralTools.Core.Models;

namespace SAGAStructuralTools.Core.Mapping
{
    /// <summary>
    /// Extrai a designação do perfil do nome IFC e faz o de-para com o catálogo Gerdau.
    /// Suporta perfis métricos (W, HP, I), imperiais U (U8"x17.1) e L (L2"x3/16").
    /// Dupla cantoneira ")( L..." é tratada como cantoneira simples.
    /// </summary>
    public class ProfileMatcher
    {
        private readonly GerdauCatalog _catalog;

        // Perfis métricos: W200x35.9, HP310x79, W 200x35.9
        private static readonly Regex MetricPattern =
            new Regex(@"\b([A-Z]+\d+(?:[xX.]\d+)+)\b", RegexOptions.Compiled);

        // Perfil U imperial: U8"x17.1  ou  U 8"x17.1
        private static readonly Regex ImperialUPattern =
            new Regex("\\bU\\s*(\\d+)\"x([\\d.]+)", RegexOptions.Compiled);

        // Cantoneira L imperial: L2"x3/16"  ou  L 1.1/4"x1/4"
        private static readonly Regex ImperialLPattern =
            new Regex("\\bL\\s*[\\d./]+\"\\s*x\\s*[\\d./]+\"", RegexOptions.Compiled);

        public ProfileMatcher(GerdauCatalog catalog)
        {
            _catalog = catalog;
        }

        public ProfileMapping MatchBeam(string ifcFullName)   => Match(ifcFullName, isColumn: false);
        public ProfileMapping MatchColumn(string ifcFullName) => Match(ifcFullName, isColumn: true);

        private ProfileMapping Match(string ifcFullName, bool isColumn)
        {
            if (string.IsNullOrWhiteSpace(ifcFullName)) return null;

            // Dupla cantoneira: ")( L2"x3/16"" → trata como cantoneira simples
            var cleanName = RemoveDoubleAnglePrefix(ifcFullName);

            // ── 1. Tenta extração de perfil métrico padrão ──
            var designation = ExtractMetricDesignation(cleanName);
            if (designation != null)
            {
                var normalized = Normalize(designation);
                var r = isColumn ? _catalog.FindColumn(normalized) : _catalog.FindBeam(normalized);
                if (r != null) return Build(ifcFullName, r.Value);
            }

            // ── 2. Tenta perfil U imperial (U8"x17.1) via índice de massa linear ──
            var uMatch = ImperialUPattern.Match(cleanName);
            if (uMatch.Success)
            {
                var depth = uMatch.Groups[1].Value;
                if (double.TryParse(uMatch.Groups[2].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double mass))
                {
                    var r = isColumn
                        ? _catalog.FindColumnByUMass(depth, mass)
                        : _catalog.FindBeamByUMass(depth, mass);
                    if (r != null) return Build(ifcFullName, r.Value);
                }
            }

            // ── 3. Tenta cantoneira L imperial (L2"x3/16") via normalização ──
            var lMatch = ImperialLPattern.Match(cleanName);
            if (lMatch.Success)
            {
                var normalized = Normalize(lMatch.Value);
                var r = isColumn ? _catalog.FindColumn(normalized) : _catalog.FindBeam(normalized);
                if (r != null) return Build(ifcFullName, r.Value);
            }

            return null;
        }

        private static string RemoveDoubleAnglePrefix(string name)
            => name.Contains(")(")
                ? Regex.Replace(name, @"\)\(\s*", "")
                : name;

        private static string ExtractMetricDesignation(string fullName)
        {
            var m = MetricPattern.Match(fullName);
            return m.Success ? m.Value : null;
        }

        private static ProfileMapping Build(string ifcFullName, (string FamilyPath, string TypeName) entry)
            => new ProfileMapping
            {
                IfcName    = ifcFullName,
                GerdauName = Path.GetFileNameWithoutExtension(entry.FamilyPath),
                FamilyPath = entry.FamilyPath,
                FamilyType = entry.TypeName
            };

        /// <summary>
        /// Normaliza a designação de perfil para a forma padrão Gerdau.
        /// Ex: W200X22_5 → W 200 x 22.5 | L2"x3/16" → L 2"x 3/16"
        /// </summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;

            // Sublinhado decimal: 22_5 → 22.5
            raw = Regex.Replace(raw, @"(\d)_(\d)", "$1.$2");

            // Espaço entre letra e dígito: W200 → W 200 | x3 → x 3
            raw = Regex.Replace(raw, @"([A-Za-z]+)(\d)", "$1 $2");

            // Separador de dimensão em minúsculo: 200X22 → 200 x 22
            raw = Regex.Replace(raw, @"(\d)\s*[xX]\s*(\d)", "$1 x $2");

            // Remove .0 de inteiros: 13.0 → 13 (mas 35.9 fica intacto)
            raw = Regex.Replace(raw, @"\.0(?!\d)", "");

            // Colapsa espaço imediatamente após símbolo de polegada: 2" x → 2"x
            // Uniformiza catálogo Gerdau ("L 2"" x 3/16"") com IFC ("L2"x3/16")
            raw = Regex.Replace(raw, "\"\\s+", "\"");

            // Colapsa espaços
            raw = Regex.Replace(raw, @"\s+", " ").Trim();

            return raw;
        }
    }
}
