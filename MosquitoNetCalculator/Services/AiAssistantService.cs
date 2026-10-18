using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// AI Assistant service — sends user messages to OpenRouter API (OpenAI-compatible)
    /// and parses structured JSON responses into <see cref="AiCommand"/> objects.
    /// </summary>
    /// <summary>
    /// Result of a single API-key ping. Returned by <see cref="AiAssistantService.TestApiKeyAsync(AiProvider, string, CancellationToken)"/>.
    /// </summary>
    public sealed record AiApiKeyTestResult(
        bool IsOk,
        int StatusCode,
        int LatencyMs,
        string Detail);

    public sealed class AiAssistantService
    {
        // Injectable for unit tests (retry/fallback scenarios); restored by tests.
        internal static HttpClient HttpClient { get; set; } = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        /// <summary>How many times a single model is retried on transient failures
        /// (429/5xx/network) before the next fallback model is tried.</summary>
        internal static int MaxAttemptsPerModel { get; set; } = 3;

        /// <summary>Base backoff in ms between retries of the same model.</summary>
        internal static int RetryDelayMs { get; set; } = 300;

        /// <summary>
        /// First-token watchdog for streaming responses (ms). If a model accepts
        /// the request (HTTP 200) but emits no text token within this window, the
        /// attempt is treated as a transient failure and the chain moves on — a
        /// hot free vision model can otherwise leave «Думает…» hanging for minutes.
        /// </summary>
        internal static int FirstTokenTimeoutMs { get; set; } = 8_000;

        // ── Provider endpoints ─────────────────────────────────
        // Stage-3 hardening: URL constants now live in the dedicated helper
        // classes — AiKeyValidator.OpenRouterApiUrl / NvidiaApiUrl /
        // OpenRouterAuthKeyUrl and AiModelCatalogClient.OpenRouterModelsUrl /
        // NvidiaModelsUrl. They are referenced through those classes' public
        // API rather than via private const fields.
        /// <summary>
        /// OpenRouter's «Free Models Router» (<c>openrouter/free</c>): OpenRouter
        /// resolves a currently-working free model server-side for each request
        /// (filtering by capabilities like vision and structured outputs). Kept as
        /// the standard OpenRouter model so catalog reshuffles can never strand
        /// the app on one dead <c>:free</c> slug.
        /// </summary>
        internal const string OpenRouterFreeRouter = "openrouter/free";

        private const string DefaultModel = OpenRouterFreeRouter;

        /// <summary>
        /// Stage-2 hardening: thin delegate to <see cref="AiPromptBuilder"/>.
        /// The prompt body lives in <c>Resources/ai-system-prompt.md</c> and
        /// the catalog/prices are sourced from <see cref="AiFactsProvider"/>
        /// via <c>PriceService.DefaultPrices</c> — this method stays here for
        /// binary compatibility with any caller that referenced it directly.
        /// </summary>
        private static string BuildSystemPrompt(string? orderContext)
            => AiPromptBuilder.BuildSystemPrompt(orderContext);
        /// <summary>
        /// Thin overload that forwards to <see cref="AiKeyValidator.TestApiKeyAsync"/>
        /// using the service's <see cref="HttpClient"/>. Kept here for binary
        /// compatibility — existing UI callers can stay on this signature.
        /// </summary>
        public static Task<AiApiKeyTestResult> TestApiKeyAsync(
            AiProvider provider,
            string apiKey,
            CancellationToken ct = default)
            => AiKeyValidator.TestApiKeyAsync(provider, apiKey, HttpClient, ct);



        // API credentials are intentionally never compiled into the application.
        // They are read from the user's protected settings file by AiKeyValidator.

        /// <summary>
        /// Built-in fallback models used only when the remote catalogs cannot be fetched.
        /// The normal UI catalog is populated dynamically from both provider endpoints.
        /// </summary>
        public static IReadOnlyList<AiModelOption> FreeModels { get; } = new List<AiModelOption>
        {
            // OpenRouter's Free Models Router — the standard default. It needs
            // no snapshot: OpenRouter picks a working free model per request.
            new(OpenRouterFreeRouter, "Free Models Router", AiProvider.OpenRouter),

            // Curated snapshot of OpenRouter zero-price chat models. The live
            // runtime list is auto-analyzed from /models on each refresh; this
            // list is only the offline fallback when the catalog can't load.
            new("google/gemini-2.0-flash-exp:free", "Google Gemini 2.0 Flash (free, vision)") { SupportsVision = true },
            new("qwen/qwen-2.5-vl-72b-instruct:free", "Qwen 2.5 VL 72B (free, vision)") { SupportsVision = true },
            new("meta-llama/llama-3.2-11b-vision-instruct:free", "Meta Llama 3.2 11B Vision (free, vision)") { SupportsVision = true },
            new("google/gemma-4-31b-it:free", "Google Gemma 4 31B (free)"),
            new("google/gemma-4-26b-a4b-it:free", "Google Gemma 4 26B (free)"),
            new("nvidia/nemotron-3-super-120b-a12b:free", "NVIDIA Nemotron 3 Super 120B (free)"),
            new("nvidia/nemotron-3-nano-30b-a3b:free", "NVIDIA Nemotron 3 Nano 30B (free)"),
            new("nvidia/nemotron-nano-9b-v2:free", "NVIDIA Nemotron Nano 9B (free)"),
            new("z-ai/glm-5.2:free", "Z.AI GLM 5.2 (free)"),
            new("openai/gpt-oss-20b:free", "OpenAI GPT-OSS 20B (free)"),
            new("liquid/lfm-2.5-2.6b:free", "Liquid LFM 2.5 (free)"),
            new("nvidia/nemotron-3.5-lightning:free", "NVIDIA Nemotron 3.5 Lightning (free)"),

            // NVIDIA free-tier model (endpoint: integrate.api.nvidia.com)
            new("deepseek-ai/deepseek-v4-flash-0731", "DeepSeek V4 Flash (NVIDIA, free)", AiProvider.Nvidia),

            // FreeLLMAPI models (default: http://localhost:3001/v1)
            new("auto", "FreeLLMAPI: Auto Router", AiProvider.FreeLlmApi),
            new("auto:fast", "FreeLLMAPI: Fast", AiProvider.FreeLlmApi),
            new("auto:quality", "FreeLLMAPI: Quality", AiProvider.FreeLlmApi),
            new("auto:vision", "FreeLLMAPI: Vision", AiProvider.FreeLlmApi) { SupportsVision = true },
            new("fusion", "FreeLLMAPI: Fusion", AiProvider.FreeLlmApi)
        };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // Models that cannot process an image part often answer with padding or
        // special tokens instead of text (e.g. a wall of <pad><pad>…). Strip them
        // before they reach the chat bubble.
        private static readonly Regex SpecialTokenRegex = new(
            @"<\|[^>]*\|>|<pad>|</?s>|<bos>|<eos>|<eot>|<unk>|<think>[\s\S]*?</think>|<thought>[\s\S]*?</thought>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RepetitionLoopRegex = new(
            @"(.{12,100}?)\s*(?:\1\s*){3,}",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex NumberedLoopRegex = new(
            @"((?:[^\n\r\d]{10,80}))\s*\d+[\.:\)]\s*(?:\1\s*\d+[\.:\)]\s*){3,}",
            RegexOptions.Compiled | RegexOptions.Singleline);

        // Updated after a successful catalog load so provider routing also works
        // for models which were not present in the original fallback list.
        private static IReadOnlyList<AiModelOption> _availableModels = FreeModels;

        // In-memory availability cache (id → probe result). Seeded lazily from the
        // persisted ai-settings.json and refreshed by AnalyzeAvailableModelsAsync or
        // by send-path failures. Only "unavailable" entries older than
        // AvailabilityTtl are allowed to expire so a dead model can recover later.
        private static readonly object AvailabilityLock = new();
        private static readonly Dictionary<string, AiModelAvailability> _availability =
            new(StringComparer.OrdinalIgnoreCase);
        private static bool _availabilityLoaded;
        private static readonly TimeSpan AvailabilityTtl = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Current catalog used for provider routing. It is refreshed automatically
        /// when the settings dialog loads or refreshes the remote free catalogs.
        /// </summary>
        public static IReadOnlyList<AiModelOption> AvailableModels => _availableModels;

        /// <summary>
        /// Fetches free models from both providers in parallel. OpenRouter exposes
        /// zero pricing explicitly; NVIDIA's /v1/models endpoint exposes the models
        /// available to the authenticated inference API but no pricing field, so
        /// every returned NVIDIA catalog entry is treated as free API access.
        /// The result is cached and replaces removed models, so additions and
        /// deletions are reflected automatically on the next refresh.
        /// </summary>
        public static async Task<IReadOnlyList<AiModelOption>> FetchAvailableModelsAsync(
            string apiKey,
            bool forceRefresh = false,
            CancellationToken ct = default,
            TimeSpan? cacheTtl = null,
            string? nvidiaApiKey = null,
            string? freeLlmApiKey = null,
            string? freeLlmApiEndpoint = null)
        {
            var ttl = cacheTtl ?? TimeSpan.FromHours(1);
            var (cached, cachedAt) = AppSettingsServiceAi.LoadCachedModels();
            bool hasUserKey = !string.IsNullOrWhiteSpace(apiKey)
                              || !string.IsNullOrWhiteSpace(nvidiaApiKey)
                              || !string.IsNullOrWhiteSpace(freeLlmApiKey);
            int cachedNvidiaCount = cached.Count(m => m.Provider == AiProvider.Nvidia);
            bool cacheLooksLegacy = cachedNvidiaCount > 0 && cachedNvidiaCount <= 2;
            bool cacheHasBothProviders = cached.Any(m => m.Provider == AiProvider.OpenRouter)
                                         && cachedNvidiaCount > 0;

            // User keys must take effect immediately, not after the old cache TTL.
            // Also bypass the pre-dynamic cache that had no NVIDIA catalog or only
            // the previous two hardcoded NVIDIA models.
            if (!forceRefresh && !hasUserKey && cacheHasBothProviders && !cacheLooksLegacy
                && cached.Count > 0 && cachedAt.HasValue
                && DateTime.UtcNow - cachedAt.Value < ttl)
            {
                SetAvailableModels(cached);
                return cached;
            }

            var openRouterKey = apiKey?.Trim() ?? string.Empty;
            var nvidiaKey = string.IsNullOrWhiteSpace(nvidiaApiKey)
                ? AiKeyValidator.GetApiKey(AiProvider.Nvidia)
                : nvidiaApiKey.Trim();
            var flKey = string.IsNullOrWhiteSpace(freeLlmApiKey)
                ? AiKeyValidator.GetApiKey(AiProvider.FreeLlmApi)
                : freeLlmApiKey.Trim();
            var flEndpoint = string.IsNullOrWhiteSpace(freeLlmApiEndpoint)
                ? AppSettingsServiceAi.LoadAiFreeLlmApiEndpoint()
                : freeLlmApiEndpoint.Trim();

            var openRouterTask = AiModelCatalogClient.FetchOpenRouterModelsAsync(HttpClient, openRouterKey, ct);
            var nvidiaTask = AiModelCatalogClient.FetchNvidiaModelsAsync(HttpClient, nvidiaKey, ct);
            var freeLlmTask = AiModelCatalogClient.FetchFreeLlmApiModelsAsync(HttpClient, flKey, flEndpoint, ct);
            await Task.WhenAll(openRouterTask, nvidiaTask, freeLlmTask);

            var openRouter = await openRouterTask;
            var nvidia = await nvidiaTask;
            var freeLlm = await freeLlmTask;
            var merged = new List<AiModelOption>();

            // A failed provider keeps its previous catalog until that provider
            // responds again; a failed refresh must not erase usable selections.
            AddProviderResult(merged, openRouter, cached, AiProvider.OpenRouter);
            AddProviderResult(merged, nvidia, cached, AiProvider.Nvidia);
            AddProviderResult(merged, freeLlm, cached, AiProvider.FreeLlmApi);

            if (merged.Count == 0)
                merged = FreeModels.ToList();

            // The router must always lead the catalog, no matter what the live
            // endpoints returned this minute.
            EnsureFreeRouterFirst(merged);

            if (openRouter.IsFromApi && nvidia.IsFromApi)
            {
                // The cache is a merged catalog, so only advance its timestamp
                // after both providers have answered. A partial refresh may be
                // shown in the current dialog, but must not make stale data from
                // the failed provider look fresh for the next hour.
                SetAvailableModels(merged);
                AppSettingsServiceAi.SaveCachedModels(merged, DateTime.UtcNow);
                AiModelCatalogClient.ReconcileSavedModels(merged);
            }
            else
            {
                SetAvailableModels(merged);
            }

            return merged;
        }

        private static void AddProviderResult(
            List<AiModelOption> destination,
            AiModelCatalogClient.CatalogFetchResult fetched,
            IReadOnlyList<AiModelOption> cached,
            AiProvider provider)
        {
            var source = fetched.IsFromApi
                ? fetched.Models
                : cached.Where(m => m.Provider == provider).ToList();

            if (source.Count == 0 && !fetched.IsFromApi)
                source = FreeModels.Where(m => m.Provider == provider).ToList();

            foreach (var model in source)
            {
                if (destination.All(existing =>
                    !string.Equals(existing.Id, model.Id, StringComparison.OrdinalIgnoreCase)))
                    destination.Add(model);
            }
        }

        /// <summary>
        /// Guarantees the Free Models Router (<c>openrouter/free</c>) is present
        /// and first in the catalog. OpenRouter resolves a currently-working
        /// free model server-side per request, so this entry survives catalog
        /// reshuffles and stays the standard default even when individual
        /// :free slugs appear or disappear.
        /// </summary>
        private static void EnsureFreeRouterFirst(List<AiModelOption> models)
        {
            var existing = models.FirstOrDefault(m =>
                string.Equals(m.Id, OpenRouterFreeRouter, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                models.Remove(existing);
            models.Insert(0, existing ?? new AiModelOption(
                OpenRouterFreeRouter, "Free Models Router", AiProvider.OpenRouter));
        }



        /// <summary>
        /// Auto-analyzes which candidate models are actually usable right now by
        /// sending a minimal 1-token chat probe to each. Only free/chat candidates
        /// should be passed in (the catalog fetchers already enforce that). Results
        /// are persisted and the verified-available set replaces
        /// <see cref="AvailableModels"/> so auto-routing prefers models that answer.
        /// </summary>
        public static async Task<IReadOnlyList<AiModelAvailability>> AnalyzeAvailableModelsAsync(
            IReadOnlyList<AiModelOption> candidates,
            string? openRouterApiKey = null,
            string? nvidiaApiKey = null,
            string? freeLlmApiKey = null,
            int maxModelsPerProvider = 6,
            CancellationToken ct = default)
        {
            if (candidates == null || candidates.Count == 0)
                return Array.Empty<AiModelAvailability>();

            var orKey = string.IsNullOrWhiteSpace(openRouterApiKey)
                ? AiKeyValidator.GetApiKey(AiProvider.OpenRouter)
                : openRouterApiKey.Trim();
            var nvKey = string.IsNullOrWhiteSpace(nvidiaApiKey)
                ? AiKeyValidator.GetApiKey(AiProvider.Nvidia)
                : nvidiaApiKey.Trim();
            var flKey = string.IsNullOrWhiteSpace(freeLlmApiKey)
                ? AiKeyValidator.GetApiKey(AiProvider.FreeLlmApi)
                : freeLlmApiKey.Trim();

            var probeList = new List<(string Id, AiProvider Provider, string Key)>();
            foreach (var model in candidates)
            {
                if (string.IsNullOrWhiteSpace(model.Id)) continue;
                if (model.Provider == AiProvider.Nvidia)
                {
                    if (probeList.Count(p => p.Provider == AiProvider.Nvidia) < Math.Max(0, maxModelsPerProvider))
                        probeList.Add((model.Id, AiProvider.Nvidia, nvKey));
                }
                else if (model.Provider == AiProvider.FreeLlmApi)
                {
                    if (probeList.Count(p => p.Provider == AiProvider.FreeLlmApi) < Math.Max(0, maxModelsPerProvider))
                        probeList.Add((model.Id, AiProvider.FreeLlmApi, flKey));
                }
                else
                {
                    if (probeList.Count(p => p.Provider == AiProvider.OpenRouter) < Math.Max(0, maxModelsPerProvider))
                        probeList.Add((model.Id, AiProvider.OpenRouter, orKey));
                }
            }

            var tasks = probeList
                .Select(p => ProbeModelAsync(p.Id, p.Provider, p.Key, ct))
                .ToArray();
            var results = (await Task.WhenAll(tasks)).ToList();

            ApplyAvailabilityResults(results);
            return results;
        }

        /// <summary>
        /// Convenience probe overload for measuring latency and model health.
        /// </summary>
        public static Task<AiModelAvailability> ProbeModelAsync(string modelId, CancellationToken ct = default)
        {
            var provider = AiKeyValidator.GetProviderForModel(modelId);
            var apiKey = AiKeyValidator.GetApiKey(provider);
            return ProbeModelAsync(modelId, provider, apiKey, ct);
        }

        /// <summary>
        /// Sends a minimal "ping" chat request to a single model and reports whether
        /// it answered. A 401/403 means the key is invalid or exhausted, 404 means
        /// the model is unavailable for this account, and a 2xx with a non-empty
        /// reply means the model is usable.
        /// </summary>
        internal static async Task<AiModelAvailability> ProbeModelAsync(
            string modelId,
            AiProvider provider,
            string apiKey,
            CancellationToken ct = default)
        {
            var result = new AiModelAvailability
            {
                Id = modelId,
                Provider = provider,
                IsAvailable = false,
                CheckedAt = DateTime.UtcNow
            };

            try
            {
                var probeRequest = new ChatCompletionRequest
                {
                    Model = modelId,
                    Messages = new List<ChatMessage>
                    {
                        new() { Role = "user", Content = "ping" }
                    },
                    Temperature = 0,
                    MaxTokens = 1
                };

                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, AiKeyValidator.GetApiUrl(provider))
                {
                    Content = JsonContent.Create(probeRequest, options: JsonOptions)
                };
                if (!string.IsNullOrWhiteSpace(apiKey))
                    httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
                httpRequest.Headers.Add("HTTP-Referer", "https://arcframe.app");
                httpRequest.Headers.Add("X-Title", "A.R.C. Frame");

                using var response = await HttpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, ct);
                result.StatusCode = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    if (AiKeyValidator.LooksLikeWebPage(body))
                    {
                        // The FreeLLMAPI gateway serves its dashboard page with
                        // HTTP 200 for any path outside /v1 — a "success" that
                        // carries no model answer at all.
                        result.IsAvailable = false;
                        result.Detail = "по адресу отвечает веб-страница, а не API — проверьте URL (нужен суффикс /v1)";
                    }
                    else
                    {
                        try
                        {
                            var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(body, JsonOptions);
                            var content = GetTextContent(completion?.Choices?.FirstOrDefault()?.Message?.Content);
                            result.IsAvailable = !string.IsNullOrWhiteSpace(content);
                            result.Detail = result.IsAvailable ? "OK" : "пустой ответ";
                        }
                        catch (JsonException)
                        {
                            result.IsAvailable = false;
                            result.Detail = "ответ не является OpenAI-совместимым JSON — проверьте URL";
                        }
                    }
                }
                else
                {
                    result.Detail = DescribeProbeFailure((int)response.StatusCode);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Detail = $"ошибка сети: {ex.GetType().Name}";
            }

            return result;
        }

        private static string DescribeProbeFailure(int statusCode) => statusCode switch
        {
            401 => "неверный ключ",
            403 => "ключ исчерпан или заблокирован",
            404 => "модель недоступна для этого ключа",
            429 => "превышен лимит запросов",
            400 => "модель не принимает запрос",
            >= 500 => "сервер провайдера недоступен",
            _ => $"HTTP {statusCode}"
        };

        /// <summary>
        /// Persists probe results and, when at least one model answered, narrows the
        /// routing catalog to the verified-available set. A fully-failed probe (e.g.
        /// network down during the check) keeps the previous catalog untouched.
        /// </summary>
        private static void ApplyAvailabilityResults(IReadOnlyList<AiModelAvailability> results)
        {
            if (results == null || results.Count == 0) return;

            foreach (var result in results)
                RecordModelAvailability(result);

            AppSettingsServiceAi.SaveModelAvailability(GetAvailability().Values);

            var available = results
                .Where(r => r.IsAvailable)
                .Select(r => new AiModelOption(r.Id, r.Id, r.Provider))
                .ToList();
            if (available.Count > 0)
                SetAvailableModels(available);
        }

        /// <summary>
        /// Records a per-model availability observation in the in-memory cache
        /// (used by both explicit probes and send-path failures).
        /// </summary>
        internal static void RecordModelAvailability(AiModelAvailability entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Id)) return;
            lock (AvailabilityLock)
            {
                _availabilityLoaded = true;
                _availability[entry.Id] = entry;
            }
        }

        private static IReadOnlyDictionary<string, AiModelAvailability> GetAvailability()
        {
            lock (AvailabilityLock)
            {
                if (!_availabilityLoaded)
                {
                    foreach (var entry in AppSettingsServiceAi.LoadModelAvailability())
                    {
                        if (!string.IsNullOrWhiteSpace(entry.Id))
                            _availability[entry.Id] = entry;
                    }
                    _availabilityLoaded = true;
                }
                return _availability;
            }
        }

        /// <summary>
        /// Clears the in-memory availability cache (used by tests to isolate state).
        /// </summary>
        internal static void ResetAvailabilityCache()
        {
            lock (AvailabilityLock)
            {
                _availability.Clear();
                _availabilityLoaded = false;
            }
        }

        /// <summary>
        /// Restores the routing catalog to the curated free fallback (used by tests
        /// to undo the narrowing performed by <see cref="AnalyzeAvailableModelsAsync"/>).
        /// </summary>
        internal static void ResetAvailableModelsCatalog()
        {
            _availableModels = FreeModels;
        }

        /// <summary>
        /// Marks a model as unavailable after a fatal per-model failure (401/403/404).
        /// The entry lives in the session cache so subsequent requests in the same run
        /// skip the model; explicit probes persist it to disk.
        /// </summary>
        internal static void RecordModelUnavailable(string modelId, int statusCode)
        {
            RecordModelAvailability(new AiModelAvailability
            {
                Id = modelId,
                Provider = AiKeyValidator.GetProviderForModel(modelId),
                IsAvailable = false,
                StatusCode = statusCode,
                Detail = DescribeProbeFailure(statusCode),
                CheckedAt = DateTime.UtcNow
            });
        }

        private static void SetAvailableModels(IEnumerable<AiModelOption> models)
            => _availableModels = AiModelCatalogClient.Deduplicate(models);







        /// <summary>
        /// Sends a user message with SSE streaming. Fires <paramref name="onChunk"/>
        /// for each text delta and <paramref name="onDone"/> with the full accumulated
        /// text when streaming completes. Tries fallback models only if streaming fails
        /// BEFORE the first token is received (mid-stream failures don't fall back).
        /// When every model in the chain fails, the free catalog is refreshed once
        /// (free models rotate server-side — the router needs no stale snapshot) and
        /// the updated chain gets a second pass before the aggregated per-provider
        /// error is reported.
        /// </summary>
        public async Task SendStreamingAsync(
            string userMessage,
            List<(string Role, string Content)> history,
            Action<string> onChunk,
            Action<string> onDone,
            Action<string> onError,
            IReadOnlyList<string>? imageDataUrls = null,
            Action<string>? onModelUsed = null,
            string? orderContext = null,
            CancellationToken ct = default,
            Action<AiStreamInfo>? onStreamInfo = null,
            bool hasOcrText = false)
        {
            if (!AiKeyValidator.HasAnyConfiguredApiKey)
            {
                onError("⚠ API-ключ не настроен.\n\nОткройте Настройки → AI Ассистент и введите ключ OpenRouter или NVIDIA.\nПолучить ключ OpenRouter бесплатно: https://openrouter.ai/keys");
                return;
            }

            bool hasImages = imageDataUrls is { Count: > 0 };
            var fallbackFailures = new Dictionary<AiProvider, string>();

            var messages = BuildMessages(userMessage, history, orderContext, imageDataUrls);

            double temp = AppSettingsServiceAi.LoadAiTemperature();
            var request = new ChatCompletionRequest
            {
                Messages = messages,
                Temperature = temp,
                MaxTokens = hasImages ? 4096 : 2048,
                Stream = true,
                ResponseFormat = hasImages ? new { type = "json_object" } : null
            };

            // Pass 1: current chain. If it fully fails, refresh the catalog and
            // give the updated chain one more chance before reporting failure.
            if (await TrySendWithChainAsync(
                    request,
                    ResolveFallbackModels(userMessage, hasImages, hasOcrText),
                    fallbackFailures,
                    onChunk, onDone, onError, onModelUsed, onStreamInfo, ct,
                    hasImages: hasImages))
            {
                return;
            }

            // Self-heal: free catalogs reshuffle constantly (models are added,
            // renamed, retired). A forced refresh + second pass costs one extra
            // catalog request and fixes the «все модели умерли разом» case.
            await FetchAvailableModelsAsync(
                AiKeyValidator.GetApiKey(AiProvider.OpenRouter),
                forceRefresh: true,
                ct: ct,
                nvidiaApiKey: AiKeyValidator.GetApiKey(AiProvider.Nvidia),
                freeLlmApiKey: AiKeyValidator.GetApiKey(AiProvider.FreeLlmApi));

            if (await TrySendWithChainAsync(
                    request,
                    ResolveFallbackModels(userMessage, hasImages, hasOcrText),
                    fallbackFailures,
                    onChunk, onDone, onError, onModelUsed, onStreamInfo, ct,
                    hasImages: hasImages))
            {
                return;
            }

            // All models failed on both passes — report per provider so the user
            // sees which side is actually down instead of a generic message.
            onError(BuildTotalFailureMessage(fallbackFailures));
        }

        /// <summary>
        /// Tries every model in <paramref name="fallbackModels"/> (with per-model
        /// retries on transient failures) until a stream completes. Returns true
        /// when a response was delivered or the failure was already surfaced to
        /// the user (auth error, cancellation, mid-stream loss); returns false
        /// only when every model failed up-front, so the caller may refresh the
        /// catalog and retry once. Per-provider failure reasons are collected
        /// into <paramref name="fallbackFailures"/> for the final summary.
        /// </summary>
        private async Task<bool> TrySendWithChainAsync(
            ChatCompletionRequest request,
            IReadOnlyList<string> fallbackModels,
            Dictionary<AiProvider, string> fallbackFailures,
            Action<string> onChunk,
            Action<string> onDone,
            Action<string> onError,
            Action<string>? onModelUsed,
            Action<AiStreamInfo>? onStreamInfo,
            CancellationToken ct,
            bool hasImages = false)
        {
            int modelsTried = 0;
            foreach (var modelId in fallbackModels)
            {
                modelsTried++;
                request.Model = modelId;
                var provider = AiKeyValidator.GetProviderForModel(modelId);
                var apiKey = AiKeyValidator.GetApiKey(provider);
                var apiUrl = AiKeyValidator.GetApiUrl(provider);

                // A fallback model from a provider without a configured key is
                // unavailable by definition (except FreeLlmApi which can run without key on localhost).
                // Skip it without making an anonymous request or leaking an empty Bearer header.
                if (string.IsNullOrWhiteSpace(apiKey) && provider != AiProvider.FreeLlmApi)
                {
                    fallbackFailures[provider] = "API-ключ не настроен";
                    continue;
                }

                // Retry the same model a few times on transient failures (429/5xx/
                // network hiccups) before moving to the next fallback model.
                for (int attempt = 1; attempt <= MaxAttemptsPerModel; attempt++)
                {
                    if (attempt > 1)
                        await Task.Delay(TimeSpan.FromMilliseconds(RetryDelayMs * attempt), ct);

                    bool gotFirstToken = false;
                    bool modelAnnounced = false;
                    var fullText = new StringBuilder();
                    bool transientFailure = false;
                    // Padding-only / empty response: skip to the next model instead
                    // of retrying (e.g. an image sent to a text-only model).
                    bool skipModel = false;

                    try
                    {
                        var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiUrl)
                        {
                            Content = JsonContent.Create(request, options: JsonOptions)
                        };
                        if (!string.IsNullOrWhiteSpace(apiKey))
                            httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
                        httpRequest.Headers.Add("HTTP-Referer", "https://arcframe.app");
                        httpRequest.Headers.Add("X-Title", "A.R.C. Frame");

                        var httpResponse = await HttpClient.SendAsync(
                            httpRequest,
                            HttpCompletionOption.ResponseHeadersRead,
                            ct);

                        if (!httpResponse.IsSuccessStatusCode)
                        {
                            await httpResponse.Content.ReadAsStringAsync(ct);
                            int statusCode = (int)httpResponse.StatusCode;

                            // If HTTP 400 occurred and response_format was used, model might not support structured outputs.
                            // Clear response_format and retry this model immediately without it.
                            if (statusCode == 400 && request.ResponseFormat != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"[AI] Model {request.Model} returned 400 with response_format; retrying without structured output.");
                                request.ResponseFormat = null;
                                attempt--;
                                continue;
                            }

                            // Fatal only for auth errors (401/403) — a wrong key can't be
                            // fixed by another model. A 404 from NVIDIA or 503 from FreeLLMAPI means "model not
                            // available/configured": record it so fallback chain doesn't get stuck.
                            if (statusCode is 401 or 403 or 404 or 400 or 503)
                                RecordModelUnavailable(request.Model, statusCode);

                            fallbackFailures[provider] = DescribeProbeFailure(statusCode);

                            // 401 = genuinely bad key -> abort (no key can fix it).
                            // 403 = forbidden (guardrail/block/moderation/account
                            // scope on THIS model) — NOT a bad key: message the next
                            // fallback model/provider instead. The key itself is fine
                            // (the dialog's /auth/key probe proves it), so blaming
                            // the key here would mislead the user.
                            if (statusCode == 401)
                            {
                                onError($"⚠ Ошибка авторизации у провайдера «{AiKeyValidator.ProviderName(provider)}» (код 401).\nПроверьте API-ключ в настройках.");
                                return true;
                            }
                            // Retry only transient errors: 429 (rate limit) and
                            // 5xx. Other 4xx (400/404: model unavailable for this
                            // account) can never succeed on retry — move straight
                            // to the next fallback model.
                            transientFailure = statusCode is 429 or >= 500;
                            if (transientFailure)
                                AiModelSelector.ReportModelFailure(request.Model, statusCode);
                        }
                        else
                        {
                            // Wait for the first actual text token before announcing the
                            // model. A successful HTTP response can still produce an empty
                            // or malformed stream and must not leave a false model badge.
                            using var stream = await httpResponse.Content.ReadAsStreamAsync(ct);
                            using var reader = new StreamReader(stream);
                            using var firstTokenCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            int effectiveWatchdog = hasImages ? Math.Max(25_000, FirstTokenTimeoutMs) : FirstTokenTimeoutMs;
                            firstTokenCts.CancelAfter(TimeSpan.FromMilliseconds(effectiveWatchdog));

                            while (!reader.EndOfStream)
                            {
                                firstTokenCts.Token.ThrowIfCancellationRequested();
                                var line = await reader.ReadLineAsync(firstTokenCts.Token);

                                if (string.IsNullOrEmpty(line)) continue;
                                if (!line.StartsWith("data: ")) continue;

                                var data = line.Substring(6);
                                if (data == "[DONE]")
                                    break;

                                try
                                {
                                    var chunk = JsonSerializer.Deserialize<ChatCompletionResponse>(data, JsonOptions);
                                    var content = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
                                    if (string.IsNullOrEmpty(content))
                                        continue;

                                    // A text-only model that received an image part
                                    // answers with <pad>/<|…|> tokens — drop them before
                                    // they reach the chat bubble.
                                    var clean = StripSpecialTokens(content);
                                    if (clean.Length == 0)
                                        continue;

                                    gotFirstToken = true;
                                    // First token arrived — disable the watchdog so a long
                                    // but ACTIVE stream is never cut mid-answer.
                                    firstTokenCts.CancelAfter(Timeout.InfiniteTimeSpan);
                                    if (!modelAnnounced)
                                    {
                                        modelAnnounced = true;
                                        onModelUsed?.Invoke($"{FormatModelName(modelId)} · {AiKeyValidator.ProviderName(provider)}");
                                        onStreamInfo?.Invoke(new AiStreamInfo
                                        {
                                            ModelLabel = $"{FormatModelName(modelId)} · {AiKeyValidator.ProviderName(provider)}",
                                            Provider = provider,
                                            Attempt = attempt,
                                            FallbackUsed = modelsTried > 1
                                        });
                                    }
                                    fullText.Append(clean);
                                    if (HasRepetitiveDegeneration(fullText))
                                    {
                                        transientFailure = true;
                                        skipModel = true;
                                        fallbackFailures[provider] = "зацикливание генерации (повтор фраз)";
                                        break;
                                    }
                                    onChunk(clean);
                                }
                                catch (JsonException)
                                {
                                    // Skip malformed chunks silently
                                }
                            }

                            // Stream finished ([DONE] or EOF).
                            if (!skipModel)
                            {
                                var text = fullText.ToString();
                                if (hasImages && LooksLikeVisionRefusal(text))
                                {
                                    System.Diagnostics.Debug.WriteLine($"[AI] Model {request.Model} emitted vision refusal; skipping to next fallback model.");
                                    fallbackFailures[provider] = "модель не поддерживает изображения (отказ)";
                                    skipModel = true;
                                    transientFailure = false;
                                }
                                else if (text.Length > 0)
                                {
                                    AiModelSelector.ReportModelSuccess(request.Model);
                                    onDone(text);
                                    return true;
                                }
                                else
                                {
                                    // Padding-only / empty response — skip this model.
                                    // The reason names the actual cause when the endpoint
                                    // answered with a web page instead of an SSE stream
                                    // (FreeLLMAPI without the /v1 suffix is the classic
                                    // case: 200 + dashboard HTML parses to zero tokens).
                                    fallbackFailures[provider] = DescribeEmptyCompletion(provider, httpResponse, apiUrl);
                                    skipModel = true;
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // First-token watchdog expired: the model answered 200 OK but
                        // never started streaming (hot free vision queue). This is NOT
                        // a user cancel — degrade this attempt as transient so a retry
                        // or the next fallback model takes over instead of hanging.
                        AiModelSelector.ReportModelFailure(request.Model, 0);
                        transientFailure = true;
                        fallbackFailures[provider] = "нет первого токена (таймаут)";
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        onError("stream_cancelled");
                        return true;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
                    {
                        if (!gotFirstToken)
                        {
                            // Transient network failure before the first token:
                            // retry the same model (up to MaxAttemptsPerModel).
                            transientFailure = true;
                            fallbackFailures[provider] = "ошибка сети";
                        }
                        else
                        {
                            // Mid-stream failure: keep partial text, don't retry.
                            var partial = fullText.ToString();
                            if (partial.Length > 0)
                            {
                                onDone(partial + "\n\n⚠ Соединение прервано.");
                            }
                            else
                            {
                                onError($"⚠ Не удалось подключиться к модели «{FormatModelName(modelId)}». Проверьте интернет-соединение.");
                            }
                            return true;
                        }
                    }

                    if (skipModel)
                        break;

                    if (transientFailure && attempt < MaxAttemptsPerModel)
                        continue; // retry same model

                    break; // attempt limit reached → try next model
                }
            }

            // Every model failed up-front — the caller may refresh and retry.
            return false;
        }

        /// <summary>
        /// Explains a successful (2xx) response that produced no text tokens.
        /// The common non-obvious cause is a wrong endpoint: the FreeLLMAPI
        /// gateway answers any path outside /v1 with 200 + index.html, so every
        /// request "succeeds" while the body is a web page, not an SSE stream.
        /// </summary>
        private static string DescribeEmptyCompletion(
            AiProvider provider,
            HttpResponseMessage response,
            string apiUrl)
        {
            var mediaType = response.Content?.Headers?.ContentType?.MediaType ?? string.Empty;
            if (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                // The provider summary line already names the effective URL
                // (see BuildTotalFailureMessage) — don't repeat it here.
                return provider == AiProvider.FreeLlmApi
                    ? "по адресу отвечает веб-страница, а не API — проверьте URL (обычно нужен суффикс /v1)"
                    : "эндпоинт вернул веб-страницу вместо API";
            }

            return "модели вернули пустой ответ";
        }

        private static bool HasRepetitiveDegeneration(StringBuilder fullText)
        {
            if (fullText.Length < 120) return false;
            int len = fullText.Length;
            int checkLen = Math.Min(len, 500);
            var tail = fullText.ToString(len - checkLen, checkLen);
            return RepetitionLoopRegex.IsMatch(tail) || NumberedLoopRegex.IsMatch(tail);
        }

        /// <summary>
        /// Detects model refusal text indicating it cannot see, read or access the attached image/file,
        /// e.g. when OpenRouter router forwards multimodal input to a text-only model.
        /// </summary>
        internal static bool LooksLikeVisionRefusal(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.ToLowerInvariant();
            return ((t.Contains("доступ к") || t.Contains("доступа к") || t.Contains("не имею доступа") || t.Contains("нет доступа") || t.Contains("не могу получить доступ")) &&
                    (t.Contains("файл") || t.Contains("изображен") || t.Contains("картинк") || t.Contains("снимок") || t.Contains("скриншот") || t.Contains("экрана") || t.Contains("таблиц")))
                   || ((t.Contains("не могу") || t.Contains("не умею") || t.Contains("не способен")) &&
                    (t.Contains("просматривать") || t.Contains("распознавать изображение") || t.Contains("видеть картинки") || t.Contains("видеть фото") || t.Contains("видеть изображение") || t.Contains("обрабатывать изображения") || t.Contains("анализировать изображения")))
                   || t.Contains("поделитесь им с помощью кнопки")
                   || t.Contains("поделитесь файлом")
                   || t.Contains("i cannot view images")
                   || t.Contains("i can't see the image")
                   || t.Contains("i do not have access to");
        }

        /// <summary>
        /// Builds the final user-facing error after both passes failed: keeps the
        /// classic first line (compat) and appends the per-provider summary so
        /// the user sees whether OpenRouter, NVIDIA or both are down.
        /// </summary>
        private static string BuildTotalFailureMessage(Dictionary<AiProvider, string> fallbackFailures)
        {
            var sb = new StringBuilder("⚠ Все доступные модели недоступны.\n\n");
            if (fallbackFailures != null && fallbackFailures.Count > 0)
            {
                foreach (var kv in fallbackFailures)
                {
                    var providerName = AiKeyValidator.ProviderName(kv.Key);
                    if (kv.Key == AiProvider.FreeLlmApi)
                    {
                        var chatUrl = AiKeyValidator.BuildFreeLlmApiChatUrl(
                            AppSettingsServiceAi.LoadAiFreeLlmApiEndpoint());
                        providerName += $" ({chatUrl})";
                    }
                    sb.AppendLine($"{providerName}: {kv.Value}.");
                }
                sb.AppendLine();
            }
            sb.Append("Проверьте интернет-соединение, API-ключ или выберите другие модели в настройках.");
            return sb.ToString();
        }

        /// <summary>
        /// Resolves the ordered list of fallback models for a user message.
        /// When auto-mode is enabled, classifies the task and picks the best
        /// models automatically, merging with user-selected models as top priority.
        /// When auto-mode is off, returns the user's manual selection.
        /// </summary>
        private static IReadOnlyList<string> ResolveFallbackModels(string userMessage, bool hasImages = false, bool hasOcrText = false)
        {
            var userSelected = AppSettingsServiceAi.LoadAiFallbackModels();
            bool autoMode = AppSettingsServiceAi.LoadAutoSelectModel();

            // Detect active providers
            bool hasOpenRouterKey = !string.IsNullOrWhiteSpace(AiKeyValidator.GetApiKey(AiProvider.OpenRouter));
            bool hasNvidiaKey = !string.IsNullOrWhiteSpace(AiKeyValidator.GetApiKey(AiProvider.Nvidia));
            bool hasFreeLlm = !string.IsNullOrWhiteSpace(AppSettingsServiceAi.LoadAiFreeLlmApiEndpoint());

            bool IsProviderActive(AiProvider p) => p switch
            {
                AiProvider.OpenRouter => hasOpenRouterKey,
                AiProvider.Nvidia => hasNvidiaKey,
                AiProvider.FreeLlmApi => hasFreeLlm,
                _ => false
            };

            IReadOnlyList<string> resolved;
            if (!autoMode)
            {
                resolved = userSelected.Count == 0
                    ? (hasFreeLlm && !hasOpenRouterKey ? new[] { "auto" } : new[] { DefaultModel })
                    : userSelected;
            }
            else
            {
                // Auto mode: classify the task and select best models
                var taskType = AiTaskClassifier.Classify(userMessage, hasImages);
                var catalog = AvailableModels;
                resolved = AiModelSelector.SelectForTask(taskType, catalog, userSelected, IsProviderActive);

                if (resolved.Count == 0)
                {
                    resolved = hasFreeLlm && !hasOpenRouterKey
                        ? new[] { hasImages ? "auto:vision" : "auto" }
                        : new[] { DefaultModel };
                }
            }

            // Auto-analysis: drop models that a recent probe or send-path failure
            // already marked dead (401/403/404). Never return an empty chain.
            resolved = ExcludeUnavailable(resolved);

            if (hasImages)
            {
                // When images are attached, ensure models with verified vision capability
                // are strictly prioritized, so pure text models that emit padding tokens or 400s
                // are not tried first.
                var visionModels = resolved.Where(IsVisionCapable).ToList();
                var fallbackVision = FreeModels
                    .Where(m => m.SupportsVision == true && IsProviderActive(m.Provider))
                    .Select(m => m.Id)
                    .ToList();

                var list = new List<string>();
                if (hasOpenRouterKey) list.Add(OpenRouterFreeRouter);
                if (hasFreeLlm) list.Add("auto");
                if (list.Count == 0) list.Add(OpenRouterFreeRouter);

                var userVision = userSelected.Where(IsVisionCapable).ToList();
                if (userVision.Count > 0)
                {
                    resolved = userVision
                        .Concat(list)
                        .Concat(visionModels)
                        .Concat(fallbackVision)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                else
                {
                    resolved = list
                        .Concat(visionModels)
                        .Concat(fallbackVision)
                        .Concat(resolved.Where(IsVisionCapable))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }
            else if (autoMode)
            {
                var routers = new List<string>();
                if (hasFreeLlm && !hasOpenRouterKey)
                    routers.Add("auto");
                else if (hasOpenRouterKey)
                    routers.Add(OpenRouterFreeRouter);

                if (userSelected.Count > 0)
                {
                    // User selection was already prioritized in SelectForTask
                    resolved = resolved.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }
                else
                {
                    resolved = routers
                        .Concat(resolved.Where(m => !IsRouterModel(m)))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }

            // Append NVIDIA fallback if key is present and none was selected yet
            if (hasNvidiaKey)
            {
                resolved = EnsureNvidiaFallback(resolved);
            }

            return resolved;
        }

        /// <summary>
        /// Guarantees at least one NVIDIA free model in the fallback chain. When
        /// OpenRouter (or the user's manual selection) is unavailable, the request
        /// still has a second provider to try instead of failing immediately.
        /// </summary>
        private static IReadOnlyList<string> EnsureNvidiaFallback(IReadOnlyList<string> models)
        {
            if (models.Any(m => AiKeyValidator.GetProviderForModel(m) == AiProvider.Nvidia))
                return models;

            var nvidiaDefaults = AvailableModels
                .Concat(FreeModels)
                .Where(m => m.Provider == AiProvider.Nvidia)
                .Select(m => m.Id)
                .Distinct()
                .ToList();

            return nvidiaDefaults.Count == 0
                ? models
                : models.Concat(nvidiaDefaults).ToList();
        }

        /// <summary>
        /// Removes models that were recently probed as unavailable. The cache only
        /// affects entries marked dead within <see cref="AvailabilityTtl"/> so a
        /// temporary outage cannot ban a model forever. The Free Models Router is
        /// never excluded: it resolves a working free model server-side per request,
        /// so a stale ban (a single dead <c>:free</c> slug) must not lock it out
        /// for the TTL window.
        /// </summary>
        private static IReadOnlyList<string> ExcludeUnavailable(IEnumerable<string> models)
        {
            var list = models?.ToList() ?? new List<string>();
            if (list.Count == 0) return list;

            var availability = GetAvailability();
            var fresh = list
                .Where(id => !IsRouterModel(id) && !IsRecentlyUnavailable(availability, id))
                .ToList();
            return fresh.Count > 0 ? fresh : list;
        }

        /// <summary>True when the id is the Free Models Router (<c>openrouter/free</c>).</summary>
        internal static bool IsRouterModel(string modelId)
            => string.Equals(modelId, OpenRouterFreeRouter, StringComparison.OrdinalIgnoreCase);

        private static bool IsRecentlyUnavailable(
            IReadOnlyDictionary<string, AiModelAvailability> availability,
            string modelId)
        {
            if (!availability.TryGetValue(modelId, out var entry)) return false;
            if (entry.IsAvailable) return false;
            return entry.CheckedAt == null
                   || DateTime.UtcNow - entry.CheckedAt.Value < AvailabilityTtl;
        }

        // Heuristic markers for models that accept image inputs. Used only to
        // prioritize vision models when an image is attached; the full fallback
        // chain is kept as a safety net (non-vision models still try last).
        private static readonly string[] VisionModelMarkers =
        {
            "gemma-4", "gemma-3", "gemini", "llama-4", "llama-3.2", "qwen2.5-vl",
            "qwen-vl", "gpt-4o", "gpt-5", "gpt-oss", "claude", "pixtral", "llava",
            "vision", "omni", "glm", "-vl"
        };

        internal static bool IsVisionCapable(string modelId)
        {
            // Prefer the catalog's authoritative modality metadata when known;
            // fall back to name heuristics for entries without it (NVIDIA catalog
            // and cached models saved by older builds).
            foreach (var option in AvailableModels)
            {
                if (string.Equals(option.Id, modelId, StringComparison.OrdinalIgnoreCase))
                {
                    if (option.SupportsVision is bool vision)
                        return vision;
                    break;
                }
            }

            var id = modelId.ToLowerInvariant();
            foreach (var marker in VisionModelMarkers)
            {
                if (id.Contains(marker, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        // Markers for NVIDIA catalog entries that are NOT general chat models
        // (embeddings, code completion, image/diffusion, safety, reward, parse…).
        private static readonly string[] NonChatModelMarkers =
        {
            "embed", "starcoder", "codegemma", "deepseek-coder", "code-instruct",
            "deplot", "diffusion", "safety", "guard", "reward", "parse", "bge-",
            "nomic-", "e5-", "rerank", "recurrentgemma", "fuyu", "dall-e", "flux",
            "imagen", "midjourney", "sdxl", "translate"
        };

        internal static bool IsChatModel(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId)) return false;
            var id = modelId.ToLowerInvariant();
            foreach (var marker in NonChatModelMarkers)
            {
                if (id.Contains(marker, StringComparison.Ordinal))
                    return false;
            }

            // Keep only instruction/chat-tuned entries; bare base models (e.g.
            // "google/gemma-2b") would answer chat prompts with garbage.
            return id.Contains("instruct", StringComparison.Ordinal)
                   || id.Contains("chat", StringComparison.Ordinal)
                   || id.Contains("-it", StringComparison.Ordinal)
                   || id.Contains("flash", StringComparison.Ordinal)
                   || id.Contains("-pro", StringComparison.Ordinal)
                   || id.Contains("nemotron", StringComparison.Ordinal)
                   || id.Contains("yi-large", StringComparison.Ordinal)
                   || id.Contains("jamba", StringComparison.Ordinal)
                   || id.Contains("dbrx", StringComparison.Ordinal)
                   || id.Contains("sea-lion", StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether an OpenRouter catalog entry is a general chat model.
        /// The free catalog also lists embeddings, image/audio/video generators and
        /// code-completion models — those must never be offered as chat fallbacks.
        /// Unlike <see cref="IsChatModel"/> (NVIDIA-oriented), this does not require
        /// an "-it"/"instruct" suffix: OpenRouter free chat models use varied IDs.
        /// </summary>


        /// <summary>
        /// Builds the messages list shared between streaming and non-streaming paths.
        /// </summary>
        private static List<ChatMessage> BuildMessages(
            string userMessage,
            List<(string Role, string Content)> history,
            string? orderContext,
            IReadOnlyList<string>? imageDataUrls)
        {
            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = BuildSystemPrompt(orderContext) }
            };

            int start = Math.Max(0, history.Count - 20);
            for (int i = start; i < history.Count; i++)
            {
                messages.Add(new ChatMessage
                {
                    Role = history[i].Role,
                    Content = history[i].Content
                });
            }

            messages.Add(new ChatMessage { Role = "user", Content = BuildUserContent(userMessage, imageDataUrls) });
            return messages;
        }

        /// <summary>
        /// Builds the user message content: a plain string when no images are
        /// attached, or an OpenAI-compatible multimodal parts array when they are.
        /// </summary>
        private static object BuildUserContent(
            string userMessage,
            IReadOnlyList<string>? imageDataUrls)
        {
            if (imageDataUrls == null || imageDataUrls.Count == 0)
                return userMessage;

            var parts = new List<object>(imageDataUrls.Count + 1);
            var text = string.IsNullOrWhiteSpace(userMessage)
                ? "Что на изображении?"
                : userMessage;

            parts.Add(new ChatContentTextPart { Text = text });

            foreach (var url in imageDataUrls)
            {
                if (string.IsNullOrWhiteSpace(url)) continue;
                parts.Add(new ChatContentImagePart
                {
                    ImageUrl = new ChatContentImageUrl { Url = url }
                });
            }

            return parts.Count == 0 ? userMessage : parts;
        }

        /// <summary>
        /// Extracts the plain text from message content (string or multimodal parts).
        /// Used for parsing/plan context that only needs the textual part.
        /// </summary>
        internal static string GetTextContent(object? content)
        {
            switch (content)
            {
                case null:
                    return string.Empty;
                case string s:
                    return s;
                case IEnumerable<object> parts:
                    var sb = new StringBuilder();
                    foreach (var part in parts)
                    {
                        if (part is ChatContentTextPart text)
                            sb.Append(text.Text);
                    }
                    return sb.ToString();
                default:
                    return content.ToString() ?? string.Empty;
            }
        }

        /// <summary>
        /// Removes padding/special tokens that some models emit verbatim — most
        /// often a text-only model that received an image part and answers with
        /// a wall of <c>&lt;pad&gt;</c> / <c>&lt;|image|&gt;</c> tokens instead of text.
        /// </summary>
        internal static string StripSpecialTokens(string? text)
        {
            // Only a truly empty chunk is dropped. Whitespace-only chunks are
            // PRESERVED: tokenizers stream the space before the next word as
            // its own chunk (or as the leading char of the next word), so any
            // whitespace-based filter would glue the words together client-side.
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            return SpecialTokenRegex.Replace(text, string.Empty);
        }

        /// <summary>
        /// Formats a display name for a model ID used in user-facing messages.
        /// </summary>
        internal static string FormatModelName(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
                return "(неизвестная модель)";

            // Try to find a friendly display name in the built-in list first
            var display = AvailableModels.Concat(FreeModels)
                .FirstOrDefault(m =>
                    string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase))?.DisplayName;

            return display ?? modelId;
        }

        /// <summary>
        /// Resolves the provider for a model ID (default: OpenRouter).
        /// </summary>
        /// <summary>
        /// Lightweight API key ping. Hits GET /v1/models for the chosen provider
        /// with a short timeout and reports OK or HTTP error + latency in ms.
        /// 2xx → success; 401/403 → bad key; other 4xx/5xx → partial; network error → false.
        /// Result is safe to consume from the UI thread (no exceptions thrown).
        /// </summary>


        /// <summary>Resolves the provider for a model ID (default: OpenRouter).</summary>


        /// <summary>
        /// Returns the API key configured by the user for a provider.
        /// Empty means this provider is not configured.
        /// </summary>


        /// <summary>Returns the chat-completions endpoint for a provider.</summary>


        /// <summary>
        /// Builds a user-friendly error response based on the last API failure.
        /// </summary>
        private static AiResponse BuildErrorResponse(int? statusCode, string body, Exception? exception, string modelId)
        {
            var providerName = AiKeyValidator.ProviderName(AiKeyValidator.GetProviderForModel(modelId));
            var apiPrefix = $"⚠ Провайдер «{providerName}» недоступен.\n\n";
            var modelName = FormatModelName(modelId);

            if (statusCode == 429)
            {
                return new AiResponse
                {
                    Reply = $"{apiPrefix}Модель «{modelName}» превысила лимит запросов (rate limit). Подождите немного или смените модель в настройках."
                };
            }

            if (statusCode >= 500)
            {
                return new AiResponse
                {
                    Reply = $"{apiPrefix}Сервер {providerName} временно недоступен при использовании модели «{modelName}». Повторные попытки не помогли. Попробуйте позже или выберите другую модель."
                };
            }

            if (statusCode == 401 || statusCode == 403)
            {
                return new AiResponse
                {
                    Reply = $"{apiPrefix}Ошибка авторизации при использовании модели «{modelName}». Проверьте API-ключ в Настройках → AI Ассистент."
                };
            }

            if (statusCode == 400)
            {
                return new AiResponse
                {
                    Reply = $"{apiPrefix}Неверный запрос к {providerName} для модели «{modelName}». Возможно, выбранная модель недоступна. Попробуйте сменить модель в настройках."
                };
            }

            if (exception is HttpRequestException or TimeoutException or TaskCanceledException)
            {
                return new AiResponse
                {
                    Reply = $"{apiPrefix}Не удалось подключиться к сети или запрос превысил время ожидания для модели «{modelName}». Проверьте интернет-соединение и попробуйте снова."
                };
            }

            var snippet = string.IsNullOrWhiteSpace(body) ? "" : $" ({body[..Math.Min(200, body.Length)]})";
            return new AiResponse
            {
                Reply = $"{apiPrefix}Ошибка API (код {statusCode?.ToString() ?? "—"}) для модели «{modelName}».{snippet}"
            };
        }
    }

    // ── Internal models for OpenRouter API ──────────────────────

    internal sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        /// <summary>
        /// Text (string) or multimodal parts (list of <see cref="ChatContentTextPart"/>
        /// / <see cref="ChatContentImagePart"/>) for image-aware requests.
        /// </summary>
        [JsonPropertyName("content")]
        public object? Content { get; set; }
    }

    internal sealed class ChatContentTextPart
    {
        [JsonPropertyName("type")]
        public string Type { get; } = "text";

        [JsonPropertyName("text")]
        public string Text { get; set; } = "";
    }

    internal sealed class ChatContentImagePart
    {
        [JsonPropertyName("type")]
        public string Type { get; } = "image_url";

        [JsonPropertyName("image_url")]
        public ChatContentImageUrl ImageUrl { get; set; } = new();
    }

    internal sealed class ChatContentImageUrl
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";
    }

    internal sealed class ChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; set; } = new();

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; } = 0.3;

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; } = 1024;

        [JsonPropertyName("stream")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Stream { get; set; }

        [JsonPropertyName("response_format")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? ResponseFormat { get; set; }
    }

    internal sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }
    }

    internal sealed class Choice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }

        [JsonPropertyName("delta")]
        public StreamDelta? Delta { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    internal sealed class StreamDelta
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

}
