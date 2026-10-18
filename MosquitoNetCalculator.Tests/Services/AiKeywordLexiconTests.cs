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

        // ── Several positions in one request ───────────────────────────

        [Fact]
        public void ParseSizeEntries_ScreenshotWithTwoPositions_ReadsBoth()
        {
            // The reported case: one screenshot whose text carries two rows of
            // «ПМС Anwis, бел. 1 <size>». Both must be read — the card used to
            // keep only the first one.
            var entries = AiKeywordLexicon.ParseSizeEntries(
                "ПМС Anwis, бел.\n1 304x1593\nПМС Anwis, бел.\n1 724x1593");

            Assert.Equal(2, entries.Count);
            Assert.Equal((304, 1593), (entries[0].Width, entries[0].Height));
            Assert.Equal((724, 1593), (entries[1].Width, entries[1].Height));
            Assert.All(entries, e => Assert.Equal(1, e.Quantity));
        }

        [Fact]
        public void ParseSizeEntries_QuantityStaysWithItsOwnSize()
        {
            var entries = AiKeywordLexicon.ParseSizeEntries("2 шт 700х1400 3 шт 900х1600");

            Assert.Equal(2, entries.Count);
            Assert.Equal(2, entries[0].Quantity);
            Assert.Equal(3, entries[1].Quantity);
        }

        [Theory]
        [InlineData("739×1116 4 шт", 4)]   // count right behind the size
        [InlineData("Anwis белый 2 шт 739х1116", 2)] // …or right before it
        [InlineData("4 739х1116", 4)]        // bare number in front
        [InlineData("сетка 700х1400", 1)]     // nothing named → 1
        public void ParseSizeEntries_ReadsTheQuantityOfTheSize(string text, double expected)
        {
            var entries = AiKeywordLexicon.ParseSizeEntries(text);

            Assert.Single(entries);
            Assert.Equal(expected, entries[0].Quantity);
        }

        [Fact]
        public void ParseSizeEntries_CountOnAnotherLine_DoesNotLeakOntoTheSize()
        {
            // «герметик 2 шт» on the next line belongs to another position.
            var entries = AiKeywordLexicon.ParseSizeEntries("сетка 700х1400\nгерметик 2 шт");

            Assert.Single(entries);
            Assert.Equal(1, entries[0].Quantity);
        }

        [Fact]
        public void ParseSizeEntries_SameSizeTwiceInOneRequest_IsOnePosition()
        {
            // The same order is regularly present twice: in the attachment file
            // name and in the OCR of its pixels. Adding it twice would inflate
            // the order.
            var entries = AiKeywordLexicon.ParseSizeEntries(
                "ПМС Anwis, бел. 1 619x1295.png\nПМС Anwis, бел. 1 619x1295");

            Assert.Single(entries);
        }

        [Fact]
        public void ParseSizeEntries_GluedOcrDigits_ReturnsNothing()
        {
            // «3711217» — no separator, so the split would be a guess.
            Assert.Empty(AiKeywordLexicon.ParseSizeEntries("ПМС Anwis, бел. 2 шт 3711217"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void ParseSizeEntries_EmptyText_ReturnsNothing(string? text)
        {
            Assert.Empty(AiKeywordLexicon.ParseSizeEntries(text));
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

        [Theory]
        [InlineData("Какой режим Anwis использовать для двух сеток 1272×608?", 1272, 608, 2)]
        [InlineData("сделай две сетки 600х1200", 600, 1200, 2)]
        [InlineData("три штуки 500*1000", 500, 1000, 3)]
        public void ParseSizeEntries_RussianNumberWords_DetectsQuantity(string text, int expectedW, int expectedH, double expectedQ)
        {
            var entries = AiKeywordLexicon.ParseSizeEntries(text);
            Assert.Single(entries);
            Assert.Equal(expectedW, entries[0].Width);
            Assert.Equal(expectedH, entries[0].Height);
            Assert.Equal(expectedQ, entries[0].Quantity);
        }

        [Fact]
        public void ClarificationPrefill_FromReply_SelectsAnwisAndPrefillsWordQuantity()
        {
            var form = new MosquitoNetCalculator.Models.AiClarificationForm(null, null);
            AiClarificationPrefill.FromReply(form, "Какой режим Anwis использовать для двух сеток 1272×608? ББ60, ББ70, ПП, Проём или Габарит?");

            Assert.Equal("Anwis", form.SelectedType);
            Assert.Single(form.SizeRows);
            Assert.Equal("1272", form.SizeRows[0].WidthText);
            Assert.Equal("608", form.SizeRows[0].HeightText);
            Assert.Equal("2", form.SizeRows[0].QuantityText);
        }

        [Fact]
        public void ParseSizeEntries_WithGlassUnitMentions_IdentifiesGlassUnits()
        {
            var text = "Какой режим Anwis использовать для двух сеток 1272×608? ББ60, ББ70, ПП, Проём или Габарит?\n" +
                       "Также: что сделать со стеклопакетом (СП 615×1303)? Добавить как «Свой товар» или только просчитать сетки?";
            var entries = AiKeywordLexicon.ParseSizeEntries(text);

            Assert.Equal(2, entries.Count);
            // Net entry
            Assert.Equal(1272, entries[0].Width);
            Assert.Equal(608, entries[0].Height);
            Assert.Equal(2, entries[0].Quantity);
            Assert.False(entries[0].IsGlassUnit);

            // Glass unit entry
            Assert.Equal(615, entries[1].Width);
            Assert.Equal(1303, entries[1].Height);
            Assert.Equal(1, entries[1].Quantity);
            Assert.True(entries[1].IsGlassUnit);
        }

        [Fact]
        public void ClarificationPrefill_FromReply_OnlyGlassUnit_DoesNotPrefillAnwisSize()
        {
            // Exact scenario from user's bug report: model replied only about Anwis mode options
            // and asked about glass unit (СП 615×7303). The Anwis card must NOT receive 615×7303!
            var reply = "Какой режим Anwis использовать? ББ60, ББ70, ПП, Проём или Габарит?\n\n" +
                        "Также: что сделать со стеклопакетом (СП 615×7303)? Добавить как «Свой товар» или только просчитать сетки?";
            var form = new MosquitoNetCalculator.Models.AiClarificationForm(null, null);
            AiClarificationPrefill.FromReply(form, reply);

            Assert.Equal("Anwis", form.SelectedType);
            // Size rows must remain empty for manual entry — not filled with glass unit 615×7303
            Assert.Single(form.SizeRows);
            Assert.Empty(form.SizeRows[0].WidthText);
            Assert.Empty(form.SizeRows[0].HeightText);
        }

        [Fact]
        public void ClarificationPrefill_FromReply_BothNetsAndGlassUnit_PrefillsOnlyNets()
        {
            var reply = "Какой режим Anwis использовать для двух сеток 1272×608? ББ60, ББ70, ПП, Проём или Габарит?\n\n" +
                        "Также: что сделать со стеклопакетом (СП 615×1303)? Добавить как «Свой товар» или только просчитать сетки?";
            var form = new MosquitoNetCalculator.Models.AiClarificationForm(null, null);
            AiClarificationPrefill.FromReply(form, reply);

            Assert.Equal("Anwis", form.SelectedType);
            Assert.Single(form.SizeRows);
            Assert.Equal("1272", form.SizeRows[0].WidthText);
            Assert.Equal("608", form.SizeRows[0].HeightText);
            Assert.Equal("2", form.SizeRows[0].QuantityText);
        }
    }
}
