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

        // ─── v3.50.3 split-button «Печать КП» ─────────────────────────
        // Main segment = предпросмотр (ShowPrintOverlay); caret menu =
        // прямые действия БЕЗ предпросмотра. Every route reuses an EXISTING
        // pipeline (no second print route anywhere).

        // v3.50.3 split-button semantics (owner request):
        //   main segment → ShowPrintOverlay (предпросмотр)
        //   caret menu   → direct actions, NO preview:
        //     Печать без предпросмотра        → TriggerPrinterPrint
        //     «Чистая» + «В производство»     → TriggerPrinterPrintWithProductionCopy
        //     Сохранить в PDF                 → TriggerPdfExport (save dialog)
        // Every item reuses the PrintPreviewControl pipeline — no second route.

        private void BtnPrintPdf_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(BtnPrintPdf_Click), out var mw)) return;
            PrintMenuPopup.IsOpen = false;

            var validItems = mw.OrderItems.Where(i => !string.IsNullOrEmpty(i.Name) && i.IsActive && i.Total > 0).ToList();
            if (validItems.Count == 0)
            {
                ToastService.ShowToast("Добавьте хотя бы одну позицию.", ToastType.Warning);
                return;
            }

            // Build+show the overlay (existing path) offscreen-equivalent: build the
            // document exactly as ShowPrintOverlay does, then trigger the PDF save
            // through the control's own button handler. The overlay stays closed —
            // one-click export as the prototype describes.
            mw.ShowPrintOverlay();
            mw.PrintPreviewControl.TriggerPdfExport();
            mw.CloseAllOverlays();
        }

        private void BtnPrintCaret_Click(object sender, RoutedEventArgs e)
        {
            // v3.50.2 bugfix: the XAML previously had no Click wiring on this
            // caret segment — the variants menu could only be opened by pure
            // luck (keyboard focus), while a real mouse click did nothing.
            PrintMenuPopup.IsOpen = !PrintMenuPopup.IsOpen;
        }

        private void BtnPrintPreview_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(BtnPrintPreview_Click), out var mw)) return;
            PrintMenuPopup.IsOpen = false;
            mw.ShowPrintOverlay();
        }

        private void BtnPrinterDirect_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(BtnPrinterDirect_Click), out var mw)) return;
            PrintMenuPopup.IsOpen = false;

            var validItems = mw.OrderItems.Where(i => !string.IsNullOrEmpty(i.Name) && i.IsActive && i.Total > 0).ToList();
            if (validItems.Count == 0)
            {
                ToastService.ShowToast("Добавьте хотя бы одну позицию.", ToastType.Warning);
                return;
            }

            mw.ShowPrintOverlay();
            mw.PrintPreviewControl.TriggerPrinterPrint();
        }

        private void BtnPrinterProduction_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(BtnPrinterProduction_Click), out var mw)) return;
            PrintMenuPopup.IsOpen = false;

            var validItems = mw.OrderItems.Where(i => !string.IsNullOrEmpty(i.Name) && i.IsActive && i.Total > 0).ToList();
            if (validItems.Count == 0)
            {
                ToastService.ShowToast("Добавьте хотя бы одну позицию.", ToastType.Warning);
                return;
            }

            mw.ShowPrintOverlay();
            // Force the production copy ON for this attempt (customer set +
            // stamped «В ПРОИЗВОДСТВО» set), then reuse the normal print path.
            mw.PrintPreviewControl.TriggerPrinterPrintWithProductionCopy();
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
            var validItems = mw.OrderItems.Where(i => !string.IsNullOrEmpty(i.Name) && i.Total > 0).ToList();
            if (validItems.Count > 0)
            {
                if (!DialogService.ShowConfirm("У вас есть несохранённые данные. Создать новый заказ?", "Новый заказ", mw)) return;
            }

            mw.StartNewOrder();
            mw.UpdateEmptyState();
        }

        private void BtnTemplates_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(BtnTemplates_Click), out var mw)) return;

            // Шаблоны (замена «На завод», v3.54): быстрое добавление готового
            // набора позиций. Каталог — чистый OrderTemplateService, UI —
            // модальное окно. Добавление в заказ выполняет само окно после
            // подтверждения пользователя (атомарно, один Undo-шаг).
            var window = new TemplatesWindow(mw)
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

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(BtnUndo_Click), out var mw)) return;
            mw.Undo();
        }

        private void BtnRedo_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(BtnRedo_Click), out var mw)) return;
            mw.Redo();
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(BtnClearAll_Click), out var mw)) return;
            if (mw.OrderItems.Count == 0) return;

            if (DialogService.ShowConfirmDestructive("Очистить все позиции расчёта?", "Очистить", "Очистить всё", mw))
            {
                mw.PushUndo();
                mw.CalcVM.UnsubscribeAll(mw.UpdateTotal);
                mw.CalcVM.ClearAll();
                mw.UpdateTotal();
                mw.UpdateEmptyState();

                // v3.50: undo toast — PushUndo above makes the clear reversible
                // through Undo (the prototype's «Отменить» action).
                ToastService.ShowToast(
                    "Заказ очищен",
                    ToastType.Info,
                    "Отменить",
                    () => mw.Undo());
            }
        }

    }
}
