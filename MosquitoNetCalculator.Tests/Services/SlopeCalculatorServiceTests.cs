using System;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class SlopeCalculatorServiceTests
    {
        // ────────────────────────────────────────────────────────────
        // OptimizeStripsForPerimeter — критичный баг-фикс v3.43.3:
        // W=2150, H=1500 (7300мм по 4 сторонам) должен давать 3 полосы по 3 м,
        // а не 2 (как прежний 3-сторонний алгоритм [W,H,H]).
        // ────────────────────────────────────────────────────────────

        [Fact]
        public void OptimizeStripsForPerimeter_2150x1500_ReturnsThreeStrips()
        {
            // Пользовательский репорт: «для 2150×1500 надо 3 полосы, программа считает 2».
            // 4 стороны: 2×W + 2×H = 2×2150 + 2×1500 = 7300мм. 7300 / 3м = 2.43 → ceil = 3.
            // Greedy: [2150,2150,1500,1500] sorted desc.
            //   strip1 (2150)→off[850];  2150: 850<2150→strip2 (2150)→off[850,850];
            //   1500: 850<1500→strip3 (1500)→off[850,850,1500]; 1500: 1500 fit→off[850,850].
            // = 3 полосы ✓
            int strips = SlopeCalculatorService.OptimizeStripsForPerimeter(2150, 1500);
            Assert.Equal(3, strips);
        }

        [Fact]
        public void OptimizeStripsForPerimeter_700x1500_ReturnsTwoStrips()
        {
            // [1500,1500,700,700]: strip1(1500)→off[1500]; 1500 fit→off[0];
            // 700 нет кандидата→strip2(700)→off[2300]; 700 fit→off[2300→0]. = 2 полосы.
            int strips = SlopeCalculatorService.OptimizeStripsForPerimeter(700, 1500);
            Assert.Equal(2, strips);
        }

        [Fact]
        public void OptimizeStripsForPerimeter_1500x700_ReturnsTwoStrips()
        {
            // Симметричный предыдущему: [1500,1500,700,700] sorted.
            int strips = SlopeCalculatorService.OptimizeStripsForPerimeter(1500, 700);
            Assert.Equal(2, strips);
        }

        [Fact]
        public void OptimizeStripsForPerimeter_1000x2100_ReturnsThreeStrips()
        {
            // [2100,2100,1000,1000]: strip1(2100)→off[900]; 2100: 900<2100→strip2(2100)→off[900,900];
            // 1000: 900<1000→strip3(1000)→off[900,900,2000]; 1000: 2000 fit→off[900,900]. = 3 полосы.
            int strips = SlopeCalculatorService.OptimizeStripsForPerimeter(1000, 2100);
            Assert.Equal(3, strips);
        }

        [Fact]
        public void OptimizeStripsForPerimeter_1500x1500_ReturnsTwoStrips()
        {
            // 4×1500=6000мм. Greedy: strip1(1500)→off[1500]; 1500 fit→off[0];
            // strip2(1500)→off[1500]; 1500 fit→off[0]. = 2 полосы (оптимально для 6000мм).
            int strips = SlopeCalculatorService.OptimizeStripsForPerimeter(1500, 1500);
            Assert.Equal(2, strips);
        }

        [Fact]
        public void OptimizeStripsForPerimeter_ZeroDimensions_ReturnsZero()
        {
            int strips = SlopeCalculatorService.OptimizeStripsForPerimeter(0, 0);
            Assert.Equal(0, strips);
        }

        [Fact]
        public void OptimizeStripsForPerimeter_LongWidth_GreedyWithMultiStrip()
        {
            // Edge case: piece > 3000мм (например, французский балкон 3500мм шириной).
            // v3.43.5 (bugfix #8): остатки длинных кусков (>3000) теперь упаковываются
            // совместно, поэтому [3500, 3500] даёт  полосы вместо 4:
            //   3500 → 1 полная + остаток 500; 3500 → 1 полная + остаток 500;
            //   остатки [500, 500] → 1 полоса. Итого 3.
            int strips = SlopeCalculatorService.OptimizeStripsForPerimeter(3500, 0);
            Assert.Equal(3, strips);
        }

        // ────────────────────────────────────────────────────────────
        // OptimizeStrips direct — regression tests for bugfix #8
        // (pieces longer than 3000 mm).
        // ────────────────────────────────────────────────────────────

        [Fact]
        public void OptimizeStrips_TwoLongPieces_SharesRemainders()
        {
            // [3500, 3500]: old algorithm gave 4 strips, new gives 3.
            // Note: must use explicit array to avoid the (int, int) 3-sided overload.
            int strips = SlopeCalculatorService.OptimizeStrips(new int[] { 3500, 3500 });
            Assert.Equal(3, strips);
        }

        [Fact]
        public void OptimizeStrips_LongPieceWithShortFiller_PacksRemainder()
        {
            // [3500, 2500]: 3500 → 1 full strip + 500 remainder;
            // 2500 → 1 strip with 500 offcut; the 500 remainder fits that offcut.
            // Total 2 strips.
            int strips = SlopeCalculatorService.OptimizeStrips(new int[] { 3500, 2500 });
            Assert.Equal(2, strips);
        }

        [Fact]
        public void OptimizeStrips_ExactMultiple_NoRemainder()
        {
            // [6000, 1000]: 6000 → 2 full strips, no remainder; 1000 → 1 strip. Total 3.
            int strips = SlopeCalculatorService.OptimizeStrips(new int[] { 6000, 1000 });
            Assert.Equal(3, strips);
        }

        [Fact]
        public void OptimizeStrips_ExactlyStripLength_OneStrip()
        {
            int strips = SlopeCalculatorService.OptimizeStrips(new int[] { 3000 });
            Assert.Equal(1, strips);
        }

        [Fact]
        public void OptimizeStrips_MixedLongAndShort_OptimalPacking()
        {
            // [4500, 2500]: 4500 → 1 full strip + 1500 remainder;
            // 2500 → 1 strip with 500 offcut; 1500 remainder needs its own strip.
            // Total 3 strips.
            int strips = SlopeCalculatorService.OptimizeStrips(new int[] { 4500, 2500 });
            Assert.Equal(3, strips);
        }

        [Fact]
        public void OptimizeStrips_EmptyInput_ReturnsZero()
        {
            int strips = SlopeCalculatorService.OptimizeStrips();
            Assert.Equal(0, strips);
        }

        [Fact]
        public void OptimizeStrips_AllNonPositive_ReturnsZero()
        {
            int strips = SlopeCalculatorService.OptimizeStrips(new int[] { 0, -100, 0 });
            Assert.Equal(0, strips);
        }

        [Fact]
        public void OptimizeStrips_ThreeLongPieces_RemaindersPackTogether()
        {
            // [3500, 3500, 3500]: 3 full strips + 3×500 remainders.
            // All three 500 remainders pack into one additional strip.
            // Total 4 strips (old algorithm would give 6).
            int strips = SlopeCalculatorService.OptimizeStrips(new int[] { 3500, 3500, 3500 });
            Assert.Equal(4, strips);
        }

        [Fact]
        public void OptimizeStrips_LongPieceRemainderFitsShortPiece()
        {
            // [3500, 2400, 500]: 3500 → 1 full strip + 500 remainder;
            // 2400 → 1 packing strip + 600 offcut (500 fits there);
            // 500 remainder needs its own strip. Total 3 strips.
            int strips = SlopeCalculatorService.OptimizeStrips(new int[] { 3500, 2400, 500 });
            Assert.Equal(3, strips);
        }

        [Fact]
        public void OptimizeStripsForPerimeter_LongSide_HandlesCorrectly()
        {
            // 4 стороны: [2700, 2700, 2700, 2700]. Greedy: первый 2700 → strip1, off=300
            // (ни один остаток 300 < 2700 не помещает следующий кусок). Каждый следующий
            // 2700 — новая полоса, потому что off=300 не может вместить 2700.
            // 4 × 2700 = 10800 мм / 3000 = 3.6 → ceil = 4 полосы (оптимально).
            int strips = SlopeCalculatorService.OptimizeStripsForPerimeter(2700, 2700);
            Assert.Equal(4, strips);
        }

        [Fact]
        public void OptimizeStripsForPerimeter_AsymmetricLargePiece_RequiresStripAndOffcut()
        {
            // W=2700, H=700: [2700, 2700, 700, 700].
            //   strip1 (2700)→off[300]; 2700: 300<2700→strip2 (2700)→off[300,300];
            //   700: 300<700→strip3 (700)→off[300,300,2300]; 700: 2300 fit→off[300,300,1600].
            // = 3 полосы
            int strips = SlopeCalculatorService.OptimizeStripsForPerimeter(2700, 700);
            Assert.Equal(3, strips);
        }

        [Fact]
        public void OptimizeStrips_SixThousand_ExactMultiple_TwoStrips()
        {
            // [6000]: exactly 2×3000, no remainder.
            int strips = SlopeCalculatorService.OptimizeStrips(new int[] { 6000 });
            Assert.Equal(2, strips);
        }

        [Fact]
        public void OptimizeStrips_NineThousand_ExactMultiple_ThreeStrips()
        {
            // [9000]: exactly 3×3000, no remainder.
            int strips = SlopeCalculatorService.OptimizeStrips(new int[] { 9000 });
            Assert.Equal(3, strips);
        }

        [Fact]
        public void OptimizeStrips_FourLongPieces_RemaindersPackEfficiently()
        {
            // [3500, 3500, 3500, 3500]: 4 full strips + 4×500 remainders.
            // 500×4 = 2000mm → fits in one additional strip. Total 5.
            int strips = SlopeCalculatorService.OptimizeStrips(new int[] { 3500, 3500, 3500, 3500 });
            Assert.Equal(5, strips);
        }

        [Fact]
        public void OptimizeStrips_TwoHalves_FitOneStrip()
        {
            // [1500, 1500]: two halves fit exactly into one strip.
            int strips = SlopeCalculatorService.OptimizeStrips(new int[] { 1500, 1500 });
            Assert.Equal(1, strips);
        }

        [Fact]
        public void OptimizeStrips_LargeRemaindersPackTogether()
        {
            // [4500, 4500]: each gives 1 full strip + 1500 remainder.
            // 1500 + 1500 = 3000 → fits in one strip. Total 3.
            int strips = SlopeCalculatorService.OptimizeStrips(new int[] { 4500, 4500 });
            Assert.Equal(3, strips);
        }

        [Fact]
        public void OptimizeStrips_NullInput_ReturnsZero()
        {
            int strips = SlopeCalculatorService.OptimizeStrips(null!);
            Assert.Equal(0, strips);
        }

        // ────────────────────────────────────────────────────────────
        // Backward compat: OptimizeStrips(W, H) — 3 стороны для Сэндвича.
        // ────────────────────────────────────────────────────────────

        [Fact]
        public void OptimizeStrips_Legacy3Sided_StillWorks()
        {
            // Прежняя 2-аргументная сигнатура: 3 стороны [W, H, H] — для Сэндвича (P3 формула).
            int strips = SlopeCalculatorService.OptimizeStrips(700, 1500);
            // Спецификация: для W=700, H=1500 — 2 полосы по 3-стороннему алгоритму.
            Assert.Equal(2, strips);
        }

        // ────────────────────────────────────────────────────────────
        // UpdateInPlace — v3.43.3 — сохранение overrides при правке W/H/D.
        // ────────────────────────────────────────────────────────────

        [Fact]
        public void UpdateInPlace_PreservesUserOverride_OnSandwichQuantity()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1,
                1200, 750, 350, 135, 135, 250, 450, 600);

            double autoQty = calc.Sandwich.Quantity;
            // Юзер вручную меняет кол-во сэндвича
            calc.Sandwich.IsQuantityOverridden = true;
            calc.Sandwich.Quantity = 99.5;

            // UpdateInPlace с изменённой шириной (W=2000 вместо 1000 — куда хотим
            // пересчитать 4-сторонний раскрой Старта).
            SlopeCalculatorService.UpdateInPlace(calc, 2000, 1000, 0.15, 1, 1,
                1200, 750, 350, 135, 135, 250, 450, 600);

            // Sandwich.Quantity должен остаться 99.5 (override), а не пересчитаться
            Assert.Equal(99.5, calc.Sandwich.Quantity, 4);
            // Другие позиции без override — пересчитались
            Assert.NotEqual(autoQty, calc.Sandwich.Quantity);
            Assert.Equal(1200, calc.Sandwich.Price, 2); // цена переставилась
            // 4-сторонний раскрой для W=2000, H=1000: [2000,2000,1000,1000] greedy →
            // strip1(2000)→off[1000]; 2000: 1000<2000 → strip2(2000)→off[1000,1000];
            // 1000: fit→off[1000]; 1000: fit→off[].  = 2 полосы.
            Assert.Equal(2, calc.StartProfile.Quantity);
            Assert.Equal(135, calc.StartProfile.Price, 2); // startPrice=135 (5-й параметр выше)
        }

        [Fact]
        public void UpdateInPlace_RecalculatesNonOverriddenFields_OnWidthChange()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            calc.StartProfile.Quantity = 5; // юзер переопределил Start
            calc.StartProfile.IsQuantityOverridden = true;

            double oldFoam = calc.Foam.Quantity;

            SlopeCalculatorService.UpdateInPlace(calc, 2000, 1000, 0.15, 1, 1);

            // Foam (без override) пересчитался (но он всё равно = 1 на окно)
            Assert.Equal(oldFoam, calc.Foam.Quantity);
            // Start (с override) сохранил 5
            Assert.Equal(5, calc.StartProfile.Quantity);
        }

        [Fact]
        public void UpdateInPlace_StartQuantity_UsesThreeSidedPerimeter()
        {
            // v3.43.8: Старт считает 3 стороны (верх + 2 бока, без низа).
            var calc = SlopeCalculatorService.Calculate(2150, 1500, 0.15, 1, 1);
            // Calculate → Start = 2 полосы (OptimizeStrips W=2150 H=1500: 3 стороны)
            Assert.Equal(2, calc.StartProfile.Quantity);
            // UpdateInPlace сохраняет то же значение.
            SlopeCalculatorService.UpdateInPlace(calc, 2160, 1500, 0.15, 1, 1);
            Assert.Equal(2, calc.StartProfile.Quantity);
        }

        [Fact]
        public void UpdateInPlace_ResetOverrides_RestoresAutoValues()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            double originalSandwich = calc.Sandwich.Quantity;

            // override + change
            calc.Sandwich.IsQuantityOverridden = true;
            calc.Sandwich.Quantity = 99.5;

            // reset and re-calc
            calc.ResetOverrides();
            SlopeCalculatorService.UpdateInPlace(calc, 1000, 1000, 0.15, 1, 1);

            Assert.Equal(originalSandwich, calc.Sandwich.Quantity, 4);
        }

        // ────────────────────────────────────────────────────────────
        // RecalculateSealantAndTape — shared materials distribution (bugfix #1).
        // ────────────────────────────────────────────────────────────

        [Fact]
        public void RecalculateSealantAndTape_ThreeWindows_DistributesSharedCost()
        {
            // 3 different slope configurations, each Quantity=1.
            // Shared materials should be counted exactly once for the whole order,
            // then distributed proportionally by WindowCount.
            var calc1 = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            var calc2 = SlopeCalculatorService.Calculate(1200, 1200, 0.15, 1, 1);
            var calc3 = SlopeCalculatorService.Calculate(1400, 1400, 0.15, 1, 1);

            var item1 = new OrderItem { Name = "Откос", SlopeData = calc1, Quantity = 1 };
            var item2 = new OrderItem { Name = "Откос", SlopeData = calc2, Quantity = 1 };
            var item3 = new OrderItem { Name = "Откос", SlopeData = calc3, Quantity = 1 };

            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item1, item2, item3 });

            // Sealant quantity = ceil(3 / 4) = 1, Tape quantity = ceil(3 / 3) = 1.
            Assert.Equal(1, calc1.Sealant.Quantity);
            Assert.Equal(1, calc1.Tape.Quantity);
            Assert.Equal(1, calc2.Sealant.Quantity);
            Assert.Equal(1, calc2.Tape.Quantity);
            Assert.Equal(1, calc3.Sealant.Quantity);
            Assert.Equal(1, calc3.Tape.Quantity);

            // DistributedSharedSum should be approximately equal for equal WindowCount.
            // Rounding may leave the last item 1 kopek lower.
            Assert.Equal(calc1.DistributedSharedSum, calc2.DistributedSharedSum, 2);
            Assert.True(Math.Abs(calc2.DistributedSharedSum - calc3.DistributedSharedSum) <= 0.01);

            // Sum of distributed shares equals the true order-level shared cost
            // (one sealant + one tape), not the sum across all calc instances.
            double trueSharedCost = calc1.Sealant.Sum + calc1.Tape.Sum;
            double distributed = calc1.DistributedSharedSum +
                                 calc2.DistributedSharedSum +
                                 calc3.DistributedSharedSum;
            Assert.Equal(trueSharedCost, distributed, 2);
        }

        [Fact]
        public void RecalculateSealantAndTape_UnevenWindowCount_LastTakesRemainder()
        {
            // 2 windows in first item, 1 window in second item.
            var calc1 = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 2, 3);
            var calc2 = SlopeCalculatorService.Calculate(1200, 1200, 0.15, 1, 3);

            var item1 = new OrderItem { Name = "Откос", SlopeData = calc1, Quantity = 2 };
            var item2 = new OrderItem { Name = "Откос", SlopeData = calc2, Quantity = 1 };

            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item1, item2 });

            // Total window count = 3.
            // Sealant = ceil(3/4) = 1, Tape = ceil(3/3) = 1 for each calc.
            Assert.Equal(1, calc1.Sealant.Quantity);
            Assert.Equal(1, calc1.Tape.Quantity);
            Assert.Equal(1, calc2.Sealant.Quantity);
            Assert.Equal(1, calc2.Tape.Quantity);

            // First item gets 2/3 of shared sum, second gets remaining 1/3 + rounding remainder.
            double trueSharedCost = calc1.Sealant.Sum + calc1.Tape.Sum;
            Assert.Equal(trueSharedCost, calc1.DistributedSharedSum + calc2.DistributedSharedSum, 2);
            Assert.True(calc1.DistributedSharedSum > calc2.DistributedSharedSum);
        }

        [Fact]
        public void RecalculateSealantAndTape_SharedSlopeData_DoesNotDoubleCount()
        {
            // Two order items referencing the same SlopeCalculation should not
            // double-count the window count.
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            var item1 = new OrderItem { Name = "Откос", SlopeData = calc, Quantity = 1 };
            var item2 = new OrderItem { Name = "Откос", SlopeData = calc, Quantity = 1 };

            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item1, item2 });

            // Total window count should be 1, not 2.
            Assert.Equal(1, calc.Sealant.Quantity);
            Assert.Equal(1, calc.Tape.Quantity);
        }

        [Fact]
        public void RecalculateSealantAndTape_LaborItemsIgnored()
        {
            // "Работа за откос" items should not affect sealant/tape calculation.
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            var laborItem = new OrderItem { Name = "Работа за откос", SlopeData = calc, Quantity = 1 };
            var materialItem = new OrderItem { Name = "Откос", SlopeData = calc, Quantity = 1 };

            SlopeCalculatorService.RecalculateSealantAndTape(new[] { laborItem, materialItem });

            // Total window count should be 1 (only material item counts).
            Assert.Equal(1, calc.Sealant.Quantity);
            Assert.Equal(1, calc.Tape.Quantity);
        }

        [Fact]
        public void RecalculateSealantAndTape_OrderItemTotal_UsesDistributedSharedSum()
        {
            // Regression: OrderItem.Total for "Откос" must use DistributedSharedSum
            // instead of Sealant.Sum + Tape.Sum to avoid overcharging.
            var calc1 = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            var calc2 = SlopeCalculatorService.Calculate(1200, 1200, 0.15, 1, 1);

            var item1 = new OrderItem { Name = "Откос", SlopeData = calc1, Quantity = 1 };
            var item2 = new OrderItem { Name = "Откос", SlopeData = calc2, Quantity = 1 };

            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item1, item2 });

            double expectedTotal1 = Math.Round(
                (calc1.Sandwich.Sum + calc1.Foam.Sum + calc1.StartProfile.Sum +
                 calc1.FProfile.Sum + calc1.Penoplex.Sum) * item1.Quantity +
                calc1.DistributedSharedSum, 2);

            Assert.Equal(expectedTotal1, item1.Total, 2);
            Assert.Equal(calc1.DistributedSharedSum, item1.SlopeData!.DistributedSharedSum, 2);

            // v3.44.1: DistributedSharedSum must equal the true order-level shared
            // cost (one sealant + one tape), not the sum across both calc instances.
            double trueSharedCost = calc1.Sealant.Sum + calc1.Tape.Sum;
            Assert.Equal(trueSharedCost,
                calc1.DistributedSharedSum + calc2.DistributedSharedSum, 2);
        }

        [Fact]
        public void RecalculateSealantAndTape_SingleItem_GetsFullSharedSum()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            var item = new OrderItem { Name = "Откос", SlopeData = calc, Quantity = 1 };

            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item });

            double expectedShared = calc.Sealant.Sum + calc.Tape.Sum;
            Assert.Equal(expectedShared, calc.DistributedSharedSum, 2);
        }

        [Fact]
        public void RecalculateSealantAndTape_EmptyInput_DoesNotThrow()
        {
            var exception = Record.Exception(() =>
                SlopeCalculatorService.RecalculateSealantAndTape(Array.Empty<OrderItem>()));

            Assert.Null(exception);
        }

        [Fact]
        public void RecalculateSealantAndTape_PreservesUserOverrides()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            calc.Sealant.IsQuantityOverridden = true;
            calc.Sealant.Quantity = 5;
            calc.Tape.IsQuantityOverridden = true;
            calc.Tape.Quantity = 3;

            var item = new OrderItem { Name = "Откос", SlopeData = calc, Quantity = 1 };

            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item });

            Assert.Equal(5, calc.Sealant.Quantity);
            Assert.Equal(3, calc.Tape.Quantity);
        }

        [Fact]
        public void RecalculateSealantAndTape_OnlyLaborItems_DoesNotSetDistributedSharedSum()
        {
            // Only "Работа за откос" items should not create any distribution.
            // Use totalWindowCount != windowCount so the defensive init does not
            // pre-populate DistributedSharedSum.
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 3);
            var laborItem = new OrderItem { Name = "Работа за откос", SlopeData = calc, Quantity = 1 };

            SlopeCalculatorService.RecalculateSealantAndTape(new[] { laborItem });

            Assert.Equal(0, calc.DistributedSharedSum, 2);
        }

        [Fact]
        public void RecalculateSealantAndTape_BoundaryFourWindows_SealantRoundsUp()
        {
            // 4 windows: sealant threshold exactly at 1, tape rounds up to 2.
            var calc1 = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 2, 4);
            var calc2 = SlopeCalculatorService.Calculate(1200, 1200, 0.15, 2, 4);
            var item1 = new OrderItem { Name = "Откос", SlopeData = calc1, Quantity = 1 };
            var item2 = new OrderItem { Name = "Откос", SlopeData = calc2, Quantity = 1 };

            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item1, item2 });

            Assert.Equal(1, calc1.Sealant.Quantity);
            Assert.Equal(2, calc1.Tape.Quantity);
            Assert.Equal(2, calc2.Tape.Quantity);
        }

        [Fact]
        public void RecalculateSealantAndTape_BoundaryThreeWindows_TapeExactlyOne()
        {
            // 3 windows: tape threshold exactly at 1.
            var calc1 = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 3);
            var calc2 = SlopeCalculatorService.Calculate(1200, 1200, 0.15, 2, 3);
            var item1 = new OrderItem { Name = "Откос", SlopeData = calc1, Quantity = 1 };
            var item2 = new OrderItem { Name = "Откос", SlopeData = calc2, Quantity = 1 };

            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item1, item2 });

            Assert.Equal(1, calc1.Tape.Quantity);
            Assert.Equal(1, calc2.Tape.Quantity);
        }

        [Fact]
        public void RecalculateSealantAndTape_Idempotent_SecondCallSameResult()
        {
            var calc1 = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 2);
            var calc2 = SlopeCalculatorService.Calculate(1200, 1200, 0.15, 1, 2);
            var item1 = new OrderItem { Name = "Откос", SlopeData = calc1, Quantity = 1 };
            var item2 = new OrderItem { Name = "Откос", SlopeData = calc2, Quantity = 1 };

            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item1, item2 });
            double first = calc1.DistributedSharedSum;

            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item1, item2 });

            Assert.Equal(first, calc1.DistributedSharedSum, 2);
            // DistributedSharedSum equals the true order-level shared cost
            // (one sealant + one tape), not the sum across all calc instances.
            double trueSharedCost = calc1.Sealant.Sum + calc1.Tape.Sum;
            Assert.Equal(calc1.DistributedSharedSum + calc2.DistributedSharedSum,
                         trueSharedCost, 2);
        }

        [Fact]
        public void RecalculateSealantAndTape_PriceOverrides_Preserved()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            calc.Sealant.Price = 999;
            calc.Tape.Price = 888;
            var item = new OrderItem { Name = "Откос", SlopeData = calc, Quantity = 1 };

            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item });

            Assert.Equal(999, calc.Sealant.Price, 2);
            Assert.Equal(888, calc.Tape.Price, 2);
            Assert.Equal(calc.Sealant.Sum + calc.Tape.Sum, calc.DistributedSharedSum, 2);
        }

        [Fact]
        public void Calculate_DefensiveInit_SetsDistributedSharedSumForSingleSlope()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);

            double expected = calc.Sealant.Sum + calc.Tape.Sum;
            Assert.Equal(expected, calc.DistributedSharedSum, 2);
        }

        // ────────────────────────────────────────────────────────────
        // Start/F-profile economy — v3.44.1
        // ────────────────────────────────────────────────────────────

        [Fact]
        public void RecalculateSealantAndTape_ProfileEconomyDisabled_StartProfileIsPerWindow()
        {
            // Regression: with economy OFF, StartProfile.Quantity must represent
            // the per-window (3-sided) strip count, not the total for all N windows.
            // Otherwise OrderItem.Total multiplies it by WindowCount again.
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 2, 2);
            calc.IsProfileEconomyApplied = false;

            var item = new OrderItem { Name = "Откос", SlopeData = calc, Quantity = 2, IsActive = true };

            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item });

            // Per-window quantities: Start = 1 strip, F-profile = 2 strips
            // (3 sides + 100mm margin: 1100×3 = 3300mm → 2 strips).
            Assert.Equal(1, calc.StartProfile.Quantity);
            Assert.Equal(2, calc.FProfile.Quantity);
        }

        [Fact]
        public void RecalculateSealantAndTape_ProfileEconomyDisabled_OrderItemTotal_NoDoubleCount()
        {
            // Regression: with economy OFF, the OrderItem.Total must not double-count
            // Start/F-profile costs. The "Откос" item total includes only materials
            // (per-window × Quantity) + shared (sealant + tape); labor is a separate
            // "Работа за откос" line item.
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 2, 2);
            calc.IsProfileEconomyApplied = false;

            var item = new OrderItem { Name = "Откос", SlopeData = calc, Quantity = 2, IsActive = true };

            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item });

            double perWindowSum = calc.Sandwich.Sum + calc.Foam.Sum + calc.Penoplex.Sum
                                  + calc.StartProfile.Sum + calc.FProfile.Sum;
            double expectedTotal = Math.Round(perWindowSum * 2 + calc.DistributedSharedSum, 2);

            Assert.Equal(expectedTotal, item.Total, 2);
            // Sanity: StartProfile cost in the total equals 2 strips × 135 (not 4).
            Assert.Equal(2 * 135, calc.StartProfile.Sum * 2, 2);
        }

        [Fact]
        public void RecalculateSealantAndTape_MixedEconomy_OnlyAppliesToOptedInSlopes()
        {
            // Regression: if one slope has economy ON and another OFF, the OFF slope
            // must keep its per-window profile quantities and must NOT pay for the
            // profile shared cost. Profile economy only optimizes among participants.
            var calc1 = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            var calc2 = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            calc1.IsProfileEconomyApplied = true;
            calc2.IsProfileEconomyApplied = false;

            var item1 = new OrderItem { Name = "Откос", SlopeData = calc1, Quantity = 1, IsActive = true };
            var item2 = new OrderItem { Name = "Откос", SlopeData = calc2, Quantity = 1, IsActive = true };

            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item1, item2 });

            // calc2 (economy OFF) should have per-window quantities.
            Assert.Equal(1, calc2.StartProfile.Quantity);
            Assert.Equal(2, calc2.FProfile.Quantity);

            // calc1 (economy ON) participates alone, so it gets per-window quantities too
            // (no cross-window saving with only one participant).
            Assert.Equal(1, calc1.StartProfile.Quantity);
            Assert.Equal(2, calc1.FProfile.Quantity);

            // Profile shared cost should be borne only by calc1; calc2 pays sealant+tape only.
            double sealantTapeShared = calc1.Sealant.Sum + calc1.Tape.Sum;
            double profileSharedSum = calc1.StartProfile.Sum + calc1.FProfile.Sum;

            // calc1 pays its half of sealant+tape plus the full profile shared cost.
            Assert.Equal(sealantTapeShared / 2 + profileSharedSum,
                calc1.DistributedSharedSum, 2);

            Assert.Equal(sealantTapeShared + profileSharedSum,
                calc1.DistributedSharedSum + calc2.DistributedSharedSum, 2);
            Assert.True(calc1.DistributedSharedSum > calc2.DistributedSharedSum);
            // calc2 pays exactly its half of sealant+tape (no profile shared cost).
            Assert.Equal(sealantTapeShared / 2, calc2.DistributedSharedSum, 2);
        }
        [Fact]
        public void RecalculateSealantAndTape_ProfileEconomy_OptimizesAcrossWindows()
        {
            // Two identical windows 1000×1000. Without cross-window optimization
            // each window needs its own Start/F-profile strips. With economy,
            // pieces from both windows are packed together.
            var calc1 = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            var calc2 = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            calc1.IsProfileEconomyApplied = true;
            calc2.IsProfileEconomyApplied = true;

            var item1 = new OrderItem { Name = "Откос", SlopeData = calc1, Quantity = 1, IsActive = true };
            var item2 = new OrderItem { Name = "Откос", SlopeData = calc2, Quantity = 1, IsActive = true };

            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item1, item2 });

            // Start per window (3 sides): [1000,1000,1000] → 1 strip each → 2 strips total without economy.
            // Cross-window: [1000,1000,1000,1000,1000,1000] → 2 strips (3m each). Same here, but logic applied.
            Assert.True(calc1.StartProfile.Quantity > 0);
            Assert.Equal(calc1.StartProfile.Quantity, calc2.StartProfile.Quantity);

            // DistributedSharedSum now includes Start + F-profile shared cost.
            double trueSharedCost = calc1.Sealant.Sum + calc1.Tape.Sum +
                                    calc1.StartProfile.Sum + calc1.FProfile.Sum;
            Assert.Equal(trueSharedCost,
                calc1.DistributedSharedSum + calc2.DistributedSharedSum, 2);

            // Cross-window optimization should not cost more than per-window.
            // For two 1000×1000 windows the totals are equal in this specific case,
            // but the shared cost must be counted exactly once across the order.
            double orderTotalWithEconomy = item1.Total + item2.Total;
            double perWindowMaterials = calc1.Sandwich.Sum + calc1.Foam.Sum + calc1.Penoplex.Sum
                                      + calc2.Sandwich.Sum + calc2.Foam.Sum + calc2.Penoplex.Sum;
            double sharedMaterials = calc1.Sealant.Sum + calc1.Tape.Sum
                                   + calc1.StartProfile.Sum + calc1.FProfile.Sum;
            Assert.Equal(perWindowMaterials + sharedMaterials, orderTotalWithEconomy, 2);
        }

        [Fact]
        public void RecalculateSealantAndTape_ProfileEconomy_Disabled_KeepsPerWindow()
        {
            var calc1 = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            var calc2 = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            calc1.IsProfileEconomyApplied = false;
            calc2.IsProfileEconomyApplied = false;

            var item1 = new OrderItem { Name = "Откос", SlopeData = calc1, Quantity = 1, IsActive = true };
            var item2 = new OrderItem { Name = "Откос", SlopeData = calc2, Quantity = 1, IsActive = true };

            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item1, item2 });

            // Start/F-profile should remain per-window quantities.
            Assert.Equal(1, calc1.StartProfile.Quantity);
            Assert.Equal(1, calc2.StartProfile.Quantity);

            // DistributedSharedSum should only include sealant + tape.
            double trueSharedCost = calc1.Sealant.Sum + calc1.Tape.Sum;
            Assert.Equal(trueSharedCost,
                calc1.DistributedSharedSum + calc2.DistributedSharedSum, 2);
        }

        [Fact]
        public void RecalculateSealantAndTape_IsActiveFalse_IgnoredInOptimization()
        {
            var calc1 = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            var calc2 = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            calc1.IsProfileEconomyApplied = true;
            calc2.IsProfileEconomyApplied = true;

            var item1 = new OrderItem { Name = "Откос", SlopeData = calc1, Quantity = 1, IsActive = true };
            var item2 = new OrderItem { Name = "Откос", SlopeData = calc2, Quantity = 1, IsActive = false };

            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item1, item2 });

            // Only the active item should participate in shared calculations.
            Assert.Equal(1, calc1.Sealant.Quantity);
            Assert.Equal(1, calc1.Tape.Quantity);
            Assert.Equal(0, calc2.DistributedSharedSum);
        }

        [Fact]
        public void RecalculateSealantAndTape_NoDoubleCounting_ForMultipleItems()
        {
            // Regression: previously totalSharedSum summed Sealant.Sum + Tape.Sum
            // across every calc, multiplying the true order cost by the number of items.
            var calc1 = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            var calc2 = SlopeCalculatorService.Calculate(1200, 1200, 0.15, 1, 1);
            var calc3 = SlopeCalculatorService.Calculate(1400, 1400, 0.15, 1, 1);

            var item1 = new OrderItem { Name = "Откос", SlopeData = calc1, Quantity = 1, IsActive = true };
            var item2 = new OrderItem { Name = "Откос", SlopeData = calc2, Quantity = 1, IsActive = true };
            var item3 = new OrderItem { Name = "Откос", SlopeData = calc3, Quantity = 1, IsActive = true };

            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item1, item2, item3 });

            double trueSharedCost = calc1.Sealant.Sum + calc1.Tape.Sum; // one sealant + one tape
            double distributed = calc1.DistributedSharedSum + calc2.DistributedSharedSum + calc3.DistributedSharedSum;
            Assert.Equal(trueSharedCost, distributed, 2);
        }

        [Fact]
        public void Calculate_NoDefensiveInit_LeavesDistributedSharedSumZeroForMultiSlope()
        {
            // windowCount=1, totalWindowCount=3 simulates a multi-slope order before
            // RecalculateSealantAndTape has run.
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 3);

            Assert.Equal(0, calc.DistributedSharedSum, 2);
        }

        // ────────────────────────────────────────────────────────────
        // SlopeCalculation каскад PropertyChanged.
        // ────────────────────────────────────────────────────────────

        [Fact]
        public void SlopeCalculation_ChildMaterialQuantityChange_FiresTotalMaterialsChanged()
        {
            var calc = new SlopeCalculation { WidthMm = 1000, HeightMm = 1000, DepthM = 0.15 };

            bool totalFired = false;
            calc.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SlopeCalculation.TotalMaterials))
                    totalFired = true;
            };

            calc.Sandwich.Quantity = 5.0;
            Assert.True(totalFired);
        }

        [Fact]
        public void SlopeCalculation_ChildMaterialPriceChange_FiresTotalMaterialsChanged()
        {
            var calc = new SlopeCalculation { WidthMm = 1000, HeightMm = 1000, DepthM = 0.15 };

            bool totalFired = false;
            calc.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SlopeCalculation.TotalMaterials))
                    totalFired = true;
            };

            calc.Sandwich.Price = 1500;
            Assert.True(totalFired);
        }

        [Fact]
        public void SlopeCalculation_ResetOverrides_ClearsAllFlags()
        {
            var calc = new SlopeCalculation();
            calc.Sandwich.IsQuantityOverridden = true;
            calc.Foam.IsQuantityOverridden = true;
            calc.Sealant.IsQuantityOverridden = true;
            calc.Tape.IsQuantityOverridden = true;
            calc.StartProfile.IsQuantityOverridden = true;
            calc.FProfile.IsQuantityOverridden = true;
            calc.Penoplex.IsQuantityOverridden = true;
            calc.Laminatina.IsQuantityOverridden = true;
            calc.Labor.IsQuantityOverridden = true;
            calc.LaminatinaLabor.IsQuantityOverridden = true;

            calc.ResetOverrides();

            Assert.False(calc.Sandwich.IsQuantityOverridden);
            Assert.False(calc.Foam.IsQuantityOverridden);
            Assert.False(calc.Sealant.IsQuantityOverridden);
            Assert.False(calc.Tape.IsQuantityOverridden);
            Assert.False(calc.StartProfile.IsQuantityOverridden);
            Assert.False(calc.FProfile.IsQuantityOverridden);
            Assert.False(calc.Penoplex.IsQuantityOverridden);
            Assert.False(calc.Laminatina.IsQuantityOverridden);
            Assert.False(calc.Labor.IsQuantityOverridden);
            Assert.False(calc.LaminatinaLabor.IsQuantityOverridden);
        }

        // ────────────────────────────────────────────────────────────
        // Laminatina — v3.44.0
        // ────────────────────────────────────────────────────────────

        [Fact]
        public void Calculate_Laminatina_InitializedToZeroWithDefaultPrice()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);

            Assert.Equal(0, calc.Laminatina.Quantity, 4);
            Assert.Equal(500, calc.Laminatina.Price, 2);
            Assert.Equal("шт.", calc.Laminatina.Unit);
            Assert.Equal("Ламинат", calc.Laminatina.Name);

            Assert.Equal(0, calc.LaminatinaLabor.Quantity, 4);
            Assert.Equal(500, calc.LaminatinaLabor.Price, 2);
            Assert.Equal("шт.", calc.LaminatinaLabor.Unit);
            Assert.Equal("Работа за ламинат", calc.LaminatinaLabor.Name);
        }

        [Fact]
        public void Laminatina_Added_IncreasesTotalMaterialsAndTotalLabor()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            double materialsBefore = calc.TotalMaterials;
            double laborBefore = calc.TotalLabor;

            calc.Laminatina.Quantity = 3;
            calc.Laminatina.Price = 500;
            calc.LaminatinaLabor.Quantity = 3;
            calc.LaminatinaLabor.Price = 500;

            Assert.Equal(materialsBefore + 1500, calc.TotalMaterials, 2);
            Assert.Equal(laborBefore + 1500, calc.TotalLabor, 2);
        }

        [Fact]
        public void Laminatina_IsIncludedInTotalMaterials()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            calc.Laminatina.Quantity = 2;
            calc.Laminatina.Price = 500;

            double expectedTotalMaterials = calc.Sandwich.Sum + calc.Foam.Sum + calc.Sealant.Sum
                + calc.Tape.Sum + calc.StartProfile.Sum + calc.FProfile.Sum + calc.Penoplex.Sum
                + calc.Laminatina.Sum;

            Assert.Equal(expectedTotalMaterials, calc.TotalMaterials, 2);
        }

        [Fact]
        public void UpdateInPlace_PreservesLaminatinaOverride()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            calc.Laminatina.Quantity = 5;
            calc.Laminatina.IsQuantityOverridden = true;
            calc.LaminatinaLabor.Quantity = 5;
            calc.LaminatinaLabor.IsQuantityOverridden = true;

            SlopeCalculatorService.UpdateInPlace(calc, 1200, 1200, 0.20, 1, 1);

            Assert.Equal(5, calc.Laminatina.Quantity, 4);
            Assert.Equal(5, calc.LaminatinaLabor.Quantity, 4);
        }

        [Fact]
        public void LaminatinaLabor_DefaultPrice_500()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            Assert.Equal(500, calc.LaminatinaLabor.Price, 2);
        }

        [Fact]
        public void Laminatina_Serialization_RoundTrip_PreservesValues()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            calc.Laminatina.Quantity = 7;
            calc.Laminatina.Price = 500;
            calc.LaminatinaLabor.Quantity = 7;
            calc.LaminatinaLabor.Price = 500;

            var data = SlopeCalculationData.FromSlopeCalculation(calc);
            var restored = data.ToSlopeCalculation();

            Assert.Equal(7, restored.Laminatina.Quantity, 4);
            Assert.Equal(500, restored.Laminatina.Price, 2);
            Assert.Equal(7, restored.LaminatinaLabor.Quantity, 4);
            Assert.Equal(500, restored.LaminatinaLabor.Price, 2);
        }

        [Fact]
        public void Laminatina_OrderItemTotal_IncludesLaminatinaInPerWindowMaterials()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            calc.Laminatina.Quantity = 2;
            calc.Laminatina.Price = 500;
            calc.LaminatinaLabor.Quantity = 2;
            calc.LaminatinaLabor.Price = 500;

            var item = new OrderItem { Name = "Откос", SlopeData = calc, Quantity = 1 };

            double expectedPerWindow = calc.Sandwich.Sum + calc.Foam.Sum + calc.StartProfile.Sum
                + calc.FProfile.Sum + calc.Penoplex.Sum + calc.Laminatina.Sum;
            double expectedTotal = expectedPerWindow + calc.DistributedSharedSum;

            Assert.Equal(expectedTotal, item.Total, 2);
        }

        [Fact]
        public void SlopeCalculationData_RoundTrip_PreservesIsProfileEconomyApplied()
        {
            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            calc.IsProfileEconomyApplied = true;

            var data = SlopeCalculationData.FromSlopeCalculation(calc);
            var restored = data.ToSlopeCalculation();

            Assert.True(restored.IsProfileEconomyApplied);
        }

        // ────────────────────────────────────────────────────────────
        // Edge cases — последовательности вызовов с мутациями между ними
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// (1) Один SlopeData переиспользуется в двух строках «Откос» + «Работа за откос».
        /// После «удаления» строки «Откос» (rename в не-slope) orphan-calc
        /// получает DSS=0 (а не устаревшее значение от defensive init).
        /// v3.44.x добавил сброс allSlopeData в else-ветке для покрытия этого кейса.
        /// </summary>
        [Fact]
        public void RecalculateSealantAndTape_SharedSlopeBetweenOtkosAndRabota_RemoveOtkos_ResetsOrphanCalcViaBroadenedReset()
        {
            // Contract under test: broadened else-branch reset scopes DSS to all items-referenced calcs, including orphans.
            // Future refactor away from this contract must update this test.

            var calc = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            var slopeItem = new OrderItem { Name = "Откос", SlopeData = calc, Quantity = 1, IsActive = true };
            var laborItem = new OrderItem { Name = "Работа за откос", SlopeData = calc, Quantity = 1, IsActive = true };

            // First call: singleton Откос + labor sharing one calc.
            // Distinct dedupe → slopeItems = [calc] (WindowCount contributing once).
            // Defensive init set DSS=485 (1 sealant + 1 tape); recalc keeps it for singleton.
            SlopeCalculatorService.RecalculateSealantAndTape(new[] { slopeItem, laborItem });
            Assert.Equal(calc.Sealant.Sum + calc.Tape.Sum, calc.DistributedSharedSum, 2);

            // "Remove" the slope row by renaming — orphan calc should now reset DSS.
            slopeItem.Name = "Отлив";
            SlopeCalculatorService.RecalculateSealantAndTape(new[] { slopeItem, laborItem });

            // v3.44.x: orphan calc (no longer in allSlopeData) gets DSS=0 via else-branch fix.
            Assert.Equal(0, calc.DistributedSharedSum, 2);

            // Restore name — DSS resumes singleton full share.
            slopeItem.Name = "Откос";
            SlopeCalculatorService.RecalculateSealantAndTape(new[] { slopeItem, laborItem });
            Assert.Equal(calc.Sealant.Sum + calc.Tape.Sum, calc.DistributedSharedSum, 2);
        }

        /// <summary>
        /// (2) Idempotency: повторные вызовы с идентичными inputs + включённой
        /// Start/F-планка экономией и промискуитетным IsActive — результат
        /// не мутирует от вызова к вызову.
        /// </summary>
        [Fact]
        public void RecalculateSealantAndTape_Idempotent_ProfileEconomyAndIsActiveSettings()
        {
            var calc1 = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            var calc2 = SlopeCalculatorService.Calculate(1200, 1200, 0.15, 1, 1);
            calc1.IsProfileEconomyApplied = true;
            calc2.IsProfileEconomyApplied = true;

            var item1 = new OrderItem { Name = "Откос", SlopeData = calc1, Quantity = 1, IsActive = true };
            var item2 = new OrderItem { Name = "Откос", SlopeData = calc2, Quantity = 1, IsActive = true };

            // Snapshot after first call.
            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item1, item2 });
            double dss1First = calc1.DistributedSharedSum;
            double dss2First = calc2.DistributedSharedSum;
            double total1First = item1.Total;
            double total2First = item2.Total;

            // Re-call three more times — no mutation.
            for (int i = 0; i < 3; i++)
                SlopeCalculatorService.RecalculateSealantAndTape(new[] { item1, item2 });

            Assert.Equal(dss1First, calc1.DistributedSharedSum, 2);
            Assert.Equal(dss2First, calc2.DistributedSharedSum, 2);
            Assert.Equal(total1First, item1.Total, 2);
            Assert.Equal(total2First, item2.Total, 2);

            // ProfileEconomy включена — общая стоимость теперь
            // включает Start + F-планка. Сумма распределений = порядок-уровневый cost.
            double trueSharedCost = calc1.Sealant.Sum + calc1.Tape.Sum
                                  + calc1.StartProfile.Sum + calc1.FProfile.Sum;
            Assert.Equal(trueSharedCost,
                calc1.DistributedSharedSum + calc2.DistributedSharedSum, 2);
        }

        /// <summary>
        /// (3) IsActive toggle между вызовами RecalculateSealantAndTape:
        /// выключенная строка сбрасывает DSS в 0; при повторном включении —
        /// возвращается к участию в общем распределении.
        /// </summary>
        [Fact]
        public void RecalculateSealantAndTape_IsActiveToggleBetweenCalls_FollowsParticipation()
        {
            var calc1 = SlopeCalculatorService.Calculate(1000, 1000, 0.15, 1, 1);
            var calc2 = SlopeCalculatorService.Calculate(1200, 1200, 0.15, 1, 1);
            var calc3 = SlopeCalculatorService.Calculate(1400, 1400, 0.15, 1, 1);

            var item1 = new OrderItem { Name = "Откос", SlopeData = calc1, Quantity = 1, IsActive = true };
            var item2 = new OrderItem { Name = "Откос", SlopeData = calc2, Quantity = 1, IsActive = true };
            var item3 = new OrderItem { Name = "Откос", SlopeData = calc3, Quantity = 1, IsActive = true };

            // All three active → three-way split.
            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item1, item2, item3 });
            Assert.True(calc1.DistributedSharedSum > 0);
            Assert.True(calc2.DistributedSharedSum > 0);
            Assert.True(calc3.DistributedSharedSum > 0);
            double threeWayTotal = calc1.DistributedSharedSum + calc2.DistributedSharedSum + calc3.DistributedSharedSum;

            // Toggle item2 inactive → middle cal DSS must drop to 0,
            // active siblings redistribute the cost between themselves.
            item2.IsActive = false;
            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item1, item2, item3 });

            Assert.Equal(0, calc2.DistributedSharedSum, 2);
            // True shared cost distributed only among {calc1, calc3}.
            Assert.Equal(calc1.Sealant.Sum + calc1.Tape.Sum,
                calc1.DistributedSharedSum + calc3.DistributedSharedSum, 2);

            // Toggle item2 back active → three-way split resumes.
            item2.IsActive = true;
            SlopeCalculatorService.RecalculateSealantAndTape(new[] { item1, item2, item3 });

            Assert.True(calc2.DistributedSharedSum > 0);
            Assert.Equal(threeWayTotal,
                calc1.DistributedSharedSum + calc2.DistributedSharedSum + calc3.DistributedSharedSum, 2);
        }
    }
}
