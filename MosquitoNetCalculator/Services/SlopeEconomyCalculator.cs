using System;
using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// v3.44.9: расчёт детализации экономии материалов по всем откосам в заказе.
    /// </summary>
    public static class SlopeEconomyCalculator
    {
        /// <summary>
        /// Рассчитывает строки детализации экономии для герметика, скотча,
        /// Старта и F-планки по набору расчётов откосов.
        /// </summary>
        /// <param name="slopes">Расчёты откосов (Distinct по ссылке).</param>
        /// <returns>Список строк для отображения в таблице.</returns>
        public static List<EconomyDetailRow> CalculateDetails(IEnumerable<SlopeCalculation> slopes)
        {
            var slopeList = slopes?.Where(s => s != null).ToList() ?? new List<SlopeCalculation>();
            int totalWindowCount = slopeList.Sum(s => s.WindowCount);

            if (totalWindowCount == 0)
                return new List<EconomyDetailRow>();

            var rows = new List<EconomyDetailRow>();

            rows.Add(CalculateSealantRow(slopeList, totalWindowCount));
            rows.Add(CalculateTapeRow(slopeList, totalWindowCount));
            rows.AddRange(CalculateProfileRows(slopeList, totalWindowCount));

            return rows;
        }

        /// <summary>
        /// Возвращает общую сумму экономии по всем материалам.
        /// </summary>
        public static double CalculateTotalSaved(IEnumerable<SlopeCalculation> slopes)
        {
            return CalculateDetails(slopes).Sum(r => r.AmountSaved);
        }

        private static EconomyDetailRow CalculateSealantRow(List<SlopeCalculation> slopes, int totalWindowCount)
        {
            double price = slopes.FirstOrDefault()?.Sealant.Price ?? 350;
            double without = totalWindowCount;
            double with = Math.Ceiling(totalWindowCount / 4.0);
            double saved = without - with;
            double amount = saved * price;

            return new EconomyDetailRow
            {
                MaterialName = "Герметик",
                Unit = "тюб.",
                QtyWithoutEconomy = $"{without:F0}",
                QtyWithEconomy = $"{with:F0}",
                QtySaved = saved > 0 ? $"{saved:F0}" : "0",
                AmountSaved = amount,
                AverageSavedPerSlope = totalWindowCount > 0 ? amount / totalWindowCount : 0.0,
                Tooltip = BuildTooltip("тюб.", without, with, saved, price)
            };
        }

        private static EconomyDetailRow CalculateTapeRow(List<SlopeCalculation> slopes, int totalWindowCount)
        {
            double price = slopes.FirstOrDefault()?.Tape.Price ?? 135;
            double without = totalWindowCount;
            double with = Math.Ceiling(totalWindowCount / 3.0);
            double saved = without - with;
            double amount = saved * price;

            return new EconomyDetailRow
            {
                MaterialName = "Скотч",
                Unit = "мот.",
                QtyWithoutEconomy = $"{without:F0}",
                QtyWithEconomy = $"{with:F0}",
                QtySaved = saved > 0 ? $"{saved:F0}" : "0",
                AmountSaved = amount,
                AverageSavedPerSlope = totalWindowCount > 0 ? amount / totalWindowCount : 0.0,
                Tooltip = BuildTooltip("мот.", without, with, saved, price)
            };
        }

        private static IEnumerable<EconomyDetailRow> CalculateProfileRows(List<SlopeCalculation> slopes, int totalWindowCount)
        {
            var economySlopes = slopes.Where(s => s.IsProfileEconomyApplied).ToList();
            var nonEconomySlopes = slopes.Where(s => !s.IsProfileEconomyApplied).ToList();

            yield return CalculateProfileRow(
                "Старт",
                "пол.",
                economySlopes,
                nonEconomySlopes,
                totalWindowCount,
                (int w, int h) => SlopeCalculatorService.OptimizeStrips(w, h),
                BuildStartPieces);

            yield return CalculateProfileRow(
                "F-планка",
                "пол.",
                economySlopes,
                nonEconomySlopes,
                totalWindowCount,
                (int w, int h) => SlopeCalculatorService.OptimizeStrips(w + 100, h + 100),
                BuildFProfilePieces);
        }

        /// <summary>
        /// v3.44.10 (bugfix): агрегирует все детали Старта из откосов с экономией
        /// и считает общий раскрой через OptimizeStrips. Раньше использовались
        /// только размеры первого откоса, что давало неверный результат при
        /// смешанных размерах.
        /// </summary>
        private static int BuildStartPieces(List<SlopeCalculation> economySlopes)
        {
            var pieces = new List<int>();
            foreach (var calc in economySlopes)
            {
                for (int i = 0; i < calc.WindowCount; i++)
                {
                    pieces.Add((int)calc.WidthMm);
                    pieces.Add((int)calc.HeightMm);
                    pieces.Add((int)calc.HeightMm);
                }
            }
            return SlopeCalculatorService.OptimizeStrips(pieces.ToArray());
        }

        /// <summary>
        /// v3.44.10 (bugfix): агрегирует все детали F-планки из откосов с экономией
        /// и считает общий раскрой через OptimizeStrips.
        /// </summary>
        private static int BuildFProfilePieces(List<SlopeCalculation> economySlopes)
        {
            var pieces = new List<int>();
            foreach (var calc in economySlopes)
            {
                for (int i = 0; i < calc.WindowCount; i++)
                {
                    pieces.Add((int)calc.WidthMm + 100);
                    pieces.Add((int)calc.HeightMm + 100);
                    pieces.Add((int)calc.HeightMm + 100);
                }
            }
            return SlopeCalculatorService.OptimizeStrips(pieces.ToArray());
        }

        private static EconomyDetailRow CalculateProfileRow(
            string name,
            string unit,
            List<SlopeCalculation> economySlopes,
            List<SlopeCalculation> nonEconomySlopes,
            int totalWindowCount,
            Func<int, int, int> perWindowOptimizer,
            Func<List<SlopeCalculation>, int> globalOptimizer)
        {
            double price = economySlopes.FirstOrDefault()?.StartProfile.Price
                ?? nonEconomySlopes.FirstOrDefault()?.StartProfile.Price
                ?? 135;

            int without = 0;
            foreach (var calc in economySlopes.Concat(nonEconomySlopes))
            {
                int perWindow = perWindowOptimizer((int)calc.WidthMm, (int)calc.HeightMm);
                without += perWindow * calc.WindowCount;
            }

            int with = 0;
            foreach (var calc in nonEconomySlopes)
            {
                int perWindow = perWindowOptimizer((int)calc.WidthMm, (int)calc.HeightMm);
                with += perWindow * calc.WindowCount;
            }

            if (economySlopes.Count > 0)
            {
                with += globalOptimizer(economySlopes);
            }

            int saved = Math.Max(0, without - with);
            double amount = saved * price;

            return new EconomyDetailRow
            {
                MaterialName = name,
                Unit = unit,
                QtyWithoutEconomy = $"{without}",
                QtyWithEconomy = $"{with}",
                QtySaved = saved > 0 ? $"{saved}" : "0",
                AmountSaved = amount,
                AverageSavedPerSlope = totalWindowCount > 0 ? amount / totalWindowCount : 0.0,
                Tooltip = BuildTooltip(unit, without, with, saved, price)
            };
        }

        /// <summary>
        /// Формирует подробный tooltip с расшифровкой экономии для строки таблицы.
        /// </summary>
        private static string BuildTooltip(string unit, double without, double with, double saved, double price)
        {
            double withoutCost = without * price;
            double withCost = with * price;
            double savedCost = saved * price;

            return $"Без экономии: {without:F0} {unit} × {price:N0} ₽ = {withoutCost:N0} ₽\n"
                 + $"С экономией: {with:F0} {unit} × {price:N0} ₽ = {withCost:N0} ₽\n"
                 + $"Экономия: {saved:F0} {unit} × {price:N0} ₽ = −{savedCost:N0} ₽";
        }
    }
}
