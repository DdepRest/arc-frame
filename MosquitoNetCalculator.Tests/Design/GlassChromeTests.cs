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

        /// <summary>
        /// Кнопки со скруглением не должны выставлять SnapsToDevicePixels="True":
        /// привязка к пиксельной сетке применяется только к прямым сегментам рамки,
        /// а дуги Безье не привязываются, вызывая затемненные пиксельные заломы на стыках.
        /// </summary>
        [Fact]
        public void Buttons_GlassTemplate_HasNoSnapsToDevicePixelsOnRoundedBorder()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var app = Application.Current!;
                var template = (ControlTemplate)app.FindResource("GlassButtonTemplate");
                var btn = new Button { Template = template };
                btn.ApplyTemplate();
                var border = (Border)btn.Template.FindName("Bd", btn);
                Assert.NotNull(border);
                Assert.False(border.SnapsToDevicePixels, "GlassButtonTemplate Bd не должен иметь SnapsToDevicePixels=True");
            });
        }

        [Fact]
        public void ActionBarControl_SplitButtons_HaveNoSnapsToDevicePixels()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var bar = new MosquitoNetCalculator.Controls.ActionBarControl();
                var mainStyle = (Style)bar.Resources["PrintMainButton"];
                var caretStyle = (Style)bar.Resources["PrintCaretButton"];

                var btnMain = new Button { Style = mainStyle };
                btnMain.ApplyTemplate();
                var bdMain = (Border)btnMain.Template.FindName("Bd", btnMain);
                Assert.NotNull(bdMain);
                Assert.False(bdMain.SnapsToDevicePixels, "PrintMainButton Bd не должен иметь SnapsToDevicePixels=True");

                var btnCaret = new Button { Style = caretStyle };
                btnCaret.ApplyTemplate();
                var bdCaret = (Border)btnCaret.Template.FindName("Bd", btnCaret);
                Assert.NotNull(bdCaret);
                Assert.False(bdCaret.SnapsToDevicePixels, "PrintCaretButton Bd не должен иметь SnapsToDevicePixels=True");
            });
        }

        [Fact]
        public void CheckBox_DefaultStyle_IsFluentToggleSwitch()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var app = Application.Current!;
                var style = (Style)app.FindResource(typeof(CheckBox));
                var cb = new CheckBox { Style = style };
                cb.ApplyTemplate();
                var track = (Border)cb.Template.FindName("SwitchTrack", cb);
                Assert.NotNull(track);
                Assert.Equal(40, track.Width);
                Assert.Equal(20, track.Height);
                Assert.False(track.SnapsToDevicePixels);

                var thumb = (Border)cb.Template.FindName("SwitchThumb", cb);
                Assert.NotNull(thumb);
                Assert.Equal(14, thumb.Width);
                Assert.Equal(14, thumb.Height);
                Assert.False(thumb.SnapsToDevicePixels);
            });
        }

        [Fact]
        public void ToggleSwitch_FluentStyle_IsCapsuleWithoutGlassSheen()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var app = Application.Current!;
                var style = (Style)app.FindResource("FluentToggleSwitch");
                var cb = new CheckBox { Style = style };
                cb.ApplyTemplate();
                var track = (Border)cb.Template.FindName("SwitchTrack", cb);
                Assert.NotNull(track);
                Assert.Equal(40, track.Width);
                Assert.Equal(20, track.Height);
                Assert.False(track.SnapsToDevicePixels);

                var thumb = (Border)cb.Template.FindName("SwitchThumb", cb);
                Assert.NotNull(thumb);
                Assert.Equal(14, thumb.Width);
                Assert.Equal(14, thumb.Height);
                Assert.False(thumb.SnapsToDevicePixels);

                var sheen = cb.Template.FindName("TrackSheen", cb);
                Assert.Null(sheen);
            });
        }

        [Fact]
        public void OrderItems_RowCheckBoxes_And_AdditionalKps_UseToggleSwitch()
        {
            string orderItemsXaml = ReadWithoutComments("Controls/OrderItemsControl.xaml");
            // По запросу пользователя: везде тумблеры (Fluent toggle switch), SquareCheckBox снят
            Assert.DoesNotContain("SquareCheckBox", orderItemsXaml);
            Assert.Contains("IsActive", orderItemsXaml);

            string additionalKpsXaml = ReadWithoutComments("Controls/AdditionalKpsControl.xaml");
            Assert.Contains("Style=\"{StaticResource FluentToggleSwitch}\"", additionalKpsXaml);
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
                    var row3 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
                    row3.Children.Add(new CheckBox { Content = "Тумблер выкл", IsChecked = false, Margin = new Thickness(0, 0, 16, 0) });
                    row3.Children.Add(new CheckBox { Content = "Тумблер вкл", IsChecked = true, Margin = new Thickness(0, 0, 24, 0) });
                    row3.Children.Add(new CheckBox { Style = (Style)app.FindResource("SignToggleCheckBox"), IsChecked = false, Margin = new Thickness(0, 0, 16, 0) });
                    row3.Children.Add(new CheckBox { Style = (Style)app.FindResource("SignToggleCheckBox"), IsChecked = true });
                    stack.Children.Add(row3);


                    container.Child = stack;
                    container.Measure(new Size(760, 260));
                    container.Arrange(new Rect(0, 0, 760, 240));
                    container.UpdateLayout();

                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(760, 240, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
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

                    try
                    {
                        var tmplWin = new MosquitoNetCalculator.Controls.TemplatesWindow(null!);
                        var openMethod = typeof(MosquitoNetCalculator.Controls.TemplatesWindow)
                            .GetMethod("OpenChecklist", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                        var windowTemplate = MosquitoNetCalculator.Services.OrderTemplateService.All.First(t => t.Id == "window");
                        openMethod.Invoke(tmplWin, new object[] { windowTemplate });
                        tmplWin.Measure(new Size(640, 720));
                        tmplWin.Arrange(new Rect(0, 0, 640, 720));
                        tmplWin.UpdateLayout();

                        tmplWin.RootBorder.Opacity = 1;
                        tmplWin.RootBorder.Measure(new Size(640, 800));
                        tmplWin.RootBorder.Arrange(new Rect(0, 0, 640, 800));
                        tmplWin.RootBorder.UpdateLayout();
                        var rtbTmpl = new System.Windows.Media.Imaging.RenderTargetBitmap(640, 800, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        rtbTmpl.Render(tmplWin.RootBorder);

                        var encTmpl = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encTmpl.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtbTmpl));
                        string tmplPath = Path.Combine(artifactDir, isDark ? "rendered_templates_checklist_dark.png" : "rendered_templates_checklist_light.png");
                        using var tmplStream = File.Create(tmplPath);
                        encTmpl.Save(tmplStream);
                        tmplWin.Close();
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
