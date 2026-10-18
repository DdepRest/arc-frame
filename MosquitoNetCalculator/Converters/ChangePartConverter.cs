using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MosquitoNetCalculator.Converters
{
    /// <summary>
    /// Конвертер для разделения строки изменения формата «{Тема}: {Описание}»
    /// на структурированные части для удобного отображения в карточке обновлений:
    /// <list type="bullet">
    ///   <item><description><c>topic</c> — возвращает тему изменения (до двоеточия) или пустую строку, если темы нет.</description></item>
    ///   <item><description><c>body</c> — возвращает текст описания (после двоеточия) или всю строку, если темы нет.</description></item>
    ///   <item><description><c>hasTopic</c> — возвращает <see cref="Visibility.Visible"/> если есть тема, иначе <see cref="Visibility.Collapsed"/>.</description></item>
    /// </list>
    /// </summary>
    public class ChangePartConverter : IValueConverter
    {
        private const int MaxTopicLength = 70;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string text = value as string ?? string.Empty;
            string param = parameter as string ?? "body";

            var (topic, body) = SplitChange(text);

            return param.ToLowerInvariant() switch
            {
                "topic" => topic,
                "body" => body,
                "hastopic" => string.IsNullOrWhiteSpace(topic) ? Visibility.Collapsed : Visibility.Visible,
                _ => body
            };
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();

        /// <summary>
        /// Разделяет строку изменения на тему (до первого двоеточия) и описание (после двоеточия).
        /// Если двоеточия нет или оно слишком далеко (более 70 символов), строка считается сплошным описанием.
        /// </summary>
        public static (string Topic, string Body) SplitChange(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return (string.Empty, string.Empty);

            string trimmed = input.Trim();
            int colonIndex = trimmed.IndexOf(':');

            // Двоеточие должно быть не в самом начале, не в самом конце и в пределах разумной длины темы
            if (colonIndex > 0 && colonIndex <= MaxTopicLength)
            {
                string topic = trimmed.Substring(0, colonIndex).Trim();
                string body = trimmed.Substring(colonIndex + 1).Trim();

                if (!string.IsNullOrEmpty(body))
                {
                    // Делаем первую букву описания заглавной, если она строчная
                    if (char.IsLower(body[0]))
                    {
                        body = char.ToUpper(body[0], CultureInfo.CurrentCulture) + (body.Length > 1 ? body.Substring(1) : string.Empty);
                    }
                    return (topic, body);
                }
            }

            return (string.Empty, trimmed);
        }
    }
}
