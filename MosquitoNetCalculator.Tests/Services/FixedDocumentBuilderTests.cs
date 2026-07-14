using System;
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
    }
}
