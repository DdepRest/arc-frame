using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Обмен отчётами офисов через секретный GitHub Gist — «облачная папка»
    /// без клиента синхронизации: каждый офис при старте тихо обновляет СВОЙ
    /// файл <c>office-{prefix}.json</c> (PATCH gist), админ-панель читает весь
    /// gist и строит таблицу статусов.
    ///
    /// Gist создан владельцем:
    ///   https://gist.github.com/DdepRest/6d6ff7389efc44f7aa6e57361a55ee24
    ///
    /// Офисы не выполняют НИКАКИХ действий (без аккаунтов, без установок) —
    /// всё делает сама программа, один тихий запрос при старте.
    ///
    /// Токен встраивается в сборку при релизе (см. <see cref="CompiledToken"/>);
    /// для отладки/тестов его можно переопределить через settings.json
    /// (AppSettingsService.SaveOfficeReportToken) — это же позволяет сменить
    /// токен без пересборки.
    /// </summary>
    public static class OfficeReportService
    {
        /// <summary>ID секретного gist с отчётами офисов.</summary>
        public const string GistId = "6d6ff7389efc44f7aa6e57361a55ee24";

        /// <summary>Владелец gist (аккаунт, под которым создан gist).</summary>
        public const string GistOwner = "DdepRest";

        /// <summary>
        /// Порог «мёртвого» файла-дубля для АВТОочистки: если последний отчёт
        /// файла старше этого срока, он удаляется автоматически при обновлении
        /// админ-панели (<see cref="CleanupStaleDuplicatesAsync"/>). Свежие дубли
        /// не трогаются: две живые копии программы на одном ПК (обычная + dev)
        /// шлют отчёты в свои файлы, и удаление свежего файла дало бы пинг-понг
        /// «удалил → воссоздал» в истории gist.
        /// </summary>
        public static readonly TimeSpan StaleDuplicateAfter = TimeSpan.FromHours(24);

        /// <summary>
        /// Актуальный токен: переопределение из settings.json (для отладки/тестов)
        /// или токен, встроенный в сборку при компиляции (см. csproj target
        /// GenerateOfficeReportToken: env OFFICE_REPORT_TOKEN или локальный файл
        /// .office-report-token, в git не попадает — репозиторий публичный).
        /// Источник токена — сгенерированный класс <c>OfficeReportTokenSource</c>.
        /// </summary>
        public static string GistToken
        {
            get
            {
                var fromSettings = AppSettingsService.LoadOfficeReportToken();
                return string.IsNullOrWhiteSpace(fromSettings) ? OfficeReportTokenSource.Token : fromSettings;
            }
        }

        /// <summary>
        /// Актуальный ID gist: переопределение из settings.json (миграция хранилища
        /// без пересборки) или скомпилированная константа.
        /// </summary>
        public static string ResolvedGistId
        {
            get
            {
                var fromSettings = AppSettingsService.LoadOfficeReportGistId();
                return string.IsNullOrWhiteSpace(fromSettings) ? GistId : fromSettings;
            }
        }

        /// <summary>
        /// True, когда токен реально доступен (из сборки или settings.json).
        /// Пока false — отчёты не отправляются, панель показывает предупреждение.
        /// </summary>
        public static bool IsConfigured => !string.IsNullOrWhiteSpace(GistToken);

        private static string GistApiUrl => $"https://api.github.com/gists/{ResolvedGistId}";

        /// <summary>
        /// Имя файла отчёта устройства в gist: по файлу на устройство — в одном
        /// офисе может быть несколько ПК, они не перетирают друг друга.
        /// Для легаси-отчётов (без deviceId) — старый формат по офису.
        /// </summary>
        public static string ReportFileName(string prefix, string deviceId)
            => string.IsNullOrWhiteSpace(deviceId)
                ? $"office-{prefix}.json"
                : $"office-{prefix}-{deviceId}.json";

        /// <summary>
        /// Кол-во заказов в программе на этом ПК (файлы *.json в папке заказов).
        /// При любой ошибке — 0 (отчёт всё равно уходит).
        /// </summary>
        private static int CountOrders()
        {
            try
            {
                var dir = OrderStorageService.OrdersDir;
                return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json").Length : 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OfficeReport] count orders failed: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Тихий PATCH отчёта текущего устройства в gist. Вызывается при старте
        /// программы, при каждой проверке обновлений и планировщиком каждые 30 мин —
        /// без входа в админ-панель. Всегда завершается без исключений —
        /// при любой ошибке просто возвращает false.
        /// </summary>
        public static async Task<bool> SendReportAsync(HttpClient? httpClient = null)
        {
            if (!IsConfigured) return false;

            var deviceId = AppSettingsService.LoadOrCreateDeviceId();
            var report = new OfficeReport
            {
                Prefix = AppSettingsService.LoadContractPrefix(),
                LocationName = AppSettingsService.LoadLocationName(),
                DeviceId = deviceId,
                DeviceName = SafeMachineName(),
                Version = UpdateService.CurrentVersion.ToString(),
                ReportedAt = DateTimeOffset.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                OrderCount = CountOrders(),
            };

            var body = JsonSerializer.Serialize(new
            {
                files = new Dictionary<string, object>
                {
                    [ReportFileName(report.Prefix, report.DeviceId)] = new { content = JsonSerializer.Serialize(report) },
                },
            });

            try
            {
                var ownsClient = httpClient == null;
                var http = httpClient ?? UpdateManifestClient.CreateConfiguredHttpClient(TimeSpan.FromSeconds(15));
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Patch, GistApiUrl);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GistToken);
                    request.Headers.UserAgent.ParseAdd("MosquitoNetCalculator/3.0");
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    var response = await http.SendAsync(request).ConfigureAwait(false);
                    return response.IsSuccessStatusCode;
                }
                finally
                {
                    if (ownsClient) http.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OfficeReport] send failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Имя машины для отчёта; при ошибке — пустая строка.</summary>
        private static string SafeMachineName()
        {
            try { return Environment.MachineName; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OfficeReport] machine name failed: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Читает отчёты всех офисов из gist. Возвращает пустой список при любой
        /// ошибке (сеть, токен, битый JSON) — панель показывает «нет связи».
        /// </summary>
        public static async Task<IReadOnlyList<OfficeReport>> FetchReportsAsync(HttpClient? httpClient = null)
        {
            var files = await FetchReportFilesAsync(httpClient);
            return files.Select(f => f.Report).ToList();
        }

        /// <summary>
        /// Читает из gist файлы отчётов ВМЕСТЕ С ИХ ИМЕНАМИ (имя нужно для
        /// удаления дублей). Возвращает пустой список при любой ошибке.
        /// </summary>
        public static async Task<IReadOnlyList<OfficeReportFile>> FetchReportFilesAsync(HttpClient? httpClient = null)
        {
            if (!IsConfigured) return Array.Empty<OfficeReportFile>();

            try
            {
                var ownsClient = httpClient == null;
                var http = httpClient ?? UpdateManifestClient.CreateConfiguredHttpClient(TimeSpan.FromSeconds(15));
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, GistApiUrl);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GistToken);
                    request.Headers.UserAgent.ParseAdd("MosquitoNetCalculator/3.0");
                    var response = await http.SendAsync(request).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) return Array.Empty<OfficeReportFile>();
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return ParseReportFiles(json);
                }
                finally
                {
                    if (ownsClient) http.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OfficeReport] fetch failed: {ex.Message}");
                return Array.Empty<OfficeReportFile>();
            }
        }

        /// <summary>
        /// Парсит ответ GitHub API <c>GET /gists/{id}</c> (содержимое файлов в
        /// <c>files.*.content</c>) в список отчётов. Битые/не-наши файлы пропускаются.
        /// Отделено от сети — покрыто юнит-тестами.
        /// </summary>
        public static IReadOnlyList<OfficeReport> ParseReports(string gistJson)
            => ParseReportFiles(gistJson).Select(f => f.Report).ToList();

        /// <summary>
        /// Парсит ответ gist в список «имя файла → отчёт» (имя файла нужно
        /// для удаления дублей устройств). Отделено от сети — покрыто тестами.
        /// </summary>
        public static IReadOnlyList<OfficeReportFile> ParseReportFiles(string gistJson)
        {
            var result = new List<OfficeReportFile>();
            try
            {
                using var doc = JsonDocument.Parse(gistJson);
                if (!doc.RootElement.TryGetProperty("files", out var files))
                    return result;

                foreach (var prop in files.EnumerateObject())
                {
                    if (!prop.Name.StartsWith("office-", StringComparison.OrdinalIgnoreCase)
                        || !prop.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!prop.Value.TryGetProperty("content", out var content)
                        || content.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    try
                    {
                        var report = JsonSerializer.Deserialize<OfficeReport>(content.GetString() ?? "{}");
                        if (report != null) result.Add(new OfficeReportFile(prop.Name, report));
                    }
                    catch (JsonException)
                    {
                        // Битый файл — пропускаем, панель покажет по офису «нет данных».
                    }
                }
            }
            catch (JsonException)
            {
                // Весь ответ битый — вернём пустой список.
            }

            return result;
        }

        /// <summary>
        /// Вычисляет, какие файлы в gist — ДУБЛИ устройств (их нужно удалить):
        /// несколько файлов одного ПК (одинаковое имя машины — обычная версия + dev)
        /// и легаси-файлы <c>office-{{prefix}}.json</c> при наличии именованных устройств.
        /// Оставляются ровно те файлы, которые показывает панель
        /// (<see cref="OfficeDeviceGrouping.DistinctDevices"/>). Чистая функция — покрыта тестами.
        /// </summary>
        internal static IReadOnlyList<string> ComputeDuplicateFilesToDelete(IReadOnlyList<OfficeReportFile> files)
        {
            var toDelete = new List<string>();
            foreach (var prefixGroup in files.GroupBy(f => f.Report.Prefix))
            {
                var prefixFiles = prefixGroup.ToList();
                var kept = OfficeDeviceGrouping.DistinctDevices(prefixFiles.Select(f => f.Report)).ToHashSet();
                foreach (var file in prefixFiles)
                {
                    if (!kept.Contains(file.Report))
                        toDelete.Add(file.FileName);
                }
            }
            return toDelete;
        }

        /// <summary>
        /// Удаляет из gist файлы-дубли устройств (кнопка «Очистить дубли» в панели):
        /// остаются только новейшие файлы каждого ПК офиса. Возвращает количество
        /// удалённых файлов; -1 при ошибке (нет токена, нет связи, ошибка API).
        /// Удаление — PATCH gist с <c>content: null</c> для лишних имён.
        /// </summary>
        public static async Task<int> CleanupDuplicatesAsync(HttpClient? httpClient = null)
        {
            if (!IsConfigured) return -1;

            try
            {
                var files = await FetchReportFilesAsync(httpClient);
                var toDelete = ComputeDuplicateFilesToDelete(files);
                if (toDelete.Count == 0) return 0;

                return await DeleteGistFilesAsync(toDelete, httpClient).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OfficeReport] cleanup duplicates failed: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Вычисляет, какие файлы-дубли устройств УСТАРЕЛИ и их можно удалить
        /// автоматически: пересечение <see cref="ComputeDuplicateFilesToDelete"/>
        /// (дубли, которых панель не показывает) с файлами, чей последний отчёт
        /// старше <paramref name="staleAfter"/>. Отчёт без читаемой даты считается
        /// устаревшим (мёртвая заглушка). Чистая функция — покрыта тестами.
        /// </summary>
        internal static IReadOnlyList<string> ComputeStaleDuplicateFilesToDelete(
            IReadOnlyList<OfficeReportFile> files, DateTimeOffset nowUtc, TimeSpan staleAfter)
        {
            var staleCutoff = nowUtc - staleAfter;
            var toDelete = new List<string>();
            foreach (var fileName in ComputeDuplicateFilesToDelete(files))
            {
                var reportedAt = files.FirstOrDefault(f => f.FileName == fileName)?.Report.ReportedAtUtc;
                if (reportedAt == null || reportedAt.Value < staleCutoff)
                    toDelete.Add(fileName);
            }
            return toDelete;
        }

        /// <summary>
        /// Тихая АВТОочистка gist при обновлении админ-панели: удаляет только
        /// файлы-дубли, молчащие дольше <see cref="StaleDuplicateAfter"/> — старые
        /// файлы устройств (deviceId сменился) и легаси-записи при наличии
        /// именованных. Живые дубли (две копии на одном ПК) и не-дубли (например,
        /// легаси-файл офиса без именованных устройств) не трогаются. Возвращает
        /// количество удалённых файлов; -1 при ошибке (нет токена, нет связи,
        /// ошибка API). Никогда не бросает исключений.
        /// </summary>
        public static async Task<int> CleanupStaleDuplicatesAsync(HttpClient? httpClient = null)
        {
            if (!IsConfigured) return -1;

            try
            {
                var files = await FetchReportFilesAsync(httpClient);
                var toDelete = ComputeStaleDuplicateFilesToDelete(files, DateTimeOffset.UtcNow, StaleDuplicateAfter);
                if (toDelete.Count == 0) return 0;

                return await DeleteGistFilesAsync(toDelete, httpClient).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OfficeReport] stale-duplicate cleanup failed: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// PATCH gist с <c>content: null</c> для перечисленных имён — файлы
        /// удаляются, остальные не трогаются. Возвращает количество удалённых
        /// файлов; -1 при ошибке сети/API.
        /// </summary>
        private static async Task<int> DeleteGistFilesAsync(IReadOnlyList<string> fileNames, HttpClient? httpClient)
        {
            if (fileNames.Count == 0) return 0;

            var body = JsonSerializer.Serialize(new
            {
                files = fileNames.ToDictionary(name => name, _ => (object?)null),
            });

            var ownsClient = httpClient == null;
            var http = httpClient ?? UpdateManifestClient.CreateConfiguredHttpClient(TimeSpan.FromSeconds(15));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Patch, GistApiUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GistToken);
                request.Headers.UserAgent.ParseAdd("MosquitoNetCalculator/3.0");
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await http.SendAsync(request).ConfigureAwait(false);
                return response.IsSuccessStatusCode ? fileNames.Count : -1;
            }
            finally
            {
                if (ownsClient) http.Dispose();
            }
        }
    }

    /// <summary>Файл отчёта в gist: имя файла + распарсенный отчёт (имя нужно для удаления дублей).</summary>
    public sealed record OfficeReportFile(string FileName, OfficeReport Report);
}
