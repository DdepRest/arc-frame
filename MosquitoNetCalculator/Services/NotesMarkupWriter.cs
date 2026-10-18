using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Serializes the notes editor's <see cref="FlowDocument"/> back into the stored
    /// lightweight markup (<see cref="NotesFormatter"/>). Round-trip counterpart of
    /// <see cref="NotesDocumentBuilder"/>.
    ///
    /// Why per line: the parser is line-based, so every emitted line is made
    /// self-contained (open tags are closed at the end of the line and reopened on
    /// the next one). Formatting is read from each element's EFFECTIVE value, which
    /// means everything the editor can produce is covered: a value set directly on a
    /// <see cref="Run"/>, a value inherited from a <see cref="Span"/>, or WPF's own
    /// caret formatting applied to freshly typed text.
    /// </summary>
    public static class NotesMarkupWriter
    {
        /// <summary>
        /// Converts the document into markup text. <paramref name="defaultForeground"/>
        /// is the editor's own text colour: text painted with it carries no colour tag
        /// (the theme keeps it), anything else is wrapped into <c>[color=#RRGGBB]</c>.
        /// </summary>
        public static string ToMarkup(FlowDocument? document, Brush? defaultForeground = null)
        {
            if (document == null)
                return string.Empty;

            var baseColor = (defaultForeground as SolidColorBrush)?.Color ?? Colors.Black;
            var lines = new List<string>();

            foreach (var block in document.Blocks)
                AppendBlock(block, lines, baseColor, string.Empty);

            return string.Join("\n", lines);
        }

        private static void AppendBlock(Block block, List<string> lines, Color baseColor, string marker)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    foreach (var line in CollectLines(paragraph.Inlines, baseColor))
                        lines.Add(marker + EmitLine(line));
                    break;

                case List list:
                    foreach (var item in list.ListItems)
                        foreach (var inner in item.Blocks)
                            AppendBlock(inner, lines, baseColor, "- ");
                    break;

                case Section section:
                    foreach (var inner in section.Blocks)
                        AppendBlock(inner, lines, baseColor, marker);
                    break;

                default:
                    // Незнакомый блок (таблица и т.п.): сохраняем хотя бы текст,
                    // чтобы правка примечаний не потеряла содержимое.
                    lines.Add(marker + PlainText(block));
                    break;
            }
        }

        /// <summary>
        /// Flattens a paragraph into lines of formatted spans. A <see cref="LineBreak"/>
        /// starts a new line — the stored format has no multi-line spans.
        /// </summary>
        private static List<List<StyledSpan>> CollectLines(InlineCollection inlines, Color baseColor)
        {
            var lines = new List<List<StyledSpan>> { new() };

            void Walk(InlineCollection collection)
            {
                foreach (var inline in collection)
                {
                    switch (inline)
                    {
                        case Run run:
                            AppendText(lines, run.Text, run, baseColor);
                            break;

                        case Span span:
                            // Hyperlink тоже Span: текст сохраняем, подчёркивание —
                            // нет, формат примечаний его не знает.
                            Walk(span.Inlines);
                            break;

                        case LineBreak:
                            lines.Add(new List<StyledSpan>());
                            break;

                        case AnchoredBlock anchored:
                            AppendText(lines, PlainText(anchored), anchored, baseColor);
                            break;

                        default:
                            // InlineUIContainer и прочие вставки: текста для хранения нет.
                            break;
                    }
                }
            }

            Walk(inlines);
            return lines;
        }

        private static void AppendText(List<List<StyledSpan>> lines, string text, TextElement element, Color baseColor)
        {
            if (string.IsNullOrEmpty(text))
                return;

            bool bold = IsBold(element);
            bool italic = IsItalic(element);
            var color = EffectiveColor(element, baseColor);

            var parts = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                    lines.Add(new List<StyledSpan>());

                if (parts[i].Length > 0)
                    lines[^1].Add(new StyledSpan(parts[i], bold, italic, color));
            }
        }

        /// <summary>
        /// Emits one line: markers are written only where the formatting actually
        /// changes, so «**a****b**» cannot be produced for one continuous bold span.
        /// </summary>
        private static string EmitLine(List<StyledSpan> spans)
        {
            var merged = Merge(spans);
            if (merged.Count == 0)
                return string.Empty;

            var builder = new StringBuilder();
            bool bold = false;
            bool italic = false;
            Color? color = null;

            foreach (var span in merged)
            {
                if (span.Color != color)
                {
                    if (color != null)
                        builder.Append("[/color]");
                    if (span.Color is { } value)
                        builder.Append("[color=").Append(ToHex(value)).Append(']');
                    color = span.Color;
                }

                if (span.Italic != italic)
                {
                    builder.Append('*');
                    italic = span.Italic;
                }

                if (span.Bold != bold)
                {
                    builder.Append("**");
                    bold = span.Bold;
                }

                builder.Append(span.Text);
            }

            if (color != null)
                builder.Append("[/color]");
            if (italic)
                builder.Append('*');
            if (bold)
                builder.Append("**");

            return builder.ToString();
        }

        /// <summary>
        /// Соседние куски с одинаковым форматированием сливаются: WPF дробит текст
        /// на Run'ы как угодно (в том числе на каждый набранный символ), а в
        /// хранимой строке это дало бы мусор из пар маркеров.
        /// </summary>
        private static List<StyledSpan> Merge(List<StyledSpan> spans)
        {
            var merged = new List<StyledSpan>(spans.Count);

            foreach (var span in spans)
            {
                if (span.Text.Length == 0)
                    continue;

                if (merged.Count > 0)
                {
                    var last = merged[^1];
                    if (last.Bold == span.Bold && last.Italic == span.Italic && last.Color == span.Color)
                    {
                        merged[^1] = last with { Text = last.Text + span.Text };
                        continue;
                    }
                }

                merged.Add(span);
            }

            return merged;
        }

        private static bool IsBold(TextElement element)
            => element.FontWeight.ToOpenTypeWeight() >= FontWeights.Bold.ToOpenTypeWeight();

        private static bool IsItalic(TextElement element)
            => element.FontStyle != FontStyles.Normal;

        /// <summary>
        /// Цвет темы (унаследованный от редактора) тегом не считается — иначе
        /// каждый обычный Run получал бы <c>[color=…]</c> на цвет текущей темы.
        /// </summary>
        private static Color? EffectiveColor(TextElement element, Color baseColor)
            => element.Foreground is SolidColorBrush brush && brush.Color != baseColor ? brush.Color : null;

        private static string PlainText(TextElement element)
            => new TextRange(element.ContentStart, element.ContentEnd).Text.TrimEnd('\r', '\n');

        private static string ToHex(Color color)
            => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        /// <summary>Кусок текста с одинаковым форматированием внутри одной строки.</summary>
        private readonly record struct StyledSpan(string Text, bool Bold, bool Italic, Color? Color);
    }
}
