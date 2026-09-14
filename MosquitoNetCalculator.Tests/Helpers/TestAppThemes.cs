using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace MosquitoNetCalculator.Tests.Helpers
{
    /// <summary>
    /// Единый bootstrap для тестов, которым нужны РЕАЛЬНЫЕ стили приложения:
    /// поднимает <see cref="Application"/> и мержит словари <c>Themes/*.xaml</c>
    /// ровно в том порядке, что <c>App.xaml</c>, плюс регистрирует конвертеры.
    ///
    /// <para><b>Почему это отдельный тип.</b> Раньше bootstrap был скопирован в
    /// каждый STA-класс, и вся коллекция <c>WPF_UI</c> (45 кейсов) делила одну
    /// хрупкую последовательность. Один сбой в ней валил не один тест, а все
    /// 45 — с сообщением, по которому причина не видна (голый стек
    /// <c>WpfXamlLoader</c> или «более одного Application»).</para>
    ///
    /// <para><b>Что именно лечится.</b> У WPF два статических слота:
    /// <c>_appCreatedInThisAppDomain</c> (гейт конструктора) и
    /// <c>_appInstance</c> (за <c>Application.Current</c>). Они могут разойтись:
    /// гейт выставлен, а инстанса уже нет (см. разбор в
    /// <c>AppLifecycleTests.ClearWpfApplicationStatic</c>). Старые копии
    /// проверяли только <c>Application.Current != null</c> и в таком состоянии
    /// очистку пропускали — после чего <c>new Application()</c> бросал
    /// «Нельзя создать более одного экземпляра…» в каждом следующем тесте
    /// коллекции. Здесь состояние проверяется и чинится по обоим слотам.</para>
    /// </summary>
    internal static class TestAppThemes
    {
        public const int DefaultTimeoutMs = 90_000;

        /// <summary>
        /// Попытки загрузки одного словаря. Словари — loose XAML с диска, и
        /// первый прогон в свежем клоне может поймать разовую ошибку чтения
        /// (антивирус/индексатор обходят только что созданные файлы). Из-за
        /// одного такого сбоя весь набор падать не должен.
        /// </summary>
        private const int DictionaryLoadAttempts = 3;

        /// <summary>Словари в порядке App.xaml: StaticResource между словарями
        /// требует порядок загрузки (FluentFocusVisual до тех, кто его берёт).</summary>
        private static readonly string[] Dictionaries =
        {
            "Brushes.xaml", "FocusVisualStyles.xaml",
            // Design tokens v3.53.0 — тот же порядок, что в App.xaml: стили
            // ссылаются на них через DynamicResource.
            "Tokens.Spacing.xaml", "Tokens.Radius.xaml",
            "Tokens.Typography.xaml", "Tokens.Motion.xaml",
            "CardStyles.xaml",
            "FontStyles.xaml", "TabStyles.xaml", "ButtonStyles.xaml",
            "InputStyles.xaml", "DataGridStyles.xaml", "InputStyles.RadioButton.xaml",
            "ScrollViewerStyles.xaml", "ContextMenuStyles.xaml",
            // После ButtonStyles: EmptyState.Action наследует GhostButton
            // через BasedOn StaticResource (тот же порядок, что в App.xaml).
            "EmptyStateStyles.xaml",
            "MiscStyles.xaml",
        };

        private static readonly string SourceDir = LocateSourceProject();

        /// <summary>
        /// Запускает тело теста на выделенном STA-потоке, гарантируя приложение
        /// с темами. Приложение переиспользуется между тестами (темы при этом не
        /// переключаются), поэтому 12 словарей парсятся один раз на процесс, а не
        /// 12×45 раз за прогон — меньше файлового ввода-вывода, меньше шансов
        /// поймать разовый сбой чтения.
        /// </summary>
        public static void RunOnSta(Action body, int timeoutMs = DefaultTimeoutMs)
        {
            WpfTestHelper.RunOnSta(() =>
            {
                Ensure();
                body();
            }, timeoutMs);
        }

        /// <summary>
        /// Идемпотентно приводит процесс в состояние «есть живое приложение
        /// с темами»: переиспользует готовое, иначе чинит статику WPF и
        /// поднимает приложение заново.
        /// </summary>
        public static void Ensure()
        {
            if (HasThemedApplication()) return;

            // Оба слота чистим ВСЕГДА: Current может быть null, когда гейт
            // создания ещё выставлен — иначе следующий new Application() бросит.
            ResetStatics();

            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            foreach (var name in Dictionaries)
            {
                string path = Path.Combine(SourceDir, "Themes", name);
                application.Resources.MergedDictionaries.Add(LoadWithRetry(
                    () => new ResourceDictionary { Source = new Uri(path, UriKind.Absolute) },
                    DictionaryLoadAttempts, name, path));
            }

            RegisterConverters(application);

            // Та же подмена токена Font.Text, что делает App.OnStartup:
            // вшитый Inter задаётся кодом (в XAML абсолютный pack-URI для
            // шрифта не выразить — см. AppFontService).
            MosquitoNetCalculator.Services.AppFontService.Install(application);
        }

        /// <summary>
        /// Загружает ресурс с повторами. Итоговая ошибка называет файл и
        /// исходную причину — вместо голого стека загрузчика XAML.
        /// </summary>
        internal static T LoadWithRetry<T>(Func<T> load, int attempts, string what, string path)
        {
            Exception? last = null;

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    return load();
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (attempt < attempts)
                        Thread.Sleep(50 * attempt);   // короткая пауза: разовый сбой чтения
                }
            }

            throw new InvalidOperationException(
                $"Не удалось загрузить «{what}» из «{path}» за {attempts} попытки: " +
                $"{last?.GetType().Name}: {last?.Message}", last);
        }

        /// <summary>
        /// Чистит оба статических слота WPF безусловно (в отличие от проверки
        /// одного <c>Application.Current</c>) — это и есть лечение «отравленного»
        /// состояния. Если WPF переименует поля, бросает actionable-ошибку.
        /// </summary>
        internal static void ResetStatics()
        {
            var appType = typeof(Application);
            const System.Reflection.BindingFlags PrivateStatic =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;

            // 1. Гейт «приложение уже создавалось» — конструктор проверяет его
            //    ПЕРВЫМ, поэтому и чистим первым.
            var flagField = appType.GetField("_appCreatedInThisAppDomain", PrivateStatic)
                ?? throw new InvalidOperationException(
                    "TestAppThemes: WPF Application._appCreatedInThisAppDomain не найдено на " +
                    appType.FullName + " (" + appType.Assembly.GetName().FullName + ") — " +
                    "WPF переименовал приватное поле, поправьте TestAppThemes.ResetStatics.");
            flagField.SetValue(null, false);

            // 2. Ссылка на инстанс — за Application.Current.
            var refField = appType.GetField("_appInstance", PrivateStatic)
                ?? throw new InvalidOperationException(
                    "TestAppThemes: WPF Application._appInstance не найдено на " +
                    appType.FullName + " (" + appType.Assembly.GetName().FullName + ") — " +
                    "WPF переименовал приватное поле, поправьте TestAppThemes.ResetStatics.");
            refField.SetValue(null, null);
        }

        /// <summary>
        /// Приводит статику к состоянию «гейт выставлен, инстанса нет» — то,
        /// что ломало всю коллекцию до этой правки. Только для регрессионного
        /// теста самовосстановления.
        /// </summary>
        internal static void PoisonStaticStateForTest()
        {
            ResetStatics();

            var flagField = typeof(Application).GetField("_appCreatedInThisAppDomain",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            flagField.SetValue(null, true);
        }

        /// <summary>Живое приложение с уже загруженными темами?</summary>
        private static bool HasThemedApplication()
        {
            try
            {
                var current = Application.Current;
                return current != null
                    && current.Resources["Surface"] != null
                    && current.Resources["GhostButton"] != null;
            }
            catch (InvalidOperationException)
            {
                // Геттер Current делает VerifyAccess на диспетчере приложения и
                // может бросить после начатого shutdown — такое приложение мёртвое.
                return false;
            }
        }

        /// <summary>Конвертеры из App.xaml — продукты берут их через StaticResource.</summary>
        private static void RegisterConverters(Application application)
        {
            application.Resources["DimConv"] = new MosquitoNetCalculator.Converters.DimensionConverter();
            application.Resources["BoolToVis"] = new BooleanToVisibilityConverter();
            application.Resources["StatusToBadgeBg"] = new MosquitoNetCalculator.Converters.StatusToBadgeBackgroundConverter();
            application.Resources["StatusToBadgeFg"] = new MosquitoNetCalculator.Converters.StatusToBadgeForegroundConverter();
            application.Resources["MoneyConv"] = new MosquitoNetCalculator.Converters.MoneyConverter();
            application.Resources["QtyConv"] = new MosquitoNetCalculator.Converters.QuantityConverter();
            application.Resources["RussianDateConv"] = new MosquitoNetCalculator.Converters.RussianDateConverter();
            application.Resources["UpdateTypeBrush"] = new MosquitoNetCalculator.Converters.UpdateTypeToBrushConverter();
            application.Resources["UpdateTypeIcon"] = new MosquitoNetCalculator.Converters.UpdateTypeToIconConverter();
            application.Resources["IsMyVersion"] = new MosquitoNetCalculator.Converters.IsMyVersionConverter();
            application.Resources["OfficeStatusBadgeBg"] = new MosquitoNetCalculator.Converters.OfficeStatusToBadgeBackgroundConverter();
            application.Resources["OfficeStatusBadgeFg"] = new MosquitoNetCalculator.Converters.OfficeStatusToBadgeForegroundConverter();
            application.Resources["OfficeStatusStripeBrush"] = new MosquitoNetCalculator.Converters.OfficeStatusToStripeBrushConverter();
            application.Resources["UnbindModeToVisibility"] = new MosquitoNetCalculator.Converters.UnbindModeToVisibilityConverter();
            application.Resources["InverseBool"] = new MosquitoNetCalculator.Converters.InverseBoolConverter();
            application.Resources["BoolVisibility"] = new MosquitoNetCalculator.Converters.BoolVisibilityConverter();
            application.Resources["InverseBoolVisibility"] = new MosquitoNetCalculator.Converters.InverseBoolVisibilityConverter();
        }

        private static string LocateSourceProject()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "MosquitoNetCalculator");
                if (File.Exists(Path.Combine(candidate, "App.xaml")))
                    return candidate;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate MosquitoNetCalculator source project.");
        }
    }
}
