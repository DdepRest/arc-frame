using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Design
{
    /// <summary>
    /// Стеклянный хром кнопок и тумблеров (v3.56).
    ///
    /// Регрессия, которую держат эти тесты: «стекло» было набором белых
    /// alpha-градиентов (<c>GlassSurface</c>, <c>GlassBorder</c>, <c>GlassSheen</c>),
    /// а ThemeService переключает только SolidColorBrush. В светлой теме
    /// плашка и рамка вторичных кнопок становились белыми на белом (кнопка
    /// читалась как простой текст), а белый блик поверх заливки опускал
    /// контраст белого текста на Accent #3574C3 с 4.73:1 до ≈3.6:1 — ниже AA.
    ///
    /// Новый контракт: плашка и рамка — ТЕМАТИЧЕСКИЕ токены (GhostBg,
    /// GhostBorder, RowHover, BorderHover), под текстом нет полупрозрачной
    /// накладки, а «стекло» даёт 1px спекулярная кромка рамки (не под глифами).
    /// </summary>
    [Collection("WPF_UI")]
    public class GlassChromeTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "MosquitoNetCalculator", "App.xaml")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Не найден корень репозитория.");
        }

        private static string AppDir => Path.Combine(RepoRoot(), "MosquitoNetCalculator");

        private static string Read(string relativePath) =>
            File.ReadAllText(Path.Combine(AppDir, relativePath));

        /// <summary>XAML без комментариев: в пояснениях цвета и токены упоминаются как примеры.</summary>
        private static string ReadWithoutComments(string relativePath) =>
            Regex.Replace(Read(relativePath), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

        /// <summary>
        /// Токены, которые рисовали полупрозрачную плёнку поверх заливки и не
        /// переключались темой. Возврат любого из них в ButtonStyles — это
        /// возврат бага «белое на белом» / просадки контраста.
        /// </summary>
        private static readonly string[] ThemeBlindGlassTokens =
        {
            "GlassSurface", "GlassSurfaceHover", "GlassSurfacePressed",
            "GlassBorder", "GlassBorderHover",
            "GlassSheen", "GlassSheenHover",
            "GlassTrackBg", "GlassTrackHover",
        };

        [Fact]
        public void ButtonStyles_DoNotUseThemeBlindGlassSurfaces()
        {
            string text = ReadWithoutComments("Themes/ButtonStyles.xaml");

            var offenders = ThemeBlindGlassTokens
                .Where(token => Regex.IsMatch(
                    text, $"\\{{DynamicResource {Regex.Escape(token)}\\}}"))
                .ToList();

            Assert.True(offenders.Count == 0,
                "Кнопки снова используют полупрозрачное «стекло» вне темы: " +
                string.Join(", ", offenders) +
                ". В светлой теме такая плашка/рамка исчезает на белом, а блик поверх " +
                "заливки опускает белый текст на Accent ниже AA (4.5:1). " +
                "Плашка и рамка — GhostBg/GhostBorder/RowHover/BorderHover; " +
                "стекло даёт GlassSpecularBorder на кромке и GlassPressVeil на нажатии.");
        }

        [Fact]
        public void EveryGlassResourceReferencedInXaml_IsDefinedInBrushes()
        {
            string brushes = ReadWithoutComments("Themes/Brushes.xaml");
            var defined = Regex.Matches(brushes, "x:Key=\"(Glass[A-Za-z]+)\"")
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);

            var referenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in Directory.EnumerateFiles(AppDir, "*.xaml", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    continue;

                foreach (Match m in Regex.Matches(
                             ReadWithoutComments(Path.GetRelativePath(AppDir, file)),
                             "\\{DynamicResource (Glass[A-Za-z]+)\\}"))
                    referenced.Add(m.Groups[1].Value);
            }

            var missing = referenced.Where(key => !defined.Contains(key)).OrderBy(k => k).ToList();
            Assert.True(missing.Count == 0,
                "Ссылки на стеклянные токены без определения в Themes/Brushes.xaml: " +
                string.Join(", ", missing));
        }

        /// <summary>
        /// Все action-кнопки — один шаблон. Иначе hover/press/disabled/focus
        /// снова начнут расходиться между вариантами (до v3.56 в файле жило
        /// девять почти одинаковых шаблонов, и фикс нужно было повторять девять раз).
        /// </summary>
        [Fact]
        public void ActionButtons_ShareOneTemplate_AndKeepKeyboardFocusVisual()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var app = Application.Current!;
                var expectedTemplate = app.FindResource("GlassButtonTemplate");

                string[] keys =
                {
                    "PrimaryButton", "SuccessButton", "DangerButton", "CompactAddButton",
                    "GhostButton", "SubtleActionButton", "DangerSubtleButton",
                    "DangerGhostButton", "AccentBorderButton",
                };

                var problems = new List<string>();
                foreach (string key in keys)
                {
                    var style = Assert.IsType<Style>(app.FindResource(key));

                    object? template = FindSetterValue(style, Control.TemplateProperty);
                    if (!ReferenceEquals(template, expectedTemplate))
                        problems.Add($"{key}: свой ControlTemplate вместо GlassButtonTemplate");

                    object? focus = FindSetterValue(style, FrameworkElement.FocusVisualStyleProperty);
                    if (focus == null)
                        problems.Add($"{key}: нет FocusVisualStyle (клавиатурный фокус не виден)");
                }

                Assert.True(problems.Count == 0, string.Join("\n  ", problems));
            });
        }

        [Fact]
        public void RenderGlassButtonsPreviews_SavesVisualArtifacts()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var app = Application.Current!;
                string artifactDir = @"C:\Users\Asus\.gemini\antigravity\brain\83c522c1-af1c-4b6d-94ea-798d6e1a4510";
                if (!Directory.Exists(artifactDir)) return;

                void RenderForTheme(bool isDark, string filename)
                {
                    if (MosquitoNetCalculator.Services.ThemeService.IsDarkTheme != isDark)
                    {
                        MosquitoNetCalculator.Services.ThemeService.ToggleTheme();
                    }
                    MosquitoNetCalculator.Services.ThemeService.ApplyTheme(TimeSpan.Zero);

                    var container = new Border
                    {
                        Background = (System.Windows.Media.Brush)app.FindResource("AppBg"),
                        Padding = new Thickness(28)
                    };

                    var stack = new StackPanel { Orientation = Orientation.Vertical };
                    var title = new TextBlock
                    {
                        Text = isDark ? "Windows 11 Fluent Action Buttons (Dark Theme)" : "Windows 11 Fluent Action Buttons (Light Theme)",
                        Foreground = (System.Windows.Media.Brush)app.FindResource("TextPrimary"),
                        FontSize = 18,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 24)
                    };
                    stack.Children.Add(title);

                    var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 20) };
                    row1.Children.Add(new Button { Style = (Style)app.FindResource("PrimaryButton"), Content = "Print KP", Margin = new Thickness(0, 0, 16, 0) });
                    row1.Children.Add(new Button { Style = (Style)app.FindResource("SuccessButton"), Content = "Save Order", Margin = new Thickness(0, 0, 16, 0) });
                    row1.Children.Add(new Button { Style = (Style)app.FindResource("GhostButton"), Content = "Templates", Margin = new Thickness(0, 0, 16, 0) });
                    row1.Children.Add(new Button { Style = (Style)app.FindResource("SubtleActionButton"), Content = "New Order", Margin = new Thickness(0, 0, 16, 0) });
                    row1.Children.Add(new Button { Style = (Style)app.FindResource("DangerButton"), Content = "Clear All" });
                    stack.Children.Add(row1);

                    var row2 = new StackPanel { Orientation = Orientation.Horizontal };
                    row2.Children.Add(new Button { Style = (Style)app.FindResource("CompactAddButton"), Content = "+ Add Item", Margin = new Thickness(0, 0, 16, 0) });
                    row2.Children.Add(new Button { Style = (Style)app.FindResource("DialogPrimaryButton"), Content = "Save Changes", Margin = new Thickness(0, 0, 16, 0) });
                    row2.Children.Add(new Button { Style = (Style)app.FindResource("DialogOutlineButton"), Content = "Cancel", Margin = new Thickness(0, 0, 16, 0) });
                    row2.Children.Add(new Button { Style = (Style)app.FindResource("DialogCloseButton"), Content = "X", Width = 32, Height = 32 });
                    stack.Children.Add(row2);

                    container.Child = stack;
                    container.Measure(new Size(760, 220));
                    container.Arrange(new Rect(0, 0, 760, 200));
                    container.UpdateLayout();

                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(760, 200, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    rtb.Render(container);

                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                    string outPath = Path.Combine(artifactDir, filename);
                    using var stream = File.Create(outPath);
                    encoder.Save(stream);

                    try
                    {
                        var bar = new MosquitoNetCalculator.Controls.ActionBarControl();
                        bar.CardBarBorder.Opacity = 1;
                        bar.Measure(new Size(1020, 64));
                        bar.Arrange(new Rect(0, 0, 1020, 64));
                        bar.UpdateLayout();

                        var rtbBar = new System.Windows.Media.Imaging.RenderTargetBitmap(1020, 64, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        rtbBar.Render(bar);

                        var encBar = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encBar.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtbBar));
                        string barPath = Path.Combine(artifactDir, isDark ? "rendered_fluent_actionbar_dark.png" : "rendered_fluent_actionbar_light.png");
                        using var barStream = File.Create(barPath);
                        encBar.Save(barStream);
                    }
                    catch { }

                    try
                    {
                        var totalCard = new MosquitoNetCalculator.Controls.TotalCardControl();
                        totalCard.CardTotal.Opacity = 1;
                        totalCard.RunTotalAmount.Text = "7 444,80";
                        totalCard.TxtTotalSub.Text = "1 поз., 1,622 м², доп. КП 4 274,00 руб.";
                        totalCard.TxtAmountInWords.Text = "Семь тысяч четыреста сорок четыре рубля 80 копеек";
                        totalCard.TxtPositionsMeta.Text = "Позиций: 1   ctrl+z — отменить удаление";
                        totalCard.Measure(new Size(1020, 80));
                        totalCard.Arrange(new Rect(0, 0, 1020, 80));
                        totalCard.UpdateLayout();

                        var rtbTotal = new System.Windows.Media.Imaging.RenderTargetBitmap(1020, 80, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        rtbTotal.Render(totalCard);

                        var encTotal = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encTotal.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtbTotal));
                        string totalPath = Path.Combine(artifactDir, isDark ? "rendered_total_card_dark.png" : "rendered_total_card_light.png");
                        using var totalStream = File.Create(totalPath);
                        encTotal.Save(totalStream);
                    }
                    catch { }
                }

                bool originalDark = MosquitoNetCalculator.Services.ThemeService.IsDarkTheme;
                try
                {
                    RenderForTheme(true, "rendered_glass_preview.png");
                    RenderForTheme(false, "rendered_glass_preview_light.png");
                }
                finally
                {
                    if (MosquitoNetCalculator.Services.ThemeService.IsDarkTheme != originalDark)
                    {
                        MosquitoNetCalculator.Services.ThemeService.ToggleTheme();
                        MosquitoNetCalculator.Services.ThemeService.ApplyTheme(TimeSpan.Zero);
                    }
                }
            });
        }

        private static object? FindSetterValue(Style style, DependencyProperty property)
        {
            for (Style? current = style; current != null; current = current.BasedOn)
            {
                foreach (SetterBase setterBase in current.Setters)
                {
                    if (setterBase is Setter setter && setter.Property == property)
                        return setter.Value;
                }
            }

            return null;
        }
    }
}
