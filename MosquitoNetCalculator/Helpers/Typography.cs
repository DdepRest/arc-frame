using System.Windows;

namespace MosquitoNetCalculator.Helpers
{
    /// <summary>
    /// Типографика из дизайн-токенов (v3.53.0 / v3.54.4) для программного кода C# —
    /// те же ключи <c>Type.*</c>, что и в <c>Themes/Tokens.Typography.xaml</c>;
    /// контракт — <c>docs/specs/design-system-v3.53.md</c>.
    ///
    /// <para><b>Зачем.</b> В коде C# создаётся часть динамического UI (тосты
    /// <c>ToastService</c>, шаблоны <c>TemplatesWindow</c>, журнал версий
    /// <c>ChangelogViewBuilder</c>, автосайзер колонок). До этого они держали
    /// литералы <c>FontSize = 11</c>, <c>12</c>, <c>14</c>. Этот класс
    /// даёт строго типизированный доступ к шкале шрифтов с безопасным фолбэком
    /// на случай вызова вне WPF Application loop (например, в юнит-тестах).</para>
    /// </summary>
    public static class Typography
    {
        /// <summary>Монограмма бейджа (9px) — <c>Type.Monogram</c>.</summary>
        public static double Monogram => Token("Type.Monogram", 9.0);

        /// <summary>Подписи, хинты, шапки колонок, бейджи (11px) — <c>Type.Caption</c>.</summary>
        public static double Caption => Token("Type.Caption", 11.0);

        /// <summary>Основной текст интерфейса (12px) — <c>Type.Body</c>.</summary>
        public static double Body => Token("Type.Body", 12.0);

        /// <summary>Акцентный текст, подписи чипов, заголовки карточек (14px) — <c>Type.BodyLg</c>.</summary>
        public static double BodyLg => Token("Type.BodyLg", 14.0);

        /// <summary>Заголовок блока/секции (16px) — <c>Type.Subtitle</c>.</summary>
        public static double Subtitle => Token("Type.Subtitle", 16.0);

        /// <summary>Заголовок панели (18px) — <c>Type.Title</c>.</summary>
        public static double Title => Token("Type.Title", 18.0);

        /// <summary>Крупный заголовок (20px) — <c>Type.TitleLg</c>.</summary>
        public static double TitleLg => Token("Type.TitleLg", 20.0);

        /// <summary>Числовая метрика (24px) — <c>Type.Display</c>.</summary>
        public static double Display => Token("Type.Display", 24.0);

        /// <summary>Крупная метрика и ИТОГО (32px) — <c>Type.Metric</c>.</summary>
        public static double Metric => Token("Type.Metric", 32.0);

        /// <summary>Экранный герой (42px) — <c>Type.Hero</c>.</summary>
        public static double Hero => Token("Type.Hero", 42.0);

        private static double Token(string key, double fallback) =>
            Application.Current?.TryFindResource(key) is double value
                ? value
                : fallback;
    }
}
