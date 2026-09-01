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
    /// Verifies vision-capability detection: the OpenRouter catalog's
    /// <c>architecture.input_modalities</c> must flow into
    /// <see cref="AiModelOption.SupportsVision"/> (persisted in the cache), and
    /// routing must try metadata-flagged vision models first when the user
    /// attaches photos — even when the model name carries no vision marker.
    /// </summary>
    [Collection("FileSystem")]
    public sealed class AiAssistantServiceVisionTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _originalAiSettingsPath;
        private readonly HttpClient _originalHttpClient;

        public AiAssistantServiceVisionTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "mnc_ai_vision_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _originalAiSettingsPath = AppSettingsServiceAi.AiSettingsPath;
            AppSettingsServiceAi.AiSettingsPath = Path.Combine(_tempDir, "ai-settings.json");

            _originalHttpClient = AiAssistantService.HttpClient;
            // The production service now requires explicit user configuration;
            // use harmless fixture values so these tests exercise HTTP routing.
            AppSettingsServiceAi.SaveAiApiKey("test-openrouter-key");
            AppSettingsServiceAi.SaveAiNvidiaApiKey("test-nvidia-key");
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

        private static HttpResponseMessage JsonResponse(string json)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

        /// <summary>Stubs both catalogs with the given OpenRouter entries.</summary>
        private static HttpClient CreateCatalogClient(params string[] openRouterEntries)
        {
            string orJson = "{\"data\":[" + string.Join(",", openRouterEntries) + "]}";
            string nvJson = "{\"data\":[{\"id\":\"deepseek-ai/deepseek-v4-flash-0731\",\"name\":\"DeepSeek V4 Flash\"}]}";

            var handler = new TestHttpMessageHandler(req =>
            {
                if (req.RequestUri!.AbsoluteUri.Contains("openrouter.ai"))
                    return JsonResponse(orJson);
                return JsonResponse(nvJson);
            });
            return new HttpClient(handler);
        }

        private static string OrEntry(
            string id,
            bool withImage,
            bool withArchitecture = true)
        {
            string architecture = withArchitecture
                ? $",\"architecture\":{{\"input_modalities\":[{(withImage ? "\"text\",\"image\"" : "\"text\"")}]}}"
                : "";
            return $"{{\"id\":\"{id}\",\"name\":\"{id}\",\"pricing\":{{\"prompt\":\"0\",\"completion\":\"0\"}}{architecture}}}";
        }

        [Fact]
        public async Task FetchAvailableModelsAsync_ParsesVisionMetadataFromCatalog()
        {
            AiAssistantService.HttpClient = CreateCatalogClient(
                OrEntry("vendor/photo-reader", withImage: true),
                OrEntry("vendor/text-only", withImage: false),
                OrEntry("vendor/no-architecture", withImage: false, withArchitecture: false));

            var models = await AiAssistantService.FetchAvailableModelsAsync(
                apiKey: "", forceRefresh: true);

            var vision = models.First(m => m.Id == "vendor/photo-reader");
            Assert.True(vision.SupportsVision);

            var textOnly = models.First(m => m.Id == "vendor/text-only");
            Assert.False(textOnly.SupportsVision);

            var noArch = models.First(m => m.Id == "vendor/no-architecture");
            Assert.False(noArch.SupportsVision);

            // NVIDIA catalog exposes no modality metadata → unknown (null),
            // so routing falls back to name heuristics for those.
            var nvidia = models.First(m => m.Provider == AiProvider.Nvidia);
            Assert.Null(nvidia.SupportsVision);
        }

        [Fact]
        public async Task FetchAvailableModelsAsync_VisionMetadata_SurvivesCacheRoundtrip()
        {
            AiAssistantService.HttpClient = CreateCatalogClient(
                OrEntry("vendor/photo-reader", withImage: true));

            var first = await AiAssistantService.FetchAvailableModelsAsync("", forceRefresh: true);
            Assert.True(first.First(m => m.Id == "vendor/photo-reader").SupportsVision);

            // Reload from the persisted cache — the flag must not be lost.
            var (cached, _) = AppSettingsServiceAi.LoadCachedModels();
            Assert.True(cached.First(m => m.Id == "vendor/photo-reader").SupportsVision);
        }

        [Fact]
        public async Task SendStreamingAsync_WithImage_RouterFirst_NotSlowVisionModel()
        {
            // With image requests the Free Models Router must lead the chain,same
            // as text requests: OpenRouter resolves a vision-capable free model
            // server-side, and promoting individual (slow) vision models made
            // image requests minutes long. The vision-capable "photo-reader"
            // must NOT be pushed ahead of the router even though it has the
            // correct metadata.
            AiAssistantService.HttpClient = CreateCatalogClient(
                OrEntry("vendor/photo-reader", withImage: true),
                OrEntry("vendor/text-only", withImage: false));

            await AiAssistantService.FetchAvailableModelsAsync("", forceRefresh: true);
            AppSettingsServiceAi.SaveAutoSelectModel(true);
            AppSettingsServiceAi.SaveAiFallbackModels(Array.Empty<string>());

            var requestedModels = new List<string>();
            var handler = new TestHttpMessageHandler(req =>
            {
                requestedModels.Add(ReadModelFromBody(req));
                return SseResponse("Вижу фото");
            });
            AiAssistantService.HttpClient = new HttpClient(handler);

            var service = new AiAssistantService();
            await service.SendStreamingAsync(
                "Что на фото?",
                new List<(string Role, string Content)>(),
                _ => { },
                _ => { },
                _ => { },
                imageDataUrls: new[] { "data:image/png;base64,AAAA" });

            Assert.NotEmpty(requestedModels);
            Assert.Equal(AiAssistantService.OpenRouterFreeRouter, requestedModels[0]);
        }

        [Fact]
        public async Task SendStreamingAsync_NoImage_DoesNotReorderChain()
        {
            // Without images the chain keeps the task-ranked order: the vision
            // model must NOT be promoted ahead of the general chat model.
            AiAssistantService.HttpClient = CreateCatalogClient(
                OrEntry("vendor/aaa-text-only", withImage: false),
                OrEntry("vendor/photo-reader", withImage: true));

            await AiAssistantService.FetchAvailableModelsAsync("", forceRefresh: true);
            AppSettingsServiceAi.SaveAutoSelectModel(true);
            AppSettingsServiceAi.SaveAiFallbackModels(Array.Empty<string>());

            var requestedModels = new List<string>();
            var handler = new TestHttpMessageHandler(req =>
            {
                requestedModels.Add(ReadModelFromBody(req));
                return SseResponse("Привет");
            });
            AiAssistantService.HttpClient = new HttpClient(handler);

            var service = new AiAssistantService();
            await service.SendStreamingAsync(
                "Привет",
                new List<(string Role, string Content)>(),
                _ => { },
                _ => { },
                _ => { });

            Assert.NotEmpty(requestedModels);
            // Task ranking (deepseek scores highest) wins — the vision model
            // must NOT be promoted ahead of it without an attached image.
            Assert.DoesNotContain("photo-reader", requestedModels[0]);
        }

        private static HttpResponseMessage SseResponse(string text)
        {
            var body = $"data: {{\"choices\":[{{\"delta\":{{\"content\":\"{text}\"}}}}]}}\n\ndata: [DONE]\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
            };
        }

        private static string ReadModelFromBody(HttpRequestMessage req)
        {
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("model").GetString() ?? "";
        }
    }
}
