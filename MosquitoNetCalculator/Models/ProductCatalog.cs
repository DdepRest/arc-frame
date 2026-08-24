using System;
using System.Collections.Generic;
using System.Linq;

namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// A group of products with display name, optional sub-title, and ordered products.
    /// </summary>
    public class ProductGroup
    {
        public string Name { get; init; } = "";
        public string? Subtitle { get; init; }
        public List<string> Products { get; init; } = new();
    }

    /// <summary>
    /// Single source of truth for product categories and catalog metadata.
    /// Extracted from <see cref="OrderItem"/> during Phase 5 refactoring to keep
    /// the domain model free from catalog concerns.
    /// </summary>
    public static class ProductCatalog
    {
        /// <summary>
        /// Products eligible for the installation toggle.
        /// </summary>
        public static readonly HashSet<string> InstallationApplicableProducts = new()
        {
            "Anwis", "На навесах", "Дверная сетка", "Оконная на метал. крепл.", "Отлив", "Козырёк"
        };

        /// <summary>
        /// Products whose installation cost scales with the perimeter
        /// (per linear meter, ₽/м.п.) rather than per piece. The
        /// <c>TotalWithDeduction</c> formula multiplies the rate by
        /// <c>InstallationLinearMeters</c> for these products.
        /// <para/>
        /// v3.47.0: only Отлив and Козырёк qualify. Adding a new product to this
        /// set MUST be paired with a migration step in
        /// <c>CalculationViewModel.LoadFromOrderData</c> — see <see cref="GOTCHAS.md"/>
        /// (legacy DTO defaults trap: pre-existing JSON entries for newly-
        /// installable products still carry <c>(mode=0, ded=-500, sur=-500, adj=0)</c>
        /// defaults which clamp <c>TotalWithDeduction</c> to 0 in modes 1/2).
        /// </summary>
        public static readonly HashSet<string> PerLinearMeterProducts = new()
        {
            "Отлив", "Козырёк"
        };

        /// <summary>
        /// Products measured by square meters: CalculatedValue = W * H / 1_000_000.
        /// Source of truth for the Quick-Add preview in MainWindow.xaml.cs.
        /// </summary>
        public static readonly HashSet<string> AreaBasedProducts = new()
        {
            "Anwis",
            "На навесах",
            "Оконная на метал. крепл.",
            "Дверная сетка",
            "Отлив",
            "Козырёк",
            "Короб"
        };

        /// <summary>
        /// Products that need only Quantity + Price (manual sum).
        /// Color, Width, Height columns are hidden for these products.
        /// </summary>
        public static readonly HashSet<string> ManualPieceProducts = new()
        {
            "Работа",
            "Откос",
            "Работа за откос",
            "Брус",
            "Пояс",
            "Доставка",
            "Материал"
        };

        /// <summary>
        /// Manual-piece products that display ONLY the sum (сумма) —
        /// no quantity, no width, no height, no area/length.
        /// </summary>
        public static readonly HashSet<string> AmountOnlyProducts = new()
        {
            "Брус",
            "Пояс",
            "Доставка"
        };

        /// <summary>
        /// Manual-piece products where quantity is optional.
        /// When quantity equals the default (1), the quantity and area/length
        /// columns are hidden in the DataGrid, leaving only the sum visible.
        /// </summary>
        public static readonly HashSet<string> OptionalQuantityProducts = new()
        {
            "Материал"
        };

        /// <summary>
        /// Manual-piece products that ALSO record Width as a per-row spec —
        /// Width is captured (e.g. slope material 250 mm) but does NOT enter
        /// the Total formula because CalculatedValue = 1 шт. for these products.
        /// </summary>
        public static readonly HashSet<string> WidthOnlyProducts = new()
        {
            "Откос",
        };

        /// <summary>
        /// Products that support the anti-cat fabric surcharge.
        /// A fixed +2000 ₽/m² is added to the catalog price when the user checks
        /// the "Антикошка" option in QuickAdd.
        /// </summary>
        public static readonly HashSet<string> AnticatApplicableProducts = new()
        {
            "Anwis",
            "На навесах",
            "Оконная на метал. крепл.",
            "Дверная сетка"
        };

        /// <summary>
        /// Products that do not have color variants.
        /// Color dropdown is disabled for these products in QuickAdd.
        /// </summary>
        public static readonly HashSet<string> NoColorProducts = new()
        {
            "Работа",
            "Откос",
            "Работа за откос",
            "Брус",
            "Пояс",
            "Доставка",
            "ПСУЛ",
            "Материал"
        };

        /// <summary>True when the product is eligible for the installation toggle.</summary>
        public static bool IsInstallationApplicable(string? name) =>
            !string.IsNullOrEmpty(name) && InstallationApplicableProducts.Contains(name);

        /// <summary>
        /// True when the product's installation cost is calculated per linear
        /// meter (perimeter) rather than per piece. Single source of truth —
        /// used by <c>OrderItem.IsInstallationPerLinearMeter</c>,
        /// <c>CalculationViewModel.LoadFromOrderData</c> legacy migration, and
        /// v3.46.1 sign-flip exclusion.
        /// </summary>
        public static bool IsPerLinearMeter(string? name) =>
            !string.IsNullOrEmpty(name) && PerLinearMeterProducts.Contains(name);

        /// <summary>True when the product is measured by square meters.</summary>
        public static bool IsAreaBased(string? name) =>
            !string.IsNullOrEmpty(name) && AreaBasedProducts.Contains(name);

        /// <summary>True when the product uses manual piece semantics.</summary>
        public static bool IsManualPiece(string? name) =>
            !string.IsNullOrEmpty(name) && ManualPieceProducts.Contains(name);

        /// <summary>True when the product displays only the sum.</summary>
        public static bool IsAmountOnly(string? name) =>
            !string.IsNullOrEmpty(name) && AmountOnlyProducts.Contains(name);

        /// <summary>True when the product has optional quantity display.</summary>
        public static bool IsQuantityOptional(string? name) =>
            !string.IsNullOrEmpty(name) && OptionalQuantityProducts.Contains(name);

        /// <summary>True when the product records Width as a per-row spec.</summary>
        public static bool IsWidthOnly(string? name) =>
            !string.IsNullOrEmpty(name) && WidthOnlyProducts.Contains(name);

        /// <summary>True when the product supports the anti-cat fabric surcharge.</summary>
        public static bool IsAnticatApplicable(string? name) =>
            !string.IsNullOrEmpty(name) && AnticatApplicableProducts.Contains(name);

        /// <summary>True when the product has no color variants.</summary>
        public static bool IsNoColor(string? name) =>
            !string.IsNullOrEmpty(name) && NoColorProducts.Contains(name);

        /// <summary>
        /// Window screen products that automatically get an impost bar (импост)
        /// when the screen is large enough (width >= 500 mm OR height >= 1500 mm).
        /// The impost surcharge is +200 RUB per linear meter of WIDTH and is added
        /// to the row total; it cannot be removed (HasImpost is derived from
        /// Name + Width + Height and is never serialized).
        /// <para/>
        /// The door screen is deliberately EXCLUDED — the
        /// impost system does not participate in its calculation at all.
        /// </summary>
        public static readonly HashSet<string> ImpostApplicableProducts = new()
        {
            "Anwis",
            "На навесах",
            "Оконная на метал. крепл."
        };

        /// <summary>True when the product participates in the automatic impost system.</summary>
        public static bool IsImpostApplicable(string? name) =>
            !string.IsNullOrEmpty(name) && ImpostApplicableProducts.Contains(name);

        /// <summary>
        /// True when the product name exists in the fixed UX catalog
        /// (<see cref="UserGroups"/>). User-added / «Свой товар» names return
        /// false — those are manual sum rows (Total = Price × Qty), not
        /// area-based products.
        /// </summary>
        public static bool IsKnownProduct(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return UserGroups.Any(g => g.Products.Any(p =>
                string.Equals(p, name, StringComparison.OrdinalIgnoreCase)));
        }

        // ─────────────────────────────────────────────────────────
        // Product grouping for the UX catalog (all groups visible)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Defines the user-facing product groups with their display names,
        /// optional sub-titles, and ordered product lists.
        /// Inner slope materials are excluded — they are used only by the
        /// automatic slope calculator.
        /// </summary>
        public static readonly List<ProductGroup> UserGroups = new()
        {
            new ProductGroup
            {
                Name = "Москитные сетки",
                Products = new List<string> { "Anwis", "На навесах", "Оконная на метал. крепл.", "Дверная сетка" }
            },
            new ProductGroup
            {
                Name = "Изделия из металла",
                Subtitle = "Фасадные доборы",
                Products = new List<string> { "Отлив", "Козырёк", "Короб" }
            },
            new ProductGroup
            {
                Name = "Комплектующие и материалы",
                Products = new List<string> { "ПСУЛ", "Уплотнение", "Брус", "Пояс", "Материал" }
            },
            new ProductGroup
            {
                Name = "Откосы",
                Products = new List<string> { "Откос", "Работа за откос" }
            },
            new ProductGroup
            {
                Name = "Услуги",
                Products = new List<string> { "Работа", "Доставка" }
            },
        };

        /// <summary>
        /// Returns the user-facing product groups filtered to only include
        /// products that actually exist in the given product name list.
        /// This gracefully handles user-added products that don't appear
        /// in the fixed grouping — they simply won't appear in groups.
        /// </summary>
        public static List<ProductGroup> GetVisibleGroups(List<string> productNames)
        {
            var nameSet = new HashSet<string>(productNames);
            return UserGroups
                .Select(g => new ProductGroup
                {
                    Name = g.Name,
                    Subtitle = g.Subtitle,
                    Products = g.Products.Where(p => nameSet.Contains(p)).ToList()
                })
                .Where(g => g.Products.Count > 0)
                .ToList();
        }

        /// <summary>
        /// Orders a flat product-name list by the UX catalog hierarchy:
        /// group order first (<see cref="UserGroups"/>), then the product
        /// order inside each group. Names that don't exist in the catalog
        /// (user-added products) are appended at the end, keeping their
        /// original relative order.
        /// <para/>
        /// Single source of truth for the QuickAdd «Тип» ComboBox, the
        /// search-suggestion popup and the empty-state product chips.
        /// Do NOT apply an alphabetical sort here — the user-approved
        /// catalog order (Сетки → Доборы → Комплектующие → Откосы → Услуги)
        /// must survive, otherwise «Anwis» ends up last.
        /// </summary>
        public static List<string> OrderProductNames(IEnumerable<string> names)
        {
            // Callers pass materialized distinct lists, so a HashSet is a safe,
            // case-sensitive membership filter; duplicate input names collapse.
            var remaining = new HashSet<string>(names);
            var ordered = new List<string>();

            foreach (var group in UserGroups)
                foreach (var product in group.Products)
                    if (remaining.Remove(product))
                        ordered.Add(product);

            // User-added products not present in any group keep their original order.
            ordered.AddRange(names.Where(remaining.Contains));
            return ordered;
        }
    }
}
