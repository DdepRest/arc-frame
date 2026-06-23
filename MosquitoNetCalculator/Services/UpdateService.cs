using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Manages application auto-updates via GitHub Releases.
    ///
    /// ─── Как это работает ──────────────────────────────────────────────────
    /// 1. При запуске вызывается <see cref="CheckOnStartupAsync"/> — тихая
    ///    проверка в фоне, без блокировки интерфейса.
    /// 2. Пользователь может нажать «Проверить обновления» в меню Настроек —
    ///    вызывается <see cref="CheckAndApplyAsync"/> с интерактивным диалогом.
    /// 3. Клиент скачивает releases.json с raw.githubusercontent.com, сверяет
    ///    версии, скачивает ZIP, проверяет SHA-256, запускает watchdog .bat
    ///    и перезапускает приложение.
    ///
    /// ─── Где хранить файлы обновлений ─────────────────────────────────────
    ///   • GitHub Releases — бесплатно, без кредитной карты.
    ///     ZIP загружается через gh release upload.
    ///     Манифест releases.json коммитится в репозиторий.
    ///
    /// Настройка:
    ///   • Ничего не нужно — URL манифеста захардкожен.
    ///   • Для переключения на свой форк — измените ManifestUrl.
    /// ────────────────────────────────────────────────────────────────────────
    /// </summary>
    public static class UpdateService
    {
        private const string ManifestUrl =
            "https://raw.githubusercontent.com/DdepRest/arc-frame/main/releases.json";

        /// <summary>
        /// Текущая версия приложения. Автоматически читается из
        /// <c>AssemblyInformationalVersionAttribute</c>, который .NET SDK
        /// генерирует из свойства <c>&lt;Version&gt;</c> в .csproj.
        /// Менять версию нужно только в .csproj — сюда она подтянется сама.
        /// В single-file publish атрибут сохраняется, в отличие от
        /// Assembly.GetName().Version, поэтому этот подход работает.
        /// </summary>
        internal static readonly Version CurrentVersion =
            TryResolveCurrentVersion() ?? new Version(0, 0, 0);

        private static bool _isChecking;
        private static double _downloadProgress;
        private static bool _isDownloading;

        /// <summary>
        /// True while an update check or download is in progress.
        /// Bind UI elements (buttons, spinners) to this.
        /// </summary>
        public static bool IsChecking
        {
            get => _isChecking;
            private set
            {
                _isChecking = value;
                Application.Current?.Dispatcher.Invoke(() =>
                    CheckingChanged?.Invoke(null, EventArgs.Empty));
            }
        }

        /// <summary>
        /// Download progress 0–100. Updated during the download phase
        /// of <see cref="CheckAndApplyAsync"/>.
        /// </summary>
        public static double DownloadProgress
        {
            get => _downloadProgress;
            private set
            {
                _downloadProgress = value;
                Application.Current?.Dispatcher.Invoke(() =>
                    ProgressChanged?.Invoke(null, EventArgs.Empty));
            }
        }

        /// <summary>
        /// True only during the active download phase (progress 0→100).
        /// The progress bar should be visible exactly while this is true.
        /// </summary>
        public static bool IsDownloading
        {
            get => _isDownloading;
            private set
            {
                _isDownloading = value;
                Application.Current?.Dispatcher.Invoke(() =>
                    ProgressChanged?.Invoke(null, EventArgs.Empty));
            }
        }

        /// <summary>
        /// Fires whenever <see cref="IsChecking"/> changes — use for UI bindings.
        /// </summary>
        public static event EventHandler? CheckingChanged;

        /// <summary>
        /// Fires whenever <see cref="DownloadProgress"/> or <see cref="IsDownloading"/>
        /// changes — use to update the download progress bar in the ActionBar.
        /// </summary>
        public static event EventHandler? ProgressChanged;

        /// <summary>
        /// Returns true if a previous startup check found a pending update.
        /// Used to show a visual indicator (e.g., badge on the Settings button).
        /// </summary>
        public static bool HasPendingUpdate()
        {
            string? pending = AppSettingsService.LoadPendingUpdateVersion();
            return !string.IsNullOrEmpty(pending);
        }

        /// <summary>
        /// Silent background check on startup. If an update is available,
        /// shows a toast notification. Does NOT auto-download or restart.
        /// Called from App.OnStartup after MainWindow is shown.
        /// </summary>
        public static async Task CheckOnStartupAsync()
        {
            IsChecking = true;
            try
            {
                var manifest = await FetchManifestAsync().ConfigureAwait(false);
                if (manifest == null || manifest.Releases.Count == 0)
                    return;

                Version? latestVersion = ParseSafe(manifest.Latest);
                if (latestVersion == null || latestVersion <= CurrentVersion)
                    return;

                // Update available — show a notification toast on the UI thread
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    ToastService.ShowToast(
                        $"Доступна новая версия: {manifest.Latest}. " +
                        "Нажмите «Проверить обновления» в Настройках для установки.",
                        ToastType.Info);
                });

                AppSettingsService.SavePendingUpdateVersion(manifest.Latest);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] Startup check failed: {ex.Message}");
            }
            finally
            {
                IsChecking = false;
            }
        }

        /// <summary>
        /// Interactive update flow — shows dialogs, downloads, and restarts.
        /// Call from UI thread (button click handler).
        /// </summary>
        public static async Task CheckAndApplyAsync(Window owner)
        {
            if (IsChecking)
            {
                ToastService.ShowToast("Проверка обновлений уже выполняется...", ToastType.Info);
                return;
            }

            IsChecking = true;
            try
            {
                // Phase 1: Fetch manifest
                ToastService.ShowToast("Проверка наличия обновлений...", ToastType.Info);

                var manifest = await FetchManifestAsync().ConfigureAwait(true);
                if (manifest == null || manifest.Releases.Count == 0)
                {
                    ToastService.ShowToast("Не удалось получить список обновлений.", ToastType.Warning);
                    return;
                }

                Version? latestVersion = ParseSafe(manifest.Latest);
                if (latestVersion == null)
                {
                    ToastService.ShowToast("Не удалось определить версию обновления.", ToastType.Warning);
                    return;
                }

                if (latestVersion <= CurrentVersion)
                {
                    ToastService.ShowToast("У вас последняя версия.", ToastType.Success);
                    AppSettingsService.SavePendingUpdateVersion(null);
                    return;
                }

                var release = manifest.Releases[0]; // newest first

                // Phase 2: Confirm download
                bool confirmed = DialogService.ShowUpdateAvailable(manifest.Latest, owner);
                if (!confirmed) return;

                // Phase 3: Download with progress
                IsDownloading = true;
                DownloadProgress = 0;

                string tempZip = Path.Combine(Path.GetTempPath(),
                    $"arc-update-{manifest.Latest}-{Guid.NewGuid():N}.zip");

                try
                {
                    await DownloadWithProgressAsync(release.Url, tempZip,
                        new Progress<int>(p => DownloadProgress = p)).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    IsDownloading = false;
                    TryDelete(tempZip);
                    MessageBox.Show(
                        $"Не удалось скачать обновление:\n{ex.Message}",
                        "Ошибка скачивания",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                IsDownloading = false;
                DownloadProgress = 100;

                // Phase 4: Verify SHA-256
                ToastService.ShowToast("Проверка целостности архива...", ToastType.Info);

                if (!string.IsNullOrEmpty(release.Sha256))
                {
                    string actualHash = ComputeSha256(tempZip);
                    if (!string.Equals(actualHash, release.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDelete(tempZip);
                        MessageBox.Show(
                            "Хеш-сумма архива не совпадает. Возможно, файл повреждён при скачивании.",
                            "Ошибка проверки",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;
                    }
                }

                // Phase 5: Stage update and restart
                ToastService.ShowToast("Установка обновления...", ToastType.Info);
                AppSettingsService.SavePendingUpdateVersion(null);

                WatchdogService.StageUpdate(tempZip);

                // Launch watchdog .bat — it will wait for us to exit, then swap the exe
                try
                {
                    Process.Start(new ProcessStartInfo(WatchdogService.WatchdogPath)
                    {
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[UpdateService] Failed to launch watchdog: {ex.Message}");
                }

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                var errorMsg = ex.InnerException?.Message ?? ex.Message;
                MessageBox.Show(
                    $"Не удалось проверить обновления:\n{errorMsg}",
                    "Ошибка обновления",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Debug.WriteLine($"[UpdateService] Check failed: {ex}");
            }
            finally
            {
                IsChecking = false;
                IsDownloading = false;
            }
        }

        // ─── Private helpers ──────────────────────────────────────────

        /// <summary>
        /// Fetches and deserializes the releases.json manifest from GitHub.
        /// Returns null on any failure (network, parse, etc.).
        /// </summary>
        private static async Task<UpdateManifest?> FetchManifestAsync()
        {
            try
            {
                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(15);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("MosquitoNetCalculator/3.0");

                var response = await http.GetAsync(ManifestUrl,
                    HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonSerializer.Deserialize<UpdateManifest>(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] FetchManifest failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Downloads a file from <paramref name="url"/> to <paramref name="destinationPath"/>,
        /// reporting progress 0–100 via <paramref name="progress"/>.
        /// </summary>
        private static async Task DownloadWithProgressAsync(
            string url, string destinationPath, IProgress<int> progress)
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(10);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MosquitoNetCalculator/3.0");

            using var response = await http.GetAsync(url,
                HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;

            using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var fileStream = new FileStream(destinationPath, FileMode.Create,
                FileAccess.Write, FileShare.None, 8192, useAsync: true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;
            int lastPercent = -1;

            while ((bytesRead = await contentStream.ReadAsync(
                buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
                totalRead += bytesRead;

                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    int percent = (int)(totalRead * 100 / totalBytes.Value);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress.Report(percent);
                    }
                }
            }

            // If we couldn't determine total size, report 100 at the end
            if (!totalBytes.HasValue)
                progress.Report(100);
        }

        /// <summary>
        /// Computes the SHA-256 hash of a file as a lowercase hex string.
        /// </summary>
        private static string ComputeSha256(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Safely parses a version string, returning null on failure
        /// (null input, empty whitespace, or malformed value).
        /// Exposed as <c>internal</c> for direct unit testing.
        /// </summary>
        internal static Version? ParseSafe(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return null;
            try { return new Version(version); }
            catch { return null; }
        }

        /// <summary>
        /// Strips the git commit hash suffix (e.g., "3.34.4+abc123" → "3.34.4")
        /// that .NET SDK automatically appends to
        /// <c>AssemblyInformationalVersionAttribute</c> when source control
        /// info is available. Also strips any pre-release suffix after '-'.
        /// Exposed as <c>internal</c> for direct unit testing.
        /// </summary>
        internal static string? StripVersionSuffix(string? version)
        {
            if (string.IsNullOrEmpty(version))
                return version;
            int plusIdx = version.IndexOf('+');
            if (plusIdx >= 0)
                version = version.Substring(0, plusIdx);
            int dashIdx = version.IndexOf('-');
            if (dashIdx >= 0)
                version = version.Substring(0, dashIdx);
            return version;
        }

        /// <summary>
        /// Резолвит версию из произвольной сборки (тестируется с динамическими сборками).
        /// Приоритет источников (от качественного к фоллбэку):
        /// 1. <c>AssemblyInformationalVersionAttribute</c> — содержит «3.34.4+gitHash»
        ///    при наличии source control, отсекаем суффикс → чистый 3-part («v3.34.4» в заголовке).
        /// 2. <c>AssemblyFileVersionAttribute</c> — без git hash, но содержит 4-ю часть («3.34.4.0»).
        /// 3. <c>Assembly.GetName().Version</c> — legacy fallback (в single-file обычно null).
        /// При ошибке — null (caller подставит 0.0.0 + запишет в Debug).
        /// </summary>
        internal static Version? ResolveVersion(Assembly? assembly)
        {
            try
            {
                if (assembly == null) return null;

                // 1. InformationalVersion (3.34.4+gitHash) — лучший display-источник.
                var infoAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                var vInfo = ParseSafe(StripVersionSuffix(infoAttr?.InformationalVersion));
                if (vInfo != null)
                {
                    Debug.WriteLine($"[UpdateService] Resolved via InformationalVersion='{infoAttr?.InformationalVersion}' -> {vInfo}");
                    return vInfo;
                }

                // 2. FileVersion (3.34.4.0) — fallback, даст «v3.34.4.0».
                var fileAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
                var vFile = ParseSafe(fileAttr?.Version);
                if (vFile != null)
                {
                    Debug.WriteLine($"[UpdateService] Resolved via FileVersion='{fileAttr?.Version}' -> {vFile}");
                    return vFile;
                }

                // 3. GetName().Version — legacy fallback.
                var nameVer = assembly.GetName().Version;
                if (nameVer != null)
                {
                    Debug.WriteLine($"[UpdateService] Resolved via GetName().Version -> {nameVer}");
                    return nameVer;
                }

                Debug.WriteLine("[UpdateService] All version sources failed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] Version resolve exception: {ex}");
            }
            return null;
        }

        /// <summary>
        /// Резолвит текущую версию приложения (из сборки UpdateService).
        /// Production-call site для <see cref="CurrentVersion"/>.
        /// </summary>
        internal static Version? TryResolveCurrentVersion()
            => ResolveVersion(typeof(UpdateService).Assembly);

        /// <summary>
        /// Tries to delete a file, swallowing any exceptions.
        /// </summary>
        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* best effort */ }
        }
    }
}
