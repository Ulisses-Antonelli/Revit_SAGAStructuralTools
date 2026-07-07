using System;
using System.Globalization;
using System.Windows.Data;

namespace SAGAStructuralTools.UI.Converters
{
    public class EnumToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value?.ToString() == parameter?.ToString();

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return Enum.Parse(targetType, parameter?.ToString() ?? "");
            return Binding.DoNothing;
        }
    }
}
