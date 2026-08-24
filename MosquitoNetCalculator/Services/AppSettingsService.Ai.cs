using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    // Partial class extension for AI assistant settings.
    // Separated to keep the original AppSettingsService clean.
    public static partial class AppSettingsServiceAi
    {
        /// <summary>
        /// Path to the AI assistant settings file. Exposed so tests can redirect it.
        /// </summary>
        public static string AiSettingsPath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MosquitoNetCalculator",
            "ai-settings.json");

        private static readonly object SettingsLock = new();

        private class AiSettings
        {
            public string ApiKey { get; set; } = "";
            public string NvidiaApiKey { get; set; } = "";
            public string Model { get; set; } = AiAssistantService.OpenRouterFreeRouter;
            public List<string> FallbackModels { get; set; } = new();
            public List<AiModelOption> CachedModels { get; set; } = new();
            public DateTime? CachedModelsAt { get; set; }
            public List<AiModelAvailability> ModelAvailability { get; set; } = new();
            public List<AiChatMessage> ChatHistory { get; set; } = new();
            public bool AutoSelectModel { get; set; } = true;
        }

        /// <summary>
        /// Reads the settings file. Caller must own <see cref="SettingsLock"/>.
        /// </summary>
        private static AiSettings LoadAiSettingsCore()
        {
            try
            {
                if (File.Exists(AiSettingsPath))
                {
                    var json = File.ReadAllText(AiSettingsPath);
                    var settings = JsonSerializer.Deserialize<AiSettings>(json);
                    if (settings != null) return settings;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AiSettings] load failed: {ex.Message}");
            }
            return new AiSettings();
        }

        /// <summary>
        /// Writes the settings file. Caller must own <see cref="SettingsLock"/>.
        /// </summary>
        private static void SaveAiSettingsCore(AiSettings settings)
        {
            try
            {
                var dir = Path.GetDirectoryName(AiSettingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(AiSettingsPath, JsonSerializer.Serialize(settings,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AiSettings] save failed: {ex.Message}");
            }
        }

        public static string LoadAiApiKey()
        {
            lock (SettingsLock)
            {
                return LoadAiSettingsCore().ApiKey ?? "";
            }
        }

        public static void SaveAiApiKey(string key)
        {
            lock (SettingsLock)
            {
                var settings = LoadAiSettingsCore();
                settings.ApiKey = key?.Trim() ?? "";
                SaveAiSettingsCore(settings);
            }
        }

        /// <summary>
        /// Loads the user-configured NVIDIA API key (empty when not set — the
        /// built-in embedded NVIDIA key is used in that case).
        /// </summary>
        public static string LoadAiNvidiaApiKey()
        {
            lock (SettingsLock)
            {
                return LoadAiSettingsCore().NvidiaApiKey ?? "";
            }
        }

        public static void SaveAiNvidiaApiKey(string key)
        {
            lock (SettingsLock)
            {
                var settings = LoadAiSettingsCore();
                settings.NvidiaApiKey = key?.Trim() ?? "";
                SaveAiSettingsCore(settings);
            }
        }

        public static string? LoadAiModel()
        {
            lock (SettingsLock)
            {
                var model = LoadAiSettingsCore().Model;
                return string.IsNullOrWhiteSpace(model) ? null : model;
            }
        }

        public static void SaveAiModel(string model)
        {
            lock (SettingsLock)
            {
                var settings = LoadAiSettingsCore();
                settings.Model = model?.Trim() ?? AiAssistantService.OpenRouterFreeRouter;
                SaveAiSettingsCore(settings);
            }
        }

        /// <summary>
        /// Loads the ordered list of fallback AI models.
        /// Falls back to the legacy single model setting if the list is empty.
        /// </summary>
        public static IReadOnlyList<string> LoadAiFallbackModels()
        {
            lock (SettingsLock)
            {
                var settings = LoadAiSettingsCore();
                if (settings.FallbackModels == null || settings.FallbackModels.Count == 0)
                {
                    var single = string.IsNullOrWhiteSpace(settings.Model)
                        ? AiAssistantService.OpenRouterFreeRouter
                        : settings.Model;
                    return new List<string> { single };
                }
                return settings.FallbackModels.AsReadOnly();
            }
        }

        /// <summary>
        /// Saves the ordered list of fallback AI models and keeps the legacy
        /// single-model field in sync.
        /// </summary>
        public static void SaveAiFallbackModels(IEnumerable<string>? models)
        {
            lock (SettingsLock)
            {
                var settings = LoadAiSettingsCore();
                settings.FallbackModels = models?
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Select(m => m.Trim())
                    .Distinct()
                    .ToList() ?? new List<string>();

                settings.Model = settings.FallbackModels.FirstOrDefault()
                    ?? AiAssistantService.OpenRouterFreeRouter;

                SaveAiSettingsCore(settings);
            }
        }

        /// <summary>
        /// Loads the cached model list and its timestamp.
        /// </summary>
        public static (IReadOnlyList<AiModelOption> Models, DateTime? CachedAt) LoadCachedModels()
        {
            lock (SettingsLock)
            {
                var settings = LoadAiSettingsCore();
                IReadOnlyList<AiModelOption> models = settings.CachedModels != null
                    ? settings.CachedModels.AsReadOnly()
                    : Array.Empty<AiModelOption>();
                return (models, settings.CachedModelsAt);
            }
        }

        /// <summary>
        /// Saves the cached model list and timestamp.
        /// </summary>
        public static void SaveCachedModels(IEnumerable<AiModelOption>? models, DateTime? cachedAt)
        {
            lock (SettingsLock)
            {
                var settings = LoadAiSettingsCore();
                settings.CachedModels = models?
                    .Where(m => !string.IsNullOrWhiteSpace(m.Id))
                    .DistinctBy(m => m.Id)
                    .ToList() ?? new List<AiModelOption>();
                settings.CachedModelsAt = cachedAt;
                SaveAiSettingsCore(settings);
            }
        }

        /// <summary>
        /// Loads the persisted model availability probe results.
        /// </summary>
        public static IReadOnlyList<AiModelAvailability> LoadModelAvailability()
        {
            lock (SettingsLock)
            {
                return (LoadAiSettingsCore().ModelAvailability
                        ?? new List<AiModelAvailability>())
                    .AsReadOnly();
            }
        }

        /// <summary>
        /// Persists model availability probe results, keeping the latest entry
        /// per model id.
        /// </summary>
        public static void SaveModelAvailability(IEnumerable<AiModelAvailability>? items)
        {
            lock (SettingsLock)
            {
                var settings = LoadAiSettingsCore();
                settings.ModelAvailability = items?
                    .Where(a => !string.IsNullOrWhiteSpace(a.Id))
                    .GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(a => a.CheckedAt ?? DateTime.MinValue).First())
                    .ToList() ?? new List<AiModelAvailability>();
                SaveAiSettingsCore(settings);
            }
        }

        /// <summary>
        /// Clears the cached model list.
        /// </summary>
        public static void ClearModelCache()
        {
            lock (SettingsLock)
            {
                var settings = LoadAiSettingsCore();
                settings.CachedModels = new List<AiModelOption>();
                settings.CachedModelsAt = null;
                SaveAiSettingsCore(settings);
            }
        }

        /// <summary>
        /// Loads the persisted AI chat history.
        /// Returns an read-only view of the messages stored in ai-settings.json.
        /// Empty-text messages (e.g. leftover streaming placeholders saved by
        /// older builds) are filtered out so the chat never shows blank bubbles.
        /// </summary>
        public static IReadOnlyList<AiChatMessage> LoadChatHistory()
        {
            lock (SettingsLock)
            {
                return LoadAiSettingsCore().ChatHistory
                    .Where(m => !string.IsNullOrWhiteSpace(m.Text))
                    .ToList().AsReadOnly();
            }
        }

        /// <summary>
        /// Persists the AI chat history, keeping only the last <paramref name="maxMessages"/> messages
        /// to prevent the settings file from growing unbounded.
        /// Empty-text messages are never persisted — a blank bubble has no value
        /// in a restored conversation.
        /// </summary>
        public static void SaveChatHistory(IEnumerable<AiChatMessage> messages, int maxMessages = 50)
        {
            lock (SettingsLock)
            {
                var settings = LoadAiSettingsCore();
                settings.ChatHistory = messages
                    .Where(m => !string.IsNullOrWhiteSpace(m.Text))
                    .TakeLast(maxMessages)
                    .ToList();
                SaveAiSettingsCore(settings);
            }
        }

        /// <summary>
        /// Returns true when the user has enabled automatic model selection.
        /// Default is true (opt-out) so new users get the feature immediately.
        /// </summary>
        public static bool LoadAutoSelectModel()
        {
            lock (SettingsLock)
            {
                return LoadAiSettingsCore().AutoSelectModel;
            }
        }

        /// <summary>
        /// Persists the automatic model selection preference.
        /// </summary>
        public static void SaveAutoSelectModel(bool enabled)
        {
            lock (SettingsLock)
            {
                var settings = LoadAiSettingsCore();
                settings.AutoSelectModel = enabled;
                SaveAiSettingsCore(settings);
            }
        }
    }
}
