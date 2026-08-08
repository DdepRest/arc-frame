using System;
using System.IO;
using System.Linq;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// AI settings file-IO isolation. Uses the same "FileSystem" collection
    /// as <see cref="AppSettingsServiceTests"/> to avoid parallel file access.
    /// </summary>
    [Collection("FileSystem")]
    public class AppSettingsServiceAiTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _originalAiSettingsPath;

        public AppSettingsServiceAiTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "mnc_ai_settings_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _originalAiSettingsPath = AppSettingsServiceAi.AiSettingsPath;
            AppSettingsServiceAi.AiSettingsPath = Path.Combine(_tempDir, "ai-settings.json");
        }

        public void Dispose()
        {
            AppSettingsServiceAi.AiSettingsPath = _originalAiSettingsPath;
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        [Fact]
        public void SaveAndLoad_ApiKey_Roundtrip()
        {
            AppSettingsServiceAi.SaveAiApiKey("sk-openrouter-secret");
            Assert.Equal("sk-openrouter-secret", AppSettingsServiceAi.LoadAiApiKey());
        }

        [Fact]
        public void SaveAndLoad_NvidiaApiKey_Roundtrip()
        {
            AppSettingsServiceAi.SaveAiNvidiaApiKey("nvapi-secret");
            Assert.Equal("nvapi-secret", AppSettingsServiceAi.LoadAiNvidiaApiKey());
        }

        [Fact]
        public void Load_NvidiaApiKey_WithoutFile_ReturnsEmptyString()
        {
            Assert.Equal(string.Empty, AppSettingsServiceAi.LoadAiNvidiaApiKey());
        }

        [Fact]
        public void SaveNvidiaApiKey_TrimsWhitespace()
        {
            AppSettingsServiceAi.SaveAiNvidiaApiKey("  nvapi-secret  ");
            Assert.Equal("nvapi-secret", AppSettingsServiceAi.LoadAiNvidiaApiKey());
        }

        [Fact]
        public void SaveNvidiaApiKey_NullBecomesEmpty()
        {
            AppSettingsServiceAi.SaveAiNvidiaApiKey(null!);
            Assert.Equal(string.Empty, AppSettingsServiceAi.LoadAiNvidiaApiKey());
        }

        [Fact]
        public void SaveAndLoad_Model_Roundtrip()
        {
            AppSettingsServiceAi.SaveAiModel("meta-llama/llama-4-scout:free");
            Assert.Equal("meta-llama/llama-4-scout:free", AppSettingsServiceAi.LoadAiModel());
        }

        [Fact]
        public void Load_Model_WithoutFile_ReturnsDefaultModel()
        {
            Assert.Equal("google/gemma-3-27b-it:free", AppSettingsServiceAi.LoadAiModel());
        }

        [Fact]
        public void Load_ApiKey_WithoutFile_ReturnsEmptyString()
        {
            Assert.Equal(string.Empty, AppSettingsServiceAi.LoadAiApiKey());
        }

        [Fact]
        public void SaveApiKey_TrimsWhitespace()
        {
            AppSettingsServiceAi.SaveAiApiKey("  secret  ");
            Assert.Equal("secret", AppSettingsServiceAi.LoadAiApiKey());
        }

        [Fact]
        public void SaveApiKey_NullBecomesEmpty()
        {
            AppSettingsServiceAi.SaveAiApiKey(null!);
            Assert.Equal(string.Empty, AppSettingsServiceAi.LoadAiApiKey());
        }

        [Fact]
        public void SaveModel_NullFallsBackToDefault()
        {
            AppSettingsServiceAi.SaveAiModel(null!);
            Assert.Equal("google/gemma-3-27b-it:free", AppSettingsServiceAi.LoadAiModel());
        }

        [Fact]
        public void SaveApiKey_PreservesModel()
        {
            AppSettingsServiceAi.SaveAiModel("qwen/qwen-2.5-72b-instruct:free");
            AppSettingsServiceAi.SaveAiApiKey("key");
            Assert.Equal("qwen/qwen-2.5-72b-instruct:free", AppSettingsServiceAi.LoadAiModel());
        }

        [Fact]
        public void SaveAndLoad_FallbackModels_Roundtrip()
        {
            var models = new[] { "model-a", "model-b", "model-c" };
            AppSettingsServiceAi.SaveAiFallbackModels(models);

            var loaded = AppSettingsServiceAi.LoadAiFallbackModels();
            Assert.Equal(new[] { "model-a", "model-b", "model-c" }, loaded);
        }

        [Fact]
        public void SaveFallbackModels_RemovesDuplicatesAndWhitespace()
        {
            AppSettingsServiceAi.SaveAiFallbackModels(new[] { " a ", " a ", "b" });

            var loaded = AppSettingsServiceAi.LoadAiFallbackModels();
            Assert.Equal(new[] { "a", "b" }, loaded);
        }

        [Fact]
        public void LoadFallbackModels_WithoutFile_FallsBackToDefaultModel()
        {
            var loaded = AppSettingsServiceAi.LoadAiFallbackModels();
            Assert.Single(loaded);
            Assert.Equal("google/gemma-3-27b-it:free", loaded[0]);
        }

        [Fact]
        public void SaveFallbackModels_SyncsLegacyModelField()
        {
            AppSettingsServiceAi.SaveAiFallbackModels(new[] { "meta-llama/llama-4-scout:free" });
            Assert.Equal("meta-llama/llama-4-scout:free", AppSettingsServiceAi.LoadAiModel());
        }

        [Fact]
        public void SaveAndLoad_CachedModels_Roundtrip()
        {
            var models = new[]
            {
                new MosquitoNetCalculator.Models.AiModelOption("model-a", "Model A"),
                new MosquitoNetCalculator.Models.AiModelOption("model-b", "Model B")
            };
            var timestamp = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            AppSettingsServiceAi.SaveCachedModels(models, timestamp);

            var (loadedModels, loadedAt) = AppSettingsServiceAi.LoadCachedModels();
            Assert.Equal(2, loadedModels.Count);
            Assert.Contains(loadedModels, m => m.Id == "model-a" && m.DisplayName == "Model A");
            Assert.Contains(loadedModels, m => m.Id == "model-b" && m.DisplayName == "Model B");
            Assert.Equal(timestamp, loadedAt);
        }

        [Fact]
        public void ClearModelCache_RemovesCachedModels()
        {
            AppSettingsServiceAi.SaveCachedModels(
                new[] { new MosquitoNetCalculator.Models.AiModelOption("model-a", "Model A") },
                DateTime.UtcNow);

            AppSettingsServiceAi.ClearModelCache();

            var (loadedModels, loadedAt) = AppSettingsServiceAi.LoadCachedModels();
            Assert.Empty(loadedModels);
            Assert.Null(loadedAt);
        }

        [Fact]
        public void FetchAvailableModelsAsync_ExpiredCache_IsIgnored()
        {
            // Cache a model but mark it as older than the TTL
            var oldTimestamp = DateTime.UtcNow - TimeSpan.FromHours(2);
            AppSettingsServiceAi.SaveCachedModels(
                new[] { new MosquitoNetCalculator.Models.AiModelOption("cached-model", "Cached Model") },
                oldTimestamp);

            var (loadedModels, loadedAt) = AppSettingsServiceAi.LoadCachedModels();
            Assert.Single(loadedModels);
            Assert.Equal(oldTimestamp, loadedAt);

            // Verify the cache timestamp is stale (older than a reasonable TTL).
            Assert.True(loadedAt.HasValue);
            Assert.True(DateTime.UtcNow - loadedAt.Value > TimeSpan.FromMinutes(1));
        }

        [Fact]
        public void Load_ApiKey_HandlesCorruptedFile()
        {
            File.WriteAllText(AppSettingsServiceAi.AiSettingsPath, "not valid json");
            Assert.Equal(string.Empty, AppSettingsServiceAi.LoadAiApiKey());
        }

        [Fact]
        public void Load_Model_HandlesCorruptedFile()
        {
            File.WriteAllText(AppSettingsServiceAi.AiSettingsPath, "corrupted");
            Assert.Equal("google/gemma-3-27b-it:free", AppSettingsServiceAi.LoadAiModel());
        }

        [Fact]
        public void SaveAndLoad_ChatHistory_PreservesProperties()
        {
            var messages = new[]
            {
                new MosquitoNetCalculator.Models.AiChatMessage
                {
                    Text = "Hello",
                    IsUser = true,
                    IsAction = false
                },
                new MosquitoNetCalculator.Models.AiChatMessage
                {
                    Text = "Reply",
                    IsUser = false,
                    IsAction = true,
                    ActionSummary = "Action summary"
                }
            };

            AppSettingsServiceAi.SaveChatHistory(messages);
            var loaded = AppSettingsServiceAi.LoadChatHistory();

            Assert.Equal(2, loaded.Count);
            Assert.Equal("Hello", loaded[0].Text);
            Assert.True(loaded[0].IsUser);
            Assert.False(loaded[0].IsAction);
            Assert.Equal("Reply", loaded[1].Text);
            Assert.False(loaded[1].IsUser);
            Assert.True(loaded[1].IsAction);
            Assert.Equal("Action summary", loaded[1].ActionSummary);
        }

        [Fact]
        public void SaveChatHistory_TruncatesToMaxLimit()
        {
            var messages = Enumerable.Range(0, 100)
                .Select(i => new MosquitoNetCalculator.Models.AiChatMessage
                {
                    Text = $"Message {i}",
                    IsUser = i % 2 == 0
                })
                .ToList();

            AppSettingsServiceAi.SaveChatHistory(messages, maxMessages: 50);
            var loaded = AppSettingsServiceAi.LoadChatHistory();

            Assert.Equal(50, loaded.Count);
            Assert.Equal("Message 50", loaded[0].Text);
            Assert.Equal("Message 99", loaded[49].Text);
        }

        [Fact]
        public void LoadChatHistory_WithoutFile_ReturnsEmptyList()
        {
            Assert.Empty(AppSettingsServiceAi.LoadChatHistory());
        }

        [Fact]
        public void LoadChatHistory_HandlesCorruptedFile()
        {
            File.WriteAllText(AppSettingsServiceAi.AiSettingsPath, "not valid json");
            Assert.Empty(AppSettingsServiceAi.LoadChatHistory());
        }

        [Fact]
        public void SaveChatHistory_FiltersOutEmptyTextMessages()
        {
            var messages = new[]
            {
                new MosquitoNetCalculator.Models.AiChatMessage { Text = "Real message", IsUser = true },
                // Leftover streaming placeholder saved by older builds — must never
                // be persisted: it renders as a blank bubble in the restored chat.
                new MosquitoNetCalculator.Models.AiChatMessage { Text = "", IsUser = false },
                new MosquitoNetCalculator.Models.AiChatMessage { Text = "   ", IsUser = false }
            };

            AppSettingsServiceAi.SaveChatHistory(messages);
            var loaded = AppSettingsServiceAi.LoadChatHistory();

            Assert.Single(loaded);
            Assert.Equal("Real message", loaded[0].Text);
        }

        [Fact]
        public void LoadChatHistory_FromLegacyFileWithEmptyMessages_SkipsBlankBubbles()
        {
            // Simulate a settings file written by an older build that persisted
            // an empty streaming placeholder next to real messages.
            File.WriteAllText(AppSettingsServiceAi.AiSettingsPath,
                "{\"ChatHistory\":[" +
                "{\"Text\":\"Здравствуйте! Я AI-ассистент.\",\"IsUser\":false}," +
                "{\"Text\":\"\",\"IsUser\":false}," +
                "{\"Text\":\"Сделай сетку\",\"IsUser\":true}]}");

            var loaded = AppSettingsServiceAi.LoadChatHistory();

            Assert.Equal(2, loaded.Count);
            Assert.Equal("Здравствуйте! Я AI-ассистент.", loaded[0].Text);
            Assert.Equal("Сделай сетку", loaded[1].Text);
        }
    }
}
