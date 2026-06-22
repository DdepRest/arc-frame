using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// Боковая панель «Дополнительное КП».
    /// Вынесена из MainWindow.xaml для уменьшения размера главного окна.
    /// Биндится к ClientInfo через DataContext (унаследованный от MainWindow).
    /// </summary>
    public partial class AdditionalKpsControl : UserControl
    {
        private bool _suppressTextChanged;
        private bool _hasPushedUndoForThisField;

        public AdditionalKpsControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Resolves the parent MainWindow from DataContext, logging a diagnostic if the
        /// DataContext is not a MainWindow. Returns false in that case so callers can
        /// bail out gracefully.
        /// </summary>
        private bool TryGetMainWindow(string handlerName, [NotNullWhen(true)] out MainWindow? mw)
        {
            if (DataContext is MainWindow window)
            {
                mw = window;
                return true;
            }
            System.Diagnostics.Trace.WriteLine($"[AdditionalKpsControl] DataContext is not MainWindow ({DataContext?.GetType().Name ?? "null"}), handler '{handlerName}' skipped.");
            mw = null;
            return false;
        }

        private void BtnAddAdditionalKp_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(BtnAddAdditionalKp_Click), out var mw)) return;
            mw.PushUndo();
            mw.ClientInfo.AdditionalKps.Add(new AdditionalKpItem());
        }

        private void BtnDeleteAdditionalKp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not AdditionalKpItem item) return;
            if (!TryGetMainWindow(nameof(BtnDeleteAdditionalKp_Click), out var mw)) return;
            mw.PushUndo();
            mw.ClientInfo.AdditionalKps.Remove(item);
        }

        private void AdditionalKpAmountTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb) return;
            DataObject.AddPastingHandler(tb, OnAdditionalKpAmountPaste);

            if (tb.DataContext is AdditionalKpItem item)
                tb.Text = MoneyFormatService.Format(item.Amount);
        }

        private void AdditionalKpAmountTextBox_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                DataObject.RemovePastingHandler(tb, OnAdditionalKpAmountPaste);
        }

        private void OnAdditionalKpAmountPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(DataFormats.Text))
            {
                var text = (string)e.DataObject.GetData(DataFormats.Text);
                if (sender is TextBox tb && tb.DataContext is AdditionalKpItem item &&
                    MoneyFormatService.TryParse(text, out double val))
                {
                    if (!_hasPushedUndoForThisField && TryGetMainWindow(nameof(OnAdditionalKpAmountPaste), out var mw))
                    {
                        mw.PushUndo();
                        _hasPushedUndoForThisField = true;
                    }

                    _suppressTextChanged = true;
                    try
                    {
                        item.Amount = val;
                        tb.Text = MoneyFormatService.Format(val);
                        tb.CaretIndex = tb.Text.Length;
                    }
                    finally { _suppressTextChanged = false; }
                    e.CancelCommand();
                    e.Handled = true;
                }
            }
        }

        private void OnAmountTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressTextChanged) return;
            if (sender is TextBox tb && tb.DataContext is AdditionalKpItem item)
            {
                if (!_hasPushedUndoForThisField && TryGetMainWindow(nameof(OnAmountTextChanged), out var mw))
                {
                    mw.PushUndo();
                    _hasPushedUndoForThisField = true;
                }

                _suppressTextChanged = true;
                try
                {
                    if (MoneyFormatService.TryParse(tb.Text, out double val))
                        item.Amount = val;
                }
                finally { _suppressTextChanged = false; }
            }
        }

        private void OnAmountLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb || tb.DataContext is not AdditionalKpItem item) return;

            var formatted = MoneyFormatService.Format(item.Amount);
            if (tb.Text != formatted)
            {
                _suppressTextChanged = true;
                try
                {
                    tb.Text = formatted;
                }
                finally { _suppressTextChanged = false; }
            }
        }

        private void SelectAll_OnFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                _hasPushedUndoForThisField = false;
                tb.Dispatcher.BeginInvoke(() => tb.SelectAll());
            }
        }
    }
}
