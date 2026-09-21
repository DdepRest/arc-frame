using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Страж ГОЛОСА пользовательской записи об обновлении.
    ///
    /// <para>
    /// <c>Resources/update-log.json</c> читает пользователь, а не разработчик: окно
    /// «Что нового» сразу после обновления и вкладка «Обновления». Технический разбор
    /// (имена файлов, токены, замеры в пикселях, счётчики тестов, ссылки на GOTCHAS)
    /// место жительства имеет другое — <c>CHANGELOG.md</c>. В v3.53.0 генератор
    /// <c>tools/release/generate-update-log.ps1</c> занёс в пользовательскую запись
    /// весь текст CHANGELOG, и в «Что нового» уехали 22 пункта с путями файлов,
    /// токенами и прогонами тестов.
    /// </para>
    ///
    /// <para>
    /// То же уехало и в записи 3.50.0–3.52.0 («Playtest-раунд», «Архитектурный проход»,
    /// прогоны тестов, `.tools/shots`): владелец попросил пройтись по всему, что
    /// написано после 3.49.0, — четыре записи переписаны на язык результата.
    /// Отдельным заходом тем же вычищен и сам 3.49.0: 5 пунктов на 4489 знаков с
    /// именами тестов, отступами в DIP/мм и счётчиком 2128/2128 — теперь 7 пунктов
    /// на 1595 знаков о том, что заказчик видит и трогает.
    /// </para>
    ///
    /// <para>
    /// Следом — весь ряд 3.48.x: 3.48.5 держал 149 пунктов на 44 821 знак (журнал
    /// разработки с именами модулей, TFM, счётчиками 2102/2102 и 1602/1602, утечкой
    /// встроенного админ-пароля в текст), 3.48.7 — 9 пунктов на 3987 знаков с
    /// XPS-сериализацией, `mirrorUrl` и внутренними константами. Теперь 13 пунктов
    /// на 3533 знака и 4 пункта на 942 знака; 3.48.3, 3.48.4 и 3.48.6 уже были
    /// пользовательскими — их не трогали.
    /// </para>
    ///
    /// <para>
    /// Правило (GOTCHAS §39): пользовательская запись отвечает на «что стало лучше
    /// для меня», а не на «что изменено в коде». Техжаргон — ошибка сборки.
    /// </para>
    /// </summary>
    public class UpdateLogVoiceTests
    {
        /// <summary>
        /// С какой версии действует правило — самая старая переписанная запись:
        /// 3.48.3 (записей 3.48.0–3.48.2 в журнале нет). Всё, что старше, —
        /// выпущенная история с текстом, который никто не переписывал: её не трогаем
        /// (append-only), но и это не повод ослаблять страж для новых версий.
        /// </summary>
        private static readonly Version Cutoff = new(3, 48, 3);

        /// <summary>
        /// Пользователь читает список, а не журнал: одна запись — это карточка
        /// в «Обновлениях» и пункты в «Что нового».
        /// </summary>
        private const int MaxBulletsPerEntry = 14;

        /// <summary>Абзац на пункт — это уже не список изменений (было до 2000 знаков).</summary>
        private const int MaxBulletChars = 480;

        private static readonly RegexOptions Opts =
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        /// <summary>
        /// Маркеры техжаргона. Список курируемый: он описывает НАБЛЮДЁННЫЕ способы
        /// утечки технического текста в пользовательскую запись, а не «все плохие
        /// слова»; при законном употреблении слово убирается отсюда осознанно.
        /// </summary>
        private static readonly (string Pattern, string Why)[] DevMarkers =
        {
            (@"`", "обратные кавычки: так в тексте помечают код"),
            (@"§", "ссылка на GOTCHAS"),
            (@"\bGOTCHAS\b", "ссылка на технический журнал"),
            (@"\.(cs|xaml|json|ps1|md|ttf|exe|sln|bat|csproj|manifest)\b", "имя файла проекта"),
            (@"\b[A-Z][A-Za-z0-9_]*\.[A-Z][A-Za-z0-9_]+\b", "идентификатор вида Radius.Card / Motion.Run"),
            (@"\bpx\b", "замер в пикселях: пользователю нужен итог, а не чернила"),
            (@"\b\d{3,}/\d{3,}\b", "счётчик прогона (2394/2394)"),
            (@"\b\d+\s*/\s*\d+\s+(pass|тест\w*|кадр\w*)\b", "счётчик прогона (17/17 кадров)"),
            (@"\bpass\b", "техжаргон: «pass»"),
            (@"#[0-9A-Fa-f]{3,8}\b", "hex-цвет"),
            (@"\b(WPF|XAML|Win32|GDI|HKCU|Storyboard|DynamicResource|StaticResource|CornerRadius|MinWidth|MaxWidth|Padding|Margin|FontSize|FontFamily|DataGrid|TextBlock|StackPanel|QuestPDF|RenderTargetBitmap|TimeSpan|Binding|Dispatcher|ToolTip|ContextMenu|Popup|Setter)\b",
                "класс/свойство платформы"),
            (@"\b(токен\w*|литерал\w*|ратчет\w*|бюджет\w*|харнесс\w*|базлайн\w*|тест\w*|сборк\w*|коммит\w*|рефактор\w*|рантайм\w*|оверлей\w*|биндинг\w*|билд\w*|ассерт\w*|страж\w*|паттерн\w*)\b",
                "техжаргон"),
        };

        // ── Стражи ───────────────────────────────────────────────────────

        [Fact]
        public void UserFacingEntries_HaveNoDevJargon()
        {
            var problems = new List<string>();

            foreach (var entry in EmbeddedEntries().Where(e => IsUserFacing(e.Version)))
            {
                problems.AddRange(JargonIn(entry.Version, $"заголовок «{entry.Title}»", entry.Title));
                for (int i = 0; i < entry.Changes.Count; i++)
                    problems.AddRange(JargonIn(entry.Version, $"пункт {i + 1}", entry.Changes[i]));
            }

            Assert.True(problems.Count == 0,
                "Запись об обновлении читает пользователь («Что нового» + вкладка «Обновления») — " +
                "технический разбор живёт в CHANGELOG.md (GOTCHAS §39).\n  " +
                string.Join("\n  ", problems));
        }

        [Fact]
        public void UserFacingEntries_StayShortAndReadable()
        {
            var problems = new List<string>();

            foreach (var entry in EmbeddedEntries().Where(e => IsUserFacing(e.Version)))
            {
                if (entry.Changes.Count > MaxBulletsPerEntry)
                    problems.Add($"v{entry.Version}: пунктов {entry.Changes.Count} (максимум {MaxBulletsPerEntry}) — " +
                                 "это карточка «Что нового», а не журнал изменений.");

                for (int i = 0; i < entry.Changes.Count; i++)
                {
                    if (entry.Changes[i].Length > MaxBulletChars)
                        problems.Add($"v{entry.Version}, пункт {i + 1}: {entry.Changes[i].Length} знаков " +
                                     $"(максимум {MaxBulletChars}) — пиши итог для заказчика, а разбор оставляй в CHANGELOG.");
                }
            }

            Assert.True(problems.Count == 0, string.Join("\n  ", problems));
        }

        /// <summary>
        /// То же правило для `releases.json`: его <c>changes</c> показывает диалог
        /// обновления перед установкой. Версии ниже <see cref="Cutoff"/> не трогаем —
        /// это уже выпущенная история.
        /// </summary>
        [Fact]
        public void ReleasesJson_UserFacingEntries_HaveNoDevJargon()
        {
            string path = Path.Combine(RepoRoot(), "releases.json");
            if (!File.Exists(path)) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("releases", out var releases)) return;

            var problems = new List<string>();
            foreach (var release in releases.EnumerateArray())
            {
                string version = release.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
                if (!IsUserFacing(version)) continue;

                if (release.TryGetProperty("title", out var t))
                    problems.AddRange(JargonIn(version, "заголовок", t.GetString() ?? ""));

                if (release.TryGetProperty("changes", out var ch) && ch.ValueKind == JsonValueKind.Array)
                {
                    int i = 0;
                    foreach (var bullet in ch.EnumerateArray())
                        problems.AddRange(JargonIn(version, $"пункт {++i}", bullet.GetString() ?? ""));
                }
            }

            Assert.True(problems.Count == 0,
                "releases.json читает пользователь в диалоге обновления — техжаргону там не место (GOTCHAS §39).\n  " +
                string.Join("\n  ", problems));
        }

        // ── Проверка пробой: страж не вакуумный ──────────────────────────

        /// <summary>
        /// Контроль на текст, который УЖЕ уехал в «Что нового» с 3.53.0 (вырезанные
        /// куски технического разбора). Если детектор перестанет их ловить, страж
        /// станет декоративным — этот тест упадёт первым.
        /// </summary>
        [Fact]
        public void Detector_CatchesTheOriginalV3530Text()
        {
            string leaked =
                "Дизайн-токены четырёх шкал: (`Themes/Tokens.Spacing|Radius|Typography|Motion.xaml`): " +
                "отступы 4/8/12/16/24/32/48; типографика 11…42 с базовым 12. " +
                "Радиусы в C# (тосты, «История обновлений») идут через новый `Helpers/Radii`. " +
                "Карточка → `Radius.Card`, полоса — `Radius.StripeInner`. " +
                "Разбор — GOTCHAS §27. Итого **2394/2394**, сборка 0 предупреждений, " +
                "шапка 36px против строки 26px, Margin=\"6,0\".";

            var found = Detect(leaked);

            var distinct = found.Select(f => f.Why).Distinct().ToList();
            Assert.True(distinct.Count >= 6,
                $"Детектор поймал только {distinct.Count} видов техжаргона из: {string.Join("; ", found)}");
            Assert.Contains(found, f => f.Found == "`");
            Assert.Contains(found, f => f.Found == "§");
            Assert.Contains(found, f => f.Found.Contains(".xaml", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(found, f => f.Found.Contains("Radius.", StringComparison.Ordinal));
        }

        /// <summary>
        /// Контрольная группа: нормальная пользовательская формулировка не должна
        /// падать на детекторе (иначе страж мешает работать, а не защищает).
        /// </summary>
        [Fact]
        public void Detector_KeepsUserFacingText()
        {
            foreach (string fine in new[]
            {
                "Адрес заказчика виден полностью: длинный адрес переносится по словам и больше не обрезается многоточием.",
                "Программа одинаково выглядит на Windows 10 и Windows 11 — нужный шрифт поставляется вместе с программой.",
                "Календарь поля «Дата» оформлен как остальная программа — раньше вместо него открывался белый системный квадрат.",
                "На том же экране помещается примерно на три позиции больше.",
            })
            {
                Assert.Empty(Detect(fine));
            }
        }

        // ── Внутренности ─────────────────────────────────────────────────

        private static bool IsUserFacing(string version)
            => Version.TryParse(version, out var v) && v >= Cutoff;

        private static IReadOnlyList<(string Found, string Why)> Detect(string text)
        {
            var found = new List<(string, string)>();
            foreach (var (pattern, why) in DevMarkers)
            {
                // Все вхождения одного вида — чтобы падение называло КАЖДЫЙ
                // найденный кусок, а не только первый.
                foreach (var value in Regex.Matches(text, pattern, Opts)
                             .Select(m => m.Value)
                             .Distinct(StringComparer.Ordinal)
                             .Take(5))
                {
                    found.Add((value, why));
                }
            }
            return found;
        }

        private static IEnumerable<string> JargonIn(string version, string where, string text)
        {
            foreach (var (found, why) in Detect(text))
            {
                string clip = text.Length <= 120 ? text : text[..120] + "…";
                yield return $"v{version}, {where}: «{found}» — {why}. Текст: {clip}";
            }
        }

        /// <summary>
        /// Записи читаются из embedded-ресурса напрямую (как в
        /// <c>UpdateLogTests.AppendOnly_NewEntryAppendedToEnd_PreservesOldRecords</c>),
        /// а не через <c>UpdateLog.AllNewestFirst()</c>: тот мутирует runtime-флаг
        /// <c>IsLatest</c> и мешал бы параллельным классам коллекции «UpdateLogState».
        /// </summary>
        private static IReadOnlyList<UpdateItem> EmbeddedEntries()
        {
            var assembly = typeof(UpdateLog).Assembly;
            using var stream = assembly.GetManifestResourceStream("MosquitoNetCalculator.Resources.update-log.json");
            Assert.NotNull(stream);

            using var reader = new StreamReader(stream!);
            var items = JsonSerializer.Deserialize<UpdateItem[]>(reader.ReadToEnd(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(items);
            Assert.NotEmpty(items!);
            return items!;
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "MosquitoNetCalculator.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Не найден корень репозитория (MosquitoNetCalculator.sln).");
        }
    }
}
