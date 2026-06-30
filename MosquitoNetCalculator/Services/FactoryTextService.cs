using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Generates structured plain-text summaries of selected order items,
    /// grouped by product type, suitable for sending to the factory,
    /// copy-pasting for installers, or purchasing.
    /// </summary>
    public static class FactoryTextService
    {
        /// <summary>
        /// Item-level selection model used by the SendToFactory dialog.
        /// </summary>
        public class SelectableItem
        {
            public OrderItem? OrderItem { get; set; }
            public AdditionalKpItem? AdditionalKp { get; set; }
            public string DisplayName { get; set; } = "";
            public string Detail { get; set; } = "";
            public bool IsSelected { get; set; }
            public bool IsOrderItem => OrderItem != null;
            public bool IsAdditionalKp => AdditionalKp != null;
        }

        /// <summary>
        /// Builds a list of <see cref="SelectableItem"/> from the current order.
        /// Items typically not sent to production (Работа, ПСУЛ, Отлив, Доставка, Брус, Пояс)
        /// are unchecked by default; all others are checked.
        /// </summary>
        public static List<SelectableItem> BuildSelectableItems(
            IEnumerable<OrderItem> orderItems,
            IEnumerable<AdditionalKpItem> additionalKps)
        {
            // Product names that are usually NOT sent to production.
            // "Отлив" is staged separately (not made out of netting —
            // it's a finished metal/plastic sill) so the user opts in
            // explicitly via the checkbox before it travels with the
            // factory batch.
            var notForProduction = new HashSet<string>
            {
                "Работа", "ПСУЛ", "Отлив", "Доставка", "Брус", "Пояс", "Откос материал", "Уплотнение", "Короб"
            };

            var result = new List<SelectableItem>();

            foreach (var item in orderItems.Where(i => !string.IsNullOrEmpty(i.Name) && i.Total > 0))
            {
                string detail;
                if (item.IsManualPiece)
                {
                    detail = $"{item.Quantity} шт.";
                }
                else if (AnwisSizeService.IsApplicable(item.Name))
                {
                    int origW = (int)item.Width;
                    int origH = (int)item.Height;
                    detail = $"{origW}\u00D7{origH} \u2014 {item.Quantity} шт.";
                }
                else
                {
                    // Show Width×Height format for factory readability
                    string w = ((int)item.Width).ToString();
                    string h = ((int)item.Height).ToString();
                    detail = $"{w}\u00D7{h} \u2014 {item.Quantity} шт.";
                }

                result.Add(new SelectableItem
                {
                    OrderItem = item,
                    DisplayName = item.DisplayName,
                    Detail = detail,
                    IsSelected = !notForProduction.Contains(item.Name)
                });
            }

            // Additional KPs — show as section with number
            foreach (var kp in additionalKps.Where(k => k.IsActive))
            {
                result.Add(new SelectableItem
                {
                    AdditionalKp = kp,
                    DisplayName = string.IsNullOrWhiteSpace(kp.Number) ? "Доп. КП" : $"КП {kp.Number}",
                    Detail = $"({kp.Amount:N0} руб.)",
                    IsSelected = true
                });
            }

            return result;
        }



        /// <summary>
        /// Generates the final structured text from selected items.
        /// Format:
        ///   Адрес: ...
        ///
        ///   К КП № 2-2256
        ///
        ///   Anwis, габаритный размер:
        ///   1000×1000 — 1 шт.
        ///   1200×1400 — 2 шт.
        ///
        ///   Отлив:
        ///   200×1000 — 1 шт.
        /// </summary>
        public static string Generate(
            string address,
            List<SelectableItem> selectedItems)
        {
            var sb = new StringBuilder();

            // ── Address ──
            if (!string.IsNullOrWhiteSpace(address))
            {
                sb.AppendLine($"Адрес: {address}");
            }

            sb.AppendLine(); // blank line separator

            // ── "К КП" section from selected Additional KPs ──
            var selectedKps = selectedItems
                .Where(i => i.IsAdditionalKp && i.IsSelected)
                .ToList();

            if (selectedKps.Count > 0)
            {
                foreach (var kp in selectedKps)
                {
                    string number = kp.AdditionalKp?.Number?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(number))
                        sb.AppendLine($"К КП № {number}");
                    else
                        sb.AppendLine("К КП");
                }
                sb.AppendLine(); // blank line separator
            }

            // ── Order items grouped by product name ──
            var selectedOrderItems = selectedItems
                .Where(i => i.IsOrderItem && i.IsSelected)
                .Select(i => i.OrderItem!)
                .ToList();

            if (selectedOrderItems.Count > 0)
            {
                // Group by product Name AND AnwisSizeMode — different modes
                // of Anwis get separate sections with mode-aware headers
                // (e.g. "Anwis, размер проёма (ББ 60):").
                // Non-Anwis products group by name only as before.
                var groups = selectedOrderItems
                    .GroupBy(item => new
                    {
                        Name = item.DisplayName,
                        Mode = AnwisSizeService.IsApplicable(item.Name)
                            ? item.AnwisSizeMode
                            : (AnwisSizeMode?)null
                    })
                    .ToList();

                bool first = true;
                foreach (var group in groups)
                {
                    if (!first) sb.AppendLine();
                    first = false;

                    string header = group.Key.Mode.HasValue
                        ? AnwisSizeService.GetSectionHeader(group.Key.Mode.Value).Replace("Anwis", group.Key.Name) + ":"
                        : $"{group.Key.Name}:";
                    sb.AppendLine(header);

                    foreach (var item in group)
                    {
                        if (item.IsManualPiece)
                        {
                            sb.AppendLine($"{item.Quantity} шт.");
                        }
                        else if (AnwisSizeService.IsApplicable(item.Name))
                        {
                            int copyW = (int)item.Размеры.ШиринаЗавод;
                            int copyH = (int)item.Размеры.ВысотаЗавод;
                            sb.AppendLine($"\u0428: {copyW} \u00D7 \u0412: {copyH} \u2014 {item.Quantity} шт.");
                        }
                        else
                        {
                            string w = ((int)item.Width).ToString();
                            string h = ((int)item.Height).ToString();
                            sb.AppendLine($"\u0428: {w} \u00D7 \u0412: {h} \u2014 {item.Quantity} шт.");
                        }
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }
    }
}
