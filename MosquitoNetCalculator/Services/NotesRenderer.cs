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
        /// </summary>
        public static IEnumerable<Inline> ToInlines(NoteLine line)
        {
            var inlines = new List<Inline>();

            if (line.IsListItem)
                inlines.Add(new Run("• ") { FontWeight = FontWeights.Bold });

            foreach (var segment in line.Segments)
            {
                var run = new Run(segment.Text)
                {
                    FontWeight = segment.IsBold ? FontWeights.Bold : FontWeights.Normal,
                    FontStyle = segment.IsItalic ? FontStyles.Italic : FontStyles.Normal
                };

                if (!string.IsNullOrWhiteSpace(segment.ColorTag))
                {
                    try
                    {
                        run.Foreground = (Brush)new BrushConverter().ConvertFromString(segment.ColorTag)!;
                    }
                    catch
                    {
                        // Unknown color name/hex — fall back to default foreground.
                    }
                }

                inlines.Add(run);
            }

            return inlines;
        }
    }
}
