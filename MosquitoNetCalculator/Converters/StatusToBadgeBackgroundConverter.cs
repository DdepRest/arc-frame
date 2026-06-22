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
            string key = status switch
            {
                "Подтверждён"        => "BadgeSuccessBg",
                "Отправлен на завод" => "BadgeWarningBg",
                "В производстве"     => "BadgeWarningBg",
                "Готов к установке"  => "BadgeSuccessBg",
                "Установлен"         => "BadgeSuccessBg",
                "Оплачен"            => "BadgeSuccessBg",
                "Отменён"            => "BadgeDangerBg",
                _                    => "BadgeDefaultBg",
            };
            return Application.Current?.Resources[key] as Brush ?? Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}