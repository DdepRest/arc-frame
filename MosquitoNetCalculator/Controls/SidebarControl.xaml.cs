using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    public partial class SidebarControl : UserControl
    {
        public Border CardClientBorder => CardClient;
        public Border CardContractBorder => CardContract;
        public Border CardNotesBorder => CardNotes;
        public TextBox TxtPrefix => TxtContractPrefix;
        public TextBox TxtNumber => TxtContractNumber;
        public ComboBox CmbOrderStatus => CmbStatus;

        public SidebarControl()
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
            System.Diagnostics.Trace.WriteLine($"[SidebarControl] DataContext is not MainWindow ({DataContext?.GetType().Name ?? "null"}), handler '{handlerName}' skipped.");
            mw = null;
            return false;
        }

        private void TxtContractPrefix_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(TxtContractPrefix_TextChanged), out var mw)) return;
            if (mw._suppressContractNumberUpdate) return;
            if (!mw.IsInitialized) return;
            mw.SuppressPrefixSave = false;
            mw.UpdateContractNumber();
        }

        private void TxtContractPrefix_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(TxtContractPrefix_LostFocus), out var mw)) return;
            if (mw.SuppressPrefixSave) return;
            AppSettingsService.SaveContractPrefix(TxtContractPrefix.Text);
        }

        private void SelectAll_OnFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.Dispatcher.BeginInvoke(() => tb.SelectAll());
        }
    }
}
