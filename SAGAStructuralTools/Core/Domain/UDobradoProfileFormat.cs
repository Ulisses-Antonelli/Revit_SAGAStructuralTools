using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SAGAStructuralTools.Core.Domain
{
    public class UDobradoDimensions
    {
        public double HeightMm;
        public double WidthMm;
        public double ThicknessMm;
    }

    /// <summary>
    /// Reconhece e formata a designação de perfil U dobrado a frio (sem aba enrijecedora) no
    /// padrão "U {altura} x {largura} x {espessura}" usado tanto pelo Robot (ex.: "U 200x50x6.3")
    /// quanto pelo catálogo Gerdau já normalizado (ex.: "U 200 x 50 x 6.35").
    /// </summary>
    public static class UDobradoProfileFormat
    {
        private static readonly Regex Pattern =
            new Regex(@"^U\s*(?<h>[\d.]+)\s*x\s*(?<w>[\d.]+)\s*x\s*(?<t>[\d.]+)$", RegexOptions.IgnoreCase);

        public static UDobradoDimensions TryParse(string designation)
        {
            if (string.IsNullOrWhiteSpace(designation)) return null;
            var m = Pattern.Match(Mapping.ProfileMatcher.Normalize(designation));
            if (!m.Success) return null;

            if (!TryParseInvariant(m.Groups["h"].Value, out double h) ||
                !TryParseInvariant(m.Groups["w"].Value, out double w) ||
                !TryParseInvariant(m.Groups["t"].Value, out double t))
                return null;

            return new UDobradoDimensions { HeightMm = h, WidthMm = w, ThicknessMm = t };
        }

        public static string BuildTypeName(UDobradoDimensions d)
            => $"U {FormatLoose(d.HeightMm)} x {FormatLoose(d.WidthMm)} x {FormatLoose(d.ThicknessMm)}";

        /// <summary>Formata uma linha CSV pronta pra anexar ao catálogo de tipos do Revit, no mesmo
        /// estilo já usado no arquivo (largura/altura inteiras, espessura e peso com 2 decimais).</summary>
        public static string BuildCatalogRow(UDobradoDimensions d, double weightKgPerMeter)
        {
            string typeName = BuildTypeName(d);
            string width = FormatInteger(d.WidthMm);
            string height = FormatInteger(d.HeightMm);
            string thickness = d.ThicknessMm.ToString("0.00", CultureInfo.InvariantCulture);
            string weight = weightKgPerMeter.ToString("0.00", CultureInfo.InvariantCulture);
            return $"{typeName},{width},{height},{thickness},{weight}";
        }

        /// <summary>
        /// Estimativa geométrica de peso (kg/m) por parede fina (sem raio de dobra), calibrada contra
        /// o catálogo real da Gerdau (SGA_VIGA_U_DOBRADO.txt): erro tipicamente abaixo de 1% nos
        /// tamanhos testados manualmente, de 45x17x1.8 (1,03 kg/m real vs. 1,04 estimado) até
        /// 200x50x6.35 (14,05 kg/m real vs. 14,01 estimado). É só um ponto de partida editável - o
        /// peso nominal publicado pelo fabricante considera tolerância real de fabricação e pode
        /// divergir um pouco.
        /// </summary>
        public static double EstimateWeightKgPerMeter(UDobradoDimensions d)
        {
            const double SteelWeightPerMm2PerMeter = 0.00785; // aço, 7850 kg/m3
            double centerlinePerimeterMm = d.HeightMm + 2 * d.WidthMm - 3 * d.ThicknessMm;
            double areaMm2 = d.ThicknessMm * centerlinePerimeterMm;
            return areaMm2 * SteelWeightPerMm2PerMeter;
        }

        private static string FormatLoose(double v)
            => v.ToString("0.####", CultureInfo.InvariantCulture);

        private static string FormatInteger(double v)
            => v.ToString("0", CultureInfo.InvariantCulture);

        private static bool TryParseInvariant(string s, out double value)
            => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
