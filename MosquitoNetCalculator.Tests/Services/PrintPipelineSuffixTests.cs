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
    /// FlowDocumentBuilder (КП печать) and PdfExportService (QuestPDF export)
    /// — and read back the actual produced text, not the model property.
    /// (v3.54: «На завод» / FactoryTextService удалён вместе с кнопкой;
    /// печатные суффиксы по-прежнему обязательны в КП и PDF.)
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

    // ─── «На завод» (FactoryTextService) удалён в v3.54 вместе с кнопкой.
    // Печатные суффиксы для КП и PDF продолжают держать тесты выше.
    }
}
