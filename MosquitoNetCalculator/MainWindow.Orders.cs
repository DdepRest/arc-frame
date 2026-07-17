using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator
{
    public partial class MainWindow
    {
        // Lazy-init so MainWindow, which owns the VM, is always available
        // before the ImportExport service is asked to do anything.
        private OrderImportExportService? _importExport;
        private OrderImportExportService ImportExport =>
            _importExport ??= new OrderImportExportService(ViewModel.OrdersVM);

        // ─── Refresh ────────────────────────────────────────────

        internal void RefreshOrdersList()
        {
            var orders = ViewModel.OrdersVM.LoadAllOrders();

            // Grid-level work (autosize, sort restore, sort indicators)
            // delegated to the presenter. MainWindow only owns the
            // side-effects (count chip, nav badge).
            OrderGridPresenter.RefreshOrdersGrid(OrdersHistoryControl.OrdersGrid, orders);

            if (OrdersHistoryControl?.OrdersCount != null)
                OrdersHistoryControl.OrdersCount.Text = $"Заказов: {orders.Count}";
            OrdersHistoryControl?.SetOrdersCount(orders.Count);

            RefreshNavBadges();
        }

        // ─── Status change (modal dialog — sole UI surface, v3.45.0) ─
        //
        // v3.45.0 bug-fix: the legacy inline sub-menu "Изменить статус → <status>"
        // approach was deleted. The dynamically-populated sub-items often collapsed
        // in WPF's virtualised ContextMenu caching (user couldn't reliably click a
        // status), and the PrePhase-6 chromeless ChangeOrderStatusWindow had no XAML
        // entry-point (CtxStatus_Click was dead code until v3.45.0 restored the
        // Click binding on CtxStatusMenu in OrdersHistoryControl.xaml).
        //
        // Sole flow now: user right-clicks row → «Изменить статус...» →
        // CtxStatus_Click → MainWindow.ChangeSelectedOrderStatus() →
        // ChangeOrderStatusWindow.ShowDialog() → Saved + SelectedStatus.

        internal void BtnRefreshOrders_Click(object sender, RoutedEventArgs e)
        {
            RefreshOrdersList();
        }

        // ─── Export / Import (delegated to OrderImportExportService) ──

        internal void BtnExportOrders_Click(object sender, RoutedEventArgs e)
        {
            var allOrders = ViewModel.OrdersVM.LoadAllOrders();
            if (ImportExport.ExportAllOrders(allOrders, this))
                RefreshOrdersList();
        }

        internal void BtnImportOrders_Click(object sender, RoutedEventArgs e)
        {
            if (ImportExport.ImportOrders(this))
                RefreshOrdersList();
        }

        // ─── Double-click → open order ─────────────────────────────

        private void OrdersList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Header clicks already trigger a sort — don't also open the order.
            if (OrderGridPresenter.IsHeaderClick(e.OriginalSource as DependencyObject))
                return;
            OpenSelectedOrder();
        }

        // ─── Open order (complex flow — intentionally kept inline) ──
        //
        // Why: this method touches ClientInfo, Sidebar, ViewModel.CalcVM,
        // UndoRedo, QuickAddControl, and many MainWindow state flags
        // (_suppressContractNumberUpdate, SuppressPrefixSave, CurrentOrderId).
        // Extracting it into a service would require passing ~10 state
        // handles per call — an anti-pattern worse than the current
        // partial-class delegation.

        internal void OpenSelectedOrder()
        {
            if (OrdersHistoryControl.OrdersGrid.SelectedItem is not OrderData order) return;

            if (!DialogService.ShowConfirm($"Открыть заказ \u00AB{order.ContractNumber}\u00BB \u2014 {order.ClientName}?\n\nТекущие данные будут заменены.", "Открыть заказ", this)) return;

            CurrentOrderId = order.Id;
            IsNewOrder = false;
            _suppressContractNumberUpdate = true;
            try
            {
                SuppressPrefixSave = true;

                ViewModel.UndoRedo.SuppressDirtyChanges(() =>
                {
                    ClientInfo.ClientName = order.ClientName;
                    ClientInfo.ClientPhone = order.ClientPhone;
                    ClientInfo.ClientAddress = order.ClientAddress;
                    ClientInfo.ContractNumber = order.ContractNumber;
                    ClientInfo.ContractDate = order.ContractDate;
                    ClientInfo.Notes = order.Notes ?? "";
                    ClientInfo.AdditionalKps.Clear();
                    if (order.AdditionalKps != null && order.AdditionalKps.Count > 0)
                    {
                        foreach (var kp in order.AdditionalKps)
                            ClientInfo.AdditionalKps.Add(new AdditionalKpItem { Number = kp.Number ?? "", Amount = kp.Amount, IsActive = kp.IsActive });
                        ClientInfo.HasAdditionalKp = true;
                    }
                    else if (order.HasAdditionalKp && !string.IsNullOrEmpty(order.AdditionalKpNumber))
                    {
                        ClientInfo.AdditionalKps.Add(new AdditionalKpItem
                        {
                            Number = order.AdditionalKpNumber ?? "",
                            Amount = order.AdditionalKpAmount
                        });
                        ClientInfo.HasAdditionalKp = true;
                    }
                    else
                    {
                        ClientInfo.HasAdditionalKp = false;
                    }

                    if (order.ContractNumber.Contains('-'))
                    {
                        var parts = order.ContractNumber.Split('-', 2);
                        Sidebar.TxtPrefix.Text = parts[0];
                    }
                    else
                    {
                        Sidebar.TxtPrefix.Text = string.Empty;
                    }

                    Sidebar.CmbOrderStatus.SelectedItem = order.Status;

                    QuickAddControl.ResetAnwisMode();

                    ViewModel.CalcVM.UnsubscribeAll(UpdateTotal);
                    ViewModel.CalcVM.LoadFromOrderData(order, RecalculateAndUpdateTotal);

                    UpdateTotal();
                    UpdateCurrentOrderInfo();
                    UpdateEmptyState();
                });
                MarkClean();
                ViewModel.UndoRedo.Clear();
                UpdateUndoRedoHint();

                NavigateToCalculation();
            }
            finally
            {
                _suppressContractNumberUpdate = false;
            }
        }

        // ─── Change status (XAML dialog — Phase 4 pattern) ──────────

        internal void ChangeSelectedOrderStatus()
        {
            if (OrdersHistoryControl.OrdersGrid.SelectedItem is not OrderData order) return;

            // Phase 6 (v3.45.0): inline XAML mini-dialog replaced by a
            // dedicated chromeless window using the Phase 4 pattern.
            // Strips ~110 lines of legacy code from this file.
            var dlg = new ChangeOrderStatusWindow(order, this);
            dlg.ShowDialog();

            if (!dlg.Saved || dlg.SelectedStatus == null || dlg.SelectedStatus == order.Status)
                return;

            order.Status = dlg.SelectedStatus;
            ViewModel.OrdersVM.SaveOrder(order);
            RefreshOrdersList();
            ToastService.ShowToast("Статус обновлён.", ToastType.Success);
        }

        // ─── Copy (delegated to OrderImportExportService.CopyOrder) ──

        internal void CopySelectedOrder()
        {
            if (OrdersHistoryControl.OrdersGrid.SelectedItem is not OrderData source) return;

            var newNumber = ImportExport.CopyOrder(source);
            if (newNumber == null) return;

            RefreshOrdersList();
            ToastService.ShowToast($"Заказ скопирован: {newNumber}", ToastType.Success);
        }

        internal void DeleteSelectedOrder()
        {
            if (OrdersHistoryControl.OrdersGrid.SelectedItem is not OrderData order) return;

            if (DialogService.ShowConfirm($"Удалить заказ \u00AB{order.ContractNumber}\u00BB \u2014 {order.ClientName}?\n\nЭто действие нельзя отменить.", "Удалить заказ", this))
            {
                ViewModel.OrdersVM.DeleteOrder(order.Id);
                RefreshOrdersList();
                ToastService.ShowToast("Заказ удалён.", ToastType.Info);
            }
        }

        internal void ExportSelectedOrder()
        {
            if (OrdersHistoryControl.OrdersGrid.SelectedItem is not OrderData order) return;
            ImportExport.ExportSingleOrder(order, this);
        }

        // ─── Sort indicator management (delegated to OrderGridPresenter) ──

        internal void OrdersList_Sorting(object sender, DataGridSortingEventArgs e)
        {
            // Indicator update is post-sort — Dispatcher yields so the
            // DataGrid finishes applying its sort before we touch headers.
            Dispatcher.BeginInvoke(new Action(() =>
                OrderGridPresenter.ApplySortIndicators(OrdersHistoryControl.OrdersGrid)),
                DispatcherPriority.Background);
        }

        internal void UpdateSortIndicatorsFromSortDescriptions()
        {
            OrderGridPresenter.ApplySortIndicators(OrdersHistoryControl.OrdersGrid);
        }
    }
}
