using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Единый источник истории обновлений приложения.
    ///
    /// <para>
    /// <b>Контракт append-only:</b> данные читаются из embedded-ресурса
    /// <c>Resources/update-log.json</c>. Чтобы добавить новую версию —
    /// допишите запись в КОНЕЦ массива JSON. Существующие записи править
    /// не нужно: позиция в массиве не имеет значения, актуальный порядок
    /// для UI вычисляется runtime по (Date desc, Version desc).
    /// </para>
    ///
    /// <para>
    /// Признак «новейшей версии» хранится в <see cref="UpdateItem.IsLatest"/>
    /// и устанавливается в <see cref="AllNewestFirst"/>. Это in-memory
    /// свойство (с <see cref="System.Text.Json.Serialization.JsonIgnoreAttribute"/>),
    /// которое не записывается в JSON — добавление новой записи в КОНЕЦ
    /// массива не требует редактирования старых, что и было целью архитектурного
    /// рефакторинга блока «Обновления» (см. дизайн-план в CURRENT_STATE/CALCULATION).
    /// </para>
    ///
    /// <para>
    /// <b>Контракт тестов (UpdateLogTests):</b><br/>
    /// — <c>items[0].IsLatest == true</c> и это элемент с максимальной Date,
    /// при равенстве дат — с максимальной Version.<br/>
    /// — <c>items[i].IsLatest == false</c> для всех i ≥ 1.<br/>
    /// — Дубликаты Version недопустимы — ловится <c>ValidateLogInvariant</c>.
    /// </para>
    /// </summary>
    public static class UpdateLog
    {
        private static readonly Lazy<UpdateItem[]> _entries = new(LoadEntries);

        private static UpdateItem[] LoadEntries()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "MosquitoNetCalculator.Resources.update-log.json";

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException(
                        "update-log.json not found — embedded resource missing.");

                using (var reader = new StreamReader(stream))
                {
                    string json = reader.ReadToEnd();
                    var items = JsonSerializer.Deserialize<UpdateItem[]>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return items ?? Array.Empty<UpdateItem>();
                }
            }
        }

        /// <summary>
        /// Возвращает все записи в порядке «от новых к старым».
        /// Сортировка: date descending → version descending (для записей с одинаковой датой).
        /// Это то, что биндится в UI: индекс 0 — самая свежая версия.
        /// Возвращается новая коллекция — мутации безопасны.
        ///
        /// <para>
        /// В процессе сортировки вычисляется признак <see cref="UpdateItem.IsLatest"/>:
        /// — true ровно для одной (самой свежей) записи;<br/>
        /// — false для всех остальных.
        /// Это позволяет XAML привязывать бейдж «Новейшая» к свойству
        /// <c>IsLatest</c> вместо позиции в коллекции — добавление новой
        /// записи в runtime не сдвигает позиции старых.
        /// </para>
        ///
        /// <para>
        /// v3.37.2 (GOTCHAS#14 follow-up): предыдущая реализация полагалась
        /// на <c>.Reverse()</c> порядка записей в JSON. Когда при релизе v3.37.1
        /// запись была вручную вставлена в начало (вместо дописывания в конец),
        /// oldest-first контракт нарушился — <c>Reverse()</c> вернул неверный порядок.
        /// Теперь порядок в файле не имеет значения: сортировка в коде по дате/версии.
        /// </para>
        /// </summary>
        public static ObservableCollection<UpdateItem> AllNewestFirst()
        {
            var items = _entries.Value;
            var sorted = items
                .OrderByDescending(e => e.Date)
                .ThenByDescending(e => ParseVersion(e.Version) ?? new Version(0, 0))
                .ToArray();

            // Reset all IsLatest to false first, then mark exactly one as true.
            // Needed because _entries is cached (Lazy<>) — repeated calls share
            // the same UpdateItem[] under the hood.
            foreach (var e in items)
                e.IsLatest = false;
            if (sorted.Length > 0)
                sorted[0].IsLatest = true;

            return new ObservableCollection<UpdateItem>(sorted);
        }

        /// <summary>
        /// Возвращает записи changelog для всех версий строго newerThan <paramref name="currentVersion"/>.
        /// Результат отсортирован от старой версии к новой (хронологический порядок).
        /// Используется для показа пропущенных изменений в диалоге обновления.
        ///
        /// <para>
        /// <see cref="UpdateItem.IsLatest"/> для возвращённых элементов НЕ
        /// выставляется — это не UI-коллекция, а данные для диалога. Этот факт
        /// остаётся согласованным с AllNewestFirst, потому что исходный массив
        /// общий и после первого вызова AllNewestFirst() ровно один элемент имеет
        /// IsLatest=true; эта функция чтения не меняет состояние.
        /// </para>
        /// </summary>
        public static UpdateItem[] GetChangesSince(Version currentVersion)
        {
            return _entries.Value
                .Where(e => ParseVersion(e.Version) > currentVersion)
                .OrderBy(e => e.Date)
                .ThenBy(e => ParseVersion(e.Version) ?? new Version(0, 0))
                .ToArray();
        }

        /// <summary>
        /// Парсит строку версии (например, "3.36.2") в <see cref="Version"/>.
        /// Возвращает <c>null</c> при ошибке парсинга.
        /// Пишет <c>Debug.WriteLine</c> при сбое — опечатка в JSON видна в отладчике.
        /// </summary>
        private static Version? ParseVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return null;
            try { return new Version(version); }
            catch
            {
                Debug.WriteLine($"[UpdateLog] Failed to parse version '{version}', treating as null");
                return null;
            }
        }

        /// <summary>
        /// Проверяет append-only инвариант загруженных записей:
        /// — все Version парсятся в System.Version;
        /// — Version не пустое;
        /// — нет дубликатов Version;
        /// — IsLatest выставлен ровно для одной записи (постусловие AllNewestFirst).
        /// </summary>
        /// <remarks>
        /// Используется в unit-тестах и в diagnose-builds. Не бросает исключения —
        /// возвращает список проблем (пустой список = инвариант соблюдён).
        /// </remarks>
        public static System.Collections.Generic.List<string> ValidateLogInvariant()
        {
            var problems = new System.Collections.Generic.List<string>();
            var items = _entries.Value;

            foreach (var e in items)
            {
                if (string.IsNullOrWhiteSpace(e.Version))
                {
                    problems.Add($"Empty Version found (Title=\"{e.Title}\")");
                    continue;
                }
                if (ParseVersion(e.Version) == null)
                {
                    problems.Add($"Unparseable Version: \"{e.Version}\" (Title=\"{e.Title}\")");
                }
            }

            // Duplicates by Version — Ordinal (case-sensitive) on purpose.
            // Version strings are machine-generated (CI/copy-paste from
            // CHANGELOG.md); casing carries no semantic meaning here, but a
            // typo like "3.40O.0" vs "3.40.0" should NOT silently merge.
            // StringComparer.Ordinal catches it; OrdinalIgnoreCase wouldn't.
            var dupes = items
                .GroupBy(e => e.Version, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);
            foreach (var v in dupes)
                problems.Add($"Duplicate Version: \"{v}\" ({items.Count(e => e.Version == v)} times)");

            return problems;
        }
    }
}
