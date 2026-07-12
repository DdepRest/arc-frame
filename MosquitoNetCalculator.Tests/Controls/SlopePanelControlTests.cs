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

            Assert.Equal("2 пол. ×3", startRow.PerDetail);
            Assert.Equal("6 полос 3м", startRow.TotalDisplay);
            Assert.False(startRow.HasNote);

            Assert.Equal("3 пол. ×3", fRow.PerDetail);
            Assert.Equal("9 полос 3м", fRow.TotalDisplay);
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

            Assert.Equal("1 пол. ×2", startRow.PerDetail);
            Assert.Equal("2 полос 3м", startRow.TotalDisplay);
            Assert.False(startRow.HasNote);

            Assert.Equal("2 пол. ×2", fRow.PerDetail);
            Assert.Equal("4 полос 3м", fRow.TotalDisplay);
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

            Assert.Equal("1 пол. ×1", startRow.PerDetail);
            Assert.Equal("1 полос", startRow.TotalDisplay);
            Assert.False(startRow.HasNote);

            Assert.Equal("2 пол. ×1", fRow.PerDetail);
            Assert.Equal("2 полос 3м", fRow.TotalDisplay);
            Assert.False(fRow.HasNote);
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
    }
}
