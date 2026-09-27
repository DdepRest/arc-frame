using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Converters
{
    /// <summary>
    /// Maps an update Type to a Segoe Fluent Icons glyph used in the type badge.
    /// Новинка → ✦ star, Улучшение → ↗ up arrow, Исправление → ⚙ settings/repair.
    /// Принимает и строку типа, и запись UpdateItem (у композита берётся первый тип).
    /// </summary>
    public class UpdateTypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string type = value switch
            {
                UpdateItem item => item.Types.FirstOrDefault() ?? "",
                string s => s,
                _ => ""
            };
            return type switch
            {
                "Новинка"     => "\uE735", // star
                "Исправление" => "\uE90F", // repair/wrench
                "Улучшение"   => "\uE74A", // up arrow
                _             => "\uE946", // info
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}