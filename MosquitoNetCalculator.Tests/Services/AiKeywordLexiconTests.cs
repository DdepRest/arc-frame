using System.Text.Json;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Stage-2 hardening: <see cref="AiKeywordLexicon"/> consolidates
    /// the keyword/regex detection logic that previously lived in
    /// <c>AiCommandParser</c> and <c>AiClarificationForm</c>. These
    /// tests guard every public predicate so the safety-policy pipeline
    /// can rely on them offline.
    /// </summary>
    public class AiKeywordLexiconTests
    {
        // ── Anwis size-mode labels ────────────────────────────────────

        [Theory]
        [InlineData(AnwisSizeMode.Брусбокс60, "ББ60")]
        [InlineData(AnwisSizeMode.Брусбокс70, "ББ70")]
        [InlineData(AnwisSizeMode.Профипласт, "ПП")]
        [InlineData(AnwisSizeMode.РазмерПроёма, "Проём")]
        [InlineData(AnwisSizeMode.Габаритный, "Габарит")]
        public void AnwisModeLabel_AllModes_ReturnCanonicalLabel(AnwisSizeMode m, string expected)
        {
            Assert.Equal(expected, AiKeywordLexicon.AnwisModeLabel(m));
        }

        [Theory]
        [InlineData("ББ60", AnwisSizeMode.Брусбокс60)]
        [InlineData("бб60", AnwisSizeMode.Брусбокс60)]
        [InlineData("BB60", AnwisSizeMode.Брусбокс60)]
        [InlineData("брусбокс 60", AnwisSizeMode.Брусбокс60)]
        [InlineData("ББ70", AnwisSizeMode.Брусбокс70)]
        [InlineData("пп", AnwisSizeMode.Профипласт)]
        [InlineData("проём", AnwisSizeMode.РазмерПроёма)]
        [InlineData("проем", AnwisSizeMode.РазмерПроёма)]
        [InlineData("габарит", AnwisSizeMode.Габаритный)]
        [InlineData("габаритный", AnwisSizeMode.Габаритный)]
        public void ParseAnwisModeString_KnownAliases(string token, AnwisSizeMode expected)
        {
            Assert.Equal(expected, AiKeywordLexicon.ParseAnwisModeString(token));
        }

        [Fact]
        public void ParseAnwisModeString_Unknown_FallsBackToDefault()
        {
            Assert.Equal(AnwisSizeService.DefaultMode,
                AiKeywordLexicon.ParseAnwisModeString("непонятно"));
        }

        [Theory]
        [InlineData("Сделай сетку Anwis бел 700×1400 ПП", "ПП")]
        [InlineData("сетка 700x1400 ББ 60 бел", "ББ 60")]
        [InlineData("сетка 700x1400 бб70", "ББ 70")]
        [InlineData("сетка 700x1400 профипласт", "ПП")]
        [InlineData("сетка 700x1400 проём", "Проём")]
        [InlineData("сетка 700x1400 габарит", "Габарит")]
        public void DetectAnwisMode_RussianPhrases(string text, string expected)
        {
            Assert.Equal(expected, AiKeywordLexicon.DetectAnwisMode(text));
        }

        [Theory]
        [InlineData("Сделай сетку ПП")]
        [InlineData("ББ70")]
        [InlineData("габарит")]
        public void AnwisModeSpecified_ReturnsTrue_WhenModeInText(string text)
        {
            Assert.True(AiKeywordLexicon.AnwisModeSpecified(text));
        }

        [Theory]
        [InlineData("Сделай сетку 700×1400")]
        [InlineData("Анвис белый 700×1400")]
        [InlineData("")]
        [InlineData(null)]
        public void AnwisModeSpecified_ReturnsFalse_WhenNoMode(string? text)
        {
            Assert.False(AiKeywordLexicon.AnwisModeSpecified(text));
        }

        // ── Installation mode ─────────────────────────────────────────

        [Theory]
        [InlineData("с монтажом")]
        [InlineData("с монтажём")]
        [InlineData("монтаж включён")]
        [InlineData("монтаж включен")]
        public void DetectInstallationMode_With_ReturnsZero(string text)
        {
            Assert.Equal(0, AiKeywordLexicon.DetectInstallationMode(text));
        }

        [Theory]
        [InlineData("без монтажа")]
        [InlineData("без установки")]
        public void DetectInstallationMode_Without_ReturnsOne(string text)
        {
            Assert.Equal(1, AiKeywordLexicon.DetectInstallationMode(text));
        }

        [Theory]
        [InlineData("в конструкцию")]
        [InlineData("в конструцию")]
        [InlineData("конструкция")]
        public void DetectInstallationMode_InConstruction_ReturnsTwo(string text)
        {
            Assert.Equal(2, AiKeywordLexicon.DetectInstallationMode(text));
        }

        [Theory]
        [InlineData("Отлив 200x1500")]
        [InlineData("")]
        [InlineData(null)]
        public void DetectInstallationMode_NotMentioned_ReturnsMinusOne(string? text)
        {
            Assert.Equal(-1, AiKeywordLexicon.DetectInstallationMode(text));
        }

        [Theory]
        [InlineData("отлив 200×1500 с монтажом", true)]
        [InlineData("отлив 200×1500 без монтажа", true)]
        [InlineData("отлив 200×1500", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void InstallationModeSpecified_FollowsDetection(string? text, bool expected)
        {
            Assert.Equal(expected, AiKeywordLexicon.InstallationModeSpecified(text));
        }

        [Fact]
        public void InstallationLabel_ReturnsHumanLabelForCode()
        {
            Assert.Equal("С монтажом", AiKeywordLexicon.InstallationLabel(0));
            Assert.Equal("Без монтажа", AiKeywordLexicon.InstallationLabel(1));
            Assert.Equal("В конструкцию", AiKeywordLexicon.InstallationLabel(2));
            Assert.Equal("Не указывать", AiKeywordLexicon.InstallationLabel(null));
            Assert.Equal("Не указывать", AiKeywordLexicon.InstallationLabel(99));
        }

        [Theory]
        [InlineData("2", 2)]
        [InlineData("в конструкцию", 2)]
        [InlineData("0", 0)]
        [InlineData("с монтажом", 0)]
        [InlineData("1", 1)]
        [InlineData("без монтажа", 1)]
        public void ParseInstallationModeField_AcceptsNumberAndString(string token, int expected)
        {
            using var doc = JsonDocument.Parse($"\"{token}\"");
            Assert.Equal(expected, AiKeywordLexicon.ParseInstallationModeField(doc.RootElement));
        }

        [Fact]
        public void ParseInstallationModeField_OutOfRangeNumber_ReturnsNull()
        {
            using var doc = JsonDocument.Parse("9");
            Assert.Null(AiKeywordLexicon.ParseInstallationModeField(doc.RootElement));
        }

        // ── Colors ─────────────────────────────────────────────────────

        [Theory]
        [InlineData("Анвис белый 700×1400", "Белый")]
        [InlineData("Анвис бел 700×1400", "Белый")]
        [InlineData("Отлив коричневый", "Коричневый")]
        [InlineData("Отлив корич 200x1500", "Коричневый")]
        [InlineData("Отлив антрацит", "Антрацит")]
        [InlineData("Отлив золотой дуб", "Золотой дуб")]
        [InlineData("Отлив дуб", "Золотой дуб")]
        [InlineData("Уплотнение серый", "Серый")]
        [InlineData("Уплотнение чёрный", "Чёрный")]
        public void DetectColor_AllKnownStems(string text, string expected)
        {
            Assert.Equal(expected, AiKeywordLexicon.DetectColor(text));
        }

        [Theory]
        [InlineData("Анвис 700×1400")]
        [InlineData("Сетка")]
        [InlineData("")]
        [InlineData(null)]
        public void DetectColor_Unknown_ReturnsNull(string? text)
        {
            Assert.Null(AiKeywordLexicon.DetectColor(text));
        }

        // ── Regex ──────────────────────────────────────────────────────

        [Theory]
        [InlineData("739х1116", 739, 1116)]
        [InlineData("739 х 1116", 739, 1116)]
        [InlineData("739 x 1116", 739, 1116)]
        [InlineData("739×1116", 739, 1116)]
        [InlineData("739*1116", 739, 1116)]
        public void DimensionRegex_CapturesWidthAndHeight(string input, int w, int h)
        {
            var m = AiKeywordLexicon.DimensionRegex.Match(input);
            Assert.True(m.Success);
            Assert.Equal(w.ToString(), m.Groups[1].Value);
            Assert.Equal(h.ToString(), m.Groups[2].Value);
        }

        [Theory]
        [InlineData("4 шт", "4")]
        [InlineData("2,5шт", "2,5")]
        [InlineData("3 ШТ", "3")]
        public void QuantityRegex_CapturesNumber(string input, string expected)
        {
            var m = AiKeywordLexicon.QuantityRegex.Match(input);
            Assert.True(m.Success);
            Assert.Equal(expected, m.Groups[1].Value);
        }

        [Theory]
        [InlineData("ПМС Anwis, бел. 1 3711217", "371", "1217", "ПМС Anwis, бел. 1 371×1217")]
        [InlineData("ПМС Anwis, бел. 1 371 1217", "371", "1217", "ПМС Anwis, бел. 1 371×1217")]
        [InlineData("3711217", "371", "1217", "371×1217")]
        [InlineData("ПМС Anwis, бел. 1 371x1217", "371", "1217", "ПМС Anwis, бел. 1 371x1217")] // уже разделено
        [InlineData("400x1500", "1500", "400", "400x1500")] // перевёрнутая пара не совпадает → не трогаем
        [InlineData("Anwis 700×1400", "371", "1217", "Anwis 700×1400")] // чужая пара
        [InlineData("", "371", "1217", "")]
        [InlineData("   ", "371", "1217", "   ")]
        public void NormalizeCompactDimension_OnlyReplacesWhenPairMatches(
            string? text, string width, string height, string? expected)
        {
            Assert.Equal(expected, AiKeywordLexicon.NormalizeCompactDimension(text, width, height));
        }

        [Theory]
        [InlineData("ПМС Anwis, бел. 1 3711217", true)]
        [InlineData("ПМС Anwis 3711217", true)]
        [InlineData("79991234567", true)]
        [InlineData("ПМС Anwis, бел. 1 371×1217", false)] // явный разделитель есть
        [InlineData("ПМС Anwis 371x1217", false)]
        [InlineData("ПМС Anwis, бел.", false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData(null, false)]
        public void ShouldHideOcrFromBubble_GluedDigitsWithoutSeparator_ReturnsTrue(
            string? text, bool expected)
        {
            Assert.Equal(expected, AiKeywordLexicon.ShouldHideOcrFromBubble(text));
        }

        [Fact]
        public void LeadingNumberRegex_CapturesTrailingNumber()
        {
            var m = AiKeywordLexicon.LeadingNumberRegex.Match("4");
            Assert.True(m.Success);
            Assert.Equal("4", m.Groups[1].Value);
        }

        // ── ContainsAny ───────────────────────────────────────────────

        [Theory]
        [InlineData("отлив белый", true, "отлив")]
        [InlineData("отлив белый", true, "белый")]
        [InlineData("отлив белый", false, "коричневый")]
        [InlineData("отлив", false, "с монтажом")]
        public void ContainsAny_FindsKeywordSubstrings(string text, bool expected, string kw)
        {
            Assert.Equal(expected, AiKeywordLexicon.ContainsAny(text, kw));
        }
    }
}
