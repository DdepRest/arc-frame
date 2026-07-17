using System.Windows;
using System.Windows.Input;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// v3.45.0 (Phase 6 refactoring): chromeless order-status dialog
    /// using the Phase 4 MessageDialogWindow aesthetic — replaces the
    /// 80-line inline XAML block previously embedded in
    /// <c>MainWindow.ChangeSelectedOrderStatus</c>. Reads the full
    /// workflow from <see cref="OrderStatuses.All"/>.
    /// </summary>
    public partial class ChangeOrderStatusWindow : Window
    {
        /// <summary>
        /// Status picked by the user. Equals the original order's status
        /// if the dialog was cancelled or closed via Esc — caller should
        /// compare against the source to detect actual changes.
        /// </summary>
        public string? SelectedStatus { get; private set; }

        /// <summary>
        /// True only when the user clicked «Сохранить» (IsDefault button).
        /// <c>false</c> on Cancel, Esc, or close-button.
        /// </summary>
        public bool Saved { get; private set; }

        public ChangeOrderStatusWindow(OrderData order, Window? owner = null)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;

            OrderHeaderText.Text = $"Заказ: {order.ContractNumber}";
            Title = "Изменить статус заказа";

            // Pre-select current status.
            SelectedStatus = order.Status;
            StatusCombo.SelectedItem = order.Status;

            // Focus on Loaded. Pre-selecting text only makes sense if the
            // ComboBox is editable — non-editable combo (the default style)
            // has no selected text to highlight. Gate the SelectAll so we
            // don't trigger a TextBox-FindName cycle when PART_EditableTextBox
            // is absent.
            Loaded += (_, _) =>
            {
                StatusCombo.Focus();
                if (StatusCombo.IsEditable
                    && StatusCombo.Template?.FindName("PART_EditableTextBox", StatusCombo) is System.Windows.Controls.TextBox tb)
                {
                    tb.SelectAll();
                }
            };
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Close button behaves like Cancel — does NOT set Saved.
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            SelectedStatus = StatusCombo.SelectedItem as string;
            Saved = true;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Esc and Enter are handled by IsCancel / IsDefault; this is
            // a defensive escape-hatch if either path fails (e.g. focus is
            // somewhere that eats the key).
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }
    }
}
