using System;
using System.Globalization;
using System.Windows.Data;

namespace MosquitoNetCalculator.Converters
{
    /// <summary>
    /// Shows empty string for zero values, formatted number + suffix for non-zero.
    /// ConverterParameter = suffix string (e.g. " мм")
    /// </summary>
    public class DimensionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string suffix = parameter as string ?? "";

            if (value is double d && d > 0)
                return $"{d:F0}{suffix}";
            if (value is int i && i > 0)
                return $"{i}{suffix}";

            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s)
            {
                string suffix = parameter as string ?? "";
                s = s.Replace(suffix, "").Trim();
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double result))
                    return result;
                if (double.TryParse(s, out result))
                    return result;
            }
            return 0.0;
        }
    }
}