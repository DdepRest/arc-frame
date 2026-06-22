using System;
using System.IO;
using System.Text.Json;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Manages application-wide settings stored in settings.json.
    /// Currently responsible for the contract-number prefix.
    /// </summary>
    public static class AppSettingsService
    {
        // Mutable `static` (NOT `readonly`) so that test code in
        // MosquitoNetCalculator.Tests can redirect the path to a temp
        // directory per-test. .NET 8 throws FieldAccessException on
        // FieldInfo.SetValue against initonly fields, so the property
        // must be public-mutable from inside the class itself.
        // Data lives in %AppData%\MosquitoNetCalculator\, not in the app directory.
        // Velopack updates wipe the `current` folder — user settings must
        // survive across updates, so they go into %AppData%.
        public static string SettingsPath { get; set; } =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MosquitoNetCalculator",
                "settings.json");
        private static readonly object _lock = new();

        private class Settings
        {
            public string Theme { get; set; } = "light";
            public string ContractPrefix { get; set; } = "1";
            public string LocationName { get; set; } = "";
            public bool FirstRunComplete { get; set; } = false;
            public string UpdateUrl { get; set; } = "";
            public bool UseVelopackFlow { get; set; } = false;
            public string? PendingUpdateVersion { get; set; }
        }

        private static Settings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<Settings>(json);
                    if (settings != null) return settings;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AppSettings] load failed: {ex.Message}"); }
            return new Settings();
        }

        private static void SaveSettings(Settings settings)
        {
            try
            {
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AppSettings] save failed: {ex.Message}"); }
        }

        /// <summary>
        /// Loads the saved contract prefix from settings.json.
        /// Returns "1" if no saved prefix exists.
        /// </summary>
        public static string LoadContractPrefix()
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                return !string.IsNullOrWhiteSpace(settings.ContractPrefix) ? settings.ContractPrefix.Trim() : "1";
            }
        }

        /// <summary>
        /// Saves the contract prefix to settings.json, preserving the current theme.
        /// </summary>
        public static void SaveContractPrefix(string prefix)
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                settings.ContractPrefix = string.IsNullOrWhiteSpace(prefix) ? "1" : prefix.Trim();
                SaveSettings(settings);
            }
        }

        /// <summary>
        /// Loads the saved theme name from settings.json.
        /// Returns "light" if no saved theme exists.
        /// </summary>
        public static string LoadTheme()
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                return !string.IsNullOrWhiteSpace(settings.Theme) ? settings.Theme.Trim().ToLower() : "light";
            }
        }

        /// <summary>
        /// Returns true if this is the first run of the application
        /// (settings.json does not exist or FirstRunComplete is false).
        /// </summary>
        public static bool IsFirstRun()
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                return !settings.FirstRunComplete;
            }
        }

        /// <summary>
        /// Marks the first-run welcome flow as completed.
        /// </summary>
        public static void MarkFirstRunComplete()
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                settings.FirstRunComplete = true;
                SaveSettings(settings);
            }
        }

        /// <summary>
        /// Loads the saved location name from settings.json.
        /// Returns empty string if none saved.
        /// </summary>
        public static string LoadLocationName()
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                return settings.LocationName ?? "";
            }
        }

        /// <summary>
        /// Saves the human-readable location name (e.g. "Красношапки 44 — «Дом Окон+»").
        /// </summary>
        public static void SaveLocationName(string name)
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                settings.LocationName = name ?? "";
                SaveSettings(settings);
            }
        }

        /// <summary>
        /// Saves the theme name to settings.json, preserving the current contract prefix.
        /// </summary>
        public static void SaveTheme(string theme)
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                settings.Theme = string.IsNullOrWhiteSpace(theme) ? "light" : theme.Trim().ToLower();
                SaveSettings(settings);
            }
        }

        /// <summary>
        /// Returns true if Velopack Flow is enabled.
        /// </summary>
        public static bool IsFlowEnabled()
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                return settings.UseVelopackFlow;
            }
        }

        /// <summary>
        /// Enables or disables Velopack Flow auto-updates.
        /// </summary>
        public static void SetFlowEnabled(bool enabled)
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                settings.UseVelopackFlow = enabled;
                SaveSettings(settings);
            }
        }

        /// <summary>
        /// Returns the configured update URL for Velopack auto-updates.
        /// Empty string means auto-update is disabled.
        /// </summary>
        public static string LoadUpdateUrl()
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                return settings.UpdateUrl?.Trim() ?? "";
            }
        }

        /// <summary>
        /// Saves the update URL for Velopack auto-updates.
        /// </summary>
        public static void SaveUpdateUrl(string url)
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                settings.UpdateUrl = url?.Trim() ?? "";
                SaveSettings(settings);
            }
        }

        /// <summary>
        /// Returns the pending update version string if a previous check found one.
        /// Null or empty means no pending update.
        /// </summary>
        public static string? LoadPendingUpdateVersion()
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                return settings.PendingUpdateVersion;
            }
        }

        /// <summary>
        /// Saves (or clears) the pending update version.
        /// Pass null to clear.
        /// </summary>
        public static void SavePendingUpdateVersion(string? version)
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                settings.PendingUpdateVersion = version;
                SaveSettings(settings);
            }
        }
    }
}
