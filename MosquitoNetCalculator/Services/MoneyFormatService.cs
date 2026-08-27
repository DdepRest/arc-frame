using System;
using System.Globalization;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Single source of truth for formatting monetary values throughout the application.
    /// Uses Russian locale with a space as the thousands separator and a comma for decimals.
    /// Examples: 1000 → "1 000,00"; 1234567.89 → "1 234 567,89".
    /// </summary>
    public static class MoneyFormatService
    {
        /// <summary>
        /// Cached Russian culture (ru-RU). Reused across services for consistent formatting
        /// (e.g. money, decimal displays).
        /// </summary>
        public static readonly CultureInfo RuCulture;

        private static readonly NumberFormatInfo RuNumberFormat;

        static MoneyFormatService()
        {
            RuCulture = CultureInfo.GetCultureInfo("ru-RU");
            RuNumberFormat = (NumberFormatInfo)RuCulture.NumberFormat.Clone();
            RuNumberFormat.NumberGroupSeparator = " ";
            RuNumberFormat.NumberDecimalSeparator = ",";
            RuNumberFormat.NumberDecimalDigits = 2;
        }

        /// <summary>
        /// Formats a monetary value with thousands separator and two decimal places.
        /// </summary>
        public static string Format(double amount)
        {
            return amount.ToString("N", RuNumberFormat);
        }

        /// <summary>
        /// Formats a monetary value without decimal places (useful for unit prices in Quick-Add).
        /// </summary>
        public static string FormatWhole(double amount)
        {
            var fmt = (NumberFormatInfo)RuNumberFormat.Clone();
            fmt.NumberDecimalDigits = 0;
            return amount.ToString("N", fmt);
        }

        /// <summary>
        /// Parses a user-entered string that may contain spaces, dots, or commas.
        /// Single source of truth for ALL user-facing numeric input in the app
        /// (QuickAdd, grid converters, AI-карточка, откосы, монтаж) — «2 150,00»
        /// никогда не должно превращаться в 0 из-за локали или разделителей.
        /// Returns true on success and outputs the parsed value.
        /// </summary>
        public static bool TryParse(string? input, out double result)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                result = 0;
                return false;
            }

            // Remove all space variants used as thousand separators:
            // regular, non-breaking (U+00A0), thin (U+2009), narrow NBSP (U+202F)
            var compact = input
                .Replace(" ", "")
                .Replace("\u00A0", "")
                .Replace("\u2009", "")
                .Replace("\u202F", "");

            // Единый ru-формат: «2 150,00», «2150,5», «2150.50» (точка → запятая).
            // Сознательно БЕЗ invariant-фолбэка: он трактовал бы «15000,5,»
            // как 15005 (запятая — разделитель тысяч), ломая контракт
            // GOTCHAS#13 — кривые варианты обязаны возвращать false.
            return double.TryParse(compact.Replace('.', ','), NumberStyles.Any, RuNumberFormat, out result);
        }

        /// <summary>
        /// Parses a user-entered integer that may contain space thousand
        /// separators («1 360» → 1360). Companion to <see cref="TryParse"/>
        /// for целых полей (ширина/высота/количество). Returns true on success.
        /// </summary>
        public static bool TryParseInt(string? input, out int result)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                result = 0;
                return false;
            }

            var compact = input
                .Replace(" ", "")
                .Replace("\u00A0", "")
                .Replace("\u2009", "")
                .Replace("\u202F", "");

            return int.TryParse(compact, NumberStyles.Integer, RuCulture, out result);
        }
    }
}
