using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace MosquitoNetCalculator.Tests
{
    /// <summary>
    /// XAML binding regression guards.
    ///
    /// These tests do NOT load the WPF tree (that would require
    /// <c>Application.Current</c> + a Dispatcher under STA) — instead they
    /// lock the static contract by parsing each <c>.xaml</c> file and
    /// asserting that the right <c>UpdateSourceTrigger</c> sits on the right
    /// editable column.
    ///
    /// v3.37-pre bug: in tab «Расчёт» column «Цена»,
    /// <c>UpdateSourceTrigger=LostFocus</c> meant <c>Width="Auto"</c> sized
    /// the column for the formatted OLD value, so typing 15000 truncated the
    /// visible editing cell to 5000. Fix: <c>PropertyChanged</c> on the
    /// Price binding so <c>Width="Auto"</c> tracks per-keystroke.
    /// See docs/arc/GOTCHAS.md#13.
    /// </summary>
    public class DataGridBindingsTests
    {
        // Test runner CWD is the test binary dir, not the repo root. Walk up
        // until we find the marker file (MosquitoNetCalculator.csproj). Same
        // pattern as ManualChecklistTests.LocateSourceProjectDir.
        private static readonly string RepoRoot = LocateRepoRoot();

        private static string ReadXaml(string relativePath) =>
            File.ReadAllText(Path.Combine(RepoRoot, relativePath));

        private static string LocateRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null
                   && !File.Exists(Path.Combine(dir.FullName, "MosquitoNetCalculator", "MosquitoNetCalculator.csproj")))
                dir = dir.Parent;
            if (dir == null)
                throw new InvalidOperationException(
                    $"Could not locate repo root from {AppContext.BaseDirectory} — no `MosquitoNetCalculator/MosquitoNetCalculator.csproj` found.");
            return dir.FullName;
        }

        // ─── Regression guard for ГОТЧА #13 ──────────────────────────

        [Fact]
        public void Расчёт_Цена_UsesPropertyChanged_So_AutoWidthTracksTyping()
        {
            var content = ReadXaml("MosquitoNetCalculator/Controls/OrderItemsControl.xaml");
            var trigger = GetColumnUpdateTrigger(content, columnHeader: "Цена");
            Assert.Equal("PropertyChanged", trigger);
        }

        [Fact]
        public void Цены_Цена_UsesPropertyChanged_So_AutoWidthTracksTyping()
        {
            var content = ReadXaml("MosquitoNetCalculator/Controls/PricesControl.xaml");
            // Same helper — it handles BOTH the multi-line
            //   <DataGridTextColumn.Binding><Binding …/></DataGridTextColumn.Binding>
            // and the single-line
            //   Binding="{Binding …, UpdateSourceTrigger=…}"
            // forms.
            var trigger = GetColumnUpdateTrigger(content, columnHeader: "Цена, руб.");
            Assert.Equal("PropertyChanged", trigger);
        }

        // ─── Guardrails on neighboring columns we did NOT change ─────

        [Fact]
        public void Расчёт_Ширина_StillUsesPropertyChanged()
        {
            var content = ReadXaml("MosquitoNetCalculator/Controls/OrderItemsControl.xaml");
            var trigger = GetColumnUpdateTrigger(content, columnHeader: "Ширина");
            Assert.Equal("PropertyChanged", trigger);
        }

        [Fact]
        public void Расчёт_Высота_StillUsesPropertyChanged()
        {
            var content = ReadXaml("MosquitoNetCalculator/Controls/OrderItemsControl.xaml");
            var trigger = GetColumnUpdateTrigger(content, columnHeader: "Высота");
            Assert.Equal("PropertyChanged", trigger);
        }

        [Fact]
        public void Расчёт_Колво_StillUsesPropertyChanged()
        {
            var content = ReadXaml("MosquitoNetCalculator/Controls/OrderItemsControl.xaml");
            var trigger = GetColumnUpdateTrigger(content, columnHeader: "Кол-во");
            Assert.Equal("PropertyChanged", trigger);
        }

        // ─── Helper ──────────────────────────────────────────────────

        /// <summary>
        /// Finds the editable <c>&lt;DataGridTextColumn&gt;</c> whose
        /// <c>Header</c> matches <paramref name="columnHeader"/> exactly and
        /// returns its <c>UpdateSourceTrigger</c> value (or <c>null</c> if
        /// no <c>UpdateSourceTrigger</c> is present).
        ///
        /// Supports both:
        /// <list type="bullet">
        ///   <item>multi-line
        ///     <c>&lt;DataGridTextColumn.Binding&gt;&lt;Binding … UpdateSourceTrigger="X"/&gt;&lt;…&gt;</c>
        ///     (used in OrderItemsControl.xaml);</item>
        ///   <item>single-line
        ///     <c>Binding="{Binding …, UpdateSourceTrigger=X}"</c>
        ///     (used in PricesControl.xaml).</item>
        /// </list>
        ///
        /// Strategy: extract the single <c>&lt;DataGridTextColumn&gt;</c>
        /// element bounded by its opening tag (matched via Header attribute)
        /// and either the self-closing <c>/&gt;</c> or the
        /// <c>&lt;/DataGridTextColumn&gt;</c> closing tag. We work on the
        /// extracted element only, so we never spill into the next column.
        /// </summary>
        private static string? GetColumnUpdateTrigger(string xaml, string columnHeader)
        {
            var columnElement = ExtractDataGridTextColumnElement(xaml, columnHeader);
            if (columnElement == null) return null;

            // Multi-line form first — the child <DataGridTextColumn.Binding> element.
            var multiline = Regex.Match(
                columnElement,
                @"<DataGridTextColumn\.Binding>\s*<Binding\b[^/>]*UpdateSourceTrigger=""(?<t>[^""]+)""",
                RegexOptions.Singleline);
            if (multiline.Success)
                return multiline.Groups["t"].Value;

            // Single-line form — `Binding="{Binding …, UpdateSourceTrigger=X}"`.
            // Use [^"]*? (non-greedy, not-quote) so nested `{StaticResource ...}`
            // braces inside the Binding value don't truncate the match early.
            // The Binding value is wrapped in `"..."` so a non-quote char class
            // bound by the trailing pattern works here even with arbitrary
            // nested `{ }`.
            var singleline = Regex.Match(
                columnElement,
                @"Binding\s*=\s*""\{Binding\b[^""]*?UpdateSourceTrigger\s*=\s*(?<t>[^,""}]+)",
                RegexOptions.Singleline);
            if (singleline.Success)
                return singleline.Groups["t"].Value.Trim();

            return null;
        }

        /// <summary>
        /// Returns the substring between the opening tag of the
        /// <c>&lt;DataGridTextColumn&gt;</c> whose <c>Header</c> matches the
        /// given string and the matching closing <c>&lt;/DataGridTextColumn&gt;</c>
        /// (or, when absent, the column's self-closing <c>/&gt;</c>).
        /// Bounded at <c>4096</c> chars to avoid runaway scans on pathological inputs.
        ///
        /// The explicit-close path runs first because nested elements inside a
        /// <c>DataGridTextColumn</c> (its <c>&lt;DataGridTextColumn.Binding&gt;</c>,
        /// its <c>&lt;DataGridTextColumn.ElementStyle&gt;</c>, etc.) often contain
        /// self-closing tags — picking the first <c>/&gt;</c> would truncate the
        /// slice before the column's actual terminator.
        /// </summary>
        private static string? ExtractDataGridTextColumnElement(string xaml, string columnHeader)
        {
            var headerAttr = $"Header=\"{columnHeader}\"";
            var headerIdx = xaml.IndexOf(headerAttr, StringComparison.Ordinal);
            if (headerIdx < 0) return null;

            // Walk backward to find `<DataGridTextColumn` opening tag — Header
            // is often preceded by whitespace/newlines, not directly by the tag,
            // so we scan back to the nearest `<`.
            var tagStart = xaml.LastIndexOf("<DataGridTextColumn", headerIdx, StringComparison.Ordinal);
            if (tagStart < 0) return null;

            // Search for the closing tag forward. Limit to first 4096 chars so
            // we never walk the whole file.
            const int maxScan = 4096;
            var searchStart = headerIdx + headerAttr.Length;
            var remaining = Math.Min(maxScan, xaml.Length - searchStart);
            if (remaining <= 0) return null;
            var searchSlice = xaml.Substring(searchStart, remaining);

            // Prefer the explicit close — it's the column's actual terminator
            // even when nested self-closing children would have lied about the
            // bound earlier.
            var explicitClose = searchSlice.IndexOf("</DataGridTextColumn>", StringComparison.Ordinal);
            if (explicitClose >= 0)
                return xaml.Substring(tagStart, searchStart + explicitClose - tagStart + "</DataGridTextColumn>".Length);

            // No explicit close within the scan window — fall back to the
            // column's self-closing `/>` (column whose body is empty or
            // single-line Binding).
            var selfClose = searchSlice.IndexOf("/>", StringComparison.Ordinal);
            if (selfClose >= 0)
                return xaml.Substring(tagStart, searchStart + selfClose - tagStart);

            return null;
        }
    }
}
