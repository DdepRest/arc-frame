using System.Linq;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// Unit tests for <see cref="SlopePanelControl.BuildMaterialSummaryRows"/>.
    /// Verifies that the material summary rows show correct per-window and total
    /// quantities for Start/F-profile with and without profile economy applied.
    /// </summary>
    public class SlopePanelControlTests
    {
        [Fact]
        public void BuildMaterialSummaryRows_ProfileEconomyOn_2150x1500x3_ShowsPerWindowAndTotal()
        {
            // User-reported dimensions: W=2150, H=1500, 3 windows, economy ON.
            // Physically no packing saving, so global totals equal per-window × N.
            var calc = SlopeCalculatorService.Calculate(2150, 1500, 0.30, 3, 3);
            calc.IsProfileEconomyApplied = true;
            calc.StartProfile.Quantity = 6; // global total
            calc.FProfile.Quantity = 9;     // global total

            var rows = SlopePanelControl.BuildMaterialSummaryRows(calc);
            var startRow = rows.First(r => r.Name == "Старт");
            var fRow = rows.First(r => r.Name == "F-планка");

            Assert.Equal("6 пол.", startRow.PerDetail);
            Assert.Equal("6 пол. (3 м)", startRow.TotalDisplay);
            Assert.False(startRow.HasNote);

            Assert.Equal("9 пол.", fRow.PerDetail);
            Assert.Equal("9 пол. (3 м)", fRow.TotalDisplay);
            Assert.False(fRow.HasNote);
        }

        [Fact]
        public void BuildMaterialSummaryRows_ProfileEconomyOff_1000x1000x2_ShowsPerWindowAndTotal()
        {
            // Without economy, Start/F-profile are per-window (3 sides).
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 2, 2);
            calc.IsProfileEconomyApplied = false;
            calc.StartProfile.Quantity = 1; // per-window
            calc.FProfile.Quantity = 2;     // per-window

            var rows = SlopePanelControl.BuildMaterialSummaryRows(calc);
            var startRow = rows.First(r => r.Name == "Старт");
            var fRow = rows.First(r => r.Name == "F-планка");

            Assert.Equal("2 пол.", startRow.PerDetail);
            Assert.Equal("2 пол. (3 м)", startRow.TotalDisplay);
            Assert.False(startRow.HasNote);

            Assert.Equal("4 пол.", fRow.PerDetail);
            Assert.Equal("4 пол. (3 м)", fRow.TotalDisplay);
            Assert.False(fRow.HasNote);
        }

        [Fact]
        public void BuildMaterialSummaryRows_SingleWindow_UsesSingularPluralization()
        {
            // Single window: total = per-window, and pluralization should be singular.
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            calc.IsProfileEconomyApplied = false;
            calc.StartProfile.Quantity = 1;
            calc.FProfile.Quantity = 2;

            var rows = SlopePanelControl.BuildMaterialSummaryRows(calc);
            var startRow = rows.First(r => r.Name == "Старт");
            var fRow = rows.First(r => r.Name == "F-планка");

            Assert.Equal("1 пол.", startRow.PerDetail);
            Assert.Equal("1 пол. (3 м)", startRow.TotalDisplay);
            Assert.False(startRow.HasNote);

            Assert.Equal("2 пол.", fRow.PerDetail);
            Assert.Equal("2 пол. (3 м)", fRow.TotalDisplay);
            Assert.False(fRow.HasNote);
        }

        [Fact]
        public void BuildMaterialSummaryRows_ProfileEconomyOff_PerDetailShowsTotalQuantity()
        {
            // Without economy, Start/F-profile are per-window (3 sides).
            // PerDetail should show the total (per-window × N), not "×N".
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 2, 2);
            calc.IsProfileEconomyApplied = false;
            calc.StartProfile.Quantity = 1; // per-window
            calc.FProfile.Quantity = 2;     // per-window

            var rows = SlopePanelControl.BuildMaterialSummaryRows(calc);
            var startRow = rows.First(r => r.Name == "Старт");
            var fRow = rows.First(r => r.Name == "F-планка");

            Assert.Equal("2 пол.", startRow.PerDetail);
            Assert.Equal("2 пол. (3 м)", startRow.TotalDisplay);

            Assert.Equal("4 пол.", fRow.PerDetail);
            Assert.Equal("4 пол. (3 м)", fRow.TotalDisplay);
        }

        [Fact]
        public void BuildMaterialSummaryRows_LaminatinaZero_DoesNotShowLaminatinaRows()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            calc.Laminatina.Quantity = 0;

            var rows = SlopePanelControl.BuildMaterialSummaryRows(calc);

            Assert.DoesNotContain(rows, r => r.Name == "Ламинат");
            Assert.DoesNotContain(rows, r => r.Name == "Работа за ламинат");
        }

        [Fact]
        public void BuildMaterialSummaryRows_LaminatinaPositive_ShowsLaminatinaRows()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 2, 2);
            calc.Laminatina.Quantity = 3;
            calc.LaminatinaLabor.Quantity = 3;

            var rows = SlopePanelControl.BuildMaterialSummaryRows(calc);

            Assert.Contains(rows, r => r.Name == "Ламинат");
            Assert.Contains(rows, r => r.Name == "Работа за ламинат");
        }

        [Fact]
        public void BuildMaterialSummaryRows_SharedMaterials_ShowsEconomyNoteAndTooltip()
        {
            // 4 windows: sealant/tape are shared across windows (ceil(N/4), ceil(N/3)).
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 4, 4);
            calc.IsProfileEconomyApplied = false;

            var rows = SlopePanelControl.BuildMaterialSummaryRows(calc);
            var sealantRow = rows.First(r => r.Name == "Герметик");
            var tapeRow = rows.First(r => r.Name == "Скотч");

            Assert.True(sealantRow.HasNote);
            Assert.Contains("экон.", sealantRow.Note);
            Assert.Contains("4 → 1 тюб. = −", sealantRow.Note);
            Assert.NotNull(sealantRow.EconomyTooltip);
            Assert.Contains("Было:", sealantRow.EconomyTooltip);
            Assert.Contains("Стало:", sealantRow.EconomyTooltip);
            Assert.Contains("Экономия:", sealantRow.EconomyTooltip);
            Assert.Contains("× 350 ₽", sealantRow.EconomyTooltip);

            Assert.True(tapeRow.HasNote);
            Assert.Contains("экон.", tapeRow.Note);
            Assert.Contains("4 → 2 мот. = −", tapeRow.Note);
            Assert.NotNull(tapeRow.EconomyTooltip);
        }

        [Fact]
        public void BuildMaterialSummaryRows_ProfileEconomyOn_1500x2150x3_ShowsGlobalTotals()
        {
            // User-reported dimensions: 1500×2150×300, 3 windows, economy ON.
            // Per-window: Start=2, F=3. Without economy: Start=6, F=9.
            // With cross-window optimization for identical windows there is no packing saving,
            // so global totals must still equal per-window × N (6 and 9), not per-window values.
            var calc = SlopeCalculatorService.Calculate(2150, 1500, 0.30, 3, 3);
            calc.IsProfileEconomyApplied = true;
            calc.StartProfile.Quantity = SlopeCalculatorService.OptimizeStripsForMultipleWindows3Sides(
                (int)calc.WidthMm, (int)calc.HeightMm, calc.WindowCount);
            calc.FProfile.Quantity = SlopeCalculatorService.OptimizeStripsForMultipleWindows3Sides(
                (int)calc.WidthMm + 100, (int)calc.HeightMm + 100, calc.WindowCount);

            var rows = SlopePanelControl.BuildMaterialSummaryRows(calc);
            var startRow = rows.First(r => r.Name == "Старт");
            var fRow = rows.First(r => r.Name == "F-планка");

            Assert.Equal("6 пол.", startRow.PerDetail);
            Assert.Equal("6 пол. (3 м)", startRow.TotalDisplay);
            Assert.False(startRow.HasNote); // no actual savings for identical windows

            Assert.Equal("9 пол.", fRow.PerDetail);
            Assert.Equal("9 пол. (3 м)", fRow.TotalDisplay);
            Assert.False(fRow.HasNote);
        }

        [Theory]
        [InlineData(10000.0, 8500.0, 1500.0)]
        [InlineData(5000.0, 5000.0, 0.0)]
        [InlineData(7500.0, 8000.0, 0.0)]
        public void ComputeTotalSavings_ReturnsNonNegativeDifference(double fullTotal, double realOrderTotal, double expected)
        {
            double actual = SlopePanelControl.ComputeTotalSavings(fullTotal, realOrderTotal);
            Assert.Equal(expected, actual, 2);
        }

        [Fact]
        public void BuildMaterialSummaryRows_ProfileEconomyOn_WithSavings_ShowsProfileEconomyNote()
        {
            // Use fixed quantities so the test is deterministic: simulate that
            // the optimizer packed 4 windows into fewer strips than the per-window sum.
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 4, 4);
            calc.IsProfileEconomyApplied = true;
            calc.StartProfile.Quantity = 3; // global optimized total (no-economy baseline is 4)
            calc.FProfile.Quantity = 5;     // global optimized total (no-economy baseline is 8)

            var rows = SlopePanelControl.BuildMaterialSummaryRows(calc);
            var startRow = rows.First(r => r.Name == "Старт");
            var fRow = rows.First(r => r.Name == "F-планка");

            Assert.True(startRow.HasNote);
            Assert.Contains("экон.", startRow.Note);
            Assert.Contains("4 → 3 пол. = −", startRow.Note);
            Assert.NotNull(startRow.EconomyTooltip);
            Assert.Contains("Было:", startRow.EconomyTooltip);
            Assert.Contains("Стало:", startRow.EconomyTooltip);
            Assert.Contains("Экономия:", startRow.EconomyTooltip);

            Assert.True(fRow.HasNote);
            Assert.Contains("экон.", fRow.Note);
            Assert.Contains("6 → 5 пол. = −", fRow.Note);
            Assert.NotNull(fRow.EconomyTooltip);
            Assert.Contains("Было:", fRow.EconomyTooltip);
        }
    }
}
