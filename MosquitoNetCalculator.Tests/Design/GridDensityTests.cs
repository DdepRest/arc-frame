using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// Плотность таблицы позиций (v3.53.1, разбор — GOTCHAS §25).
    ///
    /// <para><b>Почему проверка по числам, а не рендером.</b> Первая версия этого
    /// стража рендерила настоящую таблицу (как <c>CardStripeCornerTests</c>), но в
    /// общем прогоне она падает кросс-поточным <c>InvalidOperationException</c>:
    /// приложение тестов живёт на одном STA-потоке, а рендер идёт на другом, и
    /// <c>FrameworkTemplate.Seal()</c> (шаблон клетки/шапки из словаря тем,
    /// запечатывается при применении) делает <c>VerifyAccess</c>. В одиночку тест
    /// зелёный, в прогоне — красный, то есть как раз тот случай, когда «тест есть,
    /// а смысла нет». Поэтому здесь проверяются числа стиля и строение клетки:
    /// регрессия «вернули 32/36» или «вернули подпись под бейджем» падает сразу.</para>
    ///
    /// <para>Инварианты: (1) строка не «воздушная» — пустота вокруг строки текста
    /// 12px не больше 12px; (2) шапка не выше строк данных; (3) подпись шапки идёт
    /// токеном, а не литералом ниже пола шкалы; (4) сумма монтажа стоит в одной
    /// строке с бейджем.</para>
    /// </summary>
    [Collection("WPF_UI")]
    public class GridDensityTests
    {
        /// <summary>Верхняя граница пустоты вокруг строки текста 12px.</summary>
        private const double MaxRowAir = 12;

        /// <summary>Ниже этого строку не опустить: в клетках живут элементы 20px.</summary>
        private const double MinRowFloor = 20;

        private static readonly Regex MinRowHeight = new("MinRowHeight\"\\s+Value=\"([0-9.]+)\"", RegexOptions.Compiled);
        private static readonly Regex HeaderHeight = new("ColumnHeaderHeight\"\\s+Value=\"([0-9.]+)\"", RegexOptions.Compiled);

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

        private static string ReadWithoutComments(string path) =>
            Regex.Replace(File.ReadAllText(path), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

        private static string GridStyles => ReadWithoutComments(Path.Combine(AppDir, "Themes", "DataGridStyles.xaml"));

        private static string GridMarkup => ReadWithoutComments(
            Path.Combine(AppDir, "Controls", "OrderItemsControl.xaml"));

        private static double Value(Regex pattern, string text, string what)
        {
            var match = pattern.Match(text);
            Assert.True(match.Success, $"В стилях таблицы позиций нет {what} — плотность не проверить.");
            return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Строка текста 12px тем же шрифтом, что и таблица, — тем самым вшитым
        /// Inter, который ставит приложению <c>AppFontService</c>.
        /// </summary>
        private static double TextLineBox()
        {
            // TestAppThemes, а не «голый» STA: создание FontFamily по pack-URI
            // требует зарегистрированной схемы pack (её поднимает приложение
            // тестов), иначе — UriFormatException «Invalid port specified».
            double lineBox = 0;
            TestAppThemes.RunOnSta(() =>
            {
                var family = MosquitoNetCalculator.Services.AppFontService.CreateInterFamily();

                // Иначе замер бессмыслен: фолбэк на системный шрифт даёт другую строку.
                var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                Assert.True(typeface.TryGetGlyphTypeface(out var glyph)
                            && glyph.FamilyNames.Values.Any(n => n.Contains("Inter", StringComparison.OrdinalIgnoreCase)),
                    "Вшитый Inter не загрузился — замер строки текста был бы по системному шрифту.");

                var text = new System.Windows.Controls.TextBlock
                {
                    Text = "Расчёт",
                    FontFamily = family,
                    FontSize = 12,
                };
                text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                lineBox = text.DesiredSize.Height;
            });

            return lineBox;
        }

        [Fact]
        public void DataRow_IsNotMostlyAir()
        {
            double row = Value(MinRowHeight, GridStyles, "MinRowHeight");
            double lineBox = TextLineBox();
            double air = row - lineBox;

            Assert.True(row >= MinRowFloor,
                $"MinRowHeight={row} — в клетке «Монтаж» кнопка 20px, строка её обрежет.");
            Assert.True(air <= MaxRowAir,
                $"Строка {row}px при строке текста {Math.Round(lineBox, 1)}px — пустоты {Math.Round(air, 1)}px " +
                $"(допустимо {MaxRowAir}). Таблица снова «воздушная»: 55% строки было пустотой при 32px — GOTCHAS §25.");
        }

        [Fact]
        public void ColumnHeader_IsNotTallerThanDataRows()
        {
            double row = Value(MinRowHeight, GridStyles, "MinRowHeight");
            double header = Value(HeaderHeight, GridStyles, "ColumnHeaderHeight");

            Assert.True(header <= row,
                $"Шапка {header}px выше строки {row}px — перевёрнутая иерархия: самая широкая полоса " +
                "несёт самую мелкую подпись (было 36 против 32, GOTCHAS §25).");
        }

        [Fact]
        public void HeaderCaption_UsesTheToken_NotALiteral()
        {
            Assert.Contains("Property=\"FontSize\" Value=\"{StaticResource Type.Caption}\"", GridStyles);

            // 10.5px был и дробным, и ниже пола шкалы — то есть сразу два долга.
            Assert.DoesNotContain("Value=\"10.5\"", GridStyles);
        }

        /// <summary>
        /// Числовые колонки дышат ВЛЕВО: правое выравнивание прижимает число к
        /// своей правой границе, поэтому воздух между соседними числами даёт
        /// ЛЕВЫЙ отступ. Замер по кадрам при симметричных 6px: между «Площ./Дл.» и
        /// «Ценой» оставалось 11px, между «Ценой» и «Суммой» — 18px на самой
        /// длинной сумме, тогда как у остальных колонок 26–36px. Симметричный
        /// отступ вернёт «зажёванные» суммы (GOTCHAS §28).
        /// </summary>
        [Fact]
        public void NumericCells_KeepAirFromTheirLeftNeighbour()
        {
            const double minLeftAir = 12;
            const double maxRightAir = 8;

            var rightCellStyle = new Regex("<Style[^>]*RightCell[^>]*>(.*?)</Style>", RegexOptions.Singleline);
            var marginSetter = new Regex(
                "Property=\"Margin\" Value=\"([0-9.]+),([0-9.]+)(?:,([0-9.]+),([0-9.]+))?\"");

            var offenders = new List<string>();
            int checkedSetters = 0;
            foreach (Match style in rightCellStyle.Matches(GridMarkup))
            {
                foreach (Match setter in marginSetter.Matches(style.Groups[1].Value))
                {
                    checkedSetters++;
                    double left = double.Parse(setter.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (!setter.Groups[4].Success)
                    {
                        offenders.Add($"симметричный Margin=\"{setter.Groups[1].Value},{setter.Groups[2].Value}\" — " +
                                      "числа соседних колонок слипнутся");
                        continue;
                    }

                    double right = double.Parse(setter.Groups[2].Value, CultureInfo.InvariantCulture);
                    if (left < minLeftAir) offenders.Add($"левый отступ {left}px < {minLeftAir}px");
                    if (right > maxRightAir) offenders.Add($"правый отступ {right}px — число отрывается от своей границы");
                }
            }

            Assert.True(checkedSetters >= 5,
                $"Числовых клеток с отступом нашлось {checkedSetters} — проверка вырождается в пустую.");
            Assert.True(offenders.Count == 0,
                "Клетки с правым выравниванием: воздух даётся слева (проверка «зажёванных» сумм, GOTCHAS §28):\n  " +
                string.Join("\n  ", offenders));
        }

        [Fact]
        public void InstallationAmount_StaysOnTheBadgeLine()
        {
            string markup = GridMarkup;

            int start = markup.IndexOf("Header=\"Монтаж\"", StringComparison.Ordinal);
            Assert.True(start >= 0, "В разметке нет колонки «Монтаж» — проверка ритма строки ничего не проверяет.");
            int end = markup.IndexOf("</DataGridTemplateColumn>", start, StringComparison.Ordinal);
            string cell = markup[start..end];

            Assert.Contains("Orientation=\"Horizontal\"", cell);
            Assert.DoesNotContain("Margin=\"0,-1,0,0\"", cell);

            // Кнопка подчинена плотности строки: 26px она сама задавала высоту строки.
            var height = Regex.Match(cell, "Height=\"([0-9.]+)\"");
            Assert.True(height.Success, "У бейджа «Монтаж» не задана высота — строка перестала быть предсказуемой.");
            double badge = double.Parse(height.Groups[1].Value, CultureInfo.InvariantCulture);
            double row = Value(MinRowHeight, GridStyles, "MinRowHeight");

            Assert.True(badge <= row - 4,
                $"Бейдж {badge}px в строке {row}px — на воздух остаётся меньше 4px, строка снова начнёт расти.");
        }
    }
}
