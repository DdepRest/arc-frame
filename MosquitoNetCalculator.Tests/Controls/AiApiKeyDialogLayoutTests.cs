using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    public sealed class AiApiKeyDialogLayoutTests
    {
        [Fact]
        public void BottomActions_ArePinnedToWindowBottom_AndNeverClip()
        {
            var source = File.ReadAllText(LocateSource("Controls/AiApiKeyDialog.xaml"));
            var rowDefinitions = Regex.Match(
                source,
                @"<Grid\.RowDefinitions>(?<rows>[\s\S]*?)</Grid\.RowDefinitions>",
                RegexOptions.CultureInvariant);

            Assert.True(rowDefinitions.Success, "The AI settings dialog must define its root grid rows.");

            var rows = Regex.Matches(
                    rowDefinitions.Groups["rows"].Value,
                    @"<RowDefinition\s+Height=""(?<height>[^""]+)""\s*/>",
                    RegexOptions.CultureInvariant)
                .Select(match => match.Groups["height"].Value)
                .ToArray();

            // Three-zone layout: fixed header, scrollable middle, Auto footer.
            // WPF lays the Auto footer row out inside the fixed window, so the
            // buttons can never be pushed out of view by tall content above.
            Assert.Equal(new[] { "54", "*", "Auto" }, rows);

            // The middle zone is a ScrollViewer that absorbs overflow instead of
            // letting the grid overflow past the window bottom.
            Assert.Contains("<ScrollViewer Grid.Row=\"1\"", source);

            // The model list is bounded so it cannot force the window taller.
            Assert.Contains("MaxHeight=\"190\"", source);

            // The footer row hosts both action buttons. The assertion is anchored
            // to the footer's unique attributes so the keys card (also Grid.Row="2"
            // inside the nested grid) cannot satisfy it.
            Assert.Contains("Border Grid.Row=\"2\" Margin=\"-1,0,-1,0\" Padding=\"0,12,0,14\"", source);
            Assert.Contains("Content=\"Отмена\"", source);
            Assert.Contains("Content=\"Сохранить\"", source);
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
