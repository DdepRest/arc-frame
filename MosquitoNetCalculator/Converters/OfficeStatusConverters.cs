using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Converters
{
    /// <summary>
    /// Фон бейджа статуса офиса в админ-панели. Возвращает живую ссылку на
    /// кисть темы, поэтому бейдж перекрашивается при смене темы автоматически.
    /// </summary>
    public class OfficeStatusToBadgeBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var key = value is OfficeStatus s
                ? s switch
                {
                    OfficeStatus.UpToDate => "BadgeSuccessBg",
                    OfficeStatus.Outdated => "BadgeWarningBg",
                    _ => "BadgeDangerBg",
                }
                : "BadgeDefaultBg";
            return Application.Current?.Resources[key] as Brush ?? Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Цвет текста бейджа статуса офиса в админ-панели (живая кисть темы).
    /// </summary>
    public class OfficeStatusToBadgeForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var key = value is OfficeStatus s
                ? s switch
                {
                    OfficeStatus.UpToDate => "BadgeSuccessFg",
                    OfficeStatus.Outdated => "BadgeWarningFg",
                    _ => "BadgeDangerFg",
                }
                : "BadgeDefaultFg";
            return Application.Current?.Resources[key] as Brush ?? Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
