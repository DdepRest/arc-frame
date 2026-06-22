using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Manages application auto-updates via GitHub Releases.
    /// Replaces the previous Velopack-based implementation. Lib only — UI prompts
    /// live elsewhere (DialogService, ToastService).
    ///
    /// ─── What this does ──────────────────────────────────────────────────
    /// 1. <see cref="CheckOnStartupAsync"/> (called from App.OnStartup) — silent
    ///    background check. Hot-path caches the result via UpdateUrlOverride
    ///    in settings.json.
    /// 2. <see cref="CheckAndApplyAsync"/> (called from the Settings menu) —
    ///    interactive: tells the user a new version is ready, downloads it,
    ///    verifies SHA-256, stages it for the next run, restarts.
    /// 3. <see cref="HasPendingUpdate"/> drives the red dot on the Settings button.
    ///
    /// ─── Where files come from ─────────────────────────────────────────────
    /// GitHub Releases on https://github.com/DdepRest/arc-frame:
    ///   releases.json is stored in the repo (rendered raw via
    ///   raw.githubusercontent.com/<owner>/<repo>/main/releases.json).
    ///   Each release ZIP is attached as an asset under tagged releases.
    /// </summary>
    public static class UpdateService
    {
        private const string DefaultManifestUrl =
            "https://raw.githubusercontent.com/DdepRest/arc-frame/main/releases.json";

        // ─── HTTP clients ────────────────────────────────────────────────
        // Manifest fetcher: short timeout (checks should be fast).
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestHeaders =
            {
                { "User-Agent", "ARC-Frame-Updater/1.0" }
            }
        };

        // ZIP downloader: long timeout (100+ MB archives over slow links).
        private static readonly HttpClient _httpDownload = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15),
            DefaultRequestHeaders =
            {
                { "User-Agent", "ARC-Frame-Updater/1.0" }
            }
        };

        // ─── Bindable state (UI listens to events) ──────────────────────────
        private static bool _isChecking;
        private static double _downloadProgress;
        private static bool _isDownloading;

        public static bool IsChecking
        {
            get => _isChecking;
            private set
            {
                if (_isChecking == value) return;
                _isChecking = value;
                DispatchRaise(CheckingChanged);
            }
        }

        public static double DownloadProgress
        {
            get => _downloadProgress;
            private set
            {
                if (Math.Abs(_downloadProgress - value) < 0.5) return;
                _downloadProgress = value;
                DispatchRaise(ProgressChanged);
            }
        }

        public static bool IsDownloading
        {
            get => _isDownloading;
            private set
            {
                if (_isDownloading == value) return;
                _isDownloading = value;
                DispatchRaise(ProgressChanged);
            }
        }

        public static event EventHandler? CheckingChanged;
        public static event EventHandler? ProgressChanged;

        // ─── Manifest URL resolution ───────────────────────────────────────
        // Allows overriding manifest URL via settings.json — kept here for
        // future flexibility (e.g. self-hosted fallback). The legacy Velopack
        // 'UpdateUrl' field is repurposed for this.
        public static string GetManifestUrl()
        {
            string? custom = AppSettingsService.LoadUpdateUrl();
            if (string.IsNullOrWhiteSpace(custom)) return DefaultManifestUrl;
            string trimmed = custom.TrimEnd('/');
            // If it's a full path, leave it; otherwise treat as a base URL.
            return trimmed.EndsWith("releases.json", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : trimmed + "/releases.json";
        }

        // ─── Public API used by ActionBarControl and App.OnStartup ──────────
        /// <summary>True if a previous startup check found a pending update.</summary>
        public static bool HasPendingUpdate()
        {
            string? pending = AppSettingsService.LoadPendingUpdateVersion();
            return !string.IsNullOrEmpty(pending);
        }

        /// <summary>
        /// Silent background check on startup. If an update is available,
        /// shows a toast and stores the pending version for the badge.
        /// </summary>
        public static async Task CheckOnStartupAsync()
        {
            IsChecking = true;
            try
            {
                UpdateManifest? manifest = await FetchManifestAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                if (manifest == null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "[UpdateService] Startup check: manifest not retrieved");
                    return;
                }

                Version? current = GetCurrentVersion();
                Version? latest = ParseVersionSafely(manifest.Latest);
                if (current == null || latest == null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[UpdateService] Startup check: cannot compare current={current} vs latest={latest}");
                    return;
                }

                if (latest > current)
                {
                    await ShowPendingToastAsync(manifest.Latest).ConfigureAwait(false);
                    AppSettingsService.SavePendingUpdateVersion(manifest.Latest);
                }
                else
                {
                    AppSettingsService.SavePendingUpdateVersion(null);
                }
            }
            catch (Exception ex)
            {
                // Silently ignore — startup failures should never disrupt UX.
                System.Diagnostics.Debug.WriteLine(
                    $"[UpdateService] Startup check failed: {ex.Message}");
            }
            finally
            {
                IsChecking = false;
            }
        }

        /// <summary>
        /// Interactive update flow — shown when the user clicks
        /// «Проверить обновления» in the Settings menu. Shows dialogs,
        /// downloads with progress, and arranges for restart.
        /// </summary>
        public static async Task CheckAndApplyAsync(Window owner)
        {
            if (IsChecking)
            {
                ToastService.ShowToast(
                    "Проверка обновлений уже выполняется...", ToastType.Info);
                return;
            }

            IsChecking = true;
            try
            {
                ToastService.ShowToast(
                    "Проверка наличия обновлений...", ToastType.Info);

                UpdateManifest? manifest = await FetchManifestAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                if (manifest?.Releases == null || manifest.Releases.Count == 0)
                {
                    ToastService.ShowToast(
                        "Не удалось получить список обновлений.",
                        ToastType.Warning);
                    return;
                }

                Version? current = GetCurrentVersion();
                ReleaseInfo? newest = manifest.Releases[0]; // first = newest in our schema
                Version? latest = ParseVersionSafely(newest.Version);

                if (current == null || latest == null || latest <= current)
                {
                    ToastService.ShowToast(
                        "У вас последняя версия.", ToastType.Success);
                    AppSettingsService.SavePendingUpdateVersion(null);
                    return;
                }

                bool confirmed = DialogService.ShowUpdateAvailable(newest.Version, owner);
                if (!confirmed) return;

                IsDownloading = true;
                DownloadProgress = 0;

                string downloadedZip = await DownloadAndVerifyAsync(
                    newest, CancellationToken.None).ConfigureAwait(false);

                IsDownloading = false;
                DownloadProgress = 100;

                ToastService.ShowToast(
                    "Установка обновления...", ToastType.Info);

                WatchdogService.StageUpdate(downloadedZip);

                try { File.Delete(downloadedZip); } catch { /* swallow */ }

                AppSettingsService.SavePendingUpdateVersion(null);

                // Launch watchdog.bat and shut down. Watchdog will replace .exe
                // and restart us. UseShellExecute=true so the .bat inherits env.
                var psi = new System.Diagnostics.ProcessStartInfo(WatchdogService.WatchdogPath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = WatchdogService.BasePath,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                System.Diagnostics.Process.Start(psi);

                Application.Current?.Shutdown();
            }
            catch (Exception ex)
            {
                string errorMsg = ex.InnerException?.Message ?? ex.Message;
                MessageBox.Show(
                    $"Не удалось выполнить обновление:\n{errorMsg}",
                    "Ошибка обновления",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine(
                    $"[UpdateService] Check failed: {ex}");
                IsDownloading = false;
            }
            finally
            {
                IsChecking = false;
                IsDownloading = false;
            }
        }

        // ─── Internals ──────────────────────────────────────────────────────
        private static async Task<UpdateManifest?> FetchManifestAsync(CancellationToken ct)
        {
            string url = GetManifestUrl();
            try
            {
                using HttpResponseMessage resp = await _http.GetAsync(
                    url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[UpdateService] Manifest HTTP {resp.StatusCode} for {url}");
                    return null;
                }

                string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json)) return null;

                UpdateManifest? manifest = JsonSerializer.Deserialize<UpdateManifest>(json);
                return manifest;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[UpdateService] FetchManifest failed for {url}: {ex.Message}");
                return null;
            }
        }

        private static async Task<string> DownloadAndVerifyAsync(
            ReleaseInfo release, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(release.Url))
                throw new InvalidOperationException("Release manifest has empty URL");

            string tempZip = Path.Combine(
                Path.GetTempPath(),
                $"ARC-Frame-{SafeVersion(release.Version)}-download-{Guid.NewGuid():N}.zip");

            try
            {
                using HttpResponseMessage resp = await _httpDownload.GetAsync(
                    release.Url, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException(
                        $"HTTP {(int)resp.StatusCode} downloading release");

                long? total = resp.Content.Headers.ContentLength;
                long received = 0;
                int lastReportedPercent = -1;

                using (FileStream fs = new FileStream(
                    tempZip, FileMode.Create, FileAccess.Write, FileShare.None,
                    bufferSize: 81920, useAsync: true))
                using (Stream net = await resp.Content.ReadAsStreamAsync()
                    .ConfigureAwait(false))
                {
                    byte[] buffer = new byte[81920];
                    int read;
                    while ((read = await net.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        await fs.WriteAsync(
                            buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        received += read;

                        if (total.HasValue && total.Value > 0)
                        {
                            int percent = (int)(received * 100L / total.Value);
                            // Throttle updates so UI thread isn't flooded.
                            if (percent != lastReportedPercent && percent % 2 == 0)
                            {
                                lastReportedPercent = percent;
                                DownloadProgress = percent;
                            }
                        }
                    }
                }
                DownloadProgress = 100;

                // SHA-256 verify.
                string actual = await ComputeSha256HexAsync(tempZip, ct)
                    .ConfigureAwait(false);
                string expected = (release.Sha256 ?? string.Empty)
                    .Trim().ToLowerInvariant();

                if (string.IsNullOrEmpty(expected))
                {
                    throw new InvalidOperationException(
                        "Manifest has no SHA-256 for this release — build pipeline error");
                }
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"SHA-256 mismatch.\nExpected: {expected}\nGot:      {actual}");
                }

                return tempZip;
            }
            catch
            {
                try { File.Delete(tempZip); } catch { /* swallow */ }
                throw;
            }
        }

        private static async Task<string> ComputeSha256HexAsync(string path, CancellationToken ct)
        {
            using FileStream fs = File.OpenRead(path);
            using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buf = new byte[81920];
            int read;
            while ((read = await fs.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
            {
                hasher.AppendData(buf, 0, read);
            }
            byte[] hash = hasher.GetHashAndReset();
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static async Task ShowPendingToastAsync(string version)
        {
            // Toast must be shown on UI thread; we may be on a thread-pool thread here.
            Application? app = Application.Current;
            if (app == null) return;
            await app.Dispatcher.InvokeAsync(() =>
            {
                ToastService.ShowToast(
                    $"Доступна новая версия: {version}. " +
                    "Нажмите «Проверить обновления» в Настройках для установки.",
                    ToastType.Info);
            });
        }

        private static void DispatchRaise(EventHandler? handler)
        {
            // Marshal to UI thread — setters may be called from background
            // continuations of CheckOnStartupAsync / DownloadAndVerifyAsync.
            Application.Current?.Dispatcher.BeginInvoke(
                DispatcherPriority.Normal, () => handler?.Invoke(null, EventArgs.Empty));
        }

        private static Version? GetCurrentVersion()
        {
            try
            {
                Assembly? entry = Assembly.GetEntryAssembly();
                return entry?.GetName().Version;
            }
            catch
            {
                return null;
            }
        }

        private static Version? ParseVersionSafely(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim().TrimStart('v', 'V');
            // Version.Parse requires 2-4 dotted parts. Pad with zeros.
            while (s.Split('.').Length < 2) s += ".0";
            return Version.TryParse(s, out Version? v) ? v : null;
        }

        private static string SafeVersion(string v)
        {
            // Strip anything that isn't friendly for a temp filename.
            foreach (char c in Path.GetInvalidFileNameChars()) v = v.Replace(c, '_');
            return v;
        }
    }
}
