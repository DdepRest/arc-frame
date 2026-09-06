using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class PdfExportServiceTests
    {
        [Fact]
        public void Export_ThrowsArgumentException_WhenFilePathIsEmpty()
        {
            var service = new PdfExportService();
            Assert.Throws<ArgumentException>(() =>
                service.Export("", new List<OrderItem>(), new ClientInfo(), 0, ""));
        }

        [Fact]
        public void Export_CreatesPdfFile_WithValidItems()
        {
            var path = Path.Combine(Path.GetTempPath(), $"arc-test-{Guid.NewGuid()}.pdf");
            try
            {
                var service = new PdfExportService();
                var items = new List<OrderItem>
                {
                    new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1800 }
                };
                service.Export(path, items, new ClientInfo { ContractNumber = "1-1" }, 1800, "");
                Assert.True(File.Exists(path));
                var fileInfo = new FileInfo(path);
                Assert.True(fileInfo.Length > 0);
                var header = File.ReadAllBytes(path).AsSpan(0, Math.Min(4, (int)fileInfo.Length));
                Assert.True(header.SequenceEqual(new byte[] { 0x25, 0x50, 0x44, 0x46 }));
            }
            finally
            {
                try { File.Delete(path); } catch { /* ignore cleanup errors */ }
            }
        }

        [Fact]
        public void Export_WithProductionCopy_CreatesValidPdf()
        {
            var path = Path.Combine(Path.GetTempPath(), $"arc-test-{Guid.NewGuid()}.pdf");
            try
            {
                var service = new PdfExportService();
                var items = new List<OrderItem>
                {
                    new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1800 }
                };
                var settings = new PrintSettings { IncludeProductionCopy = true };
                service.Export(path, items, new ClientInfo { ContractNumber = "1-1" }, 1800, "", attemptSettings: settings);
                Assert.True(File.Exists(path));
                var fileInfo = new FileInfo(path);
                Assert.True(fileInfo.Length > 0);
                var header = File.ReadAllBytes(path).AsSpan(0, Math.Min(4, (int)fileInfo.Length));
                Assert.True(header.SequenceEqual(new byte[] { 0x25, 0x50, 0x44, 0x46 }));
            }
            finally
            {
                try { File.Delete(path); } catch { /* ignore cleanup errors */ }
            }
        }

        /// <summary>
        /// Многостраничное КП + флажок «В производство»: штамп должен быть только на
        /// ПЕРВОМ листе производственного комплекта (как в физической печати и в
        /// предпросмотре), а не повторяться на каждой странице секции (баг page.Foreground()).
        /// </summary>
        [Fact]
        public void Export_WithProductionCopy_Multipage_StampsOnlyFirstPageOfProductionSet()
        {
            var path = Path.Combine(Path.GetTempPath(), $"arc-test-{Guid.NewGuid()}.pdf");
            try
            {
                var service = new PdfExportService();
                var items = new List<OrderItem>();
                for (int i = 0; i < 90; i++)
                {
                    items.Add(new()
                    {
                        Name = $"Окно {i} — длинное наименование для переноса",
                        Color = "Белый", Width = 1000 + i, Height = 1000 + i,
                        Quantity = 1, Price = 1800, Total = 1800
                    });
                }
                var settings = new PrintSettings { IncludeProductionCopy = true, Copies = 1 };
                service.Export(path, items, new ClientInfo { ContractNumber = "1-1" }, 1800 * 90, "", attemptSettings: settings);

                var (totalPages, pagesWithImage) = CountPdfStructure(File.ReadAllBytes(path));
                Assert.True(totalPages > 1, "КП из 90 позиций должен занимать больше одной страницы.");
                Assert.True(pagesWithImage == 1, $"Штамп обязан быть ровно на одной странице, а найден на {pagesWithImage} из {totalPages}.");
            }
            finally
            {
                try { File.Delete(path); } catch { /* ignore cleanup errors */ }
            }
        }

        /// <summary>
        /// PDF-экспорт обязан учитывать режим «Страницы» (Single/Range) так же, как
        /// физическая печать: выбор применяется к ЧИСТОМУ комплекту КП, копии повторяют
        /// выбранный набор, производственная копия идёт целиком в конце.
        /// </summary>
        [Fact]
        public void Export_SinglePage_ExportsOnlyThatCustomerPage_PlusProduction()
        {
            var path = Path.Combine(Path.GetTempPath(), $"arc-test-{Guid.NewGuid()}.pdf");
            try
            {
                var service = new PdfExportService();
                var items = MakeLongItemList(); // многостраничное КП
                var settings = new PrintSettings
                {
                    Copies = 1,
                    IncludeProductionCopy = true,
                    Pages = PageMode.Single,
                    SinglePage = 1,
                };
                service.Export(path, items, new ClientInfo { ContractNumber = "1-1" }, 100, "", attemptSettings: settings);

                var (totalPages, _) = CountPdfStructure(File.ReadAllBytes(path));
                int images = CountImageObjectsFileWide(File.ReadAllBytes(path));
                // 1 чистая стр. (выбрана одна) + весь производственный комплект.
                Assert.True(totalPages >= 2, $"Ожидалось >= 2 страниц (1 чистая + производственный комплект), получено {totalPages}.");
                // Штамп пережил TakePages: image + /SMask = 2 XObject-картинки.
                Assert.True(images >= 2, $"Штамп потерян при извлечении страниц: image-объектов {images}.");
            }
            finally
            {
                try { File.Delete(path); } catch { /* cleanup */ }
            }
        }

        [Fact]
        public void Export_PageRange_ExportsOnlyRange_PlusProduction()
        {
            var path = Path.Combine(Path.GetTempPath(), $"arc-test-{Guid.NewGuid()}.pdf");
            try
            {
                var service = new PdfExportService();
                var items = MakeLongItemList(); // многостраничное КП
                var settings = new PrintSettings
                {
                    Copies = 1,
                    IncludeProductionCopy = true,
                    Pages = PageMode.Range,
                    PageFrom = 1,
                    PageTo = 2,
                };
                service.Export(path, items, new ClientInfo { ContractNumber = "1-1" }, 100, "", attemptSettings: settings);

                var (totalPages, _) = CountPdfStructure(File.ReadAllBytes(path));
                int stampImages = CountImageObjectsFileWide(File.ReadAllBytes(path));
                // Сравниваем с полным экспортом того же КП: чистых страниц должно стать меньше.
                var fullPath = Path.Combine(Path.GetTempPath(), $"arc-test-{Guid.NewGuid()}.pdf");
                try
                {
                    var fullSettings = new PrintSettings { Copies = 1, IncludeProductionCopy = true, Pages = PageMode.All };
                    service.Export(fullPath, items, new ClientInfo { ContractNumber = "1-1" }, 100, "", attemptSettings: fullSettings);
                    var (fullPages, _) = CountPdfStructure(File.ReadAllBytes(fullPath));

                    Assert.True(totalPages < fullPages,
                        $"Range-экспорт ({totalPages} стр.) должен быть меньше полного ({fullPages} стр.).");
                    // Штамп пережил TakePages: image + /SMask = 2 XObject-картинки.
                    Assert.True(stampImages >= 2, $"Штамп потерян при извлечении страниц: image-объектов {stampImages}.");
                }
                finally
                {
                    try { File.Delete(fullPath); } catch { /* cleanup */ }
                }
            }
            finally
            {
                try { File.Delete(path); } catch { /* cleanup */ }
            }
        }

        [Fact]
        public void Export_PageRange_ClampsOutOfRangeValues()
        {
            var path = Path.Combine(Path.GetTempPath(), $"arc-test-{Guid.NewGuid()}.pdf");
            try
            {
                var service = new PdfExportService();
                var items = new List<OrderItem>
                {
                    new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1800 }
                };
                // Диапазон за пределами документа (999..1001) клэмпится к фактическому числу страниц.
                var settings = new PrintSettings
                {
                    Copies = 1,
                    Pages = PageMode.Range,
                    PageFrom = 999,
                    PageTo = 1001,
                };
                service.Export(path, items, new ClientInfo { ContractNumber = "1-1" }, 1800, "", attemptSettings: settings);

                var (totalPages, _) = CountPdfStructure(File.ReadAllBytes(path));
                Assert.True(totalPages >= 1, "Клэмпинг диапазона обязан дать хотя бы одну страницу.");
            }
            finally
            {
                try { File.Delete(path); } catch { /* cleanup */ }
            }
        }

        private static List<OrderItem> MakeLongItemList()
        {
            var items = new List<OrderItem>();
            for (int i = 0; i < 90; i++)
            {
                items.Add(new()
                {
                    Name = $"Окно {i} — длинное наименование для переноса",
                    Color = "Белый", Width = 1000 + i, Height = 1000 + i,
                    Quantity = 1, Price = 1800, Total = 1800
                });
            }
            return items;
        }

        /// <summary>
        /// Считает XObject-картинки (/Subtype /Image) ПО ВСЕМУ файлу.
        /// PNG с прозрачностью даёт пары image + /SMask, поэтому штамп = 2.
        /// После DocumentOperation ресурсы страницы живут в отдельных объектах —
        /// постраничный подсчёт по блоку объекта страницы там не работает.
        /// </summary>
        private static int CountImageObjectsFileWide(byte[] pdfBytes)
        {
            string text = Encoding.Latin1.GetString(pdfBytes);
            return System.Text.RegularExpressions.Regex.Matches(text, @"/Subtype\s*/Image").Count;
        }

        /// <summary>
        /// Считает страницы и страницы с картинками, вырезая блок объекта
        /// «N 0 obj … endobj»: окно не заезжает на соседние объекты PDF.
        /// </summary>
        private static (int totalPages, int pagesWithImage) CountPdfStructure(byte[] pdfBytes)
        {
            string text = Encoding.Latin1.GetString(pdfBytes);
            int totalPages = 0;
            int pagesWithImage = 0;
            foreach (Match pm in Regex.Matches(text, @"/Type\s*/Page[\s>/]"))
            {
                int objStart = text.LastIndexOf("obj", pm.Index, Math.Min(pm.Index, text.Length));
                int blockStart = objStart >= 0 ? text.LastIndexOf('\n', objStart) + 1 : 0;
                int endObj = text.IndexOf("endobj", pm.Index);
                int blockEnd = endObj >= 0 ? endObj : text.Length;
                string block = text.Substring(blockStart, Math.Max(0, blockEnd - blockStart));
                totalPages++;
                if (Regex.IsMatch(block, @"/Subtype\s*/Image") || Regex.IsMatch(block, @"/XObject"))
                    pagesWithImage++;
            }
            return (totalPages, pagesWithImage);
        }

        [Fact]
        public void Export_FormattedNotes_DoesNotThrow()
        {
            var path = Path.Combine(Path.GetTempPath(), $"arc-test-{Guid.NewGuid()}.pdf");
            try
            {
                var service = new PdfExportService();
                var items = new List<OrderItem>
                {
                    new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1800 }
                };
                var client = new ClientInfo
                {
                    ContractNumber = "1-1",
                    Notes = "**важно** *курсив* [color=#D32F2F]красный[/color]\n- пункт"
                };
                service.Export(path, items, client, 1800, "");
                Assert.True(File.Exists(path));
                var fileInfo = new FileInfo(path);
                Assert.True(fileInfo.Length > 0);
            }
            finally
            {
                try { File.Delete(path); } catch { /* ignore cleanup errors */ }
            }
        }

        // ─── Column-width sizing helpers (regression tests) ──────────
        //
        // v3.45: PdfExportService.BuildAdditionalKpPdf switched from a
        // hardcoded ConstantItem(130) to a dynamic measurement of every row's
        // rendered amount width (System.Drawing.Graphics.MeasureString with
        // PageUnit=Point). These tests pin both helpers so future font or
        // column-width changes are caught.

        private static readonly MethodInfo MeasureTextWidthPt =
            typeof(PdfExportService).GetMethod(
                "MeasureTextWidthPt",
                BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("MeasureTextWidthPt not found on PdfExportService.");

        private static readonly MethodInfo ComputeAmountColumnWidth =
            typeof(PdfExportService).GetMethod(
                "ComputeAmountColumnWidth",
                BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ComputeAmountColumnWidth not found on PdfExportService.");

        /// <summary>
        /// Width measurement must run on an STA thread so System.Drawing's
        /// GDI+ measurement of Segoe UI works without Gdip initialization
        /// errors. Production callers (MainWindow, PdfExport from UI) are
        /// already STA; tests need the helper to opt in explicitly.
        /// </summary>
        private static T InvokeSta<T>(Func<T> body) =>
            MosquitoNetCalculator.Tests.Helpers.WpfTestHelper.RunOnSta(body);

        [Fact]
        public void MeasureTextWidthPt_ReturnsPositiveValue()
        {
            InvokeSta<float>(() =>
            {
                float w = (float)MeasureTextWidthPt.Invoke(null, new object?[] { "123,45 руб.", 9f, true })!;
                Assert.True(w > 0, $"Expected positive width, got {w}");
                return w;
            });
        }

        [Fact]
        public void MeasureTextWidthPt_LargerFontProducesWiderResult()
        {
            // Same string at 12pt Bold (grand-total font) must measure wider
            // than at 9pt Bold (regular-row font). If this regresses, the
            // grand-total row would clip before regular rows.
            InvokeSta<bool>(() =>
            {
                const string text = "127 932,98 руб.";
                float w9 = (float)MeasureTextWidthPt.Invoke(null, new object?[] { text, 9f, true })!;
                float w12 = (float)MeasureTextWidthPt.Invoke(null, new object?[] { text, 12f, true })!;
                Assert.True(w12 > w9,
                    $"12pt width ({w12:F2}) must exceed 9pt width ({w9:F2})");
                return w12 > w9;
            });
        }

        [Fact]
        public void MeasureTextWidthPt_LongerTextProducesWiderResult()
        {
            // Width must grow with string length — otherwise the dynamic
            // helper would never widen for long sums. We assert strict
            // monotonicity (longer → wider) but NOT a fixed ratio, because
            // the trailing " руб." adds constant overhead per row regardless
            // of digit count — so a 12-digit number is only ~2-3× wider than
            // a single-digit one, not 12×.
            InvokeSta<bool>(() =>
            {
                float shortW = (float)MeasureTextWidthPt.Invoke(null, new object?[] { "1,00 руб.", 9f, true })!;
                float longW = (float)MeasureTextWidthPt.Invoke(null, new object?[] { "999 999 999,99 руб.", 9f, true })!;
                Assert.True(longW > shortW,
                    $"Long text ({longW:F2}) must exceed short ({shortW:F2})");
                return longW > shortW;
            });
        }

        [Fact]
        public void ComputeAmountColumnWidth_ReturnsWidthAtLeastMaxRowWidth()
        {
            // The returned column must be at least as wide as every row it
            // was sized for. Lazy contract: shouldn't wrap.
            InvokeSta<bool>(() =>
            {
                var rows = new[]
                {
                    (amount: 1000.0,    isGrand: false),
                    (amount: 12_345.67, isGrand: false),
                    (amount: 127_932.98, isGrand: true),
                };
                float width = (float)ComputeAmountColumnWidth.Invoke(null, new object?[] { rows })!;

                // Reproduce exactly what BuildAdditionalKpPdf constructs:
                // $"{MoneyFormatService.Format(amount)} руб." — if we deviate
                // here (e.g. use $":N2") a future MoneyFormatService change
                // could break the contract without any test catching it.
                foreach (var (amount, isGrand) in rows)
                {
                    float fontSize = isGrand ? 12f : 9f;
                    string text = $"{MoneyFormatService.Format(amount)} руб.";
                    float rowW = (float)MeasureTextWidthPt.Invoke(null, new object?[] { text, fontSize, true })!;
                    Assert.True(width >= rowW,
                        $"Column width {width:F2} < row width {rowW:F2} for «{text}» — would wrap!");
                }
                return true;
            });
        }

        [Fact]
        public void ComputeAmountColumnWidth_GrowsWithLongerAmounts()
        {
            // Regression for the "127 932,98 руб." screenshot: a 6-digit
            // sum must WIDEN the column relative to a 4-digit one — and the
            // old hardcoded floor of 130 was an over-estimate (the real
            // rendered width is closer to 100pt). We pin the *direction* of
            // growth, not the magic number.
            InvokeSta<bool>(() =>
            {
                var shortRows = new[]
                {
                    (amount: 99.99,     isGrand: false),
                    (amount: 1099.99,   isGrand: false),
                };
                var longRows = new[]
                {
                    (amount: 99.99,         isGrand: false),
                    (amount: 1099.99,       isGrand: false),
                    (amount: 127_932.98,    isGrand: true),
                };

                float shortW = (float)ComputeAmountColumnWidth.Invoke(null, new object?[] { shortRows })!;
                float longW = (float)ComputeAmountColumnWidth.Invoke(null, new object?[] { longRows })!;

                Assert.True(longW > shortW,
                    $"Adding 6-digit grand total must widen column: short {shortW:F2}, long {longW:F2}");
                // Sanity floor: the dynamic width must be large enough
                // for the rendered 12pt-Bold grand total. We compare to the
                // helper's own measurement rather than a magic number so the
                // test stays correct if fonts or padding ever change.
                string grand = $"{MoneyFormatService.Format(127_932.98)} руб.";
                float grandW = (float)MeasureTextWidthPt.Invoke(null, new object?[] { grand, 12f, true })!;
                Assert.True(longW >= grandW,
                    $"Long column ({longW:F2}) must fit grand total «{grand}» ({grandW:F2})");
                return longW > shortW;
            });
        }

        [Fact]
        public void ComputeAmountColumnWidth_GrandTotalFontIsLargest()
        {
            // Even if the grand total's numeric value is smaller than a
            // regular row, its 12pt Bold font must still be measured larger
            // — otherwise the dynamic helper would clip "руб." on the
            // grand-total line.
            InvokeSta<bool>(() =>
            {
                var rows = new[]
                {
                    (amount: 99_999.99, isGrand: false), // 9pt
                    (amount: 10.0,      isGrand: true),  // 12pt Bold
                };
                float width = (float)ComputeAmountColumnWidth.Invoke(null, new object?[] { rows })!;

                // Same rendering as production: $"{Format(amount)} руб.".
                string grand = $"{MoneyFormatService.Format(10.0)} руб.";
                float grandW = (float)MeasureTextWidthPt.Invoke(null, new object?[] { grand, 12f, true })!;
                Assert.True(width >= grandW,
                    $"Grand-total row «{grand}» ({grandW:F2}) must fit inside column ({width:F2})");
                return width >= grandW;
            });
        }

        [Fact]
        public void ComputeAmountColumnWidth_EmptyRows_ReturnsPaddingOnly()
        {
            // Defensive: empty input → still returns at least the padding
            // (so callers never get a 0-width column that would crash
            // QuestPDF's layout). Read the padding constant via reflection
            // so the test stays in sync if the constant changes.
            float padding = (float)(typeof(PdfExportService).GetField(
                "AmountColumnPaddingPt",
                BindingFlags.NonPublic | BindingFlags.Static)?.GetRawConstantValue() ?? 4f);

            InvokeSta<float>(() =>
            {
                float width = (float)ComputeAmountColumnWidth.Invoke(null, new object?[] { Array.Empty<(double, bool)>() })!;
                Assert.True(width >= padding,
                    $"Empty-rows width must include padding ({padding:F2}), got {width:F2}");
                return width;
            });
        }
    }
}
