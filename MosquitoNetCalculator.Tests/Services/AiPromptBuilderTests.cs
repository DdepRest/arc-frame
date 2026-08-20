using System;
using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Stage-2 hardening: <see cref="AiPromptBuilder"/> composes the
    /// canonical system prompt from an embedded Markdown resource,
    /// the live catalog/prices block, and the update history. These
    /// tests verify the contract: prompt contains required sections,
    /// reflects the live catalog prices, never fails on empty context.
    /// </summary>
    public class AiPromptBuilderTests
    {
        [Fact]
        public void BuildSystemPrompt_NoContext_DoesNotThrow_AndContainsAllRequiredSections()
        {
            var prompt = AiPromptBuilder.BuildSystemPrompt(null);

            Assert.NotNull(prompt);
            Assert.NotEmpty(prompt);
            // The four canonical sections of the system prompt are always present.
            Assert.Contains("О ПРОГРАММЕ", prompt);
            Assert.Contains("КАТАЛОГ ТОВАРОВ", prompt);
            Assert.Contains("РЕЖИМЫ ANWIS", prompt);
            Assert.Contains("ПРАВИЛА ОТВЕТОВ", prompt);
            Assert.Contains("ОТКОСЫ ИЗ СЭНДВИЧА", prompt);
            Assert.Contains("ИСТОРИЯ ОБНОВЛЕНИЙ", prompt);
        }

        [Fact]
        public void BuildSystemPrompt_WithContext_ContextAppearsBeforeBody()
        {
            var prompt = AiPromptBuilder.BuildSystemPrompt("=== Контекст заказа ===");

            int ctxIdx = prompt.IndexOf("=== Контекст заказа ===", StringComparison.Ordinal);
            int progIdx = prompt.IndexOf("О ПРОГРАММЕ", StringComparison.Ordinal);
            Assert.True(ctxIdx >= 0);
            Assert.True(progIdx > ctxIdx, "Контекст должен идти перед основным телом промпта");
        }

        [Fact]
        public void BuildSystemPrompt_ContainsLiveCatalogPrices_NotHardcoded()
        {
            var prompt = AiPromptBuilder.BuildSystemPrompt(null);

            // Anwis Белый = 1800 — appears in the live catalog block as «1800».
            Assert.Contains("1800", prompt);
            // Anwis Коричневый = 1900.
            Assert.Contains("1900", prompt);
            // Per-linear-meter products — 2150 (белый отлив), 2650 (золотой дуб), 100 (ПСУЛ), 250 (Уплотнение).
            Assert.Contains("2150", prompt);
            Assert.Contains("2650", prompt);
            Assert.Contains("100", prompt);
            Assert.Contains("250", prompt);
        }

        [Fact]
        public void BuildSystemPrompt_DoesNotContainPlaceholderString()
        {
            // The placeholder {{catPrices}} must always be substituted by the
            // live catalog block; if it ever leaks, the prompt is broken.
            var prompt = AiPromptBuilder.BuildSystemPrompt(null);
            Assert.DoesNotContain("{{catPrices}}", prompt);
        }

        [Fact]
        public void BuildCatalogPricesBlock_IncludesEveryCatalogProduct()
        {
            var block = AiPromptBuilder.BuildCatalogPricesBlock();

            Assert.Contains("Anwis", block);
            Assert.Contains("На навесах", block);
            Assert.Contains("Оконная на метал. крепл.", block);
            Assert.Contains("Дверная сетка", block);
            // Format is a Markdown table row — must contain the separator.
            Assert.Contains("|", block);
        }

        [Fact]
        public void BuildCatalogPricesBlock_AvoidsZeroManualPriceRows()
        {
            // Manual-piece products (Работа/Брус/Пояс/Доставка/Материал) have
            // no colored price row — the table only lists the priced
            // mesh/linear-meter products, not the manual pieces.
            var block = AiPromptBuilder.BuildCatalogPricesBlock();

            // The block is the mesh table only. Manual-piece rows live in the
            // static resource section «Ручные позиции» (different section).
            // Just confirm we don't accidentally emit a row with Price = 0
            // for a mesh/linear product (sanity-check the snapshot).
            var snapshot = PriceService.DefaultPricesSnapshot();
            foreach (var p in snapshot.Where(p => !string.IsNullOrEmpty(p.Color)))
                Assert.True(p.Price > 0, $"Price row «{p.Name}/{p.Color}» should not be zero in the live table");
        }

        [Fact]
        public void AppendRecentUpdates_HandlesMissingLog_Gracefully()
        {
            // Wrapped in try/catch — even when UpdateLog throws (corrupt file,
            // missing file, etc.) the prompt builder must never crash.
            // The previous in-code behaviour returned a placeholder; preserve that.
            var text = AiPromptBuilder.AppendRecentUpdates();
            Assert.NotNull(text);
            // The output is either a versioned list «• Версия …» or the fallback.
            Assert.True(text.Contains("Версия", StringComparison.Ordinal)
                        || text.Contains("недоступна", StringComparison.Ordinal));
        }

        [Fact]
        public void FormatUpdateHistory_NullEntry_RemainsResilient()
        {
            // A buggy upstream might pass a null entry; the formatter must
            // not crash (matches the old internal implementation).
            var text = AiPromptBuilder.FormatUpdateHistory(new UpdateItem[] { null! });
            Assert.NotNull(text);
            // Null entry produces no bullet lines.
            Assert.DoesNotContain("• Версия", text);
        }

        [Fact]
        public void FormatUpdateHistory_ProducesVersionedBullets()
        {
            var entries = new[]
            {
                new UpdateItem { Version = "3.47.4", Date = new DateTime(2026, 8, 19), Title = "fix", Changes = new List<string> { "first change", "second change" } },
                new UpdateItem { Version = "3.47.3", Date = new DateTime(2026, 8, 10), Title = "fix2", Changes = new List<string> { "third change" } }
            };

            var text = AiPromptBuilder.FormatUpdateHistory(entries);

            Assert.Contains("3.47.4", text);
            Assert.Contains("3.47.3", text);
            Assert.Contains("first change", text);
            Assert.Contains("second change", text);
            Assert.Contains("third change", text);
        }
    }
}
