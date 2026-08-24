using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Verifies the Free Models Router (<c>openrouter/free</c>) behaviour:
    /// it is always present FIRST in the catalog, immune to the availability
    /// ban, and the send path self-heals by forcing a catalog refresh + one
    /// retry after a total chain failure, reporting a per-provider summary
    /// if both passes fail.
    /// </summary>
    [Collection("FileSystem")]
    public sealed class AiAssistantServiceFreeRouterTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _originalAiSettingsPath;
        private readonly HttpClient _originalHttpClient;
        private readonly int _originalRetryDelay;
        private readonly int _originalFirstTokenTimeout;

        public AiAssistantServiceFreeRouterTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "mnc_ai_router_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _originalAiSettingsPath = AppSettingsServiceAi.AiSettingsPath;
            AppSettingsServiceAi.AiSettingsPath = Path.Combine(_tempDir, "ai-settings.json");

            _originalHttpClient = AiAssistantService.HttpClient;
            _originalRetryDelay = AiAssistantService.RetryDelayMs;
            _originalFirstTokenTimeout = AiAssistantService.FirstTokenTimeoutMs;
            AiAssistantService.RetryDelayMs = 1; // keep retry-chain tests fast
            AiAssistantService.ResetAvailabilityCache();
            AiAssistantService.ResetAvailableModelsCatalog();
        }

        public void Dispose()
        {
            AiAssistantService.HttpClient = _originalHttpClient;
            AiAssistantService.RetryDelayMs = _originalRetryDelay;
            AiAssistantService.FirstTokenTimeoutMs = _originalFirstTokenTimeout;
            AppSettingsServiceAi.AiSettingsPath = _originalAiSettingsPath;
            AiAssistantService.ResetAvailabilityCache();
            AiAssistantService.ResetAvailableModelsCatalog();
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        private static HttpResponseMessage JsonResponse(string json)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

        private static HttpResponseMessage SseResponse(string text)
        {
            var body = $"data: {{\"choices\":[{{\"delta\":{{\"content\":\"{text}\"}}}}]}}\n\ndata: [DONE]\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
            };
        }

        /// <summary>
        /// Stubs both catalogs WITHOUT the router entry — the client must insert
        /// it itself. OpenRouter returns one :free chat model, NVIDIA one model.
        /// </summary>
        private static HttpClient CreateCatalogClient()
        {
            const string orJson =
                "{\"data\":[{\"id\":\"google/gemma-4-31b-it:free\",\"name\":\"Gemma 4 31B\",\"pricing\":{\"prompt\":\"0\",\"completion\":\"0\"}}]}";
            const string nvJson =
                "{\"data\":[{\"id\":\"deepseek-ai/deepseek-v4-flash-0731\",\"name\":\"DeepSeek V4 Flash\"}]}";

            var handler = new TestHttpMessageHandler(req =>
            {
                var uri = req.RequestUri!.AbsoluteUri.ToLowerInvariant();
                return uri.Contains("openrouter.ai") ? JsonResponse(orJson) : JsonResponse(nvJson);
            });
            return new HttpClient(handler);
        }

        [Fact]
        public async Task FetchAvailableModelsAsync_InsertsFreeRouterFirst()
        {
            // The stubbed catalog does NOT contain openrouter/free — the fetch
            // must guarantee it anyway, at position 0.
            AiAssistantService.HttpClient = CreateCatalogClient();

            var models = await AiAssistantService.FetchAvailableModelsAsync("", forceRefresh: true);

            Assert.NotEmpty(models);
            Assert.Equal(AiAssistantService.OpenRouterFreeRouter, models[0].Id);
            Assert.Contains(models, m => m.Id == "google/gemma-4-31b-it:free");
        }

        [Fact]
        public void ReconcileSavedModels_PrependsFreeRouter_WhenMissing()
        {
            // A saved manual selection from an older build (router didn't exist
            // yet) must be migrated so the router leads the fallback chain.
            AppSettingsServiceAi.SaveAiFallbackModels(new[] { "google/gemma-4-31b-it:free" });

            AiModelCatalogClient.ReconcileSavedModels(new List<AiModelOption>
            {
                new("google/gemma-4-31b-it:free", "Gemma 4 31B"),
                new(AiAssistantService.OpenRouterFreeRouter, "Free Models Router")
            });

            var loaded = AppSettingsServiceAi.LoadAiFallbackModels();
            Assert.Equal(AiAssistantService.OpenRouterFreeRouter, loaded[0]);
            Assert.Contains("google/gemma-4-31b-it:free", loaded);
        }

        [Fact]
        public async Task SendStreamingAsync_StillUsesRouter_WhenStaleBanSaysDead()
        {
            // Manual selection: only the router. A stale probe marks it dead —
            // the send path must ignore the ban because the router resolves a
            // working free model per request.
            AppSettingsServiceAi.SaveAiFallbackModels(new[] { AiAssistantService.OpenRouterFreeRouter });
            AppSettingsServiceAi.SaveAutoSelectModel(false);
            AiAssistantService.RecordModelUnavailable(AiAssistantService.OpenRouterFreeRouter, 404);

            var requested = new List<string>();
            var handler = new TestHttpMessageHandler(req =>
            {
                var model = ExtractModel(req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "");
                if (model != null) requested.Add(model);
                return SseResponse("Привет от роутера");
            });
            AiAssistantService.HttpClient = new HttpClient(handler);

            string? done = null;
            var errors = new List<string>();
            var service = new AiAssistantService();
            await service.SendStreamingAsync(
                "привет",
                new List<(string Role, string Content)>(),
                _ => { },
                t => done = t,
                e => errors.Add(e));

            Assert.Empty(errors);
            Assert.Equal("Привет от роутера", done);
            Assert.Equal(AiAssistantService.OpenRouterFreeRouter, requested[0]);
        }

        [Fact]
        public async Task SendStreamingAsync_AfterTotalFailure_RefreshesCatalogAndTriesAgain()
        {
            // Every chat request fails with 500; the catalog fetch succeeds.
            // After pass 1 fails, the send path must refresh the catalog (the
            // forced refresh makes a catalog GET) and run a second pass before
            // reporting the aggregated per-provider failure.
            AppSettingsServiceAi.SaveAiFallbackModels(new[] { AiAssistantService.OpenRouterFreeRouter });
            AppSettingsServiceAi.SaveAutoSelectModel(false);

            int chatPosts = 0;
            int catalogGets = 0;
            var handler = new TestHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Post)
                {
                    chatPosts++;
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }

                catalogGets++;
                var uri = req.RequestUri!.AbsoluteUri.ToLowerInvariant();
                return uri.Contains("openrouter")
                    ? JsonResponse("{\"data\":[{\"id\":\"google/gemma-4-31b-it:free\",\"name\":\"Gemma\",\"pricing\":{\"prompt\":\"0\",\"completion\":\"0\"}}]}")
                    : JsonResponse("{\"data\":[{\"id\":\"deepseek-ai/deepseek-v4-flash-0731\",\"name\":\"DeepSeek\"}]}");
            });
            AiAssistantService.HttpClient = new HttpClient(handler);

            var errors = new List<string>();
            var service = new AiAssistantService();
            await service.SendStreamingAsync(
                "привет",
                new List<(string Role, string Content)>(),
                _ => { },
                _ => { },
                e => errors.Add(e));

            // Pass 1: router (3 attempts) + NVIDIA fallback (3 attempts) = 6.
            // Pass 2: same → ≥ 12 chat posts overall.
            Assert.True(chatPosts >= 12, $"expected two full passes, got {chatPosts}");
            // The forced refresh performed an actual catalog fetch.
            Assert.True(catalogGets >= 1, "catalog refresh after total failure did not happen");

            var message = Assert.Single(errors);
            Assert.Contains("недоступны", message, StringComparison.OrdinalIgnoreCase);
            // Per-provider summary is present so the user sees which side is down.
            Assert.Contains("OpenRouter", message);
            Assert.Contains("NVIDIA", message);
        }

        [Fact]
        public async Task SendStreamingAsync_PreservesWhitespace_AcrossStreamedChunks()
        {
            // Regression: tokenizers stream the space as its own chunk or as the
            // leading char of the next word. The per-chunk Trim() used to strip
            // it, gluing «Чем могу помочь?» into «Чеммогупомочь?». Whitespace
            // chunks and leading-space chunks must reach the bubble intact.
            AppSettingsServiceAi.SaveAiFallbackModels(new[] { AiAssistantService.OpenRouterFreeRouter });
            AppSettingsServiceAi.SaveAutoSelectModel(false);

            var body =
                "data: {\"choices\":[{\"delta\":{\"content\":\"Привет\"}}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"content\":\" \"}}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"content\":\"Чем\"}}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"content\":\" могу\"}}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"content\":\" помочь?\"}}]}\n\n" +
                "data: [DONE]\n";

            AiAssistantService.HttpClient = new HttpClient(new TestHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
                }));

            var chunks = new List<string>();
            string? done = null;
            var errors = new List<string>();
            var service = new AiAssistantService();
            await service.SendStreamingAsync(
                "привет",
                new List<(string Role, string Content)>(),
                c => chunks.Add(c),
                t => done = t,
                e => errors.Add(e));

            Assert.Empty(errors);
            Assert.Equal("Привет Чем могу помочь?", done);
            Assert.Contains(" ", chunks); // the standalone space chunk survived
            Assert.Contains(" могу", chunks); // leading-space chunk survived
        }

        /// <summary>
        /// A stream that accepts the request but never emits any data — simulates
        /// a hot free vision model whose upstream queue is stuck. The first-token
        /// watchdog must fire instead of hanging forever.
        /// </summary>
        private sealed class HangingStream : MemoryStream
        {
            public override async Task<int> ReadAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                // Block until the watchdog cancels, then throw the cancellation.
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return 0;
            }
        }

        [Fact]
        public async Task SendStreamingAsync_WithImageAndOcrText_KeepsRouterFirst()
        {
            // When local OCR already read the image (its text is in the prompt),
            // the send path must NOT promote slow vision models: the fast chain
            // (router first) answers from the text instead. This keeps image
            // requests as snappy as plain text ones.
            AiAssistantService.HttpClient = CreateCatalogClient();
            await AiAssistantService.FetchAvailableModelsAsync("", forceRefresh: true);
            AppSettingsServiceAi.SaveAutoSelectModel(true);
            AppSettingsServiceAi.SaveAiFallbackModels(Array.Empty<string>());

            var requested = new List<string>();
            var handler = new TestHttpMessageHandler(req =>
            {
                var model = ExtractModel(req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "");
                if (model != null) requested.Add(model);
                return SseResponse("Готово");
            });
            AiAssistantService.HttpClient = new HttpClient(handler);

            var service = new AiAssistantService();
            await service.SendStreamingAsync(
                "Текст с картинки: ПМС Anwis, бел. 1 371x1217",
                new List<(string Role, string Content)>(),
                _ => { },
                _ => { },
                _ => { },
                imageDataUrls: new[] { "data:image/png;base64,AAAA" },
                hasOcrText: true);

            Assert.NotEmpty(requested);
            Assert.Equal(AiAssistantService.OpenRouterFreeRouter, requested[0]);
        }

        [Fact]
        public async Task SendStreamingAsync_ImageWithoutOcr_RouterStillFirst()
        {
            // Even when OCR produced nothing (so the image is the only source),
            // the router (openrouter/free) must lead the chain: OpenRouter picks
            // a vision-capable free model server-side for image requests. No more
            // promoting slow vision catalog entries that made image requests
            // hang for minutes.
            AiAssistantService.HttpClient = CreateCatalogClient();
            await AiAssistantService.FetchAvailableModelsAsync("", forceRefresh: true);
            AppSettingsServiceAi.SaveAutoSelectModel(true);
            AppSettingsServiceAi.SaveAiFallbackModels(Array.Empty<string>());

            var requested = new List<string>();
            var handler = new TestHttpMessageHandler(req =>
            {
                var model = ExtractModel(req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "");
                if (model != null) requested.Add(model);
                return SseResponse("Готово");
            });
            AiAssistantService.HttpClient = new HttpClient(handler);

            var service = new AiAssistantService();
            await service.SendStreamingAsync(
                "",
                new List<(string Role, string Content)>(),
                _ => { },
                _ => { },
                _ => { },
                imageDataUrls: new[] { "data:image/png;base64,AAAA" },
                hasOcrText: false);

            Assert.NotEmpty(requested);
            Assert.Equal(AiAssistantService.OpenRouterFreeRouter, requested[0]);
        }

        [Fact]
        public async Task SendStreamingAsync_StalledStream_TimesOutOnFirstToken_AndTriesNextModel()
        {
            // A model that returns 200 OK but never streams a token must not hang
            // «Думает…» forever: the first-token watchdog fails the attempt as
            // transient, then the next model (NVIDIA fallback) is tried.
            AppSettingsServiceAi.SaveAiFallbackModels(new[] { AiAssistantService.OpenRouterFreeRouter });
            AppSettingsServiceAi.SaveAutoSelectModel(false);
            AiAssistantService.RetryDelayMs = 1;
            AiAssistantService.FirstTokenTimeoutMs = 120; // fast watchdog for the test

            int chatPosts = 0;
            var handler = new TestHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Post)
                {
                    chatPosts++;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StreamContent(new HangingStream())
                    };
                }

                // Catalog GETs for the self-heal refresh.
                var uri = req.RequestUri!.AbsoluteUri.ToLowerInvariant();
                return uri.Contains("openrouter")
                    ? JsonResponse("{\"data\":[{\"id\":\"google/gemma-4-31b-it:free\",\"name\":\"Gemma\",\"pricing\":{\"prompt\":\"0\",\"completion\":\"0\"}}]}")
                    : JsonResponse("{\"data\":[{\"id\":\"deepseek-ai/deepseek-v4-flash-0731\",\"name\":\"DeepSeek\"}]}");
            });
            AiAssistantService.HttpClient = new HttpClient(handler);

            var errors = new List<string>();
            var service = new AiAssistantService();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await service.SendStreamingAsync(
                "привет",
                new List<(string Role, string Content)>(),
                _ => { },
                _ => { },
                e => errors.Add(e));
            sw.Stop();

            // Router + NVIDIA attempt (each timed out by the watchdog), then the
            // self-heal refresh and the second pass — the call must NOT hang.
            Assert.True(chatPosts >= 2, $"chain should have moved past the stalled model, got {chatPosts} posts");
            Assert.True(sw.ElapsedMilliseconds < 15_000, $"watchdog should bound the wait, took {sw.ElapsedMilliseconds} ms");
            var message = Assert.Single(errors);
            Assert.Contains("недоступны", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task SendStreamingAsync_403OnOpenRouter_FallsBackToNvidia_WithoutKeyBlame()
        {
            // Regression: a 403 from chat/completions is NOT a bad key (the
            // settings dialog probes /auth/key where the same key returns 200
            // «OK»). It means the key lacks access to THIS model (guardrail /
            // account scope). The send path must treat it as a model-level
            // failure — try the next fallback (NVIDIA) — and never tell the
            // user to check the API key.
            AppSettingsServiceAi.SaveAiFallbackModels(new[] { "google/gemma-3-27b-it:free" });
            AppSettingsServiceAi.SaveAutoSelectModel(false);

            var tried = new List<string>();
            var handler = new TestHttpMessageHandler(req =>
            {
                var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                var model = ExtractModel(body);
                if (model != null) tried.Add(model);

                return model == "google/gemma-3-27b-it:free"
                    ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                    : SseResponse("Работает через NVIDIA");
            });
            AiAssistantService.HttpClient = new HttpClient(handler);

            var service = new AiAssistantService();
            string? done = null;
            var errors = new List<string>();
            await service.SendStreamingAsync(
                "привет",
                new List<(string Role, string Content)>(),
                _ => { },
                t => done = t,
                e => errors.Add(e));

            Assert.Empty(errors);
            Assert.Equal("Работает через NVIDIA", done);
            Assert.Contains("google/gemma-3-27b-it:free", tried);
            Assert.Contains(tried, m => AiKeyValidator.GetProviderForModel(m) == AiProvider.Nvidia);
            Assert.DoesNotContain(errors, e => e.Contains("Проверьте API-ключ", StringComparison.OrdinalIgnoreCase));
        }

        private static string? ExtractModel(string body)
        {
            const string key = "\"model\"";
            int idx = body.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return null;
            int quote1 = body.IndexOf('"', idx + key.Length);
            if (quote1 < 0) return null;
            int quote2 = body.IndexOf('"', quote1 + 1);
            if (quote2 <= quote1) return null;
            return body.Substring(quote1 + 1, quote2 - quote1 - 1);
        }
    }
}