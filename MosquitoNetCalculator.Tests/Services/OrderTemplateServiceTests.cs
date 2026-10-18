using System;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Система шаблонов заказа (v3.54): каталог всех 4 шаблонов, валидация,
    /// расчет цен и сборка позиций.
    /// </summary>
    public class OrderTemplateServiceTests
    {
        private static Func<string, string, double> CatalogPrice() =>
            (type, color) => AiFactsProvider.GetPrice(type, color);

        // ── Каталог ─────────────────────────────────────────────────

        [Fact]
        public void Catalog_HasFourTemplates_AllAvailable()
        {
            int count = OrderTemplateService.All.Count;
            Assert.Equal(4, count);

            Assert.Equal(new[] { "Окно", "Балконный блок", "Рама", "Француз" },
                OrderTemplateService.All.Select(t => t.Name));

            int available = OrderTemplateService.All.Count(t => t.IsAvailable);
            Assert.Equal(4, available);
            Assert.All(OrderTemplateService.All, t => Assert.True(t.IsAvailable));
        }

        [Fact]
        public void WindowTemplate_RowsMatchOwnerSpec()
        {
            var window = OrderTemplateService.All.Single(t => t.Id == "window");

            Assert.Equal(
                new[] { OrderTemplateService.OrderTemplateGridProduct, "Отлив", "ПСУЛ", "Доставка" },
                window.Rows.Select(r => r.ProductName));

            Assert.All(window.Rows, r => Assert.True(r.DefaultChecked));
            Assert.All(window.Rows, r => Assert.False(r.IsCheckedLocked));
        }

        [Fact]
        public void BalconyTemplate_RowsMatchSpec()
        {
            var balcony = OrderTemplateService.All.Single(t => t.Id == "balcony");

            Assert.Equal(
                new[] { "Сетка", "Отлив", "ПСУЛ", "Доставка" },
                balcony.Rows.Select(r => r.ProductName));

            var gridRow = balcony.Rows.First(r => r.RowKey == "grid");
            Assert.Equal("Дверная сетка", gridRow.DefaultProductType);
            Assert.Equal(0, gridRow.DefaultInstallationMode);
        }

        [Fact]
        public void FrameTemplate_RowsMatchSpec()
        {
            var frame = OrderTemplateService.All.Single(t => t.Id == "frame");

            Assert.Equal(8, frame.Rows.Count);
            Assert.Equal(
                new[] { "grid", "otliv_1", "otliv_2", "kozyrek_1", "kozyrek_2", "brus", "korob", "delivery" },
                frame.Rows.Select(r => r.RowKey));

            var korobRow = frame.Rows.Single(r => r.RowKey == "korob");
            Assert.Equal("Короб", korobRow.ProductName);
            Assert.True(korobRow.IsCustomProduct);
        }

        [Fact]
        public void FrenchTemplate_RowsMatchSpec()
        {
            var french = OrderTemplateService.All.Single(t => t.Id == "french");

            Assert.Equal(5, french.Rows.Count);
            Assert.Equal(
                new[] { "grid", "kozyrek_1", "kozyrek_2", "belt_box", "delivery" },
                french.Rows.Select(r => r.RowKey));

            var beltBox = french.Rows.Single(r => r.RowKey == "belt_box");
            Assert.Equal("Пояс + Короб", beltBox.ProductName);
            Assert.True(beltBox.IsCustomProduct);
        }

        [Fact]
        public void GridProductChoices_MatchScreenProductsCatalog()
        {
            var catalogScreens = ProductCatalog.UserGroups
                .First(g => g.Name == "Москитные сетки").Products;
            Assert.True(catalogScreens.All(p => OrderTemplateService.GridProductChoices.Contains(p)),
                "Каждый экран из каталога должен быть доступен в шаблонах.");
        }

        // ── Валидация ───────────────────────────────────────────────

        [Fact]
        public void Validate_MissingGridSize_ReturnsError_AndPointsToGridRow()
        {
            var state = new OrderTemplateService
            {
                GridProductIndex = 0,
                GridWidth = 0,
                GridHeight = 1200
            };
            Assert.Contains("размеры сетки", state.GetValidationError());
            Assert.Equal("grid-size", state.LastErrorRow);
        }

        [Fact]
        public void Validate_MissingGridType_ReturnsError()
        {
            var state = new OrderTemplateService { GridProductIndex = -1, GridWidth = 800, GridHeight = 1200 };
            Assert.NotNull(state.GetValidationError());
            Assert.Equal("grid-type", state.LastErrorRow);
        }

        [Fact]
        public void Validate_DisabledRows_AreNotValidated()
        {
            var state = new OrderTemplateService
            {
                GridProductIndex = 0, GridWidth = 800, GridHeight = 1200, GridQuantity = 1,
                OtlivEnabled = false,
                OtlivWidth = 0, OtlivHeight = 0,
                PsulEnabled = false,
                DeliveryEnabled = false
            };
            Assert.Null(state.GetValidationError());
        }

        [Fact]
        public void Validate_DisabledGrid_IsNotValidated()
        {
            var state = new OrderTemplateService
            {
                GridEnabled = false,
                GridProductIndex = -1,
                GridWidth = 0, GridHeight = 0,
                OtlivEnabled = false,
                PsulEnabled = false,
                DeliveryEnabled = false
            };
            Assert.Null(state.GetValidationError());
        }

        [Fact]
        public void Validate_EnabledOtlivWithoutDims_ReturnsError()
        {
            var state = new OrderTemplateService
            {
                GridProductIndex = 0, GridWidth = 800, GridHeight = 1200, GridQuantity = 1,
                OtlivEnabled = true,
                OtlivWidth = 0, OtlivHeight = 0,
                PsulEnabled = false,
                DeliveryEnabled = false
            };
            Assert.Contains("отлива", state.GetValidationError());
            Assert.Equal("otliv-size", state.LastErrorRow);
        }

        [Fact]
        public void Validate_PsulWithoutDims_IsAllowed()
        {
            var state = new OrderTemplateService
            {
                GridProductIndex = 0, GridWidth = 800, GridHeight = 1200, GridQuantity = 1,
                PsulEnabled = true, PsulWidth = 0, PsulHeight = 0, PsulQuantity = 1,
                DeliveryEnabled = false
            };
            Assert.Null(state.GetValidationError());
        }

        [Fact]
        public void Validate_FullyFilledState_Passes()
        {
            var state = new OrderTemplateService
            {
                GridProductIndex = 0, GridWidth = 800, GridHeight = 1200, GridQuantity = 2,
                PsulEnabled = true, PsulWidth = 800, PsulHeight = 1200, PsulQuantity = 1,
                DeliveryEnabled = true, DeliveryAmount = 500, DeliveryQuantity = 1,
                OtlivEnabled = true, OtlivWidth = 800, OtlivHeight = 120, OtlivQuantity = 1
            };
            Assert.Null(state.GetValidationError());
        }

        [Fact]
        public void Validate_MissingQuantity_ReturnsError()
        {
            var state = new OrderTemplateService
            {
                GridProductIndex = 0, GridWidth = 800, GridHeight = 1200, GridQuantity = 0,
                PsulEnabled = false, DeliveryEnabled = false
            };
            Assert.Contains("количество сеток", state.GetValidationError());

            state.GridQuantity = 1;
            state.PsulEnabled = true;
            state.PsulQuantity = 0;
            Assert.Contains("количество ПСУЛ", state.GetValidationError());
        }

        [Fact]
        public void Validate_ManualAmountZero_ReturnsError()
        {
            var state = new OrderTemplateService
            {
                GridProductIndex = 0, GridWidth = 800, GridHeight = 1200, GridQuantity = 1,
                PsulEnabled = false,
                DeliveryEnabled = true,
                DeliveryAmount = 0,
                DeliveryQuantity = 1
            };
            var err = state.GetValidationError();
            Assert.NotNull(err);
            Assert.Contains("Укажите сумму", err);
            Assert.Contains("Доставка", err);
            Assert.Equal("delivery", state.LastErrorRow);
        }

        [Fact]
        public void Validate_CustomProductZeroAmount_ReturnsError()
        {
            var frame = OrderTemplateService.All.Single(t => t.Id == "frame");
            var state = new OrderTemplateService();
            state.InitForTemplate(frame);

            // Заполняем сетку, отливы, козырьки, брус
            state.RowStates["grid"].Width = 1000;
            state.RowStates["grid"].Height = 1500;
            state.RowStates["grid"].Quantity = 1;

            state.RowStates["otliv_1"].Width = 1000;
            state.RowStates["otliv_1"].Height = 150;
            state.RowStates["otliv_1"].Quantity = 1;

            state.RowStates["otliv_2"].Width = 1000;
            state.RowStates["otliv_2"].Height = 150;
            state.RowStates["otliv_2"].Quantity = 1;

            state.RowStates["kozyrek_1"].Width = 1000;
            state.RowStates["kozyrek_1"].Height = 200;
            state.RowStates["kozyrek_1"].Quantity = 1;

            state.RowStates["kozyrek_2"].Width = 1000;
            state.RowStates["kozyrek_2"].Height = 200;
            state.RowStates["kozyrek_2"].Quantity = 1;

            state.RowStates["brus"].ManualAmount = 500;
            state.RowStates["brus"].Quantity = 1;

            state.RowStates["delivery"].ManualAmount = 600;
            state.RowStates["delivery"].Quantity = 1;

            // Короб: количество 1, но сумма 0!
            state.RowStates["korob"].Quantity = 1;
            state.RowStates["korob"].ManualAmount = 0;

            var err = state.GetValidationError(frame);
            Assert.NotNull(err);
            Assert.Contains("Укажите сумму (Короб)", err);
            Assert.Equal("korob", state.LastErrorRow);
        }

        [Fact]
        public void CalculateTotal_ZeroQuantity_ReturnsZero()
        {
            var rowState = new OrderTemplateService.TemplateRowState
            {
                RowKey = "grid",
                BaseProductName = "Сетка",
                SelectedProductType = "Anwis",
                Color = "Белый",
                Width = 1000,
                Height = 1500,
                Quantity = 0
            };
            Assert.Equal(0, rowState.CalculateTotal(CatalogPrice()));

            var brusState = new OrderTemplateService.TemplateRowState
            {
                RowKey = "brus",
                BaseProductName = "Брус",
                ManualAmount = 500,
                Quantity = 0
            };
            Assert.Equal(0, brusState.CalculateTotal(CatalogPrice()));
        }

        [Fact]
        public void Delivery_WithoutQuantity_CalculatesTotalAndPassesValidation()
        {
            var state = new OrderTemplateService
            {
                GridProductIndex = 0, GridWidth = 800, GridHeight = 1200, GridQuantity = 1,
                PsulEnabled = false,
                DeliveryEnabled = true,
                DeliveryAmount = 500,
                DeliveryQuantity = 0 // кол-во не указано
            };
            Assert.Null(state.GetValidationError());

            var row = state.RowStates["delivery"];
            Assert.Equal(500, row.CalculateTotal(CatalogPrice()));

            var window = OrderTemplateService.All.Single(t => t.Id == "window");
            var specs = state.BuildItemSpecs(window, CatalogPrice());
            var deliverySpec = specs.Single(s => s.RowKey == "delivery");
            Assert.Equal(1, deliverySpec.Quantity);
            Assert.Equal(500, deliverySpec.Price);
        }

        // ── Резолв цен и расчёт ──────────────────────────────────────

        [Fact]
        public void ResolvePrice_ManualPiece_IsZero()
        {
            Assert.Equal(0, OrderTemplateService.ResolveRowPrice("Доставка", "", CatalogPrice()));
        }

        [Fact]
        public void ResolvePrice_CatalogProducts_TakeCatalogPrice()
        {
            Assert.Equal(1800, OrderTemplateService.ResolveRowPrice("Anwis", "Белый", CatalogPrice()));
            Assert.Equal(2150, OrderTemplateService.ResolveRowPrice("Отлив", "Белый", CatalogPrice()));
            Assert.Equal(100, OrderTemplateService.ResolveRowPrice("ПСУЛ", "", CatalogPrice()));
            Assert.Equal(2150, OrderTemplateService.ResolveRowPrice("Козырёк", "Белый", CatalogPrice()));
        }

        [Fact]
        public void CalculateTotal_GridWithAreaAndAnticatAndImpost()
        {
            var rowState = new OrderTemplateService.TemplateRowState
            {
                RowKey = "grid",
                BaseProductName = "Сетка",
                SelectedProductType = "Anwis",
                Color = "Белый",
                Width = 1000,
                Height = 1500,
                Quantity = 1,
                Anticat = true,
                InstallationMode = 2 // В конструкцию (0 руб.)
            };

            // Anwis Белый = 1800 + 2000 (Антикошка) = 3800 руб/м2
            // Площадь 1000*1500 / 1 000 000 = 1.500 м2 -> 1.5 * 3800 = 5700
            // Импост (H >= 1500): +200 * (1000 / 1000) * 1 = +200
            // Итого: 5700 + 200 = 5900 руб.
            double total = rowState.CalculateTotal(CatalogPrice());
            Assert.Equal(5900, total);
        }

        // ── Сборка позиций ──────────────────────────────────────────

        [Fact]
        public void BuildSpecs_DefaultWindow_ProducesFourRows_InOwnerOrder()
        {
            var window = OrderTemplateService.All.Single(t => t.Id == "window");
            var state = new OrderTemplateService
            {
                GridProductIndex = 0, GridWidth = 800, GridHeight = 1200,
                OtlivEnabled = true, OtlivWidth = 800, OtlivHeight = 120,
                PsulWidth = 800, PsulHeight = 1200
            };

            var specs = state.BuildItemSpecs(window, CatalogPrice());

            Assert.Equal(
                new[] { "grid", "otliv", "psul", "delivery" },
                specs.Select(s => s.RowKey));
            Assert.Equal("Anwis", specs[0].Type);
        }

        [Fact]
        public void BuildSpecs_DisabledPsul_IsExcluded()
        {
            var window = OrderTemplateService.All.Single(t => t.Id == "window");
            var state = new OrderTemplateService
            {
                GridProductIndex = 0, GridWidth = 800, GridHeight = 1200,
                PsulEnabled = false
            };

            var specs = state.BuildItemSpecs(window, CatalogPrice());

            Assert.DoesNotContain(specs, s => s.RowKey == "psul");
            Assert.Equal(new[] { "grid", "delivery" }, specs.Select(s => s.RowKey));
        }

        [Fact]
        public void BuildSpecs_EnabledOtliv_IsIncluded_WithInstallation()
        {
            var window = OrderTemplateService.All.Single(t => t.Id == "window");
            var state = new OrderTemplateService
            {
                GridProductIndex = 0, GridWidth = 800, GridHeight = 1200,
                OtlivEnabled = true, OtlivWidth = 800, OtlivHeight = 120,
                OtlivInstallationMode = 2
            };

            var specs = state.BuildItemSpecs(window, CatalogPrice());

            var otliv = specs.Single(s => s.RowKey == "otliv");
            Assert.Equal("Отлив", otliv.Type);
            Assert.Equal(2150, otliv.Price);
            Assert.Equal(2, otliv.InstallationMode);
        }

        [Fact]
        public void BuildSpecs_FrameTemplate_GeneratesAllEightItems()
        {
            var frame = OrderTemplateService.All.Single(t => t.Id == "frame");
            var state = new OrderTemplateService();
            state.InitForTemplate(frame);

            state.RowStates["grid"].Width = 600;
            state.RowStates["grid"].Height = 1400;
            state.RowStates["otliv_1"].Width = 1000;
            state.RowStates["otliv_1"].Height = 150;
            state.RowStates["otliv_2"].Width = 1200;
            state.RowStates["otliv_2"].Height = 200;
            state.RowStates["kozyrek_1"].Width = 1000;
            state.RowStates["kozyrek_1"].Height = 250;
            state.RowStates["kozyrek_2"].Width = 1200;
            state.RowStates["kozyrek_2"].Height = 300;
            state.RowStates["brus"].ManualAmount = 700;
            state.RowStates["korob"].ManualAmount = 1500;
            state.RowStates["delivery"].ManualAmount = 500;

            var specs = state.BuildItemSpecs(frame, CatalogPrice());
            Assert.Equal(8, specs.Count);

            var korobSpec = specs.Single(s => s.RowKey == "korob");
            Assert.Equal("Короб", korobSpec.Type);
            Assert.True(korobSpec.IsCustomProduct);
            Assert.Equal(1500, korobSpec.Price);
        }

        [Fact]
        public void BuildSpecs_FrenchTemplate_GeneratesAllFiveItems()
        {
            var french = OrderTemplateService.All.Single(t => t.Id == "french");
            var state = new OrderTemplateService();
            state.InitForTemplate(french);

            state.RowStates["grid"].Width = 700;
            state.RowStates["grid"].Height = 2000;
            state.RowStates["kozyrek_1"].Width = 1500;
            state.RowStates["kozyrek_1"].Height = 300;
            state.RowStates["kozyrek_2"].Width = 1500;
            state.RowStates["kozyrek_2"].Height = 300;
            state.RowStates["belt_box"].ManualAmount = 2500;
            state.RowStates["delivery"].ManualAmount = 500;

            var specs = state.BuildItemSpecs(french, CatalogPrice());
            Assert.Equal(5, specs.Count);

            var beltBox = specs.Single(s => s.RowKey == "belt_box");
            Assert.Equal("Пояс + Короб", beltBox.Type);
            Assert.True(beltBox.IsCustomProduct);
            Assert.Equal(2500, beltBox.Price);
        }

        [Fact]
        public void BuildSpecs_AnwisType_CarriesModeAndAnticat()
        {
            var window = OrderTemplateService.All.Single(t => t.Id == "window");
            var state = new OrderTemplateService
            {
                GridProductIndex = 0, GridWidth = 739, GridHeight = 1116,
                GridAnwisMode = AnwisSizeMode.РазмерПроёма,
                GridAnticat = true
            };

            var specs = state.BuildItemSpecs(window, CatalogPrice());

            var grid = specs.Single(s => s.RowKey == "grid");
            Assert.Equal(AnwisSizeMode.РазмерПроёма, grid.AnwisMode);
            Assert.True(grid.Anticat);
        }

        [Fact]
        public void BuildSpecs_NonAnwisType_HasNoAnwisMode()
        {
            int doorIndex = Array.IndexOf(OrderTemplateService.GridProductChoices, "Дверная сетка");
            var window = OrderTemplateService.All.Single(t => t.Id == "window");
            var state = new OrderTemplateService
            {
                GridProductIndex = doorIndex, GridWidth = 700, GridHeight = 2000
            };

            var specs = state.BuildItemSpecs(window, CatalogPrice());

            var grid = specs.Single(s => s.RowKey == "grid");
            Assert.Equal("Дверная сетка", grid.Type);
            Assert.Null(grid.AnwisMode);
            Assert.False(grid.Anticat);
        }

        [Fact]
        public void BuildSpecs_DisabledGrid_IsExcluded_OthersKept()
        {
            var window = OrderTemplateService.All.Single(t => t.Id == "window");
            var state = new OrderTemplateService
            {
                GridEnabled = false,
                GridProductIndex = 0, GridWidth = 800, GridHeight = 1200,
                OtlivEnabled = true, OtlivWidth = 800, OtlivHeight = 120,
                PsulWidth = 800, PsulHeight = 1200
            };

            var specs = state.BuildItemSpecs(window, CatalogPrice());

            Assert.DoesNotContain(specs, s => s.RowKey == "grid");
            Assert.Equal(new[] { "otliv", "psul", "delivery" }, specs.Select(s => s.RowKey));
        }

        [Fact]
        public void BuildSpecs_PsulWithoutDims_KeepsZeroDims()
        {
            var window = OrderTemplateService.All.Single(t => t.Id == "window");
            var state = new OrderTemplateService
            {
                GridProductIndex = 0, GridWidth = 800, GridHeight = 1200,
                PsulEnabled = true, PsulWidth = 0, PsulHeight = 0
            };

            var specs = state.BuildItemSpecs(window, CatalogPrice());

            var psul = specs.Single(s => s.RowKey == "psul");
            Assert.Equal(0, psul.Width);
            Assert.Equal(0, psul.Height);
            Assert.Equal(1, psul.Quantity);
        }
    }
}
