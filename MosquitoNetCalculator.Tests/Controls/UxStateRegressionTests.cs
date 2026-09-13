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
        public void DirtyChip_IsGone_FromActionBar_OwnedByStatusBarOnly()
        {
            // v3.50.2 owner request: the top action bar must not duplicate the
            // status-bar «Есть изменения» indicator — one owner (StatusDirtyIndicator
            // in MainWindow.xaml) instead of two chips showing the same state.
            var sourceDir = LocateSourceProject();
            var xaml = File.ReadAllText(Path.Combine(sourceDir, "Controls", "ActionBarControl.xaml"));
            var code = File.ReadAllText(Path.Combine(sourceDir, "Controls", "ActionBarControl.xaml.cs"));

            Assert.DoesNotContain("x:Name=\"DirtyChip\"", xaml);
            Assert.DoesNotContain("Есть изменения", xaml);
            Assert.DoesNotContain("DirtyChip_Click", code);

            // The status-bar indicator in MainWindow.xaml stays the single owner.
            var mainWindowXaml = File.ReadAllText(Path.Combine(sourceDir, "MainWindow.xaml"));
            Assert.Contains("x:Name=\"StatusDirtyIndicator\"", mainWindowXaml);
        }

        [Fact]
        public void ClientInfoButton_IsPrimaryAccent_SameAsPrintButton()
        {
            // v3.50.2 owner request: «Заказчик» must always be the same solid
            // accent blue as «Печать КП» (prototype .btn.blue) — no ghost or
            // tinted intermediate states.
            var sourceDir = LocateSourceProject();
            var xaml = File.ReadAllText(Path.Combine(sourceDir, "Controls", "ActionBarControl.xaml"));
            int styleStart = xaml.IndexOf("x:Key=\"ClientInfoButton\"", StringComparison.Ordinal);
            Assert.True(styleStart >= 0, "ClientInfoButton style was not found.");
            int styleEnd = xaml.IndexOf("/>", styleStart, StringComparison.Ordinal);
            string style = xaml.Substring(styleStart, styleEnd - styleStart);

            Assert.Contains("BasedOn=\"{StaticResource PrimaryButton}\"", style);
            Assert.DoesNotContain("AccentLight", style);
            Assert.DoesNotContain("IsClientInfoFilled", style);
        }

        [Fact]
        public void PrintSplitButton_MainSegmentAndCaret_AreWiredConsistently()
        {
            var sourceDir = LocateSourceProject();
            var xaml = File.ReadAllText(Path.Combine(sourceDir, "Controls", "ActionBarControl.xaml"));
            var code = File.ReadAllText(Path.Combine(sourceDir, "Controls", "ActionBarControl.xaml.cs"));
            var previewCode = File.ReadAllText(Path.Combine(sourceDir, "Controls", "PrintPreviewControl.xaml.cs"));

            // v3.50.2 regression: the caret segment shipped without a Click
            // wiring once — a mouse click opened nothing while the main
            // segment still worked, so the split button looked broken.
            int caretStart = xaml.IndexOf("x:Name=\"BtnPrintCaret\"", StringComparison.Ordinal);
            Assert.True(caretStart >= 0, "BtnPrintCaret was not found.");
            int caretEnd = xaml.IndexOf(">", caretStart, StringComparison.Ordinal);
            Assert.True(caretEnd > caretStart, "BtnPrintCaret opening tag is malformed.");
            string caretTag = xaml.Substring(caretStart, caretEnd - caretStart + 1);
            Assert.Contains("Click=\"BtnPrintCaret_Click\"", caretTag);

            // The caret handler must exist and toggle the popup.
            Assert.Contains("private void BtnPrintCaret_Click", code);
            Assert.Contains("PrintMenuPopup.IsOpen = !PrintMenuPopup.IsOpen", code);

            // v3.50.3 owner semantics: main segment = предпросмотр; the caret
            // menu offers exactly three direct actions, none of which opens a
            // preview. Every action must reuse the PrintPreviewControl
            // pipeline (no second print route).
            int mainStart = xaml.IndexOf("x:Name=\"BtnPrintKpMain\"", StringComparison.Ordinal);
            Assert.True(mainStart >= 0, "BtnPrintKpMain was not found.");
            int mainEnd = xaml.IndexOf(">", mainStart, StringComparison.Ordinal);
            string mainTag = xaml.Substring(mainStart, mainEnd - mainStart + 1);
            Assert.Contains("Click=\"BtnPrintPreview_Click\"", mainTag);

            Assert.Contains("Click=\"BtnPrinterDirect_Click\"", xaml);
            Assert.Contains("Click=\"BtnPrinterProduction_Click\"", xaml);
            Assert.Contains("Click=\"BtnPrintPdf_Click\"", xaml);
            Assert.Contains("Печать без предпросмотра", xaml);
            Assert.Contains("«Чистая» + «В производство»", xaml);
            Assert.Contains("Сохранить в PDF", xaml);

            Assert.Contains("TriggerPrinterPrint()", code);
            Assert.Contains("TriggerPrinterPrintWithProductionCopy()", code);
            Assert.Contains("TriggerPdfExport()", code);

            // The production-copy trigger must flip the existing checkbox and
            // reuse Print_Click — not spawn a parallel pipeline.
            Assert.Contains("public void TriggerPrinterPrintWithProductionCopy", previewCode);
            Assert.Contains("TriggerPrinterPrint();", previewCode);
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
