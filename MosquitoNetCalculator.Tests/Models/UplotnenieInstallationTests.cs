using System;
using System.IO;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.ViewModels;
using Xunit;

namespace MosquitoNetCalculator.Tests.Models
{
    public class UplotnenieInstallationTests
    {
        [Fact]
        public void Uplotnenie_DefaultPrices_Are100Rubles()
        {
            var defaults = PriceService.DefaultPricesSnapshot()
                .Where(p => p.Name == "Уплотнение")
                .ToList();

            Assert.Equal(2, defaults.Count);
            Assert.All(defaults, p => Assert.Equal(100, p.Price));
        }

        [Fact]
        public void Uplotnenie_Migration_Updates250To100()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ArcCalc_UplotnenieTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var tempPricesPath = Path.Combine(tempDir, "prices.json");

            try
            {
                PriceService.PricesPath = tempPricesPath;
                File.WriteAllText(tempPricesPath, @"[
                    { ""Name"": ""Уплотнение"", ""Color"": ""Серый"", ""Price"": 250.0 },
                    { ""Name"": ""Уплотнение"", ""Color"": ""Чёрный"", ""Price"": 250.0 }
                ]");

                var service = new PriceService();
                var loaded = service.LoadPrices();

                Assert.Equal(100, loaded.First(p => p.Name == "Уплотнение" && p.Color == "Серый").Price);
                Assert.Equal(100, loaded.First(p => p.Name == "Уплотнение" && p.Color == "Чёрный").Price);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void Uplotnenie_IsInstallationApplicable_And_PerLinearMeter()
        {
            Assert.True(ProductCatalog.IsInstallationApplicable("Уплотнение"));
            Assert.True(ProductCatalog.IsPerLinearMeter("Уплотнение"));

            var item = new OrderItem { Name = "Уплотнение" };
            Assert.True(item.IsInstallationApplicable);
            Assert.True(item.IsInstallationPerLinearMeter);
        }

        [Fact]
        public void Uplotnenie_DefaultRates_Mode0_Mode1_Mode2()
        {
            Assert.Equal(160, OrderItem.GetDefaultInstallationAdjustment("Уплотнение"));
            Assert.Equal(0, OrderItem.GetDefaultInstallationDeduction("Уплотнение"));
            Assert.Equal(160, OrderItem.GetDefaultInstallationSurcharge("Уплотнение"));
        }

        [Theory]
        [InlineData(0, 2600.00, 1600.00, "+1 600", "Вкл")] // Mode 0: 10×100 + 160×10 = 2600
        [InlineData(1, 1000.00, 0.00, "", "Без")]          // Mode 1: 10×100 + 0 = 1000
        [InlineData(2, 2600.00, 0.00, "+1 600", "ВК")]     // Mode 2: 10×100 + 160×10 = 2600
        public void Uplotnenie_QuantityOnly_CalculatesCorrectly(int mode, double expectedTotal, double expectedLabor, string expectedAmountDisplay, string expectedButton)
        {
            var vm = new CalculationViewModel();
            var item = vm.AddItem("Уплотнение", "Серый", 0, 0, 10, 100)!;

            item.InstallationMode = mode;

            Assert.Equal(0, item.CalculatedValue);
            Assert.Equal(1.0, item.InstallationLinearMeters);
            Assert.Equal(1000.00, item.Total);
            Assert.Equal(expectedTotal, item.TotalWithDeduction);
            Assert.Equal(expectedLabor, item.InstallationLaborTotal);
            Assert.Equal(expectedAmountDisplay, item.InstallationAmountDisplay);
            Assert.Equal(expectedButton, item.InstallationButtonLabel);
        }

        [Theory]
        [InlineData(0, 1560.00, 960.00, "+960", "Вкл")] // Mode 0: 6.000×100×1 + 160×6 = 1560
        [InlineData(1, 600.00, 0.00, "", "Без")]         // Mode 1: 600 + 0 = 600
        [InlineData(2, 1560.00, 0.00, "+960", "ВК")]     // Mode 2: 600 + 160×6 = 1560
        public void Uplotnenie_PerimeterBased_CalculatesCorrectly(int mode, double expectedTotal, double expectedLabor, string expectedAmountDisplay, string expectedButton)
        {
            var vm = new CalculationViewModel();
            var item = vm.AddItem("Уплотнение", "Серый", 1000, 2000, 1, 100)!;

            item.InstallationMode = mode;

            Assert.Equal(6.000, item.CalculatedValue, 3);
            Assert.Equal(6.000, item.InstallationLinearMeters, 3);
            Assert.Equal(600.00, item.Total);
            Assert.Equal(expectedTotal, item.TotalWithDeduction);
            Assert.Equal(expectedLabor, item.InstallationLaborTotal);
            Assert.Equal(expectedAmountDisplay, item.InstallationAmountDisplay);
            Assert.Equal(expectedButton, item.InstallationButtonLabel);
        }

        [Theory]
        [InlineData(0, 4680.00, 2880.00, "+2 880")] // Mode 0: 6.000×3×100 + 160×18 = 1800 + 2880 = 4680
        [InlineData(1, 1800.00, 0.00, "")]          // Mode 1: 1800 + 0 = 1800
        [InlineData(2, 4680.00, 0.00, "+2 880")]     // Mode 2: 1800 + 160×18 = 4680
        public void Uplotnenie_PerimeterBased_Quantity3_ScalesLinearly(int mode, double expectedTotal, double expectedLabor, string expectedAmountDisplay)
        {
            var vm = new CalculationViewModel();
            var item = vm.AddItem("Уплотнение", "Чёрный", 1000, 2000, 3, 100)!;

            item.InstallationMode = mode;

            Assert.Equal(6.000, item.CalculatedValue, 3);
            Assert.Equal(6.000, item.InstallationLinearMeters, 3);
            Assert.Equal(1800.00, item.Total);
            Assert.Equal(expectedTotal, item.TotalWithDeduction);
            Assert.Equal(expectedLabor, item.InstallationLaborTotal);
            Assert.Equal(expectedAmountDisplay, item.InstallationAmountDisplay);
        }
    }
}
