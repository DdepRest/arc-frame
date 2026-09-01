using System;
using System.Collections.Generic;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Pure calculations and display-row construction for the slope summary.
    /// The WPF control remains a thin adapter and keeps its existing internal API.
    /// </summary>
    internal static class SlopeSummaryCalculator
    {
        /// <summary>
        /// Полная стоимость откоса (материалы + работа), как она попадёт в заказ.
        /// Общие материалы распределяются по доле окон.
        /// </summary>
        public static double ComputePanelTotal(SlopeCalculation calc, int windowCountInOrder)
        {
            int n = calc.WindowCount;

            double perWindowMaterials = calc.Sandwich.Sum + calc.Foam.Sum
                                      + calc.Penoplex.Sum + calc.Laminatina.Sum;
            if (!calc.IsProfileEconomyApplied)
                perWindowMaterials += calc.StartProfile.Sum + calc.FProfile.Sum;

            double sharedMaterials = calc.Sealant.Sum + calc.Tape.Sum;
            if (calc.IsProfileEconomyApplied)
                sharedMaterials += calc.StartProfile.Sum + calc.FProfile.Sum;

            int totalWindowCount = windowCountInOrder + n;
            double sharedShare = totalWindowCount > 0
                ? Math.Round(sharedMaterials * n / totalWindowCount, 2)
                : 0.0;

            return Math.Round(perWindowMaterials * n + sharedShare + calc.TotalLabor * n, 2);
        }

        public static double ComputeTotalSavings(double fullTotal, double realOrderTotal)
            => Math.Max(0, Math.Round(fullTotal - realOrderTotal, 2));

        /// <summary>
        /// Строит display-only сводку расхода материалов на все откосы.
        /// </summary>
        public static List<MaterialSummaryRow> BuildMaterialSummaryRows(SlopeCalculation calc)
        {
            int n = calc.WindowCount;
            var rows = new List<MaterialSummaryRow>();

            double sandwichQty = calc.Sandwich.Quantity;
            rows.Add(new MaterialSummaryRow
            {
                Name = "Сэндвич",
                PerDetail = $"{sandwichQty:F3} м² ×{n}",
                TotalDisplay = $"{sandwichQty * n:F3} м²",
            });

            rows.Add(new MaterialSummaryRow
            {
                Name = "Пена",
                PerDetail = $"1 баллон ×{n}",
                TotalDisplay = $"{n} баллон{(n == 1 ? "" : "ов")}",
            });

            double sealantQty = calc.Sealant.Quantity;
            double sealantWas = 1.0 * n;
            int sealantSaved = (int)(sealantWas - sealantQty);
            double sealantSavings = sealantSaved * calc.Sealant.Price;
            string sealantNote = sealantSaved > 0 ? $"экон. {sealantWas:F0} → {sealantQty:F0} тюб. = −{sealantSavings:N0} ₽" : "";
            string? sealantTooltip = sealantSaved > 0
                ? $"Экономия за счёт общего расхода герметика на все окна.\n"
                  + $"Было: {sealantWas:F0} тюб. × {calc.Sealant.Price:N0} ₽ = {sealantWas * calc.Sealant.Price:N0} ₽\n"
                  + $"Стало: {sealantQty:F0} тюб. × {calc.Sealant.Price:N0} ₽ = {sealantQty * calc.Sealant.Price:N0} ₽\n"
                  + $"Экономия: {sealantSaved} тюб. × {calc.Sealant.Price:N0} ₽ = −{sealantSavings:N0} ₽"
                : null;
            rows.Add(new MaterialSummaryRow
            {
                Name = "Герметик",
                PerDetail = $"{sealantQty:F0} тюбик{(sealantQty == 1 ? "" : "а")}",
                TotalDisplay = $"{sealantQty:F0} тюбик{(sealantQty == 1 ? "" : "а")}",
                Note = sealantNote,
                EconomyTooltip = sealantTooltip,
            });

            double tapeQty = calc.Tape.Quantity;
            double tapeWas = 1.0 * n;
            int tapeSaved = (int)(tapeWas - tapeQty);
            double tapeSavings = tapeSaved * calc.Tape.Price;
            string tapeNote = tapeSaved > 0 ? $"экон. {tapeWas:F0} → {tapeQty:F0} мот. = −{tapeSavings:N0} ₽" : "";
            string? tapeTooltip = tapeSaved > 0
                ? $"Экономия за счёт общего расхода скотча на все окна.\n"
                  + $"Было: {tapeWas:F0} мот. × {calc.Tape.Price:N0} ₽ = {tapeWas * calc.Tape.Price:N0} ₽\n"
                  + $"Стало: {tapeQty:F0} мот. × {calc.Tape.Price:N0} ₽ = {tapeQty * calc.Tape.Price:N0} ₽\n"
                  + $"Экономия: {tapeSaved} мот. × {calc.Tape.Price:N0} ₽ = −{tapeSavings:N0} ₽"
                : null;
            rows.Add(new MaterialSummaryRow
            {
                Name = "Скотч",
                PerDetail = $"{tapeQty:F0} моток",
                TotalDisplay = $"{tapeQty:F0} моток",
                Note = tapeNote,
                EconomyTooltip = tapeTooltip,
            });

            int startNoEcon = SlopeCalculatorService.OptimizeStripsForMultipleWindows3Sides(
                (int)calc.WidthMm, (int)calc.HeightMm, n);
            int startQtyTotal = calc.IsProfileEconomyApplied
                ? (int)calc.StartProfile.Quantity
                : (int)calc.StartProfile.Quantity * n;
            int startSaved = Math.Max(0, startNoEcon - startQtyTotal);
            double startSavings = startSaved * calc.StartProfile.Price;
            string startNote = startSaved > 0 ? $"экон. {startNoEcon:F0} → {startQtyTotal:F0} пол. = −{startSavings:N0} ₽" : "";
            string? startTooltip = startSaved > 0
                ? $"Экономия за счёт общего раскроя профилей на все окна.\n"
                  + $"Было: {startNoEcon:F0} пол. × {calc.StartProfile.Price:N0} ₽ = {startNoEcon * calc.StartProfile.Price:N0} ₽\n"
                  + $"Стало: {startQtyTotal:F0} пол. × {calc.StartProfile.Price:N0} ₽ = {startQtyTotal * calc.StartProfile.Price:N0} ₽\n"
                  + $"Экономия: {startSaved} пол. × {calc.StartProfile.Price:N0} ₽ = −{startSavings:N0} ₽"
                : null;
            rows.Add(new MaterialSummaryRow
            {
                Name = "Старт",
                PerDetail = $"{startQtyTotal:F0} пол.",
                TotalDisplay = $"{startQtyTotal:F0} пол. (3 м)",
                Note = startNote,
                EconomyTooltip = startTooltip,
            });

            int fNoEcon = SlopeCalculatorService.OptimizeStripsForMultipleWindows3Sides(
                (int)calc.WidthMm + 100, (int)calc.HeightMm + 100, n);
            int fQtyTotal = calc.IsProfileEconomyApplied
                ? (int)calc.FProfile.Quantity
                : (int)calc.FProfile.Quantity * n;
            int fSaved = Math.Max(0, fNoEcon - fQtyTotal);
            double fSavings = fSaved * calc.FProfile.Price;
            string fNote = fSaved > 0 ? $"экон. {fNoEcon:F0} → {fQtyTotal:F0} пол. = −{fSavings:N0} ₽" : "";
            string? fTooltip = fSaved > 0
                ? $"Экономия за счёт общего раскроя профилей на все окна.\n"
                  + $"Было: {fNoEcon:F0} пол. × {calc.FProfile.Price:N0} ₽ = {fNoEcon * calc.FProfile.Price:N0} ₽\n"
                  + $"Стало: {fQtyTotal:F0} пол. × {calc.FProfile.Price:N0} ₽ = {fQtyTotal * calc.FProfile.Price:N0} ₽\n"
                  + $"Экономия: {fSaved} пол. × {calc.FProfile.Price:N0} ₽ = −{fSavings:N0} ₽"
                : null;
            rows.Add(new MaterialSummaryRow
            {
                Name = "F-планка",
                PerDetail = $"{fQtyTotal:F0} пол.",
                TotalDisplay = $"{fQtyTotal:F0} пол. (3 м)",
                Note = fNote,
                EconomyTooltip = fTooltip,
            });

            double penoplexQty = calc.Penoplex.Quantity;
            rows.Add(new MaterialSummaryRow
            {
                Name = "Пеноплекс",
                PerDetail = $"{penoplexQty:F0} лист{(penoplexQty == 1 ? "" : "а")} ×{n}",
                TotalDisplay = $"{penoplexQty * n:F0} лист{(penoplexQty * n == 1 ? "" : "ов")}",
            });

            double laborQty = calc.Labor.Quantity;
            rows.Add(new MaterialSummaryRow
            {
                Name = "Работа",
                PerDetail = $"{laborQty:F2} м.п. ×{n}",
                TotalDisplay = $"{laborQty * n:F2} м.п.",
            });

            double laminatinaQty = calc.Laminatina.Quantity;
            if (laminatinaQty > 0)
            {
                rows.Add(new MaterialSummaryRow
                {
                    Name = "Ламинат",
                    PerDetail = $"{laminatinaQty:F0} шт. ×{n}",
                    TotalDisplay = $"{laminatinaQty * n:F0} шт.",
                });

                double laminatinaLaborQty = calc.LaminatinaLabor.Quantity;
                rows.Add(new MaterialSummaryRow
                {
                    Name = "Работа за ламинат",
                    PerDetail = $"{laminatinaLaborQty:F0} шт. ×{n}",
                    TotalDisplay = $"{laminatinaLaborQty * n:F0} шт.",
                });
            }

            return rows;
        }
    }
}
