using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Stage-3 (REFACTORING_PLAN_BIG_FILES.md §4 Фаза B#3):
    /// API-key probe and provider-key/URL helpers extracted from
    /// <see cref="AiAssistantService"/>. The service delegates here for
    /// both the user-driven «Test API key» dialog action and the
    /// internal routing decisions (which provider serves which model).
    /// </summary>
    public static class AiKeyValidator
    {
        // ── Endpoint URLs (extracted verbatim) ─────────────────────────
        internal const string OpenRouterApiUrl = "https://openrouter.ai/api/v1/chat/completions";
        internal const string NvidiaApiUrl = "https://integrate.api.nvidia.com/v1/chat/completions";
        internal const string OpenRouterAuthKeyUrl = "https://openrouter.ai/api/v1/auth/key";

        /// <summary>Default FreeLLMAPI base URL when the user leaves the field empty.</summary>
        internal const string DefaultFreeLlmApiEndpoint = "http://localhost:3001/v1";

        private const string ChatCompletionsSuffix = "/chat/completions";

        // ── FreeLLMAPI endpoint normalization ─────────────────────────
        // The settings field holds a BASE url (e.g. http://127.0.0.1:31415 —
        // the Desktop build's port). The gateway (server and Desktop) mounts
        // its OpenAI-compatible API under /v1, and — critically — answers ANY
        // path outside /api/, /v1/ and /v1beta/ with HTTP 200 + its dashboard
        // web page. So a base without /v1 does not fail loudly: every request
        // "succeeds" with an HTML body, the SSE parser reads zero tokens and
        // the chat reports «модели вернули пустой ответ» while the connection
        // test happily says «шлюз доступен». All URL construction goes through
        // these helpers so the /v1 prefix can never be silently lost again.

        /// <summary>
        /// Builds the OpenAI-compatible chat-completions URL from a user-supplied
        /// FreeLLMAPI endpoint. A bare host gets <c>/v1</c> appended; an explicit
        /// <c>…/v1</c>, <c>…/v1beta</c> or <c>…/chat/completions</c> URL is
        /// respected as-is (escape hatch for custom mounts).
        /// </summary>
        public static string BuildFreeLlmApiChatUrl(string? rawEndpoint)
        {
            var ep = NormalizeFreeLlmApiBase(rawEndpoint);
            return ep.EndsWith(ChatCompletionsSuffix, StringComparison.OrdinalIgnoreCase)
                ? ep
                : ep + ChatCompletionsSuffix;
        }

        /// <summary>Model-catalog (<c>/v1/models</c>) URL for the same endpoint.</summary>
        public static string BuildFreeLlmApiModelsUrl(string? rawEndpoint)
        {
            var ep = NormalizeFreeLlmApiBase(rawEndpoint);
            return ep.EndsWith(ChatCompletionsSuffix, StringComparison.OrdinalIgnoreCase)
                ? ep[..^ChatCompletionsSuffix.Length] + "/models"
                : ep + "/models";
        }

        /// <summary>
        /// Trimmed base URL with the API prefix guaranteed: strips a pasted
        /// <c>/models</c> or <c>/chat/completions</c> suffix, then appends
        /// <c>/v1</c> unless the URL already carries <c>/v1</c> / <c>/v1beta</c>.
        /// </summary>
        internal static string NormalizeFreeLlmApiBase(string? rawEndpoint)
        {
            var ep = (rawEndpoint ?? string.Empty).Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(ep)) return DefaultFreeLlmApiEndpoint;

            if (ep.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
                ep = ep[..^"/models".Length];
            if (ep.EndsWith(ChatCompletionsSuffix, StringComparison.OrdinalIgnoreCase))
                return ep;
            if (ep.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                || ep.EndsWith("/v1beta", StringComparison.OrdinalIgnoreCase))
                return ep;

            return ep + "/v1";
        }

        /// <summary>
        /// True when a body is obviously a web page rather than an API payload
        /// (the gateway dashboard answers unknown paths with 200 + index.html).
        /// </summary>
        internal static bool LooksLikeWebPage(string? body)
            => !string.IsNullOrWhiteSpace(body)
               && body.TrimStart().StartsWith("<", StringComparison.Ordinal);

        /// <summary>
        /// Lightweight API key ping. Hits <c>/auth/key</c> for OpenRouter
        /// (real auth check) and <c>/v1/models</c> for NVIDIA (best probe —
        /// NVIDIA does not enforce auth on /models). 2xx → success;
        /// 401/403 → bad key; other 4xx/5xx → partial; network error → false.
        /// Result is safe to consume from the UI thread (no exceptions thrown).
        /// </summary>
        public static async Task<AiApiKeyTestResult> TestApiKeyAsync(
            AiProvider provider,
            string apiKey,
            HttpClient httpClient,
            CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(8));

                HttpResponseMessage response;
                if (provider == AiProvider.Nvidia)
                {
                    // NVIDIA: ping chat/completions with minimal payload to reliably test authorization
                    using var request = new HttpRequestMessage(HttpMethod.Post, NvidiaApiUrl);
                    if (!string.IsNullOrWhiteSpace(apiKey))
                        request.Headers.Add("Authorization", $"Bearer {apiKey.Trim()}");
                    var payload = new
                    {
                        model = "nvidia/llama-3.1-nemotron-70b-instruct",
                        messages = new[] { new { role = "user", content = "ping" } },
                        max_tokens = 1
                    };
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(payload),
                        Encoding.UTF8,
                        "application/json");

                    response = await httpClient.SendAsync(request, cts.Token);
                }
                else if (provider == AiProvider.FreeLlmApi)
                {
                    // FreeLLMAPI: an honest two-stage check (catalog + live chat) —
                    // see TestFreeLlmApiAsync. A bare 2xx is NOT a health signal:
                    // the gateway answers a URL without /v1 with 200 + HTML.
                    return await TestFreeLlmApiAsync(apiKey, httpClient, sw, ct);
                }
                else
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, OpenRouterAuthKeyUrl);
                    if (!string.IsNullOrWhiteSpace(apiKey))
                        request.Headers.Add("Authorization", $"Bearer {apiKey.Trim()}");
                    response = await httpClient.SendAsync(request, cts.Token);
                }

                using (response)
                {
                    sw.Stop();
                    int statusCode = (int)response.StatusCode;
                    if (response.IsSuccessStatusCode)
                    {
                        return new AiApiKeyTestResult(
                            IsOk: true,
                            StatusCode: statusCode,
                            LatencyMs: (int)sw.ElapsedMilliseconds,
                            Detail: "OK");
                    }

                    string snippet = statusCode is 401 or 403
                        ? "неверный или просроченный ключ"
                        : statusCode == 429
                            ? "превышен лимит запросов"
                            : statusCode == 404 && provider == AiProvider.FreeLlmApi
                                ? "эндпоинт не найден (проверьте URL)"
                                : $"HTTP {statusCode}";
                    return new AiApiKeyTestResult(
                        IsOk: false,
                        StatusCode: statusCode,
                        LatencyMs: (int)sw.ElapsedMilliseconds,
                        Detail: snippet);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException)
            {
                sw.Stop();
                return new AiApiKeyTestResult(false, 0, (int)sw.ElapsedMilliseconds, "таймаут > 8 с");
            }
            catch (HttpRequestException ex)
            {
                sw.Stop();
                string msg = provider == AiProvider.FreeLlmApi
                    ? "сервер недоступен (проверьте запуск FreeLLMAPI)"
                    : $"ошибка сети: {ex.GetType().Name}";
                return new AiApiKeyTestResult(false, 0, (int)sw.ElapsedMilliseconds, msg);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new AiApiKeyTestResult(false, 0, (int)sw.ElapsedMilliseconds, $"ошибка сети: {ex.GetType().Name}");
            }
        }

        /// <summary>
        /// Honest FreeLLMAPI check: stage 1 requires <c>/v1/models</c> to answer
        /// with a real OpenAI-style JSON catalog — a dashboard web page with
        /// HTTP 200 (endpoint without /v1) fails here; stage 2 sends a live
        /// 1-message chat through the gateway and requires a completion with a
        /// <c>choices</c> array. Result detail reports both outcomes so the user
        /// sees «шлюз доступен · N моделей · модель ответила» instead of a bare OK.
        /// </summary>
        private static async Task<AiApiKeyTestResult> TestFreeLlmApiAsync(
            string apiKey,
            HttpClient httpClient,
            Stopwatch sw,
            CancellationToken ct)
        {
            var endpoint = AppSettingsServiceAi.LoadAiFreeLlmApiEndpoint();
            var modelsUrl = BuildFreeLlmApiModelsUrl(endpoint);

            // ── Stage 1: catalog ──────────────────────────────────────
            using var catalogCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            catalogCts.CancelAfter(TimeSpan.FromSeconds(8));

            using var catalogRequest = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
            if (!string.IsNullOrWhiteSpace(apiKey))
                catalogRequest.Headers.Add("Authorization", $"Bearer {apiKey.Trim()}");

            using var catalogResponse = await httpClient.SendAsync(catalogRequest, catalogCts.Token);
            var catalogBody = await catalogResponse.Content.ReadAsStringAsync(catalogCts.Token);
            int catalogStatus = (int)catalogResponse.StatusCode;

            if (!catalogResponse.IsSuccessStatusCode)
            {
                sw.Stop();
                string failure = catalogStatus == 404
                    ? "эндпоинт не найден (проверьте URL)"
                    : DescribeFreeLlmStatus(catalogStatus);
                return new AiApiKeyTestResult(false, catalogStatus, (int)sw.ElapsedMilliseconds, failure);
            }

            if (!TryParseModelCatalog(catalogBody, out var modelIds, out string catalogError))
            {
                sw.Stop();
                return new AiApiKeyTestResult(false, catalogStatus, (int)sw.ElapsedMilliseconds, catalogError);
            }

            // ── Stage 2: live chat through the router ─────────────────
            var probeModel = PickFreeLlmProbeModel(modelIds);
            var chatUrl = BuildFreeLlmApiChatUrl(endpoint);

            using var chatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            chatCts.CancelAfter(TimeSpan.FromSeconds(8));

            using var chatRequest = new HttpRequestMessage(HttpMethod.Post, chatUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    model = probeModel,
                    messages = new[] { new { role = "user", content = "ping" } },
                    max_tokens = 32,
                    stream = false
                }), Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(apiKey))
                chatRequest.Headers.Add("Authorization", $"Bearer {apiKey.Trim()}");

            using var chatResponse = await httpClient.SendAsync(chatRequest, chatCts.Token);
            var chatBody = await chatResponse.Content.ReadAsStringAsync(chatCts.Token);
            sw.Stop();
            int chatStatus = (int)chatResponse.StatusCode;

            if (!chatResponse.IsSuccessStatusCode)
            {
                return new AiApiKeyTestResult(false, chatStatus, (int)sw.ElapsedMilliseconds,
                    $"каталог доступен ({modelIds.Count} моделей), но чат: {DescribeFreeLlmStatus(chatStatus)}");
            }

            if (!IsChatProbeAnswer(chatBody, out string chatError))
            {
                return new AiApiKeyTestResult(false, chatStatus, (int)sw.ElapsedMilliseconds,
                    $"каталог доступен ({modelIds.Count} моделей), но {chatError}");
            }

            return new AiApiKeyTestResult(true, chatStatus, (int)sw.ElapsedMilliseconds,
                $"шлюз доступен · {modelIds.Count} моделей · модель «{probeModel}» ответила");
        }

        private static string DescribeFreeLlmStatus(int statusCode) => statusCode switch
        {
            401 => "неверный или просроченный ключ",
            403 => "ключ исчерпан или заблокирован",
            404 => "эндпоинт или модель не найдены (проверьте URL)",
            429 => "превышен лимит запросов",
            400 => "модель не принимает запрос",
            >= 500 => $"ошибка шлюза (HTTP {statusCode})",
            _ => $"HTTP {statusCode}"
        };

        /// <summary>
        /// Validates that a <c>/models</c> body really is an OpenAI-style catalog.
        /// A dashboard web page (200 + index.html) or any non-JSON body is the
        /// fingerprint of an endpoint that points at the gateway root instead of
        /// <c>/v1</c> — exactly the failure that used to be reported as success.
        /// </summary>
        internal static bool TryParseModelCatalog(string? body, out List<string> modelIds, out string error)
        {
            modelIds = new List<string>();
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(body))
            {
                error = "каталог моделей пуст (пустой ответ)";
                return false;
            }

            if (LooksLikeWebPage(body))
            {
                error = "по адресу отвечает веб-страница, а не API — проверьте URL (нужен суффикс /v1)";
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                if (!document.RootElement.TryGetProperty("data", out var data)
                    || data.ValueKind != JsonValueKind.Array)
                {
                    error = "ответ не является OpenAI-совместимым каталогом моделей (нет data[]) — проверьте URL";
                    return false;
                }

                foreach (var item in data.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    if (!item.TryGetProperty("id", out var id)) continue;
                    if (id.ValueKind != JsonValueKind.String) continue;
                    var value = id.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) modelIds.Add(value!);
                }

                if (modelIds.Count == 0)
                {
                    error = "каталог моделей пуст — проверьте URL и API-ключ";
                    return false;
                }

                return true;
            }
            catch (JsonException)
            {
                error = "ответ не является OpenAI-совместимым каталогом моделей — проверьте URL (обычно нужен /v1)";
                return false;
            }
        }

        /// <summary>
        /// Picks the model for the live chat probe: the virtual router when the
        /// gateway offers one (it resolves a working upstream per request),
        /// otherwise the first chat-capable catalog entry.
        /// </summary>
        internal static string PickFreeLlmProbeModel(IReadOnlyList<string> modelIds)
        {
            var router = modelIds.FirstOrDefault(id =>
                id.Equals("auto", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("auto:", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(router)) return router;

            return modelIds.FirstOrDefault(AiModelCatalogClient.IsFreeLlmChatModel) ?? modelIds[0];
        }

        /// <summary>
        /// Accepts a non-streaming completion OR an SSE stream (some gateways
        /// ignore <c>stream:false</c>) as long as it carries a non-empty
        /// <c>choices</c> array and no gateway-level <c>error</c> object.
        /// </summary>
        internal static bool IsChatProbeAnswer(string? body, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(body))
            {
                error = "модель вернула пустой ответ";
                return false;
            }

            if (LooksLikeWebPage(body))
            {
                error = "шлюз вернул веб-страницу вместо ответа модели — проверьте URL (нужен суффикс /v1)";
                return false;
            }

            foreach (var payload in EnumerateJsonPayloads(body))
            {
                try
                {
                    using var document = JsonDocument.Parse(payload);
                    if (document.RootElement.ValueKind != JsonValueKind.Object) continue;

                    if (document.RootElement.TryGetProperty("error", out var errorElement)
                        && errorElement.ValueKind != JsonValueKind.Null)
                    {
                        error = "шлюз вернул ошибку провайдера (подробности в логе FreeLLMAPI)";
                        return false;
                    }

                    if (document.RootElement.TryGetProperty("choices", out var choices)
                        && choices.ValueKind == JsonValueKind.Array
                        && choices.GetArrayLength() > 0)
                    {
                        return true;
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed frames; another line may be the real answer.
                }
            }

            error = "ответ шлюза не содержит choices — это не OpenAI-совместимый ответ";
            return false;
        }

        /// <summary>
        /// Yields the JSON payloads of a raw response: the whole body for a
        /// non-streaming answer, or every <c>data:</c> frame of an SSE stream.
        /// </summary>
        private static IEnumerable<string> EnumerateJsonPayloads(string body)
        {
            if (body.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var rawLine in body.Split('\n'))
                {
                    var line = rawLine.TrimEnd('\r').Trim();
                    if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                    var payload = line["data:".Length..].Trim();
                    if (payload.Length == 0 || payload == "[DONE]") continue;
                    yield return payload;
                }

                yield break;
            }

            yield return body;
        }

        /// <summary>Resolves the provider for a model ID (default: OpenRouter).</summary>
        public static AiProvider GetProviderForModel(string modelId)
        {
            var model = AiAssistantService.AvailableModels.Concat(AiAssistantService.FreeModels)
                .FirstOrDefault(m =>
                    string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
            if (model != null)
                return model.Provider;

            // The cache may have been written by a previous process before the
            // current dialog was opened. Consult it lazily for correct routing.
            var (cached, _) = AppSettingsServiceAi.LoadCachedModels();
            return cached.FirstOrDefault(m =>
                string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase))?.Provider
                ?? AiProvider.OpenRouter;
        }

        /// <summary>
        /// Returns the API key configured by the user for a provider.
        /// Empty means this provider is not configured.
        /// </summary>
        public static string GetApiKey(AiProvider provider)
            => provider switch
            {
                AiProvider.Nvidia => AppSettingsServiceAi.LoadAiNvidiaApiKey(),
                AiProvider.FreeLlmApi => AppSettingsServiceAi.LoadAiFreeLlmApiKey(),
                _ => AppSettingsServiceAi.LoadAiApiKey()
            };

        /// <summary>True when at least one provider has a non-empty user key.</summary>
        public static bool HasAnyConfiguredApiKey
            => !string.IsNullOrWhiteSpace(GetApiKey(AiProvider.OpenRouter))
               || !string.IsNullOrWhiteSpace(GetApiKey(AiProvider.Nvidia))
               || !string.IsNullOrWhiteSpace(GetApiKey(AiProvider.FreeLlmApi));

        /// <summary>Returns the chat-completions endpoint for a provider.</summary>
        public static string GetApiUrl(AiProvider provider)
        {
            if (provider == AiProvider.Nvidia) return NvidiaApiUrl;
            if (provider == AiProvider.FreeLlmApi)
                return BuildFreeLlmApiChatUrl(AppSettingsServiceAi.LoadAiFreeLlmApiEndpoint());
            return OpenRouterApiUrl;
        }

        /// <summary>User-friendly provider name for error messages.</summary>
        public static string ProviderName(AiProvider provider)
            => provider switch
            {
                AiProvider.Nvidia => "NVIDIA",
                AiProvider.FreeLlmApi => "FreeLLMAPI",
                _ => "OpenRouter"
            };
    }
}
