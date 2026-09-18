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

        /// <summary>
        /// Иконочная семья для КОДА. В разметке её задаёт токен
        /// <c>Font.Icon</c> («Segoe Fluent Icons, Segoe MDL2 Assets»);
        /// код не видит словарей приложения (тосты собираются до того,
        /// как окно существует, диалоги — из сервиса), поэтому здесь
        /// та же строка-источник. ПОРЯДОК КРИТИЧЕН: «Segoe Fluent Icons»
        /// есть только на Windows 11 — без второго имени в списке глиф
        /// на Windows 10 рисуется пустым квадратом (разбор — GOTCHAS §31).
        /// </summary>
        internal static FontFamily CreateIconFamily() =>
            new(Font.IconFamilyString);

        /// <summary>Моноширинная семья для КОДА (эквивалент токена Font.Mono).
        /// Каскадия есть только на Win11/Office, поэтому фолбэки обязательны.</summary>
        internal static FontFamily CreateMonoFamily() =>
            new(Font.MonoFamilyString);
    }

    /// <summary>Строки-источники семей — один источник истины для кода и тестов.</summary>
    internal static partial class Font
    {
        internal const string IconFamilyString = "Segoe Fluent Icons, Segoe MDL2 Assets";
        internal const string MonoFamilyString = "Consolas, Cascadia Mono, Courier New";
    }
}
