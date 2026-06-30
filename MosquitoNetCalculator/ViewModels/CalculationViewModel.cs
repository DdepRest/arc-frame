using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.ViewModels
{
    /// <summary>
    /// Result of UpdateTotal — consumed by MainWindow to update UI TextBlocks.
    /// </summary>
    public sealed class TotalInfo
    {
        public double Total { get; init; }
        public int Count { get; init; }
        public double TotalArea { get; init; }
        public double TotalLinear { get; init; }
        public int TotalPieces { get; init; }
    }

    public class CalculationViewModel
    {
        public ObservableCollection<OrderItem> OrderItems { get; } = new();

        /// <summary>
        /// Adds a new order item from quick-add parameters.
        /// For Anwis products, applies calc adjustment to Width/Height before storing.
        /// Returns the created item, or null if validation failed.
        /// </summary>
        public OrderItem? AddItem(string type, string color, int width, int height, int qty, double price,
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
                InstallationMode = type == "Отлив" ? 1 : 0,
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
            int totalPieces = validItems.Where(i => i.Unit == "шт.").Sum(i => i.Quantity);

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
                var item = new OrderItem
                {
                    RowNumber = OrderItems.Count + 1,
                    Name = od.Name,
                    Color = od.Color,
                    Width = od.Width,
                    Height = od.Height,
                    Quantity = od.Quantity,
                    Price = od.Price,
                    InstallationMode = od.InstallationMode != 0 ? od.InstallationMode : (od.HasInstallation ? 0 : 1),
                    InstallationDeduction = od.InstallationDeduction,
                    InstallationSurcharge = od.InstallationSurcharge,
                    IsActive = od.IsActive,
                    IsAnticat = od.IsAnticat
                };
                // Use SetAnwisModeQuiet — Width/Height above are already-final stored
                // values for the loaded od.AnwisSizeMode. The public setter would
                // reverse-apply through default ББ 60 and corrupt dimensions.
                item.SetAnwisModeQuiet((Models.AnwisSizeMode)od.AnwisSizeMode);
                item.RecalculateRequested += recalculateCallback;
                OrderItems.Add(item);
            }
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
