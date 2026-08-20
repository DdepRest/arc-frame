using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Stage-3 (REFACTORING_PLAN_BIG_FILES.md §4 Фаза B#2):
    /// extracts the live-model-catalog HTTP code from <see cref="AiAssistantService"/>.
    /// Owns OpenRouter / NVIDIA <c>/v1/models</c> fetches, the
    /// zero-price filter, the chat/vision pre-filter and the saved-fallback
    /// reconciliation. The service delegates to this client and
    /// keeps only the network-orchestration + retry logic.
    /// </summary>
    public static class AiModelCatalogClient
    {
        // ── Endpoint URLs (extracted verbatim) ─────────────────────────
        internal const string OpenRouterModelsUrl = "https://openrouter.ai/api/v1/models";
        internal const string NvidiaModelsUrl = "https://integrate.api.nvidia.com/v1/models";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Outcome of a single provider fetch: a model list and a flag telling
        /// whether the list came from the API or the embedded fallback.
        /// </summary>
        public sealed record CatalogFetchResult(
            IReadOnlyList<AiModelOption> Models,
            bool IsFromApi)
        {
            public static CatalogFetchResult Failed { get; } =
                new(Array.Empty<AiModelOption>(), false);
        }

        // ── Public API ─────────────────────────────────────────────────

        /// <summary>Fetches the live OpenRouter free-tier chat catalog.</summary>
        public static async Task<CatalogFetchResult> FetchOpenRouterModelsAsync(
            HttpClient httpClient,
            string apiKey,
            CancellationToken ct)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, OpenRouterModelsUrl);
                request.Headers.Add("HTTP-Referer", "https://arcframe.app");
                request.Headers.Add("X-Title", "A.R.C. Frame");
                if (!string.IsNullOrWhiteSpace(apiKey))
                    request.Headers.Add("Authorization", $"Bearer {apiKey}");

                using var response = await httpClient.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                    return CatalogFetchResult.Failed;

                var parsed = JsonSerializer.Deserialize<OpenRouterModelsResponseDto>(body, JsonOptions);
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
                System.Diagnostics.Debug.WriteLine($"[AiModelCatalogClient] OpenRouter catalog failed: {ex.Message}");
                return CatalogFetchResult.Failed;
            }
        }

        /// <summary>Fetches the live NVIDIA catalog (chat-capable models only).</summary>
        public static async Task<CatalogFetchResult> FetchNvidiaModelsAsync(
            HttpClient httpClient,
            string apiKey,
            CancellationToken ct)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, NvidiaModelsUrl);
                if (!string.IsNullOrWhiteSpace(apiKey))
                    request.Headers.Add("Authorization", $"Bearer {apiKey}");

                using var response = await httpClient.SendAsync(request, ct);
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
                    if (!AiAssistantService.IsChatModel(id)) continue;

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
                System.Diagnostics.Debug.WriteLine($"[AiModelCatalogClient] NVIDIA catalog failed: {ex.Message}");
                return CatalogFetchResult.Failed;
            }
        }

        /// <summary>
        /// Reconciles the persisted fallback list against the live catalog —
        /// keeps only IDs that are still in <paramref name="models"/>, falling
        /// back to the first available one when the saved list is empty.
        /// </summary>
        public static void ReconcileSavedModels(IReadOnlyList<AiModelOption> models)
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
        /// Deduplicates the model list by ID (case-insensitive, first wins) and
        /// drops empty IDs. Called after each successful fetch.
        /// </summary>
        public static List<AiModelOption> Deduplicate(IEnumerable<AiModelOption> models)
            => models
                .Where(m => !string.IsNullOrWhiteSpace(m.Id))
                .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

        // ── Per-model filters ───────────────────────────────────────

        /// <summary>True when a price string parses to exactly 0 (OpenRouter sends decimals as strings).</summary>
        public static bool IsZeroPrice(string? value)
            => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var price)
               && price == 0m;

        /// <summary>
        /// True when the OpenRouter catalog reports the model accepts image input
        /// (<c>architecture.input_modalities</c> contains "image" or the legacy
        /// <c>modality</c> field says so).
        /// </summary>
        internal static bool HasImageInput(OpenRouterModelDto? model)
        {
            if (model?.Architecture?.InputModalities == null)
            {
                // Fall back to the legacy single string field if present.
                var legacy = model?.Architecture?.Modality;
                if (!string.IsNullOrWhiteSpace(legacy))
                    return legacy.Contains("image", StringComparison.OrdinalIgnoreCase);
                return false;
            }
            return model.Architecture.InputModalities.Any(s =>
                string.Equals(s, "image", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// OpenRouter-side filter: only keep general-purpose chat models.
        /// The free catalog also lists embeddings, image/audio/video
        /// generators and code-completion models — those must never be offered
        /// as chat fallbacks. Marker list + modality gate together cover the
        /// common false positives from OpenRouter's free catalog.
        /// </summary>
        internal static bool IsGeneralChatModel(OpenRouterModelDto model)
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

        // Markers for OpenRouter catalog entries that are NOT general chat models
        // (embeddings, code completion, image/diffusion, safety, reward, parse…).
        // Reuses the same list as <c>AiAssistantService.IsChatModel</c> so the
        // two filters agree on what counts as «chat-capable».
        private static readonly string[] NonChatModelMarkers =
        {
            "embed", "starcoder", "codegemma", "deepseek-coder", "code-instruct",
            "deplot", "diffusion", "safety", "guard", "reward", "parse", "bge-",
            "nomic-", "e5-", "rerank", "recurrentgemma", "fuyu", "dall-e", "flux",
            "imagen", "midjourney", "sdxl"
        };

        // ── DTOs (moved verbatim from AiAssistantService) ────────────
        // Suffixed with «Dto» so they don't collide with anything the host
        // assembly might pull in via shared references.

        internal sealed class OpenRouterModelsResponseDto
        {
            [JsonPropertyName("data")]
            public List<OpenRouterModelDto>? Data { get; set; }
        }

        internal sealed class OpenRouterModelDto
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = "";

            [JsonPropertyName("name")]
            public string Name { get; set; } = "";

            [JsonPropertyName("pricing")]
            public OpenRouterPricingDto? Pricing { get; set; }

            [JsonPropertyName("architecture")]
            public OpenRouterArchitectureDto? Architecture { get; set; }
        }

        internal sealed class OpenRouterArchitectureDto
        {
            [JsonPropertyName("modality")]
            public string? Modality { get; set; }

            /// <summary>Modern OpenRouter field: e.g. ["text", "image"].</summary>
            [JsonPropertyName("input_modalities")]
            public List<string>? InputModalities { get; set; }
        }

        internal sealed class OpenRouterPricingDto
        {
            [JsonPropertyName("prompt")]
            public string Prompt { get; set; } = "";

            [JsonPropertyName("completion")]
            public string Completion { get; set; } = "";
        }
    }
}
