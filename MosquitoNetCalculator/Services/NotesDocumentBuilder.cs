using System.Windows;
using System.Windows.Documents;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Builds a WPF <see cref="FlowDocument"/> from the stored notes markup
    /// (see <see cref="NotesFormatter"/>). This is what the on-screen notes editor
    /// shows, so the user sees the formatting itself instead of the markup markers.
    ///
    /// The markup string stays the ONLY stored format — it is what the printed КП,
    /// the PDF export and the saved order keep reading. The editor is just a view:
    /// <see cref="NotesMarkupWriter"/> converts it back on every edit.
    /// </summary>
    public static class NotesDocumentBuilder
    {
        /// <summary>
        /// Assembles the editor document. Empty markup yields an empty document
        /// (the editor grows its own paragraph as soon as the user types).
        /// </summary>
        public static FlowDocument Build(string? markup)
        {
            var document = new FlowDocument
            {
                // Отступ даёт сам редактор (Padding шаблона) — иначе текст
                // отходил бы от рамки дважды.
                PagePadding = new Thickness(0)
            };

            // Соседние пункты списка собираются в ОДИН List: так их понимает и
            // WPF-команда «Список» (EditingCommands.ToggleBullets), и writer
            // (префикс «- » при сериализации).
            List? openList = null;

            foreach (var line in NotesFormatter.Parse(markup))
            {
                if (line.IsListItem)
                {
                    openList ??= AddList(document);
                    openList.ListItems.Add(new ListItem(BuildParagraph(line)));
                    continue;
                }

                openList = null;
                document.Blocks.Add(BuildParagraph(line));
            }

            return document;
        }

        private static List AddList(FlowDocument document)
        {
            var list = new List
            {
                MarkerStyle = TextMarkerStyle.Disc,
                Margin = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(0)
            };

            document.Blocks.Add(list);
            return list;
        }

        private static Paragraph BuildParagraph(NoteLine line)
        {
            var paragraph = new Paragraph
            {
                // Пункт списка уже отступает маркером — второй отступ не нужен.
                Margin = line.IsListItem ? new Thickness(0) : new Thickness(0, 0, 0, 2)
            };

            foreach (var segment in line.Segments)
                paragraph.Inlines.Add(NotesRenderer.CreateEditableRun(segment));

            return paragraph;
        }
    }
}
