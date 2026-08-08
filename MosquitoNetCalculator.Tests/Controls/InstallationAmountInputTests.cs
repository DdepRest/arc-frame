using System;
using System.IO;
using MosquitoNetCalculator;
using MosquitoNetCalculator.Models;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// Regression coverage for the installation amount popup.
    ///
    /// The first two tests exercise the same pure conversion path used by the
    /// popup, including the user sequence "select − at 0, then type 500".
    /// The source-contract test pins the WPF event wiring that is difficult to
    /// execute reliably in a headless test runner.
    /// </summary>
    public class InstallationAmountInputTests
    {
        [Fact]
        public void SelectingMinusAtZero_ThenEnteringAmount_PersistsNegativeValue()
        {
            var item = new OrderItem
            {
                Name = "Anwis",
                InstallationMode = 0,
                InstallationAdjustment = 0
            };

            // At zero there is no mathematical sign. The popup must not ask
            // the CheckBox to refresh its state, so a selected "−" survives.
            Assert.False(MainWindow.ShouldRefreshInstallationSign(
                item.CurrentInstallationAmount));

            // Model the commit after the user enters 500 while "−" remains
            // selected. This is the exact signed value sent to the model by
            // the popup's commit path.
            const bool minusSelected = true;
            double committed = MainWindow.NormalizeInstallationAmount(
                500, isAdd: !minusSelected);
            item.SetCurrentInstallationAmount(committed);

            Assert.Equal(-500, item.CurrentInstallationAmount);
            Assert.Equal(-500, item.InstallationAdjustment);
        }

        [Theory]
        [InlineData(-500, true, -500)]
        [InlineData(-500, false, -500)]
        [InlineData(500, false, -500)]
        [InlineData(500, true, 500)]
        public void SignedInput_IsNotMadePositiveByToggle(double rawValue, bool isAdd, double expected)
        {
            var item = new OrderItem
            {
                Name = "Anwis",
                InstallationMode = 0
            };

            item.SetCurrentInstallationAmount(
                MainWindow.NormalizeInstallationAmount(rawValue, isAdd));

            Assert.Equal(expected, item.CurrentInstallationAmount);
        }

        [Fact]
        public void Popup_PreservesMinusAtZero_AndCommitsOnAllInputRoutes()
        {
            string source = ReadMainWindowItemsSource();

            // Refreshing a zero-valued field must not overwrite the selected
            // sign. A later non-zero value is allowed to synchronize the toggle.
            Assert.Contains("if (ShouldRefreshInstallationSign(currentVal))", source);
            Assert.Contains("chkSign.IsChecked = currentVal > 0;", source);

            // The same commit function is used by the sign toggle, LostFocus,
            // and Enter paths, so all normal ways of finishing an edit remain
            // covered by the regression fix.
            AssertHandlerCommits(source, "chkSign.Click +=", "RefreshDeductionField();");
            AssertHandlerCommits(source, "txtDeduction.LostFocus +=", "txtDeduction.KeyDown +=");
            AssertHandlerCommits(source, "txtDeduction.KeyDown +=", "deductionPanel.Children.Add(chkSign);");
            Assert.Contains("if (args.Key == Key.Enter)", source);
            Assert.Contains("double val = NormalizeInstallationAmount(rawVal, chkSign.IsChecked == true);", source);

            int lostFocus = source.IndexOf("txtDeduction.LostFocus +=", StringComparison.Ordinal);
            int enter = source.IndexOf("txtDeduction.KeyDown +=", StringComparison.Ordinal);
            Assert.True(lostFocus >= 0 && enter > lostFocus,
                "LostFocus and Enter handlers must remain wired in popup order.");
        }

        private static void AssertHandlerCommits(
            string source, string handlerStart, string nextHandlerOrEnd)
        {
            int start = source.IndexOf(handlerStart, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Handler '{handlerStart}' was not found.");

            int end = source.IndexOf(nextHandlerOrEnd, start + handlerStart.Length,
                StringComparison.Ordinal);
            Assert.True(end > start,
                $"Could not determine the body of handler '{handlerStart}'.");

            string body = source[start..end];
            Assert.Contains("CommitDeductionIfPending();", body);
        }

        private static string ReadMainWindowItemsSource()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "MosquitoNetCalculator", "MainWindow.Items.cs");
                if (File.Exists(candidate))
                    return File.ReadAllText(candidate);
                dir = dir.Parent;
            }

            throw new FileNotFoundException(
                "Could not locate MosquitoNetCalculator/MainWindow.Items.cs from the test output directory.");
        }
    }
}
