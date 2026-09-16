using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MosquitoNetCalculator.Converters
{
    /// <summary>
    /// Resolves the order-status badge background color from the current theme.
    /// Returns a live brush reference so the badge updates automatically when the theme changes.
    /// </summary>
    public class StatusToBadgeBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string status = value as string ?? "";
            // Статус → ключ токена живёт в модели: до v3.53 та же таблица была
            // продублирована здесь, во втором конвертере и в точке статуса.
            var (key, _) = MosquitoNetCalculator.Models.OrderStatuses.GetBadgeKeys(status);
            return Application.Current?.Resources[key] as Brush ?? Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}