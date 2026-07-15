using System;
using System.Globalization;
using System.Windows.Data;

namespace SAGAStructuralTools.UI.Converters
{
    /// <summary>
    /// Conversor numérico tolerante para campos editáveis: aceita vírgula ou ponto
    /// como separador decimal e preserva estados intermediários durante a digitação.
    /// </summary>
    public class FlexibleDoubleConverter : IValueConverter
    {
        private static readonly CultureInfo DisplayCulture =
            CultureInfo.GetCultureInfo("pt-BR");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is double number
                ? number.ToString("0.########", DisplayCulture)
                : "0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = (value as string)?.Trim();
            if (string.IsNullOrEmpty(text) || text == "+" || text == "-" ||
                text.EndsWith(",", StringComparison.Ordinal) ||
                text.EndsWith(".", StringComparison.Ordinal))
                return Binding.DoNothing;

            return TryParse(text, out double number)
                ? (object)number
                : Binding.DoNothing;
        }

        internal static bool TryParse(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string normalized = text.Trim()
                .Replace("\u00A0", "")
                .Replace(" ", "");

            int lastComma = normalized.LastIndexOf(',');
            int lastDot = normalized.LastIndexOf('.');
            int decimalIndex = Math.Max(lastComma, lastDot);

            if (decimalIndex >= 0)
            {
                string integerPart = normalized.Substring(0, decimalIndex)
                    .Replace(",", "")
                    .Replace(".", "");
                string fractionPart = normalized.Substring(decimalIndex + 1)
                    .Replace(",", "")
                    .Replace(".", "");
                normalized = integerPart + "." + fractionPart;
            }

            return double.TryParse(
                normalized,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture,
                out value);
        }
    }
}
