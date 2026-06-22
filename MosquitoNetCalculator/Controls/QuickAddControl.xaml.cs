using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

            // ToolTips with mode descriptions.
            ToolTipService.SetShowDuration(PillBB60, 15000);
            ToolTipService.SetShowDuration(PillBB70, 15000);
            ToolTipService.SetShowDuration(PillPP, 15000);
            ToolTipService.SetShowDuration(PillProem, 15000);
            ToolTipService.SetShowDuration(PillGab, 15000);
            PillBB60.ToolTip  = AnwisSizeService.HintTexts[AnwisSizeMode.Брусбокс60];
            PillBB70.ToolTip  = AnwisSizeService.HintTexts[AnwisSizeMode.Брусбокс70];
            PillPP.ToolTip    = AnwisSizeService.HintTexts[AnwisSizeMode.Профипласт];
            PillProem.ToolTip = AnwisSizeService.HintTexts[AnwisSizeMode.РазмерПроёма];
            PillGab.ToolTip   = AnwisSizeService.HintTexts[AnwisSizeMode.Габаритный];

            // Hover effects for segmented control segments.
            PillBB60.MouseEnter  += (_, _) => HoverSegment(PillBB60,  AnwisSizeMode.Брусбокс60);
            PillBB60.MouseLeave += (_, _) => UpdateAnwisModePills();
            PillBB70.MouseEnter  += (_, _) => HoverSegment(PillBB70,  AnwisSizeMode.Брусбокс70);
            PillBB70.MouseLeave += (_, _) => UpdateAnwisModePills();
            PillPP.MouseEnter    += (_, _) => HoverSegment(PillPP,    AnwisSizeMode.Профипласт);
            PillPP.MouseLeave   += (_, _) => UpdateAnwisModePills();
            PillProem.MouseEnter += (_, _) => HoverSegment(PillProem, AnwisSizeMode.РазмерПроёма);
            PillProem.MouseLeave += (_, _) => UpdateAnwisModePills();
            PillGab.MouseEnter   += (_, _) => HoverSegment(PillGab,   AnwisSizeMode.Габаритный);
            PillGab.MouseLeave  += (_, _) => UpdateAnwisModePills();
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

                // v3.35.0: show/hide Anwis mode pill panel with animation.
                ToggleAnwisModePanel(AnwisSizeService.IsApplicable(type));

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
        /// for Anwis mode selection — kept as fallback for experienced users.
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
                    UpdateAnwisModePills();
                    UpdateAnwisModeToolTip();
                    UpdateQuickPreview();
                },
                CmbQuickType);

            menu.IsOpen = true;
        }

        /// <summary>
        /// Left-click on an Anwis mode pill — sets the mode and highlights the pill.
        /// </summary>
        private void AnwisModePill_Click(object sender, MouseButtonEventArgs e)
        {
            var pill = sender as Border;
            if (pill == null) return;

            AnwisSizeMode? mode = pill.Name switch
            {
                nameof(PillBB60) => AnwisSizeMode.Брусбокс60,
                nameof(PillBB70) => AnwisSizeMode.Брусбокс70,
                nameof(PillPP)   => AnwisSizeMode.Профипласт,
                nameof(PillProem) => AnwisSizeMode.РазмерПроёма,
                nameof(PillGab)   => AnwisSizeMode.Габаритный,
                _ => null
            };

            if (!mode.HasValue) return;

            SelectedAnwisMode = mode.Value;
            UpdateAnwisModePills();
            UpdateAnwisModeToolTip();
            UpdateQuickPreview();

            e.Handled = true;
        }

        /// <summary>
        /// Shows or hides the Anwis mode pill panel with fade+slide animation.
        /// </summary>
        private void ToggleAnwisModePanel(bool show)
        {
            if (show)
            {
                // Cancel any in-progress hide animation before showing.
                PanelAnwisModes.BeginAnimation(OpacityProperty, null);
                var existingTransform = PanelAnwisModes.RenderTransform as TranslateTransform;
                if (existingTransform != null)
                    existingTransform.BeginAnimation(TranslateTransform.YProperty, null);

                PanelAnwisModes.Visibility = Visibility.Visible;
                PanelAnwisModes.Opacity = 0;
                PanelAnwisModes.RenderTransform = new TranslateTransform(0, -8);

                var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(250))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                PanelAnwisModes.BeginAnimation(OpacityProperty, fadeIn);

                var slideDown = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var t = (TranslateTransform)PanelAnwisModes.RenderTransform;
                t.BeginAnimation(TranslateTransform.YProperty, slideDown);

                UpdateAnwisModePills();
            }
            else
            {
                // Only animate out if currently visible.
                if (PanelAnwisModes.Visibility != Visibility.Visible)
                    return;

                var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                fadeOut.Completed += (_, _) =>
                {
                    PanelAnwisModes.Visibility = Visibility.Collapsed;
                };
                PanelAnwisModes.BeginAnimation(OpacityProperty, fadeOut);

                var slideUp = new DoubleAnimation(8, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var t = PanelAnwisModes.RenderTransform as TranslateTransform;
                if (t != null)
                    t.BeginAnimation(TranslateTransform.YProperty, slideUp);
            }
        }

        /// <summary>
        /// Updates the visual state of all Anwis mode segments to reflect
        /// <see cref="SelectedAnwisMode"/> — one active (Accent), rest transparent.
        /// Also hides separators adjacent to the active segment so the accent
        /// fill is not interrupted by divider lines.
        /// </summary>
        private void UpdateAnwisModePills()
        {
            var r = Application.Current.Resources;
            Brush activeBg = (Brush)r["Accent"];
            Brush activeFg = (Brush)r["OnAccent"];
            Brush inactiveFg = (Brush)r["TextSecondary"];
            Brush sepBrush = (Brush)r["SubtleBorder"];

            var pills = new[] { PillBB60, PillBB70, PillPP, PillProem, PillGab };
            var seps  = new[] { Sep1, Sep2, Sep3, Sep4 };
            var modes = new[]
            {
                AnwisSizeMode.Брусбокс60,
                AnwisSizeMode.Брусбокс70,
                AnwisSizeMode.Профипласт,
                AnwisSizeMode.РазмерПроёма,
                AnwisSizeMode.Габаритный
            };

            for (int i = 0; i < pills.Length; i++)
            {
                bool isActive = SelectedAnwisMode == modes[i];
                pills[i].Background = isActive ? activeBg : Brushes.Transparent;
                if (pills[i].Child is TextBlock tb)
                    tb.Foreground = isActive ? activeFg : inactiveFg;
            }

            // Hide separators adjacent to the active segment.
            for (int i = 0; i < seps.Length; i++)
            {
                // Sep[i] sits between pill[i] and pill[i+1] — hide if either neighbor is active.
                bool adjacent = SelectedAnwisMode == modes[i] || SelectedAnwisMode == modes[i + 1];
                seps[i].Fill = adjacent ? Brushes.Transparent : sepBrush;
            }

            // Visible hint below the segmented control.
            var (input, _, _) = AnwisSizeService.GetExplanation(SelectedAnwisMode);
            TxtAnwisModeHint.Text = input;
        }

        /// <summary>
        /// Applies hover styling to a segment — AccentHover for the active
        /// segment, AccentLight for inactive ones.
        /// </summary>
        private void HoverSegment(Border pill, AnwisSizeMode mode)
        {
            var r = Application.Current.Resources;
            bool isActive = SelectedAnwisMode == mode;
            pill.Background = isActive
                ? (Brush)r["AccentHover"]
                : (Brush)r["AccentLight"];
            if (pill.Child is TextBlock tb)
                tb.Foreground = isActive
                    ? (Brush)r["OnAccent"]
                    : (Brush)r["TextPrimary"];
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
            UpdateAnwisModePills();
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
