using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MosquitoNetCalculator.Tests.Design
{
    /// <summary>
    /// Рендер окон: DPI-манифест и единый стиль Window (v3.53.0).
    ///
    /// До этой версии манифеста в проекте не было (процесс оставался
    /// System-DPI-aware), а шрифт и режим сглаживания задавались только в
    /// MainWindow — вторичные окна рисовали текст иначе, особенно на 125/150%.
    /// </summary>
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
        public void SharedWindowStyle_SetsTypographyAndTextRendering()
        {
            string misc = File.ReadAllText(Path.Combine(AppDir, "Themes", "MiscStyles.xaml"));

            int start = misc.IndexOf("<Style TargetType=\"Window\">", StringComparison.Ordinal);
            Assert.True(start >= 0, "Нет неявного стиля Window — 12 вторичных окон рисуют текст сами по себе.");

            string style = misc[start..];
            style = style[..style.IndexOf("</Style>", StringComparison.Ordinal)];

            Assert.Contains("TextOptions.TextFormattingMode\" Value=\"Display\"", style);
            Assert.Contains("UseLayoutRounding\" Value=\"True\"", style);
            Assert.Contains("SnapsToDevicePixels\" Value=\"True\"", style);

            // Типографика окна тоже из токенов, а шрифт — через DynamicResource
            // (значение подменяет AppFontService на вшитый Inter).
            Assert.Contains("{DynamicResource Font.Text}", style);
            Assert.Contains("{StaticResource Type.BodyMd}", style);
        }

        [Fact]
        public void NoWindow_OverridesTheUnifiedTextRendering()
        {
            var offenders = Directory.EnumerateFiles(AppDir, "*.xaml", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                            && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                            && !p.Contains($"{Path.DirectorySeparatorChar}.artifacts{Path.DirectorySeparatorChar}"))
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
