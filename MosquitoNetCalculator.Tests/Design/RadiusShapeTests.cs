using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Design
{
    /// <summary>
    /// Форма скругления, а не его число. Тест закрепляет замеренный факт, на
    /// котором стоит вся шкала радиусов (v3.53.1): <b>WPF не клампит радиус до
    /// полусферы</b>. У квадратного элемента предел скругления — окружность, и
    /// <c>Radius.Pill</c> (999) даёт ровно её, пиксель в пиксель. У широкого
    /// элемента предел — эллипс: <c>Radius.Pill</c> на 40x20 даёт не капсулу, а
    /// «лепесток» (−69px площади против капсулы). Поэтому капсулы берут свою
    /// ступень (<c>Radius.Capsule*</c> = половина высоты элемента), а Pill
    /// остаётся кругам. Разбор — GOTCHAS §27.
    /// </summary>
    public class RadiusShapeTests
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

        private static readonly string RadiusXamlPath = Path.Combine(
            RepoRoot(), "MosquitoNetCalculator", "Themes", "Tokens.Radius.xaml");

        private static double TokenValue(string key)
        {
            string text = File.ReadAllText(RadiusXamlPath);
            var m = Regex.Match(text, $"<CornerRadius x:Key=\"Radius\\.{key}\">([0-9.,]+)</CornerRadius>");
            Assert.True(m.Success, $"в Tokens.Radius.xaml нет Radius.{key}");
            return double.Parse(m.Groups[1].Value.Split(',')[0],
                System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Рендерит сплошной Border заданного размера и радиуса вне экрана.</summary>
        private static byte[] Render(double width, double height, double radius)
        {
            var border = new Border
            {
                Width = width,
                Height = height,
                Background = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                CornerRadius = new CornerRadius(radius)
            };
            border.Measure(new Size(width, height));
            border.Arrange(new Rect(0, 0, width, height));

            var rtb = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(border);

            var pixels = new byte[(int)(width * height * 4)];
            rtb.CopyPixels(pixels, (int)(width * 4), 0);
            return pixels;
        }

        private static int Covered(byte[] pixels)
        {
            int n = 0;
            for (int i = 3; i < pixels.Length; i += 4)
                if (pixels[i] > 0) n++;
            return n;
        }

        private static int ByteDiff(byte[] a, byte[] b) =>
            a.Where((v, i) => v != b[i]).Count();

        [Theory]
        [InlineData(16, 16, 8)]    // бегунок тумблера
        [InlineData(22, 22, 11)]   // бегунок крупного тумблера
        [InlineData(18, 18, 9)]    // радио
        [InlineData(24, 24, 12)]   // кружок иконки в тосте / галочка «ключ проверен»
        [InlineData(6, 6, 3)]      // точка «есть несохранённое»
        public void Pill_OnSquareElement_RendersExactlyAsAHalfSizeRadius(
            double width, double height, double halfSize)
        {
            var half = WpfTestHelper.RunOnSta(() => Render(width, height, halfSize));
            var pill = WpfTestHelper.RunOnSta(() => Render(width, height, TokenValue("Pill")));

            Assert.Equal(0, ByteDiff(half, pill));
        }

        [Theory]
        [InlineData(40, 20, 10)]   // трек тумблера
        [InlineData(60, 23, 10)]   // бейдж-чип
        [InlineData(48, 26, 13)]   // крупный трек тумблера
        [InlineData(6, 40, 3)]     // трек скроллбара
        public void Pill_OnWideElement_IsNotACapsule_ItOverCutsTheCorners(
            double width, double height, double capsuleRadius)
        {
            var capsule = WpfTestHelper.RunOnSta(() => Render(width, height, capsuleRadius));
            var pill = WpfTestHelper.RunOnSta(() => Render(width, height, TokenValue("Pill")));

            Assert.True(ByteDiff(capsule, pill) > 0,
                $"Pill совпал с капсулой {capsuleRadius} на {width}x{height} — проверь логику замеров");
            Assert.True(Covered(pill) < Covered(capsule) - 20,
                $"Pill на {width}x{height} не режет углы сильнее капсулы: " +
                $"{Covered(pill)} против {Covered(capsule)} закрашенных пикселей");
        }

        /// <summary>
        /// Тонкие полосы (5–6px) — исключение внутри исключения: там радиус 3 и
        /// дробный 2.5 дают ОДНО И ТО ЖЕ покрытие, потому что WPF клампит радиус
        /// по меньшей стороне. Поэтому в шкале нет дробной ступени 2.5: прогресс
        /// 34x5 спокойно живёт на <c>Radius.CapsuleSm</c>.
        /// </summary>
        [Fact]
        public void CapsuleSm_FitsBothFractionalAndIntegerBarRadii()
        {
            double sm = TokenValue("CapsuleSm");
            Assert.Equal(3, sm);

            var exact = WpfTestHelper.RunOnSta(() => Render(34, 5, 2.5));
            var token = WpfTestHelper.RunOnSta(() => Render(34, 5, sm));

            Assert.Equal(Covered(exact), Covered(token));
        }

        /// <summary>
        /// Квадратность доказывают равные значения — числа или ОДИН И ТОТ ЖЕ
        /// токен размера в обоих атрибутах: <c>{StaticResource Space.Xl}</c>
        /// слева и справа означает «та же величина», а не «похожая».
        /// </summary>
        private static bool SameSize(string a, string b)
        {
            if (a == b) return true;
            var keyA = Regex.Match(a, "\\{(?:Static|Dynamic)Resource ([A-Za-z0-9._]+)\\}");
            var keyB = Regex.Match(b, "\\{(?:Static|Dynamic)Resource ([A-Za-z0-9._]+)\\}");
            return keyA.Success && keyB.Success && keyA.Groups[1].Value == keyB.Groups[1].Value;
        }

        /// <summary>
        /// Pill — только квадратные элементы. Статически проверить «элемент
        /// квадратный» нельзя, но можно потребовать явные равные Width и Height
        /// в том же теге: именно на этом сломался бы бейдж с Padding вместо
        /// размеров (он взял бы Pill и превратился в лепесток).
        /// </summary>
        [Fact]
        public void PillToken_IsUsedOnlyOnElementsWithEqualWidthAndHeight()
        {
            string appDir = Path.Combine(RepoRoot(), "MosquitoNetCalculator");
            var tag = new Regex("<([A-Za-z:]+)[^>]*?/?>", RegexOptions.Singleline);
            var offenders = new List<string>();

            foreach (string file in Directory.EnumerateFiles(appDir, "*.xaml", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                // Словари токенов сами объявляют ключ Radius.Pill — это не элемент.
                if (Path.GetFileName(file).StartsWith("Tokens.", StringComparison.OrdinalIgnoreCase)) continue;

                string text = Regex.Replace(File.ReadAllText(file), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
                foreach (Match m in tag.Matches(text))
                {
                    if (!m.Value.Contains("Radius.Pill")) continue;

                    var w = Regex.Match(m.Value, "Width=\"([^\"]+)\"");
                    var h = Regex.Match(m.Value, "Height=\"([^\"]+)\"");
                    if (!w.Success || !h.Success || !SameSize(w.Groups[1].Value, h.Groups[1].Value))
                    {
                        string rel = Path.GetRelativePath(appDir, file).Replace('\\', '/');
                        offenders.Add($"{rel}: {Regex.Replace(m.Value, "\\s+", " ").Trim()}");
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                "Radius.Pill — круг: он честен только на квадратном элементе, на широком WPF даёт эллипс " +
                "(капсулы — Radius.Capsule/CapsuleLg/CapsuleSm):\n  " + string.Join("\n  ", offenders));
        }
    }
}
