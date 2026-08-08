using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// Lightweight markdown-to-Inlines renderer for WPF TextBlocks.
    /// Attached property approach: bind <c>local:MarkdownRenderer.Text="{Binding Text}"</c>
    /// on any TextBlock and the Inlines collection is automatically populated with
    /// styled <see cref="Run"/> elements for bold (**text**), italic (*text*),
    /// inline code (`text`), bullet points (• or -), and line/paragraph breaks.
    ///
    /// Typewriter-animated messages call <see cref="ParseToInlines"/> directly on
    /// the final tick so raw text is shown during animation and formatted text
    /// replaces it on completion (same UX as ChatGPT/Claude).
    /// </summary>
    public static class MarkdownRenderer
    {
        // ─── Attached Property ─────────────────────────────────────────────
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.RegisterAttached(
                "Text",
                typeof(string),
                typeof(MarkdownRenderer),
                new PropertyMetadata(string.Empty, OnTextChanged));

        public static void SetText(DependencyObject element, string value)
            => element.SetValue(TextProperty, value);

        public static string GetText(DependencyObject element)
            => (string)element.GetValue(TextProperty);

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBlock textBlock && e.NewValue is string text)
                ParseToInlines(text, textBlock);
        }

        // ─── Parser ───────────────────────────────────────────────────────

        // Regex captures: **bold**, *italic*, `code` (in that priority order).
        // The non-greedy .*? avoids over-matching across multiple formatting spans.
        private static readonly Regex InlinePattern = new(
            @"(\*\*.*?\*\*|\*[^\s\*].*?\*|`.*?`)",
            RegexOptions.Compiled);

        /// <summary>
        /// Clears the TextBlock's Inlines and repopulates them with formatted
        /// <see cref="Run"/> elements parsed from the markdown source text.
        /// Safe to call multiple times (clears first, then adds).
        /// </summary>
        public static void ParseToInlines(string text, TextBlock textBlock)
        {
            textBlock.Inlines.Clear();
            if (string.IsNullOrEmpty(text)) return;

            // Preserve the foreground set on the TextBlock itself (from XAML
            // or DynamicResource) as the default for all new Run elements.
            var defaultForeground = textBlock.Foreground
                ?? (Brush)Application.Current.FindResource("TextPrimary");

            var lines = text.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Normalize common bullet markers to a consistent bullet char.
                if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* "))
                    line = "\u2022 " + line.TrimStart()[2..];

                // Split the line by inline formatting markers, keeping the
                // markers themselves in the result (Regex.Split with groups).
                var parts = InlinePattern.Split(line);

                foreach (var part in parts)
                {
                    if (string.IsNullOrEmpty(part)) continue;

                    if (part is ['*', '*', .., '*', '*'] && part.Length >= 5)
                    {
                        // **bold**
                        textBlock.Inlines.Add(new Run(part[2..^2])
                        {
                            FontWeight = FontWeights.SemiBold,
                            Foreground = defaultForeground
                        });
                    }
                    else if (part is ['*', .., '*'] && part.Length >= 3
                             && !char.IsWhiteSpace(part[1]))
                    {
                        // *italic* — but skip single * that are actually bullet markers
                        textBlock.Inlines.Add(new Run(part[1..^1])
                        {
                            FontStyle = FontStyles.Italic,
                            Foreground = defaultForeground
                        });
                    }
                    else if (part is ['`', .., '`'] && part.Length >= 3)
                    {
                        // `inline code`
                        textBlock.Inlines.Add(new Run(part[1..^1])
                        {
                            FontFamily = new FontFamily("Consolas, Courier New"),
                            FontSize = textBlock.FontSize * 0.92,
                        });
                    }
                    else
                    {
                        // Plain text — use default foreground from TextBlock
                        textBlock.Inlines.Add(new Run(part)
                        {
                            Foreground = defaultForeground
                        });
                    }
                }

                // Add line break between lines (but not after the last one).
                if (i < lines.Length - 1)
                    textBlock.Inlines.Add(new LineBreak());
            }
        }
    }
}
