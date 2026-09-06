using System;
using System.IO;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Единая точка доступа к изображению электронной печати «В ПРОИЗВОДСТВО».
    /// Используется и физической печатью (FixedDocumentBuilder), и PDF-экспортом
    /// (PdfExportService), чтобы положение/размер и политика «нет файла» были одинаковыми.
    /// </summary>
    public static class ProductionStampImage
    {
        /// <summary>Имя файла печати.</summary>
        public const string FileName = "ВПРОИЗВОДСТВО.png";

        /// <summary>Ширина печати при рендере (DIP) — сохраняет пропорции 2:1 исходника.</summary>
        public const double WidthDip = 264.0;

        /// <summary>Высота печати при рендере (DIP).</summary>
        public const double HeightDip = 132.0;

        /// <summary>Ширина исходника печати (px) — для пересчёта внутренних отступов картинки.</summary>
        internal const double ImageWidthPx = 3620.0;

        /// <summary>
        /// Внутренний прозрачный отступ слева в исходнике печати (px) — видимая красная
        /// рамка начинается не с самого края картинки, а на ~1.3 мм правее. Если ставить
        /// КРАЙ картинки на левое поле КП (16 мм), видимая рамка оказывается чуть правее
        /// контента. Поэтому ниже отступ уменьшается на эту величину, чтобы рамка
        /// начиналась ровно на левой линии контента.
        /// </summary>
        internal const double InnerLeftPadPx = 67.0;
        internal const double InnerLeftPadMm = InnerLeftPadPx * WidthMm / ImageWidthPx; // ≈1.30 мм

        /// <summary>
        /// Горизонтальное смещение левого края печати (DIP): левое поле КП (16 мм) минус
        /// внутренний прозрачный отступ картинки, чтобы видимая рамка оттиска начиналась
        /// на той же вертикальной линии, что и контент (FlowDocumentBuilder.PagePadding.left /
        /// PdfExportService MarginLeft).
        /// </summary>
        public const double LeftOffsetDip = (16.0 - InnerLeftPadMm) * 96.0 / 25.4;

        /// <summary>Горизонтальное смещение левого края печати в PDF (мм) = левое поле КП минус внутренний отступ.</summary>
        public const double LeftOffsetMm = 16.0 - InnerLeftPadMm;

        /// <summary>
        /// Вертикальное смещение верхнего края печати (DIP): печать держится у верхнего
        /// края листа, в свободной зоне НАД заголовком КП (контент начинается с поля
        /// 30 мм, печать высотой ~35 мм в неё не упирается). Единая константа для
        /// физической печати и предпросмотра.
        /// </summary>
        public const double TopOffsetDip = 6.0;

        /// <summary>Вертикальное смещение верхнего края печати в PDF (мм) — то же значение, что TopOffsetDip.</summary>
        public const double TopOffsetMm = TopOffsetDip * 25.4 / 96.0; // ≈1.59 мм

        /// <summary>Ширина печати в PDF (мм).</summary>
        public const double WidthMm = 70.0;

        /// <summary>Высота печати в PDF (мм).</summary>
        public const double HeightMm = 35.0;

        /// <summary>
        /// Пытается найти файл печати. Сначала ищет рядом с exe в подпапке
        /// <c>docs/images</c>, затем в корне папки приложения (флэт-копия при сборке).
        /// </summary>
        public static bool TryGetPath(out string path)
        {
            path = Path.Combine(AppContext.BaseDirectory, "docs", "images", FileName);
            if (File.Exists(path)) return true;

            path = Path.Combine(AppContext.BaseDirectory, FileName);
            return File.Exists(path);
        }

        /// <summary>Читает байты изображения печати. Возвращает null, если файл недоступен.</summary>
        public static byte[]? TryLoadBytes()
        {
            if (!TryGetPath(out var path)) return null;
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProductionStampImage] load failed: {ex.Message}");
                return null;
            }
        }
    }
}
