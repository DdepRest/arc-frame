using System.Linq;
using System.Windows;
using System.Windows.Documents;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class NotesRendererTests
    {
        [Fact]
        public void ToInlines_PlainText_ReturnsSingleRun()
        {
            var line = NotesFormatter.Parse("Позвонить за час").Single();

            WpfTestHelper.RunOnSta(() =>
            {
                var inlines = NotesRenderer.ToInlines(line).ToList();
                Assert.Single(inlines);
                var run = Assert.IsType<Run>(inlines[0]);
                Assert.Equal("Позвонить за час", run.Text);
                Assert.Equal(FontWeights.Normal, run.FontWeight);
                Assert.Equal(FontStyles.Normal, run.FontStyle);
            });
        }

        [Fact]
        public void ToInlines_ListItem_StartsWithBulletRun()
        {
            var line = NotesFormatter.Parse("- первый пункт").Single();

            WpfTestHelper.RunOnSta(() =>
            {
                var inlines = NotesRenderer.ToInlines(line).ToList();
                Assert.Equal(2, inlines.Count);
                var bullet = Assert.IsType<Run>(inlines[0]);
                Assert.Equal("• ", bullet.Text);
                Assert.Equal(FontWeights.Bold, bullet.FontWeight);
                var text = Assert.IsType<Run>(inlines[1]);
                Assert.Equal("первый пункт", text.Text);
            });
        }

        [Fact]
        public void ToInlines_BoldSegment_AppliesBoldFontWeight()
        {
            var line = NotesFormatter.Parse("**важно**").Single();

            WpfTestHelper.RunOnSta(() =>
            {
                var inlines = NotesRenderer.ToInlines(line).ToList();
                Assert.Single(inlines);
                var run = Assert.IsType<Run>(inlines[0]);
                Assert.Equal("важно", run.Text);
                Assert.Equal(FontWeights.Bold, run.FontWeight);
            });
        }

        [Fact]
        public void ToInlines_ItalicSegment_AppliesItalicFontStyle()
        {
            var line = NotesFormatter.Parse("*курсив*").Single();

            WpfTestHelper.RunOnSta(() =>
            {
                var inlines = NotesRenderer.ToInlines(line).ToList();
                Assert.Single(inlines);
                var run = Assert.IsType<Run>(inlines[0]);
                Assert.Equal("курсив", run.Text);
                Assert.Equal(FontStyles.Italic, run.FontStyle);
            });
        }

        [Fact]
        public void ToInlines_ColoredSegment_AppliesForegroundBrush()
        {
            var line = NotesFormatter.Parse("[color=#D32F2F]красный[/color]").Single();

            WpfTestHelper.RunOnSta(() =>
            {
                var inlines = NotesRenderer.ToInlines(line).ToList();
                Assert.Single(inlines);
                var run = Assert.IsType<Run>(inlines[0]);
                Assert.Equal("красный", run.Text);
                Assert.NotNull(run.Foreground);
            });
        }

        [Fact]
        public void ToInlines_MixedFormatting_ReturnsMultipleRuns()
        {
            var line = NotesFormatter.Parse("**Bold** and *italic*").Single();

            WpfTestHelper.RunOnSta(() =>
            {
                var inlines = NotesRenderer.ToInlines(line).ToList();
                Assert.Equal(3, inlines.Count);

                var boldRun = Assert.IsType<Run>(inlines[0]);
                Assert.Equal("Bold", boldRun.Text);
                Assert.Equal(FontWeights.Bold, boldRun.FontWeight);

                var plainRun = Assert.IsType<Run>(inlines[1]);
                Assert.Equal(" and ", plainRun.Text);
                Assert.Equal(FontWeights.Normal, plainRun.FontWeight);

                var italicRun = Assert.IsType<Run>(inlines[2]);
                Assert.Equal("italic", italicRun.Text);
                Assert.Equal(FontStyles.Italic, italicRun.FontStyle);
            });
        }

        [Fact]
        public void ToInlines_InvalidColorTag_FallsBackToDefaultForeground()
        {
            var line = NotesFormatter.Parse("[color=invalid]text[/color]").Single();

            WpfTestHelper.RunOnSta(() =>
            {
                var inlines = NotesRenderer.ToInlines(line).ToList();
                Assert.Single(inlines);
                var run = Assert.IsType<Run>(inlines[0]);
                Assert.Equal("text", run.Text);
                // Default WPF foreground is black; the important thing is no exception is thrown.
                Assert.Equal(System.Windows.Media.Brushes.Black, run.Foreground);
            });
        }
    }
}
