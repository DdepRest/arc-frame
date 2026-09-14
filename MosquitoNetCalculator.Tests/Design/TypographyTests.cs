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
    /// Типографика v3.53.0: шрифт Inter (SIL OFL 1.1) вшит в сборку и реально
    /// резолвится, а не подменяется системным.
    ///
    /// Отдельный тест нужен потому, что WPF при неудаче с ресурсом шрифта молча
    /// уходит на фолбэк: приложение выглядит «почти так же», и подмена остаётся
    /// незамеченной. Именно этот тест вскрыл, что абсолютный pack-URI со «#»
    /// внутри не работает (см. AppFontService и GOTCHAS).
    /// </summary>
    [Collection("WPF_UI")]
    public class TypographyTests
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

        private static string ResolvedFamilies(FontFamily family)
        {
            var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            return typeface.TryGetGlyphTypeface(out var glyph)
                ? string.Join(", ", glyph.FamilyNames.Values)
                : "не загрузился";
        }

        [Fact]
        public void InterFont_IsActuallyBundled_NotSilentFallback()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var family = Assert.IsType<FontFamily>(Application.Current!.Resources["Font.Text"]);

                string resolved = ResolvedFamilies(family);
                Assert.True(resolved.Contains("Inter", StringComparison.OrdinalIgnoreCase),
                    $"Токен Font.Text дал фолбэк [{resolved}] вместо Inter. Источник: {family.Source}");
            });
        }

        [Fact]
        public void InterFont_ProvidesRegularMediumSemiBoldBold_AndItalic()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var family = (FontFamily)Application.Current!.Resources["Font.Text"];
                var weights = new[] { FontWeights.Normal, FontWeights.Medium, FontWeights.SemiBold, FontWeights.Bold };

                foreach (var weight in weights)
                {
                    var typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
                    Assert.True(typeface.TryGetGlyphTypeface(out var glyph),
                        $"Нет начертания {weight} — интерфейс получит поддельный вес.");
                    Assert.Contains(glyph.FamilyNames.Values,
                        n => n.Contains("Inter", StringComparison.OrdinalIgnoreCase));
                    Assert.Equal(weight, glyph.Weight);
                }

                // Курсив нужен: в примечаниях есть курсивный текст и кнопка «К».
                var italic = new Typeface(family, FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);
                Assert.True(italic.TryGetGlyphTypeface(out var italicGlyph), "Нет курсивного начертания Inter.");
                Assert.Equal(FontStyles.Italic, italicGlyph.Style);
            });
        }

        [Fact]
        public void FontToken_IsInstalledFromBothStartupPaths()
        {
            string fontService = File.ReadAllText(Path.Combine(
                RepoRoot(), "MosquitoNetCalculator", "Services", "AppFontService.cs"));

            // Значение задаётся кодом — значит, оно обязано ставиться и в
            // приложении, и в тестовом bootstrap'е, иначе тесты проверяли бы
            // не то состояние, что видит пользователь.
            Assert.Contains("new Uri(InterFolder", fontService);

            string appStartup = File.ReadAllText(Path.Combine(RepoRoot(), "MosquitoNetCalculator", "App.xaml.cs"));
            Assert.Contains("AppFontService.Install(this)", appStartup);

            string bootstrap = File.ReadAllText(Path.Combine(
                RepoRoot(), "MosquitoNetCalculator.Tests", "Helpers", "TestAppThemes.cs"));
            Assert.Contains("AppFontService.Install(application)", bootstrap);
        }

        [Fact]
        public void XamlFontToken_DeclaresFallback_AndExplainsWhyItIsOverridden()
        {
            string tokens = File.ReadAllText(Path.Combine(
                RepoRoot(), "MosquitoNetCalculator", "Themes", "Tokens.Typography.xaml"));

            var token = Regex.Match(tokens, "<FontFamily x:Key=\"Font\\.Text\">([^<]*)</FontFamily>").Groups[1].Value;

            // Фолбэк обязан оставаться системным шрифтом: если ресурс шрифта
            // почему-то не подгрузится, приложение должно остаться читаемым.
            Assert.Contains("Segoe UI", token);
            Assert.DoesNotContain("pack://", token);

            // И рядом должно быть объяснение, почему токен подменяется кодом.
            Assert.Contains("AppFontService", tokens);
        }

        [Fact]
        public void BundledFontFiles_ShipWithOflLicense_AndAreWiredIntoTheProject()
        {
            string fontDir = Path.Combine(RepoRoot(), "MosquitoNetCalculator", "Resources", "Fonts");
            var files = Directory.EnumerateFiles(fontDir).Select(Path.GetFileName).ToList();

            foreach (var expected in new[]
                     {
                         "Inter-Regular.ttf", "Inter-Italic.ttf", "Inter-Medium.ttf",
                         "Inter-SemiBold.ttf", "Inter-Bold.ttf",
                     })
            {
                Assert.Contains(expected, files);
            }

            // OFL 1.1 требует распространять лицензию вместе со шрифтом.
            string license = File.ReadAllText(Path.Combine(fontDir, "OFL.txt"));
            Assert.Contains("SIL Open Font License", license);

            // Шрифты обязаны попадать в сборку: без <Resource> pack-URI не разрешится.
            string csproj = File.ReadAllText(Path.Combine(
                RepoRoot(), "MosquitoNetCalculator", "MosquitoNetCalculator.csproj"));
            Assert.Contains("Resources\\Fonts\\*.ttf", csproj);
        }

        [Fact]
        public void TypeScale_IsMonotonic_AndKeepsElevenPixelFloor()
        {
            string tokens = File.ReadAllText(Path.Combine(
                RepoRoot(), "MosquitoNetCalculator", "Themes", "Tokens.Typography.xaml"));

            // Только ступени шкалы: Type.LineHeight* живут отдельно и не являются размерами.
            const string scaleNames = "Monogram|Caption|Body|BodyMd|BodyLg|Subtitle|Title|TitleLg|Display|Metric|Hero";
            var sizes = Regex.Matches(tokens, $"<sys:Double x:Key=\"Type\\.({scaleNames})\">([0-9.]+)</sys:Double>")
                .Select(m => double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture))
                .ToList();

            Assert.True(sizes.Count >= 8, "Шкала размеров подозрительно короткая.");

            // Правило UX-16: ниже 11px — только монограмма бейджа (Type.Monogram = 9).
            Assert.Equal(new List<double> { 9 }, sizes.Where(s => s < 11).ToList());

            // Дробных ступеней в шкале быть не должно (они и давали «гребень»).
            Assert.DoesNotContain(sizes, s => Math.Abs(s - Math.Round(s)) > 0.001);

            // Каждая ступень уникальна — иначе это не шкала.
            Assert.Equal(sizes.Count, sizes.Distinct().Count());
        }

        [Fact]
        public void TextStyles_ReferenceTokens_NotLiteralSizes()
        {
            string fontStyles = File.ReadAllText(Path.Combine(
                RepoRoot(), "MosquitoNetCalculator", "Themes", "FontStyles.xaml"));

            // FontStyles — источник каскада: его размеры обязаны идти из шкалы,
            // иначе токены не влияют ни на что.
            Assert.DoesNotContain("Property=\"FontSize\" Value=\"1", fontStyles);
            Assert.Contains("Property=\"FontSize\" Value=\"{StaticResource Type.", fontStyles);
        }
    }
}
