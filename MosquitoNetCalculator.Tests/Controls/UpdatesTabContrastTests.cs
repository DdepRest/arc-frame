using System;
using System.Collections.Generic;
using System.Windows.Media;
using MosquitoNetCalculator.Converters;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// Контрастный аудит вкладки «Обновления» (v3.51, проверка владельца
    /// «в светлой теме не появились ли новые проблемы контраста»).
    ///
    /// Пары «текст/фон» берутся из РЕАЛЬНЫХ словарей ThemeService (Light/Dark) —
    /// тот же источник, что подменяет DynamicResource в рантайме. Порог AA —
    /// 4.5:1 для мелкого текста (элементы вкладки — 10.5–13px), 3:1 для
    /// крупных/графических (полоса статуса 4px — графика).
    /// </summary>
    public class UpdatesTabContrastTests
    {
        private static Dictionary<string, string> Theme(string name) =>
            name switch
            {
                "light" => ThemeService.LightColorsForTests(),
                "dark"  => ThemeService.DarkColorsForTests(),
                _ => throw new ArgumentException(name),
            };


        private static double Ratio(string fgHex, string bgHex) =>
            ContrastRatio(
                (Color)ColorConverter.ConvertFromString(fgHex),
                (Color)ColorConverter.ConvertFromString(bgHex));

        /// <summary>WCAG relative luminance + ratio.</summary>
        internal static double ContrastRatio(Color a, Color b)
        {
            static double Channel(byte c)
            {
                double s = c / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            double la = 0.2126 * Channel(a.R) + 0.7152 * Channel(a.G) + 0.0722 * Channel(a.B);
            double lb = 0.2126 * Channel(b.R) + 0.7152 * Channel(b.G) + 0.0722 * Channel(b.B);
            double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
            return (hi + 0.05) / (lo + 0.05);
        }

        [Theory]
        [InlineData("light")]
        [InlineData("dark")]
        public void Chip_InactiveText_OnChipBg_MeetsAA(string theme)
        {
            var t = Theme(theme);
            Assert.True(Ratio(t["TextSecondary"], t["ChipBg"]) >= 4.5,
                $"{theme}: TextSecondary on ChipBg must be ≥4.5:1");
        }

        [Theory]
        [InlineData("light")]
        [InlineData("dark")]
        public void Chip_ActiveText_OnAccent_MeetsAA(string theme)
        {
            var t = Theme(theme);
            // Активный чип: OnAccentPrimary на Accent (v3.50.1 прототип .btn.blue).
            double dark = Ratio(t["OnAccentPrimary"], t["Accent"]);
            double white = Ratio(t["OnAccent"], t["Accent"]);
            Assert.True(Math.Max(dark, white) >= 4.5,
                $"{theme}: neither OnAccentPrimary ({dark:F2}) nor OnAccent ({white:F2}) on Accent meets 4.5:1");
        }

        [Theory]
        [InlineData("light")]
        [InlineData("dark")]
        public void YearPill_Text_OnHeaderBg_MeetsAA(string theme)
        {
            var t = Theme(theme);
            Assert.True(Ratio(t["TextSecondary"], t["HeaderBg"]) >= 4.5,
                $"{theme}: TextSecondary on HeaderBg must be ≥4.5:1");
        }

        [Theory]
        [InlineData("light")]
        [InlineData("dark")]
        public void SearchField_Text_And_Placeholder_MeetsAA(string theme)
        {
            var t = Theme(theme);
            Assert.True(Ratio(t["TextPrimary"], t["Surface"]) >= 4.5,
                $"{theme}: TextPrimary on Surface must be ≥4.5:1");
            // Плейсхолдер — декоративная подсказка; требуем хотя бы 3:1 (не исчезает).
            Assert.True(Ratio(t["TextMuted"], t["Surface"]) >= 3.0,
                $"{theme}: TextMuted placeholder on Surface must be ≥3:1");
        }

        [Theory]
        [InlineData("light")]
        [InlineData("dark")]
        public void TypeBadge_Fg_OnSoftBg_MeetsAA(string theme)
        {
            var t = Theme(theme);

            // Пары из конвертера: (Badge*Fg на Badge*Bg) — реальные ключи обеих тем.
            foreach (var (fgKey, bgKey) in new[] {
                ("BadgeSuccessFg", "BadgeSuccessBg"),   // Новинка
                ("BadgeDefaultFg", "BadgeDefaultBg"),   // Улучшение
                ("BadgeWarningFg", "BadgeWarningBg"),   // Исправление
            })
            {
                double ratio = Ratio(t[fgKey], t[bgKey]);
                Assert.True(ratio >= 4.5, $"{theme}: {fgKey} on {bgKey} = {ratio:F2}, must be ≥4.5:1");
            }
        }

        [Theory]
        [InlineData("light")]
        [InlineData("dark")]
        public void TypeStripe_StrongColor_DistinguishableFromSurface(string theme)
        {
            var t = Theme(theme);
            // Полоса — графика: ≥3:1 к карточке (WCAG 1.4.11 non-text contrast).
            foreach (var key in new[] { "Success", "Accent", "Warning" })
            {
                double ratio = Ratio(t[key], t["Surface"]);
                Assert.True(ratio >= 3.0, $"{theme}: stripe {key} on Surface = {ratio:F2}, must be ≥3:1");
            }
        }

        [Theory]
        [InlineData("light")]
        [InlineData("dark")]
        public void CardBody_Text_OnSurface_MeetsAA(string theme)
        {
            var t = Theme(theme);
            Assert.True(Ratio(t["TextPrimary"], t["Surface"]) >= 4.5,
                $"{theme}: body text on Surface must be ≥4.5:1");
            Assert.True(Ratio(t["TextMuted"], t["Surface"]) >= 4.5,
                $"{theme}: TextMuted (dates, chevron) on Surface must be ≥4.5:1");
        }

        [Fact]
        public void ChipBg_DiffersFromSurface_InBothThemes()
        {
            // Поле поиска на Surface должно отличаться от чипов на ChipBg —
            // иначе «всё сливается» (жалоба владельца на монотонность).
            foreach (var theme in new[] { "light", "dark" })
            {
                var t = Theme(theme);
                double ratio = Ratio(t["ChipBg"], t["Surface"]);
                Assert.True(ratio >= 1.1,
                    $"{theme}: ChipBg vs Surface ratio {ratio:F2} — фоны неразличимы");
            }
        }
    }
}
