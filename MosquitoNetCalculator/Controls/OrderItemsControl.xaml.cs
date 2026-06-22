using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Controls
{
    public partial class OrderItemsControl : UserControl
    {
        public Border CardTableBorder => CardTable;
        public DataGrid Grid => OrderGrid;
        public Border Empty => EmptyState;

        public OrderItemsControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Forwards a UI event to the parent MainWindow via the inherited DataContext.
        /// Logs a diagnostic (Trace) if the DataContext is not a MainWindow — a future
        /// MainWindow refactor that renames or moves a method will surface immediately
        /// instead of silently doing nothing.
        /// </summary>
        private bool TryForwardToMain(string handlerName, Action<MainWindow> action)
        {
            if (DataContext is MainWindow mw)
            {
                action(mw);
                return true;
            }
            Trace.WriteLine($"[OrderItemsControl] DataContext is not MainWindow ({DataContext?.GetType().Name ?? "null"}), handler '{handlerName}' skipped.");
            return false;
        }

        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e) =>
            TryForwardToMain(nameof(BtnDeleteRow_Click), mw => mw.BtnDeleteRow_Click(sender, e));

        private void BtnToggleInstallation_Click(object sender, RoutedEventArgs e) =>
            TryForwardToMain(nameof(BtnToggleInstallation_Click), mw => mw.BtnToggleInstallation_Click(sender, e));

        private void AnwisModePill_PreviewRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            TryForwardToMain(nameof(AnwisModePill_PreviewRightClick), mw => mw.AnwisModePillRightClick(sender, e));

        private void SelectAll_OnFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.Dispatcher.BeginInvoke(() => tb.SelectAll());
        }
    }
}
