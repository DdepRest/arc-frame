using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Design
{
    /// <summary>
    /// Регрессии косметического прохода v3.56 (11 визуальных дефектов аудита).
    ///
    /// Тесты рендерят РЕАЛЬНЫЕ контролы на живых словарях темы, а не читают
    /// разметку регулярками — иначе «исправлено» проверялось бы по тексту XAML,
    /// а не по тому, что видит пользователь. Для дефектов, которые нельзя
    /// измерить (метка поля, подпись кнопки), оставлены явные текстовые стражи.
    /// </summary>
    [Collection("WPF_UI")]
    public class CosmeticFixesUiTests
    {
        // ── Обход визуального дерева ─────────────────────────────────────

        private static IEnumerable<DependencyObject> VisualSubtree(DependencyObject root)
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var d in VisualSubtree(child))
                    yield return d;
            }
        }

        private static Window Host(FrameworkElement content, double width, double height)
        {
            var window = new Window { Content = content, Width = width, Height = height, ShowInTaskbar = false };
            window.Show();
            window.UpdateLayout();
            return window;
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MosquitoNetCalculator.sln")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string ReadSource(string relative)
        {
            string path = Path.Combine(RepoRoot(), "MosquitoNetCalculator", relative);
            Assert.True(File.Exists(path), $"Исходник не найден: {path}");
            return File.ReadAllText(path);
        }

        // ── 1. Превью быстрого ввода: ставка вместо «× 1 = 0,00» ──────────

        [Fact]
        public void QuickAdd_Preview_ShowsUnitRate_WhenDimensionsAreEmpty()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var control = new QuickAddControl { Width = 1000, Height = 260 };
                var window = Host(control, 1020, 320);
                try
                {
                    // Каталог/цены приходят из MainWindow (DataContext), поэтому в
                    // тесте тип и цена выставляются напрямую — превью считает их
                    // теми же полями, что и при выборе пользователем.
                    control.CmbType.ItemsSource = new[] { "Anwis" };
                    control.CmbType.SelectedIndex = 0;
                    control.TxtPrice.Text = "1800";
                    window.UpdateLayout();

                    // Формат — той же культурой, что и в контроле (N2 группы разрядов).
                    string rate = $"{1800d:N2} руб/м²";

                    Assert.Equal(Visibility.Visible, control.Preview.Visibility);
                    Assert.Equal(rate, control.PreviewText.Text);
                    // Главный дефект: формула с нулевым итогом при пустых размерах.
                    Assert.DoesNotContain("= 0,00", control.PreviewText.Text);
                    Assert.DoesNotContain("×", control.PreviewText.Text);

                    control.TxtWidth.Text = "400";
                    control.TxtHeight.Text = "1000";
                    window.UpdateLayout();

                    // Появились размеры — снова честная формула расчёта.
                    Assert.Contains($"м² × {1800d:N2} руб × 1", control.PreviewText.Text);

                    // Стереть размер — вернуться к ставке (не к нулевой формуле).
                    control.TxtWidth.Text = "";
                    window.UpdateLayout();
                    Assert.Equal(rate, control.PreviewText.Text);
                }
                finally { window.Close(); }
            });
        }

        // ── 2. Сайдбар «Заказчик»: прокрутка видима и сбрасывается ───────

        [Fact]
        public void Sidebar_ScrollBarIsAuto_AndResetScrollReturnsToTop()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var sidebar = new SidebarControl();
                var window = Host(sidebar, 340, 260);
                try
                {
                    var scroller = VisualSubtree(sidebar).OfType<ScrollViewer>().First();
                    Assert.Equal(ScrollBarVisibility.Auto, scroller.VerticalScrollBarVisibility);

                    scroller.ScrollToVerticalOffset(120);
                    window.UpdateLayout();
                    Assert.True(scroller.VerticalOffset > 0,
                        "Содержимое панели должно прокручиваться, иначе тест ничего не проверяет.");

                    sidebar.ResetScroll();
                    window.UpdateLayout();
                    Assert.Equal(0, scroller.VerticalOffset);
                }
                finally { window.Close(); }
            });
        }

        // ── 3. Кнопка закрытия вписывается в шапку диалога ───────────────

        [Fact]
        public void DialogCloseButton_FitsInsideTheThirtyTwoPixelHeader()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var buttons = new List<DialogButton<object>> { new("OK", true, false, false, "PrimaryButton") };
                var windows = new List<Window>
                {
                    new MessageDialogWindow("Заголовок", "Сообщение", buttons),
                    new WhatsNewWindow(new Version(3, 56, 0), new List<UpdateItem>()),
                    new UpdateAvailableWindow("3.56.0", new List<UpdateItem>(), isAutomatic: true),
                    new SlopeEconomyDetailsWindow(),
                    new ChangeOrderStatusWindow(new OrderData { ContractNumber = "1-1" }),
                };

                try
                {
                    foreach (var window in windows)
                    {
                        window.Show();
                        window.UpdateLayout();

                        var close = VisualSubtree(window).OfType<Button>()
                            .FirstOrDefault(b => AutomationProperties.GetName(b) == "Закрыть");
                        Assert.True(close != null, $"{window.GetType().Name}: кнопка «Закрыть» не найдена.");

                        Assert.Equal(32, close!.ActualWidth);
                        Assert.Equal(32, close.ActualHeight);

                        // Ближайшая рамка шапки — та, что задаёт её высоту (32).
                        Border? header = null;
                        DependencyObject? node = VisualTreeHelper.GetParent(close);
                        while (node != null)
                        {
                            if (node is Border b && Math.Abs(b.Height - 32) < 0.01) { header = b; break; }
                            node = VisualTreeHelper.GetParent(node);
                        }
                        Assert.True(header != null, $"{window.GetType().Name}: шапка высотой 32 не найдена.");

                        // Кнопка не должна вылезать за границы шапки ни сверху, ни снизу.
                        var top = close.TransformToAncestor(header!).Transform(new Point(0, 0)).Y;
                        Assert.True(top >= -0.5,
                            $"{window.GetType().Name}: кнопка закрытия выступает над шапкой (top={top:F1}).");
                        Assert.True(top + close.ActualHeight <= header!.ActualHeight + 0.5,
                            $"{window.GetType().Name}: кнопка закрытия выходит за низ шапки " +
                            $"(bottom={top + close.ActualHeight:F1} > {header.ActualHeight:F1}).");

                        window.Close();
                    }
                }
                finally
                {
                    foreach (var w in windows)
                    {
                        if (w.IsVisible) w.Close();
                    }
                }
            });
        }

        // ── 4. Справочник цен: плейсхолдер, бейдж и выравнивание шапки ────

        [Fact]
        public void Prices_SearchPlaceholder_IsShownOnlyWhileEmpty()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var control = new PricesControl();
                var window = Host(control, 900, 500);
                try
                {
                    var placeholder = Assert.IsType<TextBlock>(control.FindName("PricesSearchPlaceholder"));
                    var search = Assert.IsType<TextBox>(control.FindName("TxtSearchPrices"));

                    Assert.Equal(Visibility.Visible, placeholder.Visibility);

                    search.Text = "отлив";
                    window.UpdateLayout();
                    Assert.Equal(Visibility.Collapsed, placeholder.Visibility);

                    search.Text = "";
                    window.UpdateLayout();
                    Assert.Equal(Visibility.Visible, placeholder.Visibility);
                }
                finally { window.Close(); }
            });
        }

        [Fact]
        public void Prices_ColumnHeaders_AlignWithTheirValues()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var control = new PricesControl();
                var window = Host(control, 900, 500);
                try
                {
                    var grid = control.PriceDataGrid;
                    Assert.True(grid.Columns.Count >= 3);

                    var left = (Style)Application.Current!.FindResource("LeftColumnHeader");
                    var right = (Style)Application.Current!.FindResource("RightColumnHeader");

                    Assert.Same(left, grid.Columns[0].HeaderStyle);   // Наименование
                    Assert.Same(left, grid.Columns[1].HeaderStyle);   // Цвет
                    Assert.Same(right, grid.Columns[2].HeaderStyle);  // Цена, руб.

                    // Стиль реально доходит до отрендеренной шапки колонки.
                    var headers = VisualSubtree(grid).OfType<DataGridColumnHeader>().ToList();
                    Assert.True(headers.Count >= 3, "Шапка грида не сгенерирована.");

                    var nameHeader = headers.First(h => Equals(h.Column, grid.Columns[0]));
                    var priceHeader = headers.First(h => Equals(h.Column, grid.Columns[2]));
                    Assert.Equal(HorizontalAlignment.Left, nameHeader.HorizontalContentAlignment);
                    Assert.Equal(HorizontalAlignment.Right, priceHeader.HorizontalContentAlignment);
                }
                finally { window.Close(); }
            });
        }

        // ── 5. Оверлей «Заказы»: без дубля заголовка, чип при фильтре ────

        [Fact]
        public void Orders_Header_HasNoDuplicateTitle_AndChipCountsFilteredRows()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var control = new OrdersHistoryControl();
                var window = Host(control, 900, 500);
                try
                {
                    // Дубликат шапки оверлея («ЗАКАЗЫ» + счётчик) убран.
                    Assert.DoesNotContain(VisualSubtree(control).OfType<TextBlock>(),
                        t => t.Text == "ЗАКАЗЫ");

                    control.OrdersGrid.ItemsSource = new List<OrderData>
                    {
                        new() { ContractNumber = "1-1", ClientAddress = "ул. Первая, 1" },
                        new() { ContractNumber = "1-2", ClientAddress = "ул. Вторая, 2" },
                    };
                    control.SetOrdersCount(2);
                    window.UpdateLayout();

                    var chip = Assert.IsType<Border>(control.FindName("OrdersCountChip"));
                    var search = Assert.IsType<TextBox>(control.FindName("TxtSearchOrders"));

                    // Без фильтра итог уже показан в шапке оверлея — чип молчит.
                    Assert.Equal(Visibility.Collapsed, chip.Visibility);
                    Assert.Equal(string.Empty, control.OrdersCount.Text);

                    search.Text = "Первая";
                    window.UpdateLayout();
                    Assert.Equal(Visibility.Visible, chip.Visibility);
                    Assert.Equal("Найдено: 1 из 2", control.OrdersCount.Text);

                    search.Text = "";
                    window.UpdateLayout();
                    Assert.Equal(Visibility.Collapsed, chip.Visibility);
                }
                finally { window.Close(); }
            });
        }

        [Fact]
        public void OrdersAndItems_Headers_UseTheAlignmentStyles()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var left = (Style)Application.Current!.FindResource("LeftColumnHeader");
                var right = (Style)Application.Current!.FindResource("RightColumnHeader");

                var items = new OrderItemsControl();
                var nameCol = items.Grid.Columns.First(c => Equals(c.Header, "Наименование"));
                var priceCol = items.Grid.Columns.First(c => Equals(c.Header, "Цена"));
                var sumCol = items.Grid.Columns.First(c => Equals(c.Header, "Сумма"));
                Assert.Same(left, nameCol.HeaderStyle);
                Assert.Same(right, priceCol.HeaderStyle);
                Assert.Same(right, sumCol.HeaderStyle);

                var orders = new OrdersHistoryControl();
                var ordersLeft = new[] { "№ КП", "Адрес", "Телефон", "Дата", "Обновлено" };
                foreach (var header in ordersLeft)
                {
                    var col = orders.OrdersGrid.Columns.First(c => Equals(c.Header, header));
                    Assert.Same(left, col.HeaderStyle);
                }
                Assert.Same(right, orders.OrdersGrid.Columns.First(c => Equals(c.Header, "Сумма, руб.")).HeaderStyle);
                // Служебная колонка статуса — капсула по центру, стиль не навязан.
                Assert.Null(orders.OrdersGrid.Columns.First(c => Equals(c.Header, "Статус")).HeaderStyle);
            });
        }

        // ── 6. Карточка «ИТОГО»: подсказка Ctrl+Z по состоянию стека ─────

        [Fact]
        public void TotalCard_UndoHint_FollowsTheUndoStack()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var card = new TotalCardControl();
                var window = Host(card, 900, 200);
                try
                {
                    var hint = Assert.IsType<TextBlock>(card.FindName("UndoHint"));

                    // Пустой заказ: отменять нечего — подсказки нет.
                    Assert.Equal(Visibility.Collapsed, hint.Visibility);

                    card.SetUndoVisible(true);
                    Assert.Equal(Visibility.Visible, hint.Visibility);

                    card.SetUndoVisible(false);
                    Assert.Equal(Visibility.Collapsed, hint.Visibility);
                }
                finally { window.Close(); }
            });
        }

        // ── 7. Текстовые стражи для правок, у которых нет измеряемого эффекта ──

        [Fact]
        public void PrintPreview_ZoomFitButton_KeepsItsActionLabel()
        {
            var code = ReadSource("Controls/PrintPreviewControl.xaml.cs");
            // Подпись кнопки — действие, а не зеркало масштаба (рядом «100%»).
            Assert.Contains("ZoomFitBtn.Content = \"Вписать\";", code);
            Assert.Contains("ZoomFitBtn.ToolTip = $\"Вписать в окно (", code);
            Assert.DoesNotContain("ZoomFitBtn.Content = $\"{_fitZoom * 100:F0}%\"", code);
        }

        [Fact]
        public void SlopePanel_DepthField_DeclaresMillimetres()
        {
            var xaml = ReadSource("Controls/SlopePanelControl.xaml");
            Assert.Contains("Text=\"Глубина, мм\"", xaml);
            Assert.DoesNotContain("\"Глубина\"", xaml);
        }
    }
}
