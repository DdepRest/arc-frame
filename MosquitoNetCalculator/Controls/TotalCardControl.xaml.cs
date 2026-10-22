using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    public partial class TotalCardControl : UserControl
    {
        public Run TotalRun => RunTotalAmount;
        public TextBlock TotalSub => TxtTotalSub;
        public TextBlock AmountWords => TxtAmountInWords;
        public Border CardTotalBorder => CardTotal;
        /// <summary>v3.50: meta line «Позиций: N» in the totals right side.</summary>
        public TextBlock PositionsMeta => TxtPositionsMeta;

        public TotalCardControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// v3.50: updates the «Позиций: N» meta line. UI mirror only —
        /// called from the existing CollectionChanged hook in MainWindow.
        /// </summary>
        internal void UpdatePositionsMeta(int count)
        {
            if (TxtPositionsMeta == null) return;
            TxtPositionsMeta.Text = count > 0 ? $"Позиций: {count}" : string.Empty;
        }

        /// <summary>
        /// v3.56: показывает/прячет подсказку «Ctrl+Z — отменить». Состояние
        /// берётся из стека отмены (MainWindow.UpdateUndoRedoHint), а не из
        /// «есть ли позиции»: после отмены последней позиции заказ снова пуст,
        /// но отменить уже нечего.
        /// </summary>
        internal void SetUndoVisible(bool visible)
        {
            if (UndoHint == null) return;
            UndoHint.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void BtnCopyTotal_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string amount = RunTotalAmount.Text;
                if (string.IsNullOrWhiteSpace(amount)) return;
                Clipboard.SetText(amount);
                ToastService.ShowToast("Сумма скопирована в буфер обмена", ToastType.Success);

                if (TxtCopyIcon != null)
                {
                    TxtCopyIcon.Text = "\uE73E";
                    TxtCopyIcon.Foreground = (System.Windows.Media.Brush)FindResource("Success");
                    await System.Threading.Tasks.Task.Delay(1000);
                    TxtCopyIcon.Text = "\uE8C8";
                    TxtCopyIcon.ClearValue(TextBlock.ForegroundProperty);
                }
            }
            catch { ToastService.ShowToast("Не удалось скопировать", ToastType.Warning); }
        }
    }
}
