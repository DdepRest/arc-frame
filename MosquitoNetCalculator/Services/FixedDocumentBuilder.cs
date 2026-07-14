using System;
using System.Collections.Generic;
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

            int copies = Math.Max(1, settings.Copies);
            bool collated = settings.Collated;
            var orderedPages = ComputeOutputOrder(selectedPages, copies, collated);
            int total = orderedPages.Count;

            var fixedDoc = new FixedDocument();
            for (int outputIdx = 0; outputIdx < total; outputIdx++)
            {
                int srcPageIdx = orderedPages[outputIdx];
                var bitmap = sourceBitmaps[srcPageIdx];
                var fp = BuildFixedPage(
                    bitmap, pageSizeDip,
                    contractNumber ?? string.Empty, contractDate,
                    outputIdx + 1, total);
                var pc = new PageContent();
                pc.Child = fp;
                fixedDoc.Pages.Add(pc);
            }

            sourceDoc.Background = originalBackground;
            return fixedDoc;
        }

        private static List<int> GetSelectedSourcePages(PrintSettings settings, int sourceCount)
        {
            switch (settings.Pages)
            {
                case PageMode.Single:
                {
                    int p = Math.Clamp(settings.SinglePage - 1, 0, sourceCount - 1);
                    return new List<int> { p };
                }
                case PageMode.Range:
                {
                    int from = Math.Clamp(settings.PageFrom - 1, 0, sourceCount - 1);
                    int to = Math.Clamp(settings.PageTo - 1, 0, sourceCount - 1);
                    if (from > to) (from, to) = (to, from);
                    var list = new List<int>(to - from + 1);
                    for (int i = from; i <= to; i++) list.Add(i);
                    return list;
                }
                case PageMode.All:
                default:
                {
                    var list = new List<int>(sourceCount);
                    for (int i = 0; i < sourceCount; i++) list.Add(i);
                    return list;
                }
            }
        }

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
            int totalPageCount)
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
                Canvas.SetLeft(header, headerX);
                Canvas.SetTop(header, 14);
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
            Canvas.SetLeft(pageFooter, rightMarginDip);
            Canvas.SetTop(pageFooter, pageHeightDip - 30);

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
                Canvas.SetLeft(dateFooter, dateX);
                Canvas.SetTop(dateFooter, pageHeightDip - 30);
            }

            return fp;
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
