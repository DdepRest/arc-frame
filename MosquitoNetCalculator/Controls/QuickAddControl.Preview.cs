using System;
using System.Windows;
using System.Windows.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    public partial class QuickAddControl
    {
        private void UpdateQuickPreview()
        {
            if (!IsLoaded) return;

            string? type = CmbQuickType.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(type))
            {
                PreviewChip.Visibility = Visibility.Collapsed;
                return;
            }

            TryParseQuickNumber(TxtQuickWidth.Text, out double width);
            TryParseQuickNumber(TxtQuickHeight.Text, out double height);
            double qty;
            TryParseQuickNumber(TxtQuickQty.Text, out qty);
            if (qty <= 0) qty = 1;
            TryParseQuickNumber(TxtQuickPrice.Text, out double price);

            double area = 0, total = 0;
            string unit = IsCustomProductType(type) ? "шт." : OrderItem.GetUnit(type);
            // Calculated dimensions (Anwis: after mode correction; others — as entered).
            double previewW = width, previewH = height;

            if (IsCustomProductType(type)) { total = price * qty; unit = "шт."; }
            else if (OrderItem.AmountOnlyProducts.Contains(type)) { total = price; }
            else if (OrderItem.ManualPieceProducts.Contains(type)) { total = price * qty; }
            else if (OrderItem.AreaBasedProducts.Contains(type))
            {
                // For products supporting size modes (Anwis, На навесах, Оконная на метал.),
                // show calc-adjusted dimensions in the preview — same
                // rounding as OrderItem.Recalculate (3 decimals) so preview and
                // the added row produce the SAME total.
                var anwisSize = AnwisSize.ОтВвода(width, height, SelectedAnwisMode, type);
                previewW = Services.AnwisSizeService.SupportsSizeMode(type)
                    ? anwisSize.ШиринаРасчёт
                    : width;
                previewH = Services.AnwisSizeService.SupportsSizeMode(type)
                    ? anwisSize.ВысотаРасчёт
                    : height;
                area = Math.Round((previewW * previewH) / 1_000_000.0, 3);
                total = area * price * qty;

                // Импост — by CALCULATED dimensions (previewW/previewH already
                // Anwis-corrected), single helper shared with OrderItem.Recalculate.
                if (OrderItem.ImpostApplies(type, previewW, previewH))
                    total += OrderItem.ImpostSurchargeFor(previewW, qty);
            }
            else if (type == "ПСУЛ")
            {
                if (width == 0 && height == 0) { area = 0; total = qty * 100; }
                else { area = (width + height) * 2 / 1000.0; total = area * price * qty; }
            }
            else if (type == "Уплотнение") { if (width == 0 && height == 0) { area = 0; total = qty * price; } else { area = (width + height) * 2 / 1000.0; total = area * price * qty; } }
            // Unknown product — fall through to area-based to match OrderItem.Recalculate
            else { area = (width * height) / 1_000_000.0; total = area * price * qty; }

            PreviewChip.Visibility = Visibility.Visible;
            double impost = OrderItem.ImpostApplies(type, previewW, previewH)
                ? OrderItem.ImpostSurchargeFor(previewW, qty)
                : 0;

            if (OrderItem.AmountOnlyProducts.Contains(type))
                TxtQuickPreview.Text = $"{price:N2} руб";
            else if (area > 0)
            {
                string impostPart = impost > 0 ? $" + Импост {impost:N2} руб" : "";
                TxtQuickPreview.Text = $"{area:F2} {unit} × {price:N2} руб × {qty}{impostPart} = {total:N2} руб";
            }
            else if (IsAreaPriced(type))
            {
                // Размеры ещё не введены: площадь = 0, и формула «ставка × 1 = 0,00»
                // была бы бессмысленной. Показываем саму ставку — это ровно то,
                // что пользователь сейчас выбирает, и избавляет от чтения нуля.
                TxtQuickPreview.Text = $"{price:N2} руб/{unit}";
            }
            else
                TxtQuickPreview.Text = $"{price:N2} руб × {qty} = {total:N2} руб";
        }

        /// <summary>
        /// True when the position's total is computed from area (or from the same
        /// fall-through path as <see cref="OrderItem.Recalculate"/> treats unknown
        /// products) — i.e. with no dimensions the total is legitimately 0 and the
        /// preview should show the unit rate instead of a zero-sum formula.
        /// </summary>
        private static bool IsAreaPriced(string type) =>
            !IsCustomProductType(type)
            && !OrderItem.AmountOnlyProducts.Contains(type)
            && !OrderItem.ManualPieceProducts.Contains(type)
            && type != "ПСУЛ"
            && type != "Уплотнение";

        public void RefreshPreview() => UpdateQuickPreview();
    }
}
