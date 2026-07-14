using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// v3.44.9: окно детализации экономии материалов по всем откосам в заказе.
    /// </summary>
    public partial class SlopeEconomyDetailsWindow : Window
    {
        public SlopeEconomyDetailsWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => Activate();
        }

        /// <summary>
        /// Заполняет таблицу детализации экономии на основе активных откосов заказа.
        /// </summary>
        /// <param name="slopes">Активные расчёты откосов (Distinct по ссылке).</param>
        public void LoadData(IEnumerable<SlopeCalculation> slopes)
        {
            var rows = SlopeEconomyCalculator.CalculateDetails(slopes);
            int totalWindowCount = slopes?.Where(s => s != null).Sum(s => s.WindowCount) ?? 0;

            TxtTotalSlopes.Text = totalWindowCount > 0
                ? $"Всего откосов: {totalWindowCount}"
                : "Нет активных откосов для расчёта экономии.";

            if (totalWindowCount == 0)
            {
                DetailsItems.ItemsSource = new List<EconomyDetailRow>();
                TxtTotalSavedPerSlope.Text = "0 ₽";
                return;
            }

            DetailsItems.ItemsSource = rows;

            double totalSaved = rows.Sum(r => r.AmountSaved);
            double perSlope = totalWindowCount > 0 ? totalSaved / totalWindowCount : 0.0;

            TxtTotalSaved.Text = totalSaved > 0 ? $"−{totalSaved:N0} ₽" : "0 ₽";
            TxtTotalSavedPerSlope.Text = perSlope > 0 ? $"−{perSlope:N0} ₽" : "0 ₽";
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Close();
        }
    }
}
