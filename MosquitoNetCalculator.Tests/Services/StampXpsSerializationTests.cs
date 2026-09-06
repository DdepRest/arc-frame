using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Xps;
using System.Windows.Xps.Packaging;
using System.Xml.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Регрессионный тест физической печати: документ сериализуется через
    /// XpsDocumentWriter (тот же путь, что PrintQueueManager.SendToQueue),
    /// затем из сериализованного .fpage читается Viewport ImageBrush'а штампа
    /// «В ПРОИЗВОДСТВО» — прямоугольник, который уходит драйверу принтера.
    /// В XPS разметке Image превращается в Path с ImageBrush, поэтому
    /// позицию читаем именно из атрибута Viewport. Сдвиг на бумаге при
    /// верном XPS — проблема драйвера/принтера, не документа.
    /// </summary>
    public class StampXpsSerializationTests
    {
        [Fact]
        public void XpsRoundTrip_PreservesStampPositionAndPageSize()
        {
            string xpsPath = Path.Combine(Path.GetTempPath(), $"stamp-xps-{Guid.NewGuid():N}.xps");
            try
            {
                WpfTestHelper.RunOnSta(() =>
                {
                    var builder = new FlowDocumentBuilder();
                    var items = new List<OrderItem>
                    {
                        new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1800 }
                    };
                    var source = builder.Build(items, new ClientInfo { ContractNumber = "1-1" }, 1800, "")!;
                    var settings = new PrintSettings { Pages = PageMode.All, Copies = 0, IncludeProductionCopy = true };
                    var doc = FixedDocumentBuilder.Build(source, settings, "1-1", DateTime.Now);

                    // Санити: позиция штампа в дереве задана RenderTransform (TranslateTransform).
                    var stampPage = (FixedPage)((PageContent)doc.Pages[doc.Pages.Count - 1]).Child;
                    var stampImg = stampPage.Children.OfType<Image>().First(i => Math.Abs(i.Width - ProductionStampImage.WidthDip) < 0.5);
                    var tf = Assert.IsType<TranslateTransform>(stampImg.RenderTransform);
                    Assert.Equal(ProductionStampImage.LeftOffsetDip, tf.X, 1);
                    Assert.Equal(ProductionStampImage.TopOffsetDip, tf.Y, 1);

                    // Сериализация тем же API, что и физическая печать
                    // (PrintQueue.CreateXpsDocumentWriter → writer.Write(paginator, ticket)).
                    var writeDoc = new XpsDocument(xpsPath, FileAccess.ReadWrite);
                    try
                    {
                        var writer = XpsDocument.CreateXpsDocumentWriter(writeDoc);
                        writer.Write(doc.DocumentPaginator);
                    }
                    finally { writeDoc.Close(); }
                    return true;
                });

                // Разбираем .fpage последней страницы (= производственная копия со штампом).
                string fpage = ReadLastFpage(xpsPath, out string pageXml);

                // В XPS-разметке Image превращается в Path (Fill=ImageBrush), а позиция
                // переносится в атрибут RenderTransform (матрица "1,0,0,1,tx,ty").
                // Именно эта матрица уходит драйверу принтера — читаем её.
                var stampRects = XDocument.Parse(pageXml)
                    .Descendants()
                    .Where(e => e.Name.LocalName == "Path")
                    .Select(e => new { Data = (string?)e.Attribute("Data"), Tf = (string?)e.Attribute("RenderTransform") })
                    .Where(p => p.Tf != null && IsRectOf(p.Data, ProductionStampImage.WidthDip, ProductionStampImage.HeightDip))
                    .ToList();

                Assert.True(stampRects.Count == 1,
                    "Expected exactly one stamp Path (264x132 with RenderTransform) in serialized XPS, found " + stampRects.Count);

                var (tx, ty) = ParseTranslate(stampRects[0].Tf!);

                // Позиция в XPS обязана совпасть с константами исходника (допуск 0.1 DIP).
                Assert.Equal(ProductionStampImage.LeftOffsetDip, tx, precision: 1);
                Assert.Equal(ProductionStampImage.TopOffsetDip, ty, precision: 1);
                Assert.Equal(793.7, GetPageWidthDip(pageXml), precision: 1);
                Assert.Equal(1122.5, GetPageHeightDip(pageXml), precision: 1);
            }
            finally
            {
                if (File.Exists(xpsPath)) File.Delete(xpsPath);
            }
        }

        private static string ReadLastFpage(string xpsPath, out string pageXml)
        {
            using var zip = ZipFile.OpenRead(xpsPath);
            var entry = zip.Entries
                .Where(e => e.FullName.EndsWith(".fpage", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                .Last();
            using var reader = new StreamReader(entry.Open());
            pageXml = reader.ReadToEnd();
            return entry.FullName;
        }

        /// <summary>Проверяет, что Data — прямоугольник заданного размера ("M0,0Lw,0 w,h 0,hZ").</summary>
        private static bool IsRectOf(string? data, double width, double height)
        {
            if (string.IsNullOrEmpty(data)) return false;
            var nums = new List<double>();
            foreach (var token in System.Text.RegularExpressions.Regex.Matches(data, @"-?\d+(?:\.\d+)?").Cast<System.Text.RegularExpressions.Match>())
                nums.Add(double.Parse(token.Value, System.Globalization.CultureInfo.InvariantCulture));
            if (nums.Count < 8 || nums.Count % 2 != 0) return false;
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            for (int i = 0; i < nums.Count; i += 2)
            {
                minX = Math.Min(minX, nums[i]); maxX = Math.Max(maxX, nums[i]);
                minY = Math.Min(minY, nums[i + 1]); maxY = Math.Max(maxY, nums[i + 1]);
            }
            return Math.Abs(minX) < 0.01 && Math.Abs(minY) < 0.01 &&
                   Math.Abs(maxX - width) < 0.5 && Math.Abs(maxY - height) < 0.5;
        }

        /// <summary>Читает tx/ty из матрицы RenderTransform "1,0,0,1,tx,ty".</summary>
        private static (double Tx, double Ty) ParseTranslate(string matrix)
        {
            var parts = matrix.Split(',');
            return (
                double.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture));
        }

        private static double GetPageWidthDip(string pageXml)
        {
            var doc = XDocument.Parse(pageXml);
            var fp = doc.Descendants().First(e => e.Name.LocalName == "FixedPage");
            return ParseDip((string?)fp.Attribute("Width"));
        }

        private static double GetPageHeightDip(string pageXml)
        {
            var doc = XDocument.Parse(pageXml);
            var fp = doc.Descendants().First(e => e.Name.LocalName == "FixedPage");
            return ParseDip((string?)fp.Attribute("Height"));
        }

        private static double ParseDip(string? value)
        {
            // Значения в .fpage вида "793.7" или "793.77777777777778".
            return double.Parse(value!.Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
