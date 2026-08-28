using System;
using System.IO;
using System.Text.Json;
using System.Threading;

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
            public string LastColor { get; set; } = "";
            public string UpdateUrl { get; set; } = "";
            public string? PendingUpdateVersion { get; set; }
            // Версия, для которой пользователь уже видел окно «Что нового»
            // после обновления. Null = ещё не фиксировалась.
            public string? LastSeenVersion { get; set; }
            // Админ-панель офисов: переопределение токена/ID gist (отладка, миграция).
            public string? OfficeReportToken { get; set; }
            public string? OfficeReportGistId { get; set; }
            // Стабильный ID устройства (один ПК = один отчёт в gist). Генерируется
            // один раз при первом запуске — см. LoadOrCreateDeviceId.
            public string DeviceId { get; set; } = "";
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

        /// <summary>
        /// Версия, для которой пользователь уже видел окно «Что нового».
        /// Null = ещё не фиксировалась (первый запуск нового механизма).
        /// </summary>
        public static string? LoadLastSeenVersion()
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                return settings.LastSeenVersion;
            }
        }

        /// <summary>
        /// Сохраняет версию, для которой окно «Что нового» уже показано.
        /// </summary>
        public static void SaveLastSeenVersion(string? version)
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                settings.LastSeenVersion = version;
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

        // ─────────────────────────────────────────────────────────
        //  Last custom colour for notes formatting toolbar.
        //  Persisted so the ColorDialog re-opens with the user's
        //  previously chosen colour.
        // ─────────────────────────────────────────────────────────

        public static string LoadLastColor()
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                return settings.LastColor ?? "";
            }
        }

        public static void SaveLastColor(string hex)
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                settings.LastColor = hex ?? "";
                SaveSettings(settings);
            }
        }

        // ─────────────────────────────────────────────────────────
        //  Админ-панель офисов (отчёты через GitHub Gist).
        //  Токен/ID gist в settings.json — ТОЛЬКО для отладки и тестов;
        //  в релизе токен встраивается компиляцией (OfficeReportService).
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Возвращает токен для gist из settings.json (пустая строка = не задан,
        /// используется скомпилированная константа OfficeReportService.CompiledToken).
        /// </summary>
        public static string LoadOfficeReportToken()
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                return settings.OfficeReportToken?.Trim() ?? "";
            }
        }

        /// <summary>
        /// Сохраняет (или очищает при null) токен gist в settings.json.
        /// </summary>
        public static void SaveOfficeReportToken(string? token)
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                settings.OfficeReportToken = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
                SaveSettings(settings);
            }
        }

        /// <summary>
        /// Возвращает ID gist из settings.json (пустая строка = используется константа).
        /// Позволяет сменить хранилище без пересборки.
        /// </summary>
        public static string LoadOfficeReportGistId()
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                return settings.OfficeReportGistId?.Trim() ?? "";
            }
        }

        /// <summary>
        /// Сохраняет (или очищает при null) ID gist в settings.json.
        /// </summary>
        public static void SaveOfficeReportGistId(string? gistId)
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                settings.OfficeReportGistId = string.IsNullOrWhiteSpace(gistId) ? null : gistId.Trim();
                SaveSettings(settings);
            }
        }

        // ─────────────────────────────────────────────────────────
        //  Пароль админ-панели — ВШИТЫЙ (одинаковый во всех офисах,
        //  офисам ничего настраивать не нужно). Владелец меняет при желании.
        //  Панель показывает только версии/статистику — данные не секретные,
        //  пароль лишь ограничивает доступ к админ-интерфейсу.
        // ─────────────────────────────────────────────────────────

        /// <summary>Вшитый пароль входа в админ-панель.</summary>
        public const string EmbeddedAdminPassword = "AZ123123Az";

        /// <summary>
        /// Проверяет введённый пароль против вшитого.
        /// </summary>
        public static bool VerifyAdminPassword(string? password)
            => password == EmbeddedAdminPassword;

        // ─────────────────────────────────────────────────────────
        //  Стабильный ID устройства (админ-панель, отчёты офисов).
        //  В одном офисе может быть несколько ПК — каждый получает свой
        //  GUID при первом запуске и хранит его в settings.json, чтобы
        //  не перетирать отчёты других устройств того же офиса.
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Возвращает стабильный ID этого устройства (GUID). При первом вызове
        /// генерирует, сохраняет в settings.json и возвращает; при повторных
        /// вызовах — возвращает сохранённый. Потокобезопасен.
        ///
        /// Генерация дополнительно защищена КРОСС-ПРОЦЕССНО: рядом с settings.json
        /// атомарно создаётся файл <c>device-id</c> (FileMode.CreateNew — победит
        /// только один процесс, остальные прочитают его ID). Это гарантирует, что
        /// две одновременно запущенные копии программы (обычная + dev) на одном ПК
        /// получат ОДИНАКОВЫЙ deviceId и не создадут два отчёта в gist.
        /// </summary>
        public static string LoadOrCreateDeviceId()
        {
            lock (_lock)
            {
                var settings = LoadSettings();
                if (!string.IsNullOrWhiteSpace(settings.DeviceId))
                    return settings.DeviceId;

                var id = TryCreateOrReadDeviceIdFile() ?? Guid.NewGuid().ToString("N");
                settings.DeviceId = id;
                SaveSettings(settings);
                return id;
            }
        }

        /// <summary>
        /// Атомарная кросс-процессная генерация ID: первый процесс создаёт файл
        /// <c>device-id</c>, остальные (чьё CreateNew упало) читают его же.
        /// Возвращает null, если файл создать/прочитать не удалось — тогда
        /// вызывающий использует случайный GUID.
        /// </summary>
        private static string? TryCreateOrReadDeviceIdFile()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (string.IsNullOrWhiteSpace(dir))
                    return null;

                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "device-id");
                try
                {
                    using var fs = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    using var writer = new StreamWriter(fs);
                    var id = Guid.NewGuid().ToString("N");
                    writer.Write(id);
                    return id;
                }
                catch (IOException)
                {
                    // Файл уже создан другим процессом — читаем его ID, дожидаясь
                    // завершения записи (окно записи ~микросекунды, ретраи дешёвые).
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        try
                        {
                            var text = File.ReadAllText(file).Trim();
                            if (!string.IsNullOrWhiteSpace(text))
                                return text;
                        }
                        catch (IOException) { /* другой процесс ещё пишет — повторим */ }
                        if (attempt < 9) Thread.Sleep(100);
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettings] device-id file failed: {ex.Message}");
                return null;
            }
        }

    }
}
