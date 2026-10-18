using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// Окно «Шаблоны». Часть 3: Расчёт сумм (RefreshSummary), валидация и применение в заказ.
    /// </summary>
    public partial class TemplatesWindow
    {
        private static bool IsRowComplete(OrderTemplateService.TemplateRowState state)
        {
            if (!state.IsEnabled) return false;
            string prod = state.ResolveEffectiveProduct();
            bool isGrid = state.BaseProductName == OrderTemplateService.OrderTemplateGridProduct;

            if (isGrid)
                return state.Width > 0 && state.Height > 0 && state.Quantity > 0;
            if (prod == "ПСУЛ")
                return state.Quantity > 0;
            if (prod == "Доставка")
                return state.ManualAmount > 0;
            if (state.IsCustomProduct || ProductCatalog.IsManualPiece(prod))
                return state.ManualAmount > 0 && state.Quantity > 0;
            if (ProductCatalog.IsAreaBased(prod))
                return state.Width > 0 && state.Height > 0 && state.Quantity > 0;

            return state.Quantity > 0;
        }

        private void RefreshSummary()
        {
            if (_template == null) return;
            var catalogPrice = (string t, string c) => _mw?.PricesVM != null ? _mw.PricesVM.GetPrice(t, c) : 0;

            int readyCount = 0;
            int enabledCount = 0;
            double grandTotal = 0;

            foreach (var row in _template.Rows)
            {
                var rowState = _state.GetOrCreateRowState(row);
                double rowTotal = rowState.CalculateTotal(catalogPrice);
                bool isComplete = IsRowComplete(rowState);

                if (_rowPriceLabels.TryGetValue(row.RowKey, out var lbl))
                {
                    if (!rowState.IsEnabled)
                    {
                        lbl.Text = "—";
                        lbl.Opacity = 0.45;
                    }
                    else
                    {
                        lbl.Opacity = 1.0;
                        lbl.Text = rowTotal > 0 ? $"{MoneyFormatService.Format(rowTotal)} ₽" : "0 ₽";
                    }
                }

                if (_statusPills.TryGetValue(row.RowKey, out var pill))
                {
                    var textBlock = (TextBlock)pill.Child;
                    if (!rowState.IsEnabled)
                    {
                        textBlock.Text = "Не требуется";
                        textBlock.Foreground = (Brush)FindResource("TextMuted");
                        pill.Background = (Brush)FindResource("ChipBg");
                    }
                    else if (isComplete)
                    {
                        textBlock.Text = "Добавится";
                        textBlock.Foreground = (Brush)FindResource("BadgeSuccessFg");
                        pill.Background = (Brush)FindResource("BadgeSuccessBg");
                    }
                    else
                    {
                        textBlock.Text = "Не заполнено";
                        textBlock.Foreground = (Brush)FindResource("TextMuted");
                        pill.Background = (Brush)FindResource("ChipBg");
                    }
                }

                if (rowState.IsEnabled)
                {
                    enabledCount++;
                    if (isComplete)
                    {
                        readyCount++;
                        grandTotal += rowTotal;
                    }
                }
            }

            if (enabledCount == 0)
            {
                TxtSummaryCount.Text = "Позиции не выбраны";
                TxtApplyLabel.Text = "Добавить в заказ";
            }
            else if (readyCount == enabledCount)
            {
                TxtSummaryCount.Text = $"Добавится позиций: {readyCount}";
                TxtApplyLabel.Text = $"Добавить: {readyCount}";
            }
            else
            {
                TxtSummaryCount.Text = $"Заполнено: {readyCount} из {enabledCount}";
                TxtApplyLabel.Text = $"Добавить ({readyCount} из {enabledCount})";
            }

            TxtSummaryTotal.Text = grandTotal > 0 ? $"≈ {MoneyFormatService.Format(grandTotal)} ₽" : "";
            BtnApply.IsEnabled = true;
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (_template == null) return;

            string? error = _state.GetValidationError(_template);
            if (error != null)
            {
                HighlightValidationError();
                ToastService.ShowToast(error, ToastType.Info);
                return;
            }

            var catalogPrice = (string t, string c) => _mw?.PricesVM != null ? _mw.PricesVM.GetPrice(t, c) : 0;
            var specs = _state.BuildItemSpecs(_template, catalogPrice);
            if (specs.Count == 0)
            {
                ToastService.ShowToast("Выберите хотя бы одну позицию.", ToastType.Info);
                return;
            }

            // Атомарное добавление: один снимок Undo на весь шаблон
            _mw.PushUndo();

            var added = new List<OrderItem>();
            foreach (var spec in specs)
            {
                if (spec.IsCustomProduct)
                {
                    var customItem = _mw.CalcVM.AddItem(
                        spec.Type,
                        string.Empty,
                        0,
                        0,
                        spec.Quantity > 0 ? spec.Quantity : 1,
                        spec.Price);
                    if (customItem == null) continue;

                    customItem.IsCustomProduct = true;
                    customItem.SetDefaultPrice(spec.Price);
                    customItem.RecalculateRequested += _mw.RecalculateAndUpdateTotal;
                    added.Add(customItem);
                    continue;
                }

                var item = _mw.CalcVM.AddItem(
                    spec.Type, spec.Color, spec.Width, spec.Height,
                    spec.Quantity, spec.Price,
                    spec.AnwisMode ?? AnwisSizeMode.Брусбокс60);
                if (item == null) continue;
                added.Add(item);

                if (spec.AnwisMode.HasValue)
                    item.SetAnwisModeQuiet(spec.AnwisMode.Value);

                if (spec.Anticat && OrderItem.AnticatApplicableProducts.Contains(spec.Type))
                    item.IsAnticat = true;

                double defaultPrice = _mw.PricesVM.GetPrice(spec.Type, spec.Color);
                if (item.IsAnticat)
                    defaultPrice += OrderItem.AnticatSurcharge;
                item.SetDefaultPrice(defaultPrice);

                if (spec.InstallationMode.HasValue &&
                    ProductCatalog.IsInstallationApplicable(spec.Type))
                {
                    item.InstallationMode = spec.InstallationMode.Value;
                    // Для режима 2 («В конструкцию») наценка 0 руб.
                    if (spec.InstallationMode.Value == 2)
                        item.InstallationSurcharge = 0;
                }

                item.RecalculateRequested += _mw.RecalculateAndUpdateTotal;
            }

            _mw.RecalculateAndUpdateTotal();
            _mw.MarkDirty();

            ToastService.ShowToast($"Шаблон «{_template.Name}» добавлен — позиций: {added.Count}.", ToastType.Success);
            Close();
        }

        private void HighlightValidationError()
        {
            string? row = _state.LastErrorRow;
            if (row == null) return;
            string key = row;
            if (key.StartsWith("grid")) key = "grid";
            else if (key.StartsWith("otliv-size") || key.StartsWith("otliv")) key = "otliv";

            if (_widthBoxes.TryGetValue(key, out var w) && w != null) w.Tag = "Invalid";
            if (_heightBoxes.TryGetValue(key, out var h) && h != null) h.Tag = "Invalid";
            if (_qtyBoxes.TryGetValue(key, out var q) && q != null) q.Tag = "Invalid";
            if (_qtyBoxes.TryGetValue(row, out var q2) && q2 != null) q2.Tag = "Invalid";
            if (_amountBoxes.TryGetValue(key, out var a) && a != null) a.Tag = "Invalid";
            if (_amountBoxes.TryGetValue(row, out var a2) && a2 != null) a2.Tag = "Invalid";
        }
    }
}
