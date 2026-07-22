using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MosquitoNetCalculator.Helpers;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator
{
    public partial class MainWindow
    {
        internal void RecalculateOrderGridColumnWidths()
        {
            if (_columnRecalcPending) return;
            _columnRecalcPending = true;

            Dispatcher.BeginInvoke(() =>
            {
                _columnRecalcPending = false;
                if (IsLoaded) RecalculateOrderGridColumnWidthsCore();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void RecalculateOrderGridColumnWidthsCore()
        {
            if (OrderItemsControl?.Grid == null) return;

            var grid = OrderItemsControl.Grid;
            if (grid.Columns.Count >= 12)
            {
                DataGridColumnAutoSizer.SetColumnMinWidth(grid, grid.Columns[0], "", null, 54, 0);
                DataGridColumnAutoSizer.SetColumnMinWidth(grid, grid.Columns[11], "", null, 32, 0);
            }

            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "№"), "№",
                OrderItems.Select(i => i.RowNumber.ToString()), headerPad: 20);
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Наименование"), "Наименование");
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Цвет"), "Цвет",
                OrderItems.Select(i => i.IsAmountOnly ? "" : i.Color));
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Ширина"), "Ширина",
                OrderItems.Select(i => i.IsAmountOnly ? "" : i.Width > 0 ? $"{i.Width:F0} мм" : ""));
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Высота"), "Высота",
                OrderItems.Select(i => i.IsAmountOnly ? "" : i.Height > 0 ? $"{i.Height:F0} мм" : ""));
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Монтаж"), "Монтаж",
                OrderItems.Select(i => i.InstallationDisplay),
                contentPad: 40, contentWeight: FontWeights.Bold, contentFontSize: 14);
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Кол-во"), "Кол-во",
                OrderItems.Select(i => i.IsAmountOnly ? "" : i.Quantity > 0 ? i.Quantity.ToString("G") : ""),
                contentPad: 24);
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Площ./Дл."), "Площ./Дл.",
                OrderItems.Select(i => i.CalculatedValueDisplay),
                contentPad: 24);
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Цена"), "Цена",
                OrderItems.Select(i => i.Price > 0 ? MoneyFormatService.Format(i.Price) : ""));
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Сумма"), "Сумма",
                OrderItems.Select(i => i.TotalDisplay),
                contentPad: 24, contentWeight: FontWeights.Medium);
        }
    }
}
