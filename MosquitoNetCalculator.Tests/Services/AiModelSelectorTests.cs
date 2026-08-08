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
    }
}
