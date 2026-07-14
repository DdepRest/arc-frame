using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
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
    ///   • Для переключения на свой форк — измените ManifestUrl в UpdateManifestClient.
    /// ────────────────────────────────────────────────────────────────────────
    /// </summary>
    public static class UpdateService
    {



        // ─── Idle detection — delegated to IdleDetector (Phase 2) ──────

        /// <summary>
        /// Returns the time span since the last user input (mouse or keyboard)
        /// across the entire system, using WinAPI <c>GetLastInputInfo</c>.
        /// Delegates to <see cref="IdleDetector.GetIdleTime"/>.
        /// </summary>
        public static TimeSpan GetIdleTime() => IdleDetector.GetIdleTime();

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

        // ─── Phase 2: version logic delegated to VersionResolver ──────

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
        /// Fires when a background check discovers a release newer than the
        /// running <see cref="CurrentVersion"/>. The payload is a stub
        /// <see cref="UpdateItem"/> with only <c>Version</c> known — the
        /// running binary can't show the future release's full changelog
        /// (that's encoded in the next version's embedded
        /// <c>update-log.json</c>). Subscribe in <c>MainWindow</c> and forward
        /// to <see cref="ViewModels.MainWindowViewModel.AddNewUpdate"/> on the
        /// UI dispatcher to surface a "Доступно обновление vX.Y.Z" card in
        /// the Updates tab without restarting.
        ///
        /// Subscribers MUST unsubscribe from <see cref="MainWindow.Closed"/>
        /// — a static source on a per-window subscriber creates a strong
        /// root that otherwise outlives the window.
        /// </summary>
        public static event Action<UpdateItem>? UpdateDetected;

        /// <summary>
        /// Test seam <c>internal</c>: optional override that replaces the
        /// <see cref="DialogService.ShowUpdateAvailable"/> call from
        /// <see cref="RunUpdateFlowAsync"/>. Production code path uses the
        /// <c>??</c> fallback when this is null, so behavior is unchanged.
        /// <para>
        /// Signature returns <c>bool</c> only (not the full tuple) because
        /// tests don't need to vary the dialog's display chrome — they only
        /// need the user's "confirm/cancel" decision. Owner and
        /// isAutomatic flags are intentionally NOT injectable so the
        /// dialog code path remains the single source of truth for those.
        /// </para>
        /// </summary>
        internal static Func<string, IEnumerable<UpdateItem>, bool>? ShowUpdateAvailableOverride;


        /// <summary>
        /// Invokes <see cref="UpdateDetected"/> with **per-subscriber**
        /// isolation: a single throwing handler aborts its own run but
        /// never blocks subsequent subscribers in the same call.
        /// This is required because the static event is multicast — a
        /// naive <c>try { handler(item); } catch { ... }</c> would short-
        /// circuit the whole invocation list on the first throw, hiding
        /// the bug in one subscriber from any later subscriber that
        /// shares the same dispatch.
        ///
        /// Logged to Debug; subscribers should NOT treat this as a
        /// delivery guarantee. Marked <c>internal</c> (not <c>private</c>)
        /// so <c>MosquitoNetCalculator.Tests</c> (already granted
        /// <c>InternalsVisibleTo</c>) can assert per-subscriber isolation
        /// without reflection on backing delegates.
        /// </summary>
        internal static void FireUpdateDetected(UpdateItem item)
        {
            var handler = UpdateDetected;
            if (handler == null) return;
            foreach (var d in handler.GetInvocationList())
            {
                try { ((Action<UpdateItem>)d)(item); }
                catch (Exception ex) { Debug.WriteLine($"[UpdateService] UpdateDetected subscriber threw: {ex.Message}"); }
            }
        }

        /// <summary>
        /// Builds the stub <see cref="UpdateItem"/> shown in the Updates tab
        /// when the auto-update flow detects a newer release. Running binary
        /// can't show the next version's full changelog (encoded in its own
        /// embedded update-log.json), so we ship a placeholder. The full
        /// changelog surfaces automatically after the user installs and
        /// restarts — <c>MainWindowViewModel.ctor</c> re-reads
        /// <c>UpdateLog.AllNewestFirst()</c> on cold start.
        /// </summary>
        internal static UpdateItem CreateReleaseStub(string version) => new UpdateItem
        {
            Version = version,
            Date = DateTime.UtcNow,
            Type = "Доступно",
            Title = $"Доступно обновление v{version}",
            Changes = new System.Collections.Generic.List<string>
            {
                "Установите обновление, чтобы увидеть подробности изменений в этой версии."
            }
        };

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
        /// Returns <c>true</c> if <paramref name="version"/> falls into the
        /// known-broken-for-auto-update half-open interval.
        /// Delegates to <see cref="VersionResolver.IsBrokenForAutoUpdate"/>.
        /// </summary>
        internal static bool IsCurrentVersionBrokenForAutoUpdate(Version? version)
            => VersionResolver.IsBrokenForAutoUpdate(version);

        /// <summary>
        /// Версия, для которой уже была показана фоновая плашка в текущей
        /// сессии. Используется чтобы не спамить одной и той же плашкой при
        /// каждом тике <see cref="UpdateCheckScheduler"/>. Сбрасывается:
        /// • при ручном «Обновить сейчас» из плашки (чтобы при ошибке загрузки
        ///   следующий scheduler-tick снова предложил установку);
        /// • при успешном завершении обновления (pending уже null).
        /// </summary>
        private static string? _lastNotifiedVersion;

        /// <summary>
        /// Тихая фоновая проверка, вызываемая из <see cref="UpdateCheckScheduler"/>
        /// каждые <see cref="UpdateCheckScheduler.CheckInterval"/> или после
        /// <see cref="UpdateCheckScheduler.IdleThreshold"/> простоя.
        ///
        /// В отличие от <see cref="CheckOnStartupAsync"/> / <see cref="CheckAndApplyAsync"/>,
        /// НЕ открывает модальный диалог — пользователь увидит плашку
        /// (<see cref="ToastService.ShowUpdateNotification"/>) с кнопками
        /// «Обновить сейчас» и «Позже».
        ///
        /// Поведение:
        /// • Манифест пуст / up-to-date / ошибка — silent (никаких toast).
        /// • Update available — сохранить pending + показать плашку (один раз
        ///   на версию в сессии).
        /// • «Обновить сейчас» → <see cref="CheckAndApplyAsync"/>(manual),
        ///   который уже открывает modal с changelog.
        /// • «Позже» → pending остаётся сохранённым, плашка закрывается.
        ///
        /// Плашка показывается только если ещё не показывалась для этой версии
        /// в текущей сессии.
        /// </summary>
        public static async Task CheckInBackgroundAsync()
        {
            // Skip if window is minimized — toast would be invisible and
            // the next tick after restore will catch up (periodic gate).
            var owner = Application.Current?.MainWindow;
            if (owner?.WindowState == WindowState.Minimized) return;

            // Re-entry guard на случай параллельного тика шедулером
            // или одновременного ручного вызова.
            if (IsChecking) return;
            IsChecking = true;
            try
            {
                var manifest = await FetchManifestAsync().ConfigureAwait(true);
                var release = GetAvailableUpdate(manifest, CurrentVersion);

                if (release == null)
                {
                    // silent — фоновые проверки не должны шуметь
                    return;
                }

                // Доступно обновление — фиксируем pending (на случай краша).
                AppSettingsService.SavePendingUpdateVersion(release.Version);

                // Плашку показываем только один раз на версию за сессию.
                if (_lastNotifiedVersion == release.Version) return;
                _lastNotifiedVersion = release.Version;

                var changelog = UpdateLog.GetChangesSince(CurrentVersion);

                // Снимок owner для замыкания — иначе к моменту клика по
                // кнопке приложение могло закрыться.
                Action onUpdate = () =>
                {
                    // Сбрасываем «уже показывали», чтобы при ошибке загрузки
                    // следующий фоновый tick смог предложить установить снова.
                    _lastNotifiedVersion = null;
                    if (owner != null)
                        _ = CheckAndApplyAsync(owner, isAutomatic: false);
                };

                // onLater — пользователь сам решил не сейчас. Pending остаётся,
                // плашка закрывается. При следующем tick-е того же релиза —
                // плашка НЕ покажется (см. guard выше); покажется при выходе
                // новой версии.
                Action onLater = () => { /* pending уже сохранён */ };

                ToastService.ShowUpdateNotification(
                    version: release.Version,
                    changelogCount: changelog.Length,
                    onUpdate: onUpdate,
                    onLater: onLater);

                // Поверхностное уведомление для вкладки «Обновления».
                // Запущенный бинарник не может показать полный changelog
                // будущей версии (он вшит в её embedded update-log.json),
                // поэтому отправляем stub с одной строкой. Полный changelog
                // подтянется автоматически ПОСЛЕ установки обновления и
                // перезапуска — MainWindow.ctor перечитает UpdateLog.AllNewestFirst().
                FireUpdateDetected(CreateReleaseStub(release.Version));
            }
            catch (Exception ex)
            {
                // Фоновая проверка не должна крашить приложение; логируем
                // в Debug и тихо выходим. Rollbar / sentry сюда добавлять
                // в будущем, если потребуется.
                Debug.WriteLine($"[UpdateService] Background check failed: {ex}");
            }
            finally
            {
                IsChecking = false;
            }
        }

        /// <summary>
        /// Silent background check on startup. If an update is available,
        /// shows the update dialog automatically with changelog.
        /// Called from App.OnStartup after MainWindow is shown.
        /// </summary>
        public static async Task CheckOnStartupAsync()
        {
            var owner = Application.Current?.MainWindow;
            if (owner == null) return;

            await RunUpdateFlowAsync(owner, isAutomatic: true);
        }

        /// <summary>
        /// Interactive update flow — shows dialogs, downloads, and restarts.
        /// Call from UI thread (button click handler).
        /// <paramref name="isAutomatic"/> distinguishes startup auto-check from
        /// manual check via Settings menu (affects toast verbosity).
        /// </summary>
        public static async Task CheckAndApplyAsync(Window owner, bool isAutomatic = false)
        {
            await RunUpdateFlowAsync(owner, isAutomatic);
        }

        /// <summary>
        /// Analyzes an update manifest against a reference version and returns
        /// the newest <see cref="ReleaseInfo"/> if an update is available.
        /// Delegates to <see cref="VersionResolver.GetAvailableUpdate"/>.
        /// </summary>
        internal static ReleaseInfo? GetAvailableUpdate(UpdateManifest? manifest, Version currentVersion)
            => VersionResolver.GetAvailableUpdate(manifest, currentVersion);

        /// <summary>
        /// Shared update flow: fetch manifest → show changelog dialog → download →
        /// verify → restart. Used by both startup auto-check and manual check.
        /// </summary>
        internal static async Task RunUpdateFlowAsync(Window? owner, bool isAutomatic, HttpClient? httpClient = null)
        {
            if (IsChecking)
            {
                if (!isAutomatic)
                    ToastService.ShowToast("Проверка обновлений уже выполняется...", ToastType.Info);
                return;
            }

            IsChecking = true;
            try
            {
                if (!isAutomatic)
                    ToastService.ShowToast("Проверка наличия обновлений...", ToastType.Info);

                var manifest = await FetchManifestAsync(httpClient).ConfigureAwait(true);
                var release = GetAvailableUpdate(manifest, CurrentVersion);

                if (release == null)
                {
                    if (!isAutomatic)
                    {
                        if (manifest == null || manifest.Releases.Count == 0)
                            ToastService.ShowToast("Не удалось получить список обновлений.", ToastType.Warning);
                        else if (ParseSafe(manifest.Latest) == null)
                            ToastService.ShowToast("Не удалось определить версию обновления.", ToastType.Warning);
                        else
                            ToastService.ShowToast("Обновлений нет ✓", ToastType.Success);
                    }
                    AppSettingsService.SavePendingUpdateVersion(null);
                    return;
                }

                // Phase 2: Confirm download with changelog
                var changelog = UpdateLog.GetChangesSince(CurrentVersion);
                // ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
                // Dialog mock seam: tests inject ShowUpdateAvailableOverride to bypass
                // WPF modal. Production path is unchanged when override is null.
                // ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
                bool confirmed = ShowUpdateAvailableOverride?.Invoke(manifest!.Latest, changelog)
                    ?? DialogService.ShowUpdateAvailable(manifest!.Latest, changelog, owner, isAutomatic);

                // Fire UpdateDetected as soon as the user ACCEPTS the dialog
                // (gated by `confirmed` so cancel-and-defer doesn't spam the
                // Updates tab). The mock used here mirrors CheckInBackgroundAsync:
                // running binary can't see the next version's full changelog
                // (encoded in its own embedded update-log.json), so we ship a
                // stub. The Modal flow + the Background flow now agree on the
                // same payload shape and same fire contract.
                if (confirmed)
                {
                    FireUpdateDetected(CreateReleaseStub(manifest.Latest));
                }
                else
                {
                    // User deferred — still save pending for next session.
                    AppSettingsService.SavePendingUpdateVersion(manifest.Latest);
                    return;
                }

                // Phase 3: Download with progress
                IsDownloading = true;
                DownloadProgress = 0;

                string tempZip = Path.Combine(Path.GetTempPath(),
                    $"arc-update-{manifest.Latest}-{Guid.NewGuid():N}.zip");

                try
                {
                    await DownloadWithProgressAsync(release.Url, tempZip,
                        new Progress<int>(p => DownloadProgress = p),
                        httpClient).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    IsDownloading = false;
                    TryDelete(tempZip);
                    if (isAutomatic)
                    {
                        ToastService.ShowToast($"Не удалось скачать обновление: {ex.Message}", ToastType.Error);
                    }
                    else
                    {
                        MessageBox.Show(
                            $"Не удалось скачать обновление:\n{ex.Message}",
                            "Ошибка скачивания",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                    return;
                }

                IsDownloading = false;
                DownloadProgress = 100;

                // Phase 4: Verify SHA-256 (MANDATORY — skip releases without hash is unsafe)
                ToastService.ShowToast("Проверка целостности архива...", ToastType.Info);

                if (string.IsNullOrEmpty(release.Sha256))
                {
                    TryDelete(tempZip);
                    var msg = "В манифесте отсутствует SHA-256 хеш для этой версии. Обновление отменено.";
                    if (isAutomatic)
                        ToastService.ShowToast(msg, ToastType.Error);
                    else
                        MessageBox.Show(msg, "Ошибка проверки", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!UpdateVerifier.VerifyHash(tempZip, release.Sha256))
                {
                    TryDelete(tempZip);
                    if (isAutomatic)
                    {
                        ToastService.ShowToast("Хеш-сумма архива не совпадает. Возможно, файл повреждён.", ToastType.Error);
                    }
                    else
                    {
                        MessageBox.Show(
                            "Хеш-сумма архива не совпадает. Возможно, файл повреждён при скачивании.",
                            "Ошибка проверки",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                    return;
                }

                // Phase 5: Stage update and restart
                ToastService.ShowToast("Установка обновления...", ToastType.Info);
                AppSettingsService.SavePendingUpdateVersion(null);
                // Сбрасываем «уже показывали плашку» — обновление завершено,
                // следующий фоновый tick сможет предложить новую версию.
                _lastNotifiedVersion = null;

                WatchdogService.StageUpdate(tempZip);

                // Launch watchdog .bat with UAC elevation to ensure write access
                // to protected directories (e.g. Program Files).
                // .bat files are registered with 'cmdfile' in the registry, so
                // Verb='runas' on the .bat path itself works directly — no
                // `cmd.exe /c` wrapper needed (and the wrapper breaks on
                // AppLocker-restricted systems where cmd.exe is blocked).
                try
                {
                    Process.Start(new ProcessStartInfo(WatchdogService.WatchdogPath)
                    {
                        UseShellExecute = true,
                        Verb = "runas", // triggers UAC prompt if needed
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[UpdateService] Failed to launch watchdog elevated: {ex.Message}");
                    // Fallback: try without elevation (user may have write access)
                    try
                    {
                        Process.Start(new ProcessStartInfo(WatchdogService.WatchdogPath)
                        {
                            UseShellExecute = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        });
                    }
                    catch (Exception ex2)
                    {
                        Debug.WriteLine($"[UpdateService] Fallback watchdog launch also failed: {ex2}");
                    }
                }

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                var errorMsg = ex.InnerException?.Message ?? ex.Message;
                if (isAutomatic)
                {
                    ToastService.ShowToast($"Не удалось проверить обновления: {errorMsg}", ToastType.Error);
                }
                else
                {
                    MessageBox.Show(
                        $"Не удалось проверить обновления:\n{errorMsg}",
                        "Ошибка обновления",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                Debug.WriteLine($"[UpdateService] Check failed: {ex}");
            }
            finally
            {
                IsChecking = false;
                IsDownloading = false;
            }
        }

        // ─── Private helpers — delegated to extracted components (Phase 2) ──

        /// <summary>
        /// Fetches and deserializes the releases.json manifest from GitHub.
        /// Delegates to <see cref="UpdateManifestClient.FetchManifestAsync"/>.
        /// </summary>
        internal static Task<UpdateManifest?> FetchManifestAsync(HttpClient? httpClient = null)
            => UpdateManifestClient.FetchManifestAsync(httpClient);

        /// <summary>
        /// Downloads a file with progress reporting and retry logic.
        /// Delegates to <see cref="UpdateDownloader.DownloadWithProgressAsync"/>.
        /// </summary>
        internal static Task DownloadWithProgressAsync(
            string url, string destinationPath, IProgress<int> progress, HttpClient? httpClient = null)
            => UpdateDownloader.DownloadWithProgressAsync(url, destinationPath, progress, httpClient);

        // ─── Version helpers — delegated to VersionResolver (Phase 2) ──

        /// <summary>
        /// Safely parses a version string, returning null on failure.
        /// Delegates to <see cref="VersionResolver.ParseSafe"/>.
        /// </summary>
        internal static Version? ParseSafe(string? version) => VersionResolver.ParseSafe(version);

        /// <summary>
        /// Strips git hash / pre-release suffix from a version string.
        /// Delegates to <see cref="VersionResolver.StripVersionSuffix"/>.
        /// </summary>
        internal static string? StripVersionSuffix(string? version) => VersionResolver.StripVersionSuffix(version);

        /// <summary>
        /// Resolves version from an assembly's attributes.
        /// Delegates to <see cref="VersionResolver.ResolveVersion"/>.
        /// </summary>
        internal static Version? ResolveVersion(Assembly? assembly) => VersionResolver.ResolveVersion(assembly);

        /// <summary>
        /// Resolves the current application version.
        /// Delegates to <see cref="VersionResolver.ResolveVersion"/>.
        /// </summary>
        internal static Version? TryResolveCurrentVersion()
            => VersionResolver.ResolveVersion(typeof(UpdateService).Assembly);

        /// <summary>
        /// Tries to delete a file, swallowing any exceptions.
        /// Delegates to <see cref="UpdateDownloader.TryDelete"/>.
        /// </summary>
        private static void TryDelete(string path) => UpdateDownloader.TryDelete(path);
    }
}
