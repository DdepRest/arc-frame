using MosquitoNetCalculator.Models;
using Xunit;

namespace MosquitoNetCalculator.Tests.Models
{
    public class AnwisSizeTests
    {
        // ────────────────────────────────────────────────────────────────
        // ОтВвода (raw → calc) — основной путь: пользователь вводит размеры,
        // система применяет корректировки режима.
        // ────────────────────────────────────────────────────────────────

        [Fact]
        public void ОтВвода_Брусбокс60_WidthPlus2_HeightMinus30()
        {
            var size = AnwisSize.ОтВвода(1000, 1000, AnwisSizeMode.Брусбокс60);

            Assert.Equal(1000, size.ШиринаОтображение);
            Assert.Equal(1000, size.ВысотаОтображение);
            Assert.Equal(1002, size.ШиринаРасчёт);
            Assert.Equal(970, size.ВысотаРасчёт);
            Assert.Equal(982, size.ШиринаЗавод);   // 1002 - 20
            Assert.Equal(950, size.ВысотаЗавод);    // 970 - 20
        }

        [Fact]
        public void ОтВвода_Брусбокс70_WidthMinus2_HeightMinus30()
        {
            var size = AnwisSize.ОтВвода(1000, 1000, AnwisSizeMode.Брусбокс70);

            Assert.Equal(1000, size.ШиринаОтображение);
            Assert.Equal(1000, size.ВысотаОтображение);
            Assert.Equal(998, size.ШиринаРасчёт);
            Assert.Equal(970, size.ВысотаРасчёт);
        }

        [Fact]
        public void ОтВвода_Профипласт_Identity()
        {
            var size = AnwisSize.ОтВвода(1000, 1000, AnwisSizeMode.Профипласт);

            Assert.Equal(1000, size.ШиринаРасчёт);
            Assert.Equal(1000, size.ВысотаРасчёт);
            Assert.Equal(980, size.ШиринаЗавод);
            Assert.Equal(980, size.ВысотаЗавод);
        }

        [Fact]
        public void ОтВвода_РазмерПроёма_WidthPlus20_HeightPlus20()
        {
            var size = AnwisSize.ОтВвода(1000, 1000, AnwisSizeMode.РазмерПроёма);

            Assert.Equal(1000, size.ШиринаОтображение);
            Assert.Equal(1000, size.ВысотаОтображение);
            Assert.Equal(1020, size.ШиринаРасчёт);
            Assert.Equal(1020, size.ВысотаРасчёт);
        }

        [Fact]
        public void ОтВвода_Габаритный_Identity()
        {
            var size = AnwisSize.ОтВвода(1000, 1000, AnwisSizeMode.Габаритный);

            Assert.Equal(1000, size.ШиринаРасчёт);
            Assert.Equal(1000, size.ВысотаРасчёт);
            Assert.Equal(980, size.ШиринаЗавод);
            Assert.Equal(980, size.ВысотаЗавод);
        }

        // ────────────────────────────────────────────────────────────────
        // ОтХранимого (calc → raw) — обратный пересчёт. Используется при
        // загрузке заказов, где хранятся расчётные размеры.
        // ────────────────────────────────────────────────────────────────

        [Fact]
        public void ОтХранимого_Брусбокс60_WidthMinus2_HeightPlus30()
        {
            // Храним 1002×970 → должно восстановиться 1000×1000
            var size = AnwisSize.ОтХранимого(1002, 970, AnwisSizeMode.Брусбокс60);

            Assert.Equal(1000, size.ШиринаОтображение);
            Assert.Equal(1000, size.ВысотаОтображение);
            Assert.Equal(1002, size.ШиринаРасчёт);
            Assert.Equal(970, size.ВысотаРасчёт);
        }

        [Fact]
        public void ОтХранимого_Брусбокс70_WidthPlus2_HeightPlus30()
        {
            var size = AnwisSize.ОтХранимого(998, 970, AnwisSizeMode.Брусбокс70);

            Assert.Equal(1000, size.ШиринаОтображение);
            Assert.Equal(1000, size.ВысотаОтображение);
        }

        [Fact]
        public void ОтХранимого_РазмерПроёма_WidthMinus20_HeightMinus20()
        {
            var size = AnwisSize.ОтХранимого(1020, 1020, AnwisSizeMode.РазмерПроёма);

            Assert.Equal(1000, size.ШиринаОтображение);
            Assert.Equal(1000, size.ВысотаОтображение);
        }

        [Fact]
        public void ОтХранимого_IdentityModes_RawEqualsCalc()
        {
            // Профипласт и Габаритный — identity: raw = calc
            var pp = AnwisSize.ОтХранимого(1000, 1000, AnwisSizeMode.Профипласт);
            Assert.Equal(1000, pp.ШиринаОтображение);
            Assert.Equal(1000, pp.ВысотаОтображение);

            var gab = AnwisSize.ОтХранимого(1000, 1000, AnwisSizeMode.Габаритный);
            Assert.Equal(1000, gab.ШиринаОтображение);
            Assert.Equal(1000, gab.ВысотаОтображение);
        }

        // ────────────────────────────────────────────────────────────────
        // Roundtrip: ОтВвода → ОтХранимого должны давать тот же raw размер.
        // ────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(AnwisSizeMode.Брусбокс60)]
        [InlineData(AnwisSizeMode.Брусбокс70)]
        [InlineData(AnwisSizeMode.Профипласт)]
        [InlineData(AnwisSizeMode.РазмерПроёма)]
        [InlineData(AnwisSizeMode.Габаритный)]
        public void Roundtrip_RawPreserved_AcrossAllModes(AnwisSizeMode mode)
        {
            const double rawW = 1200;
            const double rawH = 1500;

            var fromInput = AnwisSize.ОтВвода(rawW, rawH, mode);
            var fromStored = AnwisSize.ОтХранимого(fromInput.ШиринаРасчёт, fromInput.ВысотаРасчёт, mode);

            Assert.Equal(rawW, fromStored.ШиринаОтображение);
            Assert.Equal(rawH, fromStored.ВысотаОтображение);
        }

        // ────────────────────────────────────────────────────────────────
        // СРежимом — смена режима сохраняет сырые размеры.
        // ────────────────────────────────────────────────────────────────

        [Fact]
        public void СРежимом_SwitchFromBB60ToPP_KeepsRawDimensions()
        {
            var bb60 = AnwisSize.ОтВвода(1000, 1000, AnwisSizeMode.Брусбокс60);
            var pp = bb60.СРежимом(AnwisSizeMode.Профипласт);

            // Сырые размеры сохраняются
            Assert.Equal(bb60.ШиринаОтображение, pp.ШиринаОтображение);
            Assert.Equal(bb60.ВысотаОтображение, pp.ВысотаОтображение);
            // Расчётные пересчитались под новый режим
            Assert.Equal(1000, pp.ШиринаРасчёт);
            Assert.Equal(1000, pp.ВысотаРасчёт);
            Assert.Equal(AnwisSizeMode.Профипласт, pp.Режим);
        }

        [Fact]
        public void СРежимом_SwitchFromBB60ToProem_Recalculates()
        {
            var bb60 = AnwisSize.ОтВвода(1000, 1000, AnwisSizeMode.Брусбокс60);
            var proem = bb60.СРежимом(AnwisSizeMode.РазмерПроёма);

            // Сырые размеры сохраняются
            Assert.Equal(1000, proem.ШиринаОтображение);
            Assert.Equal(1000, proem.ВысотаОтображение);
            // Под новым режимом: W+20, H+20
            Assert.Equal(1020, proem.ШиринаРасчёт);
            Assert.Equal(1020, proem.ВысотаРасчёт);
        }

        // ────────────────────────────────────────────────────────────────
        // Negative/zero clamping — все размеры ≥ 0
        // ────────────────────────────────────────────────────────────────

        [Fact]
        public void ОтВвода_NegativeDimensions_ClampedToZero()
        {
            var size = AnwisSize.ОтВвода(-100, -200, AnwisSizeMode.Брусбокс60);

            // Raw input: -100 → clamped to 0 in display, but formula runs on raw
            Assert.Equal(0, size.ШиринаОтображение);
            Assert.Equal(0, size.ВысотаОтображение);
            // ApplyCalcWidth(-100, ББ60) = -100+2 = -98 → constructor clamps to 0
            // ApplyCalcHeight(-200, ББ60) = max(0, -200-30) = 0
            Assert.Equal(0, size.ШиринаРасчёт);
            Assert.Equal(0, size.ВысотаРасчёт);
        }

        [Fact]
        public void ОтВвода_ZeroDimensions_ProducesMinimalCalcValues()
        {
            var size = AnwisSize.ОтВвода(0, 0, AnwisSizeMode.Брусбокс70);

            Assert.Equal(0, size.ШиринаОтображение);
            Assert.Equal(0, size.ВысотаОтображение);
            // W−2 = 0−2 → max(0, -2) = 0
            Assert.Equal(0, size.ШиринаРасчёт);
            Assert.Equal(0, size.ВысотаРасчёт);
        }

        // ────────────────────────────────────────────────────────────────
        // Different aspect ratios — sanity check
        // ────────────────────────────────────────────────────────────────

        [Fact]
        public void ОтВвода_NonSquareDimensions()
        {
            var size = AnwisSize.ОтВвода(800, 2100, AnwisSizeMode.Брусбокс60);

            Assert.Equal(800, size.ШиринаОтображение);
            Assert.Equal(2100, size.ВысотаОтображение);
            Assert.Equal(802, size.ШиринаРасчёт);   // 800 + 2
            Assert.Equal(2070, size.ВысотаРасчёт);  // 2100 - 30
        }

        // ────────────────────────────────────────────────────────────────
        // Режим property
        // ────────────────────────────────────────────────────────────────

        [Fact]
        public void Режим_StoredCorrectly()
        {
            var size = AnwisSize.ОтВвода(1000, 1000, AnwisSizeMode.РазмерПроёма);
            Assert.Equal(AnwisSizeMode.РазмерПроёма, size.Режим);
        }

        [Fact]
        public void Завод_AlwaysMinus20_ForAllModes()
        {
            // All modes apply -20mm to both dimensions for factory output
            foreach (AnwisSizeMode mode in System.Enum.GetValues<AnwisSizeMode>())
            {
                var size = AnwisSize.ОтВвода(1000, 1000, mode);
                Assert.Equal(size.ШиринаРасчёт - 20, size.ШиринаЗавод);
                Assert.Equal(size.ВысотаРасчёт - 20, size.ВысотаЗавод);
            }
        }

        [Fact]
        public void Завод_ClampedToZero_WhenCalcLessThan20()
        {
            // Если расчётный размер меньше 20 мм, заводской размер = 0
            var size = AnwisSize.ОтВвода(10, 10, AnwisSizeMode.Профипласт);
            Assert.Equal(0, size.ШиринаЗавод);
            Assert.Equal(0, size.ВысотаЗавод);
        }
    }
}
