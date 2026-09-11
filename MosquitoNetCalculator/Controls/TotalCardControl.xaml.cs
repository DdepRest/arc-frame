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

        private void BtnCopyTotal_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string amount = RunTotalAmount.Text;
                if (string.IsNullOrWhiteSpace(amount)) return;
                Clipboard.SetText(amount);
                ToastService.ShowToast("Сумма скопирована в буфер обмена", ToastType.Success);
            }
            catch { ToastService.ShowToast("Не удалось скопировать", ToastType.Warning); }
        }
    }
}
