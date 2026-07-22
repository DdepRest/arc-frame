using System;
using System.Globalization;
using System.Windows.Data;

namespace MosquitoNetCalculator.Converters
{
    /// <summary>
    /// Formats quantity values allowing decimal input (e.g. 5.75, 0.5).
    /// ConvertBack accepts both dot and comma as decimal separators.
    /// </summary>
    public class QuantityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
                return d.ToString("G", CultureInfo.InvariantCulture);
            return value?.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s)
            {
                s = s.Trim().Replace(',', '.');
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double result) && result > 0)
                    return result;
            }
            return 1.0;
        }
    }
}
