using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class AiModelSelectorTests
    {
        [Fact]
        public void SelectForTask_PrioritizesStrongCalculatorModels()
        {
            var models = new[]
            {
                new AiModelOption("mistralai/mistral-7b-instruct", "Mistral 7B"),
                new AiModelOption("google/gemini-2.5-pro-exp:free", "Google Gemini 2.5 Pro (free)"),
                new AiModelOption("deepseek-ai/deepseek-v4-pro", "DeepSeek V4 Pro", AiProvider.Nvidia)
            };

            var selected = AiModelSelector.SelectForTask(AiTaskType.Calculator, models);

            Assert.Equal(3, selected.Count);
            Assert.Contains("google/gemini-2.5-pro-exp:free", selected.Take(2));
            Assert.Contains("deepseek-ai/deepseek-v4-pro", selected.Take(2));
        }

        [Fact]
        public void MergeWithUserSelection_PrioritizesAutoModels_AndKeepsUserFallbacks()
        {
            var merged = AiModelSelector.MergeWithUserSelection(
                new[] { "auto-a", "user-a", "auto-b" },
                new[] { "user-a", "user-b", "user-a" });

            Assert.Equal(new[] { "auto-a", "auto-b", "user-a", "user-b" }, merged);
        }

        [Fact]
        public void AutoSelection_UsesTaskRankingBeforeManuallyCheckedModels()
        {
            var autoOrdered = AiModelSelector.SelectForTask(
                AiTaskType.Calculator,
                new[]
                {
                    new AiModelOption("mistralai/mistral-7b-instruct", "Mistral 7B"),
                    new AiModelOption("google/gemini-2.5-pro-exp:free", "Google Gemini 2.5 Pro (free)")
                });

            var merged = AiModelSelector.MergeWithUserSelection(
                autoOrdered,
                new[] { "mistralai/mistral-7b-instruct" });

            Assert.Equal("google/gemini-2.5-pro-exp:free", merged[0]);
            Assert.Equal("mistralai/mistral-7b-instruct", merged[1]);
        }

        [Fact]
        public void SelectForTask_EmptyCatalog_ReturnsEmpty()
        {
            Assert.Empty(AiModelSelector.SelectForTask(AiTaskType.General, System.Array.Empty<AiModelOption>()));
        }

        [Fact]
        public void SelectForTask_PrioritizesUserSelectedPool_ForCalculatorTask()
        {
            var models = new[]
            {
                new AiModelOption("catalog-top", "Qwen 2.5 72B"),
                new AiModelOption("user-model-1", "Mistral 7B"),
                new AiModelOption("user-model-2", "DeepSeek V3")
            };

            var userSelected = new[] { "user-model-1", "user-model-2" };

            var selected = AiModelSelector.SelectForTask(
                AiTaskType.Calculator,
                models,
                userSelected);

            // User-selected models come first, with the stronger DeepSeek V3 before Mistral 7B
            Assert.Equal("user-model-2", selected[0]);
            Assert.Equal("user-model-1", selected[1]);
            // Catalog fallback comes after user pool
            Assert.Equal("catalog-top", selected[2]);
        }

        [Fact]
        public void SelectForTask_VisionTask_ExcludesTextOnlyModels()
        {
            var models = new[]
            {
                new AiModelOption("text-only-strong", "DeepSeek V3") { SupportsVision = false },
                new AiModelOption("vision-model", "Qwen 2.5 VL") { SupportsVision = true }
            };

            var selected = AiModelSelector.SelectForTask(AiTaskType.Vision, models);

            Assert.Single(selected);
            Assert.Equal("vision-model", selected[0]);
        }

        [Fact]
        public void SelectForTask_CooledDownModel_IsDeprioritized()
        {
            var models = new[]
            {
                new AiModelOption("model-a", "Qwen 2.5 72B"),
                new AiModelOption("model-b", "DeepSeek V3")
            };

            AiModelSelector.ReportModelFailure("model-a", 429);
            try
            {
                Assert.True(AiModelSelector.IsCooledDown("model-a"));

                var selected = AiModelSelector.SelectForTask(AiTaskType.Calculator, models);
                // Healthy model-b must be preferred over cooled-down model-a
                Assert.Equal("model-b", selected[0]);
                Assert.Equal("model-a", selected[1]);
            }
            finally
            {
                AiModelSelector.ReportModelSuccess("model-a");
            }
        }

        [Fact]
        public void SelectForTask_ProviderActiveFilter_ExcludesInactiveProviders()
        {
            var models = new[]
            {
                new AiModelOption("openrouter-model", "OpenRouter Free", AiProvider.OpenRouter),
                new AiModelOption("freellm-model", "FreeLLM Model", AiProvider.FreeLlmApi)
            };

            // Only FreeLLMApi is active
            var selected = AiModelSelector.SelectForTask(
                AiTaskType.General,
                models,
                isProviderActive: p => p == AiProvider.FreeLlmApi);

            Assert.Single(selected);
            Assert.Equal("freellm-model", selected[0]);
        }

        [Fact]
        public void SelectForTask_VisionTask_PrioritizesGeminiAndQwenVLOverLlama11B()
        {
            var models = new[]
            {
                new AiModelOption("meta/llama-3.2-11b-vision-instruct", "Llama 3.2 11B Vision", AiProvider.FreeLlmApi) { SupportsVision = true },
                new AiModelOption("gemini-3.8-flash", "Gemini 3.8 Flash", AiProvider.FreeLlmApi) { SupportsVision = true },
                new AiModelOption("qwen2.5-vl-72b", "Qwen 2.5 VL 72B", AiProvider.FreeLlmApi) { SupportsVision = true }
            };

            var selected = AiModelSelector.SelectForTask(AiTaskType.Vision, models).ToList();

            // Gemini and Qwen VL must rank ahead of Llama 3.2 11B
            Assert.True(selected.IndexOf("gemini-3.8-flash") < selected.IndexOf("meta/llama-3.2-11b-vision-instruct"));
            Assert.True(selected.IndexOf("qwen2.5-vl-72b") < selected.IndexOf("meta/llama-3.2-11b-vision-instruct"));
        }
    }
}
