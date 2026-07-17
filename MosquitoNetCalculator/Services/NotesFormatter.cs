using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Represents a single formatted segment inside a note line.
    /// </summary>
    public class NoteSegment
    {
        public string Text { get; set; } = string.Empty;
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public string? ColorTag { get; set; }
    }

    /// <summary>
    /// Represents a single line of notes, optionally a list item.
    /// </summary>
    public class NoteLine
    {
        public bool IsListItem { get; set; }
        public List<NoteSegment> Segments { get; } = new List<NoteSegment>();
    }

    /// <summary>
    /// Parses a lightweight markup syntax for client notes.
    /// Supports:
    ///   - **bold**
    ///   - *italic*
    ///   - [color=#RRGGBB]colored text[/color] or [color=Red]colored text[/color]
    ///   - - list item
    /// Plain text without markup renders unchanged.
    /// </summary>
    public static class NotesFormatter
    {
        private static readonly Regex TokenRegex = new Regex(
            @"\*\*|\*|\[color\s*=\s*[^\]]+\]|\[/color\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ColorOpenRegex = new Regex(
            @"\[color\s*=\s*([^\]]+)\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Parses the notes text into a list of formatted lines.
        /// </summary>
        public static List<NoteLine> Parse(string? input)
        {
            var lines = new List<NoteLine>();
            if (string.IsNullOrWhiteSpace(input))
                return lines;

            var rawLines = input.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            foreach (var rawLine in rawLines)
            {
                lines.Add(ParseLine(rawLine));
            }

            return lines;
        }

        private static NoteLine ParseLine(string rawLine)
        {
            var line = new NoteLine();
            string text = rawLine;

            if (text.StartsWith("- "))
            {
                line.IsListItem = true;
                text = text.Substring(2);
            }

            bool bold = false;
            bool italic = false;
            string? color = null;

            var matches = TokenRegex.Matches(text);
            int position = 0;

            foreach (Match match in matches)
            {
                // Text before the token is a content segment.
                if (match.Index > position)
                {
                    line.Segments.Add(new NoteSegment
                    {
                        Text = text.Substring(position, match.Index - position),
                        IsBold = bold,
                        IsItalic = italic,
                        ColorTag = color
                    });
                }

                string token = match.Value;
                if (token == "**")
                    bold = !bold;
                else if (token == "*")
                    italic = !italic;
                else if (token.Equals("[/color]", StringComparison.OrdinalIgnoreCase))
                    color = null;
                else if (token.StartsWith("[color", StringComparison.OrdinalIgnoreCase))
                {
                    var colorMatch = ColorOpenRegex.Match(token);
                    if (colorMatch.Success)
                        color = colorMatch.Groups[1].Value.Trim();
                }

                position = match.Index + match.Length;
            }

            // Trailing text after the last token.
            if (position < text.Length)
            {
                line.Segments.Add(new NoteSegment
                {
                    Text = text.Substring(position),
                    IsBold = bold,
                    IsItalic = italic,
                    ColorTag = color
                });
            }

            return line;
        }
    }
}
