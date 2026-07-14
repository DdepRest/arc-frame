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

        public TotalCardControl()
        {
            InitializeComponent();
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
