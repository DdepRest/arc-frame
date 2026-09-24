using System;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Система шаблонов заказа (v3.54): каталог, матрица валидации,
    /// резолв цен и сборка позиций. Чистая логика OrderTemplateService —
    /// без WPF (образец — AiPlanValidatorTests).
    /// </summary>
    public class OrderTemplateServiceTests
    {
        private static Func<string, string, double> CatalogPrice() =>
            (type, color) => AiFactsProvider.GetPrice(type, color);

        // ── Каталог ─────────────────────────────────────────────────

        [Fact]
        public void Catalog_HasFourTemplates_OnlyWindowAvailable()
        {
            int count = OrderTemplateService.All.Count;
            Assert.Equal(4, count);

            Assert.Equal(new[] { "Окно", "Балконный блок", "Рама", "Француз" },
                OrderTemplateService.All.Select(t => t.Name));

            int available = OrderTemplateService.All.Count(t => t.IsAvailable);
            Assert.Equal(1, available);
            Assert.True(OrderTemplateService.All.Single(t => t.Id == "window").IsAvailable);
        }

        [Fact]
        public void WindowTemplate_RowsMatchOwnerSpec()
        {
            var window = OrderTemplateService.All.Single(t => t.Id == "window");

            Assert.Equal(
                new[] { OrderTemplateService.OrderTemplateGridProduct, "ПСУЛ", "Доставка", "Отлив" },
                window.Rows.Select(r => r.ProductName));

            // Дефолты: сетка/ПСУЛ/доставка включены, отлив opt-in;
            // сетка — обязательная (нельзя выключить).
            Assert.True(window.Rows[0].DefaultChecked);
            Assert.True(window.Rows[0].IsCheckedLocked);
            Assert.True(window.Rows[1].DefaultChecked);
            Assert.False(window.Rows[1].IsCheckedLocked);
            Assert.True(window.Rows[2].DefaultChecked);
            Assert.False(window.Rows[3].DefaultChecked);
        }

        [Fact]
        public void GridProductChoices_MatchScreenProductsCatalog()
        {
            // Типы сеток в шаблоне — подмножество группы «Москитные сетки» каталога UX.
            var catalogScreens = ProductCatalog.UserGroups
                .First(g => g.Name == "Москитные сетки").Products;
            Assert.True(catalogScreens.All(p => OrderTemplateService.GridProductChoices.Contains(p)),
                "Каждый экран из каталога должен быть доступен в шаблоне «Окно».");
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
            // Отлив выключен («Не требуется») — пустые размеры не мешают.
            var state = new OrderTemplateService
            {
                GridProductIndex = 0, GridWidth = 800, GridHeight = 1200,
                OtlivEnabled = false,
                OtlivWidth = 0, OtlivHeight = 0
            };
            Assert.Null(state.GetValidationError());
        }

        [Fact]
        public void Validate_EnabledOtlivWithoutDims_ReturnsError()
        {
            var state = new OrderTemplateService
            {
                GridProductIndex = 0, GridWidth = 800, GridHeight = 1200,
                OtlivEnabled = true,
                OtlivWidth = 0, OtlivHeight = 0
            };
            Assert.Contains("отлива", state.GetValidationError());
            Assert.Equal("otliv-size", state.LastErrorRow);
        }

        [Fact]
        public void Validate_PsulWithoutDims_IsAllowed()
        {
            // ПСУЛ с пустыми размерами валиден: расчёт по количеству.
            var state = new OrderTemplateService
            {
                GridProductIndex = 0, GridWidth = 800, GridHeight = 1200,
                PsulEnabled = true, PsulWidth = 0, PsulHeight = 0
            };
            Assert.Null(state.GetValidationError());
        }

        [Fact]
        public void Validate_FullyFilledState_Passes()
        {
            var state = new OrderTemplateService
            {
                GridProductIndex = 0, GridWidth = 800, GridHeight = 1200, GridQuantity = 2,
                PsulEnabled = true, PsulWidth = 800, PsulHeight = 1200,
                DeliveryEnabled = true, DeliveryAmount = 500,
                OtlivEnabled = true, OtlivWidth = 800, OtlivHeight = 120, OtlivQuantity = 1
            };
            Assert.Null(state.GetValidationError());
        }

        // ── Резолв цен ──────────────────────────────────────────────

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
        }

        // ── Сборка позиций ──────────────────────────────────────────

        [Fact]
        public void BuildSpecs_DefaultWindow_ProducesThreeRows()
        {
            var window = OrderTemplateService.All.Single(t => t.Id == "window");
            var state = new OrderTemplateService
            {
                GridProductIndex = 0, GridWidth = 800, GridHeight = 1200,
                PsulWidth = 800, PsulHeight = 1200
            };

            var specs = state.BuildItemSpecs(window, CatalogPrice());

            Assert.Equal(
                new[] { "grid", "psul", "delivery" },
                specs.Select(s => s.RowKey));
            Assert.Equal("Anwis", specs[0].Type); // первый тип в списке
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
                OtlivInstallationMode = 0
            };

            var specs = state.BuildItemSpecs(window, CatalogPrice());

            var otliv = specs.Single(s => s.RowKey == "otliv");
            Assert.Equal("Отлив", otliv.Type);
            Assert.Equal(2150, otliv.Price);
            Assert.Equal(0, otliv.InstallationMode);
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
            Assert.Null(grid.AnwisMode);       // не-Anwis: режим не задаётся
            Assert.False(grid.Anticat);        // и Антикошка не включается
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
