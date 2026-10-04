using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// V1 (ввод токена через UI): вкладка «Хранилище» админ-панели сохраняет
    /// токен в settings.json этого ПК через
    /// <see cref="AppSettingsService.SaveOfficeReportToken"/> — сотрудник офиса
    /// вводит его без правки settings.json руками.
    ///
    /// Изоляция файлов — паттерн ManualChecklistTests (коллекция FileSystem,
    /// SettingsPath → temp, восстановление в Dispose). STA-темы — общий
    /// TestAppThemes.RunOnSta.
    ///
    /// Осторожно с IsConfigured: GistToken = настройки ИЛИ токен, встроенный в
    /// сборку (csproj GenerateOfficeReportToken: env/.office-report-token), а
    /// это зависит от окружения сборки. Поэтому статусные панели проверяются
    /// ИНВАРИАНТОМ «== IsConfigured», а не абсолютным «не настроен» — абсолютные
    /// проверки ломаются на машине владельца (встроенный токен всегда есть).
    /// </summary>
    [Collection("FileSystem")]
    public class AdminPanelTokenTabTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _originalSettingsPath;

        public AdminPanelTokenTabTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "mosquito_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            _originalSettingsPath = AppSettingsService.SettingsPath;
            AppSettingsService.SettingsPath = Path.Combine(_tempDir, "settings.json");
        }

        public void Dispose()
        {
            AppSettingsService.SettingsPath = _originalSettingsPath;
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        /// <summary>
        /// Открывает панель, выбирает третью вкладку («Хранилище») и прогоняет
        /// диспетчер: контент вкладки попадает в визуальное дерево только после
        /// выбора, без FlushUi Find его не видит.
        /// </summary>
        private static AdminPanelControl OpenTokenTab(out DependencyObject root)
        {
            TestAppThemes.Ensure();
            var panel = new AdminPanelControl();
            panel.Measure(new Size(900, 800));
            panel.Arrange(new Rect(0, 0, 900, 800));
            panel.UpdateLayout();

            var tabs = Find<TabControl>(panel, "PanelTabs");
            Assert.NotNull(tabs);
            tabs!.SelectedIndex = 2; // Обновления(0), Статистика(1), Хранилище(2)
            FlushUi(panel);

            root = panel;
            return panel;
        }

        private static void FlushUi(AdminPanelControl panel)
        {
            panel.Measure(new Size(900, 800));
            panel.Arrange(new Rect(0, 0, 900, 800));
            for (int i = 0; i < 2; i++)
            {
                // ContextIdle ниже DataBind/Render/Loaded — ждём ВСЕ отложенные
                // операции, как в AdminPanelPlaytestTests.FlushUi.
                panel.Dispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.ContextIdle,
                    new Action(() => { }));
                panel.UpdateLayout();
            }
        }

        private static T? Find<T>(DependencyObject root, string name) where T : class
        {
            if (root is FrameworkElement fe && fe.Name == name) return fe as T;
            int count = VisualTreeHelperGetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelperGetChild(root, i);
                var found = Find<T>(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static int VisualTreeHelperGetChildrenCount(DependencyObject node) =>
            System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);

        private static DependencyObject VisualTreeHelperGetChild(DependencyObject node, int index) =>
            System.Windows.Media.VisualTreeHelper.GetChild(node, index);

        /// <summary>Инвариант статусных панелей: видимость следует за IsConfigured.</summary>
        private static void AssertStatusMatchesIsConfigured(DependencyObject root)
        {
            bool configured = OfficeReportService.IsConfigured;

            var ok = Find<StackPanel>(root, "TokenStatusOk");
            var missing = Find<StackPanel>(root, "TokenStatusMissing");
            Assert.NotNull(ok);
            Assert.NotNull(missing);

            Assert.Equal(configured ? Visibility.Visible : Visibility.Collapsed, ok!.Visibility);
            Assert.Equal(configured ? Visibility.Collapsed : Visibility.Visible, missing!.Visibility);
        }

        [Fact]
        public void TokenTab_Save_WritesTokenToSettings_AndClearsField()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var panel = OpenTokenTab(out var root);

                var box = Find<PasswordBox>(root, "TxtToken");
                Assert.NotNull(box);
                // Пробелы по краям обязаны отрезаться — в settings попадает «чистый» токен.
                box!.Password = "  github_pat_ui_test_123  ";

                var save = Find<Button>(root, "BtnTokenSave");
                Assert.NotNull(save);
                save!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Assert.Equal("github_pat_ui_test_123", AppSettingsService.LoadOfficeReportToken());
                // Поле не хранит токен после сохранения (не светит в визуальном дереве).
                Assert.Equal(string.Empty, box.Password);

                // Токен есть → IsConfigured обязан быть true → баннер скрыт.
                Assert.True(OfficeReportService.IsConfigured);
                var banner = Find<Border>(root, "BannerNotConfigured");
                Assert.NotNull(banner);
                Assert.Equal(Visibility.Collapsed, banner!.Visibility);

                AssertStatusMatchesIsConfigured(root);
            });
        }

        [Fact]
        public void TokenTab_EmptySave_DoesNotWipeExistingToken()
        {
            TestAppThemes.RunOnSta(() =>
            {
                // Риск из карточки V1: пустое поле + «Сохранить» не должно молча
                // стирать работающий токен — очистка только кнопкой «Очистить».
                AppSettingsService.SaveOfficeReportToken("keep-me-token");

                var panel = OpenTokenTab(out var root);

                var box = Find<PasswordBox>(root, "TxtToken");
                Assert.NotNull(box);
                box!.Password = string.Empty;

                var save = Find<Button>(root, "BtnTokenSave");
                Assert.NotNull(save);
                save!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Assert.Equal("keep-me-token", AppSettingsService.LoadOfficeReportToken());

                var status = Find<TextBlock>(root, "TxtTokenCheckStatus");
                Assert.NotNull(status);
                Assert.Contains("пустое", status!.Text);
            });
        }

        [Fact]
        public void TokenTab_Clear_RemovesTokenFromSettings()
        {
            TestAppThemes.RunOnSta(() =>
            {
                AppSettingsService.SaveOfficeReportToken("to-be-cleared");

                var panel = OpenTokenTab(out var root);

                var clear = Find<Button>(root, "BtnTokenClear");
                Assert.NotNull(clear);
                clear!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Assert.Equal(string.Empty, AppSettingsService.LoadOfficeReportToken());
                AssertStatusMatchesIsConfigured(root);

                // Баннер «не настроено» отражает фактический IsConfigured
                // (на dev-сборке встроенный токен остаётся → баннер скрыт).
                var banner = Find<Border>(root, "BannerNotConfigured");
                Assert.NotNull(banner);
                Assert.Equal(
                    OfficeReportService.IsConfigured ? Visibility.Collapsed : Visibility.Visible,
                    banner!.Visibility);
            });
        }

        [Fact]
        public void TokenTab_NotConfiguredBanner_PointsAtTokenTab()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var panel = OpenTokenTab(out var root);

                // Текст баннера после V1 указывает на UI-ввод, а не на вшивание в сборку.
                var banner = Find<TextBlock>(root, "TxtBanner");
                Assert.NotNull(banner);
                Assert.Contains("Хранилище", banner!.Text);

                // Поле и подсказка о получении токена присутствуют на вкладке.
                Assert.NotNull(Find<PasswordBox>(root, "TxtToken"));
                Assert.NotNull(Find<TextBlock>(root, "TxtTokenCheckStatus"));
            });
        }

        [Fact]
        public void TokenTab_SaveThenStatus_IsConsistentWithIsConfigured()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var panel = OpenTokenTab(out var root);
                AssertStatusMatchesIsConfigured(root);

                var box = Find<PasswordBox>(root, "TxtToken");
                box!.Password = "another-token";
                Find<Button>(root, "BtnTokenSave")!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Assert.Equal("another-token", AppSettingsService.LoadOfficeReportToken());
                AssertStatusMatchesIsConfigured(root);
            });
        }

        /// <summary>
        /// Единый владелец состояния «токен настроен»:
        /// <see cref="AdminPanelControl.UpdateTokenStatus"/> применяет И панели
        /// статуса, И баннер BannerNotConfigured — раньше тернарник «показать/
        /// скрыть баннер» был продублирован в RefreshAsync и обоих обработчиках,
        /// а ветка «рефреш сначала скрывает баннер» не выполнялась ни одним тестом
        /// (RefreshAsync без DI-шва при настроенном токене уходит в живую сеть).
        ///
        /// Переходы configured → unconfigured → configured. Старый баг (баннер
        /// показан и «висит» до перезапуска) ловится принудительным ложным
        /// состоянием перед вызовом: метод обязан починить видимость в обе стороны.
        /// </summary>
        [Fact]
        public void UpdateTokenStatus_AppliesStatusAndBanner_AcrossTransitions()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var panel = OpenTokenTab(out var root);
                var banner = Find<Border>(root, "BannerNotConfigured");
                Assert.NotNull(banner);

                // 1) configured: токен сохранён → баннер обязан скрыться, даже
                //    если «залип» показанным (старый баг — не скрывался вообще).
                AppSettingsService.SaveOfficeReportToken("transition-token-a");
                Assert.True(OfficeReportService.IsConfigured);
                banner!.Visibility = Visibility.Visible;
                panel.UpdateTokenStatus();
                Assert.Equal(Visibility.Collapsed, banner.Visibility);
                AssertStatusMatchesIsConfigured(root);

                // 2) unconfigured: токен из настроек убран. На вшитой сборке
                //    (env/.office-report-token) IsConfigured остаётся true —
                //    состояние недостижимо без DI-шва, инвариант остаётся
                //    главным; на безтокенной сборке (CI) баннер обязан показаться.
                AppSettingsService.SaveOfficeReportToken(null);
                panel.UpdateTokenStatus();
                AssertStatusMatchesIsConfigured(root);
                if (!OfficeReportService.IsConfigured)
                    Assert.Equal(Visibility.Visible, banner.Visibility);

                // 3) configured снова: повторное сохранение снова скрывает баннер,
                //    опять из ложного «показан».
                AppSettingsService.SaveOfficeReportToken("transition-token-b");
                banner.Visibility = Visibility.Visible;
                panel.UpdateTokenStatus();
                Assert.Equal(Visibility.Collapsed, banner.Visibility);
                AssertStatusMatchesIsConfigured(root);
            });
        }

        /// <summary>
        /// Реальная ветка RefreshAsync «каждый рефреш заново применяет состояние
        /// баннера». Выполняется только когда токена нет: этот путь проходит БЕЗ
        /// сети (все сетевые шаги закрыты ранним return по IsConfigured) и
        /// синхронно — до первого await дело не доходит. На вшитой сборке
        /// unconfigured недостижим, а настроенная панель без DI-шва уходит в
        /// живую сеть (пункт аудита, вне этой правки) — рефреш не вызывается.
        /// </summary>
        [Fact]
        public void RefreshAsync_WithoutToken_ReappliesBanner_WithoutNetwork()
        {
            TestAppThemes.RunOnSta(() =>
            {
                if (OfficeReportService.IsConfigured)
                    return; // вшитый токен: ветка уходит в живую сеть без DI-шва

                var panel = OpenTokenTab(out var root);
                var banner = Find<Border>(root, "BannerNotConfigured");
                Assert.NotNull(banner);

                // Симулируем «баннер скрыт» до рефреша — рефреш обязан применить
                // состояние по факту: токена нет → показать.
                banner!.Visibility = Visibility.Collapsed;
                var refresh = panel.RefreshAsync(quiet: true);
                Assert.True(refresh.IsCompleted,
                    "ветка без токена обязана проходить синхронно, без сети и ожидания");
                refresh.GetAwaiter().GetResult();

                Assert.Equal(Visibility.Visible, banner.Visibility);
                AssertStatusMatchesIsConfigured(root);
            });
        }
    }
}
