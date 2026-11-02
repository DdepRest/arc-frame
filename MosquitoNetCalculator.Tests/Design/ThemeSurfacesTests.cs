using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Design
{
    /// <summary>
    /// КОНТРАКТ ТЁМНЫХ УГЛУБЛЕНИЙ. ContrastTests проверяет «текст на фоне» и был
    /// зелёным, пока вложенные панели были ТЕМНЕЕ карточки (1.05:1), границы —
    /// 1.33:1, разделители таблицы — 1.10:1. Теней в тёмной теме нет, поэтому
    /// высоту читают кромка и разделители, а вложенные панели — углубления (темнее карточки).
    /// Accent — заливка под белым текстом; для текста есть AccentText.
    /// </summary>
    public class ThemeSurfacesTests
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
        private static Dictionary<string, string> Dark() => ThemeService.DarkColorsForTests();
        private static Dictionary<string, string> Light() => ThemeService.LightColorsForTests();

        private static double Luminance(string hex)
        {
            string h = hex.TrimStart('#');
            if (h.Length == 8) h = h.Substring(2);
            double Channel(int o)
            {
                double v = Convert.ToInt32(h.Substring(o, 2), 16) / 255.0;
                return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(0) + 0.7152 * Channel(2) + 0.0722 * Channel(4);
        }

        private static double Ratio(string a, string b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        private static void AtLeast(Dictionary<string, string> t, string fg, string bg, double min, string why)
        {
            double r = Ratio(t[fg], t[bg]);
            Assert.True(r >= min, $"{fg}/{bg} = {r:F2}:1, нужно ≥{min:F1}:1 — {why}");
        }

        [Fact]
        public void Dark_Panels_AreRecessed_AndReadable()
        {
            var t = Dark();
            AtLeast(t, "Surface", "AppBg", 1.10, "карточка отделяется от фона страницы");
            foreach (var panel in new[] { "QuickBg", "SectionBg", "HeaderBg" })
            {
                Assert.True(Luminance(t[panel]) < Luminance(t["Surface"]),
                    $"{panel} светлее Surface — большая панель станет серой плитой");
                Assert.True(Ratio(t[panel], t["Surface"]) >= 1.03,
                    $"{panel}/Surface = {Ratio(t[panel], t["Surface"]):F2}:1 — углубление не видно");
            }
            AtLeast(t, "ChipBg", "Surface", 1.15, "чип отличается от карточки");
            AtLeast(t, "RowAlt", "Surface", 1.03, "чередование строк читается");
        }

        [Fact]
        public void Dark_StructuralLines_AreReadable()
        {
            var t = Dark();
            AtLeast(t, "Border", "Surface", 1.55, "рамка карточки/поля ввода видна");
            AtLeast(t, "Border", "QuickBg", 1.55, "рамка поля внутри вложенной панели");
            AtLeast(t, "GhostBorder", "GhostBg", 1.55, "кнопка-призрак держится рамкой");
            AtLeast(t, "GridLine", "Surface", 1.30, "разделитель таблицы не растворяется");
            AtLeast(t, "GridLine", "RowAlt", 1.35, "разделитель на чередующейся строке");
            AtLeast(t, "ScrollBarThumb", "Surface", 1.95, "ползунок скролла находится взглядом");
            Assert.True(Luminance(t["BorderHover"]) > Luminance(t["Border"]),
                "BorderHover должен быть светлее Border");
        }

        [Fact]
        public void Dark_SelectedRow_StandsOut()
        {
            AtLeast(Dark(), "RowAltSelected", "Surface", 1.30, "выбранная строка таблицы видна");
        }

        [Fact]
        public void AccentText_IsReadable_InBothThemes()
        {
            foreach (var (name, t) in new[] { ("light", Light()), ("dark", Dark()) })
            {
                AtLeast(t, "AccentText", "Surface", 4.5, $"{name}: акцентный текст на панели");
                // AccentLight/AppBg в светлой теме дают 4.23 и 4.35 (AccentText == Accent,
                // вид заморожен пиксель-в-пиксель) — проверяем плашку только в тёмной.
                if (name == "dark")
                    AtLeast(t, "AccentText", "AccentLight", 4.5, "dark: акцентный текст на плашке");
                AtLeast(t, "OnAccent", "Accent", 4.5, $"{name}: белый текст на заливке");
            }
        }

        [Fact]
        public void Light_AccentText_MatchesAccent()
        {
            var t = Light();
            Assert.Equal(t["Accent"], t["AccentText"]);
        }

        [Fact]
        public void Themes_ExposeTheSameTokens()
        {
            Assert.Equal(
                Light().Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
                Dark().Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public void Accent_IsNeverUsedAsTextForeground()
        {
            const string xamlPattern = "Foreground=\"{DynamicResource Accent}\"";
            var offenders = new List<string>();
            bool Clean(string p) =>
                !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");

            foreach (var file in Directory.EnumerateFiles(AppDir, "*.xaml", SearchOption.AllDirectories).Where(Clean))
            {
                string text = Regex.Replace(File.ReadAllText(file), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
                if (text.Contains(xamlPattern)) offenders.Add(Path.GetRelativePath(AppDir, file));
            }

            var codePattern = new Regex(@"Foreground\s*=\s*\(Brush\)\s*FindResource\(""Accent""\)");
            foreach (var file in Directory.EnumerateFiles(AppDir, "*.cs", SearchOption.AllDirectories).Where(Clean))
            {
                if (codePattern.IsMatch(File.ReadAllText(file))) offenders.Add(Path.GetRelativePath(AppDir, file));
            }

            Assert.True(offenders.Count == 0,
                "Accent как цвет текста (нужен AccentText):\n  " + string.Join("\n  ", offenders));
        }
    }
}
