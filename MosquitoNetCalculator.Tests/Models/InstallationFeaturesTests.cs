using System;
using System.IO;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.ViewModels;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Models
{
    public class InstallationFeaturesTests : IDisposable
    {
        private readonly string _tempSettingsDir;
        private readonly string _originalSettingsPath;

        public InstallationFeaturesTests()
        {
            _originalSettingsPath = AppSettingsService.SettingsPath;
            _tempSettingsDir = Path.Combine(Path.GetTempPath(), "MncTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempSettingsDir);
            AppSettingsService.SettingsPath = Path.Combine(_tempSettingsDir, "settings.json");
            AppSettingsService.InvalidateCache();
        }

        public void Dispose()
        {
            AppSettingsService.SettingsPath = _originalSettingsPath;
            AppSettingsService.InvalidateCache();
            if (Directory.Exists(_tempSettingsDir))
            {
                try { Directory.Delete(_tempSettingsDir, true); } catch { }
            }
        }

        [Fact]
        public void AppSettings_AskInstallationModeAndDefaultMode_PersistCorrectly()
        {
            // По умолчанию включено (true)
            Assert.True(AppSettingsService.LoadAskInstallationModeOnAdd());
            Assert.Equal(-1, AppSettingsService.LoadDefaultInstallationMode());

            AppSettingsService.SaveAskInstallationModeOnAdd(false);
            AppSettingsService.SaveDefaultInstallationMode(2); // В конструкцию

            Assert.False(AppSettingsService.LoadAskInstallationModeOnAdd());
            Assert.Equal(2, AppSettingsService.LoadDefaultInstallationMode());
        }

        [Fact]
        public void OrderItem_InstallationLaborTotal_CalculatesCorrectlyForPieceItems()
        {
            // Anwis: 500 ₽/шт
            var anwis = new OrderItem
            {
                Name = "Anwis",
                Width = 1000,
                Height = 1000,
                Quantity = 3,
                Price = 1800,
                InstallationMode = 0 // Учтён
            };

            Assert.Equal(1200, anwis.InstallationLaborTotal);
            Assert.Equal("1 200 руб.", anwis.InstallationLaborDisplay);

            // Режим 1: Без монтажа -> 0 ₽
            anwis.InstallationMode = 1;
            Assert.Equal(0, anwis.InstallationLaborTotal);
            Assert.Equal("—", anwis.InstallationLaborDisplay);

            // Режим 2: В конструкцию -> 200 ₽/шт
            anwis.InstallationMode = 2;
            Assert.Equal(600, anwis.InstallationLaborTotal);
            Assert.Equal("600 руб.", anwis.InstallationLaborDisplay);
        }

        [Fact]
        public void OrderItem_InstallationLaborTotal_CalculatesCorrectlyForDoorNet()
        {
            // Дверная сетка: 600 ₽/шт
            var door = new OrderItem
            {
                Name = "Дверная сетка",
                Width = 800,
                Height = 2000,
                Quantity = 2,
                Price = 3000,
                InstallationMode = 0
            };

            Assert.Equal(1200, door.InstallationLaborTotal);
        }

        [Fact]
        public void OrderItem_InstallationLaborTotal_CalculatesCorrectlyForPerLinearMeter()
        {
            // Отлив: 500 ₽/м.п. длины (большая сторона)
            // 200 x 1500 мм -> max = 1500 мм = 1.5 м.п.
            // 2 шт. -> 1.5 * 2 * 500 = 1500 ₽
            var otliv = new OrderItem
            {
                Name = "Отлив",
                Width = 200,
                Height = 1500,
                Quantity = 2,
                Price = 2150,
                InstallationMode = 0
            };

            Assert.Equal(1500, otliv.InstallationLaborTotal);
        }

        [Fact]
        public void OrderItem_Otliv_ExactUserCase_Calculates775Not1550()
        {
            // Отлив: 500 x 1550 мм -> max = 1550 мм = 1.55 м.п.
            // При режиме 0 (Учтён): 1.55 * 500 * 1 = 775 ₽
            var vm = new CalculationViewModel();
            var item = vm.AddItem("Отлив", "Белый", 500, 1550, 1, 1000);
            Assert.NotNull(item);
            item!.InstallationMode = 0; // Учтён

            Assert.Equal(775, item.InstallationLaborTotal);

            var totalInfo = vm.CalculateTotal(0);
            Assert.Equal(775, totalInfo.TotalInstallation);
        }

        [Fact]
        public void OrderItem_Labor_IsCountedInInstallationLaborTotal()
        {
            // «Работа» должна учитываться в «Монтаж»
            var vm = new CalculationViewModel();
            var workItem = vm.AddItem("Работа", string.Empty, 0, 0, 1, 1500);
            Assert.NotNull(workItem);

            Assert.True(ProductCatalog.IsLabor(workItem!.Name));
            Assert.Equal(1500, workItem.Total);
            Assert.Equal(1500, workItem.InstallationLaborTotal);
            Assert.Equal("1 500 руб.", workItem.InstallationLaborDisplay);
            Assert.Equal("Вкл", workItem.InstallationButtonLabel);
            Assert.Equal("+1 500", workItem.InstallationAmountDisplay);

            var totalInfo = vm.CalculateTotal(0);
            Assert.Equal(1500, totalInfo.TotalInstallation);
        }

        [Fact]
        public void OrderItem_CustomLaborName_IsCountedInInstallationLaborTotal()
        {
            // Пользователь меняет название на «Работа за демонтаж»
            var vm = new CalculationViewModel();
            var workItem = vm.AddItem("Работа за демонтаж", string.Empty, 0, 0, 2, 800);
            Assert.NotNull(workItem);

            Assert.True(ProductCatalog.IsLabor(workItem!.Name));
            Assert.Equal(1600, workItem.Total);
            Assert.Equal(1600, workItem.InstallationLaborTotal);
            Assert.Equal("1 600 руб.", workItem.InstallationLaborDisplay);

            var totalInfo = vm.CalculateTotal(0);
            Assert.Equal(1600, totalInfo.TotalInstallation);
        }

        [Fact]
        public void OrderItem_Delivery_HasZeroInstallationLabor()
        {
            // Доставка не является монтажом
            var delivery = new OrderItem
            {
                Name = "Доставка",
                Quantity = 1,
                Price = 1000
            };

            Assert.False(delivery.IsInstallationApplicable);
            Assert.False(ProductCatalog.IsLabor(delivery.Name));
            Assert.Equal(0, delivery.InstallationLaborTotal);
        }

        [Fact]
        public void CalculationViewModel_CalculateTotal_AggregatesInstallationLaborTotalWithLabor()
        {
            var vm = new CalculationViewModel();
            var item1 = vm.AddItem("Anwis", "Белый", 1000, 1000, 2, 1800); // 2 * 400 = 800 ₽
            var item2 = vm.AddItem("Дверная сетка", "Белый", 800, 2000, 1, 3000); // 1 * 600 = 600 ₽
            var item3 = vm.AddItem("Работа", string.Empty, 0, 0, 1, 1500); // 1500 ₽ монтаж

            var totalInfo = vm.CalculateTotal(0);

            Assert.Equal(2900, totalInfo.TotalInstallation);
        }

        [Fact]
        public void InstallationChoiceDialog_Otliv_ShowsCorrectDescription()
        {
            var otliv = new OrderItem
            {
                Name = "Отлив",
                Color = "Белый",
                Width = 500,
                Height = 2000,
                Quantity = 1,
                Price = 2150
            };

            // Test on STA thread
            WpfTestHelper.RunOnSta(() =>
            {
                var dlg = new InstallationChoiceDialog(otliv);
                Assert.Contains("рассчитывается отдельно", dlg.TxtMode0Desc.Text);
                Assert.Contains("+500 руб./м.п.", dlg.TxtMode0Desc.Text);
                Assert.Contains("0 руб.", dlg.TxtMode1Desc.Text);
                Assert.DoesNotContain("−500", dlg.TxtMode1Desc.Text);
                Assert.Contains("при сборке конструкции (0 руб.)", dlg.TxtMode2Desc.Text);
                Assert.DoesNotContain("сборке окна", dlg.TxtMode2Desc.Text);
                Assert.Contains("Отлив", dlg.TxtMode2Desc.Text);
                Assert.DoesNotContain("Сетка", dlg.TxtMode2Desc.Text);
            });
        }

        [Fact]
        public void InstallationChoiceDialog_Net_ShowsCorrectDescription()
        {
            var net = new OrderItem
            {
                Name = "Anwis",
                Color = "Белый",
                Width = 600,
                Height = 1400,
                Quantity = 1,
                Price = 1800
            };

            WpfTestHelper.RunOnSta(() =>
            {
                var dlg = new InstallationChoiceDialog(net);
                Assert.Contains("заложен в стоимость", dlg.TxtMode0Desc.Text);
                Assert.Contains("+400 руб. к ЗП", dlg.TxtMode0Desc.Text);
                Assert.Contains("−500 руб.", dlg.TxtMode1Desc.Text);
                Assert.Contains("при сборке конструкции (+200 руб. к ЗП)", dlg.TxtMode2Desc.Text);
                Assert.DoesNotContain("сборке окна", dlg.TxtMode2Desc.Text);
            });
        }

        [Fact]
        public void InstallationChoiceDialog_PreselectsMode()
        {
            var net = new OrderItem
            {
                Name = "Anwis",
                Width = 600,
                Height = 1400,
                Quantity = 1,
                Price = 1800
            };

            WpfTestHelper.RunOnSta(() =>
            {
                var dlg = new InstallationChoiceDialog(net, initialMode: 1);
                Assert.Equal(1, dlg.SelectedMode);
            });
        }

        [Fact]
        public void CalculationViewModel_AddItem_RespectsDefaultInstallationMode()
        {
            var vm = new CalculationViewModel();

            // Default: -1 (По товару / каталогу)
            AppSettingsService.SaveDefaultInstallationMode(-1);
            var defaultNet = vm.AddItem("Anwis", "Белый", 600, 1400, 1, 1800);
            var defaultOtliv = vm.AddItem("Отлив", "Белый", 500, 1500, 1, 2000);
            Assert.Equal(0, defaultNet!.InstallationMode);
            Assert.Equal(1, defaultOtliv!.InstallationMode);

            // User chose 1 (Без монтажа) as standard default
            AppSettingsService.SaveDefaultInstallationMode(1);
            var noInstallNet = vm.AddItem("Anwis", "Белый", 600, 1400, 1, 1800);
            var noInstallOtliv = vm.AddItem("Отлив", "Белый", 500, 1500, 1, 2000);
            Assert.Equal(1, noInstallNet!.InstallationMode);
            Assert.Equal(1, noInstallOtliv!.InstallationMode);

            // User chose 2 (В конструкцию)
            AppSettingsService.SaveDefaultInstallationMode(2);
            var inFrameNet = vm.AddItem("Anwis", "Белый", 600, 1400, 1, 1800);
            Assert.Equal(2, inFrameNet!.InstallationMode);
        }
    }
}
