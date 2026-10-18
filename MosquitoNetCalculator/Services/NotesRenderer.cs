using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// WPF-specific renderer for parsed notes.
    /// Converts <see cref="NoteLine"/>/<see cref="NoteSegment"/> into WPF <see cref="Inline"/>
    /// elements so the printed КП and the live preview render identically.
    /// </summary>
    public static class NotesRenderer
    {
        /// <summary>
        /// Converts a parsed note line into a sequence of WPF inlines.
        /// Used by the print pipeline (<c>FlowDocumentBuilder</c>): every attribute
        /// is stated explicitly, because the printed page has no theme to inherit from.
        /// </summary>
        public static IEnumerable<Inline> ToInlines(NoteLine line)
        {
            var inlines = new List<Inline>();

            if (line.IsListItem)
                inlines.Add(new Run("• ") { FontWeight = FontWeights.Bold });

            foreach (var segment in line.Segments)
                inlines.Add(CreateRun(segment));

            return inlines;
        }

        /// <summary>
        /// Run for the printed КП: weight and style are pinned (Normal/Bold,
        /// Normal/Italic) so the page renders identically in any environment.
        /// </summary>
        public static Run CreateRun(NoteSegment segment)
        {
            var run = new Run(segment.Text)
            {
                FontWeight = segment.IsBold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = segment.IsItalic ? FontStyles.Italic : FontStyles.Normal
            };

            if (TryCreateBrush(segment.ColorTag, out var brush))
                run.Foreground = brush;

            return run;
        }

        /// <summary>
        /// Run for the on-screen editor (<see cref="NotesDocumentBuilder"/>): only the
        /// attributes the stored markup actually carries are set, so everything else
        /// (theme colour, size, family) keeps inheriting from the editor. Pinning
        /// <c>FontWeight.Normal</c> here would fight the editor's own formatting
        /// commands — that is why the editor gets its own factory.
        /// </summary>
        public static Run CreateEditableRun(NoteSegment segment)
        {
            var run = new Run(segment.Text);

            if (segment.IsBold)
                run.FontWeight = FontWeights.Bold;
            if (segment.IsItalic)
                run.FontStyle = FontStyles.Italic;
            if (TryCreateBrush(segment.ColorTag, out var brush))
                run.Foreground = brush;

            return run;
        }

        /// <summary>
        /// Resolves a markup colour tag (<c>#RRGGBB</c> or a known colour name) into
        /// a brush. Returns <c>false</c> for unknown values so callers fall back to
        /// the default foreground instead of throwing.
        /// </summary>
        public static bool TryCreateBrush(string? colorTag, out Brush brush)
        {
            brush = Brushes.Black;

            if (string.IsNullOrWhiteSpace(colorTag))
                return false;

            try
            {
                if (new BrushConverter().ConvertFromString(colorTag) is not Brush parsed)
                    return false;

                brush = parsed;
                return true;
            }
            catch
            {
                // Unknown color name/hex — fall back to default foreground.
                return false;
            }
        }
    }
}
