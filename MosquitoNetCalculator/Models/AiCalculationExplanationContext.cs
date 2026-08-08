using System;
using System.Text;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// Structured, data-driven explanation of one order row. Every number is
    /// taken from the real <see cref="OrderItem"/> / <see cref="SlopeCalculation"/>;
    /// the builder never substitutes its own formula.
    /// </summary>
    public sealed class AiCalculationExplanationContext
    {
        public int Index { get; init; }
        public string Name { get; init; } = "";
        public string Color { get; init; } = "";
        public string Unit { get; init; } = "";

        // Four size layers (for Anwis these genuinely differ).
        public double WidthInput { get; init; }
        public double HeightInput { get; init; }
        public double WidthCalc { get; init; }
        public double HeightCalc { get; init; }
        public double WidthFactory { get; init; }
        public double HeightFactory { get; init; }
        public string AnwisModeLabel { get; init; } = "";
        public string AnwisFormulaHint { get; init; } = "";

        public double Quantity { get; init; }
        public double Price { get; init; }
        public double CalculatedValue { get; init; }
        public double TotalWithoutInstall { get; init; }
        public double TotalWithInstall { get; init; }
        public string InstallationLabel { get; init; } = "";

        // Slope-specific (null for ordinary products).
        public int? SlopeWindowCount { get; init; }
        public double? SlopeWidthMm { get; init; }
        public double? SlopeHeightMm { get; init; }
        public double? SlopeDepthM { get; init; }
        public double? SlopeTotalMaterials { get; init; }
        public double? SlopeTotalLabor { get; init; }
        public double? SlopeGrandTotal { get; init; }
        public bool? SlopeEconomyApplied { get; init; }

        /// <summary>Human-readable explanation text derived from the fields above.</summary>
        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"## Объяснение расчёта — позиция {Index}: {Name} {Color}".TrimEnd());

            if (SlopeGrandTotal.HasValue)
            {
                sb.AppendLine($"Окно: {SlopeWidthMm:0}×{SlopeHeightMm:0} мм, глубина {SlopeDepthM:0.000} м, {SlopeWindowCount} шт.");
                sb.AppendLine($"Материалы: {MoneyFormatService.Format(SlopeTotalMaterials ?? 0)} ₽");
                sb.AppendLine($"Работа: {MoneyFormatService.Format(SlopeTotalLabor ?? 0)} ₽");
                if (SlopeEconomyApplied == true)
                    sb.AppendLine("Применён режим экономии раскроя профилей (Старт/F-планка общие на заказ).");
                sb.AppendLine($"Итого за откосы: {MoneyFormatService.Format(SlopeGrandTotal ?? 0)} ₽");
                return sb.ToString();
            }

            bool hasSizes = WidthCalc > 0 || HeightCalc > 0;
            if (hasSizes)
            {
                if (WidthInput != WidthCalc || HeightInput != HeightCalc)
                {
                    sb.AppendLine($"Введённые размеры: {Fmt(WidthInput)}×{Fmt(HeightInput)} мм");
                    sb.AppendLine($"Расчётные размеры (для цены и КП): {Fmt(WidthCalc)}×{Fmt(HeightCalc)} мм{AnwisFormulaHint}");
                    sb.AppendLine($"Заводские размеры (расчёт − 20 мм): {Fmt(WidthFactory)}×{Fmt(HeightFactory)} мм");
                }
                else
                {
                    sb.AppendLine($"Размеры: {Fmt(WidthCalc)}×{Fmt(HeightCalc)} мм");
                    if (WidthFactory != WidthCalc || HeightFactory != HeightCalc)
                        sb.AppendLine($"Заводские размеры (расчёт − 20 мм): {Fmt(WidthFactory)}×{Fmt(HeightFactory)} мм");
                }
                sb.AppendLine($"Количество: {Quantity:0.##} шт. · единица: {Unit} · площадь/объём на шт.: {CalculatedValue:0.###} {Unit}");
            }

            sb.AppendLine($"Базовая цена: {MoneyFormatService.Format(Price)} ₽/{Unit}");
            sb.AppendLine($"Сумма без монтажа: {MoneyFormatService.Format(TotalWithoutInstall)} ₽");
            if (!string.IsNullOrEmpty(InstallationLabel))
                sb.AppendLine($"Монтаж: {InstallationLabel}");
            sb.AppendLine($"Итог в заказе: {MoneyFormatService.Format(TotalWithInstall)} ₽");
            return sb.ToString();
        }

        private static string Fmt(double v) => v == Math.Floor(v) ? ((int)v).ToString() : v.ToString("0.##");
    }
}
