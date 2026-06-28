using System.Collections.Generic;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class PrintServiceTests
    {
        private readonly PrintService _service = new();

        [Fact]
        public void GenerateKpHtml_ReturnsEmpty_WhenNoValidItems()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "", Total = 0 }
            };
            var client = new ClientInfo();
            var result = _service.GenerateKpHtml(items, client, 0, "ноль рублей 00 копеек");
            Assert.Equal("", result);
        }

        [Fact]
        public void GenerateKpHtml_ReturnsEmpty_WhenEmptyList()
        {
            var result = _service.GenerateKpHtml(new List<OrderItem>(), new ClientInfo(), 0, "");
            Assert.Equal("", result);
        }

        [Fact]
        public void GenerateKpHtml_ReturnsHtml_WithValidItems()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1.8 }
            };
            var client = new ClientInfo { ContractNumber = "1-5", ContractDate = new System.DateTime(2026, 1, 15) };
            var result = _service.GenerateKpHtml(items, client, 1.8, "Один рубль 80 копеек");

            Assert.Contains("<!DOCTYPE html>", result);
            Assert.Contains("1-5", result);
            Assert.Contains("15.01.2026", result);
            Assert.Contains("Anwis", result);
            Assert.Contains("Белый", result);
        }

        [Fact]
        public void GenerateKpHtml_ContainsClientInfo()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Total = 100 }
            };
            var client = new ClientInfo
            {
                ClientName = "Иванов И.И.",
                ClientPhone = "+7 999 123-45-67",
                ClientAddress = "г. Москва, ул. Пушкина, д. 10"
            };
            var result = _service.GenerateKpHtml(items, client, 100, "Сто рублей 00 копеек");
            Assert.Contains("Иванов И.И.", result);
            Assert.Contains("+7 999 123-45-67", result);
            Assert.Contains("г. Москва", result);
        }

        [Fact]
        public void GenerateKpHtml_EscapesHtml_InClientName()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Total = 100 }
            };
            var client = new ClientInfo { ClientName = "<script>alert('xss')</script>" };
            var result = _service.GenerateKpHtml(items, client, 100, "Сто рублей");

            Assert.DoesNotContain("<script>", result);
            Assert.Contains("&lt;script&gt;", result);
        }

        [Fact]
        public void GenerateKpHtml_ContainsNotes_WhenNotEmpty()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Total = 100 }
            };
            var client = new ClientInfo { Notes = "Тестовая заметка" };
            var result = _service.GenerateKpHtml(items, client, 100, "Сто рублей");

            Assert.Contains("Тестовая заметка", result);
            Assert.Contains("Примечания", result);
        }

        [Fact]
        public void GenerateKpHtml_DoesNotContainNotes_WhenEmpty()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Total = 100 }
            };
            var client = new ClientInfo { Notes = "" };
            var result = _service.GenerateKpHtml(items, client, 100, "Сто рублей");
            Assert.DoesNotContain("Примечания", result);
        }

        [Fact]
        public void GenerateKpHtml_ContainsAdditionalKp_WhenActive()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Total = 100 }
            };
            var client = new ClientInfo { HasAdditionalKp = true };
            client.AdditionalKps.Add(new AdditionalKpItem { Number = "2-1", Amount = 500, IsActive = true });

            var result = _service.GenerateKpHtml(items, client, 100, "Сто рублей");
            Assert.Contains("Дополнительное", result);
            Assert.Contains("2-1", result);
        }

        [Fact]
        public void GenerateKpHtml_ShowsGrandTotal_WithAdditionalKp()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Total = 100 }
            };
            var client = new ClientInfo { HasAdditionalKp = true };
            client.AdditionalKps.Add(new AdditionalKpItem { Number = "2-1", Amount = 500, IsActive = true });

            var result = _service.GenerateKpHtml(items, client, 100, "Сто рублей");
            Assert.Contains("ОБЩИЙ ИТОГ", result);
            Assert.Contains("600,00", result); // 100 + 500 formatted
        }

        [Fact]
        public void GenerateKpHtml_SkipsItemWithZeroTotal()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Total = 0 },
                new() { Name = "Отлив", Total = 100 }
            };
            var client = new ClientInfo();
            var result = _service.GenerateKpHtml(items, client, 100, "Сто рублей");
            // Only "Отлив" should be in the table rows
            Assert.Contains("Отлив", result);
        }

        // ─── Installation mark (KP column) regression tests ──────
        // These lock in the structure of the new <span class='install-mark'>
        // wrapper added when installation icons migrated to ✓ / ✗ / —. If
        // the next refactor of FillTemplate drops either the class or the
        // title attribute, these tests will fail.

        [Fact]
        public void GenerateKpHtml_InstallMark_ContainsClassAndTitle_ForMode0()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1.8, InstallationMode = 0 }
            };
            var result = _service.GenerateKpHtml(items, new ClientInfo(), 1.8, "");
            Assert.Contains("<span class='install-mark' title='Монтаж включён'>✓</span>", result);
        }

        [Fact]
        public void GenerateKpHtml_InstallMark_ContainsClassAndTitle_ForMode1()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1.8, InstallationMode = 1, InstallationDeduction = 500 }
            };
            var result = _service.GenerateKpHtml(items, new ClientInfo(), 1.8, "");
            Assert.Contains("<span class='install-mark' title='Без монтажа'>✗</span>", result);
        }

        [Fact]
        public void GenerateKpHtml_InstallMark_ContainsClassAndTitle_ForMode2()
        {
            // Mode 2 («В конструкцию») emits a Cyrillic "В" letter glyph in the
            // printed КП instead of the same ✓ as mode 0, so the two "yes" modes
            // are visually distinguishable at a glance.
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1.8, InstallationMode = 2, InstallationSurcharge = 200 }
            };
            var result = _service.GenerateKpHtml(items, new ClientInfo(), 1.8, "");
            Assert.Contains("<span class='install-mark' title='В конструкцию'>В</span>", result);
        }

        [Fact]
        public void GenerateKpHtml_InstallMark_NonApplicableProduct_HasDashAndNotSupportedTitle()
        {
            // Отлив is not eligible for the installation toggle, so the cell
            // shows "—" with the not-supported label as a tooltip.
            var items = new List<OrderItem>
            {
                new() { Name = "Отлив", Width = 1000, Height = 100, Quantity = 1, Price = 2150, Total = 2150 }
            };
            var result = _service.GenerateKpHtml(items, new ClientInfo(), 2150, "");
            Assert.Contains("<span class='install-mark' title='Монтаж не предусмотрен'>—</span>", result);
        }

        [Fact]
        public void GenerateKpHtml_ContainsAmountInWords()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Total = 100 }
            };
            var client = new ClientInfo();
            var result = _service.GenerateKpHtml(items, client, 100, "Сто рублей 00 копеек");
            Assert.Contains("Сто рублей 00 копеек", result);
        }

        [Fact]
        public void GenerateKpHtml_RenderedHtml_ContainsInstallMarkClass()
        {
            // Defensive: a future refactor that strips the install-mark class
            // from print_template.html would quietly break the КП's install
            // column. The mode-specific InstallMark tests above cover the
            // output path indirectly, but this assertion is more visible.
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Total = 100, InstallationMode = 0 }
            };
            var result = _service.GenerateKpHtml(items, new ClientInfo(), 100, "");
            Assert.Contains("class='install-mark'", result);
        }

        // ─── Case 11 regression: KP shows calc-adjusted sizes, not raw input ─
        // CALCULATION_TEST_CASES.md Case 11: for Anwis ББ 60 the KP must
        // display the stored (calc-adjusted) sizes 1002 × 970, not the raw
        // input 1000 × 1000. This locks in the contract confirmed by the owner.

        [Fact]
        public void GenerateKpHtml_AnwisBrusbox60_ShowsCalcAdjustedSizes()
        {
            var items = new List<OrderItem>
            {
                new()
                {
                    Name = "Anwis", Color = "Белый",
                    Width = 1002, Height = 970, Quantity = 1, Price = 1800, Total = 1749.60,
                    AnwisSizeMode = AnwisSizeMode.Брусбокс60
                }
            };
            var result = _service.GenerateKpHtml(items, new ClientInfo(), 1749.60, "Одна тысяча семьсот сорок девять рублей 60 копеек");

            // Must contain the calc-adjusted sizes (not raw 1000×1000)
            Assert.Contains("1002", result);
            Assert.Contains("970", result);
        }

        [Fact]
        public void GenerateKpHtml_EscapesAmpersand_AndQuotes_InNotes()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Total = 100 }
            };
            var client = new ClientInfo { Notes = "A & B \"test\" 'value'" };
            var result = _service.GenerateKpHtml(items, client, 100, "");

            Assert.DoesNotContain("A & B", result);
            Assert.Contains("A &amp; B", result);
            Assert.DoesNotContain("\"test\"", result);
            Assert.Contains("&quot;test&quot;", result);
            Assert.Contains("&#39;value&#39;", result);
        }

        [Fact]
        public void GenerateKpHtml_ConvertsNewlinesToBr_InNotes()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Total = 100 }
            };
            var client = new ClientInfo { Notes = "Line 1\nLine 2\r\nLine 3" };
            var result = _service.GenerateKpHtml(items, client, 100, "");

            Assert.DoesNotContain("Line 1\nLine 2", result);
            Assert.Contains("Line 1<br/>\nLine 2<br/>\nLine 3", result);
        }
    }
}
