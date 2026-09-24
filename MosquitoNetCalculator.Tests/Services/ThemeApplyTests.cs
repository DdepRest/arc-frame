using System;
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
    [Collection("STA")]
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
                        ? "#1E2025" // DarkColors["Surface"]
                        : "#FFFFFF"); // LightColors["Surface"]

                    // ЯДРО БАГА: до фикса кисть оставалась в цвете предыдущей
                    // темы (дефолта Brushes.xaml при первом применении).
                    Assert.Equal(expected, surface.Color);
                    Assert.Equal(targetDark ? Parse("#17181C") : Parse("#FFFFFF"), sidebar.Color);

                    // Контроль обратного перехода — обе темы доходят до цели.
                    ThemeService.ToggleTheme();
                    ThemeService.ApplyTheme(TimeSpan.Zero);
                    var surface2 = Assert.IsType<SolidColorBrush>(app.Resources["Surface"]);
                    Assert.Equal(
                        ThemeService.IsDarkTheme ? Parse("#1E2025") : Parse("#FFFFFF"),
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

        private static Color Parse(string hex) =>
            (Color)ColorConverter.ConvertFromString(hex);
    }
}
