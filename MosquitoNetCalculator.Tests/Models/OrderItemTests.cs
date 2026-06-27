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

        // ─── TotalWithDeduction QuantityMultiplier tests (bug #12 fix) ──
        //
        // Pre-fix: deduction was subtracted once per row, regardless of Quantity.
        // This understated the discount for bulk orders with Quantity > 1.
        // Post-fix: deduction × Quantity — one per-piece fee, never a flat fee.
        // See docs/arc/GOTCHAS.md#12.

        [Fact]
        public void TotalWithDeduction_Mode2_Quantity3_SubtractsPerPiece_NotOnce()
        {
            // Regression for the bug as filed: «В конструкцию» with 3 pieces
            // must subtract 3 × 500 ₽, not just 500 ₽.
            // Use На навесах (identity-sized, no Anwis formula) for clean math.
            var item = new OrderItem
            {
                Name = "На навесах",
                Width = 1500, Height = 1000,
                Quantity = 3,
                Price = 3000,
                InstallationMode = 2
            };
            // CV = 1.5 м², Total = 1.5 × 3000 × 3 = 13500 ₽
            Assert.Equal(13500, item.Total, 2);
            // Post-fix: 13500 − 500×3 = 12000 ₽  (was 13000 ₽ pre-fix)
            Assert.Equal(12000, item.TotalWithDeduction, 2);
        }

        [Fact]
        public void TotalWithDeduction_Mode1_Quantity3_SubtractsPerPiece()
        {
            // Same fix applies to «Без монтажа» — deduction is per piece.
            var item = new OrderItem
            {
                Name = "На навесах",
                Width = 1500, Height = 1000,
                Quantity = 3,
                Price = 3000,
                InstallationMode = 1
            };
            Assert.Equal(13500, item.Total, 2);
            Assert.Equal(12000, item.TotalWithDeduction, 2);
        }

        [Fact]
        public void TotalWithDeduction_Mode0_Quantity3_StillNoDeduction()
        {
            // Mode 0 («Монтаж включён») — no deduction regardless of Quantity.
            // Lockdown that scaling Quantity doesn't accidentally re-introduce
            // a deduction in the default mode.
            var item = new OrderItem
            {
                Name = "На навесах",
                Width = 1500, Height = 1000,
                Quantity = 3,
                Price = 3000,
                InstallationMode = 0
            };
            Assert.Equal(item.Total, item.TotalWithDeduction);
        }

        [Fact]
        public void TotalWithDeduction_Mode2_CustomSurcharge_Quantity3_MultipliesPerPiece()
        {
            // User-entered surcharge is PER PIECE (CurrentInstallationAmount
            // is the field value). Final deduction = surcharge × Quantity.
            var item = new OrderItem
            {
                Name = "На навесах",
                Width = 1500, Height = 1000,
                Quantity = 3,
                Price = 3000,
                InstallationMode = 2,
                InstallationSurcharge = 200
            };
            // 13500 − 200×3 = 12900 ₽
            Assert.Equal(12900, item.TotalWithDeduction, 2);
        }

        [Fact]
        public void TotalWithDeduction_AnwisMode2_Quantity3_SubtractsPerPiece()
        {
            // Same behaviour for Anwis. Use Профипласт mode (size identity
            // — no W+2/H−30 shift) so Width/Height stay user-typed = stored.
            var item = new OrderItem
            {
                Name = "Anwis",
                AnwisSizeMode = AnwisSizeMode.Профипласт,
                Width = 1000, Height = 1000,
                Quantity = 3,
                Price = 1800,
                InstallationMode = 2
            };
            // CV = 1.0, Total = 5400, TotalWithDeduction = 5400 − 1500 = 3900
            Assert.Equal(5400, item.Total, 2);
            Assert.Equal(3900, item.TotalWithDeduction, 2);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        public void TotalWithDeduction_Mode2_QuantityScaling_IsLinearWithQ(int qty)
        {
            var item = new OrderItem
            {
                Name = "На навесах",
                Width = 1500, Height = 1000,
                Quantity = qty,
                Price = 3000,
                InstallationMode = 2
            };
            // CV = 1.5 м², Total = 1.5 × 3000 × qty = 4500 × qty
            // deduction = 500 × qty
            // TotalWithDeduction = (4500 − 500) × qty = 4000 × qty
            // Linear-in-qty assertion: scaling is the proof that deduction
            // multiplies by Quality and not a flat fee.
            double expectedTotal = 4500.0 * qty;
            double expectedTotalWithDeduction = 4000.0 * qty;
            Assert.Equal(expectedTotal, item.Total, 2);
            Assert.Equal(expectedTotalWithDeduction, item.TotalWithDeduction, 2);
        }

        [Fact]
        public void TotalWithDeduction_NonApplicable_Quantity3_StillIgnoresDeduction()
        {
            // Отлив is not subject to any installation mode — TotalWithDeduction
            // must equal Total regardless of Quantity or InstallationMode.
            // Guards against a regression that accidentally re-applies the
            // formula on non-eligible products.
            var item = new OrderItem
            {
                Name = "Отлив",
                Width = 1500, Height = 100,
                Quantity = 3,
                Price = 2150,
                InstallationMode = 2
            };
            Assert.Equal(item.Total, item.TotalWithDeduction);
        }

        [Fact]
        public void TotalWithDeduction_Mode2_HighQuantity_ClampsToZero()
        {
            // When deduction × Q exceeds Total, the result is clamped to 0,
            // never negative. Cheap product × big Q × default 500 surcharge.
            var item = new OrderItem
            {
                Name = "На навесах",
                Width = 100, Height = 100,
                Quantity = 50,
                Price = 100,
                InstallationMode = 2
            };
            // CV = 0.01, Total = 0.01*100*50 = 50 ₽, deduction = 500*50 = 25000 ₽ → clamp to 0
            Assert.Equal(0, item.TotalWithDeduction, 2);
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
            // Работа is ManualPiece but NOT AmountOnly — Quantity is still shown.
            var item = new OrderItem { Name = "Работа", Quantity = 5, Price = 1000 };
            Assert.Equal("5 шт.", item.CalculatedValueDisplay);
        }

        [Fact]
        public void CalculatedValueDisplay_AmountOnly_ReturnsEmpty()
        {
            // AmountOnly products (Брус, Пояс, Доставка) hide CalculatedValueDisplay.
            var brus = new OrderItem { Name = "Брус", Quantity = 5, Price = 1000 };
            Assert.Equal("", brus.CalculatedValueDisplay);

            var pojas = new OrderItem { Name = "Пояс", Quantity = 3, Price = 500 };
            Assert.Equal("", pojas.CalculatedValueDisplay);

            var delivery = new OrderItem { Name = "Доставка", Quantity = 2, Price = 1000 };
            Assert.Equal("", delivery.CalculatedValueDisplay);
        }

        [Fact]
        public void QuantityDisplay_AmountOnly_ReturnsEmpty()
        {
            // AmountOnly products hide QuantityDisplay.
            var brus = new OrderItem { Name = "Брус", Quantity = 5, Price = 1000 };
            Assert.Equal("", brus.QuantityDisplay);

            var pojas = new OrderItem { Name = "Пояс", Quantity = 1, Price = 500 };
            Assert.Equal("", pojas.QuantityDisplay);

            // Non-AmountOnly ManualPiece (Работа) still shows quantity.
            var work = new OrderItem { Name = "Работа", Quantity = 5, Price = 1000 };
            Assert.Equal("5", work.QuantityDisplay);
        }

        [Fact]
        public void CalculatedValueDisplay_AreaProduct_MultipliedByQuantity()
        {
            var item = new OrderItem { Name = "Anwis", Width = 1000, Height = 1000, Quantity = 3, Price = 1800 };
            // Width/Height are calc-adjusted (1000×1000), area = 1.0 м², × Qty 3 = 3.000 м²
            Assert.Equal("3,000 м²", item.CalculatedValueDisplay);
        }

        // ─── IsAmountOnly tests ───────────────────────────────────────────────────────────

        [Theory]
        [InlineData("Брус")]
        [InlineData("Пояс")]
        [InlineData("Доставка")]
        public void IsAmountOnly_True_ForAmountOnlyProducts(string name)
        {
            Assert.True(new OrderItem { Name = name }.IsAmountOnly);
        }

        [Theory]
        [InlineData("Работа")]
        [InlineData("Откос материал")]
        [InlineData("Anwis")]
        [InlineData("Отлив")]
        [InlineData("ПСУЛ")]
        public void IsAmountOnly_False_ForOtherProducts(string name)
        {
            Assert.False(new OrderItem { Name = name }.IsAmountOnly);
        }

        [Fact]
        public void IsAmountOnly_ReflectsNamePropertyChange()
        {
            var item = new OrderItem { Name = "Работа" };
            Assert.False(item.IsAmountOnly);

            bool fired = false;
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(OrderItem.IsAmountOnly))
                    fired = true;
            };
            item.Name = "Брус";
            Assert.True(item.IsAmountOnly);
            Assert.True(fired, "IsAmountOnly must fire PropertyChanged when Name changes");
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

        // ─── AnwisSizeMode tests ────────────────────────────

        [Fact]
        public void AnwisSizeMode_Default_Brusbox60()
        {
            var item = new OrderItem { Name = "Anwis" };
            Assert.Equal(AnwisSizeMode.Брусбокс60, item.AnwisSizeMode);
        }

        [Fact]
        public void AnwisSizeMode_Change_RecalculatesDimensions()
        {
            // Default mode is ББ 60. Смена на ББ 70 пересчитывает размеры.
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 1002, Height = 970, // calc-adjusted for ББ 60 from raw 1000×1000
            };
            Assert.Equal(AnwisSizeMode.Брусбокс60, item.AnwisSizeMode);

            // Switch to ББ 70: reverse ББ 60 → raw 1000×1000 → apply ББ 70 → 998×970
            item.AnwisSizeMode = AnwisSizeMode.Брусбокс70;
            Assert.Equal(998, item.Width, 0);
            Assert.Equal(970, item.Height, 0);
            Assert.Equal(AnwisSizeMode.Брусбокс70, item.AnwisSizeMode);
        }

        [Fact]
        public void AnwisSizeMode_SameMode_NoChange()
        {
            var item = new OrderItem { Name = "Anwis" };
            int fireCount = 0;
            item.PropertyChanged += (s, e) => fireCount++;

            item.AnwisSizeMode = AnwisSizeMode.Брусбокс60;

            Assert.Equal(0, fireCount);
        }

        [Fact]
        public void SetAnwisModeQuiet_DoesNotRecalculateDimensions()
        {
            // Use SetAnwisModeQuiet to set the initial mode WITHOUT triggering
            // the reverse/apply logic that the public setter would do.
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 1000, Height = 1000,
            };
            item.SetAnwisModeQuiet(AnwisSizeMode.Профипласт);

            // Switch back to ББ 60 quietly — dimensions should stay as-is
            item.SetAnwisModeQuiet(AnwisSizeMode.Брусбокс60);
            Assert.Equal(AnwisSizeMode.Брусбокс60, item.AnwisSizeMode);
            Assert.Equal(1000, item.Width);
            Assert.Equal(1000, item.Height);
        }

        [Fact]
        public void SetAnwisModeQuiet_SameMode_NoOp()
        {
            var item = new OrderItem { Name = "Anwis", AnwisSizeMode = AnwisSizeMode.Брусбокс60 };
            int fireCount = 0;
            item.PropertyChanged += (s, e) => fireCount++;

            item.SetAnwisModeQuiet(AnwisSizeMode.Брусбокс60);

            Assert.Equal(0, fireCount);
        }

        // ─── ШиринаВвод / ВысотаВвод tests (AnwisSize integration) ─

        [Fact]
        public void ШиринаВвод_ReturnsDisplayWidth()
        {
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 1002, Height = 970, // calc-adjusted for ББ 60
            };
            // Raw width should be 1000 (reverse of 1002 via ББ 60: -2)
            Assert.Equal(1000, item.ШиринаВвод);
        }

        [Fact]
        public void ВысотаВвод_ReturnsDisplayHeight()
        {
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 1002, Height = 970, // calc-adjusted for ББ 60
            };
            // Raw height should be 1000 (reverse of 970 via ББ 60: +30)
            Assert.Equal(1000, item.ВысотаВвод);
        }

        [Fact]
        public void ШиринаВвод_NonAnwis_ReturnsIdentity()
        {
            // v3.35.0 fix: for non-Anwis products, Размеры returns identity
            // (all three layers equal). ШиринаВвод = stored Width = 1500.
            var item = new OrderItem { Name = "Отлив", Width = 1500 };
            Assert.Equal(1500, item.ШиринаВвод);
        }

        [Fact]
        public void ВысотаВвод_NonAnwis_ReturnsIdentity_NotPlus30()
        {
            // Regression test for v3.35.0 bug: non-Anwis products (Откос материал,
            // Работа, Пояс) showed height=30mm because ReverseCalcHeight(0, ББ60)
            // returned 0+30=30. After fix, ВысотаВвод = stored Height = 0.
            var item = new OrderItem { Name = "Откос материал", Width = 250, Height = 0 };
            Assert.Equal(0, item.ВысотаВвод);
        }

        [Theory]
        [InlineData("Откос материал")]
        [InlineData("Работа")]
        [InlineData("Пояс")]
        public void Размеры_NonAnwis_DisplayEqualsCalc(string name)
        {
            // For non-Anwis products, Отображение and Расчёт layers are identity.
            // (Завод layer = calc − 20 is never read for non-Anwis —
            //  FactoryTextService gates on IsApplicable before accessing it.)
            var item = new OrderItem { Name = name, Width = 250, Height = 0 };
            var s = item.Размеры;
            Assert.Equal(item.Width, s.ШиринаОтображение);
            Assert.Equal(item.Width, s.ШиринаРасчёт);
            Assert.Equal(item.Height, s.ВысотаОтображение);
            Assert.Equal(item.Height, s.ВысотаРасчёт);
        }

        [Fact]
        public void ШиринаВвод_Setter_UpdatesStoredWidth()
        {
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 1002, Height = 970,
                AnwisSizeMode = AnwisSizeMode.Брусбокс60
            };

            // Set raw to 1100 → stored width becomes 1102 (W+2)
            item.ШиринаВвод = 1100;
            Assert.Equal(1102, item.Width, 0);
        }

        [Fact]
        public void ВысотаВвод_Setter_UpdatesStoredHeight()
        {
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 1002, Height = 970,
                AnwisSizeMode = AnwisSizeMode.Брусбокс60
            };

            // Set raw to 1200 → stored height becomes 1170 (H-30)
            item.ВысотаВвод = 1200;
            Assert.Equal(1170, item.Height, 0);
        }

        [Fact]
        public void ШиринаВвод_Setter_NonAnwis_StoresIdentity()
        {
            // v3.35.0 fix: editing width on non-Anwis (e.g. Откос материал)
            // should store raw value directly, not apply Anwis calc (+2 for ББ60).
            var item = new OrderItem
            {
                Name = "Откос материал",
                Width = 250, Height = 0,
            };
            Assert.Equal(250, item.ШиринаВвод);

            // Edit width to 300 → stored Width should be 300, not 302.
            item.ШиринаВвод = 300;
            Assert.Equal(300, item.ШиринаВвод);
            Assert.Equal(300, item.Width);
        }

        [Fact]
        public void ВысотаВвод_Setter_NonAnwis_StoresIdentity()
        {
            // v3.35.0 fix: editing height on non-Anwis should store
            // raw value directly, not Minus30.
            // (Height is usually disabled for ManualPiece in UI, but
            //  the property should still behave correctly if set via code.)
            var item = new OrderItem
            {
                Name = "Работа",
                Width = 0, Height = 0,
            };

            item.ВысотаВвод = 50;
            Assert.Equal(50, item.ВысотаВвод);
            Assert.Equal(50, item.Height);
        }

        [Fact]
        public void AnwisSizeMode_Change_NonAnwis_PreservesDimensions()
        {
            // v3.35.0 fix: changing AnwisSizeMode on non-Anwis products
            // must NOT reverse/re-apply Anwis formulas to Width/Height.
            var item = new OrderItem
            {
                Name = "Отлив",
                Width = 1500, Height = 100,
            };

            item.AnwisSizeMode = AnwisSizeMode.Брусбокс70;
            Assert.Equal(1500, item.Width);
            Assert.Equal(100, item.Height);
            Assert.Equal(AnwisSizeMode.Брусбокс70, item.AnwisSizeMode);

            // And back to default — still no dimension change.
            item.AnwisSizeMode = AnwisSizeMode.Брусбокс60;
            Assert.Equal(1500, item.Width);
            Assert.Equal(100, item.Height);
        }

        // ─── Anwis computed properties tests ─────────────────

        [Fact]
        public void IsAnwis_True_ForAnwis()
        {
            Assert.True(new OrderItem { Name = "Anwis" }.IsAnwis);
        }

        [Fact]
        public void IsAnwis_False_ForOtherProducts()
        {
            Assert.False(new OrderItem { Name = "Отлив" }.IsAnwis);
            Assert.False(new OrderItem { Name = "ПСУЛ" }.IsAnwis);
            Assert.False(new OrderItem { Name = "Работа" }.IsAnwis);
        }

        [Fact]
        public void AnwisSizeShortLabel_NonAnwis_ReturnsEmpty()
        {
            Assert.Equal("", new OrderItem { Name = "Отлив" }.AnwisSizeShortLabel);
        }

        [Fact]
        public void AnwisSizeShortLabel_Anwis_ReturnsModeLabel()
        {
            var item = new OrderItem { Name = "Anwis", AnwisSizeMode = AnwisSizeMode.Брусбокс60 };
            Assert.Equal("ББ 60", item.AnwisSizeShortLabel);
        }

        [Fact]
        public void AnwisSizeShortLabel_ChangesWithMode()
        {
            var item = new OrderItem { Name = "Anwis", AnwisSizeMode = AnwisSizeMode.Брусбокс60 };
            Assert.Equal("ББ 60", item.AnwisSizeShortLabel);

            item.AnwisSizeMode = AnwisSizeMode.Профипласт;
            Assert.Equal("ПП", item.AnwisSizeShortLabel);
        }

        [Fact]
        public void AnwisSizeToolTip_Anwis_ContainsModeDescription()
        {
            var item = new OrderItem { Name = "Anwis" };
            Assert.Contains("Брусбокс 60", item.AnwisSizeToolTip);
        }

        [Fact]
        public void AnwisSizeToolTip_NonAnwis_ReturnsEmpty()
        {
            Assert.Equal("", new OrderItem { Name = "Отлив" }.AnwisSizeToolTip);
        }

        // ─── InstallationToolTip tests ───────────────────────

        [Fact]
        public void InstallationToolTip_Applicable_Mode0_ShowsLabelAndHint()
        {
            var item = new OrderItem { Name = "Anwis", InstallationMode = 0 };
            Assert.Contains("Монтаж включён", item.InstallationToolTip);
            Assert.Contains("нажмите для переключения", item.InstallationToolTip);
        }

        [Fact]
        public void InstallationToolTip_Mode1_ShowsDeductionAmount()
        {
            var item = new OrderItem
            {
                Name = "Anwis",
                InstallationMode = 1,
                InstallationDeduction = 500
            };
            Assert.Contains("−", item.InstallationToolTip);
            Assert.Contains("500", item.InstallationToolTip);
        }

        // ─── Per-piece tooltip tests (GOTCHAS.md#12 UI signal) ──
        //
        // The tooltip explicitly shows «руб./шт. × Кол-во» so users understand
        // that the entered fee is per piece, not a flat row fee. These tests
        // lock that signal — a regression that strips the suffix would not be
        // caught by InstallationToolTip_Mode1_ShowsDeductionAmount above
        // (it only checks the minus sign and the number).

        [Fact]
        public void InstallationToolTip_Mode1_ShowsPerPieceSuffix()
        {
            var item = new OrderItem { Name = "Anwis", InstallationMode = 1 };
            Assert.Contains("руб./шт.", item.InstallationToolTip);
            Assert.Contains("× Кол-во", item.InstallationToolTip);
        }

        [Fact]
        public void InstallationToolTip_Mode2_ShowsPerPieceSuffix()
        {
            var item = new OrderItem { Name = "Anwis", InstallationMode = 2 };
            Assert.Contains("руб./шт.", item.InstallationToolTip);
            Assert.Contains("× Кол-во", item.InstallationToolTip);
        }

        [Fact]
        public void InstallationToolTip_Mode0_DoesNotShowPerPieceSuffix()
        {
            // Mode 0 («Монтаж включён») has no deduction — the per-piece
            // suffix «× Кол-во» would be misleading, so it must NOT appear.
            var item = new OrderItem { Name = "Anwis", InstallationMode = 0 };
            Assert.DoesNotContain("× Кол-во", item.InstallationToolTip);
        }

        [Fact]
        public void InstallationToolTip_NonApplicable_ReturnsNotSupported()
        {
            var item = new OrderItem { Name = "Отлив" };
            Assert.Equal("Монтаж не предусмотрен для данного товара", item.InstallationToolTip);
        }

        // ─── CurrentInstallationAmount tests ─────────────────

        [Fact]
        public void CurrentInstallationAmount_Mode0_Zero()
        {
            var item = new OrderItem { Name = "Anwis", InstallationMode = 0 };
            Assert.Equal(0, item.CurrentInstallationAmount);
        }

        [Fact]
        public void CurrentInstallationAmount_Mode1_ReturnsDeduction()
        {
            var item = new OrderItem { Name = "Anwis", InstallationMode = 1, InstallationDeduction = 300 };
            Assert.Equal(300, item.CurrentInstallationAmount);
        }

        [Fact]
        public void CurrentInstallationAmount_Mode2_ReturnsSurcharge()
        {
            var item = new OrderItem { Name = "Anwis", InstallationMode = 2, InstallationSurcharge = 200 };
            Assert.Equal(200, item.CurrentInstallationAmount);
        }

        // ─── InstallationDeduction/Surcharge clamping ────────

        [Fact]
        public void InstallationDeduction_ClampedToZero()
        {
            var item = new OrderItem { Name = "Anwis" };
            item.InstallationDeduction = -100;
            Assert.Equal(0, item.InstallationDeduction);
        }

        [Fact]
        public void InstallationSurcharge_ClampedToZero()
        {
            var item = new OrderItem { Name = "Anwis" };
            item.InstallationSurcharge = -100;
            Assert.Equal(0, item.InstallationSurcharge);
        }

        // ─── Clone with AnwisSizeMode test ───────────────────

        [Fact]
        public void Clone_PreservesAnwisSizeMode()
        {
            var original = new OrderItem
            {
                Name = "Anwis",
                Width = 998, Height = 970, // calc-adjusted for ББ 70
            };
            original.SetAnwisModeQuiet(AnwisSizeMode.Брусбокс70);

            var clone = original.Clone();
            Assert.Equal(AnwisSizeMode.Брусбокс70, clone.AnwisSizeMode);
            Assert.Equal(original.Width, clone.Width);
            Assert.Equal(original.Height, clone.Height);
        }

        // ─── Размеры computed property test ──────────────────

        [Fact]
        public void Размеры_ReturnsSizeBasedOnStoredValues()
        {
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 1002, Height = 970,
            };

            Assert.Equal(1000, item.Размеры.ШиринаОтображение);
            Assert.Equal(1002, item.Размеры.ШиринаРасчёт);
            Assert.Equal(982, item.Размеры.ШиринаЗавод);
        }
    }
}
