using System;
using System.Globalization;
using System.Windows.Data;
using MosquitoNetCalculator.Services;

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
            // «Свой товар» rows start with quantity 0 (optional) — render the
            // cell empty, not "0". An explicitly entered quantity still shows.
            if (value is double d)
                return d > 0 ? d.ToString("G", CultureInfo.InvariantCulture) : "";
            return value?.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s)
            {
                // Единый парсер: «2 150,00»/«5,75»/«5.75» — при любой локали ОС.
                if (MoneyFormatService.TryParse(s, out double result) && result > 0)
                    return result;
            }
            return 1.0;
        }
    }
}
