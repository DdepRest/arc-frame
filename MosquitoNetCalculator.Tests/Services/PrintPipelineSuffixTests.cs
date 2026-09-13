using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using UglyToad.PdfPig;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// v3.50.2 owner requirement: the screen shows only badges (АК/ИМ pills),
    /// but PRINTED artifacts must keep the «(Антикошка)» / «(Импост)»
    /// annotations. These tests exercise the real pipelines —
    /// FlowDocumentBuilder (КП печать), PdfExportService (QuestPDF export)
    /// and FactoryTextService («На завод») — and read back the actual
    /// produced text, not the model property.
    /// </summary>
    public class PrintPipelineSuffixTests
    {
        private static List<OrderItem> SuffixedItems() => new()
        {
            // 1000 мм ≥ 500 мм → этот Anwis ещё и импост: полный суффикс
            // «(Антикошка) (Импост)» в печати.
            new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 3800, Total = 3800, IsAnticat = true },
            // «На навесах» 600×800 — импост по identity-правилу.
            new() { Name = "На навесах", Color = "Белый", Width = 600, Height = 800, Quantity = 1, Price = 1800, Total = 1800 }
        };

        /// <summary>Whitespace-stripped comparison — robust to PDF word-gap detection.</summary>
        private static string Flat(string text) => Regex.Replace(text, @"\s+", "");

        // ─── КП: FlowDocumentBuilder (физическая печать) ─────────────────

        [Fact]
        public void KpFlowDocument_KeepsAnticatAndImpostSuffixes()
        {
            var text = WpfTestHelper.RunOnSta(() =>
            {
                var doc = new FlowDocumentBuilder().Build(
                    SuffixedItems(), new ClientInfo { ContractNumber = "1-1" }, 5600, "");
                return doc != null ? FlowDocumentTextExtractor.ExtractAllText(doc) : "";
            });

            Assert.Contains("Anwis (Антикошка)", text);      // антикошка — в имени строки КП
            Assert.Contains("(Импост)", text);               // этот же Anwis 1000 мм — и импост
            Assert.Contains("На навесах (Импост)", text);    // чистый импост — в имени строки КП
        }

        // ─── PDF: PdfExportService (QuestPDF) ────────────────────────────

        [Fact]
        public void PdfExport_KeepsAnticatAndImpostSuffixes()
        {
            var path = Path.Combine(Path.GetTempPath(), $"arc-suffix-{Guid.NewGuid():N}.pdf");
            try
            {
                new PdfExportService().Export(
                    path, SuffixedItems(), new ClientInfo { ContractNumber = "1-1" }, 5600, "");
                Assert.True(File.Exists(path), "PDF file must be created");

                string text;
                using (var doc = PdfDocument.Open(path))
                    text = string.Join(" ", doc.GetPages().Select(p => p.Text));

                var flat = Flat(text);
                // Плоская строка без пробелов — не зависит от того, как
                // QuestPDF/PdfPig режут строку на прогоны и слова.
                Assert.Contains("Anwis(Антикошка)", flat);
                Assert.Contains("Нанавесах(Импост)", flat);
            }
            finally
            {
                try { File.Delete(path); } catch { /* cleanup */ }
            }
        }

        // ─── «На завод»: FactoryTextService ──────────────────────────────

        [Fact]
        public void FactoryText_KeepsAnticatAndImpostSuffixes()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1800, IsAnticat = true },
                new() { Name = "На навесах", Width = 600, Height = 800, Quantity = 2, Price = 1800, Total = 3600 }
            };
            var selectable = FactoryTextService.BuildSelectableItems(items, new List<AdditionalKpItem>());
            foreach (var si in selectable) si.IsSelected = true;

            var text = FactoryTextService.Generate("", selectable);

            // Anwis получает mode-aware заголовок (дефолтный режим ББ 60);
            // «На навесах» — обычный заголовок по имени.
            Assert.Contains("Anwis (Антикошка) (Импост), размер проёма (ББ 60):", text);
            Assert.Contains("На навесах (Импост):", text);
        }

        [Fact]
        public void FactoryText_AnticatAnwis_IsSeparateSectionFromPlainAnwis()
        {
            // Регрессия группировки: ключ секций — PrintDisplayName. Если
            // вернуть группировку по DisplayName, «Антикошка» схлопнется с
            // обычным Anwis в одну секцию и заголовок ниже исчезнет.
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1800, IsAnticat = true },
                new() { Name = "Anwis", Width = 1000, Height = 1000, Quantity = 2, Price = 1800, Total = 3600 }
            };
            var selectable = FactoryTextService.BuildSelectableItems(items, new List<AdditionalKpItem>());
            foreach (var si in selectable) si.IsSelected = true;

            var text = FactoryTextService.Generate("", selectable);

            // Обе секции существуют раздельно: «Anwis (Антикошка) (Импост), …»
            // и «Anwis (Импост), …» — вторая не является подстрокой первой,
            // поэтому Contains доказывает отдельный заголовок.
            Assert.Contains("Anwis (Антикошка) (Импост), размер проёма (ББ 60):", text);
            Assert.Contains("Anwis (Импост), размер проёма (ББ 60):", text);
            // «1 шт.» (антикошка) и «2 шт.» (обычная) не слились в одной секции:
            // каждая секция содержит только своё количество сразу за заголовком.
            Assert.Matches(@"Anwis \(Антикошка\) \(Импост\), размер проёма \(ББ 60\):\s*Ш: \d+ × В: \d+ — 1 шт\.", text);
            Assert.Matches(@"Anwis \(Импост\), размер проёма \(ББ 60\):\s*Ш: \d+ × В: \d+ — 2 шт\.", text);
        }
    }
}
