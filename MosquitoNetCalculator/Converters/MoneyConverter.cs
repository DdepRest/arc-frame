using System;
using System.Globalization;
using System.Windows.Data;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Converters
{
    /// <summary>
    /// Formats monetary values with a thousands separator (space) and two decimal places.
    /// ConvertBack accepts user input with or without separators and normalizes it.
    /// </summary>
    public class MoneyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
                return MoneyFormatService.Format(d);
            return value?.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && MoneyFormatService.TryParse(s, out double result))
                return result;
            return 0.0;
        }
    }
}