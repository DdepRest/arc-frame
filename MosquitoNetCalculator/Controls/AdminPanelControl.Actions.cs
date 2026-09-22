using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// Частичный класс админ-панели: БЫСТРЫЕ ДЕЙСТВИЯ —
    /// «Отправить напоминание» (одно сообщение для всех устаревших офисов),
    /// режим «Отвязать выбранные» (чекбоксы на устройствах → подтверждение →
    /// удаление файлов отчётов из gist одним PATCH) и обновление состояния
    /// сводного прогресса.
    /// </summary>
    public partial class AdminPanelControl
    {
        /// <summary>Активен ли режим ручной отвязки (чекбоксы на чипах устройств).</summary>
        private bool _isUnbindMode;

        /// <summary>
        /// Недавно отвязанные в этой сессии (deviceId → подпись чипа). После удаления
        /// файла gist НЕ НЕСЁТ о устройстве никаких данных — «отвязали 5 минут назад»
        /// от «ПК выключен неделю» иначе не отличить. Маркер чистится, когда устройство
        /// снова появляется в отчётах (ReplaceRows → PruneJustUnbound).
        /// </summary>
        private readonly Dictionary<string, string> _justUnbound = new();

        /// <summary>Запомнить отвязанные устройства сессии и показать маркер.</summary>
        internal void RecordJustUnbound(IReadOnlyCollection<string> deviceIds)
        {
            foreach (var id in deviceIds)
            {
                var device = Rows.SelectMany(r => r.Devices)
                    .FirstOrDefault(d => d.DeviceId == id);
                if (device != null)
                    _justUnbound[id] = device.DeviceLabel;
            }
            UpdateJustUnboundText();
        }

        /// <summary>Убрать устройства, ВЕРНУВШИЕСЯ с отчётом, и обновить маркер.
        /// Отвязанное устройство после удаления файла в отчётах ОТСУТСТВУЕТ —
        /// отсутствие в списке это «ещё не вернулся», не «вернулся». Правило
        /// «кто считается вернувшимся» — <see cref="Services.AdminPanelLogic.JustUnboundReturned"/>;
        /// контрол только применяет результат к своему состоянию.</summary>
        internal void PruneJustUnbound()
        {
            if (_justUnbound.Count == 0) return;

            var reporting = Rows.SelectMany(r => r.Devices).Select(d => d.DeviceId).ToHashSet();
            var returned = AdminPanelLogic.JustUnboundReturned(_justUnbound.Keys, reporting);
            foreach (var id in returned)
                _justUnbound.Remove(id);
            UpdateJustUnboundText();
        }

        /// <summary>Текст маркера: перечислить подписи; скрыт, когда список пуст.
        /// Фраза — <see cref="Services.AdminPanelLogic.JustUnboundText"/>.</summary>
        private void UpdateJustUnboundText()
        {
            string? text = AdminPanelLogic.JustUnboundText(_justUnbound.Values.ToList());
            if (text == null)
                TxtJustUnbound.Visibility = Visibility.Collapsed;
            else
            {
                TxtJustUnbound.Text = text;
                TxtJustUnbound.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Отмеченные для отвязки устройства: (deviceId → имя файла отчёта).
        /// Источник истины для отметки — флаг <see cref="OfficeDeviceRow.IsChecked"/>
        /// на модели (переживает перерисовку виртуализированного списка); словарь —
        /// производное представление (deviceId → файл), собирается на лету.
        /// </summary>
        private Dictionary<string, string> SelectedUnbindFiles => Rows
            .SelectMany(r => r.Devices)
            .Where(d => d.IsChecked)
            .ToDictionary(d => d.DeviceId, d => OfficeReportService.ReportFileName(
                Rows.First(r => r.Devices.Contains(d)).Prefix, d.DeviceId));

        /// <summary>
        /// «Отправить напоминание»: собирает ОДНО сообщение по всем устаревшим
        /// офисам и копирует в буфер. Кнопка дизейблится, когда устаревших нет
        /// (см. UpdateQuickActionsState).
        /// </summary>
        private void BtnRemindAll_Click(object sender, RoutedEventArgs e)
        {
            var outdated = Rows.Where(r => r.Status == OfficeStatus.Outdated).ToList();
            if (outdated.Count == 0)
            {
                ToastService.ShowToast("Все офисы в актуальной версии — напоминание не нужно.", ToastType.Success);
                return;
            }

            CopyReminders(outdated);
        }

        /// <summary>
        /// Кнопка «Отвязать выбранные»: 1-й клик — вход в режим (чекбоксы на
        /// устройствах), 2-й клик — отвязать отмеченные (с подтверждением),
        /// повторный клик без отметок — выход из режима.
        /// </summary>
        private async void BtnUnbindMode_Click(object sender, RoutedEventArgs e)
        {
            if (!_isUnbindMode)
            {
                EnterUnbindMode();
                return;
            }

            if (SelectedUnbindFiles.Count == 0)
            {
                ExitUnbindMode();
                return;
            }

            await UnbindSelectedAsync();
        }

        private void EnterUnbindMode()
        {
            _isUnbindMode = true;
            TxtUnbindModeLabel.Text = "Отвязать отмеченные";
            UnbindModeBanner.Visibility = Visibility.Visible;
            UpdateUnbindBannerText();
            // Чекбоксы на чипах устройств привязаны к ItemsControl.Tag
            // («Unbind») — DP-уведомление обновляет их без INotifyPropertyChanged.
            OfficesList.Tag = "Unbind";
        }

        private void ExitUnbindMode()
        {
            _isUnbindMode = false;
            TxtUnbindModeLabel.Text = "Отвязать выбранные";
            UnbindModeBanner.Visibility = Visibility.Collapsed;
            OfficesList.Tag = null;
            GroupedRows?.Refresh();
        }

        /// <summary>Чекбокс на чипе устройства: запоминаем/забываем имя файла отчёта.</summary>
        /// <summary>Клик по чекбоксу устройства: отметка живёт НА МОДЕЛИ
        /// (<see cref="OfficeDeviceRow.IsChecked"/>) — контейнер при любом
        /// переиспользовании перечитывает её через биндинг. Здесь — только
        /// обновление счётчика в плашке.</summary>
        private void DeviceUnbindCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isUnbindMode) return;
            UpdateUnbindBannerText();
        }

        /// <summary>Текст плашки режима отвязки: сколько устройств отмечено.</summary>
        private void UpdateUnbindBannerText()
        {
            int count = SelectedUnbindFiles.Count;
            TxtUnbindBanner.Text = count == 0
                ? "Отметьте устройства для отвязки"
                : $"Отмечено устройств: {count}";
        }

        /// <summary>
        /// Отвязывает отмеченные устройства: подтверждение → удаление их файлов
        /// отчётов из gist одним PATCH → тост + обновление панели. Отвязанное
        /// устройство само вернётся своим следующим отчётом (старт программы /
        /// раз в 2 ч) — уже под актуальным офисом (атомарный переезд).
        /// </summary>
        private async Task UnbindSelectedAsync()
        {
            var fileNames = SelectedUnbindFiles.Values.ToList();
            bool confirmed = DialogService.ShowConfirmDestructive(
                $"Отвязать устройства ({fileNames.Count})? Из хранилища отчётов будут удалены их записи.\n\n" +
                "Устройство снова появится в панели после своего следующего отчёта — " +
                "уже под актуальным офисом этого компьютера.",
                "Отвязать",
                "Отвязать выбранные",
                Window.GetWindow(this));
            if (!confirmed) return;

            BtnUnbindMode.IsEnabled = false;
            try
            {
                // Подписи запоминаем ДО рефреша: после удаления файлов строки
                // устройств в панели уже не будет.
                var justUnboundIds = SelectedUnbindFiles.Keys.ToList();
                int deleted = await OfficeReportService.DeleteReportFilesAsync(fileNames).ConfigureAwait(true);
                if (deleted < 0)
                {
                    ToastService.ShowToast("Не удалось отвязать устройства — нет связи с хранилищем данных.", ToastType.Error);
                }
                else
                {
                    ExitUnbindMode();
                    RecordJustUnbound(justUnboundIds);
                    ToastService.ShowToast(
                        deleted == 0 ? "Устройства уже отвязаны." : $"Отвязано устройств: {deleted}.",
                        "Они снова появятся в панели после своего следующего отчёта.",
                        ToastType.Success);
                }

                await RefreshAsync(quiet: false, isInitial: false).ConfigureAwait(true);
            }
            finally
            {
                BtnUnbindMode.IsEnabled = true;
            }
        }

        /// <summary>
        /// Собирает ОДНО сообщение-напоминание по списку устаревших офисов
        /// и копирует в буфер. Сбор текста —
        /// <see cref="Services.AdminPanelLogic.BulkReminderText"/>; буфер+тост —
        /// общий хелпер <see cref="CopyReminderTextToClipboard"/>.
        /// </summary>
        private void CopyReminders(IReadOnlyList<OfficeStatusRow> outdatedRows)
        {
            var devices = outdatedRows
                .SelectMany(r => r.Devices.Where(d => d.Status == OfficeStatus.Outdated)
                    .Select(d => (r.LocationName, d.DeviceLabel, d.Version)))
                .ToList();
            var fallback = outdatedRows.Select(r => $"• {r.LocationName} — установлена v{r.Version}").ToList();

            string text = AdminPanelLogic.BulkReminderText(
                devices, fallback, _latestVersion,
                _latestDownloadUrl ?? AdminPanelLogic.FallbackDownloadUrl);
            CopyReminderTextToClipboard(text, $"Офисов в списке: {outdatedRows.Count}. Можно вставить в чат/мессенджер.");
        }

        /// <summary>
        /// Состояние быстрых действий после обновления данных:
        /// «Напоминание» доступно, только если есть устаревшие офисы;
        /// если режим отвязки активен — выходим из него (список устройств мог
        /// измениться) и снимаем отметки.
        /// </summary>
        internal void UpdateQuickActionsState()
        {
            bool hasOutdated = Rows.Any(r => r.Status == OfficeStatus.Outdated);
            BtnRemindAll.IsEnabled = hasOutdated;
            BtnUnbindMode.IsEnabled = Rows.Count > 0;
            if (_isUnbindMode)
                ExitUnbindMode();
        }

        /// <summary>
        /// Сводный прогресс обновлений под шапкой карточки (доля актуальных
        /// устройств по всем офисам). Ширина заливки — доля от реальной ширины.
        /// </summary>
        internal void UpdateSummaryProgress(int upToDateDevices, int knownDevices)
        {
            _lastUpToDate = upToDateDevices;
            _lastKnown = knownDevices;

            if (knownDevices <= 0)
            {
                ProgressSummaryHost.Visibility = Visibility.Collapsed;
                return;
            }

            ProgressSummaryHost.Visibility = Visibility.Visible;
            // Ширина задаётся на Loaded-приоритете: к этому моменту Grid уже измерен.
            ProgressSummaryFill.Dispatcher.BeginInvoke(new Action(() =>
            {
                ProgressSummaryFill.Width = ProgressSummaryHost.ActualWidth * upToDateDevices / knownDevices;
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>Последние значения сводки для пересчёта при resize.</summary>
        private int _lastUpToDate;
        private int _lastKnown;

        /// <summary>Обработчик размера сводной карточки: пересчитать заливку прогресса.</summary>
        private void ProgressSummaryHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _lastKnown = Math.Max(1, _lastKnown);
            ProgressSummaryFill.Width = e.NewSize.Width * _lastUpToDate / _lastKnown;
        }
    }
}
