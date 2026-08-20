using System;
using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Single source of «truth» for the AI subsystem:
    /// canonical prices (delegating to <see cref="PriceService"/>),
    /// the color palette per product (formerly <c>AiClarificationForm.ColorMap</c>)
    /// and thin predicates that compose <see cref="ProductCatalog"/> /
    /// <see cref="AnwisSizeService"/> for downstream consumers.
    ///
    /// Every AI-side helper asks here; the only hard-coded price/color
    /// table outside this file is the canonical one in <see cref="PriceService"/>.
    /// </summary>
    public static class AiFactsProvider
    {
        /// <summary>
        /// Color palette per product. Mirrors the price list exactly so
        /// the AI prompt and the catalog dropdowns agree on which colors
        /// a product has.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string[]> ColorsByProduct =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Anwis"]                  = new[] { "Белый", "Коричневый" },
                ["На навесах"]             = new[] { "Белый", "Коричневый" },
                ["Оконная на метал. крепл."] = new[] { "Белый", "Коричневый" },
                ["Дверная сетка"]          = new[] { "Белый" },
                ["Отлив"]                  = new[] { "Белый", "Коричневый", "Антрацит", "Золотой дуб" },
                ["Козырёк"]                = new[] { "Белый", "Коричневый", "Антрацит", "Золотой дуб" },
                ["Короб"]                  = new[] { "Белый", "Коричневый", "Антрацит", "Золотой дуб" },
                ["Уплотнение"]             = new[] { "Серый", "Чёрный" }
            };

        /// <summary>Returns the palette for <paramref name="product"/> (empty when unknown).</summary>
        public static IReadOnlyList<string> GetColorsFor(string product)
        {
            if (string.IsNullOrWhiteSpace(product)) return Array.Empty<string>();
            return ColorsByProduct.TryGetValue(product, out var c) ? c : Array.Empty<string>();
        }

        /// <summary>Returns the first color in the palette, or empty when the product is colorless.</summary>
        public static string GetDefaultColor(string product)
        {
            var colors = GetColorsFor(product);
            return colors.Count > 0 ? colors[0] : "";
        }

        /// <summary>
        /// Returns the canonical price for <paramref name="product"/>+<paramref name="color"/>.
        /// Pulled from <see cref="PriceService.DefaultPricesSnapshot"/> so the
        /// AI prompt and the calculator can never disagree on what the
        /// «catalog» price is. Returns the first matching row; falls back
        /// to the row with empty color when a specific color is not priced.
        /// </summary>
        public static double GetPrice(string product, string color)
        {
            if (string.IsNullOrWhiteSpace(product)) return 0;
            var snapshot = PriceService.DefaultPricesSnapshot();
            var match = snapshot.FirstOrDefault(p =>
                p.Name == product && p.Color == (color ?? ""));
            if (match == null)
                match = snapshot.FirstOrDefault(p =>
                    p.Name == product && string.IsNullOrEmpty(p.Color));
            return match?.Price ?? 0;
        }

        // ── Catalog-side thin predicates ─────────────────────────────

        /// <summary>True when the product has a color choice in the catalog.</summary>
        public static bool HasColors(string product)
        {
            var c = GetColorsFor(product);
            return c.Count > 0 && !ProductCatalog.IsNoColor(product);
        }

        /// <summary>True when the product supports the installation toggle.</summary>
        public static bool IsInstallationApplicable(string type)
            => ProductCatalog.IsInstallationApplicable(type);

        /// <summary>True when the product uses manual entry (no auto-size).</summary>
        public static bool IsManualPiece(string type)
            => ProductCatalog.IsManualPiece(type);

        /// <summary>True when the product uses the Anwis size-mode picker.</summary>
        public static bool IsAnwisApplicable(string type)
            => AnwisSizeService.IsApplicable(type);

        /// <summary>True when the named product is in the catalog.</summary>
        public static bool IsKnownProduct(string name)
            => AiOrderContext.IsKnownProduct(name);

        /// <summary>All product names in user-facing order (Сетки → Доборы → …).</summary>
        public static IReadOnlyList<string> AllProducts =>
            ProductCatalog.UserGroups.SelectMany(g => g.Products).ToList();
    }
}
