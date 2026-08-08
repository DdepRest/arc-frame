using System.Collections;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace MosquitoNetCalculator.Tests.Helpers
{
    /// <summary>
    /// Shared helper for extracting plain text from WPF FlowDocument instances.
    /// Handles Paragraph, Table, Section and BlockUIContainer blocks recursively.
    /// Must be called on an STA thread.
    /// </summary>
    public static class FlowDocumentTextExtractor
    {
        /// <summary>Extracts all text from the given FlowDocument.</summary>
        public static string ExtractAllText(FlowDocument doc)
        {
            var sb = new StringBuilder();
            ExtractTextFromBlocks(doc.Blocks, sb);
            return sb.ToString();
        }

        private static void ExtractTextFromBlocks(IEnumerable blocks, StringBuilder sb)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case Paragraph p:
                        foreach (var inline in p.Inlines)
                            ExtractTextFromInline(inline, sb);
                        sb.Append(' ');
                        break;
                    case Table t:
                        foreach (var rowGroup in t.RowGroups)
                            foreach (var row in rowGroup.Rows)
                                foreach (var cell in row.Cells)
                                    ExtractTextFromBlocks(cell.Blocks, sb);
                        break;
                    case Section s:
                        ExtractTextFromBlocks(s.Blocks, sb);
                        break;
                    case BlockUIContainer bcu:
                        ExtractTextFromUiElement(bcu.Child, sb);
                        break;
                }
            }
        }

        private static void ExtractTextFromInline(Inline inline, StringBuilder sb)
        {
            switch (inline)
            {
                case Run r:
                    sb.Append(r.Text);
                    break;
                case Span s:
                    foreach (var child in s.Inlines)
                        ExtractTextFromInline(child, sb);
                    break;
            }
        }

        private static void ExtractTextFromUiElement(UIElement? element, StringBuilder sb)
        {
            if (element == null) return;

            if (element is TextBlock tb)
            {
                sb.Append(tb.Text);
                sb.Append(' ');
            }

            if (element is Panel panel)
            {
                foreach (UIElement child in panel.Children)
                    ExtractTextFromUiElement(child, sb);
            }
        }
    }
}
