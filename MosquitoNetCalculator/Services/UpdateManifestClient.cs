using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Downloads and deserializes the releases.json manifest from GitHub.
    ///
    /// Extracted from <see cref="UpdateService"/> (Phase 2 refactoring).
    /// Handles cache-busting, TLS configuration, and HTTP client lifecycle.
    ///
    /// Надёжность канала: раньше проверка делала ОДИН запрос к
    /// raw.githubusercontent.com — у части машин (другие офисы, провайдеры,
    /// фильтрующие шлюзы) канал рвётся или медленный, и проверка молча падала
    /// («Не удалось получить список обновлений» при ручной проверке; фоновые —
    /// вообще без сообщений). Теперь — последовательность попыток
    /// raw → api.github.com → повтор raw → jsDelivr с коротким замыканием
    /// на первом успехе:
    ///   • raw — основной канал (edge-CDN кэш ~5 мин, обходим cache-bust ?t=);
    ///   • api.github.com/contents — независимый канал: не кэшируется edge-CDN
    ///     и часто доступен там, где raw блокируется/тормозит провайдером
    ///     (тот же приём давно используется в диагностике RELEASE_PROCESS.md);
    ///   • повтор raw — на случай транзиентного сбоя;
    ///   • cdn.jsdelivr.net — последний рубеж: НЕ-GitHub CDN, отдаёт тот же
    ///     releases.json из ветки main и обычно доступен, когда провайдер
    ///     блокирует GitHub-домены ЦЕЛИКОМ (реальный сценарий: «для обновлений
    ///     нужен VPN», проверено — отдаёт идентичный манифест). Минус: jsDelivr
    ///     кэширует содержимое ветки — новый релиз через этот канал может
    ///     появиться с задержкой на часы, поэтому он именно последний.
    /// Каждая попытка возвращает <see cref="ManifestProbe"/> с человеческой
    /// причиной сбоя — она попадает в тост ручной проверки и в кнопку
    /// «Диагностика связи» (UpdatesTabControl).
    /// </summary>
    public static class UpdateManifestClient
    {
        private const string ManifestUrl =
            "https://raw.githubusercontent.com/DdepRest/arc-frame/main/releases.json";

        /// <summary>Запасной канал: contents API отдаёт releases.json без edge-кэша.</summary>
        private const string ApiContentsUrl =
            "https://api.github.com/repos/DdepRest/arc-frame/contents/releases.json";

        /// <summary>
        /// Последний рубеж: не-GitHub CDN jsDelivr, зеркало releases.json из
        /// ветки main. Работает, когда GitHub-домены заблокированы целиком
        /// (VPN-сценарий); кэширует содержимое ветки — новый релиз может
        /// появляться здесь с задержкой на часы, поэтому канал последний.
        /// </summary>
        private const string JsDelivrUrl =
            "https://cdn.jsdelivr.net/gh/DdepRest/arc-frame@main/releases.json";

        /// <summary>Таймаут ОДНОЙ попытки. Четыре попытки → худший случай ~40 с
        /// (блокировки обычно отваливаются быстро — реальный худший случай меньше).</summary>
        private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Build a cache-busted URL by appending a unique query parameter.
        /// raw.githubusercontent.com uses a 5-minute CDN edge cache;
        /// without this, users may fetch a stale manifest or hit a
        /// race where the manifest updated but the ZIP binary hasn't
        /// propagated to all CDN nodes yet.
        /// </summary>
        public static string CacheBustUrl(string url) =>
            url + "?t=" + DateTime.UtcNow.Ticks.ToString("x");

        /// <summary>
        /// Creates a production <see cref="HttpClient"/> with explicit TLS 1.2/1.3
        /// configuration. Some Windows environments (older .NET runtimes, corporate
        /// proxies, antivirus with SSL inspection) fail to negotiate TLS when the
        /// protocol is left at the OS default. Explicitly setting SslProtocols on
        /// the handler resolves "SSL connection" errors when fetching from GitHub.
        /// </summary>
        public static HttpClient CreateConfiguredHttpClient(TimeSpan timeout)
        {
            var handler = new HttpClientHandler
            {
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                    | System.Security.Authentication.SslProtocols.Tls13
            };
            var http = new HttpClient(handler) { Timeout = timeout };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MosquitoNetCalculator/3.0");
            return http;
        }

        /// <summary>Исход одной попытки: успех/человеческая причина/манифест.</summary>
        public sealed record ManifestProbe(bool Ok, string Detail, UpdateManifest? Manifest, long ElapsedMs);

        /// <summary>Итог: манифест (или null), причина последнего сбоя, список попыток.</summary>
        public sealed record ManifestFetchResult(
            UpdateManifest? Manifest, string? Error, IReadOnlyList<ManifestProbe> Attempts);

        /// <summary>
        /// Одна попытка основного канала raw.githubusercontent.com (с cache-bust).
        /// Публичная — используется кнопкой «Диагностика связи» и тестами.
        /// </summary>
        public static Task<ManifestProbe> ProbeRawAsync(HttpClient? httpClient = null)
            => ProbeUrlAsync(httpClient, CacheBustUrl(ManifestUrl), parseApiEnvelope: false);

        /// <summary>
        /// Одна попытка запасного канала api.github.com (contents API, base64).
        /// </summary>
        public static Task<ManifestProbe> ProbeApiAsync(HttpClient? httpClient = null)
            => ProbeUrlAsync(httpClient, ApiContentsUrl, parseApiEnvelope: true);

        /// <summary>
        /// Одна попытка последнего рубежа: jsDelivr (не-GitHub CDN, тот же файл
        /// из ветки main). Публичная — используется кнопкой «Диагностика связи».
        /// </summary>
        public static Task<ManifestProbe> ProbeJsDelivrAsync(HttpClient? httpClient = null)
            => ProbeUrlAsync(httpClient, JsDelivrUrl, parseApiEnvelope: false);

        /// <summary>
        /// Полная проверка: raw → api.github.com → повтор raw → jsDelivr,
        /// короткое замыкание на первом успехе. Возвращает манифест + причины
        /// всех попыток (для тоста ручной проверки и диагностики). Никогда не
        /// бросает исключений — при любом сбое манифест = null и заполнены
        /// причины попыток.
        /// </summary>
        public static async Task<ManifestFetchResult> FetchManifestDiagnosticsAsync(HttpClient? httpClient = null)
        {
            var attempts = new List<ManifestProbe>();
            foreach (var step in Steps())
            {
                var probe = await step().ConfigureAwait(false);
                attempts.Add(probe);
                if (probe.Ok)
                    return new ManifestFetchResult(probe.Manifest, null, attempts);
            }

            return new ManifestFetchResult(null, attempts[^1].Detail, attempts);

            IEnumerable<Func<Task<ManifestProbe>>> Steps()
            {
                yield return () => ProbeRawAsync(httpClient);
                yield return () => ProbeApiAsync(httpClient);
                yield return () => ProbeRawAsync(httpClient);
                yield return () => ProbeJsDelivrAsync(httpClient);
            }
        }

        /// <summary>
        /// Совместимая обёртка (старый контракт): манифест или null. Причины
        /// попыток доступны через <see cref="FetchManifestDiagnosticsAsync"/>.
        /// </summary>
        public static async Task<UpdateManifest?> FetchManifestAsync(HttpClient? httpClient = null)
        {
            var result = await FetchManifestDiagnosticsAsync(httpClient).ConfigureAwait(false);
            return result.Manifest;
        }

        // ─── Внутренности ────────────────────────────────────────────────────

        private static async Task<ManifestProbe> ProbeUrlAsync(
            HttpClient? httpClient, string url, bool parseApiEnvelope)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var ownsClient = httpClient == null;
                var http = httpClient ?? CreateConfiguredHttpClient(AttemptTimeout);
                try
                {
                    using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
                        .ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        return Fail($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}", sw);

                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var manifest = parseApiEnvelope ? ParseApiEnvelope(body) : TryDeserialize(body);
                    if (manifest == null)
                        return Fail(
                            parseApiEnvelope
                                ? "ответ api не распознан (нет content/битые данные)"
                                : "битый JSON",
                            sw);

                    return new ManifestProbe(
                        true,
                        $"HTTP {(int)response.StatusCode}, {sw.ElapsedMilliseconds} мс → v{manifest.Latest}, {manifest.Releases.Count} релизов",
                        manifest,
                        sw.ElapsedMilliseconds);
                }
                finally
                {
                    if (ownsClient) http.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                return Fail($"таймаут {AttemptTimeout.TotalSeconds:0} с", sw);
            }
            catch (HttpRequestException ex)
            {
                return Fail($"сеть: {ex.Message}", sw);
            }
            catch (Exception ex)
            {
                return Fail($"{ex.GetType().Name}: {ex.Message}", sw);
            }
        }

        private static ManifestProbe Fail(string reason, Stopwatch sw)
            => new(false, reason, null, sw.ElapsedMilliseconds);

        /// <summary>Парсит тело в манифест; битый JSON → null (без исключений).</summary>
        private static UpdateManifest? TryDeserialize(string json)
        {
            try { return JsonSerializer.Deserialize<UpdateManifest>(json); }
            catch (JsonException) { return null; }
        }

        /// <summary>
        /// Разбирает ответ contents API: { "content": "&lt;base64&gt;", "encoding": "base64" }.
        /// Для файлов &lt;1 МБ GitHub всегда отдаёт base64 (переводы строк внутри
        /// base64 допустимы — Convert.FromBase64String их игнорирует).
        /// </summary>
        internal static UpdateManifest? ParseApiEnvelope(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("content", out var content)
                    || content.ValueKind != JsonValueKind.String)
                    return null;

                var encoding = doc.RootElement.TryGetProperty("encoding", out var enc)
                    ? enc.GetString()
                    : null;
                if (!string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase))
                    return null;

                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(content.GetString() ?? ""));
                return TryDeserialize(decoded);
            }
            catch (Exception)
            {
                // JsonException (обёртка), FormatException (base64) — канал не годится.
                return null;
            }
        }
    }
}
