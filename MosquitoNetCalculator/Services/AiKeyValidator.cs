using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
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
            // OpenRouter: hit /auth/key so 401/403 really means «bad key».
            // NVIDIA: /v1/models is the best probe available — it does not enforce
            // auth, so «OK» only proves network reachability for NVIDIA. The
            // dialog clearly tells the user so we are honest about the result.
            var url = provider == AiProvider.Nvidia
                ? AiModelCatalogClient.NvidiaModelsUrl
                : OpenRouterAuthKeyUrl;
            var sw = Stopwatch.StartNew();

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrWhiteSpace(apiKey))
                    request.Headers.Add("Authorization", $"Bearer {apiKey}");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(8));

                using var response = await httpClient.SendAsync(request, cts.Token);
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
            => provider == AiProvider.Nvidia
                ? AppSettingsServiceAi.LoadAiNvidiaApiKey()
                : AppSettingsServiceAi.LoadAiApiKey();

        /// <summary>True when at least one provider has a non-empty user key.</summary>
        public static bool HasAnyConfiguredApiKey
            => !string.IsNullOrWhiteSpace(GetApiKey(AiProvider.OpenRouter))
               || !string.IsNullOrWhiteSpace(GetApiKey(AiProvider.Nvidia));

        /// <summary>Returns the chat-completions endpoint for a provider.</summary>
        public static string GetApiUrl(AiProvider provider)
            => provider == AiProvider.Nvidia ? NvidiaApiUrl : OpenRouterApiUrl;

        /// <summary>User-friendly provider name for error messages.</summary>
        public static string ProviderName(AiProvider provider)
            => provider == AiProvider.Nvidia ? "NVIDIA" : "OpenRouter";
    }
}
