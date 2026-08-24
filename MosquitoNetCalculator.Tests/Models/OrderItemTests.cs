using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MosquitoNetCalculator;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Models
{
    public class OrderItemTests
    {
        // ─── Recalculate tests ───────────────────────────────

        [Fact]
        public void Recalculate_AreaBasedProduct()
        {
            // v3.48.0: dims below the impost thresholds (width < 500, height < 1500)
            // so this recalc test stays focused on area math, not the impost surcharge.
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 400,
                Height = 1000,
                Quantity = 1,
                Price = 1800
            };
            // Width/Height are calc-adjusted values (400×1000), area = 400*1000/1M = 0.4 м²
            Assert.Equal(0.4, item.CalculatedValue, 3);
            // Total = 0.4 * 1800 * 1 = 720
            Assert.Equal(720, item.Total, 2);
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
        [InlineData("Откос", "шт.")]
        [InlineData("Работа за откос", "шт.")]
        [InlineData("Материал", "шт.")]
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
            // v3.48.0: below impost thresholds (width < 500, height < 1500).
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 400,
                Height = 1000,
                Quantity = 3,
                Price = 1800
            };
            // Width/Height are calc-adjusted (400×1000), area = 0.4 м², Total = 0.4 * 1800 * 3 = 2160
            Assert.Equal(2160, item.Total, 2);
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
        public void Recalculate_CustomProduct_TotalIsPriceTimesQty()
        {
            // «Свой товар» (flagged custom product, not in the catalog): the sum
            // must be Price × Quantity, NOT area-based — otherwise an item with
            // no dimensions shows 0 ₽.
            var item = new OrderItem
            {
                Name = "Шуруп 4×30",
                Width = 0,
                Height = 0,
                Quantity = 1,
                Price = 350,
                IsCustomProduct = true
            };
            Assert.Equal(1, item.CalculatedValue, 3);
            Assert.Equal("шт.", item.Unit);
            Assert.Equal(350, item.Total, 2);
        }

        [Fact]
        public void Recalculate_CustomProduct_ScalesByQuantity()
        {
            var item = new OrderItem
            {
                Name = "Герметик",
                Width = 0,
                Height = 0,
                Quantity = 3,
                Price = 350,
                IsCustomProduct = true
            };
            Assert.Equal(1, item.CalculatedValue, 3);
            Assert.Equal(1050, item.Total, 2);
        }

        [Fact]
        public void Recalculate_CustomProduct_IgnoresPointlessDimensions()
        {
            // Even if the user typed dimensions, a custom product is a manual sum.
            var item = new OrderItem
            {
                Name = "Лента",
                Width = 5000,
                Height = 100,
                Quantity = 2,
                Price = 100,
                IsCustomProduct = true
            };
            Assert.Equal(1, item.CalculatedValue, 3);
            Assert.Equal(200, item.Total, 2);
        }

        [Fact]
        public void GetUnit_CustomProduct_IsPerPiece()
        {
            // «Свой товар» rows report «шт.» via the instance property; a known
            // catalog product keeps its own unit.
            var custom = new OrderItem
            {
                Name = "Шуруп 4×30",
                Price = 350,
                IsCustomProduct = true
            };
            Assert.Equal("шт.", custom.Unit);
            Assert.Equal("м²", OrderItem.GetUnit("Anwis"));
        }

        [Fact]
        public void Recalculate_CustomProduct_ZeroQuantity_ShowsManualSum()
        {
            // User rule: qty defaults to 0 for «Свой товар» and may be omitted.
            // A row with qty 0 must show the manually entered sum (Price × 1),
            // not Price × 0.
            var item = new OrderItem
            {
                Name = "Герметик",
                Width = 0,
                Height = 0,
                Quantity = 1,
                Price = 250,
                IsCustomProduct = true
            };
            item.Quantity = 0; // flag-aware setter keeps 0
            Assert.Equal(0, item.Quantity, 3);
            Assert.Equal(1, item.CalculatedValue, 3);
            Assert.Equal(250, item.Total, 2); // Total = Price × 1, not × 0
        }

        [Fact]
        public void Recalculate_CustomProduct_NonZeroQuantity_ScalesTotal()
        {
            var item = new OrderItem
            {
                Name = "Герметик",
                Quantity = 1,
                Price = 250,
                IsCustomProduct = true
            };
            item.Quantity = 4;
            Assert.Equal(4, item.Quantity, 3);
            Assert.Equal(1000, item.Total, 2); // Price × 4
        }

        [Fact]
        public void Quantity_Setter_NonCustom_StillFloorsAt001()
        {
            // The 0.001 floor stays for everything except «Свой товар».
            var item = new OrderItem { Name = "Anwis", Quantity = 0 };
            Assert.Equal(0.001, item.Quantity, 3);
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
        [InlineData("Дверная сетка", "м²")]
        [InlineData("Отлив", "м²")]
        [InlineData("Козырёк", "м²")]
        [InlineData("Короб", "м²")]
        [InlineData("ПСУЛ", "м.п.")]
        [InlineData("Уплотнение", "м.п.")]
        [InlineData("Откос", "шт.")]
        [InlineData("Работа за откос", "шт.")]
        [InlineData("Работа", "шт.")]
        [InlineData("Брус", "шт.")]
        [InlineData("Пояс", "шт.")]
        [InlineData("Доставка", "шт.")]
        [InlineData("Материал", "шт.")]
        public void Unit_ReturnsCorrectValue(string name, string expectedUnit)
        {
            var item = new OrderItem { Name = name };
            Assert.Equal(expectedUnit, item.Unit);
        }

        // ─── Installation mode tests ─────────────────────────

        [Theory]
        [InlineData(-500, true, -500)]
        [InlineData(500, false, -500)]
        [InlineData(500, true, 500)]
        [InlineData(0, false, -0)]
        [InlineData(double.MaxValue, false, -double.MaxValue)]
        public void NormalizeInstallationAmount_PreservesSignedInput(
            double rawValue, bool isAdd, double expected)
        {
            // The popup accepts both an explicit minus and the +/- toggle.
            // An explicit minus must never be turned into a positive amount.
            Assert.Equal(expected,
                MainWindow.NormalizeInstallationAmount(rawValue, isAdd));
        }

        [Fact]
        public void NormalizeInstallationAmount_RejectsNonFiniteValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                MainWindow.NormalizeInstallationAmount(double.NaN, true));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                MainWindow.NormalizeInstallationAmount(double.PositiveInfinity, true));
            Assert.Equal(-double.MaxValue,
                MainWindow.NormalizeInstallationAmount(double.MinValue, true));
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(0.01, false)]
        [InlineData(-0.01, false)]
        [InlineData(500, true)]
        public void ShouldRefreshInstallationSign_OnlyForMeaningfulAmount(
            double currentValue, bool expected)
        {
            Assert.Equal(expected,
                MainWindow.ShouldRefreshInstallationSign(currentValue));
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
            // v3.46.1: signed convention — negative deduction subtracts.
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 1000, Height = 1000,
                Price = 1800,
                InstallationMode = 1,
                InstallationDeduction = -500
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
                InstallationDeduction = -500
            };
            // Total = 0.01 * 10 = 0.1, deduction = −500 → clamp to 0
            Assert.Equal(0, item.TotalWithDeduction, 2);
        }

        [Fact]
        public void TotalWithDeduction_Mode2_SubtractsSurcharge()
        {
            // v3.46.1: signed convention — negative surcharge subtracts.
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 1000, Height = 1000,
                Price = 1800,
                InstallationMode = 2,
                InstallationSurcharge = -200
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
                InstallationDeduction = 0
            };
            // v3.47.0: Отлив is now installation-applicable, but default mode 1
            // with 0 deduction means TotalWithDeduction equals Total.
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
            // v3.48.0: below impost thresholds (width < 500, height < 1500).
            var item = new OrderItem
            {
                Name = "На навесах",
                Width = 300, Height = 1000,
                Quantity = 3,
                Price = 3000,
                InstallationMode = 2
            };
            // CV = 0.3 м², Total = 0.3 × 3000 × 3 = 2700 ₽
            Assert.Equal(2700, item.Total, 2);
            // Post-fix: 2700 − 500×3 = 1200 ₽  (was 1300 ₽ pre-fix)
            Assert.Equal(1200, item.TotalWithDeduction, 2);
        }

        [Fact]
        public void TotalWithDeduction_Mode1_Quantity3_SubtractsPerPiece()
        {
            // Same fix applies to «Без монтажа» — deduction is per piece.
            // v3.48.0: below impost thresholds (width < 500, height < 1500).
            var item = new OrderItem
            {
                Name = "На навесах",
                Width = 300, Height = 1000,
                Quantity = 3,
                Price = 3000,
                InstallationMode = 1
            };
            Assert.Equal(2700, item.Total, 2);
            Assert.Equal(1200, item.TotalWithDeduction, 2);
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
            // v3.46.1: signed convention — negative surcharge subtracts per piece.
            // v3.48.0: below impost thresholds (width < 500, height < 1500).
            var item = new OrderItem
            {
                Name = "На навесах",
                Width = 300, Height = 1000,
                Quantity = 3,
                Price = 3000,
                InstallationMode = 2,
                InstallationSurcharge = -200
            };
            // 2700 + (−200×3) = 2100 ₽
            Assert.Equal(2100, item.TotalWithDeduction, 2);
        }

        [Fact]
        public void TotalWithDeduction_AnwisMode2_Quantity3_SubtractsPerPiece()
        {
            // Same behaviour for Anwis. Use Профипласт mode (size identity
            // — no W+2/H−30 shift) so Width/Height stay user-typed = stored.
            // v3.48.0: below impost thresholds (width < 500, height < 1500).
            var item = new OrderItem
            {
                Name = "Anwis",
                AnwisSizeMode = AnwisSizeMode.Профипласт,
                Width = 400, Height = 1000,
                Quantity = 3,
                Price = 1800,
                InstallationMode = 2
            };
            // CV = 0.4, Total = 2160, TotalWithDeduction = 2160 − 1500 = 660
            Assert.Equal(2160, item.Total, 2);
            Assert.Equal(660, item.TotalWithDeduction, 2);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        public void TotalWithDeduction_Mode2_QuantityScaling_IsLinearWithQ(int qty)
        {
            // v3.48.0: below impost thresholds (width < 500, height < 1500).
            var item = new OrderItem
            {
                Name = "На навесах",
                Width = 300, Height = 1000,
                Quantity = qty,
                Price = 3000,
                InstallationMode = 2
            };
            // CV = 0.3 м², Total = 0.3 × 3000 × qty = 900 × qty
            // deduction = 500 × qty
            // TotalWithDeduction = (900 − 500) × qty = 400 × qty
            // Linear-in-qty assertion: scaling is the proof that deduction
            // multiplies by Quality and not a flat fee.
            double expectedTotal = 900.0 * qty;
            double expectedTotalWithDeduction = 400.0 * qty;
            Assert.Equal(expectedTotal, item.Total, 2);
            Assert.Equal(expectedTotalWithDeduction, item.TotalWithDeduction, 2);
        }

        [Fact]
        public void TotalWithDeduction_Otliv_DefaultMode_NoInstallationCost()
        {
            // v3.47.0: Отлив supports installation but defaults to mode 1 (no installation)
            // with 0 ₽ deduction, so TotalWithDeduction equals Total.
            var item = new OrderItem
            {
                Name = "Отлив",
                Width = 1500, Height = 100,
                Quantity = 1,
                Price = 2150,
                InstallationMode = 1,
                InstallationDeduction = 0
            };
            Assert.Equal(item.Total, item.TotalWithDeduction);
        }

        [Fact]
        public void TotalWithDeduction_Otliv_Mode0_PerLinearMeter()
        {
            // v3.47.0: Отлив installation is 500 ₽/м.п.
            // Perimeter = (1500 + 100) * 2 / 1000 = 3.2 м.п.
            // Installation cost = 500 * 3.2 = 1600 ₽
            // Total = 0.15 * 2150 = 322.5; TotalWithDeduction = 322.5 + 1600 = 1922.5
            var item = new OrderItem
            {
                Name = "Отлив",
                Width = 1500, Height = 100,
                Quantity = 1,
                Price = 2150,
                InstallationMode = 0,
                InstallationAdjustment = 500
            };
            Assert.Equal(1922.5, item.TotalWithDeduction, 2);
        }

        [Fact]
        public void TotalWithDeduction_Kozyrek_Mode0_PerLinearMeter()
        {
            // v3.47.0: Козырёк installation is 750 ₽/м.п.
            // Perimeter = (1200 + 200) * 2 / 1000 = 2.8 м.п.
            // Installation cost = 750 * 2.8 = 2100 ₽
            // Total = 0.24 * 3000 = 720; TotalWithDeduction = 720 + 2100 = 2820
            var item = new OrderItem
            {
                Name = "Козырёк",
                Width = 1200, Height = 200,
                Quantity = 1,
                Price = 3000,
                InstallationMode = 0,
                InstallationAdjustment = 750
            };
            Assert.Equal(2820, item.TotalWithDeduction, 2);
        }

        [Fact]
        public void TotalWithDeduction_Otliv_Mode0_QuantityMultiplier_AppliesToLinearMeters()
        {
            // v3.47.0: per-linear-meter installation also scales with Quantity.
            // Perimeter = 3.2 м.п., Qty = 2 → linear factor = 6.4
            // Installation = 500 * 6.4 = 3200 ₽
            var item = new OrderItem
            {
                Name = "Отлив",
                Width = 1500, Height = 100,
                Quantity = 2,
                Price = 2150,
                InstallationMode = 0,
                InstallationAdjustment = 500
            };
            double expectedTotal = 0.15 * 2150 * 2; // 645
            double expected = expectedTotal + 500 * 3.2 * 2; // 645 + 3200 = 3845
            Assert.Equal(expected, item.TotalWithDeduction, 2);
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

        [Fact]
        public void IsInstallationApplicable_DvernayaSetka_True()
        {
            Assert.True(new OrderItem { Name = "Дверная сетка" }.IsInstallationApplicable);
        }

        [Fact]
        public void IsInstallationApplicable_OkonnayaNaMetallKrepl_True()
        {
            // v3.43.2.10: extend the mounted-product toggle (like Anwis /
            // На навесах / Дверная сетка) to «Оконная на метал. крепл.» —
            // user-requested feature parity. Per-piece deduction falls back
            // to the standard 500 ₽ (see DefaultInstallationDeduction in
            // OrderItem.Installation.cs).
            Assert.True(new OrderItem { Name = "Оконная на метал. крепл." }.IsInstallationApplicable);
        }

        [Theory]
        [InlineData("Короб")]
        [InlineData("ПСУЛ")]
        [InlineData("Работа")]
        [InlineData("Доставка")]
        [InlineData("Уплотнение")]
        public void IsInstallationApplicable_OtherProducts_False(string name)
        {
            Assert.False(new OrderItem { Name = name }.IsInstallationApplicable);
        }

        // ─── InstallationAdjustment tests (v3.43.2.11) ───────────────
        //
        // Mode 0 («Монтаж включён») теперь поддерживает signed adjustment,
        // ИНТУИТИВНАЯ конвенция (+ добавляет, − вычитает):
        //   • положительное значение добавляется к Total (надбавка);
        //   • отрицательное значение вычитается из Total (формула сама инвертирует знак);
        //   • 0 (default) — Total без изменений.

        [Fact]
        public void InstallationAdjustment_Defaults_To_Zero()
        {
            var item = new OrderItem { Name = "Anwis" };
            Assert.Equal(0, item.InstallationAdjustment);
        }

        [Fact]
        public void InstallationAdjustment_Positive_Kept_AsIs()
        {
            var item = new OrderItem { Name = "Anwis", InstallationAdjustment = 500 };
            Assert.Equal(500, item.InstallationAdjustment);
        }

        [Fact]
        public void InstallationAdjustment_Negative_Kept_AsIs()
        {
            // v3.43.2.10: setter НЕ клампит отрицательные — для mode 0
            // это семантически означает «добавить к сумме».
            var item = new OrderItem { Name = "Anwis", InstallationAdjustment = -300 };
            Assert.Equal(-300, item.InstallationAdjustment);
        }

        [Fact]
        public void InstallationAdjustment_Fires_PropertyChanged()
        {
            var item = new OrderItem { Name = "Anwis" };
            bool fired = false;
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(OrderItem.InstallationAdjustment))
                    fired = true;
            };
            item.InstallationAdjustment = 100;
            Assert.True(fired, "InstallationAdjustment setter must fire PropertyChanged");
        }

        [Fact]
        public void CurrentInstallationAmount_Mode0_ReturnsAdjustment()
        {
            var item = new OrderItem { Name = "Anwis", InstallationMode = 0, InstallationAdjustment = 350 };
            Assert.Equal(350, item.CurrentInstallationAmount);
        }

        [Fact]
        public void CurrentInstallationAmount_Mode0_Default_Returns_Zero()
        {
            var item = new OrderItem { Name = "Anwis", InstallationMode = 0 };
            Assert.Equal(0, item.CurrentInstallationAmount);
        }

        [Fact]
        public void SetCurrentInstallationAmount_Mode0_Writes_To_Adjustment()
        {
            // Явный override дефолтных значений 500 для Deduction/Surcharge —
            // чтобы тест проверял именно «не трогаем», а не fallback от ctor'а.
            var item = new OrderItem
            {
                Name = "Anwis",
                InstallationMode = 0,
                InstallationDeduction = 0,
                InstallationSurcharge = 0,
            };
            item.SetCurrentInstallationAmount(450);
            Assert.Equal(450, item.InstallationAdjustment);
            Assert.Equal(0, item.InstallationDeduction);  // не трогаем
            Assert.Equal(0, item.InstallationSurcharge); // не трогаем
        }

        [Fact]
        public void SetCurrentInstallationAmount_Mode0_Allows_Negative()
        {
            // v3.43.2.10: убран `if (value < 0) value = 0` из SetCurrentInstallationAmount.
            var item = new OrderItem { Name = "Anwis", InstallationMode = 0 };
            item.SetCurrentInstallationAmount(-300);
            Assert.Equal(-300, item.InstallationAdjustment);
        }

        [Fact]
        public void TotalWithDeduction_Mode0_PositiveAdjustment_Adds()
        {
            // v3.43.2.11: интуитивная конвенция — положительное значение добавляется к Total.
            // v3.48.0: below impost thresholds (width < 500, height < 1500).
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 400, Height = 1000, Price = 1800,
                InstallationMode = 0,
                InstallationAdjustment = 500,
            };
            // Total = 0.4 × 1800 = 720; 720 + 500×1 = 1220
            Assert.Equal(1220, item.TotalWithDeduction, 2);
        }

        [Fact]
        public void TotalWithDeduction_Mode0_NegativeAdjustment_Subtracts()
        {
            // v3.43.2.11: интуитивная конвенция — отрицательное значение вычитается из Total.
            // v3.48.0: below impost thresholds (width < 500, height < 1500).
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 400, Height = 1000, Price = 1800,
                InstallationMode = 0,
                InstallationAdjustment = -200,
            };
            // Total = 0.4 × 1800 = 720; 720 + (−200)×1 = 520
            Assert.Equal(520, item.TotalWithDeduction, 2);
        }

        [Fact]
        public void TotalWithDeduction_Mode0_ZeroAdjustment_ReturnsTotal()
        {
            // default case: no-op, Total passthrough.
            // v3.48.0: below impost thresholds (width < 500, height < 1500).
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 400, Height = 1000, Price = 1800,
                InstallationMode = 0,
            };
            // Total = 0.4 × 1800 = 720
            Assert.Equal(720, item.TotalWithDeduction, 2);
        }

        [Fact]
        public void TotalWithDeduction_Mode0_AdjustmentScaledByQuantity()
        {
            // Per-piece × Qty: На навесах используем (identity sizing — никаких Anwis формул).
            // v3.48.0: below impost thresholds (width < 500, height < 1500).
            // Total = 0.3 × 1200 × 3 = 1080; Adjustment = -300 × 3 = -900;
            // TotalWithDeduction = 1080 + (−900) = 180.
            var item = new OrderItem
            {
                Name = "На навесах",
                Width = 300, Height = 1000, Quantity = 3, Price = 1200,
                InstallationMode = 0,
                InstallationAdjustment = -300,
            };
            Assert.Equal(180, item.TotalWithDeduction, 2);
        }

        [Fact]
        public void TotalWithDeduction_Mode0_ClampsToZero_WhenAdjustmentExceeds()
        {
            // v3.43.2.11: флипнут конвенцию — clamp-to-zero теперь проверяем с большим
            // ОТРИЦАТЕЛЬНЫМ adjustment (Total=100, Adjustment=-1500 × Q=1 = -1500 → зажимается в 0).
            // Положительное же значение при той же формуле даёт 100 + 1500 = 1600 (без clamp),
            // поэтому для теста clamp-ветки нужно именно большое отрицательное.
            // «На навесах» (identity-sized, в InstallationApplicableProducts с v3.43.2.9).
            var item = new OrderItem
            {
                Name = "На навесах",
                Width = 1000, Height = 1000, Quantity = 1, Price = 100, // CV=1.0, Total=100
                InstallationMode = 0,
                InstallationAdjustment = -1500,
            };
            Assert.Equal(0, item.TotalWithDeduction, 2);
        }

        // v3.43.2.11: regression guard sister-test for `_ClampsToZero_WhenAdjustmentExceeds`
        // (above). Both sister-tests use «На навесах» (identity-sized, no Anwis formula
        // constants) so the only thing that changes between them is the SIGN of the
        // Adjustment — readers scanning the pair see «same product, same Total=100 base,
        // only Adjustment sign + magnitude flip». Post-flip formula `Total + Adj × Q`
        // can ONLY trigger the `Math.Max(0, …)` clamp via a NEGATIVE adjustment (giant
        // positive pushes higher, never lower). The clamp-test above covers the
        // negative-extreme; this covers the positive-extreme to guarantee it does NOT
        // over-clamp — a future "always clamp" refactor would silently convert
        // 1000099 → 0 and only this test would catch it.
        [Fact]
        public void TotalWithDeduction_Mode0_HugePositiveAdjustment_DoesNotClamp()
        {
            // v3.48.0: below impost thresholds (width < 500, height < 1500).
            var item = new OrderItem
            {
                Name = "На навесах",
                Width = 400, Height = 1000, Quantity = 1, Price = 100, // CV=0.4, Total=40
                InstallationMode = 0,
                InstallationAdjustment = 999999,
            };
            // 40 + 999999×1 = 1000039 → Math.Round(1000039, 2) = 1000039 (no clamp).
            // Both 1000039 (literal) and the computed value are exactly representable
            // integers in IEEE-754 double (≤ 2^53), so `Assert.Equal(int, double)`
            // (exact equality, no precision arg) is the right assertion here.
            Assert.Equal(1000039, item.TotalWithDeduction);
        }

        [Fact]
        public void InstallationToolTip_Mode0_AdjustmentZero_HidesAmount()
        {
            var item = new OrderItem { Name = "Anwis", InstallationMode = 0, InstallationAdjustment = 0 };
            Assert.Contains("Монтаж включён", item.InstallationToolTip);
            // При нулевой корректировке не показываем «× Кол-во»/«вычитается».
            Assert.DoesNotContain("руб./шт.", item.InstallationToolTip);
            Assert.DoesNotContain("вычитается", item.InstallationToolTip);
        }

        [Fact]
        public void InstallationToolTip_Mode0_AdjustmentPositive_ShowsPlus()
        {
            // v3.43.2.11: положительное adjustment → tooltip показывает «+500 добавляется к сумме».
            var item = new OrderItem { Name = "Anwis", InstallationMode = 0, InstallationAdjustment = 500 };
            Assert.Contains("+500", item.InstallationToolTip);
        }

        [Fact]
        public void InstallationToolTip_Mode0_AdjustmentNegative_ShowsMinus()
        {
            // v3.43.2.11: отрицательное adjustment → tooltip показывает «−300 вычитается из суммы».
            var item = new OrderItem { Name = "Anwis", InstallationMode = 0, InstallationAdjustment = -300 };
            Assert.Contains("−300", item.InstallationToolTip);
        }

        [Fact]
        public void InstallationAdjustment_Independent_From_Other_Modes()
        {
            // При переключении режимов значения adjustment/deduction/surcharge не обнуляются:
            // user мог настроить 500 в режиме 0 (вычесть), перейти в режим 1 (вычитать
            // дефолтные 500), потом вернуться — adjustment=500 сохраняется.
            var item = new OrderItem
            {
                Name = "Anwis",
                InstallationMode = 0,
                InstallationAdjustment = 500,
                InstallationDeduction = 500,
                InstallationSurcharge = 500,
            };
            item.InstallationMode = 1;
            Assert.Equal(500, item.InstallationDeduction);
            Assert.Equal(500, item.InstallationAdjustment); // сохраняется
            item.InstallationMode = 0;
            Assert.Equal(500, item.InstallationAdjustment); // значение вернулось
        }

        [Fact]
        public void Clone_PreservesInstallationAdjustment()
        {
            var original = new OrderItem
            {
                Name = "Anwis", Width = 1000, Height = 1000, Price = 1800,
                InstallationMode = 0,
                InstallationAdjustment = 250,
                InstallationDeduction = 999,
            };
            var clone = original.Clone();
            Assert.Equal(250, clone.InstallationAdjustment);
            Assert.Equal(999, clone.InstallationDeduction);
        }

        [Fact]
        public void SetCurrentInstallationAmount_Mode1_AcceptsNegative()
        {
            // v3.46.1: all modes use signed convention — negative = subtract.
            var item = new OrderItem { Name = "Anwis", InstallationMode = 1 };
            item.SetCurrentInstallationAmount(-500);
            Assert.Equal(-500, item.InstallationDeduction);
        }

        [Fact]
        public void SetCurrentInstallationAmount_Mode2_AcceptsNegative()
        {
            // v3.46.1: all modes use signed convention — negative = subtract.
            var item = new OrderItem { Name = "Anwis", InstallationMode = 2 };
            item.SetCurrentInstallationAmount(-500);
            Assert.Equal(-500, item.InstallationSurcharge);
        }

        [Theory]
        [InlineData("Anwis", -500)]
        [InlineData("На навесах", -500)]
        [InlineData("Оконная на метал. крепл.", -500)]
        [InlineData("Дверная сетка", -600)]
        [InlineData("Отлив", 0)]
        [InlineData("Козырёк", 0)]
        public void GetDefaultInstallationSurcharge_ReturnsExpected(string name, double expected)
        {
            // v3.47.4 (fix): switching to "В конструкцию" resets the surcharge
            // to the product's default. Per-piece products default to −500/−600,
            // per-linear-meter products default to 0.
            Assert.Equal(expected, OrderItem.GetDefaultInstallationSurcharge(name));
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
            var item = new OrderItem { Name = "ПСУЛ" };
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
        }        [Fact]
        public void InstallationLabel_FlipsOnNameChange_AndFiresPropertyChanged()
        {
            // Name drives IsInstallationApplicable, which drives InstallationLabel.
            // Mutating Name from "Anwis" to "ПСУЛ" must notify the binding.
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
            item.Name = "ПСУЛ";
            Assert.True(fired, "InstallationLabel PropertyChanged must fire on Name change");
            Assert.Equal("Монтаж не предусмотрен", observed);
        }

        [Fact]
        public void KpInstallationDisplay_NonApplicable_ReturnsDash()
        {
            var item = new OrderItem { Name = "ПСУЛ" };
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
            var item = new OrderItem { Name = "ПСУЛ" };
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
        public void SetCurrentInstallationAmount_NegativeAcceptedAsIs()
        {
            // v3.46.1: all modes accept signed values (negative = subtract).
            var item = new OrderItem { Name = "Anwis", InstallationMode = 1 };
            item.SetCurrentInstallationAmount(-100);
            Assert.Equal(-100, item.InstallationDeduction);
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
        }        [Fact]
        public void Quantity_ClampedToMinimumOne()
        {
            // v3.47.0: Quantity is double and clamps to a small positive
            // value to allow decimal quantities like 0.5.
            var item = new OrderItem { Quantity = 0 };
            Assert.True(item.Quantity > 0);
            Assert.Equal("0,001", item.QuantityDisplay);


            var item2 = new OrderItem { Quantity = -5 };
            Assert.True(item2.Quantity > 0);
            Assert.Equal("0,001", item2.QuantityDisplay);
        }

        [Fact]
        public void Quantity_AllowsDecimalValues()
        {
            // v3.47.0: Quantity is double, so 5.75 and 0.5 are valid.
            var item = new OrderItem { Name = "Anwis", Width = 1000, Height = 1000, Price = 1800, Quantity = 5.75 };
            Assert.Equal(5.75, item.Quantity);
            Assert.Equal("5,75", item.QuantityDisplay);

            var item2 = new OrderItem { Name = "Anwis", Width = 1000, Height = 1000, Price = 1800, Quantity = 0.5 };
            Assert.Equal(0.5, item2.Quantity);
            Assert.Equal("0,5", item2.QuantityDisplay);
        }

        [Fact]
        public void Quantity_DecimalMultiplier_UsedInTotal()
        {
            // v3.47.0: decimal Quantity must multiply Total correctly.
            // v3.48.0: below impost thresholds (width < 500, height < 1500).
            var item = new OrderItem { Name = "Anwis", Width = 400, Height = 1000, Price = 1800, Quantity = 2.5 };
            // CV = 0.4, Total = 0.4 * 1800 * 2.5 = 1800
            Assert.Equal(1800, item.Total, 2);
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

        // ─── Optional quantity product tests (Материал) ──────

        [Fact]
        public void IsQuantityOptional_True_ForMaterial()
        {
            Assert.True(new OrderItem { Name = "Материал" }.IsQuantityOptional);
        }

        [Theory]
        [InlineData("Работа")]
        [InlineData("Брус")]
        [InlineData("Anwis")]
        [InlineData("Отлив")]
        public void IsQuantityOptional_False_ForOtherProducts(string name)
        {
            Assert.False(new OrderItem { Name = name }.IsQuantityOptional);
        }

        [Fact]
        public void QuantityDisplay_Material_DefaultQuantity_Hidden()
        {
            // Материал with default quantity (1) hides quantity in the grid.
            var item = new OrderItem { Name = "Материал", Quantity = 1, Price = 5000 };
            Assert.Equal("", item.QuantityDisplay);
        }

        [Fact]
        public void CalculatedValueDisplay_Material_DefaultQuantity_Hidden()
        {
            // Материал with default quantity (1) hides calculated value in the grid.
            var item = new OrderItem { Name = "Материал", Quantity = 1, Price = 5000 };
            Assert.Equal("", item.CalculatedValueDisplay);
        }

        [Fact]
        public void QuantityDisplay_Material_QuantityGreaterThanOne_Visible()
        {
            // Материал with quantity > 1 shows quantity in the grid.
            var item = new OrderItem { Name = "Материал", Quantity = 3, Price = 1000 };
            Assert.Equal("3", item.QuantityDisplay);
        }

        [Fact]
        public void QuantityDisplay_CustomProduct_ExplicitOne_Visible()
        {
            // «Свой товар»: the user EXPLICITLY entered qty 1 → it must show
            // in the grid AND on print (0 means «не указано», 1 is a real value).
            var item = new OrderItem
            {
                Name = "Герметик",
                Quantity = 1,
                Price = 250,
                IsCustomProduct = true
            };
            Assert.Equal("1", item.QuantityDisplay);
        }

        [Fact]
        public void QuantityDisplay_CustomProduct_Zero_Hidden()
        {
            // Qty was not specified → cell stays empty (user requirement).
            // Note: set IsCustomProduct BEFORE Quantity — the flag-aware setter
            // allows 0 only when the flag is already on (matches production order).
            var item = new OrderItem
            {
                Name = "Герметик",
                Price = 250,
                IsCustomProduct = true
            };
            item.Quantity = 0;
            Assert.Equal(0, item.Quantity, 3);
            Assert.Equal("", item.QuantityDisplay);
        }

        [Fact]
        public void QuantityDisplay_CustomProduct_Multiple_Visible()
        {
            var item = new OrderItem
            {
                Name = "Герметик",
                Quantity = 4,
                Price = 250,
                IsCustomProduct = true
            };
            Assert.Equal("4", item.QuantityDisplay);
        }

        [Fact]
        public void CalculatedValueDisplay_Material_QuantityGreaterThanOne_Visible()
        {
            // Материал with quantity > 1 shows calculated value (шт.) in the grid.
            var item = new OrderItem { Name = "Материал", Quantity = 3, Price = 1000 };
            Assert.Equal("3 шт.", item.CalculatedValueDisplay);
        }

        [Fact]
        public void Material_Total_PriceTimesQuantity()
        {
            // Total for Материал = Price * Quantity (CalculatedValue = 1).
            var item = new OrderItem { Name = "Материал", Quantity = 3, Price = 1500 };
            Assert.Equal(1, item.CalculatedValue, 3);
            Assert.Equal(4500, item.Total, 2);
        }

        [Fact]
        public void Material_IsManualPiece_True()
        {
            Assert.True(new OrderItem { Name = "Материал" }.IsManualPiece);
        }

        [Fact]
        public void Material_IsAmountOnly_False()
        {
            Assert.False(new OrderItem { Name = "Материал" }.IsAmountOnly);
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
        [InlineData("Откос")]
        [InlineData("Работа за откос")]
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
        public void IsWidthOnly_True_ForOtkos()
        {
            // Откос records Width as a per-row spec but doesn't use
            // it in the Total formula, so Width stays editable while Height/Color
            // stay blocked (same gate as other ManualPiece products).
            Assert.True(new OrderItem { Name = "Откос" }.IsWidthOnly);
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
            item.Name = "Откос";
            Assert.True(item.IsWidthOnly);
            Assert.True(fired, "IsWidthOnly must fire PropertyChanged when Name changes");
        }

        [Fact]
        public void TotalWithDeduction_Mode1_RoundedToTwoDecimals()
        {
            // v3.46.1: signed convention — negative deduction subtracts.
            // v3.48.0: below impost thresholds (width < 500, height < 1500).
            var item = new OrderItem
            {
                Name = "Anwis",
                Width = 400, Height = 1000,
                Price = 1000,
                InstallationMode = 1,
                InstallationDeduction = -333.50
            };
            // Total = 400.0, 400 + (−333.50) = 66.50
            Assert.Equal(66.50, item.TotalWithDeduction, 2);
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
            var item = new OrderItem { Name = "Отлив", Width = 1500, Height = 100 };
            Assert.Equal(1500, item.ШиринаВвод);
        }

        [Fact]
        public void ВысотаВвод_NonAnwis_ReturnsIdentity_NotPlus30()
        {
            // Regression test for v3.35.0 bug: non-Anwis products (Откос,
            // Работа, Пояс) showed height=30mm because ReverseCalcHeight(0, ББ60)
            // returned 0+30=30. After fix, ВысотаВвод = stored Height = 0.
            var item = new OrderItem { Name = "Откос", Width = 250, Height = 0 };
            Assert.Equal(0, item.ВысотаВвод);
        }

        [Theory]
        [InlineData("Откос")]
        [InlineData("Работа за откос")]
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
            // v3.35.0 fix: editing width on non-Anwis (e.g. Откос)
            // should store raw value directly, not apply Anwis calc (+2 for ББ60).
            var item = new OrderItem
            {
                Name = "Откос",
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
            // v3.46.1: signed convention — negative = subtract, shows «−» and amount.
            var item = new OrderItem
            {
                Name = "Anwis",
                InstallationMode = 1,
                InstallationDeduction = -500
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
            var item = new OrderItem { Name = "ПСУЛ" };
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

        // ─── InstallationDeduction/Surcharge: no clamping (v3.46.1) ──

        [Fact]
        public void InstallationDeduction_AcceptsNegative()
        {
            // v3.46.1: clamping removed — signed convention (negative = subtract).
            var item = new OrderItem { Name = "Anwis" };
            item.InstallationDeduction = -100;
            Assert.Equal(-100, item.InstallationDeduction);
        }

        [Fact]
        public void InstallationSurcharge_AcceptsNegative()
        {
            // v3.46.1: clamping removed — signed convention (negative = subtract).
            var item = new OrderItem { Name = "Anwis" };
            item.InstallationSurcharge = -100;
            Assert.Equal(-100, item.InstallationSurcharge);
        }

        // ─── Anticat tests ───────────────────────────────────

        [Fact]
        public void DisplayName_ReturnsName_WhenNotAnticat()
        {
            var item = new OrderItem { Name = "Anwis" };
            Assert.Equal("Anwis", item.DisplayName);
        }

        [Fact]
        public void DisplayName_AppendsSuffix_WhenAnticat()
        {
            var item = new OrderItem { Name = "Anwis", IsAnticat = true };
            Assert.Equal("Anwis (Антикошка)", item.DisplayName);
        }

        [Fact]
        public void IsAnticat_FiresPropertyChanged()
        {
            var item = new OrderItem { Name = "Anwis" };
            bool fired = false;
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(OrderItem.IsAnticat))
                    fired = true;
            };
            item.IsAnticat = true;
            Assert.True(fired);
        }

        [Fact]
        public void IsAnticat_FiresDisplayNamePropertyChanged()
        {
            var item = new OrderItem { Name = "Anwis" };
            bool fired = false;
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(OrderItem.DisplayName))
                    fired = true;
            };
            item.IsAnticat = true;
            Assert.True(fired);
        }

        [Fact]
        public void Clone_PreservesIsAnticat()
        {
            var original = new OrderItem { Name = "Anwis", IsAnticat = true };
            var clone = original.Clone();
            Assert.True(clone.IsAnticat);
            Assert.Equal("Anwis (Антикошка)", clone.DisplayName);
        }

        [Theory]
        [InlineData("Anwis", true)]
        [InlineData("На навесах", true)]
        [InlineData("Оконная на метал. крепл.", true)]
        [InlineData("Дверная сетка", true)]
        [InlineData("Отлив", false)]
        [InlineData("ПСУЛ", false)]
        public void AnticatApplicableProducts_ContainsExpected(string name, bool expected)
        {
            Assert.Equal(expected, OrderItem.AnticatApplicableProducts.Contains(name));
        }

        // ─── Impost tests (v3.48.0) ──────────────────────────

        [Theory]
        [InlineData("Anwis", true)]
        [InlineData("На навесах", true)]
        [InlineData("Оконная на метал. крепл.", true)]
        [InlineData("Дверная сетка", false)]
        [InlineData("Отлив", false)]
        [InlineData("ПСУЛ", false)]
        [InlineData("Работа", false)]
        public void ImpostApplicableProducts_ContainsExpected(string name, bool expected)
        {
            Assert.Equal(expected, OrderItem.ImpostApplicableProducts.Contains(name));
        }

        // v3.48.0 fix: критерии импоста считаются по ВВОДНЫМ размерам.
        // v3.48.x fix (владелец): импост — по РАСЧЁТНЫМ размерам (Width/Height
        // после Anwis-коррекции режима), как цена и КП. «На навесах» —
        // identity-товар (расчётные = хранимым = вводным), поэтому теория тут
        // проверяет ровно границы расчёта; Anwis-строки проверяют ББ60-сдвиг
        // (ширина +2, высота −30).
        [Theory]
        [InlineData("На навесах", 600, 800, true)]   // width criterion
        [InlineData("На навесах", 400, 1600, true)]  // height criterion
        [InlineData("На навесах", 600, 1600, true)]  // both criteria
        [InlineData("На навесах", 400, 800, false)]  // neither criterion
        [InlineData("На навесах", 500, 800, true)]   // width boundary 500 → impost
        [InlineData("На навесах", 499, 800, false)]  // width boundary 499 → no impost
        [InlineData("На навесах", 400, 1500, true)]  // height boundary 1500 → impost
        [InlineData("На навесах", 400, 1499, false)] // height boundary 1499 → no impost
        // Anwis (ББ6: расчёт = ввод +2/+−30): критерий по РАСЧЁТНЫМ размерам.
        [InlineData("Anwis", 430, 1510, false)]  // расчёт 432×1480 — оба ниже порогов (высота 1480 < 1500) → нет импоста
        [InlineData("Anwis", 498, 1540, true)]   // расчёт 500×1510 → ширина 500 ≥ 500 → импост (по вводу 498 < 500 было бы нет)
        [InlineData("Anwis", 500, 1500, true)]   // расчёт 502×1470 → ширина 502 ≥ 500 → импост (эталон владельца: 502)
        [InlineData("Anwis", 1400, 1460, true)]  // расчёт 1406×1430 → ширина-критерий → импост; высота 1430 < 1500 не мешает
        [InlineData("Anwis", 400, 1529, false)]  // расчёт 402×1499 → высота 1499 < 1500 → нет импоста (ввод 1529 ≥ 1500 не играет роли)
        public void HasImpost_WidthOrHeightCriterion(string name, double width, double height, bool expected)
        {
            // Клаём ВВОДНЫЕ размеры в хранимые через raw-сеттеры ШиринаВвод/ВысотаВвод,
            // как это делает DataGrid-правка; вя Anwis они применят Anwis-формулу
            // и лягут в расчётные Width/Height.
            var item = new OrderItem { Name = name };
            item.ШиринаВвод = width;
            item.ВысотаВвод = height;
            Assert.Equal(expected, item.HasImpost);
        }

        [Fact]
        public void HasImpost_DvernayaSetka_AlwaysFalse_EvenWhenLarge()
        {
            // Door screen is excluded from the impost system entirely.
            var item = new OrderItem { Name = "Дверная сетка", Width = 600, Height = 1600 };
            Assert.False(item.HasImpost);
        }

        [Fact]
        public void HasImpost_NonScreenProducts_AlwaysFalse()
        {
            Assert.False(new OrderItem { Name = "Отлив", Width = 600, Height = 1600 }.HasImpost);
            Assert.False(new OrderItem { Name = "Короб", Width = 600, Height = 1600 }.HasImpost);
            Assert.False(new OrderItem { Name = "Работа", Width = 600, Height = 1600 }.HasImpost);
        }

        [Fact]
        public void ImpostLinearMeters_MeasuredFromCalcWidth()
        {
            // «На навесах» — identity: введённая ширина = расчётная.
            var item = new OrderItem { Name = "На навесах" };
            item.ШиринаВвод = 430;
            item.ВысотаВвод = 1510;
            Assert.Equal(0.43, item.ImpostLinearMeters);

            // Anwis ББ6: ввод 500 → расчёт 502 → м.п. от 0,502, не 0,500.
            var anwis = new OrderItem { Name = "Anwis" };
            anwis.ШиринаВвод = 500;
            anwis.ВысотаВвод = 1500;
            Assert.Equal(502, anwis.Width, 0);
            Assert.Equal(0.502, anwis.ImpostLinearMeters);
        }

        [Fact]
        public void Total_AddsImpost_FromWidth_WhenHeightCriterionTriggered()
        {
            // 430×1510 — импост сработал по ВЫСОТЕ, но сумма от ШИРИНЫ (вводной):
            // 200 × 0,43 = 86 ₽. Базовый Total — от расчётной площади (0.649 м²).
            var item = new OrderItem { Name = "На навесах", Width = 430, Height = 1510, Price = 1000, Quantity = 1 };
            double baseTotal = Math.Round(Math.Round(430.0 * 1510 / 1_000_000.0, 3) * 1000, 2); // 649
            Assert.True(item.HasImpost);
            Assert.Equal(Math.Round(baseTotal + 86, 2), item.Total); // 200 × 0.43 × 1
        }

        [Fact]
        public void Total_AddsImpost_FromWidth_WhenWidthCriterionTriggered()
        {
            // 600×800 — ширина → 200 × 0.60.
            var item = new OrderItem { Name = "На навесах", Width = 600, Height = 800, Price = 1800, Quantity = 1 };
            double baseTotal = Math.Round(600.0 * 800 / 1_000_000.0 * 1800, 2); // 864
            Assert.True(item.HasImpost);
            Assert.Equal(Math.Round(baseTotal + 120, 2), item.Total); // 200 × 0.60
        }

        [Fact]
        public void Total_AddsImpost_FromWidth_WhenBothCriteriaTriggered()
        {
            // Оба критерия → всё равно от ширины.
            var item = new OrderItem { Name = "На навесах", Width = 600, Height = 1600, Price = 1800, Quantity = 1 };
            double baseTotal = Math.Round(600.0 * 1600 / 1_000_000.0 * 1800, 2);
            Assert.True(item.HasImpost);
            Assert.Equal(Math.Round(baseTotal + 120, 2), item.Total);
        }

        [Fact]
        public void Total_Anwis_Impost_FromCalcWidth_NotEnteredWidth()
        {
            // Anwis ББ60: ввод 500×1500 хранится как 502×1470.
            // Критерий и сумма — по РАСЧЁТНЫМ: импост (502 ≥ 500), надбавка
            // 200 × 0,502 = 100,40 (а не 100 от вводной 500).
            var item = new OrderItem { Name = "Anwis" };
            item.ШиринаВвод = 500;
            item.ВысотаВвод = 1500;
            Assert.Equal(502, item.Width, 0);
            Assert.Equal(1470, item.Height, 0);
            Assert.True(item.HasImpost);
            Assert.Equal(100.4, item.ImpostSurchargeTotal, 2);

            // Полный Total: расчётная площадь 0.738 м² (502×1470, CV=0.738)
            // × 1000 + 100,40 = 838,40.
            item.Price = 1000;
            Assert.Equal(Math.Round(Math.Round(502.0 * 1470 / 1_000_000.0, 3) * 1000 + 100.4, 2), item.Total);
        }

        // Владелец (2026-08-24): смена режима сетки в уже добавленной строке
        // ОБЯЗАНА пересчитывать сумму импоста — она считается от расчётной
        // ширины, которая зависит от режима (ББ60: W+2 → 602; ББ70: W−2 → 598).
        [Fact]
        public void AnwisSizeMode_Change_RecalculatesImpostSurcharge()
        {
            var item = new OrderItem { Name = "Anwis" };
            item.ШиринаВвод = 600;
            item.ВысотаВвод = 1500;
            Assert.Equal(AnwisSizeMode.Брусбокс60, item.AnwisSizeMode);
            Assert.Equal(602, item.Width, 0);
            Assert.True(item.HasImpost);
            Assert.Equal(120.4, item.ImpostSurchargeTotal, 2); // 200 × 0,602

            // Меняем режим через публичный сеттер (как клик по меню режима в строке).
            item.AnwisSizeMode = AnwisSizeMode.Брусбокс70;
            Assert.Equal(598, item.Width, 0);
            Assert.True(item.HasImpost);
            Assert.Equal(119.6, item.ImpostSurchargeTotal, 2); // 200 × 0,598
        }


        [Fact]
        public void Total_NoImpost_WhenCriteriaNotMet()
        {
            var item = new OrderItem { Name = "На навесах", Width = 400, Height = 800, Price = 1800, Quantity = 1 };
            Assert.False(item.HasImpost);
            Assert.Equal(Math.Round(400.0 * 800 / 1_000_000.0 * 1800, 2), item.Total);
        }

        [Fact]
        public void Total_NoImpost_ForDvernayaSetka()
        {
            var item = new OrderItem { Name = "Дверная сетка", Width = 600, Height = 1600, Price = 3000, Quantity = 1 };
            Assert.False(item.HasImpost);
            Assert.Equal(Math.Round(600.0 * 1600 / 1_000_000.0 * 3000, 2), item.Total);
        }

        [Fact]
        public void TotalWithDeduction_IncludesImpost()
        {
            // Импост — часть Total, поэтому TotalWithDeduction (грид/КП/итоги) включает его.
            var item = new OrderItem { Name = "На навесах", Width = 600, Height = 800, Price = 1800, Quantity = 1 };
            Assert.Equal(item.Total, item.TotalWithDeduction); // монтаж mode 0, adjustment 0
        }

        [Fact]
        public void DisplayName_AppendsSuffix_WhenHasImpost()
        {
            // «На навесах» — identity, поэтому 600×800 по вводу = импост.
            var item = new OrderItem { Name = "На навесах", Width = 600, Height = 800 };
            Assert.Equal("На навесах (Импост)", item.DisplayName);
        }

        [Fact]
        public void DisplayName_AppendsBothSuffixes_WhenAnticatAndImpost()
        {
            var item = new OrderItem { Name = "На навесах", Width = 600, Height = 800, IsAnticat = true };
            Assert.Equal("На навесах (Антикошка) (Импост)", item.DisplayName);
        }

        [Fact]
        public void HasImpost_FiresPropertyChanged_WhenWidthChanges()
        {
            var item = new OrderItem { Name = "На навесах", Width = 400, Height = 800 };
            bool fired = false;
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(OrderItem.HasImpost))
                    fired = true;
            };
            item.Width = 600;
            Assert.True(fired);
        }

        [Fact]
        public void Clone_PreservesHasImpost()
        {
            var original = new OrderItem { Name = "Anwis", Width = 600, Height = 800 };
            var clone = original.Clone();
            Assert.True(clone.HasImpost);
            Assert.Equal("Anwis (Импост)", clone.DisplayName);
        }

        // ─── Impost/Anti-cat badges (v3.48.0) ────────────────

        [Fact]
        public void ImpostSurchargeTotal_CalculatedFromCalcWidth_AndQuantity()
        {
            // Нет-импост товары вне сеток: надбавка 0.
            var none = new OrderItem { Name = "На навесах", Width = 400, Height = 800, Price = 1000, Quantity = 2 };
            Assert.False(none.HasImpost);
            Assert.Equal(0, none.ImpostSurchargeTotal);

            // 430×1510 («На навесах», identity) — сумма от расчётной ширины (0,43 м.п.), ×2 шт.
            var item = new OrderItem { Name = "На навесах", Width = 430, Height = 1510, Price = 1000, Quantity = 2 };
            Assert.True(item.HasImpost);
            Assert.Equal(Math.Round(200 * 0.43 * 2, 2), item.ImpostSurchargeTotal);

            // Дверная сетка исключена.
            var door = new OrderItem { Name = "Дверная сетка", Width = 600, Height = 1600, Quantity = 1 };
            Assert.Equal(0, door.ImpostSurchargeTotal);
        }

        [Fact]
        public void ImpostToolTip_ContainsWidthMmSumAndConfirmed()
        {
            // «На навесах» — identity: 500×800 по вводу → импост, 0,500 м.п.
            var item = new OrderItem { Name = "На навесах", Width = 500, Height = 800, Price = 1000, Quantity = 1 };
            Assert.True(item.HasImpost);
            var tip = item.ImpostToolTip;
            Assert.Contains("Импост:", tip);
            Assert.Contains("500 мм", tip);
            Assert.Contains("0,500 м.п.", tip);
            Assert.Contains("100,00", tip); // 200 × 0,500 м.п. = 100 ₽
            Assert.Contains("учтён", tip);
        }

        [Fact]
        public void ImpostToolTip_Empty_WhenNoImpost()
        {
            Assert.Equal("", new OrderItem { Name = "Anwis", Width = 400, Height = 800 }.ImpostToolTip);
            Assert.Equal("", new OrderItem { Name = "Дверная сетка", Width = 600, Height = 1600 }.ImpostToolTip);
        }

        [Fact]
        public void AnticatSurchargeTotal_CalculatedFromArea()
        {
            // 600×800 = 0,48 м² × 2000 ₽/м² = 960 ₽.
            var item = new OrderItem { Name = "Anwis", Width = 600, Height = 800, IsAnticat = true, Quantity = 1 };
            Assert.Equal(960, item.AnticatSurchargeTotal, 2);

            var off = new OrderItem { Name = "Anwis", Width = 600, Height = 800, Quantity = 1 };
            Assert.Equal(0, off.AnticatSurchargeTotal);
        }

        [Fact]
        public void AnticatToolTip_ContainsAreaSumAndConfirmed()
        {
            var item = new OrderItem { Name = "Anwis", Width = 600, Height = 800, IsAnticat = true, Price = 1000, Quantity = 1 };
            var tip = item.AnticatToolTip;
            Assert.Contains("Антикошка:", tip);
            Assert.Contains("м²", tip);
            Assert.Contains("960,00", tip); // 2000 × 0,48 м² = 960
            Assert.Contains("учтена", tip);
        }

        [Fact]
        public void AnticatToolTip_Empty_WhenNotAnticat()
        {
            Assert.Equal("", new OrderItem { Name = "Anwis", Width = 600, Height = 800 }.AnticatToolTip);
        }

        [Fact]
        public void ImpostToolTip_FiresPropertyChanged_WhenWidthChanges()
        {
            var item = new OrderItem { Name = "Anwis", Width = 400, Height = 800 };
            bool fired = false;
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(OrderItem.ImpostToolTip))
                    fired = true;
            };
            item.Width = 600;
            Assert.True(fired);
        }

        [Fact]
        public void AnticatToolTip_FiresPropertyChanged_WhenToggled()
        {
            var item = new OrderItem { Name = "Anwis", Width = 600, Height = 800 };
            bool fired = false;
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(OrderItem.AnticatToolTip))
                    fired = true;
            };
            item.IsAnticat = true;
            Assert.True(fired);
        }

        // ─── Badge tooltips — culture & real entry points (v3.48.0) ──

        [Fact]
        public void ImpostToolTip_AlwaysUsesRussianDecimalSeparator()
        {
            // ru-RU formatting is forced regardless of the thread culture:
            // meters and quantity use a comma, not a dot.
            // «На навесах» — identity sizing, поэтому 0,430 ровно.
            var item = new OrderItem { Name = "На навесах", Width = 430, Height = 1510, Quantity = 2.5 };
            Assert.True(item.HasImpost);
            var tip = item.ImpostToolTip;
            Assert.Contains("0,430 м.п.", tip);
            Assert.Contains("× 2,5 =", tip);
            // «м.п.» содержит буквенные точки-аббревиатуры — их не трогаем;
            // главное, что погонные метры и кол-во с запятой, а не с точкой.
        }

        // DataGrid-editing input point is ШиринаВвод (raw user input).
        // «На навесах» has identity sizing — the stored Width equals the entered
        // raw value, so the tooltip shows exactly 0,510. Crossing the 500 mm
        // threshold must flip the «ИМ» badge and recalc immediately.
        [Fact]
        public void ImpostBadge_LiveFlips_WhenEditingWidthInGrid()
        {
            var item = new OrderItem { Name = "На навесах", Width = 400, Height = 800, Price = 1000, Quantity = 1 };
            var changes = new List<string>();
            item.PropertyChanged += (s, e) => { if (e.PropertyName is nameof(OrderItem.HasImpost) or nameof(OrderItem.ImpostToolTip)) changes.Add(e.PropertyName!); };

            item.ШиринаВвод = 490;
            Assert.False(item.HasImpost);

            item.ШиринаВвод = 510;
            Assert.True(item.HasImpost);
            Assert.Contains("0,510 м.п.", item.ImpostToolTip);

            item.ШиринаВвод = 480;
            Assert.False(item.HasImpost);
            Assert.Contains(nameof(OrderItem.HasImpost), changes);
            Assert.Contains(nameof(OrderItem.ImpostToolTip), changes);
        }

            [Fact]
            public void ImpostBadge_SurvivesDtoRoundTrip()
            {
                // Сохранение/загрузка заказа (ToOrderItemData → DTO → новый OrderItem):
                // HasImpost производная от размеров, поэтому бейдж «ИМ» восстанавливается.
                var item = new OrderItem { Name = "Anwis", Width = 600, Height = 800, Price = 1800 };
                Assert.True(item.HasImpost);

                var dto = item.ToOrderItemData();
                var restored = new OrderItem
                {
                    Name = dto.Name,
                    Width = dto.Width,
                    Height = dto.Height,
                    Quantity = dto.Quantity,
                    Price = dto.Price
                };
                Assert.True(restored.HasImpost);
                Assert.Equal(item.ImpostSurchargeTotal, restored.ImpostSurchargeTotal, 2);
            }

            // ─── Badge popup wiring (v3.48.0) ─────────────────
            // Реальный entry point: клик по бейджу «ИМ»/«АК» открывает Popup
            // с деталями. Проверяем через internal ToggleBadgeInfoPopup (логику
            // без синтеза MouseButtonEventArgs — у него нет публичного
            // конструктора). Headless-ограничение WPF: Popup.IsOpen не выставляется
            // в true без Application root (popup-презентация), поэтому стабильно
            // проверяем только FindLogicalPopup (поиск в логическом дереве)
            // и no-op для null. WPF Popup требует STA.
            [Fact]
            public void BadgePopup_FoundByLogicalTree_ByToggleHelper()
            {
                WpfTestHelper.RunOnSta(() =>
                {
                    var popup = new Popup { StaysOpen = false };
                    var grid = new Grid();
                    grid.Children.Add(new TextBlock { Text = "ИМ" });
                    grid.Children.Add(popup);
                    var badge = new Border { Child = grid, Tag = "ИМ" };

                    // Логический обход находит Popup в дереве бейджа.
                    OrderItemsControl.ToggleBadgeInfoPopup(badge);

                    // PlacementTarget выставляется только если найден Popup;
                    // если обход не нашёл — остался бы null.
                    Assert.Same(badge, popup.PlacementTarget);

                    // null — no-op (без исключений).
                    OrderItemsControl.ToggleBadgeInfoPopup(null);
                });
            }

            // ─── Clone with AnwisSizeMode test ───────────────

        [Fact]
        public void GetDefaultInstallationDeduction_DvernayaSetka_Returns600()
        {
            // v3.46.1: signed convention — Дверная сетка default is −600.
            Assert.Equal(-600, OrderItem.GetDefaultInstallationDeduction("Дверная сетка"));
            Assert.Equal(-600, OrderItem.GetDefaultInstallationSurcharge("Дверная сетка"));
        }

        [Fact]
        public void GetDefaultInstallationDeduction_Anwis_ReturnsFallbackNegative500()
        {
            // v3.46.1: signed convention — defaults are negative (subtract).
            Assert.Equal(-500, OrderItem.GetDefaultInstallationDeduction("Anwis"));
            Assert.Equal(-500, OrderItem.GetDefaultInstallationSurcharge("На навесах"));
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

        // ─── Slope ↔ OrderItem PropertyChanged cascade (v3.43.3) ──────
        //
        // Контракт: правка Quantity/Price в панели откоса каскадно
        // обновляет OrderItem.Total через
        //   SlopeMaterial.Quantity setter
        //   → SlopeMaterial.Sum PropertyChanged
        //   → SlopeCalculation.OnChildMaterialChanged
        //   → SlopeCalculation.TotalMaterials PropertyChanged
        //   → OrderItem.OnSlopeDataPropertyChanged (подписан в SlopeData setter)
        //   → OrderItem.Recalculate() → _total обновлён
        // Без этого контракта юзер правит «Старый: 2→3», а Total в DataGrid
        // и печатном КП остаётся прежним.

        [Fact]
        public void SlopeDataChildMaterialQuantityChange_CascadeRefreshesOrderItemTotal()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1,
                1200, 750, 350, 135, 135, 250, 450, 600);
            double materialsAtStart = calc.TotalMaterials;

            var item = new OrderItem
            {
                Name = "Откос",
                Width = 1000,
                Height = 1000,
                Quantity = 1,
                Price = materialsAtStart,
                SlopeData = calc,
            };
            // v3.43.5: DistributedSharedSum заполняется только в
            // RecalculateSealantAndTape; вызываем её, чтобы total
            // учитывал долю герметика/скотча до ручной правки.
            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item });
            double totalBefore = item.Total;

            // Симулируем правку Sandwich.Quantity в панели: юзер поставил Quantity=99.5
            // вручную. IsQuantityOverridden=true ставится из LostFocus handler'а
            // панели, а setter Quantity → cascade → OrderItem.Total обновляется.
            calc.Sandwich.IsQuantityOverridden = true;
            calc.Sandwich.Quantity = 99.5;

            // v3.43.8: Старт — 3 стороны, F-планка — 3 стороны +100 мм.
            double newMaterials = 99.5 * 1200 + 1 * 750
                + Math.Ceiling(1 / 4.0) * 350
                + Math.Ceiling(1 / 3.0) * 135
                + SlopeCalculatorService.OptimizeStrips(1000, 1000) * 135
                + SlopeCalculatorService.OptimizeStrips(1100, 1100) * 250
                + SlopeCalculatorService.GetPenoplexSheets(1.0) * 450;
            double expectedTotal = Math.Round(newMaterials, 2);

            Assert.NotEqual(totalBefore, item.Total);
            Assert.Equal(expectedTotal, item.Total, 2);
        }

        [Fact]
        public void Clone_SlopeData_IsDeepCopy_NotSharedReference()
        {
            // v3.43.3 (review fix): клон должен иметь СВОЙ SlopeCalculation,
            // не шарить инстанс с оригиналом. Иначе правка Sandwich.Quantity
            // у оригинала тоже меняла материалы клона.
            var src = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            var original = new OrderItem { Name = "Откос", SlopeData = src };

            var clone = original.Clone();

            Assert.NotNull(clone.SlopeData);
            Assert.NotSame(original.SlopeData, clone.SlopeData);
            Assert.NotSame(original.SlopeData.Sandwich, clone.SlopeData!.Sandwich);

            // Меняем оригинал — клон не должен реагировать.
            original.SlopeData!.Sandwich.IsQuantityOverridden = true;
            original.SlopeData.Sandwich.Quantity = 42.0;
            Assert.NotEqual(42.0, clone.SlopeData.Sandwich.Quantity);
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
