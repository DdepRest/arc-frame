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
            string key = status switch
            {
                "Подтверждён"        => "BadgeSuccessFg",
                "Отправлен на завод" => "BadgeWarningFg",
                "В производстве"     => "BadgeWarningFg",
                "Готов к установке"  => "BadgeSuccessFg",
                "Установлен"         => "BadgeSuccessFg",
                "Оплачен"            => "BadgeSuccessFg",
                "Отменён"            => "BadgeDangerFg",
                _                    => "BadgeDefaultFg",
            };
            return Application.Current?.Resources[key] as Brush ?? Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}