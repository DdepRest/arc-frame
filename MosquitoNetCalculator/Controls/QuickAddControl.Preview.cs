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

            int.TryParse(TxtQuickWidth.Text, out int width);
            int.TryParse(TxtQuickHeight.Text, out int height);
            int.TryParse(TxtQuickQty.Text, out int qty);
            if (qty <= 0) qty = 1;
            double.TryParse(TxtQuickPrice.Text, out double price);

            double area = 0, total = 0;
            string unit = OrderItem.GetUnit(type);

            if (OrderItem.AmountOnlyProducts.Contains(type)) { total = price; }
            else if (OrderItem.ManualPieceProducts.Contains(type)) { total = price * qty; }
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
            else if (type == "ПСУЛ")
            {
                if (width == 0 && height == 0) { area = 0; total = qty * 100; }
                else { area = (width + height) * 2 / 1000.0; total = area * price * qty; }
            }
            else if (type == "Уплотнение") { if (width == 0 && height == 0) { area = 0; total = qty * price; } else { area = (width + height) * 2 / 1000.0; total = area * price * qty; } }
            // Unknown product — fall through to area-based to match OrderItem.Recalculate
            else { area = (width * height) / 1_000_000.0; total = area * price * qty; }

            PreviewChip.Visibility = Visibility.Visible;
            if (OrderItem.AmountOnlyProducts.Contains(type))
                TxtQuickPreview.Text = $"{price:N2} руб";
            else if (area > 0)
                TxtQuickPreview.Text = $"{area:F2} {unit} × {price:N2} руб × {qty} = {total:N2} руб";
            else
                TxtQuickPreview.Text = $"{price:N2} руб × {qty} = {total:N2} руб";
        }

        public void RefreshPreview() => UpdateQuickPreview();
    }
}
