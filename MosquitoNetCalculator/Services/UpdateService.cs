using System;
using System.Threading.Tasks;
using System.Windows;
using Velopack;
using Velopack.Sources;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Manages application auto-updates via Velopack.
    ///
    /// ─── Как это работает ──────────────────────────────────────────────────
    /// 1. При запуске вызывается <see cref="CheckOnStartupAsync"/> — тихая
    ///    проверка в фоне, без блокировки интерфейса.
    /// 2. Пользователь может нажать «Проверить обновления» в меню Настроек —
    ///    вызывается <see cref="CheckAndApplyAsync"/> с интерактивным диалогом.
    /// 3. Velopack сравнивает текущую версию с манифестом на сервере обновлений,
    ///    скачивает дельту (только изменившиеся файлы) и перезапускает приложение.
    ///
    /// ─── Где хранить файлы обновлений ─────────────────────────────────────
    /// Рекомендуемый бесплатный вариант:
    ///   • Velopack Flow — облачный сервис от создателей Velopack.
    ///     Бесплатный тир, 1 команда для загрузки: vpk publish.
    ///     Скрипт deploy-flow.bat делает всё автоматически.
    ///
    /// Альтернативы:
    ///   • Cloudflare R2 — 10 ГБ бесплатно, без платы за трафик
    ///   • Yandex Object Storage / S3-совместимые хранилища
    ///   • Свой сервер с Nginx/IIS
    ///
    /// Настройка:
    ///   • Для Velopack Flow — ничего дополнительно не нужно, авто-определяется
    ///     по packId (ARC-Frame). Запустите deploy-flow.bat.
    ///   • Для своего сервера / S3 — укажите UpdateUrl в settings.json.
    /// Если ничего не задано — автообновление отключено.
    /// ────────────────────────────────────────────────────────────────────────
    /// </summary>
    public static class UpdateService
    {
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
                // Marshal to UI thread — the setter may be called from
                // background threads (CheckOnStartupAsync continuation).
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
        /// Returns a configured UpdateManager, or null if not configured.
        /// Prioritises Flow over raw URL.
        /// Does NOT cache — construction is cheap and this avoids staleness
        /// if the user edits settings.json externally.
        /// </summary>
        private static UpdateManager? GetManager()
        {
            // 1. Velopack Flow — авто-определяется по packId (MosquitoNetCalculator)
            //    Никакие ключи не нужны — Flow сам знает ваш проект по packId.
            //    Запустите deploy-flow.bat для загрузки релизов.
            if (AppSettingsService.IsFlowEnabled())
            {
                return new UpdateManager(new VelopackFlowSource());
            }

            // 2. Самостоятельный хостинг (S3, сервер, локальная папка)
            string? url = AppSettingsService.LoadUpdateUrl();
            if (string.IsNullOrWhiteSpace(url)) return null;
            return new UpdateManager(url.TrimEnd('/'));
        }

        /// <summary>
        /// Silent background check on startup. If an update is available,
        /// shows a toast notification. Does NOT auto-download or restart.
        /// Called from App.OnStartup after MainWindow is shown.
        /// </summary>
        public static async Task CheckOnStartupAsync()
        {
            var mgr = GetManager();
            if (mgr == null) return; // No URL configured — skip silently

            IsChecking = true;
            try
            {
                var updateInfo = await mgr.CheckForUpdatesAsync();
                if (updateInfo != null)
                {
                    // Update available — show a notification toast on the UI thread
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        ToastService.ShowToast(
                            $"Доступна новая версия: {updateInfo.TargetFullRelease.Version}. " +
                            $"Нажмите «Проверить обновления» в Настройках для установки.",
                            ToastType.Info);
                    });

                    // Store that we have a pending update so the UI can show an indicator
                    AppSettingsService.SavePendingUpdateVersion(
                        updateInfo.TargetFullRelease.Version.ToString());
                }
            }
            catch (Exception ex)
            {
                // Silently ignore — update check failures should never disrupt
                // the user's workflow
                System.Diagnostics.Debug.WriteLine(
                    $"[UpdateService] Startup check failed: {ex.Message}");
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
            var mgr = GetManager();
            if (mgr == null)
            {
                MessageBox.Show(
                    "Автообновление не настроено.\n\n" +
                    "Укажите URL обновлений в settings.json (поле UpdateUrl).\n" +
                    "Подробнее — в документации Velopack.",
                    "Обновление недоступно",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (IsChecking)
            {
                ToastService.ShowToast("Проверка обновлений уже выполняется...", ToastType.Info);
                return;
            }

            IsChecking = true;
            try
            {
                // Phase 1: Check
                ToastService.ShowToast("Проверка наличия обновлений...", ToastType.Info);

                var updateInfo = await mgr.CheckForUpdatesAsync();

                if (updateInfo == null)
                {
                    ToastService.ShowToast("У вас последняя версия.", ToastType.Success);
                    // Clear any stale pending-update flag
                    AppSettingsService.SavePendingUpdateVersion(null);
                    return;
                }

                // Phase 2: Confirm download — Fluent dialog, not raw MessageBox
                bool confirmed = DialogService.ShowUpdateAvailable(
                    updateInfo.TargetFullRelease.Version.ToString(), owner);

                if (!confirmed) return;

                // Phase 3: Download — show progress bar in ActionBar, not toast spam
                IsDownloading = true;
                DownloadProgress = 0;

                int lastPercent = -1;
                await mgr.DownloadUpdatesAsync(updateInfo, progress =>
                {
                    if (progress != lastPercent)
                    {
                        lastPercent = progress;
                        DownloadProgress = progress;
                    }
                });

                IsDownloading = false;
                DownloadProgress = 100;

                // Phase 4: Apply and restart
                ToastService.ShowToast("Установка обновления...", ToastType.Info);

                // Clear pending flag before restart
                AppSettingsService.SavePendingUpdateVersion(null);

                mgr.ApplyUpdatesAndRestart(updateInfo);
            }
            catch (Exception ex)
            {
                var errorMsg = ex.InnerException?.Message ?? ex.Message;
                MessageBox.Show(
                    $"Не удалось проверить обновления:\n{errorMsg}",
                    "Ошибка обновления",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine(
                    $"[UpdateService] Check failed: {ex}");
            }
            finally
            {
                IsChecking = false;
                IsDownloading = false;
            }
        }

        /// <summary>
        /// Returns true if a previous startup check found a pending update.
        /// Used to show a visual indicator (e.g., badge on the Settings button).
        /// </summary>
        public static bool HasPendingUpdate()
        {
            string? pending = AppSettingsService.LoadPendingUpdateVersion();
            return !string.IsNullOrEmpty(pending);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} Б";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} КБ";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} МБ";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} ГБ";
        }
    }
}
