using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Converts a FlowDocument КП into a FixedDocument ready to print,
    /// with per-page header/footer.
    /// </summary>
    public static class FixedDocumentBuilder
    {
        /// <summary>
        /// Renders the source <paramref name="sourceDoc"/> to a <see cref="FixedDocument"/>
        /// with header («Договор №») and footer («Страница X из Y», date).
        /// Applies <see cref="PrintSettings"/> for range, copies, and collation.
        /// </summary>
        public static FixedDocument Build(
            FlowDocument sourceDoc,
            PrintSettings settings,
            string contractNumber,
            DateTime contractDate)
        {
            if (sourceDoc == null) throw new ArgumentNullException(nameof(sourceDoc));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var originalBackground = sourceDoc.Background;
            sourceDoc.Background = Brushes.White;

            try
            {
                return BuildCore(sourceDoc, settings, contractNumber, contractDate, originalBackground);
            }
            finally
            {
                sourceDoc.Background = originalBackground;
            }
        }

        private static FixedDocument BuildCore(
            FlowDocument sourceDoc,
            PrintSettings settings,
            string contractNumber,
            DateTime contractDate,
            Brush originalBackground)
        {
            var rawPaginator = ((IDocumentPaginatorSource)sourceDoc).DocumentPaginator;

            rawPaginator.ComputePageCount();
            while (!rawPaginator.IsPageCountValid)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
            }

            int sourceCount = rawPaginator.PageCount;
            if (sourceCount == 0)
                throw new InvalidOperationException("Source document has no pages.");

            var selectedPages = GetSelectedSourcePages(settings, sourceCount);

            const int dpi = 300;
            double dpiScale = dpi / 96.0;
            Size pageSizeDip = new Size(sourceDoc.PageWidth, sourceDoc.PageHeight);
            int pageWidthPx = (int)Math.Round(pageSizeDip.Width * dpiScale);
            int pageHeightPx = (int)Math.Round(pageSizeDip.Height * dpiScale);

            var sourceBitmaps = new Dictionary<int, BitmapSource>();
            foreach (int srcPageIdx in selectedPages)
            {
                var page = rawPaginator.GetPage(srcPageIdx);
                int w = Math.Max(1, pageWidthPx);
                int h = Math.Max(1, pageHeightPx);
                var rtb = new RenderTargetBitmap(w, h, dpi, dpi, PixelFormats.Pbgra32);
                rtb.Render(page.Visual);
                rtb.Freeze();
                sourceBitmaps[srcPageIdx] = rtb;
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            }

            int copies = Math.Max(0, settings.Copies);
            bool collated = settings.Collated;
            // «Копии» — ЧИСТЫЕ листы заказчика (без печати). 0 допустимо, если
            // включена отдельная копия «В производство» (тогда печатается только она).
            var orderedPages = ComputeOutputOrder(selectedPages, copies, collated);
            int cleanCount = orderedPages.Count;
            bool includeProductionCopy = settings.IncludeProductionCopy;
            // Копия «В производство» — ВСЕГДА ОДНА (один комплект документа) и идёт
            // отдельным блоком после чистых листов заказчика, независимо от счётчика копий.
            int productionCount = includeProductionCopy ? selectedPages.Count : 0;
            int total = cleanCount + productionCount;
            if (total == 0)
                throw new InvalidOperationException("Nothing to print: set at least one copy or enable the production copy.");

            var fixedDoc = new FixedDocument();
            for (int outputIdx = 0; outputIdx < total; outputIdx++)
            {
                bool isProduction = includeProductionCopy && outputIdx >= cleanCount;
                int srcPageIdx;
                int productionPageIdx = -1;
                if (isProduction)
                {
                    productionPageIdx = outputIdx - cleanCount;
                    srcPageIdx = selectedPages[productionPageIdx];
                }
                else
                {
                    srcPageIdx = orderedPages[outputIdx];
                }

                var bitmap = sourceBitmaps[srcPageIdx];
                var fp = BuildFixedPage(
                    bitmap, pageSizeDip,
                    contractNumber ?? string.Empty, contractDate,
                    outputIdx + 1, total,
                    includeProductionStamp: isProduction && productionPageIdx == 0);
                // БЕЗ Measure/Arrange XPS-сериализация (writer.Write в
                // PrintQueueManager.SendToQueue) теряет позиции Canvas.Left/Top:
                // штамп «В ПРОИЗВОДСТВО» и колонтитулы уезжают в (0,0) на бумаге,
                // хотя предпросмотр (живое дерево WPF) показывает верно.
                // Arrange фиксирует смещения визуального дерева — см. тест
                // StampXpsSerializationTests (Viewport ImageBrush штампа в .fpage).
                fp.Measure(pageSizeDip);
                fp.Arrange(new Rect(new Point(0, 0), pageSizeDip));
                var pc = new PageContent();
                pc.Child = fp;
                fixedDoc.Pages.Add(pc);
            }

            sourceDoc.Background = originalBackground;
            return fixedDoc;
        }

        // Выбор страниц (All/Single/Range) вынесен в общий хелпер PageSelection —
        // та же семантика, что и в PDF-экспорте (PdfExportService), чтобы оба канала
        // никогда не расходились в том, какие страницы попадают в вывод.
        private static List<int> GetSelectedSourcePages(PrintSettings settings, int sourceCount)
            => PageSelection.GetSelectedSourcePages(settings, sourceCount);

        private static List<int> ComputeOutputOrder(List<int> sourcePages, int copies, bool collated)
        {
            var result = new List<int>(sourcePages.Count * copies);
            if (collated)
            {
                for (int c = 0; c < copies; c++) result.AddRange(sourcePages);
            }
            else
            {
                foreach (var src in sourcePages)
                    for (int c = 0; c < copies; c++)
                        result.Add(src);
            }
            return result;
        }

        private static FixedPage BuildFixedPage(
            BitmapSource pageBitmap,
            Size pageSizeDip,
            string contractNumber,
            DateTime contractDate,
            int currentPageNumber,
            int totalPageCount,
            bool includeProductionStamp = false)
        {
            double pageWidthDip = pageSizeDip.Width;
            double pageHeightDip = pageSizeDip.Height;

            var fp = new FixedPage
            {
                Width = pageWidthDip,
                Height = pageHeightDip,
                Background = Brushes.White,
            };

            var pageImage = new Image
            {
                Source = pageBitmap,
                Width = pageWidthDip,
                Height = pageHeightDip,
                Stretch = Stretch.Fill,
            };
            RenderOptions.SetBitmapScalingMode(pageImage, BitmapScalingMode.Linear);
            RenderOptions.SetEdgeMode(pageImage, EdgeMode.Aliased);
            fp.Children.Add(pageImage);
            Canvas.SetLeft(pageImage, 0);
            Canvas.SetTop(pageImage, 0);

            if (includeProductionStamp)
                AddProductionStamp(fp, pageWidthDip, pageHeightDip);

            var grayBrush = MakeFrozenGrayBrush(0x88);
            const double rightMarginDip = 30;
            const double defaultFontSizeDip = 7.0;

            if (!string.IsNullOrWhiteSpace(contractNumber))
            {
                var header = new TextBlock
                {
                    Text = $"Договор № {contractNumber}",
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = PtToDip(defaultFontSizeDip),
                    FontStyle = FontStyles.Italic,
                    Foreground = grayBrush,
                };
                fp.Children.Add(header);
                double headerTextWidthDip = MeasureTextWidthDip(
                    header.Text, header.FontSize, header.FontStyle);
                double headerX = Math.Max(rightMarginDip,
                    pageWidthDip - rightMarginDip - headerTextWidthDip);
                // RenderTransform вместо Canvas.Left/Top — см. комментарий в AddProductionStamp.
                header.RenderTransform = new TranslateTransform(headerX, 14);
            }

            var pageFooter = new TextBlock
            {
                Text = $"Страница {currentPageNumber} из {totalPageCount}",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = PtToDip(defaultFontSizeDip),
                FontStyle = FontStyles.Italic,
                Foreground = grayBrush,
            };
            fp.Children.Add(pageFooter);
            // RenderTransform вместо Canvas.Left/Top — см. комментарий в AddProductionStamp.
            pageFooter.RenderTransform = new TranslateTransform(rightMarginDip, pageHeightDip - 30);

            if (contractDate != default)
            {
                var dateFooter = new TextBlock
                {
                    Text = contractDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = PtToDip(defaultFontSizeDip),
                    FontStyle = FontStyles.Italic,
                    Foreground = grayBrush,
                };
                fp.Children.Add(dateFooter);
                double dateTextWidthDip = MeasureTextWidthDip(
                    dateFooter.Text, dateFooter.FontSize, dateFooter.FontStyle);
                double dateX = Math.Max(0,
                    pageWidthDip - rightMarginDip - dateTextWidthDip);
                // RenderTransform вместо Canvas.Left/Top — см. комментарий в AddProductionStamp.
                dateFooter.RenderTransform = new TranslateTransform(dateX, pageHeightDip - 30);
            }

            return fp;
        }

        private static void AddProductionStamp(FixedPage page, double pageWidthDip, double pageHeightDip)
        {
            if (!ProductionStampImage.TryGetPath(out var path))
            {
                Debug.WriteLine("[FixedDocumentBuilder] Production stamp image not found.");
                return;
            }

            var image = new Image
            {
                Source = new BitmapImage(new Uri(path, UriKind.Absolute)),
                Width = ProductionStampImage.WidthDip,
                Height = ProductionStampImage.HeightDip,
                Stretch = Stretch.Fill
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            page.Children.Add(image);
            // Левый край печати — ровно на левом поле КП (16 мм), как и PDF/предпросмотр:
            // оттиск и контент начинаются на одной вертикальной линии. Верх — в свободной
            // зоне над заголовком (top margin = 30 мм, печать 132 DIP ≈ 35 мм — влезает).
            // Позиция — через RenderTransform, а НЕ Canvas.Left/Top: отсоединённый
            // FixedPage не применяет Canvas-смещения при Arrange, и XPS-сериализация
            // (writer.Write в PrintQueueManager) уводит элемент в (0,0) — на бумаге
            // штамп уезжал за край листа при верном предпросмотре. RenderTransform
            // сериализуется в XPS всегда (матрица) — см. StampXpsSerializationTests.
            image.RenderTransform = new TranslateTransform(
                ProductionStampImage.LeftOffsetDip, ProductionStampImage.TopOffsetDip);
        }

        private static double PtToDip(double pt) => pt * 96.0 / 72.0;

        private static SolidColorBrush MakeFrozenGrayBrush(byte gray)
        {
            var brush = new SolidColorBrush(Color.FromRgb(gray, gray, gray));
            brush.Freeze();
            return brush;
        }

        private static double MeasureTextWidthDip(string text, double fontSizeDip, FontStyle style)
        {
            var typeface = new Typeface(
                new FontFamily("Segoe UI"),
                style,
                FontWeights.Normal,
                FontStretches.Normal);
            return new FormattedText(
                text ?? string.Empty,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSizeDip,
                Brushes.Black,
                pixelsPerDip: 1.0).Width;
        }
    }
}
