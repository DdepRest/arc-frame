using System;
using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.ViewModels;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Builds a structured <see cref="AiOrderContext"/> from the real order.
    /// Totals come from the already-computed <see cref="TotalInfo"/> (the same
    /// one shown in the UI) — nothing is re-calculated here.
    /// </summary>
    public static class AiOrderContextBuilder
    {
        public static AiOrderContext Build(
            IReadOnlyList<OrderItem> items,
            TotalInfo totals,
            double additionalKpTotal)
        {
            var ctx = new AiOrderContext
            {
                Count = totals.Count,
                Total = totals.Total,
                // Order with only Доп.КП (no items) must never report a negative
                // items subtotal.
                ItemsTotal = Math.Max(0, totals.Total - additionalKpTotal),
                AdditionalKpTotal = additionalKpTotal,
                TotalArea = totals.TotalArea,
                TotalLinear = totals.TotalLinear,
                TotalPieces = totals.TotalPieces
            };

            int index = 0;
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Name)) continue;
                index++;
                var sizes = item.Размеры;
                var install = item.IsInstallationApplicable ? item.InstallationLabel : "";
                ctx.Items.Add(new AiOrderItemInfo
                {
                    Index = index,
                    Name = item.Name,
                    Color = item.Color,
                    Category = AiPlanValidator.GetCategory(item.Name),
                    WidthInput = sizes.ШиринаОтображение,
                    HeightInput = sizes.ВысотаОтображение,
                    WidthCalc = sizes.ШиринаРасчёт,
                    HeightCalc = sizes.ВысотаРасчёт,
                    WidthFactory = sizes.ШиринаЗавод,
                    HeightFactory = sizes.ВысотаЗавод,
                    Quantity = item.Quantity,
                    Price = item.Price,
                    TotalWithoutInstall = item.Total,
                    TotalWithInstall = item.TotalWithDeduction,
                    InstallationMode = item.IsInstallationApplicable ? item.InstallationMode : -1,
                    InstallationLabel = install,
                    IsInstallationApplicable = item.IsInstallationApplicable,
                    AnwisModeLabel = item.IsAnwis ? AiCommandParser.AnwisModeLabel(item.AnwisSizeMode) : "",
                    Unit = item.Unit,
                    IsAreaBased = ProductCatalog.IsAreaBased(item.Name),
                    IsPerLinearMeter = ProductCatalog.IsPerLinearMeter(item.Name),
                    IsManualPiece = ProductCatalog.IsManualPiece(item.Name),
                    IsSlope = item.IsSlope,
                    IsActive = item.IsActive
                });
            }

            ctx.GroupsByProduct.AddRange(GroupBy(ctx.Items, i => i.Name));
            ctx.GroupsByColor.AddRange(GroupBy(ctx.Items, i => string.IsNullOrWhiteSpace(i.Color) ? "—" : i.Color));
            ctx.GroupsByInstallation.AddRange(GroupBy(ctx.Items, i => i.IsInstallationApplicable ? i.InstallationLabel : "не предусмотрен"));
            ctx.GroupsByCategory.AddRange(GroupBy(ctx.Items, i => string.IsNullOrWhiteSpace(i.Category) ? "прочее" : i.Category));
            return ctx;
        }

        private static IEnumerable<AiOrderGroup> GroupBy(
            IEnumerable<AiOrderItemInfo> items,
            Func<AiOrderItemInfo, string> keySelector)
        {
            return items
                .Where(i => i.IsActive)
                .GroupBy(keySelector)
                .Select(g => new AiOrderGroup
                {
                    Key = g.Key,
                    Count = g.Count(),
                    Total = Math.Round(g.Sum(x => x.TotalWithInstall), 2)
                })
                .OrderByDescending(g => g.Total)
                .ToList();
        }
    }
}
