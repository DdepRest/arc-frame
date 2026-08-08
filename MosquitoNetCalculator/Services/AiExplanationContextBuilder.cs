using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Builds <see cref="AiCalculationExplanationContext"/> from a real
    /// <see cref="OrderItem"/>. The explanation mirrors the app's own numbers:
    /// the four size layers (input/calc/factory), price, totals with and
    /// without installation, and the slope breakdown when present.
    /// </summary>
    public static class AiExplanationContextBuilder
    {
        public static AiCalculationExplanationContext Build(OrderItem item, int index)
        {
            var sizes = item.Размеры;

            // Slope fields are init-only — capture them before the initializer.
            int? slopeWindowCount = null;
            double? slopeWidthMm = null, slopeHeightMm = null, slopeDepthM = null;
            double? slopeTotalMaterials = null, slopeTotalLabor = null, slopeGrandTotal = null;
            bool? slopeEconomy = null;
            if (item.SlopeData is { } slope)
            {
                slopeWindowCount = slope.WindowCount;
                slopeWidthMm = slope.WidthMm;
                slopeHeightMm = slope.HeightMm;
                slopeDepthM = slope.DepthM;
                slopeTotalMaterials = slope.TotalMaterials;
                slopeTotalLabor = slope.TotalLabor;
                slopeGrandTotal = slope.GrandTotal;
                slopeEconomy = slope.IsProfileEconomyApplied;
            }

            return new AiCalculationExplanationContext
            {
                Index = index,
                Name = item.Name,
                Color = item.Color,
                Unit = item.Unit,
                WidthInput = sizes.ШиринаОтображение,
                HeightInput = sizes.ВысотаОтображение,
                WidthCalc = sizes.ШиринаРасчёт,
                HeightCalc = sizes.ВысотаРасчёт,
                WidthFactory = sizes.ШиринаЗавод,
                HeightFactory = sizes.ВысотаЗавод,
                AnwisModeLabel = item.IsAnwis ? AiCommandParser.AnwisModeLabel(item.AnwisSizeMode) : "",
                AnwisFormulaHint = item.IsAnwis
                    ? $" (режим {AiCommandParser.AnwisModeLabel(item.AnwisSizeMode)}: {AnwisSizeService.HintTexts[item.AnwisSizeMode]})"
                    : "",
                Quantity = item.Quantity,
                Price = item.Price,
                CalculatedValue = item.CalculatedValue,
                TotalWithoutInstall = item.Total,
                TotalWithInstall = item.TotalWithDeduction,
                InstallationLabel = item.IsInstallationApplicable ? item.InstallationLabel : "",
                SlopeWindowCount = slopeWindowCount,
                SlopeWidthMm = slopeWidthMm,
                SlopeHeightMm = slopeHeightMm,
                SlopeDepthM = slopeDepthM,
                SlopeTotalMaterials = slopeTotalMaterials,
                SlopeTotalLabor = slopeTotalLabor,
                SlopeGrandTotal = slopeGrandTotal,
                SlopeEconomyApplied = slopeEconomy
            };
        }

        /// <summary>Text form used by «/объясни» without an LLM round-trip.</summary>
        public static string BuildText(IReadOnlyList<OrderItem> items, int index)
        {
            if (items.Count == 0)
                return "Заказ пуст — объяснять нечего.";
            if (index < 1 || index > items.Count)
            {
                var valid = string.Join(", ", Enumerable.Range(1, items.Count).Take(30));
                return $"Позиции {index} нет в заказе. Доступные номера: {valid}.";
            }
            return Build(items[index - 1], index).ToText();
        }

        /// <summary>Text form for the last item.</summary>
        public static string BuildTextForLast(IReadOnlyList<OrderItem> items)
        {
            if (items.Count == 0)
                return "Заказ пуст — объяснять нечего.";
            return Build(items[^1], items.Count).ToText();
        }

        /// <summary>Text form for every item (used by «/объясни всё»).</summary>
        public static string BuildTextForAll(IReadOnlyList<OrderItem> items)
        {
            if (items.Count == 0)
                return "Заказ пуст — объяснять нечего.";
            var sb = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                sb.AppendLine(Build(items[i], i + 1).ToText());
                if (i < items.Count - 1) sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
