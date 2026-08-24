using System;
using System.IO;
using System.Text;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Models;
using Tesseract;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class AttachmentOcrServiceTests
    {
        [Fact]
        public void TryDecodeDataUrl_HappyPath_RoundTripBytes()
        {
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello"));
            byte[]? got = AttachmentOcrService.TryDecodeDataUrl($"data:text/plain;base64,{b64}");
            Assert.NotNull(got);
            Assert.Equal("hello", Encoding.UTF8.GetString(got!));
        }

        [Fact]
        public void TryDecodeDataUrl_UrlSafeVariants_NormalisesToStandardBase64()
        {
            // Standard base64 'Pz8/' decodes to 0x3F 0x3F 0x3F ('???').
            // Url-safe variant writes '+' and '/' as '-' and '_'; the
            // composer might leak either style, so we normalise on the way
            // in.
            byte[]? got = AttachmentOcrService.TryDecodeDataUrl("data:image/png;base64,Pz8_");
            Assert.NotNull(got);
            Assert.Equal(new byte[] { 0x3F, 0x3F, 0x3F }, got!);
        }

        [Fact]
        public void TryDecodeDataUrl_EmptyOrNull_ReturnsNull()
        {
            Assert.Null(AttachmentOcrService.TryDecodeDataUrl(null));
            Assert.Null(AttachmentOcrService.TryDecodeDataUrl(""));
            Assert.Null(AttachmentOcrService.TryDecodeDataUrl("not a data url"));
        }

        // ─── Fallback policy (CombineResults) ─────────────────────────────

        [Fact]
        public void CombineResults_WindowsText_WinsOverTesseract()
        {
            var windows = new OcrExtractResult("700×1400 белый", null);
            var tesseract = new OcrExtractResult("", null);
            var result = AttachmentOcrService.CombineResults(windows, tesseract);
            Assert.Equal("700×1400 белый", result.Text);
            Assert.Null(result.FailureReason);
        }

        [Fact]
        public void CombineResults_WindowsEmpty_TesseractText_Used()
        {
            var windows = new OcrExtractResult("", "Текст на фото не найден.");
            var tesseract = new OcrExtractResult("739х1116", null);
            var result = AttachmentOcrService.CombineResults(windows, tesseract);
            Assert.Equal("739х1116", result.Text);
            Assert.Null(result.FailureReason);
        }

        [Fact]
        public void CombineResults_WindowsCompactDimension_TesseractExplicitDimension_AppendsVerifiedPair()
        {
            // Windows OCR sometimes returns `3711217`, while Tesseract sees
            // the separator. Use the independently recognized pair, but keep
            // the Windows text (colour/type/count) intact.
            var windows = new OcrExtractResult("ПМС Anwis, бел. 1 3711217", null);
            var tesseract = new OcrExtractResult("ПМС Anwis, бел. 1 371x1217", null);

            var result = AttachmentOcrService.CombineResults(windows, tesseract);

            Assert.Equal("ПМС Anwis, бел. 1 371x1217", result.Text);
            var form = new AiClarificationForm(result.Text);
            Assert.Equal("371", form.WidthText);
            Assert.Equal("1217", form.HeightText);
            Assert.Equal("Белый", form.SelectedColor);
            Assert.Equal("1", form.QuantityText);
        }

        [Fact]
        public void CombineResults_SecondEngineDifferentDimension_DoesNotAppendUnrelatedPair()
        {
            var result = AttachmentOcrService.CombineResults(
                new OcrExtractResult("ПМС Anwis, бел. 1 3711217", null),
                new OcrExtractResult("ПМС Anwis, бел. 1 400x1500", null));

            Assert.DoesNotContain("400x1500", result.Text);
            Assert.Equal("ПМС Anwis, бел. 1 3711217", result.Text);
        }

        [Fact]
        public void CombineResults_CompactDimensionsInBothEngines_DoesNotGuess()
        {
            // `3711217` is deliberately ambiguous. It may represent either
            // orientation (or entirely different values), so no split is safe.
            var result = AttachmentOcrService.CombineResults(
                new OcrExtractResult("ПМС Anwis, бел. 2 шт 3711217", null),
                new OcrExtractResult("ПМС Anwis, бел. 2 шт 3711217", null));

            var form = new AiClarificationForm(result.Text);
            Assert.Equal("", form.WidthText);
            Assert.Equal("", form.HeightText);
            Assert.Equal("Белый", form.SelectedColor);
            Assert.Equal("2", form.QuantityText);
        }

        [Fact]
        public void CombineResults_BothEmpty_TesseractRan_ReportsNoText()
        {
            // Tesseract ran (no failure reason) and found nothing → the photo
            // simply has no readable text; don't blame the Windows language pack.
            var windows = new OcrExtractResult("", "Не установлен языковой пакет OCR (Параметры → Время и язык → Язык).");
            var tesseract = new OcrExtractResult("", null);
            var result = AttachmentOcrService.CombineResults(windows, tesseract);
            Assert.Equal("", result.Text);
            Assert.Equal("Текст на фото не найден.", result.FailureReason);
        }

        [Fact]
        public void CombineResults_BothEmpty_TesseractUnavailable_KeepsWindowsReason()
        {
            // Tesseract couldn't run (no tessdata) → the actionable Windows
            // hint must surface.
            var windows = new OcrExtractResult("", "Не установлен языковой пакет OCR (Параметры → Время и язык → Язык).");
            var tesseract = new OcrExtractResult("", "Встроенный OCR недоступен (tessdata не найден).");
            var result = AttachmentOcrService.CombineResults(windows, tesseract);
            Assert.Equal("", result.Text);
            Assert.Equal("Не установлен языковой пакет OCR (Параметры → Время и язык → Язык).", result.FailureReason);
        }

        [Fact]
        public void CombineResults_BothEmpty_NoReasons_ReportsNoText()
        {
            var result = AttachmentOcrService.CombineResults(
                new OcrExtractResult("", null),
                new OcrExtractResult("", null));
            Assert.Equal("Текст на фото не найден.", result.FailureReason);
        }

        // ─── Bundled Tesseract smoke test (native libs + tessdata) ────────

        [Fact]
        public void TesseractBundledEngine_ReadsFixtureText()
        {
            // Proves the whole bundle works: native leptonica/tesseract50 DLLs
            // load from x64/ and the rus/eng traineddata is present. If either
            // is missing from the output, this fails loudly instead of silently
            // degrading OCR on machines without Windows language packs.
            Assert.True(AttachmentOcrService.HasTesseractData,
                "tessdata must be copied to the output folder");

            string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "ocr-fixture.png");
            Assert.True(File.Exists(fixture), "ocr-fixture.png must be copied to the output folder");
            byte[] bytes = File.ReadAllBytes(fixture);

            string tessData = Path.Combine(AppContext.BaseDirectory, AttachmentOcrService.TessDataFolderName);
            using var engine = new TesseractEngine(tessData, "rus+eng", EngineMode.Default);
            using var pix = Pix.LoadFromMemory(bytes);
            using var page = engine.Process(pix);
            string text = page.GetText() ?? "";

            // Digits OCR reliably; the fixture renders "Anwis 700x1400 4 шт".
            Assert.Contains("700", text);
            Assert.Contains("1400", text);
        }

        [Fact]
        public async System.Threading.Tasks.Task ExtractAsync_BundledEngine_FallsBackAndReadsFixture()
        {
            // End-to-end: the public entry point must return text for a photo
            // regardless of which engine (Windows OCR or bundled Tesseract)
            // ends up handling it.
            string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "ocr-fixture.png");
            if (!File.Exists(fixture)) return;

            byte[] bytes = File.ReadAllBytes(fixture);
            var result = await AttachmentOcrService.ExtractAsync(bytes);

            Assert.True(result.Text.Length > 0, result.FailureReason ?? "no text");
        }
    }
}
