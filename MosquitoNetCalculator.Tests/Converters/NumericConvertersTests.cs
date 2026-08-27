using System.Globalization;
using MosquitoNetCalculator.Converters;
using Xunit;

namespace MosquitoNetCalculator.Tests.Converters
{
    /// <summary>
    /// Grid-cell converters for Ширина/Высота/Кол-во. Regression for the
    /// «2 150,00» → 0 class of bugs: all ConvertBack paths now go through
    /// <see cref="Services.MoneyFormatService.TryParse"/>, so space thousand
    /// separators and both decimal separators parse identically on any OS locale.
    /// </summary>
    public class NumericConvertersTests
    {
        private readonly DimensionConverter _dimension = new();
        private readonly QuantityConverter _quantity = new();

        // ─── DimensionConverter.ConvertBack ─────────────────────

        [Theory]
        [InlineData("1 360 мм", 1360.0)]   // суффикс + разделитель тысяч
        [InlineData("1360 мм", 1360.0)]
        [InlineData("1360,5", 1360.5)]     // запятая
        [InlineData("1360.5", 1360.5)]     // точка
        [InlineData("100", 100.0)]
        public void DimensionConvertBack_SpaceSeparatorAndSuffix_Parses(string input, double expected)
        {
            var result = _dimension.ConvertBack(input, typeof(double), " мм", CultureInfo.InvariantCulture);

            Assert.Equal(expected, (double)result!, 2);
        }

        [Fact]
        public void DimensionConvertBack_Invalid_ReturnsZero()
        {
            var result = _dimension.ConvertBack("abc", typeof(double), " мм", CultureInfo.InvariantCulture);

            Assert.Equal(0.0, (double)result!, 2);
        }

        // ─── QuantityConverter.ConvertBack ──────────────────────

        [Theory]
        [InlineData("5,75", 5.75)]
        [InlineData("5.75", 5.75)]
        [InlineData("2 150", 2150.0)]      // вставка из буфера с разделителем
        [InlineData("1", 1.0)]
        public void QuantityConvertBack_Separators_Parses(string input, double expected)
        {
            var result = _quantity.ConvertBack(input, typeof(double), "", CultureInfo.InvariantCulture);

            Assert.Equal(expected, (double)result!, 2);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("abc")]
        [InlineData("")]
        public void QuantityConvertBack_InvalidOrZero_FallsBackToOne(string input)
        {
            var result = _quantity.ConvertBack(input, typeof(double), "", CultureInfo.InvariantCulture);

            Assert.Equal(1.0, (double)result!, 2);
        }
    }
}
