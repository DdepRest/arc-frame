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
    /// Данные загружаются из embedded-ресурса <c>Resources/update-log.json</c>.
    /// Чтобы добавить новую версию — отредактируйте JSON-файл (допишите запись
    /// в конец массива). Порядок в JSON не важен: <c>AllNewestFirst()</c>
    /// сортирует по дате (desc), затем по версии (desc) при совпадении дат.
    ///
    /// Контракт тестов (UpdateLogTests.AllNewestFirst_*):
    ///   items[0] = версия с самой свежей датой (при равенстве — старший Version).
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
        /// v3.37.2 (GOTCHAS#14 follow-up): предыдущая реализация полагалась
        /// на <c>.Reverse()</c> порядка записей в JSON. Когда при релизе v3.37.1
        /// запись была вручную вставлена в начало (вместо дописывания в конец),
        /// oldest-first контракт нарушился — <c>Reverse()</c> вернул неверный порядок.
        /// Теперь сортировка в коде: порядок файла не имеет значения.
        /// </summary>
        public static ObservableCollection<UpdateItem> AllNewestFirst()
        {
            var sorted = _entries.Value
                .OrderByDescending(e => e.Date)
                .ThenByDescending(e => ParseVersion(e.Version) ?? new Version(0, 0))
                .ToArray();
            return new ObservableCollection<UpdateItem>(sorted);
        }

        /// <summary>
        /// Возвращает записи changelog для всех версий строго newerThan <paramref name="currentVersion"/>.
        /// Результат отсортирован от старой версии к новой (хронологический порядок).
        /// Используется для показа пропущенных изменений в диалоге обновления.
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
    }
}
