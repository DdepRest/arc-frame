using System;
using System.IO;
using System.Windows;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    public class WindowChromeContrastTests
    {
        [Fact]
        public void MainWindowXaml_WindowChrome_HasGlassFrameThicknessOne()
        {
            var xaml = File.ReadAllText(LocateSource("MainWindow.xaml"));
            Assert.Contains("GlassFrameThickness=\"1\"", xaml);
            Assert.Contains("x:Name=\"WindowBorder\"", xaml);
            Assert.Contains("BorderBrush=\"{DynamicResource WindowBorderBrush}\"", xaml);
        }

        [Fact]
        public void BrushesXaml_DefinesWindowBorderBrush()
        {
            var xaml = File.ReadAllText(LocateSource("Themes/Brushes.xaml"));
            Assert.Contains("<SolidColorBrush x:Key=\"WindowBorderBrush\" Color=\"#B8B8C4\"/>", xaml);
        }

        [Fact]
        public void ThemeService_DefinesWindowBorderBrushInLightAndDark()
        {
            var light = ThemeService.LightColorsForTests();
            var dark = ThemeService.DarkColorsForTests();

            Assert.True(light.ContainsKey("WindowBorderBrush"));
            Assert.Equal("#B8B8C4", light["WindowBorderBrush"]);

            Assert.True(dark.ContainsKey("WindowBorderBrush"));
            Assert.Equal("#46464F", dark["WindowBorderBrush"]);
        }

        [Fact]
        public void MainWindow_BorderThickness_TogglesWithWindowState()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new MainWindow();
                window.WindowState = WindowState.Normal;
                window.UpdateWindowBorderThickness();
                Assert.Equal(new Thickness(1), window.WindowBorder.BorderThickness);

                window.WindowState = WindowState.Maximized;
                window.UpdateWindowBorderThickness();
                Assert.Equal(new Thickness(0), window.WindowBorder.BorderThickness);
            });
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