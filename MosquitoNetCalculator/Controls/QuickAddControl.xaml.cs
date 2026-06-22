using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    public partial class QuickAddControl : UserControl
    {
        private bool _updatingQuickCombo;

        public Border CardQuickAddBorder => CardQuickAdd;
        public ComboBox CmbType => CmbQuickType;
        public ComboBox CmbColor => CmbQuickColor;
        public TextBox TxtWidth => TxtQuickWidth;
        public TextBox TxtHeight => TxtQuickHeight;
        public TextBox TxtQty => TxtQuickQty;
        public TextBox TxtPrice => TxtQuickPrice;
        public TextBox TxtSearch => TxtQuickSearch;
        public Popup SearchDrop => SearchPopup;
        public ListBox Suggestions => SearchSuggestions;
        public Border Preview => PreviewChip;
        public TextBlock PreviewText => TxtQuickPreview;
        /// <summary>Currently selected AnwisSizeMode — persisted across «Добавить» clicks, reset on new order.</summary>
        public Models.AnwisSizeMode SelectedAnwisMode { get; private set; } = Models.AnwisSizeMode.Брусбокс60;

        public QuickAddControl()
        {
            InitializeComponent();

            // Right-click on the Type dropdown opens the Anwis mode context menu —
            // zero pixels of permanent workspace footprint, discoverable via ToolTip.
            CmbQuickType.PreviewMouseRightButtonDown += CmbQuickType_PreviewMouseRightButtonDown;
        }

        /// <summary>
        /// Resolves the parent MainWindow from DataContext, logging a diagnostic if the
        /// DataContext is not a MainWindow. Returns false in that case so callers can
        /// bail out gracefully.
        /// </summary>
        private bool TryGetMainWindow(string handlerName, [NotNullWhen(true)] out MainWindow? mw)
        {
            if (DataContext is MainWindow window)
            {
                mw = window;
                return true;
            }
            System.Diagnostics.Trace.WriteLine($"[QuickAddControl] DataContext is not MainWindow ({DataContext?.GetType().Name ?? "null"}), handler '{handlerName}' skipped.");
            mw = null;
            return false;
        }

        private void CmbQuickType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingQuickCombo) return;
            if (!TryGetMainWindow(nameof(CmbQuickType_SelectionChanged), out var mw)) return;
            if (CmbQuickType.SelectedItem is not string type || string.IsNullOrWhiteSpace(type)) return;

            _updatingQuickCombo = true;
            try
            {
                var colors = mw.PricesVM.GetColorsForProduct(type);
                bool noColor = OrderItem.NoColorProducts.Contains(type);
                CmbQuickColor.IsEnabled = !noColor;
                CmbQuickColor.Items.Clear();
                if (noColor)
                {
                    // No-color product — load default price (or empty) and keep dropdown empty/disabled
                    var price = mw.PricesVM.GetPrice(type, string.Empty);
                    TxtQuickPrice.Text = price > 0 ? MoneyFormatService.Format(price) : string.Empty;
                }
                else if (colors.Any())
                {
                    foreach (var c in colors) CmbQuickColor.Items.Add(c);
                    CmbQuickColor.SelectedIndex = 0;
                    var price = mw.PricesVM.GetPrice(type, colors[0]);
                    TxtQuickPrice.Text = MoneyFormatService.Format(price);
                }
                else
                {
                    // No colors available for this product — still load price
                    var price = mw.PricesVM.GetPrice(type, string.Empty);
                    TxtQuickPrice.Text = price > 0 ? MoneyFormatService.Format(price) : string.Empty;
                }

                bool isManualPiece = OrderItem.ManualPieceProducts.Contains(type);
                bool widthEnabled = !isManualPiece || OrderItem.WidthOnlyProducts.Contains(type);
                bool heightEnabled = !isManualPiece;
                TxtQuickWidth.IsEnabled = widthEnabled;
                TxtQuickHeight.IsEnabled = heightEnabled;
                if (!widthEnabled)
                    TxtQuickWidth.Text = string.Empty;
                if (!heightEnabled)
                    TxtQuickHeight.Text = string.Empty;

                // Anwis mode ToolTip on the Type dropdown — 0 px of workspace.
                UpdateAnwisModeToolTip();
            }
            finally { _updatingQuickCombo = false; }
            UpdateQuickPreview();
        }

        private void CmbQuickColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingQuickCombo) return;
            if (!TryGetMainWindow(nameof(CmbQuickColor_SelectionChanged), out var mw)) return;
            if (CmbQuickType.SelectedItem is string type && CmbQuickColor.SelectedItem is string color)
            {
                var price = mw.PricesVM.GetPrice(type, color);
                TxtQuickPrice.Text = MoneyFormatService.Format(price);
            }
            UpdateQuickPreview();
        }

        private void QuickField_TextChanged(object sender, TextChangedEventArgs e) => UpdateQuickPreview();

        private void QuickField_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) QuickAddItem();
        }

        private void QuickField_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.Dispatcher.BeginInvoke(() => tb.SelectAll());
        }

        private void TxtQuickPrice_LostFocus(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(TxtQuickPrice.Text, out double price))
                TxtQuickPrice.Text = MoneyFormatService.Format(price);
        }

        private void BtnQuickAdd_Click(object sender, RoutedEventArgs e) => QuickAddItem();

        private void QuickAddItem()
        {
            if (!TryGetMainWindow(nameof(QuickAddItem), out var mw)) return;

            string? type = CmbQuickType.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(type))
            {
                ToastService.ShowToast("Выберите тип изделия.", ToastType.Info);
                return;
            }

            string? color = CmbQuickColor.SelectedItem as string;
            int.TryParse(TxtQuickWidth.Text, out int width);
            int.TryParse(TxtQuickHeight.Text, out int height);
            int.TryParse(TxtQuickQty.Text, out int qty);
            if (qty <= 0) qty = 1;
            double.TryParse(TxtQuickPrice.Text, out double price);

            if (!OrderItem.ManualPieceProducts.Contains(type))
            {
                if (width <= 0) { ToastService.ShowToast("Укажите ширину.", ToastType.Info); TxtQuickWidth.Focus(); return; }
                if (height <= 0 && type != "Откос материал") { ToastService.ShowToast("Укажите высоту.", ToastType.Info); TxtQuickHeight.Focus(); return; }
            }

            var item = mw.CalcVM.AddItem(type, color ?? string.Empty, width, height, qty, price, SelectedAnwisMode);
            if (item != null)
            {
                // Record the catalog price for this Name+Color so IsPriceOverridden can detect manual edits.
                item.SetDefaultPrice(mw.PricesVM.GetPrice(type, color ?? string.Empty));
                item.RecalculateRequested += mw.UpdateTotal;
                mw.UpdateTotal();
                mw.MarkDirty();
            }

            TxtQuickWidth.Text = string.Empty;
            TxtQuickHeight.Text = string.Empty;
            TxtQuickQty.Text = "1";
            // For manual-piece products the user just entered a custom price — keep it.
            // For catalog-priced products, refresh the field with the latest catalog price.
            if (!OrderItem.ManualPieceProducts.Contains(type))
            {
                TxtQuickPrice.Text = MoneyFormatService.Format(mw.PricesVM.GetPrice(type ?? "", color ?? string.Empty));
            }
            CmbQuickType.Focus();
            UpdateQuickPreview();
        }

        private void UpdateQuickPreview()
        {
            if (!IsLoaded) return;

            string? type = CmbQuickType.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(type))
            {
                PreviewChip.Visibility = Visibility.Collapsed;
                return;
            }

            int.TryParse(TxtQuickWidth.Text, out int width);
            int.TryParse(TxtQuickHeight.Text, out int height);
            int.TryParse(TxtQuickQty.Text, out int qty);
            if (qty <= 0) qty = 1;
            double.TryParse(TxtQuickPrice.Text, out double price);

            double area = 0, total = 0;
            string unit = OrderItem.GetUnit(type);

            if (OrderItem.ManualPieceProducts.Contains(type)) { total = price * qty; }
            else if (OrderItem.AreaBasedProducts.Contains(type))
            {
                // For Anwis, show calc-adjusted dimensions in the preview.
                // Other area-based products use raw width/height.
                var anwisSize = AnwisSize.ОтВвода(width, height, SelectedAnwisMode);
                double previewW = Services.AnwisSizeService.IsApplicable(type)
                    ? anwisSize.ШиринаРасчёт
                    : width;
                double previewH = Services.AnwisSizeService.IsApplicable(type)
                    ? anwisSize.ВысотаРасчёт
                    : height;
                area = (previewW * previewH) / 1_000_000.0;
                total = area * price * qty;
            }
            else if (type == "ПСУЛ" || type == "Уплотнение") { area = (width + height) * 2 / 1000.0; total = area * price * qty; }
            // Unknown product — fall through to area-based to match OrderItem.Recalculate
            else { area = (width * height) / 1_000_000.0; total = area * price * qty; }

            PreviewChip.Visibility = Visibility.Visible;
            if (area > 0)
                TxtQuickPreview.Text = $"{area:F2} {unit} × {price:N2} руб × {qty} = {total:N2} руб";
            else
                TxtQuickPreview.Text = $"{price:N2} руб × {qty} = {total:N2} руб";
        }

        private void TxtQuickSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSearchSuggestions();
        }

        private void TxtQuickSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (!SearchPopup.IsOpen) return;
            if (e.Key == Key.Down)
            {
                if (SearchSuggestions.SelectedIndex < SearchSuggestions.Items.Count - 1)
                    SearchSuggestions.SelectedIndex++;
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                if (SearchSuggestions.SelectedIndex > 0)
                    SearchSuggestions.SelectedIndex--;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                // If the user hasn't navigated with arrows yet, pick the first suggestion.
                string? sel = SearchSuggestions.SelectedItem as string;
                if (sel == null && SearchSuggestions.Items.Count > 0)
                    sel = SearchSuggestions.Items[0] as string;

                if (sel != null)
                {
                    CmbQuickType.SelectedItem = sel;
                    SearchPopup.IsOpen = false;
                    TxtQuickSearch.Text = "";
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                SearchPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private void SearchSuggestions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void SearchSuggestions_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var dep = (DependencyObject)e.OriginalSource;
            while (dep != null && dep != SearchSuggestions)
            {
                if (dep is ListBoxItem item)
                {
                    CmbQuickType.SelectedItem = item.Content;
                    SearchPopup.IsOpen = false;
                    TxtQuickSearch.Text = "";
                    return;
                }
                dep = VisualTreeHelper.GetParent(dep);
            }
        }

        private void UpdateSearchSuggestions()
        {
            if (!TryGetMainWindow(nameof(UpdateSearchSuggestions), out var mw)) return;

            string searchText = TxtQuickSearch.Text?.Trim().ToLower() ?? "";

            if (string.IsNullOrEmpty(searchText))
            {
                SearchSuggestions.ItemsSource = mw.ProductNames;
                SearchSuggestions.SelectedIndex = -1;
                SearchPopup.IsOpen = true;
            }
            else
            {
                var filtered = mw.ProductNames
                    .Where(n => n.ToLower().Contains(searchText))
                    .ToList();

                SearchSuggestions.ItemsSource = filtered;
                SearchSuggestions.SelectedIndex = -1;
                SearchPopup.IsOpen = filtered.Count > 0;
            }

            // No SelectedIndex assignment here — the ListBox never steals
            // focus from the search TextBox. The user navigates with Up/Down
            // keys and confirms with Enter (which auto-selects the first item
            // if nothing is selected yet).
        }

        private void SelectAll_OnFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.Dispatcher.BeginInvoke(() => tb.SelectAll());
        }

        private void TxtQuickSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            // Close popup when search field loses focus (e.g. click outside)
            // Small delay to allow click on ListBoxItem to fire first
            Dispatcher.BeginInvoke(() => SearchPopup.IsOpen = false,
                System.Windows.Threading.DispatcherPriority.Background);
        }

        public void RefreshPreview() => UpdateQuickPreview();

        /// <summary>
        /// Right-click on the «Тип» dropdown opens a radio-style context menu
        /// for Anwis mode selection — zero workspace footprint, no visible pills.
        /// </summary>
        private void CmbQuickType_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!AnwisSizeService.IsApplicable(CmbQuickType.SelectedItem as string))
                return;

            e.Handled = true; // Suppress native ComboBox context menu

            var menu = AnwisContextMenuBuilder.Build(
                SelectedAnwisMode,
                mode =>
                {
                    SelectedAnwisMode = mode;
                    UpdateAnwisModeToolTip();
                    UpdateQuickPreview();
                },
                CmbQuickType);

            menu.IsOpen = true;
        }

        /// <summary>
        /// Resets Anwis mode to <see cref="AnwisSizeService.DefaultMode"/>
        /// and clears the ToolTip. Called by <see cref="MainWindow.StartNewOrder"/>
        /// and <see cref="MainWindow.OpenSelectedOrder"/> so the user always
        /// starts a fresh order with the default mode.
        /// </summary>
        public void ResetAnwisMode()
        {
            SelectedAnwisMode = AnwisSizeService.DefaultMode;
            UpdateAnwisModeToolTip();
        }

        /// <summary>
        /// Sets/clears the ToolTip on <see cref="CmbQuickType"/> to indicate
        /// the current Anwis mode and the right-click gesture — 0 px of workspace.
        /// </summary>
        private void UpdateAnwisModeToolTip()
        {
            if (!AnwisSizeService.IsApplicable(CmbQuickType.SelectedItem as string))
            {
                ToolTipService.SetToolTip(CmbQuickType, null);
                return;
            }
            ToolTipService.SetToolTip(CmbQuickType,
                $"Текущий режим: {AnwisSizeService.ShortLabels[SelectedAnwisMode]} (ПКМ для изменения)");
        }
    }
}
