using System.Linq;
using MosquitoNetCalculator.Helpers;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator
{
    public partial class MainWindow
    {
        internal void LoadPrices()
        {
            ViewModel.PricesVM.LoadPrices();
            if (PricesControl?.PriceDataGrid != null)
            {
                PricesControl.PriceDataGrid.Items.Refresh();
                RecalculatePriceGridColumnWidths();
            }
        }

        internal void RefreshComboBoxColumns()
        {
            ViewModel.RefreshComboBoxColumns();
            QuickAddControl.CmbType.ItemsSource = ProductNames;
        }

        internal void RecalculatePriceGridColumnWidths()
        {
            if (PricesControl?.PriceDataGrid == null) return;

            var grid = PricesControl.PriceDataGrid;
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Наименование"), "Наименование");
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Цвет"), "Цвет",
                Prices.Select(p => p.Color));
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Цена, руб."), "Цена, руб.",
                Prices.Select(p => p.Price > 0 ? MoneyFormatService.Format(p.Price) : ""));
        }
    }
}
