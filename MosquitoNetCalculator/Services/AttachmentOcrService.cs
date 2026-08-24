using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Tesseract;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Extracts text from images. Primary engine: <c>Windows.Media.Ocr</c>
    /// (fast, uses the OS language packs). Fallback engine: bundled Tesseract
    /// with rus/eng tessdata shipped next to the exe — so OCR keeps working
    /// even on machines that have no Windows OCR language pack installed.
    /// Used so the clarification card can be pre-filled from the *contents* of
    /// an attached screenshot, not just the file name — managers often paste
    /// «Снимок.PNG» whose name carries nothing but whose pixels do.
    /// </summary>
    internal static class AttachmentOcrService
    {
        /// <summary>
        /// Runs OCR on <paramref name="imageBytes"/>. Returns the recognized
        /// text (joined lines, trimmed) plus, on failure, a human-readable
        /// reason («no language pack» vs «no text found» vs «decode error»).
        /// The function never throws.
        /// </summary>
        public static async Task<OcrExtractResult> ExtractAsync(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                return new OcrExtractResult(string.Empty, null);

            // 1) Windows OCR — preferred when a language pack is installed
            //    (fast, native, tuned to the OS languages).
            var windows = await TryWindowsOcrAsync(imageBytes);

            // 2) Bundled Tesseract — covers machines without Windows OCR
            //    language packs, decode errors, and photos Windows reads poorly.
            var tesseract = HasTesseractData
                ? await Task.Run(() => TryTesseract(imageBytes))
                : new OcrExtractResult(string.Empty, TesseractUnavailableReason);

            return CombineResults(windows, tesseract);
        }

        /// <summary>
        /// Merges the two engine outcomes into one result. Pure function so the
        /// fallback policy is unit-testable without invoking either engine.
        /// </summary>
        internal static OcrExtractResult CombineResults(
            OcrExtractResult windows,
            OcrExtractResult tesseract)
        {
            if (!string.IsNullOrWhiteSpace(windows.Text))
            {
                // Windows OCR is the primary source for the complete text. It can,
                // however, lose the separator between two dimensions while the
                // second engine still sees the original `371x1217` token. Merge
                // only that independently verifiable dimension token; never split
                // a compact number such as `3711217` by guessing its boundaries.
                if (!AiKeywordLexicon.DimensionRegex.IsMatch(windows.Text))
                {
                    var dimension = AiKeywordLexicon.DimensionRegex.Match(tesseract.Text ?? string.Empty);
                    if (dimension.Success
                        && ContainsCompactDimension(windows.Text, dimension))
                    {
                        var mergedText = ReplaceCompactDimension(windows.Text, dimension);
                        return new OcrExtractResult(mergedText, null);
                    }
                }

                return windows;
            }

            if (!string.IsNullOrWhiteSpace(tesseract.Text))
                return tesseract;

            // Tesseract actually ran (engine + data present) and read nothing —
            // the photo simply has no readable text; don't blame the Windows
            // language pack for that.
            if (tesseract.FailureReason == null)
                return new OcrExtractResult(string.Empty, NoTextFoundReason);

            // Tesseract was unavailable → surface the Windows failure reason
            // (usually the actionable "install a language pack" hint).
            if (windows.FailureReason != null)
                return windows;

            return new OcrExtractResult(string.Empty, NoTextFoundReason);
        }

        /// <summary>
        /// Confirms that the explicit pair from the second OCR engine corresponds
        /// to the compact numeric token from the primary engine. Whitespace is
        /// allowed between the numbers, but unrelated digits are not accepted.
        /// </summary>
        private static bool ContainsCompactDimension(
            string windowsText,
            Match explicitDimension)
        {
            return TryGetCompactDimensionRegex(explicitDimension, out var regex)
                && regex.IsMatch(windowsText ?? string.Empty);
        }

        /// <summary>Replaces the verified compact token in place, preserving text order.</summary>
        private static string ReplaceCompactDimension(string windowsText, Match explicitDimension)
        {
            if (!TryGetCompactDimensionRegex(explicitDimension, out var regex))
                return windowsText;

            return regex.Replace(windowsText, explicitDimension.Value, count: 1);
        }

        private static bool TryGetCompactDimensionRegex(Match explicitDimension, out Regex regex)
        {
            var width = explicitDimension.Groups[1].Value;
            var height = explicitDimension.Groups[2].Value;
            if (string.IsNullOrEmpty(width) || string.IsNullOrEmpty(height))
            {
                regex = null!;
                return false;
            }

            var pattern = $@"(?<!\d){Regex.Escape(width)}\s*{Regex.Escape(height)}(?!\d)";
            regex = new Regex(pattern, RegexOptions.CultureInvariant);
            return true;
        }

        private const string NoTextFoundReason = "Текст на фото не найден.";
        private const string TesseractUnavailableReason = "Встроенный OCR недоступен (tessdata не найден).";

        private static async Task<OcrExtractResult> TryWindowsOcrAsync(byte[] imageBytes)
        {
            try
            {
                var engine = PickEngine();
                if (engine == null)
                    return new OcrExtractResult(
                        string.Empty,
                        "Не установлен языковой пакет OCR (Параметры → Время и язык → Язык).");

                using var ms = new InMemoryRandomAccessStream();
                await ms.WriteAsync(imageBytes.AsBuffer());
                ms.Seek(0);

                var decoder = await BitmapDecoder.CreateAsync(ms);

                // Windows OCR refuses images larger than MaxImageDimension —
                // phone photos easily exceed it and would silently throw.
                // Downscale to the supported size first.
                uint maxDim = OcrEngine.MaxImageDimension;
                var transform = new BitmapTransform();
                if (decoder.PixelWidth > maxDim || decoder.PixelHeight > maxDim)
                {
                    double scale = Math.Min(
                        (double)maxDim / decoder.PixelWidth,
                        (double)maxDim / decoder.PixelHeight);
                    transform.ScaledWidth = (uint)Math.Max(1, decoder.PixelWidth * scale);
                    transform.ScaledHeight = (uint)Math.Max(1, decoder.PixelHeight * scale);
                }

                // Bgra8 is the most reliable pixel format for OCR. Fall back to
                // Gray8 for odd formats (indexed/CMYK) that refuse Bgra8.
                SoftwareBitmap? bitmap;
                try
                {
                    bitmap = await decoder.GetSoftwareBitmapAsync(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Ignore,
                        transform,
                        ExifOrientationMode.RespectExifOrientation,
                        ColorManagementMode.DoNotColorManage);
                }
                catch
                {
                    bitmap = await decoder.GetSoftwareBitmapAsync(
                        BitmapPixelFormat.Gray8,
                        BitmapAlphaMode.Ignore,
                        transform,
                        ExifOrientationMode.RespectExifOrientation,
                        ColorManagementMode.DoNotColorManage);
                }

                using (bitmap)
                {
                    var result = await engine.RecognizeAsync(bitmap);
                    var text = (result.Text ?? string.Empty).Trim();
                    return new OcrExtractResult(text, null);
                }
            }
            catch
            {
                return new OcrExtractResult(string.Empty, "Не удалось прочитать изображение.");
            }
        }

        /// <summary>
        /// Picks a Windows OCR engine: the user's own system languages first (most
        /// reliable for their photos, including Cyrillic), then ru/en, then any
        /// recognizer the OS exposes. Returns null when no language pack is
        /// installed at all.
        /// </summary>
        private static OcrEngine? PickEngine()
        {
            var user = OcrEngine.TryCreateFromUserProfileLanguages();
            if (user != null) return user;

            string[] preferred = { "ru-RU", "ru", "en-US", "en" };
            foreach (var tag in preferred)
            {
                var lang = new Language(tag);
                if (!OcrEngine.IsLanguageSupported(lang)) continue;
                var engine = OcrEngine.TryCreateFromLanguage(lang);
                if (engine != null) return engine;
            }

            foreach (var lang in OcrEngine.AvailableRecognizerLanguages)
            {
                var engine = OcrEngine.TryCreateFromLanguage(lang);
                if (engine != null) return engine;
            }

            return null;
        }

        // ── Bundled Tesseract fallback ─────────────────────────────────────

        /// <summary>Folder with the bundled rus/eng traineddata (next to the exe).</summary>
        internal const string TessDataFolderName = "tessdata";

        private static string TessDataPath => Path.Combine(AppContext.BaseDirectory, TessDataFolderName);

        internal static bool HasTesseractData =>
            Directory.Exists(TessDataPath)
            && File.Exists(Path.Combine(TessDataPath, "rus.traineddata"))
            && File.Exists(Path.Combine(TessDataPath, "eng.traineddata"));

        // Engine creation is expensive (~hundreds of ms + native lib load) —
        // create once per process, never dispose (app lifetime).
        private static readonly Lazy<TesseractEngine?> LazyEngine = new(
            CreateEngine,
            LazyThreadSafetyMode.ExecutionAndPublication);

        private static TesseractEngine? CreateEngine()
        {
            try
            {
                return HasTesseractData
                    ? new TesseractEngine(TessDataPath, "rus+eng", EngineMode.Default)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Runs bundled Tesseract. Returns (text, null) when it ran — even if it
        /// found nothing — and (text, reason) only when the engine could not run.
        /// </summary>
        private static OcrExtractResult TryTesseract(byte[] imageBytes)
        {
            var engine = LazyEngine.Value;
            if (engine == null)
                return new OcrExtractResult(string.Empty, TesseractUnavailableReason);

            try
            {
                using var pix = Pix.LoadFromMemory(imageBytes);
                using var page = engine.Process(pix);
                var text = (page.GetText() ?? string.Empty).Trim();
                return new OcrExtractResult(text, null);
            }
            catch
            {
                return new OcrExtractResult(string.Empty, "Не удалось прочитать изображение.");
            }
        }

        /// <summary>
        /// Decodes the base64 part of a <c>data:image/...;base64,…</c> URL back
        /// to raw image bytes. Returns null if the URL is not in the expected
        /// shape or the base64 segment is empty.
        /// </summary>
        public static byte[]? TryDecodeDataUrl(string? dataUrl)
        {
            if (string.IsNullOrWhiteSpace(dataUrl)) return null;
            int comma = dataUrl.IndexOf(',');
            if (comma < 0 || comma == dataUrl.Length - 1) return null;
            string payload = dataUrl[(comma + 1)..];
            // Strip any URL-safe variants the composer might leak in.
            payload = payload.Replace('-', '+').Replace('_', '/');
            try { return Convert.FromBase64String(payload); }
            catch (FormatException) { return null; }
        }
    }

    /// <summary>OCR outcome: recognized text and an optional failure reason.</summary>
    internal sealed class OcrExtractResult
    {
        public string Text { get; }
        public string? FailureReason { get; }

        public OcrExtractResult(string text, string? failureReason)
        {
            Text = text;
            FailureReason = failureReason;
        }
    }
}
