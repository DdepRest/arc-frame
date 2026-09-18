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

        /// <summary>Словарь токенов типографики — источник Font.Text/Mono/Icon.</summary>
        private static string TokensFile => Path.Combine(AppDir, "Themes", "Tokens.Typography.xaml");

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

        /// <summary>
        /// То же, но для C#. Концепция «цветового слоя» (см. <see cref="IsColorLayer"/>
        /// выше) включает <c>Services/ThemeService.cs</c>, то есть замысел был
        /// покрыть и код, — но проверка сканировала только XAML. Из-за этого
        /// <c>Models/OrderData.cs</c> держал ВТОРУЮ палитру бейджей hex-литералами
        /// (16 значений, светлая тема), пока бюджет рапортовал «0 хардкод-цветов»:
        /// точка статуса в сайдбаре не следовала тёмной теме и отличалась от
        /// бейджа того же статуса на одном экране.
        ///
        /// Исключение — печатный слой (<c>Services/DrawingService.cs</c>: цвета SVG
        /// схем проёма, которые идут в QuestPDF). Он не интерфейсный и не
        /// переключается темой, поэтому живёт в бюджете явной строкой, а не молчанием
        /// проверки.
        ///
        /// Комментарии в C# не вырезаются (в XAML — вырезаются): наивная обрезка
        /// строк после «//» (URL, пути к шаблонам) прятала бы настоящие литералы,
        /// а упоминание цвета в комментарии к коду — редкость и лечится переформулировкой.
        /// </summary>
        [Fact]
        public void HexColorsInCode_LiveOnlyInTheColorLayerOrPrintSchematics()
        {
            var actual = CountPerFile(ProductCs(), text => Count(text, HexColor), includeColorLayer: false);
            AssertRatchet("hexInCode", actual, Budget());
        }

        // ── 5. Семьи шрифтов ─────────────────────────────────────────────

        /// <summary>Семьи иконочных глифов: их нет в Inter, токен их не заменяет.</summary>
        private static readonly string[] IconFamilies = { "Segoe Fluent Icons", "Segoe MDL2 Assets" };

        /// <summary>
        /// ЛЮБАЯ семья в разметке задаётся только токеном: текст — <c>Font.Text</c>,
        /// код — <c>Font.Mono</c>, глифы — <c>Font.Icon</c>. Раньше иконочные литералы
        /// разрешались (80 мест с «Segoe Fluent Icons, Segoe MDL2 Assets»), но это
        /// дыра стража: опечатка в имени семьи («Segoe Fluent Icon») даёт молчаливый
        /// tofu, который не поймает ни один тест, — токен же проверяется на
        /// существование и определён в одном месте. Печатные документы
        /// (<c>FlowDocumentBuilder</c>, <c>FixedDocumentBuilder</c>, <c>DrawingService</c>)
        /// берут печатную семью в C# — это не интерфейсный текст, и разметки у них нет.
        /// </summary>
        [Fact]
        public void TextFontFamilies_AreTokens_IconFamiliesMayStayLiteral()
        {
            var offenders = new List<string>();

            foreach (var file in ProductXaml())
            {
                foreach (Match m in Regex.Matches(ReadWithoutComments(file), "FontFamily=\"([^\"]+)\""))
                {
                    string value = m.Groups[1].Value;
                    if (value.StartsWith("{", StringComparison.Ordinal)) continue;

                    offenders.Add($"{Relative(file)}: FontFamily=\"{value}\"");
                }
            }

            Assert.True(offenders.Count == 0,
                "Семья шрифта задана литералом — текст уйдёт на системный шрифт или глиф в tofu мимо токенов Font.Text/Font.Mono/Font.Icon:\n  " +
                string.Join("\n  ", offenders));
        }

        /// <summary>
        /// Токен <c>Font.Icon</c> обязан содержать ФОЛБЭК «Segoe MDL2 Assets»:
        /// «Segoe Fluent Icons» есть только на Windows 11, а продукт заявлен
        /// для Win10 — без фолбэка все 80+ глифов интерфейса рисуются пустым
        /// квадратом (реальный дефект <c>DialogService</c>, закрыт в этом же
        /// проходе). Порядок в строке-источнике кода держит тот же тест через
        /// <c>AppFontService</c>-константу.
        /// </summary>
        [Fact]
        public void IconFontToken_KeepsWin10Fallback()
        {
            foreach (var file in ProductXaml().Concat(new[] { TokensFile }))
            {
                if (!File.Exists(file)) continue;
                string text = ReadWithoutComments(file);
                foreach (Match m in Regex.Matches(text, "x:Key=\"Font.Icon\">([^<]+)</"))
                {
                    string value = m.Groups[1].Value;
                    Assert.True(value.Contains("Segoe Fluent Icons", StringComparison.Ordinal) &&
                                value.Contains("Segoe MDL2 Assets", StringComparison.Ordinal),
                        $"{Relative(file)}: Font.Icon = «{value}» — должен быть «Segoe Fluent Icons, Segoe MDL2 Assets» " +
                        "(второе имя — фолбэк Windows 10, без него глифы — пустые квадраты)");
                }
            }
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

            // Дробные размеры — отдельный, более строгий бюджет: при 100% DPI
            // они рисуются как СЛЕДУЮЩАЯ целая ступень (12.5 — как 13, замер в
            // TypographyTests.FractionalFontSize_RendersAsTheNextWholeStep),
            // то есть текст виден крупнее задуманного — GOTCHAS §26.
            var fractional = bad.Where(o => o.Value.Contains('.')).ToList();
            int fractionalAllowed = Budget().GetProperty("totals").GetProperty("fractionalFontSizes").GetInt32();
            Assert.True(fractional.Count <= fractionalAllowed,
                $"Дробных размеров шрифта стало {fractional.Count} (бюджет {fractionalAllowed}): " +
                string.Join(", ", fractional.Select(f => $"{f.File}={f.Value}")));
        }

        /// <summary>
        /// Шкала типографики: 9 (только монограммы бейджей и иконные глифы),
        /// 11 · 12 · 14 · 16 · 18 · 20 · 24 · 32 · 42. Всё прочее — долг, а не
        /// решение: дробный размер при 100% DPI даёт субпиксельную шкалу по
        /// вертикали и глиф выглядит «вытянутым», вне-шкальное целое
        /// (10/15/17/22/26) ломает ритм иерархии. Разбор — GOTCHAS §26.
        /// </summary>
        private static readonly double[] TypeScale = { 9, 11, 12, 14, 16, 18, 20, 24, 32, 42 };

        [Fact]
        public void FontSizes_LiveOnTheScale()
        {
            var offenders = new List<string>();

            foreach (var file in ProductXaml())
            {
                string text = ReadWithoutComments(file);
                var matches = FontSizeValue.Matches(text).Cast<Match>()
                    .Concat(FontSizeSetterValue.Matches(text).Cast<Match>());

                foreach (var match in matches)
                {
                    double value = double.Parse(match.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture);
                    if (!TypeScale.Contains(value))
                        offenders.Add($"{Relative(file)}: FontSize={match.Groups[1].Value}");
                }
            }

            Assert.True(offenders.Count == 0,
                "Размер шрифта вне шкалы (11·12·14·16·18·20·24·32·42, плюс 9 для " +
                "монограмм/иконок). Дробный размер «вытягивает» текст на 100% DPI, " +
                "вне-шкальное целое ломает иерархию:\n  " +
                string.Join("\n  ", offenders.Distinct().OrderBy(o => o)));
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
            var tokenRef = new Regex("\\{(?:Static|Dynamic)Resource\\s+((?:Window|Space|Gap|Pad|Radius|Type|Weight|Font|Motion|Ease)\\.[A-Za-z0-9.]+)\\}");
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

        /// <summary>
        /// Частичные радиусы обязаны повторять дугу своей карточки: шапка и
        /// подвал — те же углы, что у Card, только с одной стороны, а
        /// StripeLeft/StripeRight — зеркала друг друга. Иначе карточка и её шапка
        /// скругляются по-разному, и на стыке виден «ступенчатый» шов.
        /// </summary>
        [Fact]
        public void PartialRadiusTokens_MatchTheirCardEdges()
        {
            string radiusXaml = File.ReadAllText(Path.Combine(AppDir, "Themes", "Tokens.Radius.xaml"));

            double[] Token(string key)
            {
                var m = Regex.Match(radiusXaml,
                    $"<CornerRadius x:Key=\"Radius\\.{key}\">([0-9.,]+)</CornerRadius>");
                Assert.True(m.Success, $"в Tokens.Radius.xaml нет Radius.{key}");
                return m.Groups[1].Value.Split(',')
                    .Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture))
                    .ToArray();
            }

            // Порядок значений CornerRadius: TopLeft, TopRight, BottomRight, BottomLeft.
            double card = Token("Card")[0];

            Assert.Equal(new[] { card, card, 0, 0 }, Token("CardHeader"));
            Assert.Equal(new[] { 0, 0, card, card }, Token("CardFooter"));

            var left = Token("StripeLeft");
            var right = Token("StripeRight");
            // Зеркало по горизонтали: TopLeft ↔ TopRight, BottomLeft ↔ BottomRight.
            Assert.Equal(new[] { left[1], left[0], left[3], left[2] }, right);
            Assert.Equal(0, left[1]);                // правая кромка прямой полосы прямая
            Assert.Equal(0, left[2]);

            // Прямой угол — тоже токен: WindowChrome и плоские полосы не должны
            // возвращать в разметку литеральный ноль.
            Assert.All(Token("None"), v => Assert.Equal(0, v));
        }

        /// <summary>
        /// Радиусы в C# берутся из <c>Helpers/Radii</c> — те же токены Radius.*,
        /// что и в разметке. Иначе разметка живёт на шкале, а код задаёт свои
        /// числа на месте, и скругления расходятся (так и было в тостах и
        /// «Истории обновлений»).
        /// </summary>
        [Fact]
        public void CornerRadiusInCode_UsesTokens()
        {
            var codeRadius = new Regex(@"new\s+CornerRadius\s*\(\s*[0-9]", RegexOptions.Compiled);
            // Комментарии не код: в пояснениях к дизайну числа упоминаются как
            // примеры («new CornerRadius(2) в тостах»), и страж не должен ловить себя же.
            var commentLine = new Regex(@"^[ \t]*(?://|\*).*$", RegexOptions.Multiline);

            var offenders = ProductCs()
                .Where(f => codeRadius.IsMatch(commentLine.Replace(File.ReadAllText(f), string.Empty)))
                .Select(Relative)
                .OrderBy(f => f)
                .ToList();

            Assert.True(offenders.Count == 0,
                "Радиусы в коде — только через Helpers.Radii (токены Radius.*), иначе они вне бюджета:\n  " +
                string.Join("\n  ", offenders));
        }

        /// <summary>
        /// Семья шрифта в C# задаётся только через <c>AppFontService</c>
        /// (Inter/иконки/моно) или разрешена в ПЕЧАТНОМ слое: бумаги
        /// (<c>FlowDocumentBuilder</c>, <c>FixedDocumentBuilder</c>, <c>DrawingService</c>,
        /// QuestPDF-<c>PdfExportService</c>) Inter не касается — лист всегда белый,
        /// и шрифт печати не переключается темой. До этого прохода в UI-коде жили
        /// три места мимо токенов, одно из них (<c>DialogService</c>, глиф без
        /// MDL2-фолбэка) рисовало tofu на Windows 10. Комментарии не код.
        /// </summary>
        [Fact]
        public void FontFamiliesInCode_UseAppFontService_ExceptPrintLayer()
        {
            var familyLiteral = new Regex(@"new\s+(?:System\.Windows\.Media\.)?(?:FontFamily|Typeface)\s*\(\s*\""", RegexOptions.Compiled);
            var commentLine = new Regex(@"^[ \t]*(?://|\*).*$", RegexOptions.Multiline);
            var printLayer = new Regex(@"(?:DrawingService|FixedDocumentBuilder|FlowDocumentBuilder|PdfExportService)\.cs$", RegexOptions.Compiled);
            // AppFontService — источник строк-источников, там литералы и обязаны жить.
            var fontService = new Regex(@"AppFontService\.cs$", RegexOptions.Compiled);

            var offenders = ProductCs()
                .Where(f => !printLayer.IsMatch(f) && !fontService.IsMatch(f))
                .Where(f => familyLiteral.IsMatch(commentLine.Replace(File.ReadAllText(f), string.Empty)))
                .Select(Relative)
                .OrderBy(f => f)
                .ToList();            Assert.True(offenders.Count == 0,
                "Семья шрифта в коде — только через AppFontService (Inter/иконки/моно); литералы разрешены лишь в печатном слое " +
                "(FlowDocumentBuilder/FixedDocumentBuilder/DrawingService/PdfExportService):\n  " +
                string.Join("\n  ", offenders));
        }
    }
}
