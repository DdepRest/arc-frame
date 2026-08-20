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
    /// Result of a single API-key ping. Returned by <see cref="AiAssistantService.TestApiKeyAsync"/>.
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

        // ── Provider endpoints ─────────────────────────────────
        private const string DefaultModel = "google/gemma-4-31b-it:free";
        private const string ApiUrl = "https://openrouter.ai/api/v1/chat/completions";
        private const string ModelsUrl = "https://openrouter.ai/api/v1/models";
        private const string NvidiaApiUrl = "https://integrate.api.nvidia.com/v1/chat/completions";
        private const string NvidiaModelsUrl = "https://integrate.api.nvidia.com/v1/models";
        // OpenRouter exposes an auth probe that actually validates the key
        // (unlike /v1/models, which returns the public catalog for everyone).
        private const string OpenRouterAuthKeyUrl = "https://openrouter.ai/api/v1/auth/key";

        // ── Built-in API keys (free tier only) ────────────────
        // Embedded so the assistant works out of the box. Users can still
        // override either key in Settings → AI Ассистент.
        internal const string EmbeddedOpenRouterApiKey =
            "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";
        internal const string EmbeddedNvidiaApiKey =
            "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";

        /// <summary>True when at least one built-in key is available.</summary>
        public static bool HasEmbeddedKeys =>
            !string.IsNullOrWhiteSpace(EmbeddedOpenRouterApiKey) ||
            !string.IsNullOrWhiteSpace(EmbeddedNvidiaApiKey);

        /// <summary>
        /// Built-in fallback models used only when the remote catalogs cannot be fetched.
        /// The normal UI catalog is populated dynamically from both provider endpoints.
        /// </summary>
        public static IReadOnlyList<AiModelOption> FreeModels { get; } = new List<AiModelOption>
        {
            // Curated snapshot of OpenRouter zero-price chat models. The live
            // runtime list is auto-analyzed from /models on each refresh; this
            // list is only the offline fallback when the catalog can't load.
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
            new("deepseek-ai/deepseek-v4-flash-0731", "DeepSeek V4 Flash (NVIDIA, free)", AiProvider.Nvidia)
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
            @"<\|[^>]*\|>|<pad>|</?s>|<bos>|<eos>|<eot>|<unk>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
            string? nvidiaApiKey = null)
        {
            var ttl = cacheTtl ?? TimeSpan.FromHours(1);
            var (cached, cachedAt) = AppSettingsServiceAi.LoadCachedModels();
            bool hasUserKey = !string.IsNullOrWhiteSpace(apiKey)
                              || !string.IsNullOrWhiteSpace(nvidiaApiKey);
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

            var openRouterKey = string.IsNullOrWhiteSpace(apiKey)
                ? EmbeddedOpenRouterApiKey
                : apiKey.Trim();
            var nvidiaKey = string.IsNullOrWhiteSpace(nvidiaApiKey)
                ? GetApiKey(AiProvider.Nvidia)
                : nvidiaApiKey.Trim();

            var openRouterTask = FetchOpenRouterModelsAsync(openRouterKey, ct);
            var nvidiaTask = FetchNvidiaModelsAsync(nvidiaKey, ct);
            await Task.WhenAll(openRouterTask, nvidiaTask);

            var openRouter = await openRouterTask;
            var nvidia = await nvidiaTask;
            var merged = new List<AiModelOption>();

            // A failed provider keeps its previous catalog until that provider
            // responds again; a failed refresh must not erase usable selections.
            AddProviderResult(merged, openRouter, cached, AiProvider.OpenRouter);
            AddProviderResult(merged, nvidia, cached, AiProvider.Nvidia);

            if (merged.Count == 0)
                merged = FreeModels.ToList();

            if (openRouter.IsFromApi && nvidia.IsFromApi)
            {
                // The cache is a merged catalog, so only advance its timestamp
                // after both providers have answered. A partial refresh may be
                // shown in the current dialog, but must not make stale data from
                // the failed provider look fresh for the next hour.
                SetAvailableModels(merged);
                AppSettingsServiceAi.SaveCachedModels(merged, DateTime.UtcNow);
                ReconcileSavedModels(merged);
            }
            else
            {
                SetAvailableModels(merged);
            }

            return merged;
        }

        private static void AddProviderResult(
            List<AiModelOption> destination,
            CatalogFetchResult fetched,
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

        private static void ReconcileSavedModels(IReadOnlyList<AiModelOption> models)
        {
            var availableIds = models.Select(m => m.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var saved = AppSettingsServiceAi.LoadAiFallbackModels();
            var valid = saved.Where(availableIds.Contains).ToList();
            if (valid.Count == 0 && models.Count > 0)
                valid.Add(models[0].Id);
            AppSettingsServiceAi.SaveAiFallbackModels(valid);
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
            int maxModelsPerProvider = 6,
            CancellationToken ct = default)
        {
            if (candidates == null || candidates.Count == 0)
                return Array.Empty<AiModelAvailability>();

            var orKey = string.IsNullOrWhiteSpace(openRouterApiKey)
                ? GetApiKey(AiProvider.OpenRouter)
                : openRouterApiKey.Trim();
            var nvKey = string.IsNullOrWhiteSpace(nvidiaApiKey)
                ? GetApiKey(AiProvider.Nvidia)
                : nvidiaApiKey.Trim();

            var probeList = new List<(string Id, AiProvider Provider, string Key)>();
            foreach (var model in candidates)
            {
                if (string.IsNullOrWhiteSpace(model.Id)) continue;
                if (model.Provider == AiProvider.Nvidia)
                {
                    if (probeList.Count(p => p.Provider == AiProvider.Nvidia) < Math.Max(0, maxModelsPerProvider))
                        probeList.Add((model.Id, AiProvider.Nvidia, nvKey));
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
        /// Sends a minimal "ping" chat request to a single model and reports whether
        /// it answered. A 401/403 means the key is invalid or exhausted, 404 means
        /// the model is unavailable for this account, and a 2xx with a non-empty
        /// reply means the model is usable.
        /// </summary>
        internal static async Task<AiModelAvailability> ProbeModelAsync(
            string modelId,
            AiProvider provider,
            string apiKey,
            CancellationToken ct)
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

                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, GetApiUrl(provider))
                {
                    Content = JsonContent.Create(probeRequest, options: JsonOptions)
                };
                httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
                httpRequest.Headers.Add("HTTP-Referer", "https://arcframe.app");
                httpRequest.Headers.Add("X-Title", "A.R.C. Frame");

                using var response = await HttpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, ct);
                result.StatusCode = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(body, JsonOptions);
                    var content = GetTextContent(completion?.Choices?.FirstOrDefault()?.Message?.Content);
                    result.IsAvailable = !string.IsNullOrWhiteSpace(content);
                    result.Detail = result.IsAvailable ? "OK" : "пустой ответ";
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
                Provider = GetProviderForModel(modelId),
                IsAvailable = false,
                StatusCode = statusCode,
                Detail = DescribeProbeFailure(statusCode),
                CheckedAt = DateTime.UtcNow
            });
        }

        private static void SetAvailableModels(IEnumerable<AiModelOption> models)
        {
            _availableModels = models
                .Where(m => !string.IsNullOrWhiteSpace(m.Id))
                .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        private static async Task<CatalogFetchResult> FetchOpenRouterModelsAsync(
            string apiKey, CancellationToken ct)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, ModelsUrl);
                request.Headers.Add("HTTP-Referer", "https://arcframe.app");
                request.Headers.Add("X-Title", "A.R.C. Frame");
                if (!string.IsNullOrWhiteSpace(apiKey))
                    request.Headers.Add("Authorization", $"Bearer {apiKey}");

                using var response = await HttpClient.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                    return CatalogFetchResult.Failed;

                var parsed = JsonSerializer.Deserialize<OpenRouterModelsResponse>(body, JsonOptions);
                var models = parsed?.Data?
                    .Where(m => IsZeroPrice(m.Pricing?.Prompt)
                                && IsZeroPrice(m.Pricing?.Completion)
                                && IsGeneralChatModel(m))
                    .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(m => new AiModelOption(
                        m.Id,
                        string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name,
                        AiProvider.OpenRouter)
                    {
                        SupportsVision = HasImageInput(m)
                    })
                    .ToList() ?? new List<AiModelOption>();

                return models.Count > 0
                    ? new CatalogFetchResult(models, true)
                    : CatalogFetchResult.Failed;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AiAssistantService] OpenRouter catalog failed: {ex.Message}");
                return CatalogFetchResult.Failed;
            }
        }

        private static async Task<CatalogFetchResult> FetchNvidiaModelsAsync(
            string apiKey, CancellationToken ct)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, NvidiaModelsUrl);
                if (!string.IsNullOrWhiteSpace(apiKey))
                    request.Headers.Add("Authorization", $"Bearer {apiKey}");

                using var response = await HttpClient.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                    return CatalogFetchResult.Failed;

                using var document = JsonDocument.Parse(body);
                if (!document.RootElement.TryGetProperty("data", out var data)
                    || data.ValueKind != JsonValueKind.Array)
                    return CatalogFetchResult.Failed;

                var models = new List<AiModelOption>();
                foreach (var item in data.EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var idElement)) continue;
                    var id = idElement.GetString();
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    // NVIDIA's catalog includes embedding, code, vision, safety and
                    // reward models. Only keep chat-capable entries so auto-select
                    // never routes a chat request to a model that returns garbage.
                    if (!IsChatModel(id)) continue;

                    string displayName = id;
                    foreach (var property in new[] { "name", "display_name", "displayName" })
                    {
                        if (item.TryGetProperty(property, out var nameElement)
                            && nameElement.ValueKind == JsonValueKind.String
                            && !string.IsNullOrWhiteSpace(nameElement.GetString()))
                        {
                            displayName = nameElement.GetString()!;
                            break;
                        }
                    }

                    models.Add(new AiModelOption(id, displayName, AiProvider.Nvidia));
                }

                return models.Count > 0
                    ? new CatalogFetchResult(models, true)
                    : CatalogFetchResult.Failed;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AiAssistantService] NVIDIA catalog failed: {ex.Message}");
                return CatalogFetchResult.Failed;
            }
        }

        private static bool IsZeroPrice(string? value)
            => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var price)
               && price == 0m;

        /// <summary>
        /// True when the OpenRouter catalog reports the model accepts image input
        /// (<c>architecture.input_modalities</c> contains "image").
        /// </summary>
        private static bool HasImageInput(OpenRouterModel? model)
        {
            if (model?.Architecture?.InputModalities == null)
                return false;
            foreach (var modality in model.Architecture.InputModalities)
            {
                if (!string.IsNullOrWhiteSpace(modality)
                    && modality.Equals("image", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Sends a user message and returns an AI response with optional action.
        /// Tries each selected fallback model in order. Built-in keys are used
        /// when the user has not configured their own.
        /// </summary>
        public async Task<AiResponse> SendMessageAsync(
            string userMessage,
            List<(string Role, string Content)> history,
            IReadOnlyList<string>? imageDataUrls = null,
            string? orderContext = null, CancellationToken ct = default)
        {
            if (!HasEmbeddedKeys)
            {
                return new AiResponse
                {
                    Reply = "⚠ API-ключ не настроен.\n\nОткройте Настройки и введите ключ OpenRouter.\nПолучить бесплатно: https://openrouter.ai/keys"
                };
            }

            var fallbackModels = ResolveFallbackModels(userMessage, imageDataUrls is { Count: > 0 });

            // Build messages
            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = BuildSystemPrompt(orderContext) }
            };

            // Add conversation history (last 20 messages to stay in context)
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

            var request = new ChatCompletionRequest
            {
                Messages = messages,
                Temperature = 0.3,
                MaxTokens = 1024
            };

            // Try each fallback model in order
            AiResponse? lastResponse = null;
            bool lastFailureWasParseOrEmpty = false;
            foreach (var modelId in fallbackModels)
            {
                request.Model = modelId;
                var provider = GetProviderForModel(modelId);
                var (response, continueToNext, isParseOrEmptyError) =
                    await TrySendModelAsync(request, GetApiKey(provider), GetApiUrl(provider), ct);
                lastResponse = response;
                lastFailureWasParseOrEmpty = isParseOrEmptyError;

                if (!continueToNext)
                    return response;
            }

            if (lastFailureWasParseOrEmpty)
            {
                return new AiResponse
                {
                    Reply = "⚠ Все выбранные модели вернули пустой или нераспознанный ответ. Проверьте запрос или выберите другие модели в настройках."
                };
            }

            return lastResponse ?? new AiResponse
            {
                Reply = "⚠ Все доступные модели недоступны.\n\nПроверьте интернет-соединение, API-ключ или выберите другие модели в настройках."
            };
        }

        /// <summary>
        /// Sends a user message with SSE streaming. Fires <paramref name="onChunk"/>
        /// for each text delta and <paramref name="onDone"/> with the full accumulated
        /// text when streaming completes. Tries fallback models only if streaming fails
        /// BEFORE the first token is received (mid-stream failures don't fall back).
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
            Action<AiStreamInfo>? onStreamInfo = null)
        {
            if (!HasEmbeddedKeys)
            {
                onError("⚠ API-ключ не настроен.\n\nОткройте Настройки и введите ключ OpenRouter.\nПолучить бесплатно: https://openrouter.ai/keys");
                return;
            }

            var fallbackModels = ResolveFallbackModels(userMessage, imageDataUrls is { Count: > 0 });

            var messages = BuildMessages(userMessage, history, orderContext, imageDataUrls);

            var request = new ChatCompletionRequest
            {
                Messages = messages,
                Temperature = 0.3,
                MaxTokens = 1024,
                Stream = true
            };

            int modelsTried = 0;
            foreach (var modelId in fallbackModels)
            {
                modelsTried++;
                request.Model = modelId;
                var provider = GetProviderForModel(modelId);
                var apiKey = GetApiKey(provider);
                var apiUrl = GetApiUrl(provider);

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
                            // Fatal only for auth errors (401/403) — a wrong key can't be
                            // fixed by another model. A 404 from NVIDIA means "model not
                            // available for this account": try the next fallback model
                            // instead of aborting the whole request.
                            if (statusCode is 401 or 403 or 404 or 400)
                                RecordModelUnavailable(request.Model, statusCode);

                            if (statusCode is 401 or 403)
                            {
                                onError($"⚠ Ошибка авторизации у провайдера «{ProviderName(provider)}» (код {statusCode}).\nПроверьте API-ключ в настройках.");
                                return;
                            }
                            // Retry only transient errors: 429 (rate limit) and
                            // 5xx. Other 4xx (400/404: model unavailable for this
                            // account) can never succeed on retry — move straight
                            // to the next fallback model.
                            transientFailure = statusCode is 429 or >= 500;
                        }
                        else
                        {
                            // Wait for the first actual text token before announcing the
                            // model. A successful HTTP response can still produce an empty
                            // or malformed stream and must not leave a false model badge.
                            using var stream = await httpResponse.Content.ReadAsStreamAsync(ct);
                            using var reader = new StreamReader(stream);

                            while (!reader.EndOfStream)
                            {
                                ct.ThrowIfCancellationRequested();
                                var line = await reader.ReadLineAsync(ct);

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
                                    if (!modelAnnounced)
                                    {
                                        modelAnnounced = true;
                                        onModelUsed?.Invoke($"{FormatModelName(modelId)} · {ProviderName(provider)}");
                                        onStreamInfo?.Invoke(new AiStreamInfo
                                        {
                                            ModelLabel = $"{FormatModelName(modelId)} · {ProviderName(provider)}",
                                            Provider = provider,
                                            Attempt = attempt,
                                            FallbackUsed = modelsTried > 1
                                        });
                                    }
                                    fullText.Append(clean);
                                    onChunk(clean);
                                }
                                catch (JsonException)
                                {
                                    // Skip malformed chunks silently
                                }
                            }

                            // Stream finished ([DONE] or EOF).
                            var text = fullText.ToString();
                            if (text.Length > 0)
                            {
                                onDone(text);
                                return;
                            }
                            // Padding-only / empty response — skip this model.
                            skipModel = true;
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        onError("stream_cancelled");
                        return;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
                    {
                        if (!gotFirstToken)
                        {
                            // Transient network failure before the first token:
                            // retry the same model (up to MaxAttemptsPerModel).
                            transientFailure = true;
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
                            return;
                        }
                    }

                    if (skipModel)
                        break;

                    if (transientFailure && attempt < MaxAttemptsPerModel)
                        continue; // retry same model

                    break; // attempt limit reached → try next model
                }
            }

            // All models failed
            onError("⚠ Все доступные модели недоступны.\n\nПроверьте интернет-соединение, API-ключ или выберите другие модели в настройках.");
        }

        /// <summary>
        /// Resolves the ordered list of fallback models for a user message.
        /// When auto-mode is enabled, classifies the task and picks the best
        /// models automatically, merging with user-selected models as top priority.
        /// When auto-mode is off, returns the user's manual selection.
        /// </summary>
        private static IReadOnlyList<string> ResolveFallbackModels(string userMessage, bool hasImages = false)
        {
            var userSelected = AppSettingsServiceAi.LoadAiFallbackModels();
            bool autoMode = AppSettingsServiceAi.LoadAutoSelectModel();

            IReadOnlyList<string> resolved;
            if (!autoMode)
            {
                resolved = userSelected.Count == 0
                    ? new[] { DefaultModel }
                    : userSelected;
            }
            else
            {
                // Auto mode: classify the task and select best models
                var taskType = AiTaskClassifier.Classify(userMessage);
                var catalog = AvailableModels;
                var autoOrdered = AiModelSelector.SelectForTask(taskType, catalog);

                // Merge: user's picks first, then task-ranked models fill the chain
                var merged = AiModelSelector.MergeWithUserSelection(autoOrdered, userSelected);
                resolved = merged.Count == 0
                    ? new[] { DefaultModel }
                    : merged;
            }

            // Auto-analysis: drop models that a recent probe or send-path failure
            // already marked dead (401/403/404). Never return an empty chain — a
            // stale cache must not break the request.
            resolved = ExcludeUnavailable(resolved);

            // When the user attached images, prefer vision-capable models first so
            // a text-only model does not waste the request (and the user's patience).
            if (hasImages)
            {
                var visionFirst = resolved.Where(IsVisionCapable)
                    .Concat(resolved.Where(m => !IsVisionCapable(m)))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (visionFirst.Count > 0)
                    resolved = visionFirst;
            }

            // Even a manual OpenRouter-only selection must not die when OpenRouter
            // is down: always append an NVIDIA free model as a second provider.
            return EnsureNvidiaFallback(resolved);
        }

        /// <summary>
        /// Guarantees at least one NVIDIA free model in the fallback chain. When
        /// OpenRouter (or the user's manual selection) is unavailable, the request
        /// still has a second provider to try instead of failing immediately.
        /// </summary>
        private static IReadOnlyList<string> EnsureNvidiaFallback(IReadOnlyList<string> models)
        {
            if (models.Any(m => GetProviderForModel(m) == AiProvider.Nvidia))
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
        /// temporary outage cannot ban a model forever.
        /// </summary>
        private static IReadOnlyList<string> ExcludeUnavailable(IEnumerable<string> models)
        {
            var list = models?.ToList() ?? new List<string>();
            if (list.Count == 0) return list;

            var availability = GetAvailability();
            var fresh = list.Where(id => !IsRecentlyUnavailable(availability, id)).ToList();
            return fresh.Count > 0 ? fresh : list;
        }

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

        private static bool IsVisionCapable(string modelId)
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
            "imagen", "midjourney", "sdxl"
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
        internal static bool IsGeneralChatModel(OpenRouterModel model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Id))
                return false;

            var id = model.Id.ToLowerInvariant();
            foreach (var marker in NonChatModelMarkers)
            {
                if (id.Contains(marker, StringComparison.Ordinal))
                    return false;
            }

            // When the API reports the architecture modality, keep only text-in /
            // text-out models and reject embeddings and non-text generators.
            var modality = model.Architecture?.Modality?.ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(modality))
            {
                if (modality.Contains("embedding", StringComparison.Ordinal)
                    || modality.Contains("->image", StringComparison.Ordinal)
                    || modality.Contains("->audio", StringComparison.Ordinal)
                    || modality.Contains("->video", StringComparison.Ordinal)
                    || modality.Contains("->speech", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

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
            if (!string.IsNullOrWhiteSpace(userMessage))
                parts.Add(new ChatContentTextPart { Text = userMessage });

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
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            return SpecialTokenRegex.Replace(text, string.Empty).Trim();
        }

        /// <summary>
        /// Formats a display name for a model ID used in user-facing messages.
        /// </summary>
        private static string FormatModelName(string modelId)
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
        public static async Task<AiApiKeyTestResult> TestApiKeyAsync(
            AiProvider provider,
            string apiKey,
            CancellationToken ct = default)
        {
            // OpenRouter: hit /auth/key so 401/403 really means "bad key".
            // NVIDIA: /v1/models is the best probe available — it does not enforce
            // auth, so "OK" only proves network reachability for NVIDIA. The
            // dialog clearly tells the user so we are honest about the result.
            var url = provider == AiProvider.Nvidia ? NvidiaModelsUrl : OpenRouterAuthKeyUrl;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrWhiteSpace(apiKey))
                    request.Headers.Add("Authorization", $"Bearer {apiKey}");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(8));

                using var response = await HttpClient.SendAsync(request, cts.Token);
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
                        : $"HTTP {statusCode}";
                return new AiApiKeyTestResult(
                    IsOk: false,
                    StatusCode: statusCode,
                    LatencyMs: (int)sw.ElapsedMilliseconds,
                    Detail: snippet);
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
            catch (Exception ex)
            {
                sw.Stop();
                return new AiApiKeyTestResult(false, 0, (int)sw.ElapsedMilliseconds, $"ошибка сети: {ex.GetType().Name}");
            }
        }

        /// <summary>Resolves the provider for a model ID (default: OpenRouter).</summary>
        public static AiProvider GetProviderForModel(string modelId)
        {
            var model = AvailableModels.Concat(FreeModels)
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
        /// Returns the API key for a provider: the user's own key if configured,
        /// otherwise the embedded built-in key.
        /// </summary>
        private static string GetApiKey(AiProvider provider)
        {
            if (provider == AiProvider.Nvidia)
            {
                var userKey = AppSettingsServiceAi.LoadAiNvidiaApiKey();
                return string.IsNullOrWhiteSpace(userKey) ? EmbeddedNvidiaApiKey : userKey;
            }

            var orKey = AppSettingsServiceAi.LoadAiApiKey();
            return string.IsNullOrWhiteSpace(orKey) ? EmbeddedOpenRouterApiKey : orKey;
        }

        /// <summary>Returns the chat-completions endpoint for a provider.</summary>
        private static string GetApiUrl(AiProvider provider)
            => provider == AiProvider.Nvidia ? NvidiaApiUrl : ApiUrl;

        /// <summary>User-friendly provider name for error messages.</summary>
        private static string ProviderName(AiProvider provider)
            => provider == AiProvider.Nvidia ? "NVIDIA" : "OpenRouter";

        /// <summary>
        /// Always merges the built-in NVIDIA free models into a model list — the
        /// NVIDIA catalog does not expose pricing via /v1/models, so we keep a
        /// curated list of known-free NVIDIA models instead of filtering the
        /// API response. Applied both to fresh API results and cached lists.
        /// </summary>
        private sealed record CatalogFetchResult(
            IReadOnlyList<AiModelOption> Models,
            bool IsFromApi)
        {
            public static CatalogFetchResult Failed { get; } =
                new(Array.Empty<AiModelOption>(), false);
        }

        /// <summary>
        /// Tries to send a chat-completion request for a single model with retries.
        /// On success or fatal client error returns the response and <c>false</c>.
        /// On transient failure returns an error response and <c>true</c> so the caller
        /// can try the next fallback model.
        /// </summary>
        private static async Task<(AiResponse Response, bool ContinueToNext, bool IsParseOrEmptyError)> TrySendModelAsync(
            ChatCompletionRequest request,
            string apiKey,
            string apiUrl,
            CancellationToken ct)
        {
            const int maxAttempts = 3;
            int? lastStatusCode = null;
            string lastBody = "";
            Exception? lastException = null;
            AiResponse? lastParseResponse = null;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)); // 1s, 2s
                    await Task.Delay(delay, ct);
                }

                try
                {
                    var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiUrl)
                    {
                        Content = JsonContent.Create(request, options: JsonOptions)
                    };
                    httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
                    httpRequest.Headers.Add("HTTP-Referer", "https://arcframe.app");
                    httpRequest.Headers.Add("X-Title", "A.R.C. Frame");

                    var httpResponse = await HttpClient.SendAsync(httpRequest, ct);
                    var body = await httpResponse.Content.ReadAsStringAsync(ct);

                    if (httpResponse.IsSuccessStatusCode)
                    {
                        var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(body, JsonOptions);
                        var content = GetTextContent(completion?.Choices?.FirstOrDefault()?.Message?.Content);
                        var (parsed, isValid) = AiCommandParser.TryParse(content, GetTextContent(request.Messages[^1].Content));

                        if (isValid)
                            return (parsed, false, false);

                        // Empty or unparseable response is not a transient network error,
                        // so don't waste retries on the same model.
                        lastParseResponse = parsed;
                        break;
                    }

                    lastStatusCode = (int)httpResponse.StatusCode;
                    lastBody = body;

                    if (lastStatusCode is 401 or 403 or 404 or 400)
                        RecordModelUnavailable(request.Model, lastStatusCode.Value);

                    // Fatal only for auth errors (401/403) — a wrong key helps no
                    // model. Other 4xx (400/404: model unavailable for account),
                    // 429 and 5xx should try the next fallback model.
                    if (lastStatusCode is 401 or 403)
                    {
                        return (BuildErrorResponse(lastStatusCode, lastBody, null, request.Model), false, false);
                    }

                    // 404/400 mean the model is unavailable for this account/request
                    // and can never succeed on retry — stop retrying the same model
                    // and let the caller try the next fallback.
                    if (lastStatusCode is 404 or 400)
                        break;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (TaskCanceledException tex) when (!ct.IsCancellationRequested)
                {
                    lastException = tex;
                }
                catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
                {
                    lastException = ex;
                }
            }

            // We got a parse/empty response from a successful HTTP call; let the caller try the next model.
            if (lastParseResponse != null)
            {
                return (lastParseResponse, true, true);
            }

            // All retries for this model failed; caller may try the next fallback.
            var errorResponse = BuildErrorResponse(lastStatusCode, lastBody, lastException, request.Model);
            return (errorResponse, true, false);
        }

        /// <summary>
        /// Builds the system prompt with full product catalog and app knowledge.
        /// </summary>
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
        /// Форматирует полную историю обновлений в компактный список для system
        /// prompt: «• Версия X.Y.Z (дд.ММ.гггг): Заголовок» + каждая правка
        /// отдельной строкой «  — …». Возвращает ВСЕ записи без усечения —
        /// ассистент должен уметь рассказать про любую версию.
        /// Вынесено в internal-метод, чтобы unit-тест мог проверить, что
        /// история не обрезается до последних пяти версий (InternalsVisibleTo).
        /// </summary>
        internal static string FormatUpdateHistory(IEnumerable<UpdateItem> entries)
        {
            var sb = new StringBuilder();
            foreach (var e in entries)
            {
                sb.AppendLine($"• Версия {e.Version} ({e.Date:dd.MM.yyyy}): {e.Title}");
                foreach (var change in e.Changes)
                    sb.AppendLine($"  — {change}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Builds a user-friendly error response based on the last API failure.
        /// </summary>
        private static AiResponse BuildErrorResponse(int? statusCode, string body, Exception? exception, string modelId)
        {
            var providerName = ProviderName(GetProviderForModel(modelId));
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

    // ── OpenRouter /models endpoint response models ───────────────

    internal sealed class OpenRouterModelsResponse
    {
        [JsonPropertyName("data")]
        public List<OpenRouterModel>? Data { get; set; }
    }

    internal sealed class OpenRouterModel
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("pricing")]
        public OpenRouterPricing? Pricing { get; set; }

        [JsonPropertyName("architecture")]
        public OpenRouterArchitecture? Architecture { get; set; }
    }

    internal sealed class OpenRouterArchitecture
    {
        [JsonPropertyName("modality")]
        public string? Modality { get; set; }

        /// <summary>Modern OpenRouter field: e.g. ["text", "image"].</summary>
        [JsonPropertyName("input_modalities")]
        public List<string>? InputModalities { get; set; }
    }

    internal sealed class OpenRouterPricing
    {
        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = "";

        [JsonPropertyName("completion")]
        public string Completion { get; set; } = "";
    }
}
