using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.ViewModels
{
    public delegate void SlopesChangedHandler();

    /// <summary>
    /// Arguments for notifying that slopes have changed.
    /// </summary>

    /// <summary>
    /// Result of UpdateTotal — consumed by MainWindow to update UI TextBlocks.
    /// </summary>
    public sealed class TotalInfo
    {
        public double Total { get; init; }
        public int Count { get; init; }
        public double TotalArea { get; init; }
        public double TotalLinear { get; init; }
        public double TotalPieces { get; init; }
    }

    public class CalculationViewModel
    {
        public ObservableCollection<OrderItem> OrderItems { get; } = new();

        /// <summary>
        /// Событие: откосы в заказе изменились (добавление/удаление/изменение Q).
        /// </summary>
        public event SlopesChangedHandler? SlopesChanged;

        /// <summary>
        /// Returns true for products whose installation toggle should start in
        /// the "Без монтажа" state (0 ₽) by default.
        /// Delegates to <see cref="ProductCatalog.IsPerLinearMeter"/> — single
        /// source of truth for "per-linear-meter products" used across
        /// LoadFromOrderData migrations, sign-flip exclusion, and this AddItem.
        /// </summary>
        private static bool IsInstallationDefaultNoInstallation(string type) =>
            ProductCatalog.IsPerLinearMeter(type);

        /// <summary>
        /// Adds a new order item from quick-add parameters.
        /// For Anwis products, applies calc adjustment to Width/Height before storing.
        /// Returns the created item, or null if validation failed.
        /// </summary>
        public OrderItem? AddItem(string type, string color, int width, int height, double qty, double price,
            Models.AnwisSizeMode anwisMode = Models.AnwisSizeMode.Брусбокс60)
        {
            if (qty <= 0) qty = 1;

            // For Anwis, apply calc adjustment to the raw input dimensions.
            // The stored Width/Height in OrderItem are the calc-adjusted values.
            double storedW = width;
            double storedH = height;
            if (Services.AnwisSizeService.IsApplicable(type))
            {
                var size = AnwisSize.ОтВвода(width, height, anwisMode);
                storedW = size.ШиринаРасчёт;
                storedH = size.ВысотаРасчёт;
            }

            var item = new OrderItem
            {
                Name = type,
                Color = color,
                Width = storedW,
                Height = storedH,
                Quantity = qty,
                Price = price,
                InstallationMode = IsInstallationDefaultNoInstallation(type) ? 1 : 0,
                InstallationDeduction = OrderItem.GetDefaultInstallationDeduction(type),
                InstallationSurcharge = OrderItem.GetDefaultInstallationSurcharge(type),
                // v3.43.2.10: signed adjustment for mode 0 («Монтаж включён»).
                // Default 0 (no modification) at item-add time.
                // v3.47.0: per-linear-meter products (Отлив, Козырёк) get their rate.
                InstallationAdjustment = OrderItem.GetDefaultInstallationAdjustment(type),
                RowNumber = OrderItems.Count + 1
            };
            // Use SetAnwisModeQuiet — Width/Height above are already-final stored
            // values for `anwisMode`. Running the public setter would falsely
            // reverse-apply through the default ББ 60 and corrupt dimensions.
            item.SetAnwisModeQuiet(anwisMode);

            OrderItems.Add(item);
            return item;
        }

        /// <summary>
        /// Добавляет откос из расчёта в заказ (2 строки: «Откос» и «Работа за откос»).
        /// </summary>
        /// <param name="slopeCalc">Расчёт откоса.</param>
        /// <param name="priceService">Для получения цен из прайса.</param>
        /// <returns>Список созданных OrderItem (2 шт).</returns>
        public List<OrderItem> AddSlope(SlopeCalculation slopeCalc, PriceService? priceService = null)
        {
            var items = new List<OrderItem>();

            // Строка «Откос» — материалы
            double totalMaterials = slopeCalc.TotalMaterials;
            var materialItem = new OrderItem
            {
                Name = "Откос",
                Color = "",
                Width = slopeCalc.WidthMm,
                Height = slopeCalc.HeightMm,
                Quantity = slopeCalc.WindowCount,
                Price = totalMaterials,
                SlopeData = slopeCalc,
                RowNumber = OrderItems.Count + 1
            };
            materialItem.SetDefaultPrice(0);
            OrderItems.Add(materialItem);
            items.Add(materialItem);

            // Строка «Работа за откос» — работа
            // v3.43.4 (bugfix): НЕ проставляем SlopeData — иначе два OrderItem
            // (Откос + Работа) SHAR'ят один SlopeCalculation. Когда после AddSlope
            // вызывается RecalculateAllSlopes(), оба item'а итерируются в
            // RecalculateSealantAndTape и WindowCount суммируется дважды
            // (3+3=6 вместо 3). Это дёргает PropertyChanged каскад → вторая
            // Recalculate с неправильным TotalMaterials по sealant/tape.
            // Работа/за/откос не участвует в экономии герметика/скотча —
            // её Price 4380 статичен, а при изменении Qty в DataGrid падает
            // в fallback-путь Recalculate(): _total = Price×Qty = 4380×Qty.
            double totalLabor = slopeCalc.TotalLabor;
            var laborItem = new OrderItem
            {
                Name = "Работа за откос",
                Color = "",
                Width = 0,
                Height = 0,
                Quantity = slopeCalc.WindowCount,
                Price = totalLabor,
                RowNumber = OrderItems.Count + 1
            };
            laborItem.SetDefaultPrice(0);
            OrderItems.Add(laborItem);
            items.Add(laborItem);

            // Пересчёт герметика/скотча по всему заказу
            RecalculateAllSlopes();
            SlopesChanged?.Invoke();

            return items;
        }

        /// <summary>
        /// Пересчитывает Sealant и Tape для ВСЕХ откосов в заказе (герметик/скотч — экономия по всему заказу).
        /// </summary>
        public void RecalculateAllSlopes()
        {
            SlopeCalculatorService.RecalculateSealantAndTape(OrderItems);
        }

        /// <summary>
        /// Removes a single order item and renumbers remaining rows.
        /// Caller must unsubscribe RecalculateRequested before calling.
        /// </summary>
        public void DeleteItem(OrderItem item)
        {
            OrderItems.Remove(item);
            RenumberRows();
        }

        /// <summary>
        /// Removes all order items. Caller must unsubscribe events before calling.
        /// </summary>
        public void ClearAll()
        {
            OrderItems.Clear();
        }

        public void RenumberRows()
        {
            for (int i = 0; i < OrderItems.Count; i++)
                OrderItems[i].RowNumber = i + 1;
        }

        /// <summary>
        /// Recalculates totals across all active items.
        /// </summary>
        public TotalInfo CalculateTotal(double additionalKpTotal)
        {
            var validItems = OrderItems.Where(i => !string.IsNullOrEmpty(i.Name) && i.IsActive && i.Total > 0).ToList();
            double itemsTotal = validItems.Sum(i => i.TotalWithDeduction);
            int count = validItems.Count;
            double total = itemsTotal + additionalKpTotal;

            // Area/linear/piece subtotals must account for Quantity, not just per-row CalculatedValue.
            // E.g. 3 windows of 1 м² each → TotalArea = 3 м², not 1 м².
            double totalArea = validItems.Where(i => i.Unit == "м²").Sum(i => i.CalculatedValue * i.Quantity);
            double totalLinear = validItems.Where(i => i.Unit == "м.п.").Sum(i => i.CalculatedValue * i.Quantity);
            double totalPieces = validItems.Where(i => i.Unit == "шт.").Sum(i => i.Quantity);

            return new TotalInfo
            {
                Total = total,
                Count = count,
                TotalArea = totalArea,
                TotalLinear = totalLinear,
                TotalPieces = totalPieces
            };
        }



        /// <summary>
        /// Creates a deep clone snapshot for undo.
        /// </summary>
        public List<OrderItem> SnapshotItems() => OrderItems.Select(i => i.Clone()).ToList();

        /// <summary>
        /// Restores OrderItems from a snapshot (used by Undo/Redo).
        /// Caller must unsubscribe RecalculateRequested from all existing items before calling.
        /// </summary>
        public void RestoreFromSnapshot(List<OrderItem> snapshot, Action recalculateCallback)
        {
            OrderItems.Clear();
            foreach (var snap in snapshot)
            {
                var item = snap.Clone();
                item.RecalculateRequested += recalculateCallback;
                OrderItems.Add(item);
            }
            RenumberRows();
        }

        /// <summary>
        /// Loads order items from OrderData (used when opening a saved order).
        /// Caller must unsubscribe old events before calling.
        /// </summary>
        public void LoadFromOrderData(OrderData order, Action recalculateCallback)
        {
            OrderItems.Clear();
            foreach (var od in order.Items)
            {
                // Конвертация старого «Откос материал» в новый формат?
                if (od.Name == "Откос материал" && od.SlopeData == null)
                {
                    // Старый заказ — создаём SlopeCalculation из Width (глубина) + height (0)
                    if (od.Width > 0)
                    {
                        var oldCalc = new SlopeCalculation
                        {
                            WidthMm = od.Height > 0 ? od.Height : 1000,
                            HeightMm = od.Height > 0 ? od.Width : 1000,
                            DepthM = od.Width / 1000.0,
                            WindowCount = (int)od.Quantity
                        };
                        od.SlopeData = SlopeCalculationData.FromSlopeCalculation(
                            Services.SlopeCalculatorService.Calculate(
                                oldCalc.WidthMm, oldCalc.HeightMm, oldCalc.DepthM,
                                (int)od.Quantity, (int)od.Quantity));
                    }
                }

                var item = new OrderItem
                {
                    RowNumber = OrderItems.Count + 1,
                    Name = od.Name == "Откос материал" ? "Откос" : od.Name,
                    Color = od.Color,
                    Width = od.Width,
                    Height = od.Height,
                    Quantity = od.Quantity,
                    Price = od.Price,
                    InstallationMode = od.InstallationMode != 0 ? od.InstallationMode : (od.HasInstallation ? 0 : 1),
                    // v3.46.1 migration: positive deduction/surcharge stored in older orders
                    // means "subtract" per OLD convention. Flip sign to match v3.46.1+ signed
                    // convention (negative = subtract). EXCEPTION: per-linear-meter products
                    // (Отлив/Козырёк since v3.47.0) use POSITIVE surcharge/deduction
                    // convention — their v3.47.0+ saved values must NOT be sign-flipped.
                    InstallationDeduction = (od.InstallationDeduction > 0 && od.InstallationMode != 0)
                        && !ProductCatalog.IsPerLinearMeter(od.Name)
                        ? -od.InstallationDeduction   // v3.46.1 migration: old positive → new signed convention (negative = subtract)
                        : od.InstallationDeduction,
                    InstallationSurcharge = (od.InstallationSurcharge > 0 && od.InstallationMode != 0)
                        && !ProductCatalog.IsPerLinearMeter(od.Name)
                        ? -od.InstallationSurcharge   // v3.46.1 migration: old positive → new signed convention
                        : od.InstallationSurcharge,
                    // v3.43.2.10: signed adjustment for «Монтаж включён».
                    // DTO default 0 → старые orders.json без этого поля загружаются
                    // без изменений суммы (no-op backward-compatible).
                    InstallationAdjustment = od.InstallationAdjustment,
                    IsActive = od.IsActive,
                    IsAnticat = od.IsAnticat,
                    SlopeData = od.SlopeData?.ToSlopeCalculation()
                };
                // Use SetAnwisModeQuiet — Width/Height above are already-final stored
                // values for the loaded od.AnwisSizeMode. The public setter would
                // reverse-apply through default ББ 60 and corrupt dimensions.
                item.SetAnwisModeQuiet((Models.AnwisSizeMode)od.AnwisSizeMode);

                // v3.47.0+ migration for legacy per-linear-meter product orders.
                // These products were NOT installation-applicable before v3.47.0,
                // so pre-v3.47.0 saved JSON for them only carries DTO defaults.
                // The v3.47.0 per-linear-meter formula scales the rate by
                // InstallationLinearMeters = (W+H)*2/1000. With legacy defaults and
                // a typical Отлив 1000×500 (linear=3 м.п., Q=1, Total=1075):
                //   TotalWithDeduction = Max(0, 1075 + (-500)×3×1) = Max(0, −425) = 0
                // → selecting X (mode 1) or B (mode 2) displays 0 in the UI.
                //
                // History of legacy default conventions (handled symmetrically):
                //   pre-v3.46.1: deduction/surcharge stored as POSITIVE +500
                //     (meaning "subtract" under the OLD convention).
                //   v3.46.1+:    deduction/surcharge stored as NEGATIVE -500
                //     under the NEW signed convention.
                // The Math.Abs(Math.Abs(...) - 500) check matches both signs so the
                // migration is robust against either pre-v3.47.0 JSON shape.
                //
                // The original auto-switch to mode 1 is preserved as a defensive
                // fallback for orders with custom ded/sur but unset mode/adjustment.
                if (ProductCatalog.IsPerLinearMeter(item.Name))
                {
                    bool isLegacyLoad =
                        item.InstallationMode == 0 &&
                        Math.Abs(item.InstallationAdjustment) < 0.01 &&
                        Math.Abs(Math.Abs(item.InstallationDeduction) - 500) < 0.01 &&
                        Math.Abs(Math.Abs(item.InstallationSurcharge) - 500) < 0.01;
                    if (isLegacyLoad)
                    {
                        item.InstallationMode = 1;
                        item.InstallationDeduction = OrderItem.GetDefaultInstallationDeduction(item.Name);
                        item.InstallationSurcharge = OrderItem.GetDefaultInstallationSurcharge(item.Name);
                        item.InstallationAdjustment = OrderItem.GetDefaultInstallationAdjustment(item.Name);
                    }
                    else if (item.InstallationMode == 0 &&
                             Math.Abs(item.InstallationAdjustment) < 0.01)
                    {
                        // Defensive fallback: legacy ded/sur in custom range (not ±500),
                        // but mode+adj still at defaults. Switch to mode 1 (no install).
                        item.InstallationMode = 1;
                    }
                }

                item.RecalculateRequested += recalculateCallback;
                OrderItems.Add(item);
            }

            // v3.43.5 (bugfix): после загрузки заказа пересчитываем общие
            // материалы (герметик/скотч) и их распределение между строками.
            RecalculateAllSlopes();
        }

        /// <summary>
        /// Unsubscribes RecalculateRequested from all items.
        /// </summary>
        public void UnsubscribeAll(Action recalculateCallback)
        {
            foreach (var item in OrderItems)
                item.RecalculateRequested -= recalculateCallback;
        }
    }
}
