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
        }

        public void Dispose()
        {
            AiAssistantService.HttpClient = _originalHttpClient;
            AiAssistantService.MaxAttemptsPerModel = _originalMaxAttempts;
            AiAssistantService.RetryDelayMs = _originalRetryDelay;
            AppSettingsServiceAi.AiSettingsPath = _originalAiSettingsPath;
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
                AiAssistantService.GetProviderForModel(m) == AiProvider.Nvidia);
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
