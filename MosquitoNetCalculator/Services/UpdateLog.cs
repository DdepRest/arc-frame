using System;
using System.Collections.ObjectModel;
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
    /// в конец массива). UI автоматически покажет её первой (обратный порядок).
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
        /// Это то, что биндится в UI: индекс 0 — самая свежая версия.
        /// Возвращается новая коллекция — мутации безопасны.
        /// </summary>
        public static ObservableCollection<UpdateItem> AllNewestFirst()
        {
            return new ObservableCollection<UpdateItem>(_entries.Value.AsEnumerable().Reverse());
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
                .ToArray();
        }

        /// <summary>
        /// Парсит строку версии (например, "3.36.2") в <see cref="Version"/>.
        /// Возвращает <c>null</c> при ошибке парсинга.
        /// </summary>
        private static Version? ParseVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return null;
            try { return new Version(version); }
            catch { return null; }
        }
    }
}
