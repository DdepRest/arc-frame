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
        // Updates may replace the app directory — user settings must survive,
        // so they go into %AppData%.
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
            // EASTER-EGG v3.43.2.9 — Slopes PRO upsell 'unlocked' flag.
            // Semantics: true = user has clicked «Оплатить» + OK (unlocked permanently);
            //            false = user has only seen the joke but not paid (loop forever).
            // Safe to delete when joke is removed: JSON deserializer ignores
            // unknown keys, so existing settings.json with this key stays valid
            // even after we drop the backing field.
            public bool SlopesProUpsellUnlocked { get; set; } = false;
            public bool FirstRunComplete { get; set; } = false;
            // BETA banner for slope auto-calculation. Once dismissed, stays hidden.
            public bool SlopeBetaBannerHidden { get; set; } = false;
            public string UpdateUrl { get; set; } = "";
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

        // ─────────────────────────────────────────────────────────
        //  EASTER-EGG v3.43.2.9 — Slopes PRO upsell 'unlocked' flag.
        //  Remove these two methods + the Settings.SlopesProUpsellUnlocked field
        //  to disable the joke: no other call sites, no dependencies.
        //  v3.43.2.8 had 'SlopesProUpsellSeen' (mark-before-show semantics,
        //  loop-prevention).
        //  v3.43.2.9 renames to 'Unlocked' (mark-after-Pay semantics, strict
        //  loop until explicit Оплатить → OK).
        //  Backward-compat: old key 'SlopesProUpsellSeen' is simply ignored by
        //  System.Text.Json on read — user sees joke once more on first run after
        //  upgrade, which is correct (they haven't actually unlocked it).
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if the user has UNLOCKED the Slopes panel by clicking
        /// «Оплатить» → шутка → OK. Until unlocked, the joke dialog keeps
        /// appearing every time the Slopes menu is clicked (strict loop).
        /// </summary>
        public static bool IsSlopesProUpsellUnlocked()
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                return settings.SlopesProUpsellUnlocked;
            }
        }

        /// <summary>
        /// Marks the Slopes panel as unlocked (joke dialog will never appear again).
        /// Called only on the explicit «Оплатить» → OK happy-path.
        /// </summary>
        public static void MarkSlopesProUpsellUnlocked()
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                settings.SlopesProUpsellUnlocked = true;
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
        /// Returns the configured update URL for GitHub Releases auto-updates.
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
        /// Saves the update URL for GitHub Releases auto-updates.
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

        // ─────────────────────────────────────────────────────────
        //  BETA banner for slope auto-calculation.
        //  Once the user dismisses the banner, it stays hidden.
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if the user has previously dismissed the BETA banner
        /// in the slope panel. Default is false (banner is shown).
        /// </summary>
        public static bool IsSlopeBetaBannerHidden()
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                return settings.SlopeBetaBannerHidden;
            }
        }

        /// <summary>
        /// Marks the slope BETA banner as hidden (dismissed by the user).
        /// </summary>
        public static void HideSlopeBetaBanner()
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                settings.SlopeBetaBannerHidden = true;
                SaveSettings(settings);
            }
        }


    }
}
