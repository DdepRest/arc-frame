using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class AnwisSizeCalculatorTests
    {
        [Theory]
        [InlineData(1000, AnwisSizeMode.Брусбокс60, 1002)]
        [InlineData(1000, AnwisSizeMode.Брусбокс70, 998)]
        [InlineData(1000, AnwisSizeMode.РазмерПроёма, 1020)]
        [InlineData(1000, AnwisSizeMode.Профипласт, 1000)]
        [InlineData(1000, AnwisSizeMode.Габаритный, 1000)]
        public void ApplyCalcWidth_ReturnsExpected(double raw, AnwisSizeMode mode, double expected)
        {
            Assert.Equal(expected, AnwisSizeCalculator.ApplyCalcWidth(raw, mode));
        }

        [Theory]
        [InlineData(1000, AnwisSizeMode.Брусбокс60, 970)]
        [InlineData(1000, AnwisSizeMode.Брусбокс70, 970)]
        [InlineData(1000, AnwisSizeMode.РазмерПроёма, 1020)]
        [InlineData(1000, AnwisSizeMode.Профипласт, 1000)]
        [InlineData(1000, AnwisSizeMode.Габаритный, 1000)]
        public void ApplyCalcHeight_ReturnsExpected(double raw, AnwisSizeMode mode, double expected)
        {
            Assert.Equal(expected, AnwisSizeCalculator.ApplyCalcHeight(raw, mode));
        }

        [Theory]
        [InlineData(1002, AnwisSizeMode.Брусбокс60, 1000)]
        [InlineData(998, AnwisSizeMode.Брусбокс70, 1000)]
        [InlineData(1020, AnwisSizeMode.РазмерПроёма, 1000)]
        [InlineData(1000, AnwisSizeMode.Профипласт, 1000)]
        [InlineData(1000, AnwisSizeMode.Габаритный, 1000)]
        public void ReverseCalcWidth_ReturnsExpected(double calc, AnwisSizeMode mode, double expected)
        {
            Assert.Equal(expected, AnwisSizeCalculator.ReverseCalcWidth(calc, mode));
        }

        [Theory]
        [InlineData(970, AnwisSizeMode.Брусбокс60, 1000)]
        [InlineData(970, AnwisSizeMode.Брусбокс70, 1000)]
        [InlineData(1020, AnwisSizeMode.РазмерПроёма, 1000)]
        [InlineData(1000, AnwisSizeMode.Профипласт, 1000)]
        [InlineData(1000, AnwisSizeMode.Габаритный, 1000)]
        public void ReverseCalcHeight_ReturnsExpected(double calc, AnwisSizeMode mode, double expected)
        {
            Assert.Equal(expected, AnwisSizeCalculator.ReverseCalcHeight(calc, mode));
        }

        [Fact]
        public void ApplyCalcWidth_Negative_ClampedToZero()
        {
            Assert.Equal(0, AnwisSizeCalculator.ApplyCalcWidth(-10, AnwisSizeMode.Брусбокс70));
        }

        [Fact]
        public void ApplyCalcHeight_Negative_ClampedToZero()
        {
            Assert.Equal(0, AnwisSizeCalculator.ApplyCalcHeight(-10, AnwisSizeMode.Брусбокс60));
        }

        [Fact]
        public void AnwisSize_DelegatesToCalculator()
        {
            // Integration: AnwisSize factory methods still produce correct values
            // after delegating to AnwisSizeCalculator.
            var size = AnwisSize.ОтВвода(1000, 1000, AnwisSizeMode.Брусбокс60);
            Assert.Equal(1002, size.ШиринаРасчёт);
            Assert.Equal(970, size.ВысотаРасчёт);

            var fromStored = AnwisSize.ОтХранимого(1002, 970, AnwisSizeMode.Брусбокс60);
            Assert.Equal(1000, fromStored.ШиринаОтображение);
            Assert.Equal(1000, fromStored.ВысотаОтображение);
        }
    }
}
