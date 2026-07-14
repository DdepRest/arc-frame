using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Unit tests for <see cref="SlopeEconomyCalculator"/>.
    /// </summary>
    public class SlopeEconomyCalculatorTests
    {
        [Fact]
        public void CalculateDetails_EmptySlopes_ReturnsEmptyList()
        {
            var rows = SlopeEconomyCalculator.CalculateDetails(new SlopeCalculation[0]);
            Assert.Empty(rows);
        }

        [Fact]
        public void CalculateDetails_SingleSlope_NoProfileEconomy_ReturnsSealantTapeSavingsOnly()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            calc.IsProfileEconomyApplied = false;

            var rows = SlopeEconomyCalculator.CalculateDetails(new[] { calc });

            Assert.Equal(4, rows.Count);

            var sealant = rows.First(r => r.MaterialName == "Герметик");
            Assert.Equal("1", sealant.QtyWithoutEconomy);
            Assert.Equal("1", sealant.QtyWithEconomy);
            Assert.Equal("0", sealant.QtySaved);
            Assert.Equal(0.0, sealant.AmountSaved);

            var tape = rows.First(r => r.MaterialName == "Скотч");
            Assert.Equal("1", tape.QtyWithoutEconomy);
            Assert.Equal("1", tape.QtyWithEconomy);
            Assert.Equal("0", tape.QtySaved);
            Assert.Equal(0.0, tape.AmountSaved);

            var start = rows.First(r => r.MaterialName == "Старт");
            Assert.Equal("1", start.QtyWithoutEconomy);
            Assert.Equal("1", start.QtyWithEconomy);
            Assert.Equal("0", start.QtySaved);

            var fProfile = rows.First(r => r.MaterialName == "F-планка");
            Assert.Equal("2", fProfile.QtyWithoutEconomy);
            Assert.Equal("2", fProfile.QtyWithEconomy);
            Assert.Equal("0", fProfile.QtySaved);
        }

        [Fact]
        public void CalculateDetails_FourSlopes_WithSealantTapeSavings()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 4, 4);
            calc.IsProfileEconomyApplied = false;

            var rows = SlopeEconomyCalculator.CalculateDetails(new[] { calc });

            var sealant = rows.First(r => r.MaterialName == "Герметик");
            Assert.Equal("4", sealant.QtyWithoutEconomy);
            Assert.Equal("1", sealant.QtyWithEconomy);
            Assert.Equal("3", sealant.QtySaved);
            Assert.Equal(3 * 350, sealant.AmountSaved);

            var tape = rows.First(r => r.MaterialName == "Скотч");
            Assert.Equal("4", tape.QtyWithoutEconomy);
            Assert.Equal("2", tape.QtyWithEconomy);
            Assert.Equal("2", tape.QtySaved);
            Assert.Equal(2 * 135, tape.AmountSaved);
        }

        [Fact]
        public void CalculateDetails_MultipleSlopes_WithProfileEconomy_ShowsSavings()
        {
            // 4 identical slopes with profile economy enabled.
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 4, 4);
            calc.IsProfileEconomyApplied = true;

            var rows = SlopeEconomyCalculator.CalculateDetails(new[] { calc });

            var start = rows.First(r => r.MaterialName == "Старт");
            // Without economy: 1 strip per window * 4 = 4
            // With economy: OptimizeStripsForMultipleWindows3Sides(1000,1000,4) = 4 (no packing saving for identical windows)
            Assert.Equal("4", start.QtyWithoutEconomy);
            Assert.Equal("4", start.QtyWithEconomy);
            Assert.Equal("0", start.QtySaved);
        }

        [Fact]
        public void CalculateDetails_MixedEconomy_MergesGroupsCorrectly()
        {
            // 2 slopes with profile economy, 2 without.
            var withEcon = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 2, 2);
            withEcon.IsProfileEconomyApplied = true;
            var withoutEcon = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 2, 2);
            withoutEcon.IsProfileEconomyApplied = false;

            var rows = SlopeEconomyCalculator.CalculateDetails(new[] { withEcon, withoutEcon });

            var start = rows.First(r => r.MaterialName == "Старт");
            // Without economy group: 1 strip/window * 2 windows = 2
            // With economy group: OptimizeStripsForMultipleWindows3Sides(1000,1000,2) = 2
            // Total without: 2 + 2 = 4, total with: 2 + 2 = 4
            Assert.Equal("4", start.QtyWithoutEconomy);
            Assert.Equal("4", start.QtyWithEconomy);
        }

        [Fact]
        public void CalculateTotalSaved_SumsAllRows()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 4, 4);
            calc.IsProfileEconomyApplied = false;

            double total = SlopeEconomyCalculator.CalculateTotalSaved(new[] { calc });
            double expected = 3 * 350 + 2 * 135; // sealant + tape savings
            Assert.Equal(expected, total, 2);
        }

        [Fact]
        public void CalculateDetails_TotalSavedEqualsPerSlopeTimesWindowCount()
        {
            // Regression guard: the UI shows both total savings and per-slope savings.
            // Sum of per-row averages must equal total saved divided by window count.
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 4, 4);
            calc.IsProfileEconomyApplied = false;

            var rows = SlopeEconomyCalculator.CalculateDetails(new[] { calc });
            double totalSaved = rows.Sum(r => r.AmountSaved);
            double expectedPerSlope = totalSaved / 4;

            Assert.Equal(expectedPerSlope, rows.Sum(r => r.AverageSavedPerSlope), 2);
        }

        [Fact]
        public void CalculateDetails_Tooltip_ContainsBreakdown()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 4, 4);
            calc.IsProfileEconomyApplied = false;

            var rows = SlopeEconomyCalculator.CalculateDetails(new[] { calc });
            var sealant = rows.First(r => r.MaterialName == "Герметик");

            Assert.False(string.IsNullOrEmpty(sealant.Tooltip));
            Assert.Contains("Без экономии:", sealant.Tooltip);
            Assert.Contains("С экономией:", sealant.Tooltip);
            Assert.Contains("Экономия:", sealant.Tooltip);
            Assert.Contains("4 тюб.", sealant.Tooltip);
            Assert.Contains("1 тюб.", sealant.Tooltip);
            Assert.Contains("3 тюб.", sealant.Tooltip);
        }

        [Fact]
        public void CalculateDetails_MixedSizeEconomySlopes_UsesAllDimensions()
        {
            // Regression: two economy slopes with different sizes.
            // Slope 1: 2500×2500, 1 window → Start per-window = OptimizeStrips(2500,2500,2500) = 3 strips
            // Slope 2: 500×500, 1 window     → Start per-window = OptimizeStrips(500,500,500)   = 1 strip
            // Without economy total = 3 + 1 = 4 strips.
            // With economy: pieces = [2500,2500,2500,500,500,500]
            //   OptimizeStrips packs each 2500 with a 500 leftover → 3 strips.
            // Saved = 4 - 3 = 1 strip.
            // Old code (bug) used only the first slope's dimensions with totalWindowCount=2,
            // producing OptimizeStrips([2500×6]) = 6 strips and zero savings.
            var slope1 = SlopeCalculatorService.Calculate(2500, 2500, 0.15, 1, 1);
            slope1.IsProfileEconomyApplied = true;
            var slope2 = SlopeCalculatorService.Calculate(500, 500, 0.15, 1, 1);
            slope2.IsProfileEconomyApplied = true;

            var rows = SlopeEconomyCalculator.CalculateDetails(new[] { slope1, slope2 });

            var start = rows.First(r => r.MaterialName == "Старт");
            Assert.Equal("4", start.QtyWithoutEconomy);
            Assert.Equal("3", start.QtyWithEconomy);
            Assert.Equal("1", start.QtySaved);
            Assert.Equal(1 * 135, start.AmountSaved, 2);

            // F-profile should also reflect mixed dimensions (with +100mm).
            var fProfile = rows.First(r => r.MaterialName == "F-планка");
            Assert.Equal("4", fProfile.QtyWithoutEconomy);
            Assert.True(int.Parse(fProfile.QtyWithEconomy) <= 4);
            Assert.True(int.Parse(fProfile.QtySaved) >= 0);
        }
    }
}
