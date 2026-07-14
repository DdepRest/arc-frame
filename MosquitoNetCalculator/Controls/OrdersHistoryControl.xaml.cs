using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Controls
{
    public partial class OrdersHistoryControl : UserControl, INotifyPropertyChanged
    {
        public DataGrid OrdersGrid => OrdersList;
        public TextBlock OrdersCount => TxtOrdersCount;

        /// <summary>True when the loaded orders list is empty. Drives the
        /// empty-state placeholder Visibility via the BoolToVis converter.</summary>
        public bool IsEmpty
        {
            get => _isEmpty;
            set
            {
                if (_isEmpty != value)
                {
                    _isEmpty = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEmpty)));
                }
            }
        }
        private bool _isEmpty = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public OrdersHistoryControl()
        {
            InitializeComponent();
        }

        /// <summary>Called from <see cref="MainWindow.RefreshOrdersList"/> so the
        /// empty-state placeholder stays in sync with the loaded count.</summary>
        public void SetOrdersCount(int count)
        {
            IsEmpty = count == 0;
        }

        /// <summary>Forwards a UI event to the parent MainWindow via the inherited DataContext.
        /// Logs a diagnostic (Trace) if the DataContext is not a MainWindow — a future
        /// MainWindow refactor that renames or moves a method will surface immediately
        /// instead of silently doing nothing.</summary>
        private bool TryForwardToMain(string handlerName, Action<MainWindow> action)
        {
            if (DataContext is MainWindow mw)
            {
                action(mw);
                return true;
            }
            Trace.WriteLine($"[OrdersHistoryControl] DataContext is not MainWindow ({DataContext?.GetType().Name ?? "null"}), handler '{handlerName}' skipped.");
            return false;
        }

        private void BtnImportOrders_Click(object sender, RoutedEventArgs e) =>
            TryForwardToMain(nameof(BtnImportOrders_Click), mw => mw.BtnImportOrders_Click(sender, e));

        private void BtnExportOrders_Click(object sender, RoutedEventArgs e) =>
            TryForwardToMain(nameof(BtnExportOrders_Click), mw => mw.BtnExportOrders_Click(sender, e));

        private void BtnRefreshOrders_Click(object sender, RoutedEventArgs e) =>
            TryForwardToMain(nameof(BtnRefreshOrders_Click), mw => mw.BtnRefreshOrders_Click(sender, e));

        private void OrdersList_Sorting(object sender, DataGridSortingEventArgs e) =>
            TryForwardToMain(nameof(OrdersList_Sorting), mw => mw.OrdersList_Sorting(sender, e));

        private void CtxOpen_Click(object sender, RoutedEventArgs e) =>
            TryForwardToMain(nameof(CtxOpen_Click), mw => mw.OpenSelectedOrder());

        private void CtxStatus_Click(object sender, RoutedEventArgs e) =>
            TryForwardToMain(nameof(CtxStatus_Click), mw => mw.ChangeSelectedOrderStatus());

        private void CtxExport_Click(object sender, RoutedEventArgs e) =>
            TryForwardToMain(nameof(CtxExport_Click), mw => mw.ExportSelectedOrder());

        private void CtxCopy_Click(object sender, RoutedEventArgs e) =>
            TryForwardToMain(nameof(CtxCopy_Click), mw => mw.CopySelectedOrder());

        private void CtxDelete_Click(object sender, RoutedEventArgs e) =>
            TryForwardToMain(nameof(CtxDelete_Click), mw => mw.DeleteSelectedOrder());

        private void BtnGoToCalculation_Click(object sender, RoutedEventArgs e) =>
            TryForwardToMain(nameof(BtnGoToCalculation_Click), mw => mw.NavigateToCalculation());

        // ── Search / filter orders ──────────────────────────────────────
        private void TxtSearchOrders_TextChanged(object sender, TextChangedEventArgs e)
        {
            BtnClearOrdersSearch.Visibility = string.IsNullOrEmpty(TxtSearchOrders.Text)
                ? Visibility.Collapsed : Visibility.Visible;

            var view = CollectionViewSource.GetDefaultView(OrdersList.ItemsSource);
            if (view == null) return;
            string filter = TxtSearchOrders.Text.Trim();
            view.Filter = string.IsNullOrEmpty(filter)
                ? null
                : item =>
                {
                    if (item is OrderData order)
                        return (order.ContractNumber?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
                            || (order.ClientAddress?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
                            || (order.ClientPhone?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
                    return true;
                };
        }

        private void BtnClearOrdersSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtSearchOrders.Text = string.Empty;
            TxtSearchOrders.Focus();
        }
    }
}
