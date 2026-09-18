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
        // Телефонная колонка: контент длиннее этого капа не поднимает минимум.
        // Кап подобран так, чтобы сумма минимумов всех Auto-колонок оставляла
        // адресной (*-колонке) не меньше ~150 DIP во вьюпорте узкой панели
        // (~776 DIP при ужатом до окна оверлее). Длинные форматы не должны
        // отнимать ширину у адреса.
        private const double PhoneColumnCap = 126;

        // «Обновлено»: формат фиксированный «дд.мм.гг чч:мм» (~105px),
        // кап страхует от нестандартных значений.
        private const double UpdatedColumnCap = 110;

        /// <summary>
        /// Заголовок колонки всегда отображается В ВЕРХНЕМ РЕГИСТРЕ
        /// (<see cref="Converters.UppercaseHeaderTemplate"/>), поэтому замер
        /// ведётся по капсу — иначе минимум берётся из «Сумма, руб.» (~70px),
        /// а рисуется «СУММА, РУБ.» (~88px) и шапка режется многоточием.
        /// </summary>
        private static string UpperHeader(string header) => header.ToUpperInvariant();
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

            // АДРЕС — приоритетная колонка: минимум из XAML (150) не должен
            // опускаться до ширины заголовка «АДРЕС» ≈60px, иначе адрес снова
            // станет «ПУШКИНСКА…». Держит это сам автосайзер: объявленный в
            // разметке MinWidth — пол для ЛЮБОЙ колонки (GOTCHAS §34), поэтому
            // отдельной страховки именно для адреса больше не нужно.
            DataGridColumnAutoSizer.SetColumnMinWidth(grid,
                DataGridColumnAutoSizer.FindCol(grid, "Адрес"), UpperHeader("Адрес"));

            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "№ КП"), UpperHeader("№ КП"),
                orders.Select(o => o.ContractNumber));
            // ТЕЛЕФОН ограничен сверху разумным капом: полный номер с
            // пробелами («+7 994 948 53 24») задавал бы минимум ~190px,
            // отнимая ширину у адреса. Номер читается и без «жирной»
            // колонки — это Auto-колонка, лишний текст не обрезается.
            var phoneCol = DataGridColumnAutoSizer.FindCol(grid, "Телефон");
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, phoneCol, UpperHeader("Телефон"),
                orders.Select(o => o.ClientPhone), contentCap: PhoneColumnCap);
            if (phoneCol != null && phoneCol.MaxWidth < double.MaxValue && phoneCol.MinWidth > phoneCol.MaxWidth)
                phoneCol.MinWidth = phoneCol.MaxWidth;
            // Обновлено: содержимое фиксированного формата «дд.мм.гг чч:мм» —
            // кап не даёт длинным значениям раздуть минимум.
            var updCol = DataGridColumnAutoSizer.FindCol(grid, "Обновлено");
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, updCol, UpperHeader("Обновлено"),
                orders.Select(o => o.UpdatedAt.ToString("dd.MM.yy HH:mm")), contentCap: UpdatedColumnCap);

            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Дата"), UpperHeader("Дата"),
                orders.Select(o => o.ContractDate.ToString("dd.MM.yyyy")));
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Сумма, руб."), UpperHeader("Сумма, руб."),
                orders.Select(o => MoneyFormatService.Format(o.TotalAmount)),
                contentWeight: FontWeights.Medium);
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Статус"), UpperHeader("Статус"),
                orders.Select(o => o.Status).Distinct(),
                // кап 118 = MaxWidth XAML (150) минус contentPad 32: иначе минимум
                // от длинного бейджа («ОТПРАВЛЕН НА ЗАВОД») выходит за MaxWidth,
                // и MinWidth>MaxWidth выталкивает «Обновлено» за край панели.
                contentPad: 32, contentWeight: FontWeights.Medium, contentFontSize: 11,
                contentCap: 118);
            // Статусная колонка: MaxWidth из XAML (150) ограничивает её фактическую
            // ширину, но клампить MinWidth до MaxWidth НЕЛЬЗЯ — жёсткое равенство
            // (MinWidth=MaxWidth=150) на узкой панели лишает DataGrid возможности
            // ужать колонку, и последняя колонка («Обновлено») выталкивается за
            // край вьюпорта. Достаточно того, что сам бейдж с переносом
            // укладывается в 150.

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
