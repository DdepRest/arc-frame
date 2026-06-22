using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class AmountInWordsServiceTests
    {
        [Fact]
        public void Convert_Zero()
        {
            Assert.Equal("Ноль рублей 00 копеек", AmountInWordsService.Convert(0));
        }

        [Fact]
        public void Convert_OneRuble()
        {
            Assert.Equal("Один рубль 00 копеек", AmountInWordsService.Convert(1));
        }

        [Fact]
        public void Convert_TwoRubles()
        {
            Assert.Equal("Два рубля 00 копеек", AmountInWordsService.Convert(2));
        }

        [Fact]
        public void Convert_FiveRubles()
        {
            Assert.Equal("Пять рублей 00 копеек", AmountInWordsService.Convert(5));
        }

        [Fact]
        public void Convert_ElevenRubles()
        {
            Assert.Equal("Одиннадцать рублей 00 копеек", AmountInWordsService.Convert(11));
        }

        [Fact]
        public void Convert_TwentyOneRuble()
        {
            Assert.Equal("Двадцать один рубль 00 копеек", AmountInWordsService.Convert(21));
        }

        [Fact]
        public void Convert_OneHundred()
        {
            var result = AmountInWordsService.Convert(100);
            Assert.StartsWith("Сто", result);
            Assert.Contains("рублей", result);
        }

        [Fact]
        public void Convert_OneThousand_Feminine()
        {
            // Thousands use feminine form: "одна тысяча"
            Assert.Equal("Одна тысяча рублей 00 копеек", AmountInWordsService.Convert(1000));
        }

        [Fact]
        public void Convert_TwoThousand_Feminine()
        {
            // "две тысячи" (feminine)
            var result = AmountInWordsService.Convert(2000);
            Assert.Contains("две тысячи", result.ToLower());
        }

        [Fact]
        public void Convert_FiveThousand()
        {
            var result = AmountInWordsService.Convert(5000);
            // ThousandsForms[2] = "тысяч" (genitive plural for 5)
            Assert.Contains("тысяч", result.ToLower());
        }

        [Fact]
        public void Convert_WithKopecks()
        {
            var result = AmountInWordsService.Convert(1234.56);
            Assert.Contains("рубля", result);
            Assert.Contains("56 копеек", result);
        }

        [Fact]
        public void Convert_OneKopeck()
        {
            var result = AmountInWordsService.Convert(0.01);
            Assert.Contains("01 копейка", result);
        }

        [Fact]
        public void Convert_TwoKopecks()
        {
            var result = AmountInWordsService.Convert(0.02);
            Assert.Contains("02 копейки", result);
        }

        [Fact]
        public void Convert_FiveKopecks()
        {
            var result = AmountInWordsService.Convert(0.05);
            Assert.Contains("05 копеек", result);
        }

        [Fact]
        public void Convert_ElevenKopecks()
        {
            var result = AmountInWordsService.Convert(0.11);
            Assert.Contains("11 копеек", result);
        }

        [Fact]
        public void Convert_OneMillion()
        {
            var result = AmountInWordsService.Convert(1_000_000);
            Assert.Contains("один миллион", result.ToLower());
            Assert.Contains("рублей", result);
        }

        [Fact]
        public void Convert_CapitalizesFirstLetter()
        {
            var result = AmountInWordsService.Convert(100);
            Assert.Equal('С', result[0]); // "Сто"
        }

        [Fact]
        public void Convert_LargeAmount()
        {
            var result = AmountInWordsService.Convert(1234567.89);
            Assert.Contains("миллион", result);
            Assert.Contains("тысяч", result);
            Assert.Contains("89 копеек", result);
        }

        [Fact]
        public void Convert_RoundingKopecks()
        {
            // 1.999 should round to 2.00 → 2 рубля 00 копеек
            var result = AmountInWordsService.Convert(1.999);
            Assert.Contains("два рубля", result.ToLower());
        }

        [Fact]
        public void Convert_TwentyTwoRubles()
        {
            var result = AmountInWordsService.Convert(22);
            Assert.Contains("двадцать два", result.ToLower());
            Assert.Contains("рубля", result);
        }

        [Fact]
        public void Convert_NinetyNineRubles()
        {
            var result = AmountInWordsService.Convert(99);
            Assert.Contains("девяносто девять", result.ToLower());
            Assert.Contains("рублей", result);
        }
    }
}
