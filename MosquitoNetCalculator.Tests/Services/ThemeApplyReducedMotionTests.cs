using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Регрессия v3.53.0 (GOTCHAS §44): при системной настройке «отключить
    /// анимации» Motion.ReducedMotion=true и ApplyTheme идёт по ветке
    /// animate=false — там кисть должна создаваться СРАЗУ в целевом цвете.
    ///
    /// Переключить ClientAreaAnimation тестом нельзя (SPI_SETCLIENTAREAANIMATION
    /// на некоторых машинах игнорируется темой Windows), поэтому тест
    /// воспроизводит ТОЧНУЮ семантику ветки animate=false: цвет кисти не
    /// двигается никакой анимацией — как на машинах с выключенными анимациями.
    /// Живая проверка того же пути — .tools/check-theme-reducedmotion.ps1.
    /// </summary>
    [Collection("STA")]
    public class ThemeApplyReducedMotionTests
    {
        [Fact]
        public void ApplyTheme_NoAnimationBranch_ReachesTargetColor()
        {
            TestAppThemes.RunOnSta(() =>
            {
                bool wasDark = ThemeService.IsDarkTheme;
                try
                {
                    if (ThemeService.IsDarkTheme) ThemeService.ToggleTheme();

                    // Ветка animate=false семантически = «кисть заморожена для
                    // анимаций»: BeginAnimation не должен менять её цвет.
                    // Проверяем ИНАРИАНТ фикса: ApplyTheme(Zero) вернул кисть в
                    // целевом цвете, и никакая последующая анимация не нужна.
                    ThemeService.ApplyTheme(TimeSpan.Zero);
                    var surface = Assert.IsType<SolidColorBrush>(Application.Current!.Resources["Surface"]);
                    Assert.Equal(Parse("#FFFFFF"), surface.Color); // LightColors["Surface"]

                    // Контроль в обратную сторону (в тёмную).
                    ThemeService.ToggleTheme();
                    ThemeService.ApplyTheme(TimeSpan.Zero);
                    surface = Assert.IsType<SolidColorBrush>(Application.Current.Resources["Surface"]);
                    Assert.Equal(Parse("#1E2025"), surface.Color); // DarkColors["Surface"]
                }
                finally
                {
                    if (ThemeService.IsDarkTheme != wasDark)
                    {
                        ThemeService.ToggleTheme();
                        ThemeService.ApplyTheme(TimeSpan.Zero);
                    }
                }
            });
        }

        /// <summary>
        /// Инвариант ветки «без анимации»: кисть БЕЗ запущенной анимации
        /// остаётся в том цвете, в котором создана. Именно этот факт делает
        /// корректным создание кисти сразу в целевом цвете (фикс) и делает
        /// ошибкой создание в старом (баг v3.53.0): в slow-path старое
        /// значение использовалось только как стартовая точка BeginAnimation —
        /// без анимации стартовая точка становится вечным итогом.
        /// </summary>
        [Fact]
        public void FreshBrush_WithoutAnimation_StaysAtConstructedColor()
        {
            TestAppThemes.RunOnSta(() =>
            {
                // Кисть без анимации: цвет = конструируемый, навсегда.
                var fresh = new SolidColorBrush(Colors.Red);
                Assert.Equal(Colors.Red, fresh.Color);

                // Контраст: кисть С анимацией уходит от стартового цвета —
                // поэтому в анимированной ветке старт «в старом цвете» был
                // корректен, а в не-анимированной стал багом.
                var animated = new SolidColorBrush(Colors.Red);
                animated.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
                {
                    To = Colors.Blue,
                    Duration = TimeSpan.FromSeconds(10)
                });
                // Точный кадр анимации не проверяем — он недетерминирован
                // (зависит от tick композиционного движка): важен сам факт,
                // что цвет кисти теперь движим анимацией, а не зафиксирован.
                Assert.True(animated.HasAnimatedProperties, "анимация не привязалась к кисти");
            });
        }

        private static Color Parse(string hex) =>
            (Color)ColorConverter.ConvertFromString(hex);
    }
}
