using System;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MosquitoNetCalculator.Controls
{
    public partial class ActionBarControl : UserControl
    {
        public Border CardBarBorder => CardActionBar;

        public ActionBarControl()
        {
            InitializeComponent();
        }

        private bool TryGetMainWindow(string handlerName, [NotNullWhen(true)] out MainWindow? mw)
        {
            if (DataContext is MainWindow window)
            {
                mw = window;
                return true;
            }
            System.Diagnostics.Trace.WriteLine($"[ActionBarControl] DataContext is not MainWindow ({DataContext?.GetType().Name ?? "null"}), handler '{handlerName}' skipped.");
            mw = null;
            return false;
        }

        private void BtnPrintKp_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(BtnPrintKp_Click), out var mw)) return;
            // Navigate to Print tab — document is built and preview shown in PrintOverlay
            mw.ShowPrintOverlay();
        }

        internal void BtnSaveOrder_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(BtnSaveOrder_Click), out var mw)) return;
            var allItems = mw.OrderItems.Where(i => !string.IsNullOrEmpty(i.Name) && i.Total > 0).ToList();
            if (allItems.Count == 0)
            {
                ToastService.ShowToast("Добавьте хотя бы одну позицию перед сохранением.", ToastType.Warning);
                return;
            }

            var activeItems = allItems.Where(i => i.IsActive).ToList();
            var orderData = new OrderData
            {
                Id = mw.CurrentOrderId,
                ContractNumber = mw.ClientInfo.ContractNumber,
                ContractDate = mw.ClientInfo.ContractDate,
                ClientName = mw.ClientInfo.ClientName,
                ClientPhone = mw.ClientInfo.ClientPhone,
                ClientAddress = mw.ClientInfo.ClientAddress,
                Notes = mw.ClientInfo.Notes,
                HasAdditionalKp = mw.ClientInfo.HasAdditionalKp,
                AdditionalKpNumber = mw.ClientInfo.AdditionalKps.FirstOrDefault()?.Number ?? "",
                AdditionalKpAmount = mw.ClientInfo.AdditionalKpsTotal,
                AdditionalKps = mw.ClientInfo.AdditionalKps.Select(kp => new AdditionalKpItem
                {
                    Number = kp.Number,
                    Amount = kp.Amount,
                    IsActive = kp.IsActive
                }).ToList(),
                Status = mw.Sidebar.CmbOrderStatus.SelectedItem?.ToString() ?? OrderStatuses.All[0],
                TotalAmount = activeItems.Sum(i => i.TotalWithDeduction) + mw.ClientInfo.AdditionalKpsTotal,
                Items = allItems.Select(i => i.ToOrderItemData()).ToList()
            };

            mw.OrdersVM.SaveOrder(orderData);
            mw.IsNewOrder = false;
            mw.UpdateCurrentOrderInfo();
            mw.RefreshOrdersList();
            mw.MarkClean();
            mw.UndoRedo.Clear();

            ToastService.ShowToast($"Заказ {orderData.ContractNumber} сохранён!", ToastType.Success);
            if (!mw.SuppressPrefixSave)
                AppSettingsService.SaveContractPrefix(mw.Sidebar.TxtPrefix.Text);
        }

        private void BtnNewOrder_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(BtnNewOrder_Click), out var mw)) return;

            // v3.45.0 — confirm only when there are actual unsaved changes
            // (StatusDirtyIndicator is Visible). Previously checked
            // validItems.Count > 0 which incorrectly prompted users with
            // already-saved orders to re-confirm.
            if (mw.StatusDirtyIndicator.Visibility != Visibility.Collapsed)
            {
                var result = DialogService.ShowSaveDiscardCancel(
                    "Есть несохранённые изменения. Сохранить заказ перед созданием нового?",
                    "Новый заказ",
                    mw);
                switch (result)
                {
                    case SaveDiscardCancelResult.Save:
                        BtnSaveOrder_Click(sender, e);
                        // v3.45.0 hotfix — если сохранение не удалось
                        // (нет валидных позиций), BtnSaveOrder_Click
                        // показывает warning-toast и возвращается БЕЗ
                        // MarkClean(). Если StatusDirtyIndicator всё ещё
                        // Visible — пользователь только что нажал «Да»,
                        // но данные не сохранены. Прерываем создание
                        // нового заказа, чтобы не потерять его данные.
                        if (mw.StatusDirtyIndicator.Visibility != Visibility.Collapsed)
                            return;
                        break;
                    case SaveDiscardCancelResult.Discard:
                        // proceed without saving
                        break;
                    case SaveDiscardCancelResult.Cancel:
                    default:
                        return; // user changed mind — abort
                }
            }

            mw.StartNewOrder();
            mw.UpdateEmptyState();
        }

        private void BtnSendToFactory_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(BtnSendToFactory_Click), out var mw)) return;
            var allItems = mw.OrderItems.Where(i => !string.IsNullOrEmpty(i.Name) && i.Total > 0).ToList();
            if (allItems.Count == 0)
            {
                ToastService.ShowToast("Добавьте хотя бы одну позицию.", ToastType.Warning);
                return;
            }

            string address = mw.ClientInfo.ClientAddress ?? "";
            var additionalKps = mw.ClientInfo.AdditionalKps
                .Where(kp => kp.IsActive)
                .ToList();

            var selectableItems = FactoryTextService.BuildSelectableItems(allItems, additionalKps);

            var window = new SendToFactoryWindow(address, selectableItems)
            {
                Owner = mw
            };
            window.ShowDialog();
        }

        private void BtnOrderInfo_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(BtnOrderInfo_Click), out var mw)) return;
            mw.ToggleSidebarOverlay();
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(BtnClearAll_Click), out var mw)) return;
            if (mw.OrderItems.Count == 0) return;

            // v3.45.0 — Save/Discard/Cancel dialog replaces simple Yes/No.
            // Save: сохранить заказ, затем очистить; Discard: очистить без
            // сохранения; Cancel: отмена. Если сохранение не удалось (например,
            // нет позиций), BtnSaveOrder_Click покажет warning-toast и сам
            // вернёт управление — очистка в этом случае НЕ произойдёт
            // (следующий case Discard не сработает, потому что switch уже
            // завершился). Это корректное поведение: данные не теряются.
            var result = DialogService.ShowSaveDiscardCancel(
                "Все позиции будут удалены из расчёта. Сохранить заказ перед очисткой?",
                "Очистить всё",
                mw);
            switch (result)
            {
                case SaveDiscardCancelResult.Save:
                    BtnSaveOrder_Click(sender, e);
                    // v3.45.0 hotfix — не очищаем, если сохранение не
                    // удалось (нет валидных позиций → MarkClean не
                    // вызван → StatusDirtyIndicator всё ещё Visible).
                    if (mw.StatusDirtyIndicator.Visibility != Visibility.Collapsed)
                        return;
                    break;
                case SaveDiscardCancelResult.Discard:
                    break;
                case SaveDiscardCancelResult.Cancel:
                default:
                    return;
            }

            mw.PushUndo();
            mw.CalcVM.UnsubscribeAll(mw.UpdateTotal);
            mw.CalcVM.ClearAll();
            mw.UpdateTotal();
            mw.UpdateEmptyState();
        }

    }
}
