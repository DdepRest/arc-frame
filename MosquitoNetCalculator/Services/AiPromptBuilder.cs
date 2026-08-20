using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Builds the AI assistant's system prompt. Stage-2 hardening keeps
    /// the static rules and example tables in an embedded
    /// <c>Resources/ai-system-prompt.md</c> resource so the prompt can be
    /// reviewed and edited as data without touching C# code, and reads
    /// the canonical catalog/prices from
    /// <see cref="AiFactsProvider"/> instead of duplicating the price
    /// table inline.
    /// </summary>
    public static class AiPromptBuilder
    {
        /// <summary>Marker that gets replaced with the live catalog/prices table.</summary>
        private const string PricesPlaceholder = "{{catPrices}}";

        /// <summary>Cached resource text; loaded once per process to avoid per-request I/O.</summary>
        private static readonly Lazy<string> ResourceText = new(LoadResource);

        /// <summary>
        /// Public API used by <c>AiAssistantService</c>: pure delegation.
        /// <paramref name="orderContext"/> is the order summary pre-computed
        /// upstream (totals / item list / status); pass <c>null</c> when
        /// nothing is loaded yet.
        /// </summary>
        public static string BuildSystemPrompt(string? orderContext)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(orderContext))
            {
                sb.AppendLine(orderContext);
                sb.AppendLine();
            }
            sb.AppendLine(ResourceText.Value.Replace(PricesPlaceholder, BuildCatalogPricesBlock()));
            sb.AppendLine();
            sb.AppendLine("## ИСТОРИЯ ОБНОВЛЕНИЙ (для справки)");
            sb.AppendLine(AppendRecentUpdates());
            return sb.ToString();
        }

        /// <summary>
        /// Compiles the «live» catalog-prices block from
        /// <see cref="AiFactsProvider"/> + <see cref="PriceService.DefaultPricesSnapshot"/>.
        /// Tables are formatted as Markdown so they read identically to the
        /// static tables in the resource file.
        /// </summary>
        public static string BuildCatalogPricesBlock()
        {
            var sb = new StringBuilder();
            var snapshot = PriceService.DefaultPricesSnapshot();
            // Catalog rows for the грид table: Anwis / На навесах / Оконная на метал. крепл. / Дверная сетка.
            FormatGridRow(sb, "Anwis", "Брусбокс60/Профипласт", snapshot);
            FormatGridRow(sb, "На навесах", "—", snapshot);
            FormatGridRow(sb, "Оконная на метал. крепл.", "—", snapshot);
            FormatGridRow(sb, "Дверная сетка", "—", snapshot);
            return sb.ToString();
        }

        private static void FormatGridRow(StringBuilder sb, string product, string anwisMode, IReadOnlyList<PriceItem> snapshot)
        {
            var colors = AiFactsProvider.GetColorsFor(product);
            string colorsList = colors.Count > 0 ? string.Join("/", colors) : "—";
            // Collect prices as «min/max» when there are multiple; fall back to a single number.
            var prices = snapshot.Where(p => p.Name == product && !string.IsNullOrEmpty(p.Color)).Select(p => (int)p.Price).ToList();
            string priceColumn = prices.Count == 0 ? "—" : string.Join("/", prices);
            sb.Append("| ").Append(product).Append(" | ").Append(colorsList).Append(" | ").Append(priceColumn).Append(" | ").Append(anwisMode).AppendLine(" |");
        }

        /// <summary>
        /// Full update history (every version, every line item) so the
        /// assistant can answer questions about any past version. Wrapped
        /// in try/catch so a missing update-log.json never crashes the
        /// system prompt — same fail-soft behaviour as the previous
        /// inline implementation in <c>AiAssistantService</c>.
        /// </summary>
        public static string AppendRecentUpdates()
        {
            try
            {
                return FormatUpdateHistory(UpdateLog.AllNewestFirst());
            }
            catch
            {
                return "(история обновлений недоступна)";
            }
        }

        /// <summary>
        /// «• Версия X.Y.Z (дд.ММ.гггг): Заголовок» + каждая правка отдельной строкой.
        /// Версии не обрезаются — ассистент должен уметь рассказать про любую.
        /// </summary>
        public static string FormatUpdateHistory(IEnumerable<UpdateItem> entries)
        {
            var sb = new StringBuilder();
            foreach (var e in entries)
            {
                if (e == null) continue;
                sb.AppendLine($"• Версия {e.Version} ({e.Date:dd.MM.yyyy}): {e.Title}");
                foreach (var change in e.Changes ?? new List<string>())
                    sb.AppendLine($"  — {change}");
            }
            return sb.ToString();
        }

        /// <summary>Reads <c>Resources/ai-system-prompt.md</c> embedded by the csproj.</summary>
        private static string LoadResource()
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("MosquitoNetCalculator.Resources.ai-system-prompt.md")
                ?? throw new InvalidOperationException(
                    "Embedded resource MosquitoNetCalculator.Resources.ai-system-prompt.md not found. " +
                    "Check that <EmbeddedResource Include=\"Resources\\ai-system-prompt.md\" /> is present in " +
                    "MosquitoNetCalculator.csproj and the project has been rebuilt.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd().TrimEnd('\r', '\n') + "\n";
        }
    }
}
