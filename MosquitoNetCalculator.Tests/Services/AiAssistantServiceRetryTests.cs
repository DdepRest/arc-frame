using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Verifies that the streaming path retries transient failures and always
    /// falls back to an NVIDIA free model when the OpenRouter chain is dead.
    /// </summary>
    [Collection("FileSystem")]
    public sealed class AiAssistantServiceRetryTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _originalAiSettingsPath;
        private readonly HttpClient _originalHttpClient;
        private readonly int _originalMaxAttempts;
        private readonly int _originalRetryDelay;

        public AiAssistantServiceRetryTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "mnc_ai_retry_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _originalAiSettingsPath = AppSettingsServiceAi.AiSettingsPath;
            AppSettingsServiceAi.AiSettingsPath = Path.Combine(_tempDir, "ai-settings.json");

            _originalHttpClient = AiAssistantService.HttpClient;
            _originalMaxAttempts = AiAssistantService.MaxAttemptsPerModel;
            _originalRetryDelay = AiAssistantService.RetryDelayMs;

            // Keep tests fast and deterministic.
            AiAssistantService.MaxAttemptsPerModel = 3;
            AiAssistantService.RetryDelayMs = 1;

            // The availability cache and routing catalog are process-wide; isolate
            // each test.
            AiAssistantService.ResetAvailabilityCache();
            AiAssistantService.ResetAvailableModelsCatalog();
        }

        public void Dispose()
        {
            AiAssistantService.HttpClient = _originalHttpClient;
            AiAssistantService.MaxAttemptsPerModel = _originalMaxAttempts;
            AiAssistantService.RetryDelayMs = _originalRetryDelay;
            AppSettingsServiceAi.AiSettingsPath = _originalAiSettingsPath;
            AiAssistantService.ResetAvailabilityCache();
            AiAssistantService.ResetAvailableModelsCatalog();
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        private static HttpResponseMessage SseResponse(string text)
        {
            var body = $"data: {{\"choices\":[{{\"delta\":{{\"content\":\"{text}\"}}}}]}}\n\ndata: [DONE]\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
            };
        }

        [Fact]
        public async Task SendStreamingAsync_RetriesTransientFailure_ThenSucceeds()
        {
            AppSettingsServiceAi.SaveAiFallbackModels(new[] { "google/gemma-3-27b-it:free" });
            AppSettingsServiceAi.SaveAutoSelectModel(false);

            int requests = 0;
            var handler = new TestHttpMessageHandler(_ =>
            {
                requests++;
                return requests < 3
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : SseResponse("Привет");
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

            Assert.Equal(3, requests);      // 2 transient failures + 1 success
            Assert.Equal("Привет", done);
            Assert.Empty(errors);
        }

        [Fact]
        public async Task SendStreamingAsync_GivesUpAfterMaxAttemptsPerModel()
        {
            AppSettingsServiceAi.SaveAiFallbackModels(new[] { "google/gemma-3-27b-it:free" });
            AppSettingsServiceAi.SaveAutoSelectModel(false);

            int requests = 0;
            var handler = new TestHttpMessageHandler(_ =>
            {
                requests++;
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
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

            // 3 attempts for the OpenRouter model, then NVIDIA fallbacks are
            // appended automatically (each also tries up to 3 times).
            Assert.True(requests >= 3, $"Expected at least 3 requests, got {requests}");
            Assert.NotEmpty(errors);
            // Error must not hard-code OpenRouter — both providers may fail.
            Assert.DoesNotContain("OpenRouter недоступен", errors[0]);
            Assert.Contains("недоступны", errors[0]);
        }

        [Fact]
        public async Task SendStreamingAsync_TriesNvidiaFallback_WhenOpenRouterChainFails()
        {
            // Manual selection with ONLY an OpenRouter model.
            AppSettingsServiceAi.SaveAiFallbackModels(new[] { "google/gemma-3-27b-it:free" });
            AppSettingsServiceAi.SaveAutoSelectModel(false);

            var triedModels = new List<string>();
            var handler = new TestHttpMessageHandler(req =>
            {
                var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                var model = ExtractModel(body);
                if (model != null) triedModels.Add(model);
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
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

            // Even though the user picked an OpenRouter-only model, the chain
            // must include at least one NVIDIA model as the second provider.
            Assert.Contains(triedModels, m =>
                AiKeyValidator.GetProviderForModel(m) == AiProvider.Nvidia);
        }

        [Fact]
        public async Task SendStreamingAsync_WithImage_SendsMultimodalUserContent()
        {
            // Vision-capable model selected explicitly; the image must be
            // serialized as an OpenAI-compatible image_url part on the user message.
            AppSettingsServiceAi.SaveAiFallbackModels(new[] { "google/gemma-3-27b-it:free" });
            AppSettingsServiceAi.SaveAutoSelectModel(false);

            string? capturedBody = null;
            var handler = new TestHttpMessageHandler(req =>
            {
                capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return SseResponse("Вижу изображение");
            });
            AiAssistantService.HttpClient = new HttpClient(handler);

            var service = new AiAssistantService();
            string? done = null;
            await service.SendStreamingAsync(
                "Что на картинке?",
                new List<(string Role, string Content)>(),
                _ => { },
                t => done = t,
                _ => { },
                imageDataUrls: new[] { "data:image/png;base64,AAAA" });

            Assert.Equal("Вижу изображение", done);
            Assert.NotNull(capturedBody);

            // The last message must be the user message with a text part followed
            // by an image_url part carrying the base64 data URL (parse, don't
            // string-match — System.Text.Json escapes non-ASCII characters).
            using var doc = System.Text.Json.JsonDocument.Parse(capturedBody);
            var messages = doc.RootElement.GetProperty("messages");
            var user = messages[messages.GetArrayLength() - 1];
            Assert.Equal("user", user.GetProperty("role").GetString());

            var content = user.GetProperty("content");
            Assert.Equal(System.Text.Json.JsonValueKind.Array, content.ValueKind);
            Assert.Equal(2, content.GetArrayLength());

            Assert.Equal("text", content[0].GetProperty("type").GetString());
            Assert.Equal("Что на картинке?", content[0].GetProperty("text").GetString());

            Assert.Equal("image_url", content[1].GetProperty("type").GetString());
            Assert.Equal("data:image/png;base64,AAAA",
                content[1].GetProperty("image_url").GetProperty("url").GetString());
        }

        [Fact]
        public async Task SendStreamingAsync_StripsPaddingTokensFromStreamedChunks()
        {
            // A text-only model that received an image part answers with a wall of
            // <pad>/<|...|> tokens. Those tokens must never reach the chat bubble.
            AppSettingsServiceAi.SaveAiFallbackModels(new[] { "google/gemma-3-27b-it:free" });
            AppSettingsServiceAi.SaveAutoSelectModel(false);

            var body =
                "data: {\"choices\":[{\"delta\":{\"content\":\"<pad><pad>\"}}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"content\":\"Здравствуйте\"}}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"content\":\"<|image|><pad>\"}}]}\n\n" +
                "data: [DONE]\n";

            AiAssistantService.HttpClient = new HttpClient(new TestHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
                }));

            var chunks = new List<string>();
            var service = new AiAssistantService();
            string? done = null;
            var errors = new List<string>();
            await service.SendStreamingAsync(
                "привет",
                new List<(string Role, string Content)>(),
                c => chunks.Add(c),
                t => done = t,
                e => errors.Add(e));

            Assert.Empty(errors);
            Assert.Equal("Здравствуйте", done);
            Assert.All(chunks, c => Assert.DoesNotContain("<pad>", c, StringComparison.OrdinalIgnoreCase));
            Assert.All(chunks, c => Assert.DoesNotContain("<|", c, StringComparison.Ordinal));
        }

        [Fact]
        public async Task SendStreamingAsync_PaddingOnlyResponse_FallsBackToNextModel()
        {
            // The first model streams ONLY padding tokens; it must be skipped
            // (no pointless retries) and the next fallback model must answer.
            AppSettingsServiceAi.SaveAiFallbackModels(new[] { "google/gemma-3-27b-it:free" });
            AppSettingsServiceAi.SaveAutoSelectModel(false);

            var tried = new List<string>();
            var handler = new TestHttpMessageHandler(req =>
            {
                var reqBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                var model = ExtractModel(reqBody);
                if (model != null) tried.Add(model);

                if (model == "google/gemma-3-27b-it:free")
                {
                    var padBody =
                        "data: {\"choices\":[{\"delta\":{\"content\":\"<pad><pad><pad>\"}}]}\n\n" +
                        "data: [DONE]\n";
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(padBody, Encoding.UTF8, "text/event-stream")
                    };
                }
                return SseResponse("Работаю");
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
            Assert.Equal("Работаю", done);
            Assert.Contains("google/gemma-3-27b-it:free", tried);
            Assert.Equal(1, tried.Count(m => m == "google/gemma-3-27b-it:free"));
            Assert.Contains(tried, m => AiKeyValidator.GetProviderForModel(m) == AiProvider.Nvidia);
        }

        [Fact]
        public async Task SendStreamingAsync_SkipsUnavailableFreeModel_AndNeverTriesPaidSlug()
        {
            // Free-only routing: a 404 on a ":free" model must NOT expand to the
            // paid slug. The chain moves to the next free fallback (NVIDIA) instead.
            AppSettingsServiceAi.SaveAiFallbackModels(new[] { "google/gemma-3-27b-it:free" });
            AppSettingsServiceAi.SaveAutoSelectModel(false);

            var tried = new List<string>();
            var handler = new TestHttpMessageHandler(req =>
            {
                var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                var model = ExtractModel(body);
                if (model != null) tried.Add(model);
                return model == "google/gemma-3-27b-it:free"
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : SseResponse("Привет");
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

            Assert.Equal("Привет", done);
            Assert.Empty(errors);
            Assert.Contains("google/gemma-3-27b-it:free", tried);
            Assert.Contains("deepseek-ai/deepseek-v4-flash-0731", tried);
            Assert.DoesNotContain("google/gemma-3-27b-it", tried);
        }

        private static HttpResponseMessage CompletionResponse(string text)
        {
            var body = $"{{\"choices\":[{{\"message\":{{\"content\":\"{text}\"}}}}]}}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }

        [Fact]
        public async Task AnalyzeAvailableModelsAsync_KeepsOnlyModelsThatAnswer()
        {
            var handler = new TestHttpMessageHandler(req =>
            {
                var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                var model = ExtractModel(body);
                return model == "google/gemma-3-27b-it:free"
                    ? CompletionResponse("pong")
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            AiAssistantService.HttpClient = new HttpClient(handler);

            var candidates = new List<AiModelOption>
            {
                new("google/gemma-3-27b-it:free", "Gemma", AiProvider.OpenRouter),
                new("some/dead-model:free", "Dead", AiProvider.OpenRouter)
            };

            var results = await AiAssistantService.AnalyzeAvailableModelsAsync(candidates);

            Assert.Equal(2, results.Count);
            Assert.True(results.Single(r => r.Id == "google/gemma-3-27b-it:free").IsAvailable);

            var dead = results.Single(r => r.Id == "some/dead-model:free");
            Assert.False(dead.IsAvailable);
            Assert.Equal(404, dead.StatusCode);

            // Auto-analysis narrows the routing catalog to verified-available models.
            Assert.Contains(AiAssistantService.AvailableModels,
                m => m.Id == "google/gemma-3-27b-it:free");
            Assert.DoesNotContain(AiAssistantService.AvailableModels,
                m => m.Id == "some/dead-model:free");
        }

        [Fact]
        public async Task AnalyzeAvailableModelsAsync_ReportsExhaustedKey()
        {
            var handler = new TestHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.Forbidden));
            AiAssistantService.HttpClient = new HttpClient(handler);

            var candidates = new List<AiModelOption>
            {
                new("google/gemma-3-27b-it:free", "Gemma", AiProvider.OpenRouter)
            };

            var results = await AiAssistantService.AnalyzeAvailableModelsAsync(candidates);

            var result = Assert.Single(results);
            Assert.False(result.IsAvailable);
            Assert.Equal(403, result.StatusCode);
            Assert.Contains("ключ", result.Detail, StringComparison.OrdinalIgnoreCase);

            // A fully-failed probe must not narrow the catalog to empty.
            Assert.NotEmpty(AiAssistantService.AvailableModels);
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
