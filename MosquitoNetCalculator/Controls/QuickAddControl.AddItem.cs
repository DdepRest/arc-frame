using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    public partial class QuickAddControl
    {
        // UX#3: Show guidance toast after first successful item addition
        private bool _firstItemAdded;
        private static bool IsCustomProductType(string? type)
            => type == AiClarificationForm.CustomProductType;

        private void CmbQuickType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingQuickCombo) return;
            if (!TryGetMainWindow(nameof(CmbQuickType_SelectionChanged), out var mw)) return;
            if (CmbQuickType.SelectedItem is not string type || string.IsNullOrWhiteSpace(type)) return;

            _updatingQuickCombo = true;
            try
            {
                bool isCustom = IsCustomProductType(type);
                PanelColor.Visibility = isCustom ? Visibility.Collapsed : Visibility.Visible;
                PanelCustomName.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

                if (isCustom)
                {
                    // Custom product: no color, all numeric fields start EMPTY and
                    // optional (dims, qty, price are manual). Don't let a previous
                    // product's width/height/qty leak into the «Свой товар» row.
                    CmbQuickColor.IsEnabled = false;
                    CmbQuickColor.Items.Clear();
                    TxtQuickPrice.Text = string.Empty;
                    TxtQuickWidth.Text = string.Empty;
                    TxtQuickHeight.Text = string.Empty;
                    TxtQuickQty.Text = string.Empty;
                    TxtQuickWidth.IsEnabled = true;
                    TxtQuickHeight.IsEnabled = true;
                    TxtQuickQty.IsEnabled = true;
                    ToggleAnwisModePanel(false);
                    UpdateAnticatToggleState(type, TbtnAnticat);
                    if (string.IsNullOrWhiteSpace(TxtCustomName.Text))
                        TxtCustomName.Focus();
                }
                else
                {
                    var colors = mw.PricesVM.GetColorsForProduct(type);
                    bool noColor = OrderItem.NoColorProducts.Contains(type);
                    CmbQuickColor.IsEnabled = !noColor;
                    CmbQuickColor.Items.Clear();
                    if (noColor)
                    {
                        UpdateQuickPrice(type, string.Empty, mw);
                    }
                    else if (colors.Any())
                    {
                        foreach (var c in colors) CmbQuickColor.Items.Add(c);
                        CmbQuickColor.SelectedIndex = 0;
                        UpdateQuickPrice(type, colors[0], mw);
                    }
                    else
                    {
                        UpdateQuickPrice(type, string.Empty, mw);
                    }

                    bool isManualPiece = OrderItem.ManualPieceProducts.Contains(type);
                    bool isAmountOnly = OrderItem.AmountOnlyProducts.Contains(type);
                    bool isQuantityOptional = OrderItem.OptionalQuantityProducts.Contains(type);
                    bool widthEnabled = !isManualPiece || OrderItem.WidthOnlyProducts.Contains(type);
                    bool heightEnabled = !isManualPiece;
                    TxtQuickWidth.IsEnabled = widthEnabled;
                    TxtQuickHeight.IsEnabled = heightEnabled;
                    TxtQuickQty.IsEnabled = !isAmountOnly;
                    if (!widthEnabled)
                    {
                        TxtQuickWidth.Text = string.Empty;
                        ClearRequiredHighlight(TxtQuickWidth);
                    }
                    if (!heightEnabled)
                    {
                        TxtQuickHeight.Text = string.Empty;
                        ClearRequiredHighlight(TxtQuickHeight);
                    }
                    if (isAmountOnly)
                        TxtQuickQty.Text = "1";
                    else if (isQuantityOptional && TxtQuickQty.Text == "1")
                    {
                        TxtQuickQty.Text = "1";
                    }

                    // v3.35.0: show/hide Anwis mode pill panel with animation.
                    ToggleAnwisModePanel(AnwisSizeService.IsApplicable(type));

                    // Anti-cat toggle button visibility
                    UpdateAnticatToggleState(type, TbtnAnticat);

                    // Anwis mode ToolTip on the Type dropdown — 0 px of workspace.
                    UpdateAnwisModeToolTip();
                }
            }
            finally { _updatingQuickCombo = false; }
            HighlightRequiredIfEmpty();
            UpdateQuickPreview();
        }

        /// <summary>
        /// Updates the visibility and checked state of the anti-cat toggle button
        /// based on whether the selected product type supports the anti-cat option.
        /// </summary>
        internal static void UpdateAnticatToggleState(string selectedType, System.Windows.Controls.Primitives.ToggleButton btnAnticat)
        {
            bool isAnticatApplicable = OrderItem.AnticatApplicableProducts.Contains(selectedType);
            btnAnticat.Visibility = isAnticatApplicable ? Visibility.Visible : Visibility.Collapsed;
            if (!isAnticatApplicable)
                btnAnticat.IsChecked = false;
        }

        private void CmbQuickColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingQuickCombo) return;
            if (!TryGetMainWindow(nameof(CmbQuickColor_SelectionChanged), out var mw)) return;
            if (CmbQuickType.SelectedItem is string type && CmbQuickColor.SelectedItem is string color)
            {
                UpdateQuickPrice(type, color, mw);
            }
            UpdateQuickPreview();
        }

        /// <summary>
        /// Loads the catalog price for the given product+color and applies the anti-cat
        /// surcharge when the toggle button is checked.
        /// </summary>
        private void UpdateQuickPrice(string type, string color, MainWindow mw)
        {
            var price = mw.PricesVM.GetPrice(type, color);
            if (TbtnAnticat.IsChecked == true && OrderItem.AnticatApplicableProducts.Contains(type))
                price += OrderItem.AnticatSurcharge;
            TxtQuickPrice.Text = price > 0 ? MoneyFormatService.Format(price) : string.Empty;
        }

        private void TbtnAnticat_Click(object sender, RoutedEventArgs e)
        {
            if (_updatingQuickCombo) return;
            if (!TryGetMainWindow(nameof(TbtnAnticat_Click), out var mw)) return;
            if (CmbQuickType.SelectedItem is string type)
            {
                string? color = CmbQuickColor.SelectedItem as string;
                if (string.IsNullOrEmpty(color) || !CmbQuickColor.IsEnabled)
                    color = string.Empty;
                UpdateQuickPrice(type, color, mw);
            }
            UpdateQuickPreview();
        }

        private void TxtQuickPrice_LostFocus(object sender, RoutedEventArgs e)
        {
            if (TryParseQuickNumber(TxtQuickPrice.Text, out double price))
                TxtQuickPrice.Text = MoneyFormatService.Format(price);
            // Also re-apply required-field highlight on Price
            SetRequiredHighlight(TxtQuickPrice);
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

            bool isCustom = IsCustomProductType(type);
            if (isCustom)
            {
                string customName = TxtCustomName.Text.Trim();
                if (string.IsNullOrWhiteSpace(customName))
                {
                    ToastService.ShowToast("Введите название своего товара.", ToastType.Info);
                    TxtCustomName.Focus();
                    return;
                }
                TryParseQuickNumber(TxtQuickWidth.Text, out double cw);
                TryParseQuickNumber(TxtQuickHeight.Text, out double ch);
                // Qty is OPTIONAL for «Свой товар»: empty field → 0 (no default 1),
                // the row shows the manual sum (Price) instead of Price × 0.
                TryParseQuickNumber(TxtQuickQty.Text, out double cqty);
                TryParseQuickNumber(TxtQuickPrice.Text, out double cprice);

                // Dimensions are OPTIONAL and left empty (0) when not entered —
                // never substituted with 1×1. Calculation is qty × price only.
                var item = mw.CalcVM.AddItem(customName, string.Empty, (int)cw, (int)ch, cqty, cprice, SelectedAnwisMode);
                if (item != null)
                {
                    item.IsCustomProduct = true;
                    if (cqty <= 0) item.Quantity = 0; // re-assert 0 so the flag-aware setter keeps it
                    item.RecalculateRequested += mw.RecalculateAndUpdateTotal;
                    mw.RecalculateAndUpdateTotal();
                    mw.MarkDirty();
                }

                TxtCustomName.Text = string.Empty;
                TxtQuickWidth.Text = string.Empty;
                TxtQuickHeight.Text = string.Empty;
                TxtQuickQty.Text = "";
                TxtQuickPrice.Text = string.Empty;
                CmbQuickType.Focus();
                UpdateQuickPreview();

                if (!_firstItemAdded && mw.OrderItems.Count >= 1)
                {
                    _firstItemAdded = true;
                    ToastService.ShowToast("\u2705  Отлично! Заполните данные заказчика (кнопка \u00ABЗаказчик\u00BB) и нажмите Сохранить (Ctrl+S)", ToastType.Success, durationMs: 5000);
                }
                _ = AnimateAddSuccess();
                return;
            }

            string? color = CmbQuickColor.SelectedItem as string;
            TryParseQuickNumber(TxtQuickWidth.Text, out double width);
            TryParseQuickNumber(TxtQuickHeight.Text, out double height);
            double qty;
            TryParseQuickNumber(TxtQuickQty.Text, out qty);
            if (qty <= 0) qty = 1;
            TryParseQuickNumber(TxtQuickPrice.Text, out double price);

            if (!OrderItem.ManualPieceProducts.Contains(type))
            {
                if (width <= 0 && type != "ПСУЛ" && type != "Уплотнение") { ToastService.ShowToast("Укажите ширину.", ToastType.Info); TxtQuickWidth.Focus(); return; }
                if (height <= 0 && type != "ПСУЛ" && type != "Уплотнение") { ToastService.ShowToast("Укажите высоту.", ToastType.Info); TxtQuickHeight.Focus(); return; }
            }

            // Price (sum) is mandatory for manual-piece products where the user
            // explicitly enters the total. Without it the row has no meaning.
            if (OrderItem.OptionalQuantityProducts.Contains(type) && price <= 0)
            {
                ToastService.ShowToast("Укажите сумму.", ToastType.Info);
                TxtQuickPrice.Focus();
                SetRequiredHighlight(TxtQuickPrice);
                return;
            }

            var item2 = mw.CalcVM.AddItem(type, color ?? string.Empty, (int)width, (int)height, qty, price, SelectedAnwisMode);
            if (item2 != null)
            {
                bool isAnticatApplicable = OrderItem.AnticatApplicableProducts.Contains(type);
                item2.IsAnticat = isAnticatApplicable && TbtnAnticat.IsChecked == true;

                // Record the catalog price (with surcharge if applicable) so
                // IsPriceOverridden can detect manual edits correctly.
                double defaultPrice = mw.PricesVM.GetPrice(type, color ?? string.Empty);
                if (item2.IsAnticat)
                    defaultPrice += OrderItem.AnticatSurcharge;
                item2.SetDefaultPrice(defaultPrice);

                item2.RecalculateRequested += mw.RecalculateAndUpdateTotal;
                mw.RecalculateAndUpdateTotal();
                mw.MarkDirty();
            }

            TxtQuickWidth.Text = string.Empty;
            TxtQuickHeight.Text = string.Empty;
            TxtQuickQty.Text = "1";
            TbtnAnticat.IsChecked = false;
            // For manual-piece products the user just entered a custom price — keep it.
            // For catalog-priced products, refresh the field with the latest catalog price.
            if (!OrderItem.ManualPieceProducts.Contains(type))
            {
                TxtQuickPrice.Text = MoneyFormatService.Format(mw.PricesVM.GetPrice(type ?? "", color ?? string.Empty));
            }
            CmbQuickType.Focus();
            UpdateQuickPreview();

            // UX#3: First-time guidance toast
            if (!_firstItemAdded && mw.OrderItems.Count >= 1)
            {
                _firstItemAdded = true;
                ToastService.ShowToast("\u2705  Отлично! Заполните данные заказчика (кнопка \u00ABЗаказчик\u00BB) и нажмите Сохранить (Ctrl+S)", ToastType.Success, durationMs: 5000);
            }

            // Fluent success micro-interaction: briefly flash the Add button green
            // for peripheral confirmation — the user doesn't need to glance at the grid.
            _ = AnimateAddSuccess();
        }

        /// <summary>
        /// Fluent micro-interaction: swaps the Add button to SuccessButton style for ~700 ms
        /// after a successful item addition, then reverts. Best-effort — never throws.
        /// </summary>
        private async Task AnimateAddSuccess()
        {
            try
            {
                var btn = BtnAdd;
                if (btn == null || Application.Current == null) return;

                var originalStyle = btn.Style;
                var originalContent = btn.Content;

                btn.Style = (Style)Application.Current.FindResource("SuccessButton");
                btn.Content = new TextBlock
                {
                    Text = "✔  Добавлено",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.FindResource("OnSuccess")
                };

                await Task.Delay(700);

                btn.Style = originalStyle;
                btn.Content = originalContent;
            }
            catch { /* best-effort cosmetic animation, never throw */ }
        }
    }
}
