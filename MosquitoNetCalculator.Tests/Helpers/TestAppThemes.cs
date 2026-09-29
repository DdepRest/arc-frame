using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            // Процесс-глобальный ресурс — процесс-глобальный замок. xUnit
            // параллелит КЛАССЫ разных коллекций, а приложение в WPF одно на
            // процесс: без замка два потока могли одновременно увидеть
            // «приложения нет», и один из них успевал создать пустое
            // (без тем) приложение. Тогда следующий же тест, искавший
            // ресурс темы (напр. ToastBorder), получал
            // ResourceReferenceKeyNotFoundException — и падал не один тест,
            // а вся группа, его увидевшая. Замок снимает гонку: второй
            // вызов видит готовое приложение и просто переиспользует его.
            lock (Gate)
            {
                EnsureCore();
            }
        }

        private static readonly object Gate = new();

        /// <summary>
        /// Диагностика кросс-поточных падений (v3.53.1): пишем, с какого
        /// потока поднимается приложение и кому принадлежат ключевые
        /// ресурсы. Файл лежит рядом с тест-сборкой; удаляется при
        /// следующем прогоне. После отлова «вора» выводится из эксплуатации.
        /// </summary>
        internal static void Diag(string message)
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "sta-diag.log");
                var tid = Thread.CurrentThread.ManagedThreadId;
                File.AppendAllText(path,
                    $"{DateTime.Now:HH:mm:ss.fff} t{tid} {message}{Environment.NewLine}");
            }
            catch
            {
                // диагностика не должна ронять тесты
            }
        }

        private static void EnsureCore()
        {
            if (HasThemedApplication()) return;

            // Оба слота чистим ВСЕГДА: Current может быть null, когда гейт
            // создания ещё выставлен — иначе следующий new Application() бросит.
            ResetStatics();

            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Diag($"APP-CREATED app=#{application.GetHashCode()} workerStack={Environment.StackTrace.Split(Environment.NewLine).Length}");

            foreach (var name in Dictionaries)
            {
                string path = Path.Combine(SourceDir, "Themes", name);
                application.Resources.MergedDictionaries.Add(LoadWithRetry(
                    () => new ResourceDictionary { Source = new Uri(path, UriKind.Absolute) },
                    DictionaryLoadAttempts, name, path));
            }

            RegisterConverters(application);
            MaterializeResources(application);

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
        ///
        /// <para><b>Плюс два шага, без которых сброс НЕ лечит</b> (бисект на
        /// циклах «new Application → Shutdown» — именно так падали тесты
        /// в 3.53.1):</para>
        /// <list type="number">
        /// <item>Флаг <c>_isShuttingDown</c> — статический, остаётся
        /// <c>true</c> после чужого Shutdown и определяет, во что выльется
        /// следующая загрузка ресурса (см. п. 4);</item>
        /// <item>Пакет ресурсов <c>pack://application:,,,/</c>:
        /// <c>DoShutdown()</c> зовёт <c>PreloadedPackages.Clear()</c>, а
        /// регистрируется пакет ТОЛЬКО в статическом конструкторе
        /// <c>Application</c> — один раз на процесс. После чужого Shutdown
        /// любой <c>LoadComponent</c> (конструктор любого окна) получает
        /// null-пакет из <c>Application.GetResourcePackage</c> и падает:
        /// с флагом завершения — ловимой InvalidOperationException
        /// «Идет завершение работы объекта Application», без него (новый
        /// Application его сбрасывает) — FailFast и СБОЙ ХОСТА.</item>
        /// </list>
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

            // 3. Статический флаг завершения — иначе у GetResourcePackage
            //    ломается ветка инварианта (см. п. 4 в summary).
            appType.GetField("_isShuttingDown", PrivateStatic)?.SetValue(null, false);

            // 4. Ключевое: вернуть пакет ресурсов, унесённый DoShutdown().
            RestorePreloadedResourcePackage();
        }

        /// <summary>
        /// Возвращает пакет <c>pack://application:,,,/</c> в
        /// <c>PreloadedPackages</c>, если Shutdown его унёс (идемпотентно:
        /// при живом пакете — no-op).
        ///
        /// Внутренний API WPF вызывается зеркалом: публичного способа
        /// «перерегистрировать пакет» у WPF нет — регистрация живёт в
        /// статическом конструкторе Application и выполняется ровно один
        /// раз на процесс. Типы ищем по имени среди загруженных сборок
        /// (<c>PreloadedPackages</c> — в PresentationCore, а не там, где
        /// Application), методы — по имени и числу параметров.
        /// </summary>
        private static void RestorePreloadedResourcePackage()
        {
            var preloadedType = FindLoadedType("MS.Internal.IO.Packaging.PreloadedPackages")
                ?? throw new InvalidOperationException(
                    "TestAppThemes: тип MS.Internal.IO.Packaging.PreloadedPackages не найден " +
                    "в загруженных сборках — WPF переименовал его, поправьте " +
                    "TestAppThemes.RestorePreloadedResourcePackage.");

            const System.Reflection.BindingFlags StaticAny =
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            var getPackage = preloadedType.GetMethods(StaticAny)
                .FirstOrDefault(m => m.Name == "GetPackage" && m.GetParameters().Length == 1)
                ?? throw new InvalidOperationException(
                    "TestAppThemes: PreloadedPackages.GetPackage не найден — API WPF изменился.");

            // Тот же способ получить URI, что у WPF в ApplicationInit().
            var packAppBase = new Uri("pack://application:,,,/", UriKind.Absolute);
            var packageUri = System.IO.Packaging.PackUriHelper.GetPackageUri(packAppBase);

            if (getPackage.Invoke(null, new object[] { packageUri }) != null)
                return; // пакет на месте — восстановление не требуется

            var containerType = FindLoadedType("MS.Internal.AppModel.ResourceContainer")
                ?? throw new InvalidOperationException(
                    "TestAppThemes: тип MS.Internal.AppModel.ResourceContainer не найден — " +
                    "WPF переименовал его, поправьте TestAppThemes.RestorePreloadedResourcePackage.");
            var container = Activator.CreateInstance(containerType, nonPublic: true)
                ?? throw new InvalidOperationException(
                    "TestAppThemes: new ResourceContainer() вернул null — API WPF изменился.");

            var addPackage = preloadedType.GetMethods(StaticAny)
                .FirstOrDefault(m => m.Name == "AddPackage" && m.GetParameters().Length == 3)
                ?? throw new InvalidOperationException(
                    "TestAppThemes: PreloadedPackages.AddPackage не найден — API WPF изменился.");
            addPackage.Invoke(null, new object[] { packageUri, container, true });
        }

        private static Type? FindLoadedType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(fullName, throwOnError: false);
                    if (type != null) return type;
                }
                catch
                {
                    // повреждённая/недоступная сборка — пропускаем
                }
            }
            return null;
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

        /// <summary>
        /// Рекурсивно собирает ключи всех словарей приложения и читает каждый
        /// через <see cref="Application.TryFindResource"/> — на воркере.
        ///
        /// <para>ВАЖНО: чтение через индексер словаря НЕ материализует BAML-
        /// deferred записи — индексер возвращает обёртку DeferredResourceReference,
        /// а не Freezable (ловушка об это уже споткнулась). Разворачивает
        /// deferred-значения только поиск уровня Application: он и используется.
        /// Материализованная кисть принадлежит воркеру, и чужие потоки после
        /// этого могут её только ЧИТАТЬ (легально) — гонки владения нет.</para>
        /// </summary>
        private static void MaterializeResources(Application application)
        {
            var keys = new List<object>();
            CollectKeys(application.Resources, keys);

            foreach (var key in keys)
            {
                try { _ = application.TryFindResource(key); }
                catch { /* битый ресурс — как был, тесты скажут сами */ }
            }
        }

        private static void CollectKeys(System.Windows.ResourceDictionary dictionary, List<object> keys)
        {
            foreach (var key in dictionary.Keys)
                keys.Add(key);

            foreach (var merged in dictionary.MergedDictionaries)
                CollectKeys(merged, keys);
        }

        /// <summary>Живое приложение с уже загруженными темами?</summary>
        private static bool HasThemedApplication()
        {
            try
            {
                var current = Application.Current;
                if (current == null) { Diag("PROBE app=null"); return false; }

                // Ловушка «вора»: если владелец ключевых ресурсов — не текущий
                // (рабочий) поток, кто-то материализовал кисть мимо воркера.
                // Бросаем СО СТЕКОМ: xUnit покажет виновника по имени теста.
                int? surfaceOwner = null;
                try
                {
                    surfaceOwner = (current.Resources["Surface"] as System.Windows.Freezable)
                        ?.Dispatcher?.Thread?.ManagedThreadId;
                }
                catch (Exception ex) { Diag($"PROBE-OWNERSHIP-READ-FAILED {ex.GetType().Name}: {ex.Message}"); }

                var currentTid = Thread.CurrentThread.ManagedThreadId;
                Diag($"PROBE app=#{current.GetHashCode()} surfaceOwner=t{surfaceOwner} " +
                     $"probe=t{currentTid} " +
                     $"appDispatcher=t{current.Dispatcher.Thread.ManagedThreadId} " +
                     $"hasShutdown={current.Dispatcher.HasShutdownStarted}");

                if (surfaceOwner.HasValue && surfaceOwner.Value != currentTid)
                {
                    // Журналируем и продолжаем: материализация на воркере выше
                    // делает чужие касания read-only (легальными), а FailFast
                    // здесь убивал прогон изнутри активного теста-жертвы, не
                    // называя настоящего виновника.
                    Diag($"THIEF-DETECTED surfaceOwner=t{surfaceOwner.Value} probe=t{currentTid} " +
                         "stack=" + Environment.StackTrace.Replace(Environment.NewLine, " | "));
                }

                // Глушимое/умершее приложение непригодно: LoadComponent такого
                // экземпляра роняет окно InvalidOperationException
                // «Идет завершение работы объекта Application» (и FailFast-вариант
                // в Application.GetResourcePackage — краш testhost на CI).
                // Лечится только НОВЫМ приложением — считаем ресурсы протухшими.
                var dispatcher = current.Dispatcher;
                if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return false;

                return current.Resources["Surface"] != null
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
