using System;
using System.IO;
using System.Windows;
using MosquitoNetCalculator;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    public sealed class AiAssistantOverlayLayoutTests
    {
        [Theory]
        [InlineData(true, false, 160, 0)]
        [InlineData(false, false, 52, 0)]
        [InlineData(true, true, 160, 380)]
        [InlineData(false, true, 52, 380)]
        public void ComputeCalculationViewTargetMargin_ComputesExpectedThickness(
            bool isNavExpanded, bool isAiOpenInMaximized, double expectedLeft, double expectedRight)
        {
            var margin = MainWindow.ComputeCalculationViewTargetMargin(isNavExpanded, isAiOpenInMaximized);

            Assert.Equal(expectedLeft, margin.Left);
            Assert.Equal(0, margin.Top);
            Assert.Equal(expectedRight, margin.Right);
            Assert.Equal(0, margin.Bottom);
        }

        [Theory]
        [InlineData(1920, 1000, 0, 0, 380, 10, true)]
        [InlineData(1920, 1000, 600, 0, 380, 10, true)] // 600 + 1000 + 380 = 1980 > 1920, but center fits (left = (1920-1380)/2 = 270)
        [InlineData(1390, 1000, 0, 0, 380, 10, false)] // < 1440px work area threshold -> always uses in-window overlay
        [InlineData(1280, 1000, 0, 0, 380, 10, false)]
        public void CanFitAiWindowSideBySide_ChecksAvailableWidthCorrectly(
            double screenWidth, double mainWidth, double mainLeft, double screenLeft, double aiWidth, double spacing, bool expected)
        {
            bool fits = MainWindow.CanFitAiWindowSideBySide(screenWidth, mainWidth, mainLeft, screenLeft, aiWidth, spacing);
            Assert.Equal(expected, fits);
        }

        [Fact]
        public void MainWindowXaml_AiOverlay_IsRightDocked_HasSlideTransform_AndNoBlockingBackdrop()
        {
            var xaml = File.ReadAllText(LocateSource("MainWindow.xaml"));

            // AiOverlay must be constrained to the right edge with width 380
            Assert.Contains("x:Name=\"AiOverlay\"", xaml);
            Assert.Contains("HorizontalAlignment=\"Right\" Width=\"380\"", xaml);

            // AiBackdrop was removed so it does not block mouse clicks over CalculationView
            Assert.DoesNotContain("x:Name=\"AiBackdrop\"", xaml);

            // AiSlideTransform must exist for smooth entrance/exit
            Assert.Contains("x:Name=\"AiSlideTransform\"", xaml);
        }

        private static string LocateSource(string relativePath)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "MosquitoNetCalculator", relativePath);
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                $"Could not locate source file '{relativePath}' from '{AppContext.BaseDirectory}'.");
        }
    }
}
