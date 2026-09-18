using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MosquitoNetCalculator.Helpers
{
    internal static class DataGridColumnAutoSizer
    {
        /// <summary>Горизонтальный паддинг шапки: 10px слева + 10px справа.</summary>
        public const double HeaderPadding = 20;

        /// <summary>
        /// Размер подписи шапки (токен <c>Type.Caption</c>). Держать в паре с
        /// <c>DataGridColumnHeader</c> в <c>Themes/DataGridStyles.xaml</c>.
        /// </summary>
        public const double HeaderFontSize = 11;

        /// <summary>
        /// Объявленный в разметке <c>MinWidth</c>, снятый при ПЕРВОМ обращении
        /// к колонке. Дальше он служит полом: автосайзер может минимум поднять,
        /// но не опустить ниже разметки (почему — см. GOTCHAS §34). Запоминание
        /// первого значения (а не перечитывание текущего) защищает от храповика:
        /// после длинного содержимого минимум возвращается к объявленному.
        /// </summary>
        private static readonly ConditionalWeakTable<DataGridColumn, StrongBox<double>> DeclaredMinWidths = new();

        private static double DeclaredMinWidth(DataGridColumn col) =>
            DeclaredMinWidths.GetValue(col, c => new StrongBox<double>(c.MinWidth)).Value;

        /// <summary>
        /// Ширина подписи шапки ровно тем инструментом, которым пользуется
        /// <see cref="SetColumnMinWidth"/>. Нужна стражам: страж обязан мерить
        /// шапку тем же инструментом, что и колонка, иначе они разъедутся и
        /// страж будет молчать про реальную обрезку.
        /// </summary>
        public static double MeasureHeaderWidth(DataGrid grid, string headerText)
        {
            if (grid == null || string.IsNullOrEmpty(headerText)) return 0;
            double dpi = VisualTreeHelper.GetDpi(grid).PixelsPerDip;
            var typeface = new Typeface(grid.FontFamily, grid.FontStyle,
                FontWeights.SemiBold, grid.FontStretch);
            return GetMaxTextWidth(new[] { headerText }, typeface, HeaderFontSize, dpi);
        }

        public static void SetColumnMinWidth(
            DataGrid grid,
            DataGridColumn? col,
            string headerText,
            IEnumerable<string>? cellValues = null,
            double headerPad = 20,
            double contentPad = 16,
            FontWeight? contentWeight = null,
            double? contentFontSize = null,
            double? contentCap = null)
        {
            if (col == null || grid == null) return;

            double dpi = VisualTreeHelper.GetDpi(grid).PixelsPerDip;
            double fontSize = grid.FontSize > 0 ? grid.FontSize : 12;
            double headerFontSize = HeaderFontSize;

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
                // cap: контент длиннее капа не поднимает минимум (переносится
                // в ячейке или остаётся шире минимума — Auto-колонка всё равно
                // покажет его целиком, если вьюпорт позволяет).
                if (contentCap is double cap && contentW > cap) contentW = cap;
                if (contentW > minWidth) minWidth = contentW;
            }

            // Флор: MinWidth из разметки — не «значение по умолчанию», а
            // подобранная с запасом ширина, при которой подпись шапки
            // (11px SemiBold + паддинг 20) не режется. Замер FormattedText
            // ложится ВПРИТЫК, поэтому присваивание вычисленного минимума
            // «как есть» обнуляло запас: ужатая окном колонка показывала
            // «№…» и «СУММА, Р…» (GOTCHAS §34). Объявленный минимум не
            // пробиваем.
            double declaredMin = DeclaredMinWidth(col);
            col.MinWidth = minWidth > declaredMin ? minWidth : declaredMin;
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