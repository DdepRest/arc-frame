using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// Админ-панель — контейнер секций (вкладок). Секции читают отчёты офисов
    /// из gist и показывают разные срезы: «Обновления» (статусы версий) и
    /// «Статистика» (кол-во заказов). Новые секции добавляются как TabItem + свой
    /// список строк — канал данных (gist) и формат отчёта при этом не меняются.
    /// См. OfficeReportService (канал), OfficeStatusCalculator / OfficeStatsCalculator.
    ///
    /// UX:
    ///   - иконки статусов рядом с цветом (✓ ⚠ ❓) — взгляд «цепляет» даже без цвета;
    ///   - клик на устаревший офис → копирует напоминание («поставьте vX.Y.Z — {URL}»)
    ///     и показывает тост «Напоминание скопировано»;
    ///   - авто-рефреш каждые 15 мин, пока панель открыта — статусы, статистика
    ///     и свой отчёт держатся свежими без кнопки «Обновить»; в шапке видно
    ///     «обновлено 14:32 · авто через 12 мин»;
    ///   - автоочистка gist при каждом рефреше: файлы-дубли, молчащие >24 ч
    ///     (OfficeReportService.StaleDuplicateAfter), удаляются тихо; свежие
    ///     дубли (две живые копии на одном ПК) не трогаются;
    ///   - пустые состояния для обеих вкладок (если отчётов ещё нет).
    /// </summary>
    public partial class AdminPanelControl : UserControl
    {
        public ObservableCollection<OfficeStatusRow> Rows { get; } = new();
        public ObservableCollection<OfficeStatsRow> StatsRows { get; } = new();

        // Состояние панели для шапки/напоминаний.
        private Version? _latestVersion;
        private string? _latestDownloadUrl;
        private string? _latestFetchError;
        private DateTime _lastRefreshedAtLocal;
        private DispatcherTimer? _autoRefreshTimer;
        private bool _isRefreshing;

        // Интервал авто-рефреша панели: 15 мин. Раньше был 2 ч (синхронно с
        // OfficeReportScheduler) — но админ-панель смотрит один человек, и
        // устаревшие до 2 ч данные заставляли жать «Обновить» вручную. Теперь
        // панель сама держит данные свежими (≤15 мин), и каждый рефреш заодно
        // шлёт свежий отчёт этого ПК (SendReportAsync внутри RefreshAsync).
        // Отправка отчётов при ЗАКРЫТОЙ панели остаётся редкой (запуск программы
        // + раз в 2 ч, OfficeReportScheduler) — gist не «спамится», когда панель
        // никто не смотрит.
        private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromMinutes(15);

        public AdminPanelControl()
        {
            InitializeComponent();
            DataContext = this;

            // Клик на любой элемент списка офисов → пробуем распознать устаревший ряд
            // и скопировать напоминание. Один обработчик на ItemsControl ловит клик
            // на любой дочерней карточке, не добавляя команды в каждую строку.
            OfficesList.PreviewMouseLeftButtonUp += OfficesList_PreviewMouseLeftButtonUp;
        }

        /// <summary>
        /// Запуск панели: первый рефреш + запуск таймера авто-обновления.
        /// Вызывается из MainWindow при показе оверлея.
        /// </summary>
        public async Task StartAsync()
        {
            StartAutoRefresh();
            await RefreshAsync(quiet: false, isInitial: true).ConfigureAwait(true);
        }

        /// <summary>
        /// Остановка авто-рефреша — вызывается при закрытии оверлея панели,
        /// чтобы таймер не крутил рефреши в фоне.
        /// </summary>
        public void StopAutoRefresh()
        {
            _autoRefreshTimer?.Stop();
            _autoRefreshTimer = null;
        }

        private void StartAutoRefresh()
        {
            StopAutoRefresh();
            _autoRefreshTimer = new DispatcherTimer { Interval = AutoRefreshInterval };
            _autoRefreshTimer.Tick += async (_, _) =>
            {
                if (_isRefreshing) return; // пропускаем тик, если предыдущий ещё идёт
                await RefreshAsync(quiet: true, isInitial: false).ConfigureAwait(true);
            };
            _autoRefreshTimer.Start();
        }

        /// <summary>
        /// Полное обновление панели: тихо отправляет отчёт этого офиса, читает
        /// отчёты всех офисов и последнюю версию с GitHub, пересчитывает статусы
        /// и статистику. Никогда не бросает исключений — сбои показываются в UI.
        /// </summary>
        /// <param name="quiet">true при фоновом автообновлении — без текста «Проверка…»
        /// и без дизейбла кнопки (нет мигания UI).</param>
        /// <param name="isInitial">true при первом показе панели — без «обновлено…» в шапке.</param>
        public async Task RefreshAsync(bool quiet = false, bool isInitial = false)
        {
            _isRefreshing = true;
            try
            {
                if (!quiet)
                {
                    TxtSummary.Text = "Проверка…";
                    TxtStatsTotal.Text = "Всего заказов: …";
                    BtnRefresh.IsEnabled = false;
                }
                BannerNoConnection.Visibility = Visibility.Collapsed;

                if (!OfficeReportService.IsConfigured)
                {
                    Rows.Clear();
                    StatsRows.Clear();
                    TxtLatestVersion.Text = "Хранилище отчётов не настроено";
                    TxtSummary.Text = "— из — актуальны";
                    TxtSummaryBadge.Text = string.Empty;
                    TxtStatsTotal.Text = "Всего заказов: —";
                    TxtStatsTotalBadge.Text = string.Empty;
                    BannerNotConfigured.Visibility = Visibility.Visible;
                    UpdateEmptyStates();
                    return;
                }

                // 1) Свой отчёт — чтобы панель сразу показывала актуальный статус этого офиса.
                await OfficeReportService.SendReportAsync().ConfigureAwait(true);

                // 2) Отчёты всех офисов + последняя версия релиза (параллельно).
                var reportsTask = OfficeReportService.FetchReportsAsync();
                var latestTask = FetchLatestVersionAsync();

                await Task.WhenAll(reportsTask, latestTask).ConfigureAwait(true);

                var reports = reportsTask.Result;
                _latestVersion = latestTask.Result.Version;
                _latestDownloadUrl = latestTask.Result.Url;
                _latestFetchError = latestTask.Result.FetchError;

                // 3) Статусы (секция «Обновления»).
                var currentPrefix = AppSettingsService.LoadContractPrefix();
                var rows = OfficeStatusCalculator.BuildRows(
                    LocationOptions.All, reports, _latestVersion, DateTimeOffset.UtcNow, currentPrefix);

                Rows.Clear();
                foreach (var row in rows)
                    Rows.Add(row);

                // Сводка по УСТРОЙСТВАМ: в одном офисе может быть несколько ПК.
                // Актуальность считаем по свежим устройствам; устаревшие офисы —
                // по строкам (клик по ним копирует напоминание).
                int knownDevices = rows.Sum(r => r.DeviceCount);
                int upToDateDevices = rows.Sum(r => r.Devices.Count(d => d.Status == OfficeStatus.UpToDate));
                int outdatedRows = rows.Count(r => r.Status == OfficeStatus.Outdated);
                TxtSummary.Text = $"{upToDateDevices} из {knownDevices} устройств актуальны";
                TxtSummaryHint.Text = outdatedRows > 0
                    ? $"Кликните на устаревший офис ({outdatedRows}) — скопируется напоминание об обновлении."
                    : "Все устройства в актуальной версии.";
                TxtSummaryBadge.Text = $"{upToDateDevices}/{knownDevices}";

                // 4) Статистика.
                var statsRows = OfficeStatsCalculator.BuildRows(LocationOptions.All, reports, currentPrefix);

                StatsRows.Clear();
                foreach (var row in statsRows)
                    StatsRows.Add(row);

                int total = OfficeStatsCalculator.SumOrderCounts(statsRows);
                TxtStatsTotal.Text = reports.Count > 0
                    ? "Всего заказов в программах офисов"
                    : "Всего заказов: — (отчётов ещё нет)";
                TxtStatsTotalBadge.Text = reports.Count > 0 ? total.ToString() : string.Empty;

                // 5) Строка последней версии — при сбое показываем точную причину
                //    (таймаут/HTTP-код/сеть), а не обезличенное «нет связи».
                TxtLatestVersion.Text = _latestVersion != null
                    ? $"Последняя версия: v{_latestVersion}" + (_latestDownloadUrl != null ? "" : " · URL недоступен")
                    : "Последняя версия: недоступна" + (string.IsNullOrEmpty(_latestFetchError)
                        ? " (нет связи с GitHub)"
                        : $": {_latestFetchError}");

                _lastRefreshedAtLocal = DateTime.Now;
                UpdateEmptyStates();

                // 6) Автоочистка хвостов gist: файлы-дубли, молчащие >24 ч (старый
                //    deviceId того же ПК, легаси-записи при наличии именованных),
                //    удаляются тихо и без UI. Fire-and-forget — чистка не задерживает
                //    показ данных; свежие дубли не трогаются (StaleDuplicateAfter).
                _ = CleanupStaleDuplicatesQuietlyAsync();
            }
            catch (Exception)
            {
                BannerNoConnection.Visibility = Visibility.Visible;
                TxtSummary.Text = "— из — актуальны";
                TxtSummaryBadge.Text = string.Empty;
                TxtStatsTotal.Text = "Всего заказов: —";
                TxtStatsTotalBadge.Text = string.Empty;
            }
            finally
            {
                _isRefreshing = false;
                if (!quiet)
                    BtnRefresh.IsEnabled = true;
                // Тик обратного отсчёта в шапке — на каждом рефреше/тике.
                UpdateRefreshStatusText();
            }
        }

        /// <summary>
        /// Показывает/скрывает пустые состояния вкладок в зависимости от наличия данных.
        /// </summary>
        private void UpdateEmptyStates()
        {
            bool noData = Rows.Count == 0;
            UpdatesEmpty.Visibility = noData ? Visibility.Visible : Visibility.Collapsed;
            OfficesList.Visibility = noData ? Visibility.Collapsed : Visibility.Visible;

            bool noStats = StatsRows.Count == 0;
            StatsEmpty.Visibility = noStats ? Visibility.Visible : Visibility.Collapsed;
            StatsList.Visibility = noStats ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// Обновляет текст «обновлено HH:mm · авто через N мин» в шапке.
        /// </summary>
        private void UpdateRefreshStatusText()
        {
            if (_lastRefreshedAtLocal == default)
            {
                TxtRefreshStatus.Text = string.Empty;
                return;
            }

            var secondsAgo = (int)Math.Floor((DateTime.Now - _lastRefreshedAtLocal).TotalSeconds);
            string when = secondsAgo < 5 ? "только что" :
                          secondsAgo < 60 ? $"{secondsAgo} сек назад" :
                          $"{_lastRefreshedAtLocal:HH:mm}";

            string autoPart;
            if (_autoRefreshTimer == null)
            {
                autoPart = "";
            }
            else if (AutoRefreshInterval.TotalHours >= 1)
            {
                // Для часовых интервалов обратный отсчёт в секундах бессмысленен.
                autoPart = $" · авто каждые {AutoRefreshInterval.TotalHours:0} ч";
            }
            else if (AutoRefreshInterval.TotalMinutes >= 1)
            {
                // Минутные интервалы: отсчёт в секундах шумит — округляем до минут.
                int nextTickMin = Math.Max(1, (int)Math.Ceiling(AutoRefreshInterval.TotalMinutes - secondsAgo / 60.0));
                autoPart = $" · авто через {nextTickMin} мин";
            }
            else
            {
                int nextTickSec = Math.Max(1, (int)Math.Ceiling(AutoRefreshInterval.TotalSeconds - secondsAgo));
                autoPart = $" · авто через {nextTickSec} сек";
            }

            TxtRefreshStatus.Text = $"обновлено {when}{autoPart}";
        }

        private static async Task<LatestManifestResult> FetchLatestVersionAsync()
        {
            try
            {
                // Диагностическая версия: даже при сбое известно, ПОЧЕМУ
                // (таймаут/HTTP-код/сеть) — причина уходит в шапку панели.
                var fetch = await UpdateManifestClient.FetchManifestDiagnosticsAsync().ConfigureAwait(false);
                var manifest = fetch.Manifest;
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Latest))
                    return new LatestManifestResult(null, null, fetch.Error);

                var ver = Version.TryParse(manifest.Latest, out var v) ? v : null;
                var release = manifest.Releases?.FirstOrDefault(r => r.Version == manifest.Latest);
                return new LatestManifestResult(ver, release?.Url, null);
            }
            catch (Exception)
            {
                return new LatestManifestResult(null, null, null);
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAsync(quiet: false, isInitial: false).ConfigureAwait(true);
        }

        /// <summary>
        /// «Очистить дубли»: удаляет из gist лишние файлы устройств — несколько
        /// записей одного ПК (обычная версия + dev) и легаси-файлы. Подтверждение
        /// перед удалением, тост с результатом, после — обновление панели.
        /// </summary>
        private async void BtnCleanupDuplicates_Click(object sender, RoutedEventArgs e)
        {
            if (!OfficeReportService.IsConfigured)
            {
                ToastService.ShowToast("Хранилище отчётов не настроено — очистка недоступна.", ToastType.Warning);
                return;
            }

            bool confirmed = DialogService.ShowConfirm(
                "Удалить из хранилища отчётов лишние файлы устройств?\n\n" +
                "Останутся только новейшие записи каждого компьютера офиса " +
                "(убираются дубли от запуска обычной и dev-версии на одном ПК).",
                "Очистить дубли",
                Window.GetWindow(this));
            if (!confirmed) return;

            BtnCleanupDuplicates.IsEnabled = false;
            try
            {
                int deleted = await OfficeReportService.CleanupDuplicatesAsync().ConfigureAwait(true);
                if (deleted < 0)
                {
                    ToastService.ShowToast("Не удалось очистить дубли — нет связи с GitHub.", ToastType.Error);
                }
                else if (deleted == 0)
                {
                    ToastService.ShowToast("Дублей не найдено — по одному файлу на устройство.", ToastType.Success);
                }
                else
                {
                    int m100 = deleted % 100, m10 = deleted % 10;
                    string noun = (m100 >= 11 && m100 <= 14) || m10 == 0 || (m10 >= 5 && m10 <= 9)
                        ? "файлов"
                        : m10 == 1 ? "файл" : "файла";
                    ToastService.ShowToast($"Удалено дублей: {deleted} {noun}.", ToastType.Success);
                }

                // Панель показывает актуальное состояние gist после очистки.
                await RefreshAsync(quiet: false, isInitial: false).ConfigureAwait(true);
            }
            finally
            {
                BtnCleanupDuplicates.IsEnabled = true;
            }
        }

        /// <summary>
        /// Клик на любую карточку офиса в «Обновления»: если это устаревший ряд —
        /// копируем напоминание и показываем тост. Подсказка «Кликните…»
        /// появляется только для таких карточек, так что логика тут безопасная.
        /// </summary>
        private void OfficesList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Поднимаемся по визуальному дереву от OriginalSource до карточки-ItemControl-Item.
            var dep = e.OriginalSource as DependencyObject;
            ContentPresenter? presenter = null;
            while (dep != null)
            {
                if (dep is ContentPresenter cp && OfficesList.ItemContainerGenerator.IndexFromContainer(cp) >= 0)
                {
                    presenter = cp;
                    break;
                }
                dep = VisualTreeHelper.GetParent(dep) ?? LogicalTreeHelper.GetParent(dep);
            }
            if (presenter == null) return;

            var row = presenter.Content as OfficeStatusRow;
            if (row == null || row.Status != OfficeStatus.Outdated) return;

            e.Handled = true;
            CopyReminder(row);
        }

        /// <summary>
        /// Копирует в буфер обмена напоминание об обновлении для конкретного офиса
        /// и показывает тост «Напоминание скопировано».
        /// </summary>
        private void CopyReminder(OfficeStatusRow row)
        {
            string version = _latestVersion != null ? _latestVersion.ToString() : "актуальную";
            string url = _latestDownloadUrl ?? "(ссылка недоступна — см. последнюю версию в разделе «Обновления»)";

            // В офисе может быть несколько устройств — напоминание адресуем
            // конкретному устаревшему ПК (его версия из чипа устройства).
            var outdatedDevice = row.Devices.FirstOrDefault(d => d.Status == OfficeStatus.Outdated);
            string currentLine = outdatedDevice != null
                ? $"На устройстве «{outdatedDevice.DeviceLabel}» установлена v{outdatedDevice.Version}.\n"
                : $"Текущая версия у вас: v{row.Version}.\n";

            string text =
                $"Здравствуйте! 👋\n\n" +
                $"В программе «A.R.C. Frame» доступна новая версия v{version}.\n" +
                $"Скачайте: {url}\n\n" +
                currentLine +
                $"После обновления программа сама отчитается — спасибо!";

            try
            {
                Clipboard.SetText(text);
                ToastService.ShowToast(
                    "Напоминание скопировано",
                    $"Версия v{version} для «{row.LocationName}». Можно вставить в чат/мессенджер.",
                    ToastType.Success,
                    durationMs: 4000);
            }
            catch (Exception ex)
            {
                // Clipboard на Windows иногда бросает — фоллбэк через Toast.
                ToastService.ShowToast(
                    "Не удалось скопировать",
                    ex.Message,
                    ToastType.Error,
                    durationMs: 4000);
            }
        }

        /// <summary>
        /// Фоновое автообновление (периодическая отправка отчёта + свежие данные),
        /// вызывается планировщиком OfficeReportScheduler, когда панель открыта.
        /// </summary>
        public Task RefreshInBackgroundAsync() => RefreshAsync(quiet: true, isInitial: false);

        /// <summary>
        /// Тихая автоочистка устаревших дублей gist (>24 ч без отчёта —
        /// <see cref="OfficeReportService.StaleDuplicateAfter"/>). Best-effort:
        /// любая ошибка — только строка в Debug, панель не страдает.
        /// </summary>
        private static async Task CleanupStaleDuplicatesQuietlyAsync()
        {
            try
            {
                await OfficeReportService.CleanupStaleDuplicatesAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AdminPanel] stale-duplicate cleanup failed: {ex.Message}");
            }
        }

        /// <summary>Результат получения последней версии с GitHub (версия + download URL для напоминания + причина сбоя).</summary>
        private sealed record LatestManifestResult(Version? Version, string? Url, string? FetchError);
    }
}
