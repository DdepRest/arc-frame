using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using MosquitoNetCalculator.Helpers;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// v3.45.0 (Phase 6 refactoring): static helper that owns the visual
    /// behaviour of the Orders DataGrid — column autosizing to content,
    /// sort indicator (▲/▼) rendering, header-vs-row hit-testing for
    /// double-click filtering. Pure functions; matches the
    /// <see cref="DataGridColumnAutoSizer"/> pattern.
    /// </summary>
    internal static class OrderGridPresenter
    {
        /// <summary>
        /// Refreshes the grid with a new orders list: autosizes columns to
        /// header + widest cell content, restores prior sort descriptions,
        /// re-applies sort indicators. Mirrors the column set declared in
        /// <c>Controls/OrdersHistoryControl.xaml</c>.
        /// </summary>
        public static void RefreshOrdersGrid(DataGrid grid, List<OrderData> orders)
        {
            if (grid == null || orders == null) return;

            var sortDescriptions = grid.Items.SortDescriptions.ToList();

            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "№ КП"), "№ КП",
                orders.Select(o => o.ContractNumber));
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Адрес"), "Адрес");
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Телефон"), "Телефон",
                orders.Select(o => o.ClientPhone));
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Дата"), "Дата",
                orders.Select(o => o.ContractDate.ToString("dd.MM.yyyy")));
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Сумма, руб."), "Сумма, руб.",
                orders.Select(o => MoneyFormatService.Format(o.TotalAmount)),
                contentWeight: FontWeights.Medium);
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Статус"), "Статус",
                orders.Select(o => o.Status).Distinct(),
                contentPad: 32, contentWeight: FontWeights.Medium, contentFontSize: 11);
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Обновлено"), "Обновлено",
                orders.Select(o => o.UpdatedAt.ToString("dd.MM.yy HH:mm")));

            grid.ItemsSource = orders;

            foreach (var sd in sortDescriptions)
                grid.Items.SortDescriptions.Add(sd);
            grid.Items.Refresh();

            ApplySortIndicators(grid);
        }

        /// <summary>
        /// Updates each column's <c>Header</c> with ▲ / ▼ based on currently
        /// active SortDescriptions. Safe to call repeatedly (e.g. after
        /// <c>DataGrid.Sorting</c> event). Reads <c>SortMemberPath</c> first,
        /// then falls back to the Binding path.
        /// </summary>
        public static void ApplySortIndicators(DataGrid grid)
        {
            if (grid == null) return;
            foreach (var col in grid.Columns)
            {
                string clean = DataGridColumnAutoSizer.StripSortIndicator(col.Header?.ToString());
                string? sortKey = GetColumnSortKey(col);
                var match = !string.IsNullOrEmpty(sortKey)
                    ? grid.Items.SortDescriptions.FirstOrDefault(x => x.PropertyName == sortKey)
                    : default;
                if (!string.IsNullOrEmpty(match.PropertyName))
                {
                    col.Header = clean + (match.Direction == ListSortDirection.Ascending ? " \u25B2" : " \u25BC");
                    col.SortDirection = match.Direction;
                }
                else
                {
                    col.Header = clean;
                    col.SortDirection = null;
                }
            }
        }

        /// <summary>
        /// Extracts a bindable property name from a DataGridColumn,
        /// preferring explicit <c>SortMemberPath</c> and falling back to
        /// the Binding path of a <see cref="DataGridBoundColumn"/>.
        /// </summary>
        public static string? GetColumnSortKey(DataGridColumn col)
        {
            if (col == null) return null;
            if (!string.IsNullOrEmpty(col.SortMemberPath))
                return col.SortMemberPath;
            if (col is DataGridBoundColumn bound
                && bound.Binding is Binding b
                && b.Path != null)
                return b.Path.Path;
            return null;
        }

        /// <summary>
        /// Walks the visual tree from the click hit-test result. Returns
        /// <c>true</c> if the originating element is a <c>DataGrid</c>
        /// COLUMN header or ROW header — in which case <c>MouseDoubleClick</c>
        /// must NOT open the order (header click already triggers a sort).
        /// </summary>
        public static bool IsHeaderClick(DependencyObject? hit)
        {
            while (hit != null)
            {
                if (hit is DataGridColumnHeader || hit is DataGridRowHeader)
                    return true;
                hit = VisualTreeHelper.GetParent(hit);
            }
            return false;
        }
    }
}
