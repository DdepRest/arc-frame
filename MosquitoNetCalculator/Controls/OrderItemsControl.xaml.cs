using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        /// Populates the EmptyState product chips with the given product names.
        /// Each chip is a clickable Border that triggers
        /// <see cref="MainWindow.SelectProductFromChip"/> when clicked.
        /// </summary>
        internal void PopulateProductChips(List<string> productNames)
        {
            ProductChipsList.ItemsSource = productNames;
            ProductChipsList.PreviewMouseLeftButtonDown -= ProductChip_Click;
            ProductChipsList.PreviewMouseLeftButtonDown += ProductChip_Click;
        }

        private void ProductChip_Click(object sender, MouseButtonEventArgs e)
        {
            // Find the ContentPresenter ancestor — its Content is the product name string
            // (ItemsSource is List<string>, so ContentPresenter.Content = string).
            // More robust than walking to a TextBlock, which breaks if template changes.
            var source = e.OriginalSource as DependencyObject;
            while (source != null && source is not ContentPresenter)
                source = VisualTreeHelper.GetParent(source);
            if (source is ContentPresenter cp && cp.Content is string productName)
                TryForwardToMain(nameof(ProductChip_Click), mw => mw.SelectProductFromChip(productName));
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

        private void AnwisModePill_PreviewLeftClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            TryForwardToMain(nameof(AnwisModePill_PreviewLeftClick), mw => mw.AnwisModePillLeftClick(sender, e));

        private void AnwisModePill_PreviewRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            TryForwardToMain(nameof(AnwisModePill_PreviewRightClick), mw => mw.AnwisModePillRightClick(sender, e));

        /// <summary>
        /// v3.43.5: двойной клик по строке «Откос» открывает панель редактирования откоса.
        /// </summary>
        private void OrderGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            while (source != null && source is not DataGridRow)
                source = VisualTreeHelper.GetParent(source);

            if (source is DataGridRow row && row.DataContext is OrderItem item && item.IsSlope)
            {
                TryForwardToMain(nameof(OrderGrid_MouseDoubleClick), mw => mw.EditSlopeItem(item));
            }
        }

        /// <summary>
        /// Selects the entire content of a DataGrid editing TextBox when it gains focus.
        /// Implements the standard WPF idiom for "click into a cell → typing replaces value".
        ///
        /// GOTCHAS#14: previous implementation deferred SelectAll via
        /// <c>Dispatcher.BeginInvoke</c>. The deferred call lost the race with the first
        /// keystroke: by the time SelectAll executed, the caret stayed where the click had
        /// placed it and typed characters were <em>appended</em> (e.g. existing "30" + typed
        /// "1200" → "301200"). Synchronous SelectAll runs in the same dispatch frame as the
        /// click that positioned the caret, so it overrides the caret position and selects all.
        ///
        /// Trade-offs (kept intentionally simple):
        /// <list type="bullet">
        ///   <item>Touch case: if the first interaction is a tap, both <c>PreviewMouseLeftButtonDown</c>
        ///         and <c>GotFocus</c> fire → caret placement vs selection follow the same synchronous
        ///         path → behaviour matches click.</item>
        ///   <item>Keyboard navigation (Tab/F2): when focus arrives via keyboard (no click yet),
        ///         we still want all text selected so the first keystroke replaces — same desired UX.</item>
        /// </list>
        /// A more elaborate <c>PreviewMouseLeftButtonDown</c> + caret-cancel pattern is possible,
        /// but not needed: synchronous SelectAll here is sufficient on every input path tested.
        /// </summary>
        /// <remarks>
        /// Scope of the rule: synchronous SelectAll is required specifically for <c>GotFocus</c>
        /// on editable DataGrid-TextBoxes where the next event is almost always a keystroke.
        /// Other call sites (e.g. programmatic focus hops, async contextual menus) may still use
        /// <c>Dispatcher.BeginInvoke</c> legitimately. Don't generalise this fix into "BeginInvoke
        /// is always wrong" — that's not what GOTCHAS#14 says.
        /// A regression test in <c>DataGridBindingsTests.SelectAll_OnFocus_HasNoBeginInvoke</c>
        /// reflects on the method body to catch re-introduction of the deferred pattern.
        /// </remarks>
        private void SelectAll_OnFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.SelectAll();
        }
    }
}
