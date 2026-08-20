using System.Linq;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Stage-2 hardening: <see cref="AiFactsProvider"/> is the single
    /// source of truth for AI-side prices, colors and catalog traits.
    /// These tests prove the AI side agrees with the canonical
    /// <c>PriceService.DefaultPrices</c> table — if the two ever drift
    /// apart the prompt and the catalog would silently disagree.
    /// </summary>
    public class AiFactsProviderTests
    {
        [Fact]
        public void Golden_DefaultPrices_Match_PriceService()
        {
            var snapshot = PriceService.DefaultPricesSnapshot();
            // Anwis Белый = 1800 — confirmed canonical entry.
            Assert.Equal(1800, AiFactsProvider.GetPrice("Anwis", "Белый"));
            Assert.Contains(snapshot, p => p.Name == "Anwis" && p.Color == "Белый" && p.Price == 1800);

            // Anwis Коричневый = 1900
            Assert.Equal(1900, AiFactsProvider.GetPrice("Anwis", "Коричневый"));

            // Золотой дуб = 2650, plain белый — 2150
            Assert.Equal(2650, AiFactsProvider.GetPrice("Отлив", "Золотой дуб"));
            Assert.Equal(2150, AiFactsProvider.GetPrice("Отлив", "Белый"));
            Assert.Equal(2150, AiFactsProvider.GetPrice("Козырёк", "Антрацит"));
            Assert.Equal(2650, AiFactsProvider.GetPrice("Короб", "Золотой дуб"));
        }

        [Fact]
        public void GetPrice_UnknownProduct_ReturnsZero()
        {
            Assert.Equal(0, AiFactsProvider.GetPrice("ТоварИзДругойВселенной", "Белый"));
        }

        [Fact]
        public void GetPrice_ColorMiss_FallsBackToColorlessRow()
        {
            // Manual-piece color entries with empty color: Доставка БезЦвета → 0
            // (manual entry; price is set by the operator not the catalog).
            var dosPrice = AiFactsProvider.GetPrice("Доставка", "Белый");
            // Доставка has no color-specific row, so the empty-color row wins → 0.
            Assert.Equal(0, dosPrice);
        }

        [Fact]
        public void ColorsByProduct_IsConsistent_WithPriceList()
        {
            // Every product in the color table must be a known catalog product
            // — otherwise the AI prompt would invite the user to pick an
            // unpriced color.
            foreach (var name in AiFactsProvider.ColorsByProduct.Keys)
                Assert.True(AiFactsProvider.IsKnownProduct(name), $"Color table has unknown product «{name}»");

            // Sanity: Отлив / Козырёк / Короб all carry the same 4 colors.
            Assert.Equal(new[] { "Белый", "Коричневый", "Антрацит", "Золотой дуб" },
                AiFactsProvider.GetColorsFor("Отлив"));
            Assert.Equal(AiFactsProvider.GetColorsFor("Отлив"),
                AiFactsProvider.GetColorsFor("Козырёк"));
            Assert.Equal(AiFactsProvider.GetColorsFor("Отлив"),
                AiFactsProvider.GetColorsFor("Короб"));
        }

        [Fact]
        public void GetColorsFor_NoColorProduct_ReturnsEmpty()
        {
            // Manual-piece products that have no color choice: «Материал», «ПСУЛ», etc.
            // ПСУЛ is a linear-meter product — no color, no need for a palette.
            Assert.Empty(AiFactsProvider.GetColorsFor("ПСУЛ"));
        }

        [Fact]
        public void GetDefaultColor_ReturnsFirstInPalette()
        {
            // Документированное текущее поведение: первый цвет палитры —
            // пока владелец не выбрал вариант A/B hardening §4.2 (см.
            // AI_DEFAULTS_POLICY.md).
            Assert.Equal("Белый", AiFactsProvider.GetDefaultColor("Anwis"));
            Assert.Equal("Белый", AiFactsProvider.GetDefaultColor("Отлив"));
            Assert.Equal("Серый", AiFactsProvider.GetDefaultColor("Уплотнение"));
        }

        [Fact]
        public void GetDefaultColor_UnknownProduct_ReturnsEmpty()
        {
            Assert.Equal("", AiFactsProvider.GetDefaultColor("?"));
        }

        [Fact]
        public void HasColors_RespectsProductCatalog_IsNoColor()
        {
            // Anwis has colors, ПСУЛ doesn't, even though it's in the catalog.
            Assert.True(AiFactsProvider.HasColors("Anwis"));
            Assert.False(AiFactsProvider.HasColors("ПСУЛ"));
        }

        [Fact]
        public void IsKnownProduct_KnownAndUnknownNames()
        {
            Assert.True(AiFactsProvider.IsKnownProduct("Anwis"));
            Assert.False(AiFactsProvider.IsKnownProduct("NotAProduct"));
        }

        [Fact]
        public void AllThinPredicates_BehaveSensibly()
        {
            // Thin predicates must agree with the catalog-level predicates
            // (ProductCatalog is internal — assert the observable shapes).
            Assert.True(AiFactsProvider.IsInstallationApplicable("Отлив"));
            Assert.True(AiFactsProvider.IsInstallationApplicable("Anwis"));
            Assert.False(AiFactsProvider.IsInstallationApplicable("Короб"));
            Assert.False(AiFactsProvider.IsInstallationApplicable("Доставка"));
            Assert.True(AiFactsProvider.IsManualPiece("Доставка"));
            Assert.False(AiFactsProvider.IsManualPiece("Anwis"));
            Assert.True(AiFactsProvider.IsAnwisApplicable("Anwis"));
            Assert.False(AiFactsProvider.IsAnwisApplicable("Отлив"));
        }
    }
}
