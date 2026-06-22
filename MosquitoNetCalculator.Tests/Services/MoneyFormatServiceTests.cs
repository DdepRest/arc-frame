using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class MoneyFormatServiceTests
    {
        [Theory]
        [InlineData(1000, "1 000,00")]
        [InlineData(0, "0,00")]
        [InlineData(1234567.89, "1 234 567,89")]
        [InlineData(-500, "-500,00")]
        [InlineData(0.5, "0,50")]
        [InlineData(999999.99, "999 999,99")]
        [InlineData(1, "1,00")]
        [InlineData(0.01, "0,01")]
        public void Format_ReturnsCorrectFormat(double amount, string expected)
        {
            Assert.Equal(expected, MoneyFormatService.Format(amount));
        }

        [Theory]
        [InlineData(1000, "1 000")]
        [InlineData(0, "0")]
        [InlineData(1234567, "1 234 567")]
        [InlineData(500, "500")]
        [InlineData(1, "1")]
        public void FormatWhole_ReturnsCorrectFormat(double amount, string expected)
        {
            Assert.Equal(expected, MoneyFormatService.FormatWhole(amount));
        }

        [Theory]
        [InlineData("1 000", true, 1000)]
        [InlineData("1000,50", true, 1000.50)]
        [InlineData("1000.50", true, 1000.50)]
        [InlineData("1234567,89", true, 1234567.89)]
        [InlineData("500", true, 500)]
        [InlineData("0", true, 0)]
        [InlineData("", false, 0)]
        [InlineData(null, false, 0)]
        [InlineData("  ", false, 0)]
        [InlineData("abc", false, 0)]
        public void TryParse_HandlesVariousInputs(string? input, bool expectedSuccess, double expectedResult)
        {
            bool result = MoneyFormatService.TryParse(input!, out double value);
            Assert.Equal(expectedSuccess, result);
            if (expectedSuccess)
                Assert.Equal(expectedResult, value, 2);
        }

        [Fact]
        public void TryParse_HandlesNonBreakingSpace()
        {
            // Non-breaking space (U+00A0) as thousands separator
            bool result = MoneyFormatService.TryParse("1\u00A0000", out double value);
            Assert.True(result);
            Assert.Equal(1000, value, 2);
        }

        [Fact]
        public void TryParse_HandlesMultipleSpaces()
        {
            bool result = MoneyFormatService.TryParse("1 234 567", out double value);
            Assert.True(result);
            Assert.Equal(1234567, value, 2);
        }

        [Fact]
        public void Format_NegativeZero()
        {
            var result = MoneyFormatService.Format(-0.0);
            // .NET formats -0.0 as "-0,00" — this is platform behavior, not a bug
            Assert.True(result == "0,00" || result == "-0,00", $"Unexpected format: {result}");
        }
    }
}
