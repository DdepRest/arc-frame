using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator
{
    public partial class MainWindow
    {
        private DraftRecoveryService? _draftRecoveryService;
        private OrderBackupService? _orderBackupService;

        internal DraftRecoveryService DraftService => _draftRecoveryService ??= new DraftRecoveryService();
        internal OrderBackupService BackupService => _orderBackupService ??= new OrderBackupService();

        internal void InitializeRecoveryAndBackup()
        {
            // Wire banner events
            if (DraftBanner != null)
            {
                DraftBanner.RestoreRequested += (_, _) => RestorePendingDraft();
                DraftBanner.SelectiveRestoreRequested += (_, _) => RestoreSelectiveDraft();
                DraftBanner.PostponeRequested += (_, _) => PostponePendingDraft();
                DraftBanner.DiscardRequested += (_, _) => DiscardPendingDraft();
            }

            DraftService.DraftSaved += (s, time) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (TxtAutosaveStatus != null)
                    {
                        TxtAutosaveStatus.Text = $"🕒 Черновик сохранён: {time:HH:mm:ss}";
                        TxtAutosaveStatus.ToolTip = "Черновик сохранён в фоне. Нажмите для открытия восстановления.";
                    }
                });
            };

            // Run daily backup asynchronously on startup
            _ = BackupService.CreateDailyBackupIfNeededAsync();

            // Check if there is an unsaved pending draft from a previous session
            CheckAndShowDraftRecovery(silentIfNoDraft: true);
        }

        internal void CheckAndShowDraftRecovery(bool silentIfNoDraft = false)
        {
            if (DraftBanner == null) return;

            if (DraftService.HasPendingDraft())
            {
                var draft = DraftService.LoadDraft();
                if (draft != null)
                {
                    DraftBanner.DisplayDraft(draft);
                }
            }
            else if (!silentIfNoDraft)
            {
                ToastService.ShowToast("Несохранённых черновиков нет.", ToastType.Info);
            }
        }

        internal OrderData BuildCurrentOrderDraft()
        {
            var allItems = OrderItems?.Where(i => !string.IsNullOrEmpty(i.Name) && i.Total > 0).ToList() ?? new List<OrderItem>();
            var activeItems = allItems.Where(i => i.IsActive).ToList();

            return new OrderData
            {
                Id = CurrentOrderId,
                ContractNumber = ClientInfo.ContractNumber,
                ContractDate = ClientInfo.ContractDate,
                ClientName = ClientInfo.ClientPhone,
                ClientPhone = ClientInfo.ClientPhone,
                ClientAddress = ClientInfo.ClientAddress,
                Notes = ClientInfo.Notes,
                HasAdditionalKp = ClientInfo.HasAdditionalKp,
                AdditionalKpNumber = ClientInfo.AdditionalKps.FirstOrDefault()?.Number ?? "",
                AdditionalKpAmount = ClientInfo.AdditionalKpsTotal,
                AdditionalKps = ClientInfo.AdditionalKps.Select(kp => new AdditionalKpItem
                {
                    Number = kp.Number,
                    Amount = kp.Amount,
                    IsActive = kp.IsActive
                }).ToList(),
                Status = Sidebar?.CmbOrderStatus?.SelectedItem?.ToString() ?? OrderStatuses.All[0],
                TotalAmount = activeItems.Sum(i => i.TotalWithDeduction) + ClientInfo.AdditionalKpsTotal,
                Items = allItems.Select(i => i.ToOrderItemData()).ToList(),
                UpdatedAt = DateTime.Now
            };
        }

        /// <summary>
        /// Schedules a background debounce write of the current calculation state to draft.json.
        /// </summary>
        internal void NotifyDraftChanged()
        {
            try
            {
                var allItems = OrderItems?.Where(i => !string.IsNullOrEmpty(i.Name) && i.Total > 0).ToList() ?? new List<OrderItem>();
                bool hasClientInfo = !string.IsNullOrWhiteSpace(ClientInfo?.ClientPhone)
                    || !string.IsNullOrWhiteSpace(ClientInfo?.ClientAddress)
                    || !string.IsNullOrWhiteSpace(ClientInfo?.Notes);

                if (allItems.Count == 0 && !hasClientInfo)
                    return;

                var draft = BuildCurrentOrderDraft();
                if (TxtAutosaveStatus != null)
                    TxtAutosaveStatus.Text = "🕒 Сохранение…";
                DraftService.NotifyChanged(draft);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotifyDraftChanged] Error building draft: {ex.Message}");
            }
        }

        private void RestorePendingDraft()
        {
            var draft = DraftBanner?.CurrentDraft;
            if (draft == null) return;

            if (IsDirty && OrderItems.Any(i => !string.IsNullOrEmpty(i.Name) && i.Total > 0))
            {
                if (!DialogService.ShowConfirm(
                    "Текущие несохранённые данные будут заменены данными из черновика. Продолжить?",
                    "Восстановление черновика",
                    this))
                {
                    return;
                }
            }

            // Restore calculation and client info
            ViewModel.CalcVM.LoadFromOrderData(draft, RecalculateAndUpdateTotal);

            CurrentOrderId = draft.Id;
            ClientInfo.ContractNumber = draft.ContractNumber;
            ClientInfo.ContractDate = draft.ContractDate;
            // ClientName handled via phone
            ClientInfo.ClientPhone = draft.ClientPhone;
            ClientInfo.ClientAddress = draft.ClientAddress;
            ClientInfo.Notes = draft.Notes;
            ClientInfo.HasAdditionalKp = draft.HasAdditionalKp;
            ClientInfo.AdditionalKps.Clear();
            if (draft.AdditionalKps != null)
            {
                foreach (var kp in draft.AdditionalKps)
                {
                    ClientInfo.AdditionalKps.Add(new AdditionalKpItem
                    {
                        Number = kp.Number,
                        Amount = kp.Amount,
                        IsActive = kp.IsActive
                    });
                }
            }

            if (Sidebar?.CmbOrderStatus != null && !string.IsNullOrEmpty(draft.Status))
            {
                Sidebar.CmbOrderStatus.SelectedItem = draft.Status;
            }

            UpdateTotal();
            UpdateEmptyState();
            UpdateCurrentOrderInfo();

            DraftBanner?.HideBanner();
            ToastService.ShowToast("Черновик успешно восстановлен!", ToastType.Success);
        }

        private void RestoreSelectiveDraft()
        {
            var draft = DraftBanner?.CurrentDraft;
            if (draft == null || draft.Items == null || draft.Items.Count == 0)
            {
                ToastService.ShowToast("В черновике нет позиций для восстановления.", ToastType.Info);
                return;
            }

            var dlg = new Controls.DraftItemsPickerDialog(draft) { Owner = this };
            bool? dialogResult;
            using (DimmerScope())
            {
                dialogResult = dlg.ShowDialog();
            }
            if (dialogResult == true && dlg.SelectedItems.Count > 0)
            {
                foreach (var itemData in dlg.SelectedItems)
                {
                    var item = itemData.ToOrderItem();
                    item.Recalculate();
                    
                    OrderItems.Add(item);
                }

                UpdateTotal();
                UpdateEmptyState();
                MarkDirty();

                DraftBanner?.HideBanner();
                ToastService.ShowToast($"Восстановлено позиций: {dlg.SelectedItems.Count}", ToastType.Success);
            }
        }

        private void PostponePendingDraft()
        {
            DraftBanner?.HideBanner();
            if (TxtAutosaveStatus != null)
                TxtAutosaveStatus.Text = "🕒 Черновик отложен (нажмите для открытия)";
            ToastService.ShowToast("Черновик отложен. Вы можете открыть его в любое время.", ToastType.Info);
        }

        private void DiscardPendingDraft()
        {
            DraftService.DiscardDraft();
            DraftBanner?.HideBanner();
            if (TxtAutosaveStatus != null)
                TxtAutosaveStatus.Text = "🕒 Автосохранение включено";
            ToastService.ShowToast("Черновик удалён.", ToastType.Info);
        }
    }
}
