using System.Windows.Controls;
using MosquitoNetCalculator.Converters;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.ViewModels;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Models
{
    public class LaborRenameAndGridHeaderTests
    {
        [Fact]
        public void OrderItem_Labor_CanBeRenamedToAnything_RetainsLaborCalculations()
        {
            var vm = new CalculationViewModel();
            // User adds "Работа", then renames to "Демонтаж"
            var item = vm.AddItem("Работа", "", 0, 0, 2, 1200, isLabor: true);
            Assert.NotNull(item);
            Assert.True(item!.IsLabor);
            Assert.Equal(2400, item.Total);
            Assert.Equal(2400, item.InstallationLaborTotal);
            Assert.Equal("шт.", item.Unit);

            // Rename to arbitrary custom string
            item.Name = "Демонтаж старых конструкций";
            Assert.True(item.IsLabor);
            Assert.True(item.IsManualPiece);
            Assert.Equal(2400, item.Total);
            Assert.Equal(2400, item.InstallationLaborTotal);
            Assert.Equal("2 400 руб.", item.InstallationLaborDisplay);

            var totalInfo = vm.CalculateTotal(0);
            Assert.Equal(2400, totalInfo.Total);
            Assert.Equal(2400, totalInfo.TotalInstallation);
        }

        [Fact]
        public void OrderItemData_Serialization_PreservesIsLaborFlag()
        {
            var item = new OrderItem
            {
                Name = "Сложный замер",
                IsLabor = true,
                Price = 500,
                Quantity = 1
            };
            item.Recalculate();

            var dto = item.ToOrderItemData();
            Assert.True(dto.IsLabor);

            var loaded = new OrderItem
            {
                Name = dto.Name,
                IsLabor = dto.IsLabor,
                Price = dto.Price,
                Quantity = dto.Quantity
            };
            loaded.Recalculate();

            Assert.True(loaded.IsLabor);
            Assert.Equal(500, loaded.Total);
            Assert.Equal(500, loaded.InstallationLaborTotal);
        }

        [Fact]
        public void UppercaseHeaderTemplate_DoesNotCorruptVisualHeaders()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var grid = new DataGrid();
                var colIcon = new DataGridTextColumn
                {
                    Header = new TextBlock { Text = "\uE73A" }
                };
                var colText = new DataGridTextColumn
                {
                    Header = "наименование"
                };

                grid.Columns.Add(colIcon);
                grid.Columns.Add(colText);

                UppercaseHeaderTemplate.Apply(grid);

                // colIcon HeaderTemplate must be null, keeping the TextBlock icon intact without converting to "SYS..."
                Assert.Null(colIcon.HeaderTemplate);
                // colText receives the Uppercase HeaderTemplate
                Assert.NotNull(colText.HeaderTemplate);
            });
        }
    }
}
