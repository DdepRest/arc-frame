using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

using PrintResultType = MosquitoNetCalculator.Models.PrintResultType;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Tests for the native print pipeline: BuildFlowDocument + PrintSettings + SendToQueue.
    /// BuildFlowDocument creates FormattedText/DrawingImage objects that require STA threads,
    /// so all calls are wrapped in WpfTestHelper.RunOnSta.
    /// </summary>
    public class PrintServiceTests
    {
        private readonly PrintService _service = new();

        private static List<OrderItem> SampleItems() => new()
        {
            new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1.8 }
        };

        private static ClientInfo SampleClient() => new()
        {
            ContractNumber = "1-5",
            ContractDate = new System.DateTime(2026, 1, 15)
        };

        // Helper: build document on STA thread and extract text
        private string BuildAndExtract(List<OrderItem> items, ClientInfo client, double total, string words)
        {
            return WpfTestHelper.RunOnSta(() =>
            {
                var doc = _service.BuildFlowDocument(items, client, total, words);
                return doc != null ? FlowDocumentTextExtractor.ExtractAllText(doc) : "";
            });
        }

        // ─── BuildFlowDocument tests (all on STA) ───────────────────

        [Fact]
        public void BuildFlowDocument_ReturnsNull_WhenNoValidItems()
        {
            var items = new List<OrderItem> { new() { Name = "", Total = 0 } };
            var result = WpfTestHelper.RunOnSta(() =>
                _service.BuildFlowDocument(items, new ClientInfo(), 0, "ноль рублей 00 копеек"));
            Assert.Null(result);
        }

        [Fact]
        public void BuildFlowDocument_ReturnsNull_WhenEmptyList()
        {
            var result = WpfTestHelper.RunOnSta(() =>
                _service.BuildFlowDocument(new List<OrderItem>(), new ClientInfo(), 0, ""));
            Assert.Null(result);
        }

        [Fact]
        public void BuildFlowDocument_ReturnsDocument_WithValidItems()
        {
            var result = WpfTestHelper.RunOnSta(() =>
                _service.BuildFlowDocument(SampleItems(), SampleClient(), 1.8, "Один рубль 80 копеек"));
            Assert.NotNull(result);
            Assert.NotEmpty(result!.Blocks);
        }

        [Fact]
        public void BuildFlowDocument_ContainsContractNumber()
        {
            var text = BuildAndExtract(SampleItems(), SampleClient(), 1.8, "");
            Assert.Contains("1-5", text);
            Assert.Contains("15.01.2026", text);
        }

        [Fact]
        public void BuildFlowDocument_ContainsClientInfo()
        {
            var items = new List<OrderItem> { new() { Name = "Anwis", Total = 100 } };
            var client = new ClientInfo
            {
                ClientName = "Иванов И.И.",
                ClientPhone = "+7 999 123-45-67",
                ClientAddress = "г. Москва, ул. Пушкина, д. 10"
            };
            var text = BuildAndExtract(items, client, 100, "Сто рублей 00 копеек");
            Assert.Contains("Иванов И.И.", text);
            Assert.Contains("+7 999 123-45-67", text);
            Assert.Contains("г. Москва", text);
        }

        [Fact]
        public void BuildFlowDocument_ContainsAmountInWords()
        {
            var text = BuildAndExtract(SampleItems(), SampleClient(), 100, "Сто рублей 00 копеек");
            Assert.Contains("Сто рублей 00 копеек", text);
        }

        [Fact]
        public void BuildFlowDocument_ContainsItemName()
        {
            var text = BuildAndExtract(SampleItems(), SampleClient(), 1.8, "");
            Assert.Contains("Anwis", text);
            Assert.Contains("Белый", text);
        }

        [Fact]
        public void BuildFlowDocument_ContainsNotes_WhenNotEmpty()
        {
            var client = new ClientInfo { Notes = "Тестовая заметка" };
            var text = BuildAndExtract(SampleItems(), client, 100, "Сто рублей");
            Assert.Contains("Тестовая заметка", text);
            Assert.Contains("ПРИМЕЧАНИЯ", text);
        }

        [Fact]
        public void BuildFlowDocument_SkipsItemWithZeroTotal()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Total = 0 },
                new() { Name = "Отлив", Total = 100 }
            };
            var text = BuildAndExtract(items, new ClientInfo(), 100, "Сто рублей");
            Assert.Contains("Отлив", text);
        }

        [Fact]
        public void BuildFlowDocument_ContainsAdditionalKp_WhenActive()
        {
            var client = new ClientInfo { HasAdditionalKp = true };
            client.AdditionalKps.Add(new AdditionalKpItem { Number = "2-1", Amount = 500, IsActive = true });
            var text = BuildAndExtract(SampleItems(), client, 100, "Сто рублей");
            Assert.Contains("2-1", text);
            Assert.Contains("500", text);
        }

        [Fact]
        public void BuildFlowDocument_ShowsGrandTotal_WithAdditionalKp()
        {
            var client = new ClientInfo { HasAdditionalKp = true };
            client.AdditionalKps.Add(new AdditionalKpItem { Number = "2-1", Amount = 500, IsActive = true });
            var text = BuildAndExtract(SampleItems(), client, 100, "Сто рублей");
            Assert.Contains("ОБЩИЙ ИТОГ", text);
        }

        [Fact]
        public void BuildFlowDocument_AnticatItem_ContainsDisplayName()
        {
            var items = new List<OrderItem>
            {
                new()
                {
                    Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000,
                    Quantity = 1, Price = 3800, Total = 3800, IsAnticat = true
                }
            };
            var text = BuildAndExtract(items, new ClientInfo(), 3800, "");
            Assert.Contains("Антикошка", text);
        }

        /// <summary>
        /// Regression guard data for dimension display in the printed КП.
        /// Each case describes a product whose stored/calc/factory dimensions
        /// must appear (or must not appear) in the generated document.
        /// </summary>
        public static IEnumerable<object[]> DimensionTestCases => new List<object[]>
        {
            // Anwis ББ60: stored 1002×970 (calc-adjusted). FormatIntWithNbsp
            // threshold is ≥10 000, so values render without NBSP.
            new object[] { new DimensionTestCase(
                "Anwis ББ60 calc 1002×970",
                new OrderItem { Name = "Anwis", Color = "Белый", Width = 1002, Height = 970, Quantity = 1, Price = 1800, Total = 1749.60, AnwisSizeMode = AnwisSizeMode.Брусбокс60 },
                1749.60,
                new[] { "1002", "970" },
                Array.Empty<string>())
            },

            // v3.43.2 regression guard: Anwis ББ60 raw 500×1000 → stored 502×970.
            // Factory (calc − 20 = 482×950) and raw values must NOT leak into КП.
            new object[] { new DimensionTestCase(
                "Anwis ББ60 raw 500×1000 → 502×970 (no raw/factory)",
                new OrderItem { Name = "Anwis", Color = "Белый", Width = 502, Height = 970, Quantity = 1, Price = 1800, Total = 876.60, AnwisSizeMode = AnwisSizeMode.Брусбокс60 },
                876.60,
                new[] { "502", "970" },
                new[] { "500", "1000", "482", "950" })
            },

            // Anwis ББ70 raw 500×1000 → stored 498×970; factory 478×950 excluded.
            new object[] { new DimensionTestCase(
                "Anwis ББ70 raw 500×1000 → 498×970 (no raw/factory)",
                new OrderItem { Name = "Anwis", Color = "Белый", Width = 498, Height = 970, Quantity = 1, Price = 1800, Total = 869.40 },
                869.40,
                new[] { "498", "970" },
                new[] { "500", "1000", "478", "950" })
            },

            // Anwis РазмерПроёма: raw 600×1200 → stored 620×1220; raw/factory excluded.
            new object[] { new DimensionTestCase(
                "Anwis РазмерПроёма 600×1200 → 620×1220 (no raw)",
                new OrderItem { Name = "Anwis", Color = "Белый", Width = 620, Height = 1220, Quantity = 1, Price = 1800, Total = 1360.80 },
                1360.80,
                new[] { "620", "1220" },
                new[] { "600", "1200" })
            },

            // Anwis Габаритный: raw=calc=500×1000; factory 480×980 excluded.
            new object[] { new DimensionTestCase(
                "Anwis Габаритный 500×1000 (no factory)",
                new OrderItem { Name = "Anwis", Color = "Белый", Width = 500, Height = 1000, Quantity = 1, Price = 1800, Total = 900.00 },
                900.00,
                new[] { "500", "1000" },
                new[] { "480", "980" })
            },

            // Non-Anwis m² product: stored = raw = calc = factory.
            new object[] { new DimensionTestCase(
                "На навесах 1000×800",
                new OrderItem { Name = "На навесах", Color = "Белый", Width = 1000, Height = 800, Quantity = 1, Price = 1800, Total = 1440.00 },
                1440.00,
                new[] { "1000", "800" },
                Array.Empty<string>())
            },

            // Non-Anwis fallback product.
            new object[] { new DimensionTestCase(
                "Оконная на метал. крепл. 1200×900",
                new OrderItem { Name = "Оконная на метал. крепл.", Color = "Белый", Width = 1200, Height = 900, Quantity = 1, Price = 1800, Total = 1944.00 },
                1944.00,
                new[] { "Оконная на метал. крепл.", "1200", "900" },
                Array.Empty<string>())
            },

            // Door net.
            new object[] { new DimensionTestCase(
                "Дверная сетка 1200×1000",
                new OrderItem { Name = "Дверная сетка", Color = "Белый", Width = 1200, Height = 1000, Quantity = 1, Price = 3000, Total = 3600.00 },
                3600.00,
                new[] { "Дверная сетка", "1200", "1000" },
                Array.Empty<string>())
            },

            // Sill.
            new object[] { new DimensionTestCase(
                "Отлив 250×1200",
                new OrderItem { Name = "Отлив", Width = 250, Height = 1200, Quantity = 1, Price = 2000, Total = 600.00 },
                600.00,
                new[] { "Отлив", "250", "1200" },
                Array.Empty<string>())
            },

            // Canopy.
            new object[] { new DimensionTestCase(
                "Козырёк 1800×700",
                new OrderItem { Name = "Козырёк", Color = "Белый", Width = 1800, Height = 700, Quantity = 1, Price = 2500, Total = 3150.00 },
                3150.00,
                new[] { "Козырёк", "1800", "700" },
                Array.Empty<string>())
            },

            // Frame.
            new object[] { new DimensionTestCase(
                "Короб 2000×500",
                new OrderItem { Name = "Короб", Color = "Белый", Width = 2000, Height = 500, Quantity = 1, Price = 3000, Total = 3000.00 },
                3000.00,
                new[] { "Короб", "2000", "500" },
                Array.Empty<string>())
            },

            // Linear PSUL.
            new object[] { new DimensionTestCase(
                "ПСУЛ 400×600",
                new OrderItem { Name = "ПСУЛ", Width = 400, Height = 600, Quantity = 1, Price = 2000, Total = 4000.00 },
                4000.00,
                new[] { "ПСУЛ", "400", "600" },
                Array.Empty<string>())
            },

            // Manual piece slope material.
            new object[] { new DimensionTestCase(
                "Откос материал 250 (WidthOnly)",
                new OrderItem { Name = "Откос материал", Width = 250, Height = 0, Quantity = 3, Price = 500, Total = 1500.00 },
                1500.00,
                new[] { "Откос материал", "250" },
                Array.Empty<string>())
            },
        };

        [Theory]
        [MemberData(nameof(DimensionTestCases))]
        public void BuildFlowDocument_DimensionTests(DimensionTestCase testCase)
        {
            var text = BuildAndExtract(new List<OrderItem> { testCase.Item }, new ClientInfo(), testCase.Total, "");

            foreach (var expected in testCase.ExpectedContained)
                Assert.Contains(expected, text);

            foreach (var notExpected in testCase.ExpectedExcluded)
                Assert.DoesNotContain(notExpected, text);
        }

        /// <summary>Data holder for parameterized dimension assertions.</summary>
        public class DimensionTestCase
        {
            public string Name { get; }
            public OrderItem Item { get; }
            public double Total { get; }
            public IReadOnlyList<string> ExpectedContained { get; }
            public IReadOnlyList<string> ExpectedExcluded { get; }

            public DimensionTestCase(string name, OrderItem item, double total, string[] expectedContained, string[] expectedExcluded)
            {
                Name = name;
                Item = item;
                Total = total;
                ExpectedContained = expectedContained;
                ExpectedExcluded = expectedExcluded;
            }

            public override string ToString() => Name;
        }

        // ─── v3.43.2.12: Цвет column readability regression guards ───
        // User: «Коричневый» зажёвывается в Цвет-колонке печатного КП — виден только
        // «оричневы» (первая «К» и последняя «й» отсечены). Первоначальный fix понизил
        // floor 0.75 → 0.55, но текст стал нечитаемо мелким. Окончательный fix:
        // widen Цвет 0.08 → 0.12 (budget из № 0.05→0.04, Цена 0.10→0.09,
        // Ш 0.08→0.075, Монтаж 0.075→0.07, Чертёж 0.10→0.09), floor оставлен 0.75.
        // Теперь usableWidths[2] = 0.12 × 670 − 14 = 66.4 DIP, и
        // «Коричневый»/«Золотой дуб» shrink только до ~11.4 DIP — читаемо.
        //
        // Тесты строят FlowDocument, находят Цвет-ячейку в строке таблицы и
        // проверяют: (1) текст не клипается (measuredWidth ≤ usable + tolerance),
        // (2) fontSize остаётся читаемым (≥ 11 DIP для известных длинных цветов),
        // (3) для сверх-длинных color names auto-shrink не опускается ниже floor 0.75
        // (= 9 DIP) — защита от regression к нечитаемо мелкому тексту.
        [Fact]
        public void BuildFlowDocument_Korichnevy_NoHorizontalClip()
        {
            // «Anwis Коричневый» 1000×1000, ББ60 по умолчанию (stored 1002×970).
            // CV = 0.972 м², Total = 0.972 × 1900 = 1846.80.
            // Total вычисляется автоматически через setter cascade в OrderItem.property setters
            // (Width/Height/Quantity/Price setter-ы все вызывают Recalculate).
            var items = new List<OrderItem>
            {
                new()
                {
                    Name = "Anwis", Color = "Коричневый",
                    Width = 1002, Height = 970,
                    Quantity = 1, Price = 1900,
                    AnwisSizeMode = AnwisSizeMode.Брусбокс60
                }
            };

            WpfTestHelper.RunOnSta(() =>
            {
                var doc = _service.BuildFlowDocument(items, SampleClient(), items[0].Total, "");
                Assert.NotNull(doc);

                // FlowDocument от BuildFlowDocument содержит 3 таблицы:
                //   1) BuildClientBlock (2 колонки, 1 RowGroup — Заказчик/Телефон/Адрес)
                //   2) BuildItemsTable   (12 колонок, 2 RowGroups — таблица товаров КП)
                //   3) BuildAdditionalKpSection «ИТОГО» (2 колонки, 1 RowGroup — only если active KP)
                // (BuildTermsAndSignatureFigure тоже содержит 2-колонную sig-table.)
                // Test target — ИСКЛЮЧИТЕЛЬНО таблица товаров. УНИКАЛЬНЫЙ селектор:
                //   Columns.Count == 12 AND RowGroups.Count == 2 (header + body).
                // Item table — единственная с 12 колонками; дополнительно имеющая
                // 2 RowGroups (header + body) исключает любой гипотетический 12-col
                // single-Group кандидат в будущем.
                var table = doc.Blocks.OfType<Table>()
                    .FirstOrDefault(t => t.Columns.Count == 12 && t.RowGroups.Count == 2);
                Assert.NotNull(table);
                var itemsTable = table!;
                var bodyRows = itemsTable.RowGroups[1].Rows; // [0] = header, [1] = first item
                var itemRow = bodyRows[0];
                var colorCell = itemRow.Cells[2];
                var container = colorCell.Blocks.OfType<BlockUIContainer>().FirstOrDefault();
                Assert.NotNull(container);
                var tb = (TextBlock)container.Child;
                Assert.Equal("Коричневый", tb.Text);

                // v3.43.2.12 lock-in: Цвет-колонка теперь достаточно широкая, чтобы
                // «Коричневый»/«Золотой дуб» рендерились на читаемом размере (≥ 11 DIP).
                // Этот assert гарантирует, что fontSize не упал до нечитаемо мелкого
                // (regression против повторного floor 0.55).
                const double originalBodyFontSize = 12.0; // bodyFontSize = 9 * 96 / 72
                Assert.True(
                    tb.FontSize >= 11.0,
                    $"«{tb.Text}» оказался слишком мелким ({tb.FontSize:F2} DIP). " +
                    $"Цвет-колонка должна быть достаточно широкой, чтобы длинные color names " +
                    $"рендерились на исходном bodyFontSize ({originalBodyFontSize:F2} DIP), " +
                    $"а не shrink'ались до нечитаемого размера.");

                // Цвет column fraction 0.12 × 670 − 14 ≈ 66.4 DIP usable.
                const double colorUsable = 0.12 * 670 - 14;
                var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
                double measuredWidth = new FormattedText(
                    tb.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    typeface, tb.FontSize, Brushes.Black, 1.0).WidthIncludingTrailingWhitespace;

                Assert.True(
                    measuredWidth <= colorUsable + 0.5,
                    $"Текст «{tb.Text}» шириной {measuredWidth:F2} DIP не влезает в Цвет usableWidth {colorUsable:F2} DIP " +
                    $"(fontSize сейчас: {tb.FontSize:F2} DIP). Auto-shrink должен уменьшить fontSize так, чтобы это влезло.");
            });
        }

        [Fact]
        public void BuildFlowDocument_ZolotoyDub_NoHorizontalClip()
        {
            // «Отлив Золотой дуб» 1000×800 — самый длинный color name в каталоге (11 char).
            // CV = 0.8 м², Total = 0.8 × 2650 = 2120.00.
            // Total вычисляется автоматически через setter cascade (см. Korichnevy-тест).
            var items = new List<OrderItem>
            {
                new()
                {
                    Name = "Отлив", Color = "Золотой дуб",
                    Width = 1000, Height = 800,
                    Quantity = 1, Price = 2650  // Золотой дуб — 2650 в price list
                }
            };

            WpfTestHelper.RunOnSta(() =>
            {
                var doc = _service.BuildFlowDocument(items, SampleClient(), items[0].Total, "");
                Assert.NotNull(doc);

                // See Korichnevy: FlowDocument содержит несколько Tables; нам нужна
                // именно 12-колоночная таблица товаров из BuildItemsTable (12 cols + 2 row groups).
                var table = doc.Blocks.OfType<Table>()
                    .FirstOrDefault(t => t.Columns.Count == 12 && t.RowGroups.Count == 2);
                Assert.NotNull(table);
                var itemsTable = table!;
                var bodyRows = itemsTable.RowGroups[1].Rows;
                var itemRow = bodyRows[0];
                var colorCell = itemRow.Cells[2];
                var container = colorCell.Blocks.OfType<BlockUIContainer>().FirstOrDefault();
                Assert.NotNull(container);
                var tb = (TextBlock)container.Child;
                Assert.Equal("Золотой дуб", tb.Text);

                // v3.43.2.12 lock-in (тот же контракт, что в Korichnevy-тесте).
                const double originalBodyFontSize = 12.0;
                Assert.True(
                    tb.FontSize >= 11.0,
                    $"«{tb.Text}» оказался слишком мелким ({tb.FontSize:F2} DIP). " +
                    $"Цвет-колонка должна быть достаточно широкой, чтобы длинные color names " +
                    $"рендерились на исходном bodyFontSize ({originalBodyFontSize:F2} DIP), " +
                    $"а не shrink'ались до нечитаемого размера.");

                const double colorUsable = 0.12 * 670 - 14;
                var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
                double measuredWidth = new FormattedText(
                    tb.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    typeface, tb.FontSize, Brushes.Black, 1.0).WidthIncludingTrailingWhitespace;

                Assert.True(
                    measuredWidth <= colorUsable + 0.5,
                    $"Текст «{tb.Text}» шириной {measuredWidth:F2} DIP не влезает в Цвет usableWidth {colorUsable:F2} DIP " +
                    $"(fontSize сейчас: {tb.FontSize:F2} DIP). Auto-shrink должен уменьшить fontSize так, чтобы это влезло.");
            });
        }

        [Fact]
        public void BuildFlowDocument_UltraLongColorName_HitsFloor0_75()
        {
            // Regression guard для floor 0.75: сверх-длинный color name
            // (18 chars + space) должен shrink'аться, но НЕ ниже 9 DIP (0.75 × 12).
            // Если floor когда-нибудь понизится (например, до 0.55), этот тест
            // поймает regression — текст станет нечитаемо мелким.
            var items = new List<OrderItem>
            {
                new()
                {
                    Name = "Anwis", Color = "Тёмный орех экстра",
                    Width = 1000, Height = 1000,
                    Quantity = 1, Price = 1800
                }
            };

            WpfTestHelper.RunOnSta(() =>
            {
                var doc = _service.BuildFlowDocument(items, SampleClient(), items[0].Total, "");
                Assert.NotNull(doc);

                var table = doc.Blocks.OfType<Table>()
                    .FirstOrDefault(t => t.Columns.Count == 12 && t.RowGroups.Count == 2);
                Assert.NotNull(table);
                var itemsTable = table!;
                var itemRow = itemsTable.RowGroups[1].Rows[0];
                var colorCell = itemRow.Cells[2];
                var container = colorCell.Blocks.OfType<BlockUIContainer>().FirstOrDefault();
                Assert.NotNull(container);
                var tb = (TextBlock)container.Child;
                Assert.Equal("Тёмный орех экстра", tb.Text);

                // Floor 0.75 контракт: fontSize не может упасть ниже 9 DIP.
                // Для сверх-длинного цвета auto-shrink должен дойти до floor 9.0 DIP
                // и остановиться — не уходить ниже. Текст намеренно выбран таким
                // длинным, что даже на 9 DIP он НЕ влезает в usable width 66.4 DIP;
                // это нормально: floor защищает от нечитаемо мелкого шрифта,
                // а не гарантирует fit.
                Assert.True(
                    tb.FontSize >= 9.0,
                    $"«{tb.Text}» shrink'ался ниже floor 0.75 ({tb.FontSize:F2} DIP). " +
                    $"Floor 0.75 (= 9 DIP) — минимально читаемый размер для color names.");

                // Должен был произойти shrink до floor (иначе тест тривиальный).
                Assert.True(
                    tb.FontSize < 10.0,
                    $"«{tb.Text}» не shrink'ался до floor ({tb.FontSize:F2} DIP), тест не проверяет floor.");

                // Подтверждаем, что на floor текст всё ещё не влезает —
                // иначе shrink остановился бы раньше, и floor не был бы проверен.
                const double colorUsable = 0.12 * 670 - 14;
                var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
                double measuredWidthAtFloor = new FormattedText(
                    tb.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    typeface, 9.0, Brushes.Black, 1.0).WidthIncludingTrailingWhitespace;

                Assert.True(
                    measuredWidthAtFloor > colorUsable,
                    $"Текст «{tb.Text}» влезает в Цвет usableWidth {colorUsable:F2} DIP на floor 9 DIP " +
                    $"(width={measuredWidthAtFloor:F2}), поэтому floor не достигнут — выберите более длинный color name.");
            });
        }

        // ─── FormatIntWithNbsp tests (no STA needed) ─────────────────

        [Fact]
        public void FormatIntWithNbsp_NoNbsp_ForValuesUnder10000()
        {
            // Regression: v3.44.9 always used NBSP, making short numbers
            // (e.g. «1002» → «1 002») wider than needed in narrow columns.
            // Now threshold is ≥10_000.
            var result = InvokeFormatIntWithNbsp(1002);
            Assert.Equal("1002", result);
            Assert.DoesNotContain("\u00A0", result);
        }

        [Fact]
        public void FormatIntWithNbsp_HasNbsp_ForValues10000AndAbove()
        {
            // v3.44.9 fix preserved: numbers ≥ 10 000 still get NBSP grouping
            // so «141084» renders as «141 084» (readable).
            var result = InvokeFormatIntWithNbsp(141084);
            Assert.Equal("141\u00A0084", result);
            Assert.Contains("\u00A0", result);
        }

        [Fact]
        public void FormatIntWithNbsp_NoNbsp_For9999()
        {
            var result = InvokeFormatIntWithNbsp(9999);
            Assert.Equal("9999", result);
            Assert.DoesNotContain("\u00A0", result);
        }

        [Fact]
        public void FormatIntWithNbsp_HasNbsp_For10000()
        {
            var result = InvokeFormatIntWithNbsp(10000);
            Assert.Equal("10\u00A0000", result);
        }

        // Access private static FormatIntWithNbsp via reflection
        private static string InvokeFormatIntWithNbsp(double value)
        {
            var method = typeof(FlowDocumentBuilder).GetMethod(
                "FormatIntWithNbsp",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(method);
            return (string)method!.Invoke(null, new object[] { value })!;
        }

        // ─── PrintSettings tests (no STA needed) ────────────────────

        [Fact]
        public void PrintSettings_Defaults_AreCorrect()
        {
            var s = new PrintSettings();
            Assert.Equal(1, s.Copies);
            Assert.True(s.Collated);
            Assert.True(s.Color);
            Assert.Equal(PageMode.All, s.Pages);
            Assert.Equal(1, s.PageFrom);
            Assert.Equal(1, s.PageTo);
            Assert.Equal(1, s.SinglePage);
            Assert.Null(s.PrinterName);
        }

        [Fact]
        public void PrintSettings_Clone_CreatesIndependentCopy()
        {
            var s = new PrintSettings { Copies = 5, Collated = false, Pages = PageMode.Range, PageFrom = 2 };
            var clone = s.Clone();
            clone.Copies = 10;
            Assert.Equal(5, s.Copies);
            Assert.Equal(10, clone.Copies);
        }

        // ─── PrintResult tests (no STA needed) ──────────────────────

        [Fact]
        public void PrintResult_Ok_HasSuccessType()
        {
            var r = PrintResult.Ok();
            Assert.Equal(PrintResultType.Success, r.Type);
            Assert.False(r.IsRetryable);
        }

        [Theory]
        [InlineData(PrintResultType.Success)]
        [InlineData(PrintResultType.PrinterOffline)]
        [InlineData(PrintResultType.PrinterOutOfPaper)]
        [InlineData(PrintResultType.PrinterTonerLow)]
        [InlineData(PrintResultType.PrinterError)]
        [InlineData(PrintResultType.SpoolerStopped)]
        [InlineData(PrintResultType.AccessDenied)]
        [InlineData(PrintResultType.QueueError)]
        [InlineData(PrintResultType.Unknown)]
        public void PrintResultType_AllValues_AreDefined(PrintResultType type)
        {
            Assert.True(System.Enum.IsDefined(typeof(PrintResultType), type));
        }

    }

}
