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
