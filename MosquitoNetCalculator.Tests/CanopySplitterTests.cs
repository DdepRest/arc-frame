using System.Linq;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests
{
    public class CanopySplitterTests
    {
        // ── Эталонные тесты по разделу 6 ТЗ ──

        [Fact]
        public void Test01_BaseCase_3000_NoCorners()
        {
            // #1 | 3000 | 350 | нет | 2 | 1550, 1550 | базовый пример
            var res = CanopySplitter.CalcSplit(3000, 350, cornerStart: false, cornerEnd: false);

            Assert.True(res.Success);
            Assert.Equal(2, res.Count);
            Assert.Equal(1500, res.Base);
            Assert.Equal(new double[] { 1550, 1550 }, res.Pieces.Select(p => p.Length).ToArray());
            Assert.Equal(3100, res.TotalLength);
        }

        [Fact]
        public void Test02_3000_TwoCorners()
        {
            // #2 | 3000 | 350 | 2 | 2 | 1900, 1900 | с двумя боковинами
            var res = CanopySplitter.CalcSplit(3000, 350, cornerStart: true, cornerEnd: true);

            Assert.True(res.Success);
            Assert.Equal(2, res.Count);
            Assert.Equal(1500, res.Base);
            Assert.Equal(new double[] { 1900, 1900 }, res.Pieces.Select(p => p.Length).ToArray());
            Assert.Equal(3800, res.TotalLength);
        }

        [Fact]
        public void Test03_SidePiece_800_OneCorner()
        {
            // #3 | 800 | 350 | 1 | 1 | 1150 | боковина
            var res = CanopySplitter.CalcSplit(800, 350, cornerStart: true, cornerEnd: false);

            Assert.True(res.Success);
            Assert.Equal(1, res.Count);
            Assert.Equal(800, res.Base);
            Assert.Equal(new double[] { 1150 }, res.Pieces.Select(p => p.Length).ToArray());
        }

        [Fact]
        public void Test04_ExactLimit_2000()
        {
            // #4 | 2000 | 350 | нет | 1 | 2000 | ровно на лимите
            var res = CanopySplitter.CalcSplit(2000, 350, cornerStart: false, cornerEnd: false);

            Assert.True(res.Success);
            Assert.Equal(1, res.Count);
            Assert.Equal(2000, res.Base);
            Assert.Equal(new double[] { 2000 }, res.Pieces.Select(p => p.Length).ToArray());
        }

        [Fact]
        public void Test05_OverLimit_2001()
        {
            // #5 | 2001 | 350 | нет | 2 | 1060, 1060 | 1000.5 → 1010, +50
            var res = CanopySplitter.CalcSplit(2001, 350, cornerStart: false, cornerEnd: false);

            Assert.True(res.Success);
            Assert.Equal(2, res.Count);
            Assert.Equal(1010, res.Base);
            Assert.Equal(new double[] { 1060, 1060 }, res.Pieces.Select(p => p.Length).ToArray());
        }

        [Fact]
        public void Test06_MainBenchmark_4100_TwoCorners()
        {
            // #6 | 4100 | 350 | 2 | 3 | 1770, 1470, 1770 | 2 куска не проходят
            var res = CanopySplitter.CalcSplit(4100, 350, cornerStart: true, cornerEnd: true);

            Assert.True(res.Success);
            Assert.Equal(3, res.Count);
            Assert.Equal(1370, res.Base);
            Assert.Equal(new double[] { 1770, 1470, 1770 }, res.Pieces.Select(p => p.Length).ToArray());
        }

        [Fact]
        public void Test07_4100_NoCorners()
        {
            // #7 | 4100 | 350 | нет | 3 | 1420, 1470, 1420
            var res = CanopySplitter.CalcSplit(4100, 350, cornerStart: false, cornerEnd: false);

            Assert.True(res.Success);
            Assert.Equal(3, res.Count);
            Assert.Equal(1370, res.Base);
            Assert.Equal(new double[] { 1420, 1470, 1420 }, res.Pieces.Select(p => p.Length).ToArray());
        }

        [Fact]
        public void Test08_3700_TwoCorners()
        {
            // #8 | 3700 | 350 | 2 | 3 | 1640, 1340, 1640 | 1850+50+350=2250 ✗
            var res = CanopySplitter.CalcSplit(3700, 350, cornerStart: true, cornerEnd: true);

            Assert.True(res.Success);
            Assert.Equal(3, res.Count);
            Assert.Equal(1240, res.Base);
            Assert.Equal(new double[] { 1640, 1340, 1640 }, res.Pieces.Select(p => p.Length).ToArray());
        }

        [Fact]
        public void Test09_2500_OneCorner()
        {
            // #9 | 2500 | 350 | 1 | 2 | 1650, 1300
            // При n=2: base=1250, край с углом = 1250+50+350=1650, край без угла = 1250+50=1300
            var res = CanopySplitter.CalcSplit(2500, 350, cornerStart: true, cornerEnd: false);

            Assert.True(res.Success);
            Assert.Equal(2, res.Count);
            Assert.Equal(1250, res.Base);
            Assert.Equal(new double[] { 1650, 1300 }, res.Pieces.Select(p => p.Length).ToArray());
        }

        [Fact]
        public void Test10_5500_NoCorners()
        {
            // #10 | 5500 | 350 | нет | 3 | 1890, 1940, 1890
            var res = CanopySplitter.CalcSplit(5500, 350, cornerStart: false, cornerEnd: false);

            Assert.True(res.Success);
            Assert.Equal(3, res.Count);
            Assert.Equal(1840, res.Base);
            Assert.Equal(new double[] { 1890, 1940, 1890 }, res.Pieces.Select(p => p.Length).ToArray());
        }

        [Fact]
        public void Test11_MinimalCase_150_OneCorner()
        {
            // #11 | 150 | 350 | 1 | 1 | 500 | минимальный случай
            var res = CanopySplitter.CalcSplit(150, 350, cornerStart: true, cornerEnd: false);

            Assert.True(res.Success);
            Assert.Equal(1, res.Count);
            Assert.Equal(150, res.Base);
            Assert.Equal(new double[] { 500 }, res.Pieces.Select(p => p.Length).ToArray());
        }

        [Fact]
        public void Test12_WidthLimitExceededWithCorner()
        {
            // #12 | 3000 | 1951 | 1 | — | ошибка | 1951+50 > 2000
            var res = CanopySplitter.CalcSplit(3000, 1951, cornerStart: true, cornerEnd: false);

            Assert.False(res.Success);
            Assert.Equal("Раскрой невозможен: материал + нахлёст на угол больше 2000 мм", res.Error);
        }

        // ── Дополнительные тесты валидации и граничных условий ──

        [Fact]
        public void Validation_ZeroOrNegativeLength_ReturnsError()
        {
            var res0 = CanopySplitter.CalcSplit(0, 350, false, false);
            Assert.False(res0.Success);
            Assert.Equal("Длина должна быть больше нуля", res0.Error);

            var resNeg = CanopySplitter.CalcSplit(-100, 350, false, false);
            Assert.False(resNeg.Success);
            Assert.Equal("Длина должна быть больше нуля", resNeg.Error);
        }

        [Fact]
        public void Validation_ZeroOrNegativeWidth_ReturnsError()
        {
            var res0 = CanopySplitter.CalcSplit(1000, 0, false, false);
            Assert.False(res0.Success);
            Assert.Equal("Ширина материала должна быть больше нуля", res0.Error);
        }

        [Fact]
        public void Validation_WidthGreaterThanMaxPiece_ReturnsError()
        {
            var res = CanopySplitter.CalcSplit(1000, 2050, false, false);
            Assert.False(res.Success);
            Assert.Equal("Ширина материала превышает максимальную длину куска", res.Error);
        }
    }
}
