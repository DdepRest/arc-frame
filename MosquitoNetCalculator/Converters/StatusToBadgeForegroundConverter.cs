using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MosquitoNetCalculator.Converters
{
    /// <summary>
    /// Resolves the order-status badge foreground (text) color from the current theme.
    /// Returns a live brush reference so the badge updates automatically when the theme changes.
    /// </summary>
    public class StatusToBadgeForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string status = value as string ?? "";
            // Статус → ключ токена живёт в модели (см. фон бейджа).
            var (_, key) = MosquitoNetCalculator.Models.OrderStatuses.GetBadgeKeys(status);
            return Application.Current?.Resources[key] as Brush ?? Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}