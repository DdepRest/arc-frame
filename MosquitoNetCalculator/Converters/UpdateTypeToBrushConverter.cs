using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Converters
{
    /// <summary>
    /// Maps an update Type ("Новинка" / "Улучшение" / "Исправление") to a themed brush.
    /// Used for the colored accent bar, the type-badge background and the version text.
    /// Принимает и строку типа, и запись <see cref="UpdateItem"/> целиком — у записи
    /// с композитным типом («Новинка + Исправление») берётся ПЕРВЫЙ тип (главный).
    /// ConverterParameter:
    ///   "fg"     — foreground / text color (Accent / Success / Warning)
    ///   "soft"   — soft tinted background (Badge*Bg)
    ///   "strong" — saturated bar color (Accent / Success / Warning)
    ///   default  — strong bar color
    /// </summary>
    public class UpdateTypeToBrushConverter : IValueConverter, IMultiValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => ConvertCore(ResolveType(value), parameter);

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            => ConvertCore(values?.Select(ResolveType).FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "", parameter);

        private static string ResolveType(object? value) => value switch
        {
            UpdateItem item => item.Types.FirstOrDefault() ?? "",
            string s => s,
            _ => ""
        };

        private static object ConvertCore(string type, object? parameter)
        {
            string mode = (parameter as string ?? "strong").ToLowerInvariant();

            string key = type switch
            {
                "Новинка"     => "Success",
                "Исправление" => "Warning",
                "Улучшение"   => "Accent",
                _             => "TextMuted",
            };

            if (mode == "soft")
            {
                key = type switch
                {
                    "Новинка"     => "BadgeSuccessBg",
                    "Исправление" => "BadgeWarningBg",
                    "Улучшение"   => "BadgeDefaultBg",
                    _             => "ChipBg",
                };
            }
            else if (mode == "fg")
            {
                key = type switch
                {
                    "Новинка"     => "BadgeSuccessFg",
                    "Исправление" => "BadgeWarningFg",
                    "Улучшение"   => "BadgeDefaultFg",
                    _             => "TextMuted",
                };
            }

            return Application.Current?.Resources[key] as Brush ?? Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}