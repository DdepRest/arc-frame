using System;
using System.IO;
using System.Windows;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Helpers
{
    /// <summary>
    /// Регрессии самовосстановления тестового bootstrap'а приложения.
    ///
    /// До v3.52.0 bootstrap был скопирован в каждый STA-класс и в каждом тесте
    /// делал «чистим статику, только если <c>Application.Current != null</c>».
    /// Если слоты WPF расходились (гейт создания выставлен, инстанса нет), все
    /// тесты коллекции <c>WPF_UI</c> падали на <c>new Application()</c> с
    /// сообщением «более одного Application» — один сбой превращался в 45.
    /// </summary>
    [Collection("WPF_UI")]
    public class TestAppThemesTests
    {
        [Fact]
        public void Ensure_HealsPoisonedWpfStatics_WherePlainGuardWouldThrow()
        {
            TestAppThemes.RunOnSta(() =>
            {
                // Состояние из реальной жизни (разобрано в AppLifecycleTests):
                // гейт «приложение создано» выставлен, а Application.Current уже null.
                TestAppThemes.PoisonStaticStateForTest();

                // Именно это убивало всю коллекцию: приложение создать нельзя,
                // а старая проверка «Current != null» очистку пропускала.
                Assert.Throws<InvalidOperationException>(() => { _ = new Application(); });

                TestAppThemes.Ensure();

                Assert.NotNull(Application.Current);
                Assert.NotNull(Application.Current!.Resources["GhostButton"]);
                Assert.NotNull(Application.Current!.Resources["Surface"]);
            });
        }

        /// <summary>
        /// Главный страж стабильности тест-хоста (краши в 3.53.1).
        ///
        /// <para>Цикл «new Application → Shutdown» (его делает
        /// <c>AppLifecycleTests</c>) навсегда уносит пакет ресурсов
        /// <c>pack://application:,,,/</c>: <c>DoShutdown()</c> зовёт
        /// <c>PreloadedPackages.Clear()</c>, а регистрируется пакет только в
        /// статическом конструкторе <c>Application</c> — один раз на процесс.
        /// После этого конструктор любого окна в том же процессе падает:
        /// сначала ловимой InvalidOperationException
        /// «Идет завершение работы объекта Application», а если при этом уже
        /// создано новое Application (оно сбрасывает флаг
        /// <c>_isShuttingDown</c>) — FailFast из
        /// <c>Application.GetResourcePackage</c>, который убивает testhost
        /// целиком (на CI — «Сбой хост-процесса теста» на середине прогона,
        /// Failed: 0, все попытки).</para>
        ///
        /// <para>Лечение — <see cref="TestAppThemes.ResetStatics"/>:
        /// сбрасывает оба слота + <c>_isShuttingDown</c> и восстанавливает
        /// пакет зеркалом внутреннего API WPF. Проверка здесь живьём: после
        /// цикла окно обязано строиться.</para>
        ///
        /// <para>ВАЖНО: падает этот тест не «красным», а СБОЕМ ХОСТА — так
        /// и проявлялся оригинал; лечится только RestorePreloadedResourcePackage.</para>
        /// </summary>
        [Fact]
        public void ShutdownCycle_ResetStatics_RestoresResourcePackage_SoNextWindowBuilds()
        {
            TestAppThemes.RunOnSta(() =>
            {
                // 1. Рабочее состояние: окно строится.
                AssertWindowBuilds("до цикла Shutdown");

                // 2. Цикл «new Application → Shutdown» — как в AppLifecycleTests.
                //    Прокрутка диспетчера обязательна: именно в ShutdownCallback
                //    выполняется DoShutdown() → PreloadedPackages.Clear().
                TestAppThemes.ResetStatics();
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Shutdown();
                PumpDispatcher(TimeSpan.FromMilliseconds(600));

                // 3. Сброс (восстанавливает пакет) и обычный бутстрап.
                TestAppThemes.ResetStatics();
                TestAppThemes.Ensure();
                Assert.NotNull(Application.Current);

                // 4. Строящееся окно — тот самый симптом, что без п. 2–3
                //    ронял хост FailFast'ом.
                AssertWindowBuilds("после цикла Shutdown");
            }, 60_000);
        }

        private static void AssertWindowBuilds(string stage)
        {
            var buttons = new System.Collections.Generic.List<global::MosquitoNetCalculator.Services.DialogButton<object>>
            {
                new("OK", true, false, false, "PrimaryButton")
            };
            try
            {
                var window = new global::MosquitoNetCalculator.Controls.MessageDialogWindow("Тест", "Тест", buttons);
                window.Close();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Окно не построилось ({stage}) — пакет ресурсов приложения " +
                    $"не восстановлен: {ex.GetType().Name}: {ex.Message}", ex);
            }
        }

        private static void PumpDispatcher(TimeSpan timeout)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = timeout };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void LoadWithRetry_SurvivesTransientReadFailures(int failingAttempts)
        {
            int attempts = 0;

            string result = TestAppThemes.LoadWithRetry(() =>
            {
                attempts++;
                if (attempts <= failingAttempts)
                    throw new IOException("разовый сбой чтения (антивирус/индексатор)");
                return "Brushes.xaml";
            }, attempts: 3, what: "Brushes.xaml", path: @"C:\src\Themes\Brushes.xaml");

            Assert.Equal("Brushes.xaml", result);
            Assert.Equal(failingAttempts + 1, attempts);
        }

        [Fact]
        public void LoadWithRetry_ExhaustedAttempts_NamesDictionaryPathAndCause()
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                TestAppThemes.LoadWithRetry<string>(
                    () => throw new IOException("файл занят сканером"),
                    attempts: 3,
                    what: "Brushes.xaml",
                    path: @"C:\src\Themes\Brushes.xaml"));

            // Диагностика обязана называть словарь, путь и исходную причину —
            // иначе сбой выглядит как голый стек загрузчика XAML.
            Assert.Contains("Brushes.xaml", error.Message);
            Assert.Contains(@"C:\src\Themes\Brushes.xaml", error.Message);
            Assert.Contains("файл занят сканером", error.Message);
            Assert.IsType<IOException>(error.InnerException);
        }
    }
}
