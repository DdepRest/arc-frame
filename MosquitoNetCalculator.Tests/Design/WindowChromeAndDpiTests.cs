using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Design
{
    /// <summary>
    /// Рендер окон: DPI-манифест и общий стиль окна (v3.53.0).
    ///
    /// <para><b>Почему стиль применяется ЯВНО.</b> Сначала он был неявным
    /// (<c>TargetType="Window"</c> без <c>x:Key</c>) — и не работал: WPF
    /// сопоставляет неявный стиль по точному типу, а все окна приложения —
    /// наследники (<c>MainWindow</c> + 12 вторичных), поэтому замер давал
    /// <c>Style=null</c>, <c>Segoe UI</c> 12px, <c>TextFormattingMode=Ideal</c>,
    /// <c>UseLayoutRounding=false</c>. Проверка «стиль лежит в словаре»
    /// такое не ловит — она зелёная ровно тогда, когда ничего не применяется.
    /// Поэтому здесь два уровня: скан разметки (все окна объявляют
    /// <c>Window.Shared</c>) и рантайм-проверка настоящих окон.</para>
    ///
    /// <para><b>DPI.</b> До v3.53.0 манифеста в проекте не было (процесс
    /// оставался System-DPI-aware), а шрифт и режим сглаживания задавались
    /// только в MainWindow — вторичные окна рисовали текст иначе, особенно на
    /// 125/150%.</para>
    ///
    /// Разбор грабли — GOTCHAS §22.
    /// </summary>
    [Collection("WPF_UI")]
    public class WindowChromeAndDpiTests
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

        private static IEnumerable<string> ProductXaml() =>
            Directory.EnumerateFiles(AppDir, "*.xaml", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                            && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                            && !p.Contains($"{Path.DirectorySeparatorChar}.artifacts{Path.DirectorySeparatorChar}"));

        [Fact]
        public void AppManifest_DeclaresPerMonitorV2_AndIsReferencedByProject()
        {
            string manifestPath = Path.Combine(AppDir, "app.manifest");
            Assert.True(File.Exists(manifestPath), "app.manifest отсутствует — процесс останется System-DPI-aware.");

            string manifest = File.ReadAllText(manifestPath);
            Assert.Contains("PerMonitorV2", manifest);
            Assert.Contains("dpiAware", manifest);
            Assert.Contains("longPathAware", manifest);
            Assert.Contains("{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}", manifest); // Windows 10/11

            string csproj = File.ReadAllText(Path.Combine(AppDir, "MosquitoNetCalculator.csproj"));
            Assert.Contains("<ApplicationManifest>app.manifest</ApplicationManifest>", csproj);
        }

        [Fact]
        public void SharedWindowStyle_IsNamed_AndCarriesTypographyAndRendering()
        {
            string misc = File.ReadAllText(Path.Combine(AppDir, "Themes", "MiscStyles.xaml"));

            int start = misc.IndexOf("<Style x:Key=\"Window.Shared\"", StringComparison.Ordinal);
            Assert.True(start >= 0,
                "Нет именованного стиля Window.Shared. Неявный стиль для окон-наследников не работает (GOTCHAS §22).");

            string style = misc[start..];
            style = style[..style.IndexOf("</Style>", StringComparison.Ordinal)];

            Assert.Contains("TextOptions.TextFormattingMode\" Value=\"Display\"", style);
            Assert.Contains("UseLayoutRounding\" Value=\"True\"", style);
            Assert.Contains("SnapsToDevicePixels\" Value=\"True\"", style);

            // Типографика окна тоже из токенов, а шрифт — через DynamicResource
            // (значение подменяет AppFontService на вшитый Inter).
            Assert.Contains("{DynamicResource Font.Text}", style);
            Assert.Contains("{StaticResource Type.BodyMd}", style);

            // Неявного дубля быть не должно: он либо выбирается вместо явного
            // (и тогда ни одно окно-наследник его не видит), либо выглядит
            // работающим, не являясь им.
            Assert.DoesNotContain("<Style TargetType=\"Window\">", misc);
        }

        [Fact]
        public void EveryWindow_AppliesTheSharedStyle()
        {
            var windows = ProductXaml()
                .Where(p => Regex.IsMatch(File.ReadAllText(p), "<Window\\s+x:Class="))
                .ToList();

            // Мало файлов — значит скан сломался и проверка ничего не стережёт.
            Assert.True(windows.Count >= 13,
                $"Найдено {windows.Count} окон — ожидалось не меньше 13 (MainWindow + 12 вторичных).");

            var missing = windows
                .Where(p => !Regex.IsMatch(
                    File.ReadAllText(p),
                    "Style=\"\\{(?:Dynamic|Static)Resource Window\\.Shared\\}\"",
                    RegexOptions.Singleline))
                .Select(p => Path.GetRelativePath(AppDir, p).Replace('\\', '/'))
                .OrderBy(x => x)
                .ToList();

            Assert.True(missing.Count == 0,
                "Окно не применяет общий стиль — получит Segoe UI 12px, Ideal и без round/снаппинга:\n  " +
                string.Join("\n  ", missing));
        }

        [Fact]
        public void EveryWindow_ActuallyGetsTheSharedStyle()
        {
            // Рантайм-проверка: смотрим на РЕАЛЬНЫЕ окна, а не на файлы. Именно
            // этого не хватало — неявный стиль лежал в словаре и не применялся.
            TestAppThemes.RunOnSta(() =>
            {
                var app = Application.Current;
                double expectedFontSize = (double)app.Resources["Type.BodyMd"];

                var factories = new (string Name, Func<Window> Create)[]
                {
                    ("AdminPasswordWindow", () => new MosquitoNetCalculator.Controls.AdminPasswordWindow()),
                    ("AiAssistantWindow", () => new MosquitoNetCalculator.Controls.AiAssistantWindow()),
                    ("SlopeEconomyDetailsWindow", () => new MosquitoNetCalculator.Controls.SlopeEconomyDetailsWindow()),
                };

                var problems = new List<string>();

                foreach (var (name, create) in factories)
                {
                    Window? window = null;
                    try
                    {
                        window = create();
                        window.ShowActivated = false;
                        window.WindowStartupLocation = WindowStartupLocation.Manual;
                        window.Left = -30000;   // вне экрана: тест не мигает окнами
                        window.Top = -30000;
                        window.Show();

                        if (window.Style == null)
                            problems.Add($"{name}: Style=null — общий стиль не применился");

                        string family = window.FontFamily?.Source ?? "null";
                        if (!family.Contains("Inter", StringComparison.OrdinalIgnoreCase))
                            problems.Add($"{name}: FontFamily={family} (ожидался вшитый Inter)");

                        if (Math.Abs(window.FontSize - expectedFontSize) > 0.01)
                            problems.Add($"{name}: FontSize={window.FontSize} (ожидалось {expectedFontSize} из Type.BodyMd)");

                        var mode = TextOptions.GetTextFormattingMode(window);
                        if (mode != TextFormattingMode.Display)
                            problems.Add($"{name}: TextFormattingMode={mode} (ожидался Display)");

                        if (!window.UseLayoutRounding)
                            problems.Add($"{name}: UseLayoutRounding=false — hairline-границы поедут");

                        if (!window.SnapsToDevicePixels)
                            problems.Add($"{name}: SnapsToDevicePixels=false");
                    }
                    finally
                    {
                        window?.Close();
                    }
                }

                Assert.True(problems.Count == 0,
                    "Окно не получило общий рендер/типографику:\n  " + string.Join("\n  ", problems));
            });
        }

        [Fact]
        public void NoWindow_OverridesTheUnifiedTextRendering()
        {
            var offenders = ProductXaml()
                .SelectMany(path =>
                {
                    string text = Regex.Replace(File.ReadAllText(path), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
                    if (!text.Contains("<Window ")) return Enumerable.Empty<string>();

                    return Regex.Matches(text, "<Window[^>]*TextFormattingMode=\"([^\"]+)\"")
                        .Where(m => m.Groups[1].Value != "Display")
                        .Select(m => $"{Path.GetFileName(path)}: TextFormattingMode={m.Groups[1].Value}");
                })
                .ToList();

            // Окна обязаны пользоваться общим стилем. Исключение возможно только
            // осознанно (и тогда это должно быть Display) — контролы вроде
            // PrintPreviewControl вправе держать Ideal: там важно совпадение
            // разбиения на страницы с печатью.
            Assert.True(offenders.Count == 0,
                "Окно переопределяет режим рендера текста:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void PrintPreview_KeepsIdealModeForPaginationFidelity()
        {
            string preview = File.ReadAllText(Path.Combine(AppDir, "Controls", "PrintPreviewControl.xaml"));

            // Предпросмотр печати меряет текст так же, как печать (фикс v3.49):
            // перевод его на «Display» сломал бы разбиение на страницы.
            Assert.Contains("TextFormattingMode=\"Ideal\"", preview);
        }
    }
}
