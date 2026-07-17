using System.Linq;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class NotesFormatterTests
    {
        [Fact]
        public void Parse_EmptyString_ReturnsEmptyLines()
        {
            var result = NotesFormatter.Parse("");
            Assert.Empty(result);
        }

        [Fact]
        public void Parse_WhitespaceString_ReturnsEmptyLines()
        {
            var result = NotesFormatter.Parse("   \n\t\n  ");
            Assert.Empty(result);
        }

        [Fact]
        public void Parse_PlainText_ReturnsSingleSegment()
        {
            var result = NotesFormatter.Parse("Позвонить за час");
            Assert.Single(result);
            var line = result[0];
            Assert.False(line.IsListItem);
            Assert.Single(line.Segments);
            Assert.Equal("Позвонить за час", line.Segments[0].Text);
            Assert.False(line.Segments[0].IsBold);
            Assert.False(line.Segments[0].IsItalic);
            Assert.Null(line.Segments[0].ColorTag);
        }

        [Fact]
        public void Parse_BoldText_WrapsSegmentWithBoldFlag()
        {
            var result = NotesFormatter.Parse("**важно**");
            Assert.Single(result);
            Assert.Single(result[0].Segments);
            Assert.Equal("важно", result[0].Segments[0].Text);
            Assert.True(result[0].Segments[0].IsBold);
        }

        [Fact]
        public void Parse_ItalicText_WrapsSegmentWithItalicFlag()
        {
            var result = NotesFormatter.Parse("*курсив*");
            Assert.Single(result);
            Assert.Single(result[0].Segments);
            Assert.Equal("курсив", result[0].Segments[0].Text);
            Assert.True(result[0].Segments[0].IsItalic);
        }

        [Fact]
        public void Parse_ColorText_WrapsSegmentWithColorTag()
        {
            var result = NotesFormatter.Parse("[color=#D32F2F]красный[/color]");
            Assert.Single(result);
            Assert.Single(result[0].Segments);
            Assert.Equal("красный", result[0].Segments[0].Text);
            Assert.Equal("#D32F2F", result[0].Segments[0].ColorTag);
        }

        [Fact]
        public void Parse_ListItem_MarksLineAsListItemAndStripsBullet()
        {
            var result = NotesFormatter.Parse("- первый пункт\n- второй пункт");
            Assert.Equal(2, result.Count);
            Assert.True(result[0].IsListItem);
            Assert.Single(result[0].Segments);
            Assert.Equal("первый пункт", result[0].Segments[0].Text);
            Assert.True(result[1].IsListItem);
            Assert.Single(result[1].Segments);
            Assert.Equal("второй пункт", result[1].Segments[0].Text);
        }

        [Fact]
        public void Parse_MixedFormatting_SplitsSegmentsCorrectly()
        {
            var result = NotesFormatter.Parse("**Bold** and *italic* and [color=red]red[/color]");
            Assert.Single(result);
            var segments = result[0].Segments;
            Assert.Equal(5, segments.Count);

            Assert.Equal("Bold", segments[0].Text);
            Assert.True(segments[0].IsBold);

            Assert.Equal(" and ", segments[1].Text);
            Assert.False(segments[1].IsBold);
            Assert.False(segments[1].IsItalic);

            Assert.Equal("italic", segments[2].Text);
            Assert.True(segments[2].IsItalic);

            Assert.Equal(" and ", segments[3].Text);

            Assert.Equal("red", segments[4].Text);
            Assert.Equal("red", segments[4].ColorTag);
        }

        [Fact]
        public void Parse_NestedBoldAndItalic_KeepsBothFlags()
        {
            var result = NotesFormatter.Parse("**bold *and* italic**");
            Assert.Single(result);
            var segments = result[0].Segments;
            Assert.Equal(3, segments.Count);

            Assert.Equal("bold ", segments[0].Text);
            Assert.True(segments[0].IsBold);
            Assert.False(segments[0].IsItalic);

            Assert.Equal("and", segments[1].Text);
            Assert.True(segments[1].IsBold);
            Assert.True(segments[1].IsItalic);

            Assert.Equal(" italic", segments[2].Text);
            Assert.True(segments[2].IsBold);
            Assert.False(segments[2].IsItalic);
        }

        [Fact]
        public void Parse_ColorWithSpacesAroundEquals_ParsesColorValue()
        {
            var result = NotesFormatter.Parse("[color = #1976D2]синий[/color]");
            Assert.Single(result);
            Assert.Single(result[0].Segments);
            Assert.Equal("синий", result[0].Segments[0].Text);
            Assert.Equal("#1976D2", result[0].Segments[0].ColorTag);
        }

        [Fact]
        public void Parse_UnclosedColorTag_AppliesColorToEndOfLine()
        {
            var result = NotesFormatter.Parse("[color=red]не закрыт");
            Assert.Single(result);
            Assert.Single(result[0].Segments);
            Assert.Equal("не закрыт", result[0].Segments[0].Text);
            Assert.Equal("red", result[0].Segments[0].ColorTag);
        }

        [Fact]
        public void Parse_MultilineText_PreservesLineCount()
        {
            var result = NotesFormatter.Parse("line1\nline2\r\nline3");
            Assert.Equal(3, result.Count);
            Assert.Equal("line1", result[0].Segments[0].Text);
            Assert.Equal("line2", result[1].Segments[0].Text);
            Assert.Equal("line3", result[2].Segments[0].Text);
        }
    }
}
