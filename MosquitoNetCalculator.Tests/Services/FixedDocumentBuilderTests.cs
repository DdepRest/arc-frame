using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Documents;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class FixedDocumentBuilderTests
    {
        [Fact]
        public void Build_ThrowsArgumentNullException_WhenSourceDocIsNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                FixedDocumentBuilder.Build(null!, new PrintSettings(), "1-1", DateTime.Now));
        }

        [Fact]
        public void Build_ThrowsArgumentNullException_WhenSettingsIsNull()
        {
            var doc = new FlowDocument();
            Assert.Throws<ArgumentNullException>(() =>
                FixedDocumentBuilder.Build(doc, null!, "1-1", DateTime.Now));
        }

        [Fact]
        public void Build_ReturnsFixedDocument_WithSinglePageAllMode()
        {
            var result = WpfTestHelper.RunOnSta(() =>
            {
                var source = new FlowDocument(new Paragraph(new Run("Test")))
                {
                    PageWidth = 793.7,
                    PageHeight = 1122.5,
                    PagePadding = new System.Windows.Thickness(30)
                };
                var settings = new PrintSettings { Pages = PageMode.All, Copies = 1 };
                return FixedDocumentBuilder.Build(source, settings, "1-1", DateTime.Now);
            });

            Assert.NotNull(result);
            Assert.True(result.Pages.Count > 0);
        }

        [Fact]
        public void Build_HonorsRangeMode()
        {
            var (allCount, rangeCount) = WpfTestHelper.RunOnSta(() =>
            {
                var source = new FlowDocument();
                for (int i = 0; i < 20; i++)
                    source.Blocks.Add(new Paragraph(new Run($"Paragraph {i}")));
                // Small page height forces pagination regardless of font metrics.
                source.PageWidth = 400;
                source.PageHeight = 120;
                source.PagePadding = new System.Windows.Thickness(10);

                var allSettings = new PrintSettings { Pages = PageMode.All, Copies = 1 };
                var allDoc = FixedDocumentBuilder.Build(source, allSettings, "1-1", DateTime.Now);

                var rangeSettings = new PrintSettings { Pages = PageMode.Range, PageFrom = 1, PageTo = 1, Copies = 1 };
                var rangeDoc = FixedDocumentBuilder.Build(source, rangeSettings, "1-1", DateTime.Now);

                return (allDoc.Pages.Count, rangeDoc.Pages.Count);
            });

            Assert.True(allCount > 1, "All mode should produce multiple pages for a long document.");
            Assert.True(rangeCount < allCount, "Range mode should produce fewer pages than All mode.");
        }

        [Fact]
        public void Build_HonorsCopiesAndCollated()
        {
            var result = WpfTestHelper.RunOnSta(() =>
            {
                var source = new FlowDocument(new Paragraph(new Run("Test")))
                {
                    PageWidth = 793.7,
                    PageHeight = 1122.5,
                    PagePadding = new System.Windows.Thickness(30)
                };
                var settings = new PrintSettings { Pages = PageMode.All, Copies = 2, Collated = false };
                return FixedDocumentBuilder.Build(source, settings, "1-1", DateTime.Now);
            });

            Assert.NotNull(result);
            Assert.Equal(2, result.Pages.Count);
        }

        // ─── Производственная копия («В ПРОИЗВОДСТВО») ───────────────

        [Fact]
        public void Build_ProductionCopy_DoublesPageCount()
        {
            var result = WpfTestHelper.RunOnSta(() =>
            {
                var source = new FlowDocument(new Paragraph(new Run("Test")))
                {
                    PageWidth = 793.7,
                    PageHeight = 1122.5,
                    PagePadding = new System.Windows.Thickness(30)
                };
                var settings = new PrintSettings { Pages = PageMode.All, Copies = 1, IncludeProductionCopy = true };
                return FixedDocumentBuilder.Build(source, settings, "1-1", DateTime.Now);
            });

            Assert.NotNull(result);
            // Один исходный лист → копия заказчика (1) + копия в производство (1) = 2.
            Assert.Equal(2, result.Pages.Count);
        }

        [Fact]
        public void Build_ProductionCopy_WithoutFlag_SinglePageCount()
        {
            var result = WpfTestHelper.RunOnSta(() =>
            {
                var source = new FlowDocument(new Paragraph(new Run("Test")))
                {
                    PageWidth = 793.7,
                    PageHeight = 1122.5,
                    PagePadding = new System.Windows.Thickness(30)
                };
                var settings = new PrintSettings { Pages = PageMode.All, Copies = 1, IncludeProductionCopy = false };
                return FixedDocumentBuilder.Build(source, settings, "1-1", DateTime.Now);
            });

            Assert.NotNull(result);
            Assert.Equal(1, result.Pages.Count);
        }

        [Fact]
        public void Build_ProductionCopy_CopiesAreClean_PlusOneStamped()
        {
            var (pageCount, clean0, clean1, production) = WpfTestHelper.RunOnSta(() =>
            {
                var source = new FlowDocument(new Paragraph(new Run("Test")))
                {
                    PageWidth = 793.7,
                    PageHeight = 1122.5,
                    PagePadding = new System.Windows.Thickness(30)
                };
                // «Копии» = 2 чистых листа заказчика + 1 производственная копия со штампом.
                var settings = new PrintSettings { Pages = PageMode.All, Copies = 2, IncludeProductionCopy = true };
                var doc = FixedDocumentBuilder.Build(source, settings, "1-1", DateTime.Now);

                var p0 = Assert.IsType<FixedPage>(doc.Pages[0].Child);
                var p1 = Assert.IsType<FixedPage>(doc.Pages[1].Child);
                var p2 = Assert.IsType<FixedPage>(doc.Pages[2].Child);
                return (doc.Pages.Count, CountStampImages(p0.Children), CountStampImages(p1.Children), CountStampImages(p2.Children));
            });

            Assert.Equal(3, pageCount);          // 2 чистых + 1 производственная
            Assert.Equal(0, clean0);               // лист 1 — чистый
            Assert.Equal(0, clean1);               // лист 2 — чистый
            Assert.Equal(1, production);           // лист 3 — производственный, со штампом
        }

        [Fact]
        public void Build_ZeroCopies_WithProductionCopy_OnlyStampedSheet()
        {
            var (pageCount, stamps) = WpfTestHelper.RunOnSta(() =>
            {
                var source = new FlowDocument(new Paragraph(new Run("Test")))
                {
                    PageWidth = 793.7,
                    PageHeight = 1122.5,
                    PagePadding = new System.Windows.Thickness(30)
                };
                // 0 чистых листов + включена копия «В производство» → печатается только она (1 лист).
                var settings = new PrintSettings { Pages = PageMode.All, Copies = 0, IncludeProductionCopy = true };
                var doc = FixedDocumentBuilder.Build(source, settings, "1-1", DateTime.Now);
                var page = Assert.IsType<FixedPage>(doc.Pages[0].Child);
                return (doc.Pages.Count, CountStampImages(page.Children));
            });

            Assert.Equal(1, pageCount);
            Assert.Equal(1, stamps);
        }

        [Fact]
        public void Build_ZeroCopies_WithoutProductionCopy_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => WpfTestHelper.RunOnSta(() =>
            {
                var source = new FlowDocument(new Paragraph(new Run("Test")))
                {
                    PageWidth = 793.7,
                    PageHeight = 1122.5,
                    PagePadding = new System.Windows.Thickness(30)
                };
                var settings = new PrintSettings { Pages = PageMode.All, Copies = 0, IncludeProductionCopy = false };
                return FixedDocumentBuilder.Build(source, settings, "1-1", DateTime.Now);
            }));
        }

        [Fact]
        public void Build_ProductionCopy_StampsOnlyFirstPageOfSecondSet()
        {
            var (pageCount, count0, count1) = WpfTestHelper.RunOnSta(() =>
            {
                var source = new FlowDocument(new Paragraph(new Run("Test")))
                {
                    PageWidth = 793.7,
                    PageHeight = 1122.5,
                    PagePadding = new System.Windows.Thickness(30)
                };
                var settings = new PrintSettings { Pages = PageMode.All, Copies = 1, IncludeProductionCopy = true };
                var doc = FixedDocumentBuilder.Build(source, settings, "1-1", DateTime.Now);

                var page0 = Assert.IsType<FixedPage>(doc.Pages[0].Child);
                var page1 = Assert.IsType<FixedPage>(doc.Pages[1].Child);
                return (doc.Pages.Count, CountStampImages(page0.Children), CountStampImages(page1.Children));
            });

            Assert.Equal(2, pageCount);

            // Печать только на первой странице производственного комплекта;
            // копия заказчика — без печати.
            Assert.Equal(0, count0);
            Assert.Equal(1, count1);
        }

        [Fact]
        public void Build_ProductionCopy_StampVisibleLeft_AlignsWithContentLeftMargin()
        {
            // ВИДИМАЯ красная рамка печати «В ПРОИЗВОДСТВО» должна начинаться на
            // той же вертикальной линии, что и левый край контента КП (PagePadding.left).
            // Учитываем внутренний прозрачный отступ картинки (67 px из 3620): видимая
            // рамка находится правее края картинки на innerPadDip, поэтому край картинки
            // ставится на (16 мм − innerPad), а видимая рамка попадает ровно на 16 мм.
            var (stampLeft, contentLeft) = WpfTestHelper.RunOnSta(() =>
            {
                var builder = new FlowDocumentBuilder();
                var items = new List<OrderItem>
                {
                    new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1800 }
                };
                var source = builder.Build(items, new ClientInfo { ContractNumber = "1-1" }, 1800, "")!;
                var settings = new PrintSettings { Pages = PageMode.All, Copies = 0, IncludeProductionCopy = true };
                var doc = FixedDocumentBuilder.Build(source, settings, "1-1", DateTime.Now);
                var page = Assert.IsType<FixedPage>(doc.Pages[0].Child);

                double stampLeft = double.NaN;
                foreach (var child in page.Children)
                    if (child is Image img && Math.Abs(img.Width - ProductionStampImage.WidthDip) < 0.5)
                        // Позиция задана через RenderTransform (TranslateTransform), а не
                        // Canvas.Left — иначе XPS-сериализация теряет смещение.
                        stampLeft = (img.RenderTransform as TranslateTransform)?.X ?? Canvas.GetLeft(img);
                return (stampLeft, source.PagePadding.Left);
            });

            Assert.False(double.IsNaN(stampLeft), "Production stamp image not found on the stamped page.");
            double innerPadDip = ProductionStampImage.InnerLeftPadPx * ProductionStampImage.WidthDip / ProductionStampImage.ImageWidthPx;
            double visibleLeft = stampLeft + innerPadDip;
            // Видимая рамка печати должна совпасть с левым краем контента (точность 1 DIP).
            Assert.Equal(contentLeft, visibleLeft, precision: 1);
        }

        private static int CountStampImages(UIElementCollection children)
        {
            int count = 0;
            foreach (var child in children)
                if (child is Image img && Math.Abs(img.Width - ProductionStampImage.WidthDip) < 0.5)
                    count++;
            return count;
        }
    }
}
