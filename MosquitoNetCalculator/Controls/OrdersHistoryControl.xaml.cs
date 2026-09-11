using System;
using System.Collections.Generic;
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

        /// <summary>True when orders exist but the search filter matches none.
        /// Drives the «ничего не найдено» state via BoolToVis; mutually exclusive
        /// with <see cref="IsEmpty"/> (no orders at all).</summary>
        public bool NoSearchResults
        {
            get => _noSearchResults;
            set
            {
                if (_noSearchResults != value)
                {
                    _noSearchResults = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NoSearchResults)));
                }
            }
        }
        private bool _noSearchResults;

        public event PropertyChangedEventHandler? PropertyChanged;

        public OrdersHistoryControl()
        {
            InitializeComponent();
            // v3.50.1 (prototype parity): uppercase column captions, same as the
            // order grid. Assigned in code — see helper docs.
            Converters.UppercaseHeaderTemplate.Apply(OrdersList);
            // v3.50: status filter source = the same OrderStatuses.All the orders
            // themselves use; first entry is the neutral "all" marker.
            var statusItems = new List<string> { "Все статусы" };
            statusItems.AddRange(Models.OrderStatuses.All);
            CmbStatusFilter.ItemsSource = statusItems;
            CmbStatusFilter.SelectedIndex = 0;
        }

        // ── v3.50: status/date filters (view-only, prototype .dr-tools) ──
        // Both re-run the SAME CollectionView filter path as the text search;
        // the underlying collection is never modified.

        private void StatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => ApplyCombinedFilter();

        private void DateFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => ApplyCombinedFilter();

        /// <summary>Clears both filters (called from the search-clear button too).</summary>
        internal void ResetFilters()
        {
            CmbStatusFilter.SelectedIndex = 0;
            DpDateFilter.SelectedDate = null;
            ApplyCombinedFilter();
        }

        /// <summary>True when at least one auxiliary filter is active.</summary>
        private bool HasAuxFilters
            => CmbStatusFilter.SelectedIndex > 0 || DpDateFilter.SelectedDate != null;

        /// <summary>
        /// Re-applies the current search/status/date criteria after the grid's
        /// ItemsSource has been replaced (for example after refresh/import).
        /// The filter remains owned by this control; callers only request a
        /// reapplication and never manipulate the ICollectionView directly.
        /// </summary>
        internal void ReapplyFilters()
        {
            ApplyCombinedFilter();
        }

        private void ApplyCombinedFilter()
        {
            var view = CollectionViewSource.GetDefaultView(OrdersList.ItemsSource);
            if (view == null) return;

            string text = TxtSearchOrders.Text.Trim();
            string? status = CmbStatusFilter.SelectedIndex > 0
                ? CmbStatusFilter.SelectedItem?.ToString() : null;
            DateTime? date = DpDateFilter.SelectedDate;

            bool hasText = !string.IsNullOrEmpty(text);
            if (!hasText && status == null && date == null)
            {
                view.Filter = null;
                NoSearchResults = false;
                return;
            }

            view.Filter = item =>
            {
                if (item is not OrderData order) return true;
                if (hasText
                    && !((order.ContractNumber?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (order.ClientAddress?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (order.ClientPhone?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)))
                    return false;
                if (status != null && order.Status != status) return false;
                if (date != null && order.ContractDate.Date != date.Value.Date) return false;
                return true;
            };

            view.Refresh();
            NoSearchResults = !IsEmpty && view.IsEmpty;
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
            BtnClearOrdersSearch.Visibility = string.IsNullOrEmpty(TxtSearchOrders.Text) && !HasAuxFilters
                ? Visibility.Collapsed : Visibility.Visible;
            UpdateSearchPlaceholder();
            ApplyCombinedFilter();   // v3.50: one combined filter for text+status+date
        }

        private void TxtSearchOrders_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
            => UpdateSearchPlaceholder();

        private void TxtSearchOrders_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
            => UpdateSearchPlaceholder();

        /// <summary>Shows the ghost hint only while the field is empty AND unfocused
        /// (same rule as the AI-assistant input), so the caret never sits on the hint.</summary>
        private void UpdateSearchPlaceholder()
        {
            OrdersSearchPlaceholder.Visibility =
                string.IsNullOrEmpty(TxtSearchOrders.Text) && !TxtSearchOrders.IsKeyboardFocusWithin
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnClearOrdersSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtSearchOrders.Text = string.Empty;
            ResetFilters();   // v3.50: clears status/date too — one button, full reset
            TxtSearchOrders.Focus();
        }
    }
}
