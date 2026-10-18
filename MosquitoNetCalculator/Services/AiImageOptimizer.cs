using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MosquitoNetCalculator.Services
{
    public sealed record AiOptimizedImageResult(
        string DataUrl,
        byte[] Bytes,
        int Width,
        int Height,
        string SizeLabel);

    /// <summary>
    /// Оптимизатор изображений для AI ассистента.
    /// Выполняет downscale больших изображений (до max 1600px) и сжатие в JPEG (85%),
    /// чтобы уменьшить размер полезной нагрузки с 5-15 МБ до 100-250 КБ.
    /// Это ускоряет передачу по сети в десятки раз и устраняет таймауты Vision-моделей.
    /// </summary>
    public static class AiImageOptimizer
    {
        public const int MaxDimension = 1600;
        public const int JpegQuality = 85;

        /// <summary>
        /// Оптимизирует сырые байты изображения и возвращает результат с DataUrl.
        /// </summary>
        public static AiOptimizedImageResult Optimize(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                throw new ArgumentException("Массив байтов изображения пуст.", nameof(imageBytes));

            using var inStream = new MemoryStream(imageBytes);
            var decoder = BitmapDecoder.Create(
                inStream,
                BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);

            var frame = decoder.Frames[0];
            int origW = frame.PixelWidth;
            int origH = frame.PixelHeight;

            BitmapSource source = frame;

            // Если размер превышает MaxDimension — пропорционально масштабируем
            if (origW > MaxDimension || origH > MaxDimension)
            {
                double scale = Math.Min((double)MaxDimension / origW, (double)MaxDimension / origH);
                var transform = new ScaleTransform(scale, scale);
                source = new TransformedBitmap(frame, transform);
            }

            int finalW = source.PixelWidth;
            int finalH = source.PixelHeight;

            using var outStream = new MemoryStream();
            var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(source));
            encoder.Save(outStream);

            byte[] outBytes = outStream.ToArray();
            string base64 = Convert.ToBase64String(outBytes);
            string dataUrl = "data:image/jpeg;base64," + base64;
            string sizeLabel = FormatSize(outBytes.Length);

            return new AiOptimizedImageResult(dataUrl, outBytes, finalW, finalH, sizeLabel);
        }

        /// <summary>
        /// Оптимизирует файл по пути и возвращает результат.
        /// </summary>
        public static AiOptimizedImageResult OptimizeFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Файл изображения не найден.", filePath);

            byte[] bytes = File.ReadAllBytes(filePath);
            return Optimize(bytes);
        }

        /// <summary>
        /// Оптимизирует BitmapSource (например, полученный из буфера обмена Clipboard).
        /// </summary>
        public static AiOptimizedImageResult OptimizeBitmap(BitmapSource bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            BitmapSource source = bitmap;
            int origW = bitmap.PixelWidth;
            int origH = bitmap.PixelHeight;

            if (origW > MaxDimension || origH > MaxDimension)
            {
                double scale = Math.Min((double)MaxDimension / origW, (double)MaxDimension / origH);
                var transform = new ScaleTransform(scale, scale);
                source = new TransformedBitmap(bitmap, transform);
            }

            using var outStream = new MemoryStream();
            var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(source));
            encoder.Save(outStream);

            byte[] outBytes = outStream.ToArray();
            string base64 = Convert.ToBase64String(outBytes);
            string dataUrl = "data:image/jpeg;base64," + base64;
            string sizeLabel = FormatSize(outBytes.Length);

            return new AiOptimizedImageResult(dataUrl, outBytes, source.PixelWidth, source.PixelHeight, sizeLabel);
        }

        /// <summary>
        /// Creates a compact Base64 JPEG thumbnail (max 240px) for persisting chat history.
        /// </summary>
        public static string CreateThumbnailBase64(BitmapSource bitmap, int maxDimension = 240)
        {
            if (bitmap == null) return string.Empty;
            try
            {
                int origW = bitmap.PixelWidth;
                int origH = bitmap.PixelHeight;
                BitmapSource source = bitmap;
                if (origW > maxDimension || origH > maxDimension)
                {
                    double scale = Math.Min((double)maxDimension / origW, (double)maxDimension / origH);
                    var transform = new ScaleTransform(scale, scale);
                    source = new TransformedBitmap(bitmap, transform);
                }

                using var outStream = new MemoryStream();
                var encoder = new JpegBitmapEncoder { QualityLevel = 75 };
                encoder.Frames.Add(BitmapFrame.Create(source));
                encoder.Save(outStream);
                return Convert.ToBase64String(outStream.ToArray());
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Creates a compact Base64 JPEG thumbnail directly from a base64 DataUrl string.
        /// </summary>
        public static string CreateThumbnailBase64FromDataUrl(string dataUrl, int maxDimension = 240)
        {
            if (string.IsNullOrWhiteSpace(dataUrl)) return string.Empty;
            try
            {
                var comma = dataUrl.IndexOf(',');
                var raw = comma >= 0 ? dataUrl.Substring(comma + 1) : dataUrl;
                var bytes = Convert.FromBase64String(raw);
                using var inStream = new MemoryStream(bytes);
                var decoder = BitmapDecoder.Create(inStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames.Count > 0 ? decoder.Frames[0] : null;
                if (frame == null) return string.Empty;
                return CreateThumbnailBase64(frame, maxDimension);
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} Б";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} КБ";
            return $"{bytes / (1024.0 * 1024.0):0.##} МБ";
        }
    }
}
