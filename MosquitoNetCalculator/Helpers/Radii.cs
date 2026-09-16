using System.Windows;
using System.Windows.Media;

namespace MosquitoNetCalculator.Helpers
{
    /// <summary>
    /// Радиусы из токенов (v3.53.0) для кода — те же ключи <c>Radius.*</c>, что
    /// и в <c>Themes/Tokens.Radius.xaml</c>; контракт —
    /// <c>docs/specs/design-system-v3.53.md</c>.
    ///
    /// <para><b>Зачем.</b> До этого радиусы в C# задавались числами на месте
    /// (<c>new CornerRadius(2)</c>, <c>new CornerRadius(12)</c> в тостах,
    /// <c>new CornerRadius(4)</c> в карточках «Истории обновлений») — разметка
    /// уже жила на токенах, а код оставался единственным местом, где шкала могла
    /// разъехаться. Теперь код читает те же токены, что и XAML, поэтому смена
    /// шкалы в одном файле меняет скругления всего приложения.</para>
    ///
    /// <para><b>Почему TryFindResource с запасным значением.</b> Тост собирается
    /// до/вне показа окна, а <c>ChangelogViewBuilder</c> вызывает
    /// <c>Application.Current</c> и того раньше — в тестах словари могут быть не
    /// загружены. Радиус — не тема: он одинаков в светлой и тёмной, поэтому
    /// запасное значение здесь безопасно (в отличие от цвета, который читают
    /// через <c>ThemeService</c>).</para>
    /// </summary>
    public static class Radii
    {
        /// <summary>Карточка/панель — <c>Radius.Card</c> (12).</summary>
        public static CornerRadius Card => Token("Radius.Card", 12);

        /// <summary>Поле ввода, кнопка, чип — <c>Radius.Control</c> (8).</summary>
        public static CornerRadius Control => Token("Radius.Control", 8);

        /// <summary>Мелкий элемент внутри строки — <c>Radius.Element</c> (4).</summary>
        public static CornerRadius Element => Token("Radius.Element", 4);

        /// <summary>
        /// Круг — <c>Radius.Pill</c> (999). ТОЛЬКО для квадратных элементов
        /// (кружок иконки, точка, бегунок): у квадрата предел скругления —
        /// окружность, и 999 даёт её пиксель-в-пиксель. На широком элементе WPF
        /// НЕ клампит радиус до полусферы и рисует эллипс вместо капсулы.
        /// </summary>
        public static CornerRadius Pill => Token("Radius.Pill", 999);

        /// <summary>
        /// Капсула тонкой полосы 5–6px — <c>Radius.CapsuleSm</c> (3): полоса
        /// акцента в тосте, прогресс, трек скроллбара.
        /// </summary>
        public static CornerRadius CapsuleSm => Token("Radius.CapsuleSm", 3);

        private static CornerRadius Token(string key, double fallback) =>
            Application.Current?.TryFindResource(key) is CornerRadius value
                ? value
                : new CornerRadius(fallback);
    }
}
