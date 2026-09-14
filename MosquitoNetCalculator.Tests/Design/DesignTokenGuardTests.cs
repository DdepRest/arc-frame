using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace MosquitoNetCalculator.Tests.Design
{
    /// <summary>
    /// Стражи дизайн-системы v3.53.0 (контракт — docs/specs/design-system-v3.53.md).
    ///
    /// Зачем: дизайн-токены бесполезны, если разметку можно «на глазок» дополнять
    /// свободными числами. До v3.53.0 в разметке жило 496 литералов размера шрифта,
    /// 226 радиусов и 36 хардкод-цветов мимо палитры — именно из них «почти
    /// одинаковые» отступы, дробные размеры и выпадающие из темы элементы.
    ///
    /// Механика — «ратчет»: бютжет на файл в Design/design-token-budget.json может
    /// только убывать. Мигрировал литералы на токены — понизь цифру в том же
    /// коммите. Это позволяет чистить долг постепенно, не запрещая работу, но и не
    /// давая долгу расти.
    /// </summary>
    public class DesignTokenGuardTests
    {
        // ── Пути и сканирование ───────────────────────────────────────────

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "MosquitoNetCalculator", "App.xaml")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Не найден корень репозитория (MosquitoNetCalculator/App.xaml).");
        }

        private static string AppDir => Path.Combine(RepoRoot(), "MosquitoNetCalculator");

        /// <summary>XAML-файлы продукта без артефактов сборки (obj/bin/.artifacts).</summary>
        private static IEnumerable<string> ProductXaml()
        {
            return Directory.EnumerateFiles(AppDir, "*.xaml", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                            && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                            && !p.Contains($"{Path.DirectorySeparatorChar}.artifacts{Path.DirectorySeparatorChar}"));
        }

        private static IEnumerable<string> ProductCs()
        {
            return Directory.EnumerateFiles(AppDir, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                            && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                            && !p.Contains($"{Path.DirectorySeparatorChar}.artifacts{Path.DirectorySeparatorChar}"));
        }

        private static string Relative(string fullPath) =>
            Path.GetRelativePath(AppDir, fullPath).Replace('\\', '/');

        /// <summary>Файлы цветового слоя: только здесь допустимы hex-литералы.</summary>
        private static bool IsColorLayer(string relativePath) =>
            relativePath.Equals("Themes/Brushes.xaml", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("Themes/Tokens.", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals("Services/ThemeService.cs", StringComparison.OrdinalIgnoreCase);

        private static readonly Regex FontSizeAttr = new("FontSize=\"[0-9.]+\"", RegexOptions.Compiled);
        private static readonly Regex FontSizeSetter = new("Property=\"FontSize\"\\s*Value=\"[0-9.]+\"", RegexOptions.Compiled);
        private static readonly Regex FontSizeValue = new("FontSize=\"([0-9.]+)\"", RegexOptions.Compiled);
        private static readonly Regex FontSizeSetterValue = new("Property=\"FontSize\"\\s*Value=\"([0-9.]+)\"", RegexOptions.Compiled);
        private static readonly Regex CornerRadiusAttr = new("CornerRadius=\"[0-9]", RegexOptions.Compiled);
        private static readonly Regex CornerRadiusSetter = new("Property=\"CornerRadius\"\\s*Value=\"[0-9]", RegexOptions.Compiled);
        private static readonly Regex HexColor = new("#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?\\b", RegexOptions.Compiled);
        // v3.53: считаем только ЛИТЕРАЛЬНЫЕ длительности. Ссылка на токен
        // (Duration="{StaticResource Motion.Base}") нарушением не является.
        private static readonly Regex XamlDuration = new("Duration=\"[0-9:.]", RegexOptions.Compiled);
        private static readonly Regex CodeMilliseconds = new("TimeSpan\\.FromMilliseconds\\(", RegexOptions.Compiled);

        private static int Count(string text, params Regex[] patterns) =>
            patterns.Sum(p => p.Matches(text).Count);

        /// <summary>
        /// Читает XAML без XML-комментариев: в пояснениях к дизайну цвета и
        /// размеры упоминаются как примеры (например, «на ChipBg светлая
        /// #EBF3FC / тёмная #26384C»), и такие упоминания не должны выглядеть
        /// как нарушение цветового слоя.
        /// </summary>
        private static string ReadWithoutComments(string path) =>
            Regex.Replace(File.ReadAllText(path), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

        private static Dictionary<string, int> CountPerFile(
            IEnumerable<string> files, Func<string, int> count, bool includeColorLayer = true)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                string rel = Relative(file);
                if (!includeColorLayer && IsColorLayer(rel)) continue;

                int n = count(ReadWithoutComments(file));
                if (n > 0) result[rel] = n;
            }

            return result;
        }

        private static JsonElement Budget()
        {
            string path = Path.Combine(RepoRoot(), "MosquitoNetCalculator.Tests", "Design", "design-token-budget.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.Clone();
        }

        /// <summary>
        /// Общая проверка «ратчета»: в каждом файле литералов не больше бюджета,
        /// бюджет не «протух» (запас ≤ 3) и в разметке нет файлов с литералами,
        /// которых нет в бюджете (новый контрол обязан сразу идти на токенах).
        /// </summary>
        private static void AssertRatchet(
            string category, Dictionary<string, int> actual, JsonElement budgetRoot)
        {
            var budget = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var prop in budgetRoot.GetProperty(category).EnumerateObject())
                budget[prop.Name] = prop.Value.GetInt32();

            var problems = new List<string>();
            var improved = new List<string>();

            foreach (var (file, count) in actual)
            {
                if (!budget.TryGetValue(file, out int allowed))
                {
                    problems.Add($"{file}: {count} литералов — файла нет в бюджете (клади новый код сразу на токенах)");
                    continue;
                }

                if (count > allowed)
                    problems.Add($"{file}: {count} > бюджета {allowed} (миграция развернулась назад)");

                if (count < allowed - 3)
                    improved.Add($"{file}: стало {count} вместо {allowed} — понизь бюджет");
            }

            foreach (var (file, allowed) in budget)
            {
                if (allowed > 0 && !actual.ContainsKey(file))
                    improved.Add($"{file}: литералов не осталось (бюджет {allowed}) — понизь до 0");
            }

            Assert.True(problems.Count == 0,
                $"Категория «{category}»: литералов больше, чем разрешено.\n  " + string.Join("\n  ", problems));

            Assert.True(improved.Count == 0,
                $"Категория «{category}»: бюджет отстал от реальности — понизь цифры.\n  " + string.Join("\n  ", improved));
        }

        // ── 1-4. Бюджеты литералов ────────────────────────────────────────

        [Fact]
        public void FontSizeLiterals_DoNotGrow()
        {
            var actual = CountPerFile(ProductXaml(), text => Count(text, FontSizeAttr, FontSizeSetter));
            AssertRatchet("fontSize", actual, Budget());
        }

        [Fact]
        public void CornerRadiusLiterals_DoNotGrow()
        {
            var actual = CountPerFile(ProductXaml(), text => Count(text, CornerRadiusAttr, CornerRadiusSetter));
            AssertRatchet("cornerRadius", actual, Budget());
        }

        [Fact]
        public void AnimationDurations_DoNotGrow()
        {
            var budget = Budget().GetProperty("totals");

            int xaml = ProductXaml().Sum(f => Count(ReadWithoutComments(f), XamlDuration));
            Assert.True(xaml <= budget.GetProperty("rawAnimationDurationsXaml").GetInt32(),
                $"Литеральных Duration= в XAML стало {xaml} — используй Motion.Fast/Base/Slow/Emphasized.");

            // В коде остаются законные TimeSpan.FromMilliseconds: интервалы
            // таймеров, задержки повторов сети, ожидание первого токена —
            // это не движение. Бюджет фиксирует текущее число, чтобы новые
            // АНИМАЦИИ не появились «числом на месте».
            int code = ProductCs().Sum(f => Count(File.ReadAllText(f), CodeMilliseconds));
            Assert.True(code <= budget.GetProperty("rawAnimationDurationsCodeMs").GetInt32(),
                $"TimeSpan.FromMilliseconds в коде стало {code} (бюджет {budget.GetProperty("rawAnimationDurationsCodeMs").GetInt32()}) — " +
                "для анимаций используй Helpers/Motion, для нового таймера — осознанно подними бюджет.");
        }

        [Fact]
        public void HardcodedHexColors_LiveOnlyInTheColorLayer()
        {
            var actual = CountPerFile(ProductXaml(), text => Count(text, HexColor), includeColorLayer: false);
            int total = actual.Values.Sum();
            int allowed = Budget().GetProperty("totals").GetProperty("rawHexColorsOutsideColorLayer").GetInt32();

            Assert.True(total <= allowed,
                $"Хардкод-цветов вне цветового слоя стало {total} (бюджет {allowed}).\n  " +
                string.Join("\n  ", actual.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}: {kv.Value}")));
        }

        // ── 5. Пол 11px и запрет дробных размеров (правило UX-16) ─────────

        [Fact]
        public void SubElevenOrFractionalFontSizes_DoNotGrow()
        {
            var offenders = new List<(string File, string Value)>();
            foreach (var file in ProductXaml())
            {
                string text = ReadWithoutComments(file);
                foreach (Match m in FontSizeValue.Matches(text))
                    offenders.Add((Relative(file), m.Groups[1].Value));
                foreach (Match m in FontSizeSetterValue.Matches(text))
                    offenders.Add((Relative(file), m.Groups[1].Value));
            }

            var bad = offenders
                .Where(o => o.Value.Contains('.')
                            || double.Parse(o.Value, System.Globalization.CultureInfo.InvariantCulture) < 11)
                .ToList();

            int allowed = Budget().GetProperty("totals").GetProperty("subElevenOrFractionalFontSizes").GetInt32();
            string breakdown = string.Join("\n  ", bad.GroupBy(o => o.File)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key}: {g.Count()} ({string.Join(", ", g.Select(v => v.Value).Distinct())})"));

            Assert.True(bad.Count <= allowed,
                $"Размеров ниже 11px или дробных стало {bad.Count} (бюджет {allowed}).\n  " + breakdown);

            // Дробные размеры — отдельный, более строгий бюджет: 11.5/10.5 дают
            // субпиксельный гребень на 100% DPI и подлежат полной ликвидации.
            var fractional = bad.Where(o => o.Value.Contains('.')).ToList();
            int fractionalAllowed = Budget().GetProperty("totals").GetProperty("fractionalFontSizes").GetInt32();
            Assert.True(fractional.Count <= fractionalAllowed,
                $"Дробных размеров шрифта стало {fractional.Count} (бюджет {fractionalAllowed}): " +
                string.Join(", ", fractional.Select(f => $"{f.File}={f.Value}")));
        }

        // ── 6. Токены существуют и подключены ────────────────────────────

        [Fact]
        public void EveryTokenReferencedInXaml_IsDefined()
        {
            var themeFiles = Directory.EnumerateFiles(Path.Combine(AppDir, "Themes"), "*.xaml").ToList();
            var defined = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in themeFiles)
                foreach (Match m in Regex.Matches(File.ReadAllText(file), "x:Key=\"([^\"]+)\""))
                    defined.Add(m.Groups[1].Value);

            var referenced = new Dictionary<string, string>(StringComparer.Ordinal);
            var tokenRef = new Regex("\\{(?:Static|Dynamic)Resource\\s+((?:Space|Gap|Pad|Radius|Type|Weight|Font|Motion|Ease)\\.[A-Za-z0-9.]+)\\}");
            foreach (var file in ProductXaml())
                foreach (Match m in tokenRef.Matches(File.ReadAllText(file)))
                    referenced.TryAdd(m.Groups[1].Value, Relative(file));

            var missing = referenced.Where(kv => !defined.Contains(kv.Key))
                .Select(kv => $"{kv.Key} (в {kv.Value})")
                .OrderBy(x => x)
                .ToList();

            Assert.True(missing.Count == 0,
                "Ссылки на несуществующие токены — DynamicResource молчит и свойство остаётся дефолтным:\n  " +
                string.Join("\n  ", missing));

            Assert.True(referenced.Count > 0,
                "Ни одна разметка не использует дизайн-токены — проверь, что миграция не откатилась.");
        }

        [Fact]
        public void TokenDictionaries_AreMergedInAppAndTestBootstrap_InSameOrder()
        {
            string appXaml = File.ReadAllText(Path.Combine(AppDir, "App.xaml"));
            var appOrder = Regex.Matches(appXaml, "Source=\"Themes/(Tokens\\.[A-Za-z]+\\.xaml)\"")
                .Select(m => m.Groups[1].Value).ToList();

            Assert.Equal(4, appOrder.Count);

            string bootstrap = File.ReadAllText(Path.Combine(
                RepoRoot(), "MosquitoNetCalculator.Tests", "Helpers", "TestAppThemes.cs"));
            var bootstrapOrder = appOrder.Where(name => bootstrap.Contains($"\"{name}\"")).ToList();

            // Тестовый bootstrap мержит словари вручную и обязан повторять порядок
            // App.xaml: стили берут токены через StaticResource и до них не парсятся.
            Assert.Equal(appOrder, bootstrapOrder);
        }

        /// <summary>
        /// Токен Font.* подменяется в рантайме (AppFontService ставит вшитый
        /// Inter): StaticResource зафиксировал бы фолбэк Segoe UI, и шрифт
        /// молча не применился бы.
        /// </summary>
        [Fact]
        public void FontTokens_AreConsumedViaDynamicResource()
        {
            var offenders = new List<string>();
            foreach (var file in ProductXaml())
                foreach (Match m in Regex.Matches(ReadWithoutComments(file), "\\{StaticResource (Font\\.[A-Za-z]+)\\}"))
                    offenders.Add($"{Relative(file)}: {m.Groups[1].Value}");

            Assert.True(offenders.Count == 0,
                "Font.* нужно брать через DynamicResource (значение ставится кодом):\n  " +
                string.Join("\n  ", offenders));
        }

        [Fact]
        public void StripingRadius_IsCardRadiusMinusOnePixelFrame()
        {
            string radiusXaml = File.ReadAllText(Path.Combine(AppDir, "Themes", "Tokens.Radius.xaml"));

            double card = double.Parse(Regex.Match(radiusXaml,
                "<CornerRadius x:Key=\"Radius\\.Card\">([0-9.]+)</CornerRadius>").Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);

            var stripe = Regex.Match(radiusXaml,
                "<CornerRadius x:Key=\"Radius\\.StripeInner\">([0-9.,]+)</CornerRadius>").Groups[1].Value
                .Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();

            // Полоса — левая граница внутреннего Border: WPF обводит рамку по
            // контуру, поэтому внешняя 1px рамка карточки съедает радиус.
            // Порядок значений CornerRadius: TopLeft, TopRight, BottomRight, BottomLeft.
            Assert.Equal(4, stripe.Length);
            Assert.Equal(0, stripe[1]);              // TopRight — правая кромка прямая
            Assert.Equal(0, stripe[2]);              // BottomRight
            Assert.Equal(card - 1, stripe[0]);       // TopLeft скруглён как карточка
            Assert.Equal(card - 1, stripe[3]);       // BottomLeft
        }
    }
}
