using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Применение темы (v3.53.0-fix). Регрессия: gate «отключить анимации»
    /// (Motion.ReducedMotion → animate=false) оставлял кисти в СТАРОМ цвете —
    /// slow-path создавал кисть в старом цвете «для интерполяции», а двигать
    /// её было нечем. На устройствах с выключенными анимациями Windows тема
    /// не применялась вовсе: тёмные пользователи оставались на дефолтной
    /// палитре Brushes.xaml (светлый контент + тёмный сайдбар).
    /// </summary>
    [Collection("WPF_UI")]
    public class ThemeApplyTests
    {
        [Fact]
        public void ApplyTheme_SetsBrushesToTargetColors_WithoutAnimation()
        {
            TestAppThemes.RunOnSta(() =>
            {
                bool wasDark = ThemeService.IsDarkTheme;
                try
                {
                    // Нулевая длительность = тот же путь «без анимации», что и
                    // при системной настройке «отключить анимации»
                    // (animate=false): именно на нём кисти застревали в старом
                    // цвете. Применяем тему, противоположную текущей.
                    if (ThemeService.IsDarkTheme) ThemeService.ToggleTheme();
                    bool targetDark = ThemeService.IsDarkTheme;

                    ThemeService.ApplyTheme(TimeSpan.Zero);

                    var app = Application.Current;
                    var surface = Assert.IsType<SolidColorBrush>(app!.Resources["Surface"]);
                    var sidebar = Assert.IsType<SolidColorBrush>(app.Resources["SidebarBg"]);
                    var expected = Parse(ThemeService.IsDarkTheme
                        ? "#242427" // DarkColors["Surface"]
                        : "#FFFFFF"); // LightColors["Surface"]

                    // ЯДРО БАГА: до фикса кисть оставалась в цвете предыдущей
                    // темы (дефолта Brushes.xaml при первом применении).
                    Assert.Equal(expected, surface.Color);
                    Assert.Equal(targetDark ? Parse("#131315") : Parse("#FFFFFF"), sidebar.Color);

                    // Контроль обратного перехода — обе темы доходят до цели.
                    ThemeService.ToggleTheme();
                    ThemeService.ApplyTheme(TimeSpan.Zero);
                    var surface2 = Assert.IsType<SolidColorBrush>(app.Resources["Surface"]);
                    Assert.Equal(
                        ThemeService.IsDarkTheme ? Parse("#242427") : Parse("#FFFFFF"),
                        surface2.Color);
                }
                finally
                {
                    // Вернуть исходное состояние темы для остальных тестов.
                    if (ThemeService.IsDarkTheme != wasDark)
                    {
                        ThemeService.ToggleTheme();
                        ThemeService.ApplyTheme(TimeSpan.Zero);
                    }
                }
            });
        }

        /// <summary>
        /// Главный страж детерминизма полного прогона: смена темы С НЕ-STA НИТИ
        /// не имеет права забирать владение кистями.
        ///
        /// <para><b>Что было.</b> <c>ManualChecklistTests.Check1_ToggleTheme…</c>
        /// (и любой похожий unit-тест) зовёт ThemeService с нити xUnit.
        /// <c>ApplyTheme</c> читал и пересоздавал <c>Application.Resources["*Color"]*</c>
        /// там же — то есть владельцем всех кистей темы становилась нить
        /// тест-раннера. Дальше ЛЮБОЙ STA-тест на этом приложении падал в
        /// <c>VerifyAccess</c>: падали темы, тосты, AnwisContextMenu, drop в
        /// примечания (StyleHelper.SealIfSealable), а виновник — нет.
        /// Симптом до этого теста: 10–30 «случайных» падений в полном прогоне
        /// и сбой хост-процесса.</para>
        ///
        /// <para><b>Контракт.</b> Оба входа (ToggleTheme и ApplyTheme) с чужой
        /// нити маршалятся на dispatcher приложения; кисть после этого отвечает
        /// <c>CheckAccess() == true</c> на STA-воркере и уже в целевом цвете.</para>
        /// </summary>
        [Fact]
        public void ToggleTheme_FromNonStaThread_AppliesOnTheUiDispatcher()
        {
            // Исходное состояние — гарантированно СВЕТЛАЯ тема, применённая на
            // STA. Без этого шага тест мог «пройти» вхолостую: дефолтная
            // палитра Brushes.xaml совпадает с целевой, и незамеченное
            // отсутствие применения темы давало бы тот же цвет.
            int uiThread = WpfTestHelper.RunOnSta(() =>
            {
                if (ThemeService.IsDarkTheme) ThemeService.ToggleTheme();
                ThemeService.ApplyTheme(TimeSpan.Zero);

                var surface = Assert.IsType<SolidColorBrush>(Application.Current!.Resources["Surface"]);
                Assert.Equal(Parse("#FFFFFF"), surface.Color); // LightColors["Surface"]
                return Thread.CurrentThread.ManagedThreadId;
            });

            int notifyThread = -1;
            void Handler() => Volatile.Write(ref notifyThread, Thread.CurrentThread.ManagedThreadId);

            try
            {
                // Ровно путь виновника: смена темы с нити тест-раннера (не-STA).
                // ManualChecklistTests.Check1_ToggleTheme… идёт именно так.
                ThemeService.ThemeChanged += Handler;
                ThemeService.ToggleTheme();
                ThemeService.ApplyTheme(TimeSpan.Zero);
                ThemeService.ThemeChanged -= Handler;

                // ЯДРО: применение темы и подписчики ThemeChanged отработали на
                // UI-диспетчере. До фикса событие поднималось на нити раннера,
                // и там же материализовались кисти — StyleHelper.SealIfSealable
                // потом падал в VerifyAccess у следующих STA-тестов.
                Assert.Equal(uiThread, Volatile.Read(ref notifyThread));

                // Контроль: тема действительно доехала до кисти.
                TestAppThemes.RunOnSta(() =>
                {
                    var surface = Assert.IsType<SolidColorBrush>(Application.Current!.Resources["Surface"]);
                    Assert.Equal(Parse("#242427"), surface.Color); // DarkColors["Surface"]
                });
            }
            finally
            {
                ThemeService.ThemeChanged -= Handler;

                // Возврат к светлой теме — тоже на STA.
                TestAppThemes.RunOnSta(() =>
                {
                    if (ThemeService.IsDarkTheme) ThemeService.ToggleTheme();
                    ThemeService.ApplyTheme(TimeSpan.Zero);
                });
            }
        }

        private static Color Parse(string hex) =>
            (Color)ColorConverter.ConvertFromString(hex);
    }
}
