using System;
using System.Globalization;
using System.Windows.Data;

namespace MosquitoNetCalculator.Converters
{
    /// <summary>
    /// Formats a DateTime as "10 июня 2025" using Russian month names,
    /// regardless of the system's current culture.
    /// </summary>
    public class RussianDateConverter : IValueConverter
    {
        private static readonly CultureInfo RuCulture = new CultureInfo("ru-RU");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dt)
                return dt.ToString("dd MMMM yyyy", RuCulture);
            return value?.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}