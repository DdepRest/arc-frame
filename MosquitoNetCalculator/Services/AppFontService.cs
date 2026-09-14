using System;
using System.Windows;
using System.Windows.Media;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Вшитый Inter (SIL OFL 1.1) для всей типографики приложения.
    ///
    /// <para><b>Почему значение задаётся кодом, а не только XAML-токеном.</b>
    /// WPF разбирает строку <see cref="FontFamily"/>, и в АБСОЛЮТНОМ pack-URI
    /// символ «#» понимается как URI-фрагмент, а не как разделитель
    /// «папка → имя семейства». Токен вида
    /// <c>pack://application:,,,/MosquitoNetCalculator;component/Resources/Fonts/#Inter</c>
    /// поэтому молча падает на фолбэк (проверено тестом
    /// <c>TypographyTests.InterFont_IsActuallyBundled_NotSilentFallback</c>:
    /// «только pack-URI → не загрузился»). Работает ровно одна форма —
    /// базовый URI папки + относительное имя:</para>
    /// <code>new FontFamily(new Uri(folder), "./#Inter")</code>
    /// <para>В XAML эту форму выразить нельзя, поэтому токен
    /// <c>Font.Text</c> объявлен в <c>Themes/Tokens.Typography.xaml</c> как
    /// фолбэк (Segoe UI/Tahoma) и подменяется здесь при старте — до создания
    /// окон. Следствие: потребители обязаны брать токен через
    /// <c>DynamicResource</c> (проверяет
    /// <c>DesignTokenGuardTests.FontTokens_AreConsumedViaDynamicResource</c>).</para>
    /// </summary>
    internal static class AppFontService
    {
        /// <summary>Папка вшитых шрифтов внутри сборки (файлы — <c>&lt;Resource&gt;</c>).</summary>
        internal const string InterFolder =
            "pack://application:,,,/MosquitoNetCalculator;component/Resources/Fonts/";

        /// <summary>Относительное имя семейства внутри папки (см. разбор «#» выше).</summary>
        internal const string InterRelativeFamily = "./#Inter";

        /// <summary>
        /// Подменяет токен <c>Font.Text</c> на вшитый Inter. Идемпотентно,
        /// вызывается из <c>App.OnStartup</c> до создания первого окна и из
        /// тестового bootstrap'а — чтобы тесты видели ту же типографику.
        /// </summary>
        public static void Install(Application application) =>
            application.Resources["Font.Text"] = CreateInterFamily();

        internal static FontFamily CreateInterFamily() =>
            new(new Uri(InterFolder, UriKind.Absolute), InterRelativeFamily);
    }
}
