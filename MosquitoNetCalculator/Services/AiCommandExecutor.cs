using System;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.ViewModels;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Executes a single AI command against the real calculation view-model.
    /// WPF-free and dependency-injected so the atomicity and toast-suppression
    /// behaviour is unit-testable without spinning up <c>MainWindow</c>.
    /// </summary>
    public sealed class AiCommandExecutor
    {
        private readonly CalculationViewModel _calcVM;
        private readonly Action _pushUndo;
        private readonly Action _markDirty;
        private readonly Action _recalculateAndUpdateTotal;
        private readonly Action<string, ToastType> _showToast;
        private readonly Func<bool> _isAiOverlayVisible;
        private readonly Action _closeAiAssistant;
        private readonly Action<int, int, int, int> _openSlopeOverlay;
        private readonly Action<string, string, string, string>? _setClientInfo;
        private readonly Action? _saveOrder;
        private readonly Action? _printKp;
        private readonly Action? _exportPdf;
        private readonly Func<string, string, double>? _getCatalogPrice;

        public AiCommandExecutor(
            CalculationViewModel calcVM,
            Action pushUndo,
            Action markDirty,
            Action recalculateAndUpdateTotal,
            Action<string, ToastType> showToast,
            Func<bool> isAiOverlayVisible,
            Action closeAiAssistant,
            Action<int, int, int, int> openSlopeOverlay,
            Action<string, string, string, string>? setClientInfo = null,
            Action? saveOrder = null,
            Action? printKp = null,
            Action? exportPdf = null,
            Func<string, string, double>? getCatalogPrice = null)
        {
            _calcVM = calcVM ?? throw new ArgumentNullException(nameof(calcVM));
            _pushUndo = pushUndo ?? throw new ArgumentNullException(nameof(pushUndo));
            _markDirty = markDirty ?? throw new ArgumentNullException(nameof(markDirty));
            _recalculateAndUpdateTotal = recalculateAndUpdateTotal ?? throw new ArgumentNullException(nameof(recalculateAndUpdateTotal));
            _showToast = showToast ?? throw new ArgumentNullException(nameof(showToast));
            _isAiOverlayVisible = isAiOverlayVisible ?? throw new ArgumentNullException(nameof(isAiOverlayVisible));
            _closeAiAssistant = closeAiAssistant ?? throw new ArgumentNullException(nameof(closeAiAssistant));
            _openSlopeOverlay = openSlopeOverlay ?? throw new ArgumentNullException(nameof(openSlopeOverlay));
            _setClientInfo = setClientInfo;
            _saveOrder = saveOrder;
            _printKp = printKp;
            _exportPdf = exportPdf;
            _getCatalogPrice = getCatalogPrice;
        }

        /// <summary>
        /// Executes one AI command against the real calculation. When
        /// <paramref name="pushUndo"/> is false (batch plan steps) the caller
        /// owns the single undo snapshot, and per-step toasts are suppressed so
        /// a later failing step cannot leave a misleading «Добавлено» behind.
        /// Returns success + a user-facing error.
        /// </summary>
        public bool Execute(AiCommand command, bool pushUndo, out string? error)
        {
            error = null;
            try
            {
                switch (command.Type)
                {
                    case AiCommandType.AddItem:
                    {
                        if (pushUndo) _pushUndo();
                        var item = _calcVM.AddItem(
                            command.Params.Type,
                            command.Params.Color,
                            command.Params.Width,
                            command.Params.Height,
                            command.Params.Quantity,
                            command.Params.Price,
                            command.Params.AnwisMode);
                        if (item == null)
                        {
                            error = "Не удалось добавить позицию: неверные параметры.";
                            return false;
                        }

                        // «Свой товар» marker → manual-sum semantics (qty × price,
                        // optional dims/qty) before the row is wired to the grid.
                        if (command.Params.IsCustomProduct)
                        {
                            item.IsCustomProduct = true;
                            // AddItem forces qty→1; an unspecified qty (0) must stay 0
                            // so the row shows the manual sum, not Price × 1.
                            if (command.Params.Quantity <= 0)
                                item.Quantity = 0;
                        }

                        // Apply the installation mode the user asked for
                        // (0 = монтаж включён, 1 = без монтажа, 2 = в конструкцию).
                        // −1 means the user didn't mention it — the program's
                        // own default (from CalcVM.AddItem) is kept.
                        if (command.Params.InstallationMode >= 0)
                            item.InstallationMode = command.Params.InstallationMode;
                        if (command.Params.IsAnticat.HasValue)
                            item.IsAnticat = command.Params.IsAnticat.Value;
                        item.RecalculateRequested += _recalculateAndUpdateTotal;
                        _markDirty();
                        // Batch plans report ONE summary after the whole plan succeeds;
                        // a per-step toast here would claim «Добавлено» even when a later
                        // step fails and the executor rolls this add back.
                        if (pushUndo)
                            _showToast($"✅ Добавлено: {item.Name} {item.Color}", ToastType.Success);
                        _recalculateAndUpdateTotal();
                        return true;
                    }

                    case AiCommandType.DeleteLast:
                    {
                        if (_calcVM.OrderItems.Count == 0)
                        {
                            error = "Заказ пуст — удалять нечего.";
                            return false;
                        }
                        if (pushUndo) _pushUndo();
                        var last = _calcVM.OrderItems[^1];
                        last.RecalculateRequested -= _recalculateAndUpdateTotal;
                        _calcVM.DeleteItem(last);
                        _markDirty();
                        if (pushUndo)
                            _showToast("🗑 Последняя позиция удалена", ToastType.Info);
                        _recalculateAndUpdateTotal();
                        return true;
                    }

                    case AiCommandType.ClearAll:
                    {
                        if (pushUndo) _pushUndo();
                        _calcVM.UnsubscribeAll(_recalculateAndUpdateTotal);
                        _calcVM.ClearAll();
                        _markDirty();
                        if (pushUndo)
                            _showToast("🗑 Расчёт очищен", ToastType.Info);
                        _recalculateAndUpdateTotal();
                        return true;
                    }

                    case AiCommandType.ListProducts:
                        // The AI already listed products in its reply.
                        return true;

                    case AiCommandType.DeleteItems:
                    {
                        if (pushUndo) _pushUndo();
                        int deleted = 0;
                        for (int i = _calcVM.OrderItems.Count - 1; i >= 0; i--)
                        {
                            var oi = _calcVM.OrderItems[i];
                            if (!MatchesTarget(oi, command.Params.TargetProduct)) continue;
                            oi.RecalculateRequested -= _recalculateAndUpdateTotal;
                            _calcVM.DeleteItem(oi);
                            deleted++;
                        }
                        _markDirty();
                        _recalculateAndUpdateTotal();
                        if (pushUndo)
                            _showToast($"🗑 Удалено позиций: {deleted}", ToastType.Info);
                        return true;
                    }

                    case AiCommandType.CalcSlope:
                    {
                        // Z-order guard (IN-PANEL mode only): AiOverlay is declared
                        // AFTER SlopeOverlay in MainWindow.xaml at the same
                        // Panel.ZIndex=15, so in maximized/in-panel mode it would
                        // render ON TOP of the freshly opened slope panel and hide
                        // it. Close the in-panel surface first (chat history
                        // persists in AiVm). In docked mode the AI is a SEPARATE
                        // window to the right of the program — it never overlaps
                        // the slope overlay, so the chat stays visible.
                        if (_isAiOverlayVisible())
                            _closeAiAssistant();
                        _openSlopeOverlay(
                            command.Params.Width,
                            command.Params.Height,
                            command.Params.Depth,
                            (int)Math.Max(1, command.Params.Quantity));
                        if (pushUndo)
                            _showToast("🏗 Открыт просчёт откосов", ToastType.Info);
                        return true;
                    }

                    case AiCommandType.UpdateItems:
                    {
                        if (pushUndo) _pushUndo();
                        int updatedCount = 0;
                        foreach (var oi in _calcVM.OrderItems)
                        {
                            if (!MatchesTarget(oi, command.Params.TargetProduct))
                                continue;
                            if (command.Params.UpdateInstallationMode.HasValue)
                                oi.InstallationMode = command.Params.UpdateInstallationMode.Value;
                            if (command.Params.UpdatePrice.HasValue)
                                oi.Price = command.Params.UpdatePrice.Value;
                            if (command.Params.UpdateAnwisMode.HasValue)
                                oi.AnwisSizeMode = command.Params.UpdateAnwisMode.Value;
                            if (command.Params.UpdateColor != null)
                                oi.Color = command.Params.UpdateColor;
                            if (command.Params.UpdateInstallationAmount.HasValue)
                                oi.SetCurrentInstallationAmount(command.Params.UpdateInstallationAmount.Value);
                            updatedCount++;
                        }
                        _markDirty();
                        _recalculateAndUpdateTotal();
                        if (pushUndo)
                            _showToast($"🔄 Обновлено позиций: {updatedCount}", ToastType.Success);
                        return true;
                    }

                    case AiCommandType.UpdateItemByIndex:
                    {
                        int idx = command.Params.ItemIndex - 1;
                        if (idx < 0 || idx >= _calcVM.OrderItems.Count)
                        {
                            error = $"Строка #{command.Params.ItemIndex} не найдена (всего строк: {_calcVM.OrderItems.Count}).";
                            return false;
                        }
                        if (pushUndo) _pushUndo();
                        var item = _calcVM.OrderItems[idx];
                        if (command.Params.UpdateWidth.HasValue) item.Width = command.Params.UpdateWidth.Value;
                        if (command.Params.UpdateHeight.HasValue) item.Height = command.Params.UpdateHeight.Value;
                        if (command.Params.UpdateQuantity.HasValue) item.Quantity = command.Params.UpdateQuantity.Value;
                        if (command.Params.UpdatePrice.HasValue) item.Price = command.Params.UpdatePrice.Value;
                        if (command.Params.UpdateInstallationMode.HasValue) item.InstallationMode = command.Params.UpdateInstallationMode.Value;
                        if (command.Params.UpdateColor != null) item.Color = command.Params.UpdateColor;
                        if (command.Params.UpdateAnwisMode.HasValue) item.AnwisSizeMode = command.Params.UpdateAnwisMode.Value;
                        if (command.Params.IsAnticat.HasValue) item.IsAnticat = command.Params.IsAnticat.Value;
                        if (command.Params.IsActive.HasValue) item.IsActive = command.Params.IsActive.Value;
                        _markDirty();
                        _recalculateAndUpdateTotal();
                        if (pushUndo)
                            _showToast($"🔄 Строка #{command.Params.ItemIndex} обновлена", ToastType.Success);
                        return true;
                    }

                    case AiCommandType.DeleteItemByIndex:
                    {
                        int idx = command.Params.ItemIndex - 1;
                        if (idx < 0 || idx >= _calcVM.OrderItems.Count)
                        {
                            error = $"Строка #{command.Params.ItemIndex} не найдена (всего строк: {_calcVM.OrderItems.Count}).";
                            return false;
                        }
                        if (pushUndo) _pushUndo();
                        var item = _calcVM.OrderItems[idx];
                        item.RecalculateRequested -= _recalculateAndUpdateTotal;
                        _calcVM.DeleteItem(item);
                        _markDirty();
                        _recalculateAndUpdateTotal();
                        if (pushUndo)
                            _showToast($"🗑 Строка #{command.Params.ItemIndex} удалена", ToastType.Info);
                        return true;
                    }

                    case AiCommandType.ToggleItemActive:
                    {
                        int idx = command.Params.ItemIndex - 1;
                        if (idx < 0 || idx >= _calcVM.OrderItems.Count)
                        {
                            error = $"Строка #{command.Params.ItemIndex} не найдена (всего строк: {_calcVM.OrderItems.Count}).";
                            return false;
                        }
                        if (pushUndo) _pushUndo();
                        var item = _calcVM.OrderItems[idx];
                        item.IsActive = command.Params.IsActive ?? !item.IsActive;
                        _markDirty();
                        _recalculateAndUpdateTotal();
                        if (pushUndo)
                            _showToast($"👁 Строка #{command.Params.ItemIndex} {(item.IsActive ? "включена" : "отключена")}", ToastType.Info);
                        return true;
                    }

                    case AiCommandType.SetClientInfo:
                    {
                        if (_setClientInfo == null)
                        {
                            error = "Заполнение данных клиента недоступно.";
                            return false;
                        }
                        if (pushUndo) _pushUndo();
                        _setClientInfo(
                            command.Params.ClientPhone,
                            command.Params.ClientAddress,
                            command.Params.ContractNumber,
                            command.Params.Notes);
                        _markDirty();
                        if (pushUndo)
                            _showToast("📋 Данные клиента обновлены", ToastType.Success);
                        return true;
                    }

                    case AiCommandType.ApplyTemplate:
                    {
                        var template = OrderTemplateService.All.FirstOrDefault(
                            t => t.Id.Equals(command.Params.TemplateId, StringComparison.OrdinalIgnoreCase));
                        if (template == null)
                        {
                            error = $"Шаблон «{command.Params.TemplateId}» не найден.";
                            return false;
                        }
                        if (pushUndo) _pushUndo();
                        var priceGetter = _getCatalogPrice ?? ((p, c) => AiFactsProvider.GetPrice(p, c));
                        foreach (var row in template.Rows)
                        {
                            if (!row.DefaultChecked) continue;
                            string prodName = row.ProductName == OrderTemplateService.OrderTemplateGridProduct
                                ? (!string.IsNullOrWhiteSpace(row.DefaultProductType) ? row.DefaultProductType : "Anwis")
                                : row.ProductName;

                            int w = command.Params.Width > 0 ? command.Params.Width : (ProductCatalog.IsManualPiece(prodName) ? 0 : 700);
                            int h = command.Params.Height > 0 ? command.Params.Height : (ProductCatalog.IsManualPiece(prodName) ? 0 : 1300);
                            string color = !string.IsNullOrWhiteSpace(command.Params.Color) ? command.Params.Color : "Белый";
                            double price = priceGetter(prodName, color);

                            var item = _calcVM.AddItem(
                                prodName,
                                ProductCatalog.IsNoColor(prodName) ? "" : color,
                                w,
                                h,
                                1,
                                price,
                                AnwisSizeService.DefaultMode);
                            if (item != null)
                            {
                                item.InstallationMode = row.DefaultInstallationMode;
                                item.RecalculateRequested += _recalculateAndUpdateTotal;
                            }
                        }
                        _markDirty();
                        _recalculateAndUpdateTotal();
                        if (pushUndo)
                            _showToast($"📐 Применён шаблон «{template.Name}»", ToastType.Success);
                        return true;
                    }

                    case AiCommandType.SaveOrder:
                    {
                        if (_saveOrder == null)
                        {
                            error = "Сохранение заказа недоступно.";
                            return false;
                        }
                        _saveOrder();
                        if (pushUndo)
                            _showToast("💾 Заказ сохранён", ToastType.Success);
                        return true;
                    }

                    case AiCommandType.PrintKp:
                    {
                        if (_printKp == null)
                        {
                            error = "Печать КП недоступна.";
                            return false;
                        }
                        _printKp();
                        if (pushUndo)
                            _showToast("🖨 Открыта печать КП", ToastType.Success);
                        return true;
                    }

                    case AiCommandType.ExportPdf:
                    {
                        if (_exportPdf == null)
                        {
                            error = "Экспорт PDF недоступен.";
                            return false;
                        }
                        _exportPdf();
                        if (pushUndo)
                            _showToast("📄 Экспорт в PDF выполнен", ToastType.Success);
                        return true;
                    }

                    default:
                        error = "Неизвестная команда AI.";
                        return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AI] Command execution failed: {ex}");
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Matches an order item against a product/category filter for UpdateItems
        /// and DeleteItems.
        /// </summary>
        internal static bool MatchesTarget(OrderItem item, string target)
        {
            if (string.IsNullOrWhiteSpace(target) || target == "all")
                return true;
            var t = target.Trim().ToLowerInvariant();
            var name = item.Name;
            return t switch
            {
                "сетки" => name is "Anwis" or "На навесах" or "Оконная на метал. крепл." or "Дверная сетка",
                "фасадные" => name is "Отлив" or "Козырёк" or "Короб",
                "комплектующие" => name is "ПСУЛ" or "Уплотнение" or "Брус" or "Пояс" or "Материал",
                "услуги" => name is "Работа" or "Доставка",
                "откосы" => name is "Откос" or "Работа за откос",
                _ => string.Equals(name, target, StringComparison.OrdinalIgnoreCase)
            };
        }
    }
}
