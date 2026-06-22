using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MosquitoNetCalculator.Helpers
{
    internal static class DataGridColumnAutoSizer
    {
        public static void SetColumnMinWidth(
            DataGrid grid,
            DataGridColumn? col,
            string headerText,
            IEnumerable<string>? cellValues = null,
            double headerPad = 20,
            double contentPad = 16,
            FontWeight? contentWeight = null,
            double? contentFontSize = null)
        {
            if (col == null || grid == null) return;

            double dpi = VisualTreeHelper.GetDpi(grid).PixelsPerDip;
            double fontSize = grid.FontSize > 0 ? grid.FontSize : 12;
            double headerFontSize = 11;

            var headerTypeface = new Typeface(grid.FontFamily, grid.FontStyle,
                FontWeights.SemiBold, grid.FontStretch);
            double minWidth = string.IsNullOrEmpty(headerText)
                ? headerPad
                : GetMaxTextWidth(new[] { headerText }, headerTypeface, headerFontSize, dpi) + headerPad;

            if (cellValues != null)
            {
                double cSize = contentFontSize ?? fontSize;
                var cTypeface = new Typeface(grid.FontFamily, grid.FontStyle,
                    contentWeight ?? grid.FontWeight, grid.FontStretch);
                double contentW = GetMaxTextWidth(cellValues, cTypeface, cSize, dpi) + contentPad;
                if (contentW > minWidth) minWidth = contentW;
            }

            col.MinWidth = minWidth;
        }

        public static DataGridColumn? FindCol(DataGrid grid, string headerText)
        {
            return grid.Columns.FirstOrDefault(c =>
                StripSortIndicator(c.Header?.ToString()) == headerText);
        }

        public static string StripSortIndicator(string? headerText)
        {
            return (headerText ?? "")
                .Replace(" \u25B2", "")
                .Replace(" \u25BC", "");
        }

        private static double GetMaxTextWidth(IEnumerable<string> texts, Typeface typeface, double fontSize, double pixelsPerDip)
        {
            double max = 0;
            foreach (var text in texts)
            {
                if (string.IsNullOrEmpty(text)) continue;
                var ft = new FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    Brushes.Black,
                    pixelsPerDip);
                if (ft.Width > max) max = ft.Width;
            }
            return max;
        }
    }
}