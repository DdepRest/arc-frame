using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using MosquitoNetCalculator.Helpers;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Tests for <see cref="OrderGridPresenter"/> — pure DataGrid
    /// introspection logic (sort indicators, header-vs-row hit-testing,
    /// column sort-key extraction). STA-thread tests via
    /// <see cref="WpfTestHelper.RunOnSta"/> because
    /// <c>DataGridColumnHeader</c> / <c>DataGridRowHeader</c> and
    /// <c>ItemCollection.SortDescriptions</c> require STA.
    /// </summary>
    public class OrderGridPresenterTests
    {
        // ─── GetColumnSortKey ────────────────────────────────

        [Fact]
        public void GetColumnSortKey_ReturnsSortMemberPath_WhenSet()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var column = new DataGridTextColumn { SortMemberPath = "ContractNumber" };
                Assert.Equal("ContractNumber", OrderGridPresenter.GetColumnSortKey(column));
            });
        }

        [Fact]
        public void GetColumnSortKey_FallsBackToBindingPath_WhenSortMemberPathEmpty()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var column = new DataGridTextColumn
                {
                    SortMemberPath = "",
                    Binding = new Binding("ClientName")
                };
                Assert.Equal("ClientName", OrderGridPresenter.GetColumnSortKey(column));
            });
        }

        [Fact]
        public void GetColumnSortKey_FallsBackToBinding_WhenSortMemberPathNull()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var column = new DataGridTextColumn
                {
                    SortMemberPath = null,
                    Binding = new Binding("Phone")
                };
                Assert.Equal("Phone", OrderGridPresenter.GetColumnSortKey(column));
            });
        }

        [Fact]
        public void GetColumnSortKey_NoSortInfo_ReturnsNull()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var column = new DataGridTextColumn
                {
                    SortMemberPath = null,
                    Binding = null
                };
                Assert.Null(OrderGridPresenter.GetColumnSortKey(column));
            });
        }

        [Fact]
        public void GetColumnSortKey_NullColumn_ReturnsNull()
        {
            Assert.Null(OrderGridPresenter.GetColumnSortKey(null!));
        }

        // ─── IsHeaderClick ──────────────────────────────────

        [Fact]
        public void IsHeaderClick_DataGridColumnHeader_ReturnsTrue()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var header = new DataGridColumnHeader();
                Assert.True(OrderGridPresenter.IsHeaderClick(header));
            });
        }

        [Fact]
        public void IsHeaderClick_DataGridRowHeader_ReturnsTrue()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var header = new DataGridRowHeader();
                Assert.True(OrderGridPresenter.IsHeaderClick(header));
            });
        }

        [Fact]
        public void IsHeaderClick_GenericTextBlock_ReturnsFalse()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var textBlock = new System.Windows.Controls.TextBlock();
                Assert.False(OrderGridPresenter.IsHeaderClick(textBlock));
            });
        }

        [Fact]
        public void IsHeaderClick_Null_ReturnsFalse()
        {
            Assert.False(OrderGridPresenter.IsHeaderClick(null));
        }

        // ─── ApplySortIndicators ────────────────────────────

        private static DataGrid NewGridWithColumn(string header, string sortKey)
        {
            var grid = new DataGrid();
            // ItemsSource is a placeholder so ItemCollection.SortDescriptions
            // initialises cleanly (without ItemsSource some WPF versions
            // throw when adding). The placeholder list has no impact on
            // the header-arrow logic we are testing.
            grid.ItemsSource = new List<object> { new object() };
            var col = new DataGridTextColumn
            {
                Header = header,
                SortMemberPath = sortKey,
                Binding = new Binding(sortKey)
            };
            grid.Columns.Add(col);
            return grid;
        }

        [Fact]
        public void ApplySortIndicators_AddsAscendingArrow_WhenSortActive()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var grid = NewGridWithColumn("Name", "Name");
                grid.Items.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
                var col = grid.Columns[0];

                OrderGridPresenter.ApplySortIndicators(grid);

                Assert.Equal("Name ▲", col.Header);
                Assert.Equal(ListSortDirection.Ascending, col.SortDirection);
            });
        }

        [Fact]
        public void ApplySortIndicators_AddsDescendingArrow_WhenSortActive()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var grid = NewGridWithColumn("Date", "Date");
                grid.Items.SortDescriptions.Add(new SortDescription("Date", ListSortDirection.Descending));
                var col = grid.Columns[0];

                OrderGridPresenter.ApplySortIndicators(grid);

                Assert.Equal("Date ▼", col.Header);
                Assert.Equal(ListSortDirection.Descending, col.SortDirection);
            });
        }

        [Fact]
        public void ApplySortIndicators_StripsExistingArrow_BeforeAddingNew()
        {
            // Idempotency: a second call must not duplicate the arrow.
            WpfTestHelper.RunOnSta(() =>
            {
                var grid = NewGridWithColumn("Name ▲", "Name");
                grid.Items.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
                var col = grid.Columns[0];

                OrderGridPresenter.ApplySortIndicators(grid);

                Assert.Equal("Name ▲", col.Header);
            });
        }

        [Fact]
        public void ApplySortIndicators_NoSortActive_ClearsArrow()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                // Header simulates a sticky arrow left over from a
                // prior sort that has since been cleared.
                var grid = NewGridWithColumn("Name ▲", "Name");
                var col = grid.Columns[0];

                OrderGridPresenter.ApplySortIndicators(grid);

                Assert.Equal("Name", col.Header);
                Assert.Null(col.SortDirection);
            });
        }

        [Fact]
        public void ApplySortIndicators_NoMatchingSortKey_DoesNotPickArrow()
        {
            // SortDescriptions has a different column key — our column
            // must NOT pick up someone else's arrow.
            WpfTestHelper.RunOnSta(() =>
            {
                var grid = NewGridWithColumn("Address ▼", "Address");
                grid.Items.SortDescriptions.Add(new SortDescription("Other", ListSortDirection.Ascending));
                var col = grid.Columns[0];

                OrderGridPresenter.ApplySortIndicators(grid);

                Assert.Equal("Address", col.Header);
                Assert.Null(col.SortDirection);
            });
        }

        [Fact]
        public void ApplySortIndicators_HandlesMultipleColumns()
        {
            // Two columns, one with sort active — only the matching
            // column shows the arrow.
            WpfTestHelper.RunOnSta(() =>
            {
                var grid = new DataGrid { ItemsSource = new List<object> { new object() } };
                var colA = new DataGridTextColumn
                {
                    Header = "Name",
                    SortMemberPath = "Name",
                    Binding = new Binding("Name")
                };
                var colB = new DataGridTextColumn
                {
                    Header = "Address",
                    SortMemberPath = "Address",
                    Binding = new Binding("Address")
                };
                grid.Columns.Add(colA);
                grid.Columns.Add(colB);
                grid.Items.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));

                OrderGridPresenter.ApplySortIndicators(grid);

                Assert.Equal("Name ▲", colA.Header);
                Assert.Equal(ListSortDirection.Ascending, colA.SortDirection);
                Assert.Equal("Address", colB.Header);
                Assert.Null(colB.SortDirection);
            });
        }

        // ─── RefreshOrdersGrid (entry-point) ─────────────────

        [Fact]
        public void RefreshOrdersGrid_NullGrid_DoesNotThrow()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var ex = Record.Exception(() =>
                    OrderGridPresenter.RefreshOrdersGrid(null!, new List<OrderData> { new() }));
                Assert.Null(ex);
            });
        }

        [Fact]
        public void RefreshOrdersGrid_NullOrders_DoesNotThrow()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var grid = new DataGrid();
                var ex = Record.Exception(() =>
                    OrderGridPresenter.RefreshOrdersGrid(grid, null!));
                Assert.Null(ex);
            });
        }

        [Fact]
        public void RefreshOrdersGrid_SetsItemsSource_ToProvidedOrders()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var grid = NewGridWithColumn("№ КП", "ContractNumber");
                var orders = new List<OrderData> { new() { ContractNumber = "1-1" } };

                OrderGridPresenter.RefreshOrdersGrid(grid, orders);

                Assert.Same(orders, grid.ItemsSource);
            });
        }

        [Fact]
        public void RefreshOrdersGrid_RestoresSortDescriptions_AfterItemsSourceSwap()
        {
            // Regression guard: the inline RefreshOrdersList used to
            // snapshot SortDescriptions, set ItemsSource, restore them
            // then Refresh. After extraction to the presenter, that
            // contract must be preserved — otherwise ascending-arrow on
            // header survives but sort itself is lost on reload.
            WpfTestHelper.RunOnSta(() =>
            {
                var grid = NewGridWithColumn("Name", "Name");
                grid.Items.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Descending));

                var orders = new List<OrderData> { new() { ContractNumber = "2-1" } };
                OrderGridPresenter.RefreshOrdersGrid(grid, orders);

                var sort = grid.Items.SortDescriptions
                    .FirstOrDefault(x => x.PropertyName == "Name");
                Assert.False(string.IsNullOrEmpty(sort.PropertyName),
                    "Sort description for 'Name' must survive RefreshOrdersGrid");
                Assert.Equal(ListSortDirection.Descending, sort.Direction);
                Assert.Equal("Name ▼", grid.Columns[0].Header);
            });
        }

        // ─── SetColumnMinWidth: объявленный MinWidth — пол ─────

        /// <summary>
        /// Регрессия v3.53.1. Автосайзер ПРИСВАИВАЛ вычисленный минимум,
        /// затирая объявленный в разметке. Колонка «№ КП» получала 50px —
        /// ровно подпись 11px SemiBold плюс паддинг шапки 10+10, без запаса.
        /// На ужатом окне (источник: список заказов, окно ~790 DIP) такая
        /// колонка показывала «№…», а «СУММА, руб.» — «СУММА, Р…».
        /// </summary>
        [Fact]
        public void SetColumnMinWidth_DoesNotDropBelowDeclaredMinWidth()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var grid = new DataGrid { FontSize = 12 };
                var col = new DataGridTextColumn
                {
                    Header = "№ КП",
                    MinWidth = 60,
                    Width = DataGridLength.Auto
                };
                grid.Columns.Add(col);

                DataGridColumnAutoSizer.SetColumnMinWidth(grid, col, "№ КП", new[] { "2-50" });
                Assert.Equal(60, col.MinWidth);

                // Содержимое минимум ПОДНИМАЕТ…
                DataGridColumnAutoSizer.SetColumnMinWidth(grid, col, "№ КП", new[] { "2-50-2026-длинный-номер" });
                Assert.True(col.MinWidth > 60,
                    $"Длинное содержимое должно поднять минимум, получено {col.MinWidth}.");

                // …но не превращает его в храповик: с коротким содержимым
                // минимум возвращается к объявленному, а не остаётся раздутым.
                DataGridColumnAutoSizer.SetColumnMinWidth(grid, col, "№ КП", new[] { "2-50" });
                Assert.Equal(60, col.MinWidth);
            });
        }

        /// <summary>
        /// Страж, который поймал бы обрезку шапки ДО пользователя: объявленный
        /// в разметке <c>MinWidth</c> каждой колонки списка заказов обязан
        /// вмещать подпись шапки так, как её меряет сам автосайзер (11px
        /// SemiBold, ВЕРХНИЙ регистр — шапка рисуется капсом), плюс паддинг
        /// шапки 10+10 и запас. Без запаса колонка, ужатая узким окном до
        /// минимума, отдаёт подпись под многоточие (GOTCHAS §34).
        /// </summary>
        [Fact]
        public void Заказы_ОбъявленныйMinWidthКолонок_ВмещаетПодписьШапки()
        {
            var columns = OrdersGridColumns();
            Assert.Equal(7, columns.Count);

            WpfTestHelper.RunOnSta(() =>
            {
                var grid = new DataGrid
                {
                    FontSize = 12,
                    FontFamily = AppFontService.CreateInterFamily()
                };

                // Собираем ВСЕ проблемы в один список: падение перечисляет
                // сразу все узкие колонки, а не первую попавшуюся.
                var problems = new List<string>();
                foreach (var (header, minWidth) in columns)
                {
                    double needed = DataGridColumnAutoSizer.MeasureHeaderWidth(
                                        grid, header.ToUpperInvariant())
                                    + DataGridColumnAutoSizer.HeaderPadding;
                    if (minWidth < needed + MinimalSlack)
                        problems.Add("«" + header + "»: MinWidth " + minWidth +
                                     " < подпись " + needed.ToString("F1") +
                                     " + запас " + MinimalSlack);
                }

                Assert.True(problems.Count == 0,
                    "На узком окне колонка опускается до объявленного MinWidth, и подпись шапки " +
                    "уходит в многоточие:\n  " + string.Join("\n  ", problems));
            });
        }

        /// <summary>Запас между подписью и минимумом: страховка от дробных пикселей замера.</summary>
        private const double MinimalSlack = 4;

        /// <summary>
        /// Разбирает колонки таблицы заказов из разметки: (заголовок, MinWidth).
        /// Колонка без объявленного MinWidth — уже ошибка: именно он служит
        /// полом для автосайзера, и без него колонку снова ужмёт под подпись.
        /// </summary>
        private static List<(string Header, double MinWidth)> OrdersGridColumns()
        {
            string path = Path.Combine(RepoRoot(), "MosquitoNetCalculator", "Controls", "OrdersHistoryControl.xaml");
            string xaml = Regex.Replace(File.ReadAllText(path), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

            var result = new List<(string, double)>();
            foreach (Match m in Regex.Matches(xaml,
                "<DataGrid(?:Text|Template)Column\\b(?<body>.*?)(?:/>|>)", RegexOptions.Singleline))
            {
                var header = Regex.Match(m.Groups["body"].Value, "Header=\"(?<h>[^\"]+)\"");
                if (!header.Success) continue; // колонки без подписи (служебные) не проверяем
                string headerText = header.Groups["h"].Value;
                var min = Regex.Match(m.Groups["body"].Value, "MinWidth=\"(?<w>[0-9.]+)\"");
                Assert.True(min.Success,
                    "У колонки «" + headerText + "» нет MinWidth — автосайзер сможет ужать её под подпись шапки.");
                result.Add((header.Groups["h"].Value,
                    double.Parse(min.Groups["w"].Value, System.Globalization.CultureInfo.InvariantCulture)));
            }

            return result;
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null
                   && !File.Exists(Path.Combine(dir.FullName, "MosquitoNetCalculator", "App.xaml")))
                dir = dir.Parent;
            if (dir == null)
                throw new DirectoryNotFoundException("Не найден корень репозитория.");
            return dir.FullName;
        }
        /// <summary>
        /// Разбирает колонки таблицы позиций заказа из разметки: (заголовок, MinWidth, isStar).
        /// </summary>
        private static List<(string Header, double MinWidth, bool IsStar)> OrderItemsGridColumns()
        {
            string path = Path.Combine(RepoRoot(), "MosquitoNetCalculator", "Controls", "OrderItemsControl.xaml");
            string xaml = Regex.Replace(File.ReadAllText(path), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

            var result = new List<(string, double, bool)>();
            foreach (Match m in Regex.Matches(xaml,
                "<DataGrid(?:Text|Template)Column\\b(?<body>.*?)(?:/>|>)", RegexOptions.Singleline))
            {
                var header = Regex.Match(m.Groups["body"].Value, "Header=\"(?<h>[^\"]+)\"");
                if (!header.Success) continue;
                string headerText = header.Groups["h"].Value.Replace("&#x2116;", "№");
                if (string.IsNullOrWhiteSpace(headerText)) continue;

                var min = Regex.Match(m.Groups["body"].Value, "MinWidth=\"(?<w>[0-9.]+)\"");
                Assert.True(min.Success,
                    "У колонки «" + headerText + "» нет MinWidth — автосайзер сможет ужать её под подпись шапки.");

                bool isStar = Regex.IsMatch(m.Groups["body"].Value, "Width=\"\\*\"");
                result.Add((headerText,
                    double.Parse(min.Groups["w"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    isStar));
            }

            return result;
        }

        /// <summary>
        /// Страж таблицы позиций: объявленный в разметке MinWidth каждой колонки
        /// таблицы позиций («Расчёт») обязан вмещать подпись шапки в ВЕРХНЕМ РЕГИСТРЕ
        /// (шапка рисуется капсом через UppercaseHeaderTemplate), плюс паддинг шапки
        /// 10+10 и минимальный запас 4px. Без этого колонка на узком окне уходит в
        /// многоточие («ПЛОЩ./...») или зажёвывает числа (GOTCHAS §33, §34).
        /// </summary>
        [Fact]
        public void Позиции_ОбъявленныйMinWidthКолонок_ВмещаетПодписьШапки()
        {
            var columns = OrderItemsGridColumns();
            Assert.True(columns.Count >= 9, $"Ожидалось не менее 9 колонок с заголовками, найдено {columns.Count}");

            WpfTestHelper.RunOnSta(() =>
            {
                var grid = new DataGrid
                {
                    FontSize = 12,
                    FontFamily = AppFontService.CreateInterFamily()
                };

                var problems = new List<string>();
                foreach (var (header, minWidth, isStar) in columns)
                {
                    if (isStar) continue; // Звезда-колонка («Наименование») динамически занимает всё свободное место

                    double needed = DataGridColumnAutoSizer.MeasureHeaderWidth(
                                        grid, header.ToUpperInvariant())
                                    + DataGridColumnAutoSizer.HeaderPadding;
                    if (minWidth < needed + MinimalSlack)
                        problems.Add("«" + header + "»: MinWidth " + minWidth +
                                     " < подпись " + needed.ToString("F1") +
                                     " + запас " + MinimalSlack);
                }

                Assert.True(problems.Count == 0,
                    "На узком окне колонка позиций опускается до объявленного MinWidth, и подпись шапки " +
                    "уходит в многоточие:\n  " + string.Join("\n  ", problems));
            });
        }

        /// <summary>
        /// Страж числовых колонок «Сумма» и «Цена»: объявленный пол MinWidth обязан
        /// вмещать четырёх-пятизначные числа (напр. «1 579,40», «12 500,00») с табулярными
        /// цифрами Inter, правым выравниванием и воздухом 14px слева (Margin 14,0,6,0),
        /// чтобы числа не зажёвывались и не обрезались слева.
        /// </summary>
        [Fact]
        public void ЧисловыеКолонки_ОбъявленныйMinWidth_ВмещаетЧетырехзначныеСуммы()
        {
            var columns = OrderItemsGridColumns();
            var sumCol = columns.FirstOrDefault(c => c.Header == "Сумма");
            var priceCol = columns.FirstOrDefault(c => c.Header == "Цена");

            Assert.True(sumCol.MinWidth >= 95,
                $"Колонка «Сумма» имеет MinWidth={sumCol.MinWidth} < 95 — суммы вида «1 579,40» будут обрезаться слева.");
            Assert.True(priceCol.MinWidth >= 85,
                $"Колонка «Цена» имеет MinWidth={priceCol.MinWidth} < 85 — цены вида «15 000,00» будут обрезаться слева.");
        }
    }
}
