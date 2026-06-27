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

        // ─────────────────────────────────────────────────────────────────────
        // Regression: trailing-comma contract, locked from GOTCHAS.md#13
        //
        // Зафиксировано ЭМПИРИЧЕСКИ на .NET, использованном в проекте:
        //   double.TryParse("15000,", NumberStyles.Any, RuNumberFormat) → true, 15000.
        //
        // Современный .NET принимает trailing-запятую как нулевую дробную часть:
        // целое "15000" + trailing "," = 15000.0. Это поведение ПЛАТФОРМЫ, не
        // специфика MoneyFormatService. Реальный UX при наборе «15000» в ячейке
        // «Цена» с UpdateSourceTrigger="PropertyChanged":
        //
        //   1 → 15 → 150 → 1500 → 15000 → 15000 (без вспышки «0,00»)
        //
        // Никакого flicker на «0,00» НЕ происходит. Исходная формулировка
        // грабли #13 предполагала иное поведение, но была неверна — тесты ниже
        // фиксируют актуальный контракт и защищают future refactor of TryParse
        // от silently-changing user-visible typing experience.
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void TryParse_TrailingComma_DocumentsActualContract_FromGOTCHAS13()
        {
            // 1. Целая часть "15000" — успешно, 15000.
            Assert.True(MoneyFormatService.TryParse("15000", out var step1));
            Assert.Equal(15000, step1, 2);

            // 2. Trailing-запятая "15000," — ТОЖЕ успешно, как 15000 (платформенное
            //    поведение современного .NET, не MoneyFormatService-специфика).
            Assert.True(MoneyFormatService.TryParse("15000,", out var step2));
            Assert.Equal(15000, step2, 2);

            // 3. Первая цифра дроби "15000,5" — 15000.5.
            Assert.True(MoneyFormatService.TryParse("15000,5", out var step3));
            Assert.Equal(15000.5, step3, 2);

            // 4. Полная фракция "15000,50" — 15000.5 (две цифры дают тот же double).
            Assert.True(MoneyFormatService.TryParse("15000,50", out var step4));
            Assert.Equal(15000.5, step4, 2);

            // 5. Locale-agnostic точка "15000.5" — 15000.5 (точки нормализуются).
            Assert.True(MoneyFormatService.TryParse("15000.5", out var step5));
            Assert.Equal(15000.5, step5, 2);
        }

        [Theory]
        [InlineData("15000,", 15000)]   // trailing-запятая после целого — 15000
        [InlineData("1,", 1)]            // короткое целое — 1
        [InlineData("15000, ", 15000)]   // trailing + пробел (strip → «15000,») — 15000
        public void TryParse_TrailingComma_OnWholeNumber_AbsorbsAsInteger(string input, double expected)
        {
            Assert.True(
                MoneyFormatService.TryParse(input, out var value),
                $"Expected '{input}' to PARSE as integer, but TryParse returned false");
            Assert.Equal(expected, value, 2);
        }

        [Theory]
        [InlineData("15000,,")]       // двойная запятая — синтаксис невалиден
        [InlineData("15000.5,")]      // trailing после фракции (.,) — синтаксис невалиден
        [InlineData("15000,5,")]      // то же в comma-форме — синтаксис невалиден
        [InlineData(",")]             // только запятая — нет цифр
        [InlineData("15000,.5")]      // . после , (normalize → «15000,,5») — невалиден
        public void TryParse_MalformedCommaVariants_ReturnFalse_AndZero(string input)
        {
            Assert.False(
                MoneyFormatService.TryParse(input, out var value),
                $"Expected '{input}' to FAIL parsing, but TryParse returned true with value={value}");
            Assert.Equal(0, value, 2);
        }
    }
}
