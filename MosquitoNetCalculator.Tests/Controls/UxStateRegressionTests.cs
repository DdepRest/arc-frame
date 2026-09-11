using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.App;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// Focused regressions for the UX state paths added in v3.50.
    /// These tests deliberately cover the real control seams rather than
    /// asserting only that the source contains a feature name.
    /// </summary>
    [Collection("WPF_UI")]
    public class UxStateRegressionTests
    {
        [Fact]
        public void DeleteAndClearToastActions_RouteToUndo_NotRedo()
        {
            var sourceDir = LocateSourceProject();
            var deleteSource = File.ReadAllText(Path.Combine(sourceDir, "MainWindow.Items.cs"));
            var actionBarSource = File.ReadAllText(Path.Combine(sourceDir, "Controls", "ActionBarControl.xaml.cs"));

            int deleteStart = deleteSource.IndexOf("internal void BtnDeleteRow_Click", StringComparison.Ordinal);
            Assert.True(deleteStart >= 0, "Delete-row entry point was not found.");
            string deleteBody = deleteSource[deleteStart..];
            Assert.Contains("() => Undo()", deleteBody);
            Assert.DoesNotContain("() => Redo()", deleteBody);

            int clearStart = actionBarSource.IndexOf("private void BtnClearAll_Click", StringComparison.Ordinal);
            Assert.True(clearStart >= 0, "Clear-all entry point was not found.");
            string clearBody = actionBarSource[clearStart..];
            Assert.Contains("() => mw.Undo()", clearBody);
            Assert.DoesNotContain("() => mw.Redo()", clearBody);
        }

        [Fact]
        public void DirtyChip_Visibility_IsOwnedBy_IsDirtyTrigger()
        {
            var sourceDir = LocateSourceProject();
            var xaml = File.ReadAllText(Path.Combine(sourceDir, "Controls", "ActionBarControl.xaml"));
            int chipStart = xaml.IndexOf("x:Name=\"DirtyChip\"", StringComparison.Ordinal);
            Assert.True(chipStart >= 0, "DirtyChip was not found.");

            int openingEnd = xaml.IndexOf('>', chipStart);
            Assert.True(openingEnd > chipStart, "DirtyChip opening tag is malformed.");
            string openingTag = xaml.Substring(chipStart, openingEnd - chipStart + 1);

            // A local Visibility value would win over the Style/DataTrigger
            // and keep the chip collapsed forever after IsDirty changes.
            Assert.DoesNotContain("Visibility=", openingTag, StringComparison.OrdinalIgnoreCase);

            int styleEnd = xaml.IndexOf("</Border.Style>", openingEnd, StringComparison.Ordinal);
            Assert.True(styleEnd > openingEnd, "DirtyChip style was removed.");
            string style = xaml.Substring(openingEnd, styleEnd - openingEnd);
            Assert.Contains("Binding=\"{Binding IsDirty}\"", style);
            Assert.Contains("Value=\"Visible\"", style);
        }

        [Fact]
        public void HistoryFilter_ReappliesAfterItemsSourceRefresh()
        {
            RunOnStaWithThemes(() =>
            {
                var control = new OrdersHistoryControl();
                var search = control.FindName("TxtSearchOrders") as TextBox;
                Assert.NotNull(search);

                var firstLoad = new List<OrderData>
                {
                    new() { ContractNumber = "1-1", ClientAddress = "Alpha street" },
                    new() { ContractNumber = "1-2", ClientAddress = "Beta street" },
                };
                OrderGridPresenter.RefreshOrdersGrid(control.OrdersGrid, firstLoad);
                control.SetOrdersCount(firstLoad.Count);

                search!.Text = "alpha";
                int filteredCount = control.OrdersGrid.Items.Count;
                Assert.Equal(1, filteredCount);
                Assert.Equal("Alpha street", ((OrderData)control.OrdersGrid.Items[0]).ClientAddress);

                // This is the production refresh path: ItemsSource is replaced
                // with a newly loaded list while the user's filter stays active.
                var refreshed = new List<OrderData>
                {
                    new() { ContractNumber = "2-1", ClientAddress = "Alpha avenue" },
                    new() { ContractNumber = "2-2", ClientAddress = "Gamma avenue" },
                };
                OrderGridPresenter.RefreshOrdersGrid(control.OrdersGrid, refreshed);
                control.SetOrdersCount(refreshed.Count);
                control.ReapplyFilters();

                int refreshedFilteredCount = control.OrdersGrid.Items.Count;
                Assert.Equal(1, refreshedFilteredCount);
                Assert.Equal("Alpha avenue", ((OrderData)control.OrdersGrid.Items[0]).ClientAddress);
                Assert.True(control.IsEmpty == false);
                Assert.False(control.NoSearchResults);
            });
        }

        private static void RunOnStaWithThemes(Action action)
        {
            WpfTestHelper.RunOnSta(() =>
            {
                if (Application.Current != null)
                    AppLifecycleTests.ClearWpfApplicationStatic();

                try
                {
                    EnsureAppThemes();
                    action();
                }
                finally
                {
                    AppLifecycleTests.ClearWpfApplicationStatic();
                }
            });
        }

        private static void EnsureAppThemes()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            string sourceDir = LocateSourceProject();
            string[] dictionaries =
            {
                "Brushes.xaml", "FocusVisualStyles.xaml", "CardStyles.xaml",
                "FontStyles.xaml", "TabStyles.xaml", "ButtonStyles.xaml",
                "InputStyles.xaml", "DataGridStyles.xaml", "InputStyles.RadioButton.xaml",
                "ScrollViewerStyles.xaml", "ContextMenuStyles.xaml", "MiscStyles.xaml",
            };

            foreach (var dictionary in dictionaries)
            {
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(Path.Combine(sourceDir, "Themes", dictionary), UriKind.Absolute)
                });
            }

            app.Resources["DimConv"] = new MosquitoNetCalculator.Converters.DimensionConverter();
            app.Resources["BoolToVis"] = new BooleanToVisibilityConverter();
            app.Resources["StatusToBadgeBg"] = new MosquitoNetCalculator.Converters.StatusToBadgeBackgroundConverter();
            app.Resources["StatusToBadgeFg"] = new MosquitoNetCalculator.Converters.StatusToBadgeForegroundConverter();
            app.Resources["MoneyConv"] = new MosquitoNetCalculator.Converters.MoneyConverter();
        }

        private static string LocateSourceProject()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "MosquitoNetCalculator");
                if (File.Exists(Path.Combine(candidate, "App.xaml")))
                    return candidate;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate MosquitoNetCalculator source project.");
        }
    }
}
