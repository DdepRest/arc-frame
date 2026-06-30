using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls.Primitives;
using MosquitoNetCalculator.Controls;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// Unit tests for <see cref="QuickAddControl.UpdateAnticatToggleState"/>.
    /// Verifies that the anti-cat toggle button visibility and checked state
    /// update correctly when the product type changes.
    ///
    /// These tests cover the extracted helper in isolation. Full integration
    /// of <see cref="QuickAddControl.CmbQuickType_SelectionChanged"/> is
    /// blocked by the <c>TryGetMainWindow → MainWindow</c> dependency.
    ///
    /// WPF <see cref="ToggleButton"/> requires an STA thread. xUnit defaults to
    /// MTA, so every test body is dispatched to a dedicated STA thread via
    /// <see cref="RunOnStaThread"/>.
    /// </summary>
    public class QuickAddControlTests
    {
        /// <summary>
        /// Runs <paramref name="action"/> on a dedicated STA thread,
        /// re-throwing any exception on the calling thread. Times out
        /// after 10 seconds.
        /// </summary>
        private static void RunOnStaThread(Action action)
        {
            Exception? caught = null;
            using var gate = new ManualResetEventSlim(false);

            var t = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex) { caught = ex; }
                finally { gate.Set(); }
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();

            if (!gate.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("STA thread did not finish within 10 seconds.");

            if (caught != null)
                throw caught;
        }

        [Theory]
        [InlineData("Anwis")]
        [InlineData("На навесах")]
        [InlineData("Оконная на метал. крепл.")]
        public void UpdateAnticatToggleState_ApplicableProduct_ShowsButton(string productName)
        {
            RunOnStaThread(() =>
            {
                var btn = new ToggleButton();
                QuickAddControl.UpdateAnticatToggleState(productName, btn);

                Assert.Equal(Visibility.Visible, btn.Visibility);
            });
        }

        [Theory]
        [InlineData("Отлив")]
        [InlineData("Козырёк")]
        [InlineData("Работа")]
        [InlineData("ПСУЛ")]
        [InlineData("Доставка")]
        public void UpdateAnticatToggleState_NonApplicableProduct_HidesButton(string productName)
        {
            RunOnStaThread(() =>
            {
                var btn = new ToggleButton();
                QuickAddControl.UpdateAnticatToggleState(productName, btn);

                Assert.Equal(Visibility.Collapsed, btn.Visibility);
            });
        }

        [Fact]
        public void UpdateAnticatToggleState_NonApplicableProduct_UnchecksButton()
        {
            RunOnStaThread(() =>
            {
                var btn = new ToggleButton { IsChecked = true };
                QuickAddControl.UpdateAnticatToggleState("Отлив", btn);

                Assert.False(btn.IsChecked);
            });
        }

        [Fact]
        public void UpdateAnticatToggleState_ApplicableProduct_PreservesCheckedState()
        {
            RunOnStaThread(() =>
            {
                var btn = new ToggleButton { IsChecked = true };
                QuickAddControl.UpdateAnticatToggleState("Anwis", btn);

                Assert.Equal(Visibility.Visible, btn.Visibility);
                Assert.True(btn.IsChecked);
            });
        }

        [Fact]
        public void UpdateAnticatToggleState_SwitchFromApplicableToNonApplicable_Unchecks()
        {
            RunOnStaThread(() =>
            {
                var btn = new ToggleButton();

                // Start with applicable product and check the button
                QuickAddControl.UpdateAnticatToggleState("Anwis", btn);
                btn.IsChecked = true;

                // Switch to non-applicable product
                QuickAddControl.UpdateAnticatToggleState("Отлив", btn);

                Assert.Equal(Visibility.Collapsed, btn.Visibility);
                Assert.False(btn.IsChecked);
            });
        }

        [Fact]
        public void UpdateAnticatToggleState_SwitchFromNonApplicableToApplicable_ButtonVisibleAndUnchecked()
        {
            RunOnStaThread(() =>
            {
                var btn = new ToggleButton();

                // Start with non-applicable product
                QuickAddControl.UpdateAnticatToggleState("Отлив", btn);
                Assert.Equal(Visibility.Collapsed, btn.Visibility);

                // Switch to applicable product
                QuickAddControl.UpdateAnticatToggleState("На навесах", btn);

                Assert.Equal(Visibility.Visible, btn.Visibility);
                Assert.False(btn.IsChecked);
            });
        }

        [Fact]
        public void UpdateAnticatToggleState_EmptyProductName_HidesAndUnchecks()
        {
            RunOnStaThread(() =>
            {
                var btn = new ToggleButton { IsChecked = true };
                QuickAddControl.UpdateAnticatToggleState("", btn);

                Assert.Equal(Visibility.Collapsed, btn.Visibility);
                Assert.False(btn.IsChecked);
            });
        }

        [Fact]
        public void UpdateAnticatToggleState_WhitespaceProductName_HidesAndUnchecks()
        {
            RunOnStaThread(() =>
            {
                var btn = new ToggleButton { IsChecked = true };
                QuickAddControl.UpdateAnticatToggleState("   ", btn);

                Assert.Equal(Visibility.Collapsed, btn.Visibility);
                Assert.False(btn.IsChecked);
            });
        }
    }
}
