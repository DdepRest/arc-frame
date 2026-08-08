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
        private const string DefaultModel = "google/gemma-3-27b-it:free";
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
            new("google/gemma-3-27b-it:free", "Google Gemma 3 27B (free)"),
            new("google/gemini-2.5-pro-exp-03-25:free", "Google Gemini 2.5 Pro (free)"),
            new("google/gemini-2.5-flash-preview:free", "Google Gemini 2.5 Flash (free)"),
            new("meta-llama/llama-4-scout:free", "Meta Llama 4 Scout (free)"),
            new("mistralai/mistral-7b-instruct:free", "Mistral 7B Instruct (free)"),
            new("deepseek/deepseek-chat:free", "DeepSeek V3 Chat (free)"),
            new("nvidia/llama-3.1-nemotron-70b-instruct:free", "NVIDIA Nemotron 70B (free)"),
            new("qwen/qwen-2.5-72b-instruct:free", "Qwen 2.5 72B (free)"),

            // NVIDIA free-tier models (endpoint: integrate.api.nvidia.com)
            new("deepseek-ai/deepseek-v4-flash", "DeepSeek V4 Flash (NVIDIA, free)", AiProvider.Nvidia),
            new("deepseek-ai/deepseek-v4-pro", "DeepSeek V4 Pro (NVIDIA, free)", AiProvider.Nvidia)
        };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // Updated after a successful catalog load so provider routing also works
        // for models which were not present in the original fallback list.
        private static IReadOnlyList<AiModelOption> _availableModels = FreeModels;

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
                                && IsZeroPrice(m.Pricing?.Completion))
                    .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(m => new AiModelOption(
                        m.Id,
                        string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name,
                        AiProvider.OpenRouter))
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
        /// Sends a user message and returns an AI response with optional action.
        /// Tries each selected fallback model in order. Built-in keys are used
        /// when the user has not configured their own.
        /// </summary>
        public async Task<AiResponse> SendMessageAsync(
            string userMessage,
            List<(string Role, string Content)> history,
            string? orderContext = null, CancellationToken ct = default)
        {
            if (!HasEmbeddedKeys)
            {
                return new AiResponse
                {
                    Reply = "⚠ API-ключ не настроен.\n\nОткройте Настройки и введите ключ OpenRouter.\nПолучить бесплатно: https://openrouter.ai/keys"
                };
            }

            var fallbackModels = ResolveFallbackModels(userMessage);

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

            messages.Add(new ChatMessage { Role = "user", Content = userMessage });

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

            var fallbackModels = ResolveFallbackModels(userMessage);

            var messages = BuildMessages(userMessage, history, orderContext);

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
                                {
                                    var text = fullText.ToString();
                                    onDone(text);
                                    return;
                                }

                                try
                                {
                                    var chunk = JsonSerializer.Deserialize<ChatCompletionResponse>(data, JsonOptions);
                                    var content = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
                                    if (!string.IsNullOrEmpty(content))
                                    {
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
                                        fullText.Append(content);
                                        onChunk(content);
                                    }
                                }
                                catch (JsonException)
                                {
                                    // Skip malformed chunks silently
                                }
                            }

                            // Stream ended without [DONE]
                            var endedText = fullText.ToString();
                            if (endedText.Length > 0)
                            {
                                onDone(endedText);
                                return;
                            }
                            // Empty stream — transient, retry the same model.
                            transientFailure = true;
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
        private static IReadOnlyList<string> ResolveFallbackModels(string userMessage)
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
        /// Builds the messages list shared between streaming and non-streaming paths.
        /// </summary>
        private static List<ChatMessage> BuildMessages(
            string userMessage,
            List<(string Role, string Content)> history,
            string? orderContext)
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

            messages.Add(new ChatMessage { Role = "user", Content = userMessage });
            return messages;
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
                        var content = completion?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
                        var (parsed, isValid) = AiCommandParser.TryParse(content, request.Messages[^1].Content);

                        if (isValid)
                            return (parsed, false, false);

                        // Empty or unparseable response is not a transient network error,
                        // so don't waste retries on the same model.
                        lastParseResponse = parsed;
                        break;
                    }

                    lastStatusCode = (int)httpResponse.StatusCode;
                    lastBody = body;

                    // Fatal only for auth errors (401/403) — a wrong key helps no
                    // model. Other 4xx (400/404: model unavailable for account),
                    // 429 and 5xx should try the next fallback model.
                    if (lastStatusCode is 401 or 403)
                    {
                        return (BuildErrorResponse(lastStatusCode, lastBody, null, request.Model), false, false);
                    }
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
        private static string BuildSystemPrompt(string? orderContext)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(orderContext))
            {
                sb.AppendLine(orderContext);
                sb.AppendLine();
            }
            sb.AppendLine("Ты — AI-ассистент программы A.R.C. Frame (калькулятор сеток и откосов из сэндвича).");
            sb.AppendLine("Твоя задача — помогать менеджерам работать с программой через текстовые запросы.");
            sb.AppendLine();
            sb.AppendLine("## О ПРОГРАММЕ");
            sb.AppendLine("A.R.C. Frame — настольная программа для расчёта москитных сеток, отливов, козырьков и откосов.");
            sb.AppendLine("Возможности:");
            sb.AppendLine("• Расчёт заказа: добавление товаров (сетки, отливы, козырьки, ручные позиции), итоги, монтаж (вкл./без/в конструкцию), вычеты и надбавки.");
            sb.AppendLine("• Заказы: история, статусы, экспорт/импорт, копирование, смена статуса.");
            sb.AppendLine("• Откосы из сэндвича: АВТО-просчёт материалов (сэндвич, пена, герметик, скотч, Старт, F-планка, пеноплекс, работа) по ширине/высоте/глубине окна; режим экономии раскроя.");
            sb.AppendLine("• Цены: редактируемый каталог цен, сброс к значениям по умолчанию.");
            sb.AppendLine("• Печать КП (коммерческое предложение) и экспорт в PDF, кол-во копий, выбор принтера.");
            sb.AppendLine("• Примечания с лёгкой разметкой (**жирный**, *курсив*, [color=#RRGGBB], маркированные списки).");
            sb.AppendLine("• Обновления: проверка новых версий, журнал изменений, авто-обновление.");
            sb.AppendLine();
            sb.AppendLine("## КАТАЛОГ ТОВАРОВ");
            sb.AppendLine();
            sb.AppendLine("### Сеточные изделия (Anwis, измеряются в м²):");
            sb.AppendLine("| Товар | Цвета | Цена (₽/м²) | Anwis режим |");
            sb.AppendLine("|-------|-------|-------------|-------------|");
            sb.AppendLine("| Anwis | Белый, Коричневый | 1800/1900 | ББ60, ББ70, ПП, Проём, Габарит |");
            sb.AppendLine("| На навесах | Белый, Коричневый | 2900/3000 | — |");
            sb.AppendLine("| Оконная на метал. крепл. | Белый, Коричневый | 3200/3300 | — |");
            sb.AppendLine("| Дверная сетка | Белый | 3000 | — |");
            sb.AppendLine();
            sb.AppendLine("### Per-linear-meter (измеряются в м.п.):");
            sb.AppendLine("| Товар | Цвета | Цена (₽/м.п.) |");
            sb.AppendLine("|-------|-------|---------------|");
            sb.AppendLine("| Отлив | Белый, Коричневый, Антрацит, Золотой дуб | 2150/2650 |");
            sb.AppendLine("| Козырёк | Белый, Коричневый, Антрацит, Золотой дуб | 2150/2650 |");
            sb.AppendLine();
            sb.AppendLine("### Ручные позиции (цена вводится вручную):");
            sb.AppendLine("Работа, Брус, Пояс, Доставка, Материал — без цвета, цена 0 по умолчанию.");
            sb.AppendLine();
            sb.AppendLine("### Прочее:");
            sb.AppendLine("ПСУЛ — 100 ₽/м.п., Уплотнение — 250 ₽/м.п. (Серый/Чёрный), Короб — 2150/2650 ₽/м².");
            sb.AppendLine();
            sb.AppendLine("## РЕЖИМЫ ANWIS (только для товара «Anwis»):");
            sb.AppendLine("• ББ60 (Брусбокс 60): W+2, H−30. По умолчанию.");
            sb.AppendLine("• ББ70 (Брусбокс 70): W−2, H−30.");
            sb.AppendLine("• ПП (Профипласт): без изменений.");
            sb.AppendLine("• Проём (Размер проёма): W+20, H+20.");
            sb.AppendLine("• Габарит (Габаритный): без изменений.");
            sb.AppendLine();
            sb.AppendLine("## ПРАВИЛА ОТВЕТОВ");
            sb.AppendLine();
            sb.AppendLine("Если пользователь просит ДОБАВИТЬ ТОВАР в расчёт — ответь JSON-блоком ```json с полем \"action\".");
            sb.AppendLine("Примеры запросов на добавление:");
            sb.AppendLine("• «Сделай сетку Anwis 500×500 бб60 белую» → add_item: Anwis, Белый, 500, 500, 1, 1800, anwis_mode=ББ60");
            sb.AppendLine("• «Сделай сетку Anwis 500×500 белую» (режим НЕ указан) → задай уточняющий вопрос: ББ60, ББ70, ПП, Проём или Габарит?");
            sb.AppendLine("• «Добавь анвис корич 500 1000 в конструцию» → add_item: Anwis, Коричневый, 500, 1000, 1, 1900, anwis_mode + installation_mode=2");
            sb.AppendLine("• «Отлив 200×1500 коричневый» → add_item: Отлив, Коричневый, 200, 1500, 1, 2150");
            sb.AppendLine("• «Козырёк 350×2300, 2 штуки, антрацит» → add_item: Козырёк, Антрацит, 350, 2300, 2, 2150");
            sb.AppendLine("• «Работа 5000» → add_item: Работа, \"\", 0, 0, 1, 5000");
            sb.AppendLine("• «Доставка 500» → add_item: Доставка, \"\", 0, 0, 1, 500");
            sb.AppendLine();
            sb.AppendLine("Формат action \"add_item\":");
            sb.AppendLine("```json");
            sb.AppendLine("{");
            sb.AppendLine("  \"action\": \"add_item\",");
            sb.AppendLine("  \"params\": {");
            sb.AppendLine("    \"type\": \"Anwis\",");
            sb.AppendLine("    \"color\": \"Белый\",");
            sb.AppendLine("    \"width\": 500,");
            sb.AppendLine("    \"height\": 500,");
            sb.AppendLine("    \"quantity\": 1,");
            sb.AppendLine("    \"price\": 1800,");
            sb.AppendLine("    \"anwis_mode\": \"ББ60\",");
            sb.AppendLine("    \"installation_mode\": 2");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine("```");
            sb.AppendLine("Монтаж передавай ТОЛЬКО если пользователь его упомянул: «в конструцию»/«в конструкцию» → 2, «без монтажа» → 1, «с монтажом»/«монтаж включён» → 0. Иначе поле пропускай.");
            sb.AppendLine();
            sb.AppendLine("Другие действия:");
            sb.AppendLine("• «Удали последний» → {\"action\": \"delete_last\"}");
            sb.AppendLine("• «Удали все сетки» / «Удали козырёк» → {\"action\": \"delete_items\", \"params\": {\"product\": \"Козырёк\"}} — удаляет ВСЕ позиции, по названию товара или категории (сетки/фасадные/комплектующие/услуги/откосы).");
            sb.AppendLine("• «Очисти расчёт» → {\"action\": \"clear_all\"}");
            sb.AppendLine("• «Покажи товары» → {\"action\": \"list_products\"}");
            sb.AppendLine();
            sb.AppendLine("## ИЗМЕНЕНИЕ СУЩЕСТВУЮЩИХ ПОЗИЦИЙ (update_items)");
            sb.AppendLine("Если пользователь просит ИЗМЕНИТЬ УЖЕ ДОБАВЛЕННЫЕ товары (монтаж, цену) — используй action \"update_items\".");
            sb.AppendLine("НЕ добавляй новый товар, если речь о существующих позициях!");
            sb.AppendLine();
            sb.AppendLine("Формат \"update_items\":");
            sb.AppendLine("```json");
            sb.AppendLine("{");
            sb.AppendLine("  \"action\": \"update_items\",");
            sb.AppendLine("  \"params\": {");
            sb.AppendLine("    \"product\": \"Козырёк\",");
            sb.AppendLine("    \"installation_mode\": 0,");
            sb.AppendLine("    \"price\": 900");
            sb.AppendLine("    \"installation_amount\": 750");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine("```");
            sb.AppendLine("• product — название товара (\"Козырёк\", \"Anwis\", \"Отлив\" и т.д.) ИЛИ категория (\"сетки\", \"фасадные\", \"комплектующие\", \"услуги\", \"откосы\").");
            sb.AppendLine("  Если product не указан или \"all\" — применить ко ВСЕМ позициям.");
            sb.AppendLine("  Категории: сетки=Anwis/На навесах/Оконная на метал. крепл./Дверная сетка; фасадные=Отлив/Козырёк/Короб; комплектующие=ПСУЛ/Уплотнение/Брус/Пояс/Материал; услуги=Работа/Доставка; откосы=Откос/Работа за откос.");
            sb.AppendLine("• installation_mode (необязательно) — 0=монтаж включён, 1=без монтажа, 2=в конструкцию.");
            sb.AppendLine("• installation_amount (необязательно) — сумма монтажа в рублях (₽/шт. или ₽/м.п.). «с монтажом по 750» → installation_amount=750.");
            sb.AppendLine("• anwis_mode (необязательно) — только для Anwis. «смени с бб60 на бб70» → anwis_mode=\"ББ70\". Варианты: ББ60, ББ70, ПП, Проём, Габарит.");
            sb.AppendLine("• color (необязательно) — цвет товара. «смени цвет на коричневый» → color=\"Коричневый\".");
            sb.AppendLine("• price (необязательно) — новая цена в рублях.");
            sb.AppendLine("• Можно менять и монтаж, и цену одновременно.");
            sb.AppendLine();
            sb.AppendLine("Примеры:");
            sb.AppendLine("• «Козырёк с монтажом 900р» → update_items: product=Козырёк, installation_mode=0, price=900");
            sb.AppendLine("• «Все сетки без монтажа» → update_items: product=сетки, installation_mode=1");
            sb.AppendLine("• «Смени Anwis с бб60 на бб70» → update_items: product=Anwis, anwis_mode=\"ББ70\"");
            sb.AppendLine("• «Смени цвет Anwis на коричневый» → update_items: product=Anwis, color=\"Коричневый\"");
            sb.AppendLine("• «Сделай все позиции с монтажом» → update_items: installation_mode=0 (без product — все)");
            sb.AppendLine("• «Поменяй цену на отливах на 2500» → update_items: product=Отлив, price=2500");
            sb.AppendLine();
            sb.AppendLine("## ОТКОСЫ ИЗ СЭНДВИЧА (АВТО-ПРОСЧЁТ)");
            sb.AppendLine("«Откосы из сэндвича» — это отдельная встроенная функция АВТО-просчёта, НЕ товар из каталога.");
            sb.AppendLine("Когда пользователь просит просчитать откосы (например: «сделай просчёт откосы из сэндвича, в 1500 ш 700 г 300»,");
            sb.AppendLine("«откос 1500х700, глубина 300», «просчитай откосы для окна 1200×1400 глубиной 200»):");
            sb.AppendLine("• width = ширина окна (мм), height = высота окна (мм), depth = глубина откоса (мм), quantity = количество откосов/окон (по умолчанию 1).");
            sb.AppendLine("• Верни JSON-блок с action \"calc_slope\":");
            sb.AppendLine("```json");
            sb.AppendLine("{");
            sb.AppendLine("  \"action\": \"calc_slope\",");
            sb.AppendLine("  \"params\": {");
            sb.AppendLine("    \"width\": 1500,");
            sb.AppendLine("    \"height\": 700,");
            sb.AppendLine("    \"depth\": 300,");
            sb.AppendLine("    \"quantity\": 1");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine("```");
            sb.AppendLine("Это откроет панель откосов с уже подставленными размерами — менеджер увидит расчёт материалов и сможет добавить его в КП.");
            sb.AppendLine("Если не хватает хотя бы одного размера (ширина/высота/глубина) — задай уточняющий вопрос, НЕ выдумывай.");
            sb.AppendLine();
            sb.AppendLine("Если запрос НЕ требует действия (вопрос, справка) — отвечай обычным текстом БЕЗ JSON.");
            sb.AppendLine("Если запрос неоднозначен — задай уточняющий вопрос.");
            sb.AppendLine();
            sb.AppendLine("ВАЖНО: для Anwis, если пользователь НЕ указал режим (ББ60/ББ70/ПП/Проём/Габарит) — задай уточняющий вопрос, какой режим использовать (перечисли варианты), и НЕ добавляй товар с выдуманным режимом.");
            sb.AppendLine("ВАЖНО: если не указан цвет — используй «Белый» для товаров с цветом.");
            sb.AppendLine("ВАЖНО: если не указано количество — используй 1.");
            sb.AppendLine("ВАЖНО: если не указана цена — используй стандартную цену из каталога.");

            return sb.ToString();
        }

        /// <summary>
        /// Собирает ПОЛНУЮ историю обновлений из <see cref="UpdateLog"/>
        /// (встроенный update-log.json) — ВСЕ версии со ВСЕМИ изменениями,
        /// чтобы ассистент мог ответить на вопросы о ЛЮБОЙ версии программы,
        /// а не только о последних пяти. При любой ошибке — безопасный no-op
        /// (история недоступна), чтобы system prompt никогда не падал.
        /// </summary>
        private static string AppendRecentUpdates()
        {
            try
            {
                return FormatUpdateHistory(UpdateLog.AllNewestFirst());
            }
            catch
            {
                return "(история обновлений недоступна)";
            }
        }

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

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
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
    }

    internal sealed class OpenRouterPricing
    {
        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = "";

        [JsonPropertyName("completion")]
        public string Completion { get; set; } = "";
    }
}
