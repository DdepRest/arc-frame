using System.Windows.Controls;
using System.Windows.Documents;

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
    }
}
