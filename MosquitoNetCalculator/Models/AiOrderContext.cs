using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// One order row distilled for the AI: stable position index, product
    /// identity, the four size layers (input / calc / factory), pricing and
    /// installation facts. Numbers come from the real <see cref="OrderItem"/>
    /// — the builder never invents formulas.
    /// </summary>
    public sealed class AiOrderItemInfo
    {
        public int Index { get; init; }
        public string Name { get; init; } = "";
        public string Color { get; init; } = "";
        public string Category { get; init; } = "";

        public double WidthInput { get; init; }
        public double HeightInput { get; init; }
        public double WidthCalc { get; init; }
        public double HeightCalc { get; init; }
        public double WidthFactory { get; init; }
        public double HeightFactory { get; init; }

        public double Quantity { get; init; }
        public double Price { get; init; }
        public double TotalWithoutInstall { get; init; }
        public double TotalWithInstall { get; init; }

        public int InstallationMode { get; init; }
        public string InstallationLabel { get; init; } = "";
        public bool IsInstallationApplicable { get; init; }
        public string AnwisModeLabel { get; init; } = "";
        public string Unit { get; init; } = "";

        public bool IsAreaBased { get; init; }
        public bool IsPerLinearMeter { get; init; }
        public bool IsManualPiece { get; init; }
        public bool IsSlope { get; init; }
        public bool IsActive { get; init; }
    }

    /// <summary>Grouping bucket (by product / color / installation / category).</summary>
    public sealed class AiOrderGroup
    {
        public string Key { get; init; } = "";
        public int Count { get; init; }
        public double Total { get; init; }
    }

    /// <summary>
    /// Structured snapshot of the current order handed to the AI (and to the
    /// local slash router). Built by <see cref="AiOrderContextBuilder"/> from
    /// the real calculation view-model — totals match the UI.
    /// </summary>
    public sealed class AiOrderContext
    {
        public int Count { get; init; }
        public double Total { get; init; }
        public double ItemsTotal { get; init; }
        public double AdditionalKpTotal { get; init; }
        public double TotalArea { get; init; }
        public double TotalLinear { get; init; }
        public double TotalPieces { get; init; }

        public List<AiOrderItemInfo> Items { get; } = new();
        public List<AiOrderGroup> GroupsByProduct { get; init; } = new();
        public List<AiOrderGroup> GroupsByColor { get; init; } = new();
        public List<AiOrderGroup> GroupsByInstallation { get; init; } = new();
        public List<AiOrderGroup> GroupsByCategory { get; init; } = new();

        /// <summary>Single-line summary used by local commands («/итоги»).</summary>
        public string FormatBrief()
        {
            var parts = new List<string>();
            if (TotalArea > 0) parts.Add($"{TotalArea:F3} м²");
            if (TotalLinear > 0) parts.Add($"{TotalLinear:F3} м.п.");
            if (TotalPieces > 0) parts.Add($"{TotalPieces} шт.");
            if (AdditionalKpTotal > 0) parts.Add($"доп. КП {MoneyFormatService.Format(AdditionalKpTotal)} ₽");
            return parts.Count == 0
                ? $"Позиций: {Count}. Итого: {MoneyFormatService.Format(Total)} ₽."
                : $"Позиций: {Count}. Итого: {MoneyFormatService.Format(Total)} ₽ ({string.Join(", ", parts)}).";
        }

        /// <summary>
        /// The text injected into the AI system prompt. Contains the same data
        /// the UI shows — positions with all four size layers where applicable.
        /// </summary>
        public string ToPromptText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("## ТЕКУЩИЙ ЗАКАЗ");
            sb.AppendLine(FormatBrief());
            foreach (var it in Items)
            {
                var active = it.IsActive ? "" : " (неактивна)";
                var sizes = it.IsAreaBased || it.IsPerLinearMeter
                    ? $"{FormatDim(it.WidthInput)}×{FormatDim(it.HeightInput)} мм"
                    : "";
                var anwis = string.IsNullOrEmpty(it.AnwisModeLabel) ? "" : $", Anwis: {it.AnwisModeLabel}";
                var install = it.IsInstallationApplicable ? $", монтаж: {it.InstallationLabel}" : "";
                sb.AppendLine($"{it.Index}. {it.Name} {it.Color} {sizes}{anwis}, {it.Quantity} шт., {MoneyFormatService.Format(it.Price)} ₽/ед., итог {MoneyFormatService.Format(it.TotalWithInstall)} ₽{install}{active}");
            }
            return sb.ToString();
        }

        private static string FormatDim(double v) => v == Math.Floor(v) ? ((int)v).ToString() : v.ToString("0.##");

        /// <summary>Catalog presence check used by the plan validator.</summary>
        public static bool IsKnownProduct(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return ProductCatalog.UserGroups.Any(g =>
                g.Products.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
