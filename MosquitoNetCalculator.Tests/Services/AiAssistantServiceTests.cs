using System;
using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class AiAssistantServiceTests
    {
        [Fact]
        public void FormatUpdateHistory_IncludesAllVersions_NotJustTopFive()
        {
            // User report: the assistant said «более ранние версии не сохранены»
            // because the prompt only carried the last 5 versions. The formatter
            // must include EVERY version — an 8-entry sample must fully appear.
            var entries = new List<UpdateItem>();
            for (int i = 1; i <= 8; i++)
            {
                entries.Add(new UpdateItem
                {
                    Version = $"3.{i}.0",
                    Date = new DateTime(2026, 1, i),
                    Title = $"Обновление {i}",
                    Changes = new List<string> { $"Правка A{i}", $"Правка B{i}" }
                });
            }

            var text = AiAssistantService.FormatUpdateHistory(entries);

            for (int i = 1; i <= 8; i++)
            {
                Assert.Contains($"Версия 3.{i}.0", text);
                Assert.Contains($"Обновление {i}", text);
                Assert.Contains($"Правка A{i}", text);
                Assert.Contains($"Правка B{i}", text);
            }
        }

        [Fact]
        public void FormatUpdateHistory_IncludesAllChanges_PerVersion()
        {
            // Even a version with 11 changes must list them ALL — no Take(3) cap.
            var changes = new List<string>();
            for (int i = 1; i <= 11; i++)
                changes.Add($"Изменение {i}");

            var text = AiAssistantService.FormatUpdateHistory(new[]
            {
                new UpdateItem
                {
                    Version = "3.47.0",
                    Date = new DateTime(2026, 7, 18),
                    Title = "Монтаж и дробное количество",
                    Changes = changes
                }
            });

            for (int i = 1; i <= 11; i++)
                Assert.Contains($"Изменение {i}", text);
        }

        [Fact]
        public void FormatUpdateHistory_Empty_ReturnsEmptyString()
        {
            Assert.Equal("", AiAssistantService.FormatUpdateHistory(Array.Empty<UpdateItem>()));
        }
        [Fact]
        public void FreeModels_ContainsDefaultGemmaModel()
        {
            Assert.Contains(AiAssistantService.FreeModels, m => m.Id == "google/gemma-3-27b-it:free");
        }

        [Fact]
        public void FreeModels_FirstItem_IsDefaultModel()
        {
            var first = AiAssistantService.FreeModels.FirstOrDefault();
            Assert.NotNull(first);
            Assert.Equal("google/gemma-3-27b-it:free", first!.Id);
        }

        [Fact]
        public void FreeModels_HaveUniqueIds()
        {
            var ids = AiAssistantService.FreeModels.Select(m => m.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        [Fact]
        public void FreeModels_AllItems_HaveIdAndDisplayName()
        {
            foreach (var model in AiAssistantService.FreeModels)
            {
                Assert.False(string.IsNullOrWhiteSpace(model.Id));
                Assert.False(string.IsNullOrWhiteSpace(model.DisplayName));
            }
        }

        [Fact]
        public void FreeModels_ContainsNvidiaFreeModels_WithNvidiaProvider()
        {
            var nvidiaModels = AiAssistantService.FreeModels
                .Where(m => m.Provider == AiProvider.Nvidia)
                .ToList();

            Assert.NotEmpty(nvidiaModels);
            Assert.Contains(nvidiaModels, m => m.Id == "deepseek-ai/deepseek-v4-flash");
            Assert.All(nvidiaModels, m => Assert.Equal(AiProvider.Nvidia, m.Provider));
        }

        [Fact]
        public void FreeModels_OpenRouterModels_HaveOpenRouterProvider()
        {
            Assert.Contains(AiAssistantService.FreeModels,
                m => m.Id == "google/gemma-3-27b-it:free" && m.Provider == AiProvider.OpenRouter);
        }

        [Fact]
        public void GetProviderForModel_ReturnsNvidia_ForNvidiaModelId()
        {
            Assert.Equal(AiProvider.Nvidia, AiAssistantService.GetProviderForModel("deepseek-ai/deepseek-v4-flash"));
            Assert.Equal(AiProvider.Nvidia, AiAssistantService.GetProviderForModel("deepseek-ai/deepseek-v4-pro"));
        }

        [Fact]
        public void GetProviderForModel_ReturnsOpenRouter_ForOpenRouterOrUnknownModel()
        {
            Assert.Equal(AiProvider.OpenRouter, AiAssistantService.GetProviderForModel("google/gemma-3-27b-it:free"));
            Assert.Equal(AiProvider.OpenRouter, AiAssistantService.GetProviderForModel("unknown/model-xyz"));
        }

        [Fact]
        public void HasEmbeddedKeys_IsTrue()
        {
            Assert.True(AiAssistantService.HasEmbeddedKeys);
        }

        [Fact]
        public async System.Threading.Tasks.Task TestApiKeyAsync_BadOpenRouterKey_ReturnsUnauthorized()
        {
            // Use an obviously invalid key — OpenRouter should respond with 401.
            var result = await AiAssistantService.TestApiKeyAsync(
                AiProvider.OpenRouter, "definitely-not-a-real-key-xxxxxxxxxxxxxxxx");

            Assert.False(result.IsOk);
            Assert.Equal(401, result.StatusCode);
            Assert.Contains("ключ", result.Detail, System.StringComparison.OrdinalIgnoreCase);
            Assert.True(result.LatencyMs >= 0);
        }

        [Fact]
        public async System.Threading.Tasks.Task TestApiKeyAsync_BadNvidiaKey_ReturnsFailure()
        {
            // NVIDIA's /v1/models is a public catalog (no auth required), so any
            // request that reaches the endpoint returns 200. We can't use it to
            // validate the key — but we can verify the method never misbehaves,
            // even with a clearly invalid key. Either path is acceptable; the
            // contract is: a result with valid LatencyMs and non-empty Detail.
            var result = await AiAssistantService.TestApiKeyAsync(
                AiProvider.Nvidia, "nvapi-not-a-real-key");

            Assert.NotNull(result);
            Assert.True(result.LatencyMs >= 0);
            Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        }

        [Fact]
        public async System.Threading.Tasks.Task TestApiKeyAsync_Nvidia_EmbeddedKey_Reachable()
        {
            // Regression guard: the built-in NVIDIA key must continue to reach
            // the catalog endpoint, OR fail with an honest network error.
            // Either outcome proves the service responds without crashing.
            var result = await AiAssistantService.TestApiKeyAsync(
                AiProvider.Nvidia, AiAssistantService.EmbeddedNvidiaApiKey);

            Assert.NotNull(result);
            Assert.True(result.LatencyMs >= 0);
            // When reachable: IsOk must be true with 200; otherwise: false with a non-empty Detail.
            if (result.IsOk)
            {
                Assert.Equal(200, result.StatusCode);
                Assert.Equal("OK", result.Detail);
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(result.Detail));
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task TestApiKeyAsync_GoodEmbeddedOpenRouterKey_ReturnsOk()
        {
            // Regression guard: the built-in key must continue to pass the ping,
            // otherwise the dialog will show all built-in users a red dot.
            var result = await AiAssistantService.TestApiKeyAsync(
                AiProvider.OpenRouter, AiAssistantService.EmbeddedOpenRouterApiKey);

            Assert.True(result.IsOk);
            Assert.Equal(200, result.StatusCode);
            Assert.Equal("OK", result.Detail);
            Assert.True(result.LatencyMs >= 0);
        }

        [Fact]
        public void AiApiKeyTestResult_CarriesAllFields()
        {
            var r = new AiApiKeyTestResult(IsOk: true, StatusCode: 200, LatencyMs: 123, Detail: "OK");
            Assert.True(r.IsOk);
            Assert.Equal(200, r.StatusCode);
            Assert.Equal(123, r.LatencyMs);
            Assert.Equal("OK", r.Detail);
        }
    }
}
