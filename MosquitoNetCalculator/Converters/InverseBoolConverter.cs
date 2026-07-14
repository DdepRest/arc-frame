using System;
using System.Globalization;
using System.Windows.Data;

namespace MosquitoNetCalculator.Converters
{
    /// <summary>
    /// Конвертер: инвертирует bool. Используется для IsEnabled="{Binding IsChecked, Converter=...}"
    /// чтобы поля диапазона страниц были disabled когда «Все страницы» включён.
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return false;
        }
    }
}
