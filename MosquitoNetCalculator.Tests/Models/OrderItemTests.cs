using System;
using System.Collections.Generic;
using MosquitoNetCalculator.Models;
using Xunit;

namespace MosquitoNetCalculator.Tests.Models
{
    public class OrderItemTests
    {
        // ─── Recalculate tests ───────────────────────────────

        [Fact]
        public void Recalculate_AreaBasedProduct()
        {
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 1000,
                Height = 2000,
                Quantity = 1,
                Price = 1800
            };
            // Width/Height are calc-adjusted values (1000×2000), area = 1000*2000/1M = 2.0 м²
            Assert.Equal(2.0, item.CalculatedValue, 3);
            // Total = 2.0 * 1800 * 1 = 3600
            Assert.Equal(3600, item.Total, 2);
        }

        [Fact]
        public void Recalculate_PerimeterBasedProduct_Psul()
        {
            var item = new OrderItem
            {
                Name = "ПСУЛ",
                Width = 1000,
                Height = 2000,
                Quantity = 1,
                Price = 100
            };
            // CalculatedValue = (1000 + 2000) * 2 / 1000 = 6.0 м.п.
            Assert.Equal(6.0, item.CalculatedValue, 3);
            Assert.Equal("м.п.", item.Unit);
            Assert.Equal(600, item.Total, 2);
        }

        [Fact]
        public void Recalculate_Uplotnenie_PerimeterBased()
        {
            var item = new OrderItem
            {
                Name = "Уплотнение",
                Width = 1000,
                Height = 2000,
                Quantity = 1,
                Price = 250
            };
            Assert.Equal(6.0, item.CalculatedValue, 3);
            Assert.Equal("м.п.", item.Unit);
            Assert.Equal(1500, item.Total, 2);
        }

        [Theory]
        [InlineData("Работа", "шт.")]
        [InlineData("Брус", "шт.")]
        [InlineData("Пояс", "шт.")]
        [InlineData("Доставка", "шт.")]
        [InlineData("Откос материал", "шт.")]
        public void Recalculate_PieceBasedProduct(string name, string expectedUnit)
        {
            var item = new OrderItem
            {
                Name = name,
                Width = 0,
                Height = 0,
                Quantity = 1,
                Price = 5000
            };
            Assert.Equal(1, item.CalculatedValue, 3);
            Assert.Equal(expectedUnit, item.Unit);
            Assert.Equal(5000, item.Total, 2);
        }

        [Fact]
        public void Recalculate_QuantityMultiplier()
        {
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 1000,
                Height = 1000,
                Quantity = 3,
                Price = 1800
            };
            // Width/Height are calc-adjusted (1000×1000), area = 1.0 м², Total = 1.0 * 1800 * 3 = 5400
            Assert.Equal(5400, item.Total, 2);
        }

        [Fact]
        public void Recalculate_WithZeroDimensions_ReturnsZero()
        {
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 0,
                Height = 0,
                Price = 1800
            };
            Assert.Equal(0, item.CalculatedValue, 3);
            Assert.Equal(0, item.Total, 2);
        }

        [Fact]
        public void Recalculate_EmptyName_ReturnsZero()
        {
            var item = new OrderItem
            {
                Name = "",
                Width = 1000,
                Height = 1000,
                Price = 100
            };
            Assert.Equal(0, item.CalculatedValue, 3);
            Assert.Equal(0, item.Total, 2);
        }

        [Fact]
        public void Recalculate_AreaBasedProduct_Otliv()
        {
            var item = new OrderItem
            {
                Name = "Отлив",
                Width = 1500,
                Height = 100,
                Quantity = 1,
                Price = 2150
            };
            // 1500 * 100 / 1_000_000 = 0.150 м²
            Assert.Equal(0.150, item.CalculatedValue, 3);
            Assert.Equal("м²", item.Unit);
        }

        // ─── Unit property tests ─────────────────────────────

        [Theory]
        [InlineData("Anwis", "м²")]
        [InlineData("На навесах", "м²")]
        [InlineData("Оконная на метал. крепл.", "м²")]
        [InlineData("Отлив", "м²")]
        [InlineData("Козырёк", "м²")]
        [InlineData("Короб", "м²")]
        [InlineData("ПСУЛ", "м.п.")]
        [InlineData("Уплотнение", "м.п.")]
        [InlineData("Откос материал", "шт.")]
        [InlineData("Работа", "шт.")]
        [InlineData("Брус", "шт.")]
        [InlineData("Пояс", "шт.")]
        [InlineData("Доставка", "шт.")]
        public void Unit_ReturnsCorrectValue(string name, string expectedUnit)
        {
            var item = new OrderItem { Name = name };
            Assert.Equal(expectedUnit, item.Unit);
        }

        // ─── Installation mode tests ─────────────────────────

        [Fact]
        public void InstallationMode_ClampsToUpperBound()
        {
            var item = new OrderItem { Name = "Anwis" };
            item.InstallationMode = 5;
            Assert.Equal(2, item.InstallationMode);
        }

        [Fact]
        public void InstallationMode_ClampsToLowerBound()
        {
            var item = new OrderItem { Name = "Anwis" };
            item.InstallationMode = -1;
            Assert.Equal(0, item.InstallationMode);
        }

        [Fact]
        public void InstallationMode_ValidValues()
        {
            var item = new OrderItem { Name = "Anwis" };
            item.InstallationMode = 0;
            Assert.Equal(0, item.InstallationMode);
            item.InstallationMode = 1;
            Assert.Equal(1, item.InstallationMode);
            item.InstallationMode = 2;
            Assert.Equal(2, item.InstallationMode);
        }

        // ─── TotalWithDeduction tests ────────────────────────

        [Fact]
        public void TotalWithDeduction_Mode0_ReturnsTotal()
        {
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 1000, Height = 1000,
                Price = 1800,
                InstallationMode = 0
            };
            Assert.Equal(item.Total, item.TotalWithDeduction);
        }

        [Fact]
        public void TotalWithDeduction_Mode1_SubtractsDeduction()
        {
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 1000, Height = 1000,
                Price = 1800,
                InstallationMode = 1,
                InstallationDeduction = 500
            };
            Assert.Equal(Math.Max(0, item.Total - 500), item.TotalWithDeduction, 2);
        }

        [Fact]
        public void TotalWithDeduction_Mode1_ClampsToZero()
        {
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 100, Height = 100,
                Price = 10,
                InstallationMode = 1,
                InstallationDeduction = 500
            };
            // Total = 0.01 * 10 = 0.1, deduction = 500 → clamped to 0
            Assert.Equal(0, item.TotalWithDeduction, 2);
        }

        [Fact]
        public void TotalWithDeduction_Mode2_SubtractsSurcharge()
        {
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 1000, Height = 1000,
                Price = 1800,
                InstallationMode = 2,
                InstallationSurcharge = 200
            };
            Assert.Equal(Math.Max(0, item.Total - 200), item.TotalWithDeduction, 2);
        }

        [Fact]
        public void TotalWithDeduction_NonApplicable_ReturnsTotal()
        {
            var item = new OrderItem
            {
                Name = "Отлив",
                Width = 1000, Height = 1000,
                Price = 2150,
                InstallationMode = 1,
                InstallationDeduction = 500
            };
            Assert.Equal(item.Total, item.TotalWithDeduction);
        }

        // ─── IsInstallationApplicable tests ──────────────────

        [Fact]
        public void IsInstallationApplicable_Anwis_True()
        {
            Assert.True(new OrderItem { Name = "Anwis" }.IsInstallationApplicable);
        }

        [Fact]
        public void IsInstallationApplicable_NaNavyesakh_True()
        {
            Assert.True(new OrderItem { Name = "На навесах" }.IsInstallationApplicable);
        }

        [Theory]
        [InlineData("Отлив")]
        [InlineData("Козырёк")]
        [InlineData("Короб")]
        [InlineData("ПСУЛ")]
        [InlineData("Работа")]
        [InlineData("Доставка")]
        [InlineData("Уплотнение")]
        public void IsInstallationApplicable_OtherProducts_False(string name)
        {
            Assert.False(new OrderItem { Name = name }.IsInstallationApplicable);
        }

        // ─── Clone tests ─────────────────────────────────────

        [Fact]
        public void Clone_CreatesDeepCopy()
        {
            var original = new OrderItem
            {
                Name = "Anwis",
                Color = "Белый",
                Width = 1000,
                Height = 2000,
                Quantity = 2,
                Price = 1800,
                InstallationMode = 1,
                IsActive = false
            };

            var clone = original.Clone();
            Assert.Equal(original.Name, clone.Name);
            Assert.Equal(original.Color, clone.Color);
            Assert.Equal(original.Width, clone.Width);
            Assert.Equal(original.Height, clone.Height);
            Assert.Equal(original.Quantity, clone.Quantity);
            Assert.Equal(original.Price, clone.Price);
            Assert.Equal(original.InstallationMode, clone.InstallationMode);
            Assert.Equal(original.IsActive, clone.IsActive);
            Assert.Equal(original.InstallationDeduction, clone.InstallationDeduction);
            Assert.Equal(original.InstallationSurcharge, clone.InstallationSurcharge);
        }

        [Fact]
        public void Clone_IsIndependent()
        {
            var original = new OrderItem { Name = "Anwis", Price = 1800 };
            var clone = original.Clone();
            clone.Name = "Отлив";
            clone.Price = 2150;
            Assert.Equal("Anwis", original.Name);
            Assert.Equal(1800, original.Price);
        }

        // ─── IsPriceOverridden tests ─────────────────────────

        [Fact]
        public void SetDefaultPrice_DetectsOverride()
        {
            var item = new OrderItem { Price = 1800 };
            item.SetDefaultPrice(1800);
            Assert.False(item.IsPriceOverridden);

            item.Price = 2000;
            Assert.True(item.IsPriceOverridden);
        }

        [Fact]
        public void IsPriceOverridden_FalseByDefault()
        {
            var item = new OrderItem { Price = 1800 };
            // _defaultPrice = -1 by default, so IsPriceOverridden should be false
            Assert.False(item.IsPriceOverridden);
        }

        [Fact]
        public void IsPriceOverridden_False_WhenWithinTolerance()
        {
            var item = new OrderItem { Price = 1800 };
            item.SetDefaultPrice(1800.005); // within 0.01 tolerance
            Assert.False(item.IsPriceOverridden);
        }

        // ─── KpInstallationDisplay tests ─────────────────────

        [Fact]
        public void KpInstallationDisplay_Mode0_ReturnsCheckmark()
        {
            var item = new OrderItem { Name = "Anwis", InstallationMode = 0 };
            Assert.Equal("\u2713", item.KpInstallationDisplay);
        }

        [Fact]
        public void KpInstallationDisplay_Mode1_ReturnsBallotX()
        {
            // KP uses the same ✗ (U+2717) for "no installation" that the grid
            // icon uses, so the column reads as a single visual vocabulary:
            // ✓ (green) when yes / in-construction, ✗ when no, — when n/a.
            var item = new OrderItem { Name = "Anwis", InstallationMode = 1 };
            Assert.Equal("\u2717", item.KpInstallationDisplay);
        }

        [Fact]
        public void KpInstallationDisplay_Mode2_ReturnsVeLetter()
        {
            // The printed KP disambiguates mode 2 (В конструкцию) from mode 0
            // (Монтаж включён) by showing a Cyrillic "В" instead of the same
            // ✓ checkmark — so the reader can tell the two "yes" modes apart
            // without having to consult the title attribute.
            var item = new OrderItem { Name = "Anwis", InstallationMode = 2 };
            Assert.Equal("\u0412", item.KpInstallationDisplay);
        }

        // ─── InstallationLabel tests ─────────────────────────

        [Theory]
        [InlineData(0, "Монтаж включён")]
        [InlineData(1, "Без монтажа")]
        [InlineData(2, "В конструкцию")]
        public void InstallationLabel_ApplicableProduct_ReturnsModeLabel(int mode, string expected)
        {
            var item = new OrderItem { Name = "Anwis", InstallationMode = mode };
            Assert.Equal(expected, item.InstallationLabel);
        }

        [Fact]
        public void InstallationLabel_NonApplicableProduct_ReturnsNotSupported()
        {
            var item = new OrderItem { Name = "Отлив" };
            Assert.Equal("Монтаж не предусмотрен", item.InstallationLabel);
        }

        [Fact]
        public void InstallationLabel_ChangesWithMode()
        {
            // PropertyChanged must fire on mode change so any future grid binding
            // to InstallationLabel refreshes — guards against silent staleness.
            var item = new OrderItem { Name = "Anwis", InstallationMode = 0 };
            string? last = null;
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "InstallationLabel") last = item.InstallationLabel;
            };
            item.InstallationMode = 1;
            Assert.Equal("Без монтажа", last);
            item.InstallationMode = 2;
            Assert.Equal("В конструкцию", last);
        }

        [Fact]
        public void InstallationLabel_FlipsOnNameChange_AndFiresPropertyChanged()
        {
            // Name drives IsInstallationApplicable, which drives InstallationLabel.
            // Mutating Name from "Anwis" to "Отлив" must notify the binding.
            var item = new OrderItem { Name = "Anwis" };
            Assert.Equal("Монтаж включён", item.InstallationLabel);

            bool fired = false;
            string? observed = null;
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "InstallationLabel")
                {
                    fired = true;
                    observed = item.InstallationLabel;
                }
            };
            item.Name = "Отлив";
            Assert.True(fired, "InstallationLabel PropertyChanged must fire on Name change");
            Assert.Equal("Монтаж не предусмотрен", observed);
        }

        [Fact]
        public void KpInstallationDisplay_NonApplicable_ReturnsDash()
        {
            var item = new OrderItem { Name = "Отлив" };
            Assert.Equal("\u2014", item.KpInstallationDisplay);
        }

        // ─── InstallationDisplay tests (grid icon) ────────────

        [Fact]
        public void InstallationDisplay_Mode0_ReturnsCheckmark()
        {
            var item = new OrderItem { Name = "Anwis", InstallationMode = 0 };
            Assert.Equal("\u2713", item.InstallationDisplay);
        }

        [Fact]
        public void InstallationDisplay_Mode1_ReturnsBallotX()
        {
            var item = new OrderItem { Name = "Anwis", InstallationMode = 1 };
            Assert.Equal("\u2717", item.InstallationDisplay);
        }

        [Fact]
        public void InstallationDisplay_Mode2_ReturnsVeLetter()
        {
            // The grid icon mirrors the printed KP: mode 2 (В конструкцию)
            // uses a Cyrillic "В" letter instead of the same ✓ as mode 0, so
            // the two "yes" modes are visually distinguishable at a glance
            // (both in the live grid and in the generated PDF).
            var item = new OrderItem { Name = "Anwis", InstallationMode = 2 };
            Assert.Equal("\u0412", item.InstallationDisplay);
        }

        [Fact]
        public void InstallationDisplay_NonApplicable_ReturnsDash()
        {
            var item = new OrderItem { Name = "Отлив" };
            Assert.Equal("\u2014", item.InstallationDisplay);
        }

        // ─── SetCurrentInstallationAmount tests ──────────────

        [Fact]
        public void SetCurrentInstallationAmount_Mode1_SetsDeduction()
        {
            var item = new OrderItem { Name = "Anwis", InstallationMode = 1 };
            item.SetCurrentInstallationAmount(300);
            Assert.Equal(300, item.InstallationDeduction);
        }

        [Fact]
        public void SetCurrentInstallationAmount_Mode2_SetsSurcharge()
        {
            var item = new OrderItem { Name = "Anwis", InstallationMode = 2 };
            item.SetCurrentInstallationAmount(200);
            Assert.Equal(200, item.InstallationSurcharge);
        }

        [Fact]
        public void SetCurrentInstallationAmount_NegativeClampedToZero()
        {
            var item = new OrderItem { Name = "Anwis", InstallationMode = 1 };
            item.SetCurrentInstallationAmount(-100);
            Assert.Equal(0, item.InstallationDeduction);
        }

        // ─── PropertyChanged tests ───────────────────────────

        [Fact]
        public void PropertyChanged_Fired_OnNameChange()
        {
            var item = new OrderItem();
            var changedProperties = new List<string>();
            item.PropertyChanged += (s, e) => { if (e.PropertyName != null) changedProperties.Add(e.PropertyName); };
            item.Name = "Anwis";
            // Name setter fires PropertyChanged("Name") then Recalculate() fires many more
            Assert.Contains("Name", changedProperties);
        }

        [Fact]
        public void PropertyChanged_NotFired_WhenSameValue()
        {
            var item = new OrderItem { Name = "Anwis" };
            int count = 0;
            item.PropertyChanged += (s, e) => count++;
            item.Name = "Anwis";
            Assert.Equal(0, count);
        }

        [Fact]
        public void RecalculateRequested_Fired_OnPropertyChange()
        {
            var item = new OrderItem();
            int fireCount = 0;
            item.RecalculateRequested += () => fireCount++;
            item.Width = 1000;
            Assert.True(fireCount > 0);
        }

        // ─── IsActive tests ──────────────────────────────────

        [Fact]
        public void IsActive_DefaultTrue()
        {
            var item = new OrderItem();
            Assert.True(item.IsActive);
        }

        [Fact]
        public void IsActive_ChangeTriggersPropertyChanged()
        {
            var item = new OrderItem();
            string? changedProperty = null;
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "IsActive") changedProperty = "IsActive";
            };
            item.IsActive = false;
            Assert.Equal("IsActive", changedProperty);
        }

        // ─── Display properties tests ────────────────────────

        [Fact]
        public void CalculatedValueDisplay_EmptyWhenZero()
        {
            var item = new OrderItem { Name = "" };
            Assert.Equal("", item.CalculatedValueDisplay);
        }

        [Fact]
        public void PriceDisplay_EmptyWhenZero()
        {
            var item = new OrderItem();
            Assert.Equal("", item.PriceDisplay);
        }

        [Fact]
        public void Quantity_ClampedToMinimumOne()
        {
            // Setter now clamps non-positive values to 1, so QuantityDisplay never returns "".
            var item = new OrderItem { Quantity = 0 };
            Assert.Equal(1, item.Quantity);
            Assert.Equal("1", item.QuantityDisplay);

            var item2 = new OrderItem { Quantity = -5 };
            Assert.Equal(1, item2.Quantity);
            Assert.Equal("1", item2.QuantityDisplay);
        }

        [Fact]
        public void CalculatedValueDisplay_ManualPiece_MultipliedByQuantity()
        {
            // Bug #3 fix: CalculatedValueDisplay must account for Quantity, not show 1 for Брус × 5.
            var item = new OrderItem { Name = "Брус", Quantity = 5, Price = 1000 };
            Assert.Equal("5 шт.", item.CalculatedValueDisplay);
        }

        [Fact]
        public void CalculatedValueDisplay_AreaProduct_MultipliedByQuantity()
        {
            var item = new OrderItem { Name = "Anwis", Width = 1000, Height = 1000, Quantity = 3, Price = 1800 };
            // Width/Height are calc-adjusted (1000×1000), area = 1.0 м², × Qty 3 = 3.000 м²
            Assert.Equal("3,000 м²", item.CalculatedValueDisplay);
        }

        // ─── IsWidthOnly tests ───────────────────────────────────────────────────────────

        [Fact]
        public void IsWidthOnly_True_ForOtkosMaterial()
        {
            // Откос материал records Width as a per-row spec but doesn't use
            // it in the Total formula, so Width stays editable while Height/Color
            // stay blocked (same gate as other ManualPiece products).
            Assert.True(new OrderItem { Name = "Откос материал" }.IsWidthOnly);
        }

        [Theory]
        [InlineData("Работа")]
        [InlineData("Брус")]
        [InlineData("Пояс")]
        [InlineData("Доставка")]
        public void IsWidthOnly_False_ForOtherManualPieceProducts(string name)
        {
            // Other ManualPiece products don't have a single-dimension spec
            // (Брус/Пояс) or have a different unit (Работа/Доставка are pure
            // aggregate sums), so WidthOnly must remain false for them.
            Assert.False(new OrderItem { Name = name }.IsWidthOnly);
        }

        [Theory]
        [InlineData("Anwis")]
        [InlineData("На навесах")]
        [InlineData("Отлив")]
        [InlineData("ПСУЛ")]
        public void IsWidthOnly_False_ForNonManualPieceProducts(string name)
        {
            // Non-ManualPiece products have both Width and Height meaning —
            // W*H drives area-based Total — so they must NOT be WidthOnly,
            // and the UI keeps allowing Width/Height editing for them.
            Assert.False(new OrderItem { Name = name }.IsWidthOnly);
        }

        [Fact]
        public void IsWidthOnly_ReflectsNamePropertyChange()
        {
            // Mutating Name drives WidthOnlyProducts.Contains(Name); the property
            // must re-evaluate, and any future binding needs the PropertyChanged
            // notification to refresh the UI.
            var item = new OrderItem { Name = "Anwis" };
            Assert.False(item.IsWidthOnly);

            bool fired = false;
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(OrderItem.IsWidthOnly))
                    fired = true;
            };
            item.Name = "Откос материал";
            Assert.True(item.IsWidthOnly);
            Assert.True(fired, "IsWidthOnly must fire PropertyChanged when Name changes");
        }

        [Fact]
        public void TotalWithDeduction_Mode1_RoundedToTwoDecimals()
        {
            // Bug #7 fix: TotalWithDeduction should be rounded to 2 decimal places.
            // Use values that are exactly representable in double (no 0.01 / 0.99 binary rounding issues).
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 1000, Height = 1000,
                Price = 1000,
                InstallationMode = 1,
                InstallationDeduction = 333.50
            };
            // Width/Height are calc-adjusted (1000×1000). Total = 1000.0, 1000-333.50 = 666.50
            Assert.Equal(666.50, item.TotalWithDeduction, 2);
        }
    }
}
