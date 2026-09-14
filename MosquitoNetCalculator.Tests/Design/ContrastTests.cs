using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Design
{
    /// <summary>
    /// Контраст текста и графики по WCAG 2.1 в ОБЕИХ темах (v3.53.0).
    ///
    /// Тёмная палитра взята из утверждённого прототипа, светлая живёт давно, и до
    /// этой проверки контраст считался руками — в комментариях к токенам есть
    /// отметки вроде «≈4.3:1 — ниже AA». Так долг не виден: он всплывает только
    /// тогда, когда кто-то пожалуется.
    ///
    /// Механика: считаем коэффициент для набора пар «передний план на фоне» в
    /// каждой теме. Пары ниже минимума обязаны быть перечислены в
    /// design-contrast-debt.json (долг виден и не растёт). Починил пару — убери
    /// её из файла, иначе тест упадёт: долг нельзя «забыть» закрытым.
    /// </summary>
    public class ContrastTests
    {
        public sealed record Pair(string Foreground, string Background, double Min, string Why);

        private static readonly Pair[] Pairs =
        {
            // Текст на поверхностях (минимум AA для обычного текста)
            new("TextPrimary", "AppBg", 4.5, "основной текст на фоне приложения"),
            new("TextPrimary", "Surface", 4.5, "основной текст на панели"),
            new("TextPrimary", "QuickBg", 4.5, "основной текст на быстром добавлении"),
            new("TextPrimary", "RowAlt", 4.5, "основной текст на чередующейся строке"),
            new("TextPrimary", "ChipBg", 4.5, "основной текст на чипе"),
            new("TextSecondary", "Surface", 4.5, "вторичный текст на панели"),
            new("TextSecondary", "ChipBg", 4.5, "вторичный текст на чипе"),
            new("TextMuted", "Surface", 4.5, "приглушённый текст на панели"),
            new("TextMuted", "AppBg", 4.5, "приглушённый текст на фоне приложения"),
            new("HeaderText", "HeaderBg", 4.5, "текст шапки таблицы"),

            // Текст на заливках действий
            new("OnAccent", "Accent", 4.5, "текст на акцентной заливке"),
            new("OnAccentPrimary", "Accent", 4.5, "текст на акцентной заливке (прототип)"),
            new("OnAccentPrimary", "AccentHover", 4.5, "текст на акцентной заливке под курсором"),
            new("OnAccentPrimary", "AccentPress", 4.5, "текст на акцентной заливке под нажатием"),
            new("OnAccent", "AccentHover", 4.5, "название/галочка на акцентной заливке под курсором"),
            new("OnDanger", "Danger", 4.5, "текст на опасном действии"),
            new("OnSuccess", "Success", 4.5, "текст на успешном действии"),

            // Бейджи статусов
            new("BadgeDefaultFg", "BadgeDefaultBg", 4.5, "бейдж по умолчанию"),
            new("BadgeSuccessFg", "BadgeSuccessBg", 4.5, "бейдж успеха"),
            new("BadgeWarningFg", "BadgeWarningBg", 4.5, "бейдж предупреждения"),
            new("BadgeDangerFg", "BadgeDangerBg", 4.5, "бейдж опасности"),
            new("BadgeVisionFg", "BadgeVisionBg", 4.5, "бейдж vision-модели"),

            // Карточка ИТОГО (тёмная подложка в обеих темах)
            new("TotalText", "TotalBg", 4.5, "сумма на тёмной карточке"),
            new("TotalTextMuted", "TotalBg", 3.0, "подпись на тёмной карточке (крупная/вторичная)"),

            // Графические индикаторы и текст-акценты на панели (минимум 3:1)
            new("Danger", "Surface", 3.0, "красный индикатор на панели"),
            new("Success", "Surface", 3.0, "зелёный индикатор на панели"),
            new("Warning", "Surface", 3.0, "предупреждающий индикатор на панели"),
            new("Accent", "Surface", 3.0, "акцентная графика на панели"),
        };

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

        private static HashSet<string> Debt()
        {
            string path = Path.Combine(RepoRoot(), "MosquitoNetCalculator.Tests", "Design", "design-contrast-debt.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.GetProperty("belowMinimum")
                .EnumerateArray()
                .Select(e => e.GetString()!)
                .ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>Относительная яркость по WCAG 2.1.</summary>
        private static double Luminance(string hex)
        {
            string h = hex.TrimStart('#');
            double Channel(int offset)
            {
                double v = int.Parse(h.Substring(offset, 2), NumberStyles.HexNumber) / 255.0;
                return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(0) + 0.7152 * Channel(2) + 0.0722 * Channel(4);
        }

        private static double Ratio(string a, string b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        public static IEnumerable<object[]> AllPairs() => Pairs.Select(p => new object[] { p });

        [Theory]
        [MemberData(nameof(AllPairs))]
        public void Contrast_MeetsMinimumOrIsListedAsDebt(Pair pair)
        {
            var themes = new (string Name, Dictionary<string, string> Colors)[]
            {
                ("light", ThemeService.LightColorsForTests()),
                ("dark", ThemeService.DarkColorsForTests()),
            };

            var debt = Debt();
            var problems = new List<string>();
            var stale = new List<string>();

            foreach (var (theme, colors) in themes)
            {
                string id = $"{theme}:{pair.Foreground}/{pair.Background}";
                double ratio = Ratio(colors[pair.Foreground], colors[pair.Background]);
                bool listed = debt.Contains(id);

                if (ratio < pair.Min && !listed)
                    problems.Add($"{id} = {ratio:F2}:1 (нужно {pair.Min:F1}, {pair.Why})");

                if (ratio >= pair.Min && listed)
                    stale.Add($"{id} = {ratio:F2}:1 — долг закрыт, убери строку из design-contrast-debt.json");
            }

            Assert.True(stale.Count == 0, "Контраст починен, но долг не снят:\n  " + string.Join("\n  ", stale));
            Assert.True(problems.Count == 0,
                "Новые нарушения контраста:\n  " + string.Join("\n  ", problems) +
                "\nЛибо исправь пару токенов, либо (осознанно) допиши в design-contrast-debt.json.");
        }
    }
}
