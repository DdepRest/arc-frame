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
    /// Regression suite for the FreeLLMAPI integration. Real-world failure that
    /// motivated it: the endpoint field held a bare base address
    /// (<c>http://127.0.0.1:31415</c> — the Desktop build's port) without the
    /// <c>/v1</c> API prefix. The gateway answers ANY path outside /api/,
    /// /v1/, /v1beta/ with HTTP 200 + its dashboard web page, so:
    /// <list type="bullet">
    /// <item>the old connection test (any 2xx = success) reported
    /// «✓ шлюз доступен · 12 мс» while nothing worked;</item>
    /// <item>every chat request parsed zero SSE tokens and the user got
    /// «FreeLLMAPI: модели вернули пустой ответ».</item>
    /// </list>
    /// These tests pin the fixes: /v1 is always restored, a web page is never
    /// accepted as a healthy gateway or a model answer, and the final error
    /// names the URL problem instead of blaming the models.
    /// </summary>
    [Collection("FileSystem")]
    public sealed class AiFreeLlmApiTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _originalAiSettingsPath;
        private readonly HttpClient _originalHttpClient;

        public AiFreeLlmApiTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "mnc_ai_freellm_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _originalAiSettingsPath = AppSettingsServiceAi.AiSettingsPath;
            AppSettingsServiceAi.AiSettingsPath = Path.Combine(_tempDir, "ai-settings.json");

            _originalHttpClient = AiAssistantService.HttpClient;
            AiAssistantService.RetryDelayMs = 1;
            AiAssistantService.ResetAvailabilityCache();
            AiAssistantService.ResetAvailableModelsCatalog();
        }

        public void Dispose()
        {
            AiAssistantService.HttpClient = _originalHttpClient;
            AppSettingsServiceAi.AiSettingsPath = _originalAiSettingsPath;
            AiAssistantService.ResetAvailabilityCache();
            AiAssistantService.ResetAvailableModelsCatalog();
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        // ── Endpoint normalization ────────────────────────────────────

        [Theory]
        [InlineData("http://127.0.0.1:31415", "http://127.0.0.1:31415/v1/chat/completions")]
        [InlineData("http://127.0.0.1:31415/", "http://127.0.0.1:31415/v1/chat/completions")]
        [InlineData("  http://127.0.0.1:31415/v1  ", "http://127.0.0.1:31415/v1/chat/completions")]
        [InlineData("http://127.0.0.1:31415/v1/", "http://127.0.0.1:31415/v1/chat/completions")]
        [InlineData("http://localhost:3001/v1beta", "http://localhost:3001/v1beta/chat/completions")]
        [InlineData("http://host:3001/v1/models", "http://host:3001/v1/chat/completions")]
        [InlineData("http://host:3001/v1/chat/completions", "http://host:3001/v1/chat/completions")]
        [InlineData("", "http://localhost:3001/v1/chat/completions")]
        [InlineData(null, "http://localhost:3001/v1/chat/completions")]
        public void BuildFreeLlmApiChatUrl_AlwaysCarriesApiPrefix(string? raw, string expected)
            => Assert.Equal(expected, AiKeyValidator.BuildFreeLlmApiChatUrl(raw));

        [Theory]
        [InlineData("http://127.0.0.1:31415", "http://127.0.0.1:31415/v1/models")]
        [InlineData("http://127.0.0.1:31415/v1/", "http://127.0.0.1:31415/v1/models")]
        [InlineData("http://host:3001/v1/chat/completions", "http://host:3001/v1/models")]
        [InlineData("http://host:3001/v1/models", "http://host:3001/v1/models")]
        public void BuildFreeLlmApiModelsUrl_AlwaysCarriesApiPrefix(string? raw, string expected)
            => Assert.Equal(expected, AiKeyValidator.BuildFreeLlmApiModelsUrl(raw));

        [Fact]
        public void GetApiUrl_FreeLlmApi_NormalizesBareDesktopEndpoint()
        {
            AppSettingsServiceAi.SaveAiFreeLlmApiEndpoint("http://127.0.0.1:31415");

            Assert.Equal("http://127.0.0.1:31415/v1/chat/completions",
                AiKeyValidator.GetApiUrl(AiProvider.FreeLlmApi));
        }

        // ── Honest connectivity test ──────────────────────────────────

        [Fact]
        public async Task TestApiKeyAsync_FreeLlmApi_HtmlDashboardIsNotSuccess()
        {
            // The exact scenario from the bug report: the gateway answers the
            // catalog URL with 200 + its dashboard page. The old probe called
            // that «шлюз доступен»; it must now fail with a URL hint.
            AppSettingsServiceAi.SaveAiFreeLlmApiEndpoint("http://127.0.0.1:31415");
            AppSettingsServiceAi.SaveAiFreeLlmApiKey("freellmapi-test-key");

            var requested = new List<string>();
            AiAssistantService.HttpClient = new HttpClient(new TestHttpMessageHandler(req =>
            {
                requested.Add(req.RequestUri!.AbsoluteUri);
                return HtmlResponse("<!doctype html><html><body>FreeLLMAPI dashboard</body></html>");
            }));

            var result = await AiAssistantService.TestApiKeyAsync(AiProvider.FreeLlmApi, "freellmapi-test-key");

            Assert.False(result.IsOk);
            Assert.Contains("/v1", result.Detail);
            Assert.Contains("веб-страница", result.Detail);
            // The probe must aim at /v1/models — the missing prefix was the bug.
            Assert.Contains("http://127.0.0.1:31415/v1/models", requested);
            Assert.Single(requested);
        }

        [Fact]
        public async Task TestApiKeyAsync_FreeLlmApi_ValidCatalogAndLiveChatIsSuccess()
        {
            // A healthy gateway: /v1/models returns a real catalog and a live
            // 1-message chat answers — only then is the test allowed to pass.
            AppSettingsServiceAi.SaveAiFreeLlmApiEndpoint("http://127.0.0.1:31415");
            AppSettingsServiceAi.SaveAiFreeLlmApiKey("freellmapi-test-key");

            var paths = new List<string>();
            string chatBody = "";
            AiAssistantService.HttpClient = new HttpClient(new TestHttpMessageHandler(req =>
            {
                var uri = req.RequestUri!;
                paths.Add($"{req.Method} {uri.AbsolutePath}");
                Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
                Assert.Equal("freellmapi-test-key", req.Headers.Authorization?.Parameter);

                if (req.Method == HttpMethod.Get)
                {
                    return JsonResponse(
                        "{\"object\":\"list\",\"data\":[" +
                        "{\"id\":\"auto\"}," +
                        "{\"id\":\"gemma-3-12b-it\"}," +
                        "{\"id\":\"flux-1-schnell\"}]}");
                }

                chatBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonResponse("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"pong\"}}]}");
            }));

            var result = await AiAssistantService.TestApiKeyAsync(AiProvider.FreeLlmApi, "freellmapi-test-key");

            Assert.True(result.IsOk, result.Detail);
            Assert.Contains("3 моделей", result.Detail);
            Assert.Contains("auto", result.Detail); // the router is the probe model
            Assert.Contains("GET /v1/models", paths);
            Assert.Contains("POST /v1/chat/completions", paths);
            // The live probe asks the virtual router — it resolves an upstream itself.
            Assert.Contains("\"model\":\"auto\"", chatBody);
        }

        [Fact]
        public async Task TestApiKeyAsync_FreeLlmApi_CatalogOkButChatFails_IsFailure()
        {
            AppSettingsServiceAi.SaveAiFreeLlmApiEndpoint("http://127.0.0.1:31415");

            AiAssistantService.HttpClient = new HttpClient(new TestHttpMessageHandler(req =>
                req.Method == HttpMethod.Get
                    ? JsonResponse("{\"data\":[{\"id\":\"auto\"}]}")
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError)));

            var result = await AiAssistantService.TestApiKeyAsync(AiProvider.FreeLlmApi, "");

            Assert.False(result.IsOk);
            Assert.Contains("каталог доступен", result.Detail);
            Assert.Contains("чат", result.Detail);
        }

        [Fact]
        public async Task TestApiKeyAsync_FreeLlmApi_NonJsonCatalogIsRejected()
        {
            AppSettingsServiceAi.SaveAiFreeLlmApiEndpoint("http://127.0.0.1:31415");

            AiAssistantService.HttpClient = new HttpClient(new TestHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("not-a-catalog", Encoding.UTF8, "text/plain")
                }));

            var result = await AiAssistantService.TestApiKeyAsync(AiProvider.FreeLlmApi, "");

            Assert.False(result.IsOk);
            Assert.Contains("OpenAI-совместимым", result.Detail);
        }

        // ── Catalog filtering ─────────────────────────────────────────

        [Theory]
        [InlineData("auto", true)]
        [InlineData("auto:fast", true)]
        [InlineData("auto:vision", true)]
        [InlineData("fusion", true)]
        [InlineData("gpt-4o", true)]
        [InlineData("glm-5.3", true)]
        [InlineData("gpt-oss-20b", true)]
        [InlineData("gemma-3-12b-it", true)]
        [InlineData("qwen3-vl-235b-a22b-instruct", true)]
        [InlineData("flux-1-schnell", false)]
        [InlineData("dall-e-3", false)]
        [InlineData("whisper-large-v3", false)]
        [InlineData("text-embedding-3-small", false)]
        [InlineData("bge-reranker-v2", false)]
        [InlineData("llama-guard-4", false)]
        [InlineData("", false)]
        public void IsFreeLlmChatModel_FiltersGeneratorsAndKeepsChat(string id, bool expected)
            => Assert.Equal(expected, AiModelCatalogClient.IsFreeLlmChatModel(id));

        [Fact]
        public async Task FetchFreeLlmApiModelsAsync_FiltersNonChatEntries_KeepsRouters()
        {
            // The gateway catalog mixes chat models, media generators, embeddings
            // and its virtual routers. Only chat-capable entries may reach the
            // picker (and the fallback chain), routers included unconditionally.
            var requested = new List<string>();
            var handler = new TestHttpMessageHandler(req =>
            {
                requested.Add(req.RequestUri!.AbsoluteUri);
                return JsonResponse(
                    "{\"data\":[" +
                    "{\"id\":\"auto\"}," +
                    "{\"id\":\"fusion\"}," +
                    "{\"id\":\"gemma-3-12b-it\",\"name\":\"Gemma 3 12B\"}," +
                    "{\"id\":\"qwen2.5-vl-72b\"}," +
                    "{\"id\":\"flux-1-schnell\"}," +
                    "{\"id\":\"text-embedding-3-large\"}," +
                    "{\"id\":\"whisper-large-v3\"}]}");
            });

            var result = await AiModelCatalogClient.FetchFreeLlmApiModelsAsync(
                new HttpClient(handler), "freellmapi-test-key", "http://127.0.0.1:31415", default);

            Assert.True(result.IsFromApi);
            var ids = result.Models.Select(m => m.Id).ToList();
            Assert.Contains("auto", ids);
            Assert.Contains("fusion", ids);
            Assert.Contains("gemma-3-12b-it", ids);
            Assert.Contains("qwen2.5-vl-72b", ids); // vision chat model stays
            Assert.DoesNotContain("flux-1-schnell", ids);
            Assert.DoesNotContain("text-embedding-3-large", ids);
            Assert.DoesNotContain("whisper-large-v3", ids);
            // A bare base URL still targets the /v1 API (Desktop port 31415).
            Assert.Single(requested);
            Assert.Equal("http://127.0.0.1:31415/v1/models", requested[0]);
        }

        // ── Send path: 2xx + web page must name the real cause ────────

        [Fact]
        public async Task SendStreamingAsync_FreeLlmApiDashboardHtml_ReportsUrlProblem_NotEmptyModels()
        {
            // End-to-end regression for the screenshot: every "chat" request is
            // answered by the gateway dashboard with 200 + HTML. The user must
            // see that the endpoint returns a web page (and which URL), not
            // «модели вернули пустой ответ».
            AppSettingsServiceAi.SaveAiFreeLlmApiEndpoint("http://127.0.0.1:31415");
            AppSettingsServiceAi.SaveAiFreeLlmApiKey("freellmapi-test-key");
            AppSettingsServiceAi.SaveAiFallbackModels(new[] { "auto" });
            AppSettingsServiceAi.SaveAutoSelectModel(false);

            var posts = new List<string>();
            AiAssistantService.HttpClient = new HttpClient(new TestHttpMessageHandler(req =>
            {
                var uri = req.RequestUri!;
                if (req.Method == HttpMethod.Post)
                {
                    posts.Add(uri.AbsoluteUri);
                    return HtmlResponse("<!doctype html><html><body>FreeLLMAPI dashboard</body></html>");
                }

                // Catalog refresh between the two passes.
                if (uri.AbsoluteUri.Contains("31415"))
                    return JsonResponse("{\"data\":[{\"id\":\"auto\"}]}");
                if (uri.AbsoluteUri.Contains("openrouter"))
                    return JsonResponse("{\"data\":[{\"id\":\"google/gemma-4-31b-it:free\",\"name\":\"Gemma\",\"pricing\":{\"prompt\":\"0\",\"completion\":\"0\"}}]}");
                return JsonResponse("{\"data\":[{\"id\":\"deepseek-ai/deepseek-v4-flash-0731\",\"name\":\"DeepSeek\"}]}");
            }));

            var errors = new List<string>();
            var service = new AiAssistantService();
            await service.SendStreamingAsync(
                "привет",
                new List<(string Role, string Content)>(),
                _ => { },
                _ => { },
                e => errors.Add(e));

            var message = Assert.Single(errors);
            Assert.Contains("веб-страница", message);
            Assert.Contains("http://127.0.0.1:31415/v1/chat/completions", message);
            // The old generic text must be gone from this scenario…
            Assert.DoesNotContain("модели вернули пустой ответ", message);
            // …and the empty answer must not be retried three times per pass.
            Assert.Equal(2, posts.Count);
            Assert.All(posts, url => Assert.Equal("http://127.0.0.1:31415/v1/chat/completions", url));
        }

        // ── Helpers ───────────────────────────────────────────────────

        private static HttpResponseMessage JsonResponse(string json)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

        private static HttpResponseMessage HtmlResponse(string html)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            };
    }
}
