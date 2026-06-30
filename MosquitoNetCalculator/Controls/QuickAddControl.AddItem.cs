using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    public partial class QuickAddControl
    {
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
                bool widthEnabled = !isManualPiece || OrderItem.WidthOnlyProducts.Contains(type);
                bool heightEnabled = !isManualPiece;
                TxtQuickWidth.IsEnabled = widthEnabled;
                TxtQuickHeight.IsEnabled = heightEnabled;
                TxtQuickQty.IsEnabled = !isAmountOnly;
                if (!widthEnabled)
                    TxtQuickWidth.Text = string.Empty;
                if (!heightEnabled)
                    TxtQuickHeight.Text = string.Empty;
                if (isAmountOnly)
                    TxtQuickQty.Text = "1";

                // v3.35.0: show/hide Anwis mode pill panel with animation.
                ToggleAnwisModePanel(AnwisSizeService.IsApplicable(type));

                // Anti-cat toggle button visibility
                UpdateAnticatToggleState(type, TbtnAnticat);

                // Anwis mode ToolTip on the Type dropdown — 0 px of workspace.
                UpdateAnwisModeToolTip();
            }
            finally { _updatingQuickCombo = false; }
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
                bool isAnticatApplicable = OrderItem.AnticatApplicableProducts.Contains(type);
                item.IsAnticat = isAnticatApplicable && TbtnAnticat.IsChecked == true;

                // Record the catalog price (with surcharge if applicable) so
                // IsPriceOverridden can detect manual edits correctly.
                double defaultPrice = mw.PricesVM.GetPrice(type, color ?? string.Empty);
                if (item.IsAnticat)
                    defaultPrice += OrderItem.AnticatSurcharge;
                item.SetDefaultPrice(defaultPrice);

                item.RecalculateRequested += mw.UpdateTotal;
                mw.UpdateTotal();
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
        }
    }
}
