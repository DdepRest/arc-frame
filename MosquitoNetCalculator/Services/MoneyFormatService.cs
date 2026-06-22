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
        /// Returns true on success and outputs the parsed value.
        /// </summary>
        public static bool TryParse(string input, out double result)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                result = 0;
                return false;
            }

            // Remove spaces and normalize separators
            var normalized = input
                .Replace(" ", "")
                .Replace("\u00A0", "") // non-breaking space
                .Replace(".", ",");

            return double.TryParse(normalized, NumberStyles.Any, RuNumberFormat, out result);
        }
    }
}
