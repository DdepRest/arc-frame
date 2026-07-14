using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Unit tests for <see cref="NavigationService"/>.
    /// Tests SetActive highlighting, Expand/Collapse animations, and ActiveTag tracking.
    ///
    /// WPF controls require an STA thread. xUnit defaults to MTA, so every test
    /// body is dispatched to a dedicated STA thread.
    /// </summary>
    public class NavigationServiceTests
    {
        private static void RunOnStaThread(Action action)
        {
            Exception? caught = null;
            using var gate = new ManualResetEventSlim(false);

            var t = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { caught = ex; }
                finally { gate.Set(); }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();

            if (!gate.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("STA thread did not finish within 10 seconds.");
            if (caught != null) throw caught;
        }

        /// <summary>
        /// Creates a NavigationService with 6 dummy nav buttons, icons, labels,
        /// and a nav panel. Returns all components for assertions.
        /// </summary>
        private static (NavigationService svc, Button[] buttons, TextBlock[] icons, TextBlock[] labels, Border panel)
            CreateNavigationService()
        {
            var panel = new Border { Width = 52 };

            var buttons = new Button[6];
            var icons = new TextBlock[6];
            var labels = new TextBlock[6];
            var tags = new[] { "Calc", "Orders", "Prices", "Updates", "Slope", "Print" };

            for (int i = 0; i < 6; i++)
            {
                icons[i] = new TextBlock { Text = "icon" };
                labels[i] = new TextBlock { Text = tags[i], Opacity = 0 };
                buttons[i] = new Button { Tag = tags[i], Content = icons[i] };
            }

            // Use a dummy FrameworkElement for resource lookup
            var resourceOwner = new Border();
            var svc = new NavigationService(buttons, icons, labels, panel, resourceOwner);
            return (svc, buttons, icons, labels, panel);
        }

        [Fact]
        public void ActiveTag_DefaultsToCalc()
        {
            RunOnStaThread(() =>
            {
                var (svc, _, _, _, _) = CreateNavigationService();
                Assert.Equal("Calc", svc.ActiveTag);
            });
        }

        [Fact]
        public void SetActive_UpdatesActiveTag()
        {
            RunOnStaThread(() =>
            {
                var (svc, _, _, _, _) = CreateNavigationService();
                svc.SetActive("Orders");
                Assert.Equal("Orders", svc.ActiveTag);
            });
        }

        [Fact]
        public void SetActive_Calc_SetsCorrectFontWeight()
        {
            RunOnStaThread(() =>
            {
                var (svc, _, _, labels, _) = CreateNavigationService();
                svc.SetActive("Calc");

                Assert.Equal(FontWeights.SemiBold, labels[0].FontWeight);
                Assert.Equal(FontWeights.Regular, labels[1].FontWeight);
            });
        }

        [Fact]
        public void SetActive_Orders_SetsCorrectFontWeight()
        {
            RunOnStaThread(() =>
            {
                var (svc, _, _, labels, _) = CreateNavigationService();
                svc.SetActive("Orders");

                Assert.Equal(FontWeights.Regular, labels[0].FontWeight);
                Assert.Equal(FontWeights.SemiBold, labels[1].FontWeight);
            });
        }

        [Fact]
        public void SetActive_UnknownTag_AllRegular()
        {
            RunOnStaThread(() =>
            {
                var (svc, _, _, labels, _) = CreateNavigationService();
                svc.SetActive("NonExistent");

                for (int i = 0; i < labels.Length; i++)
                    Assert.Equal(FontWeights.Regular, labels[i].FontWeight);
            });
        }

        [Fact]
        public void SetActive_ChangesIconForeground()
        {
            RunOnStaThread(() =>
            {
                var (svc, _, icons, _, _) = CreateNavigationService();

                // Set initial active to Calc
                svc.SetActive("Calc");
                var calcBrush = icons[0].Foreground;

                // Switch to Orders
                svc.SetActive("Orders");
                var ordersBrush = icons[1].Foreground;

                // Calc icon should now be inactive (different brush)
                Assert.NotEqual(calcBrush, icons[0].Foreground);
                // Orders icon should be active (same brush that Calc had)
                Assert.Equal(calcBrush, ordersBrush);
            });
        }

        [Fact]
        public void SetActive_ChangesLabelForeground()
        {
            RunOnStaThread(() =>
            {
                var (svc, _, _, labels, _) = CreateNavigationService();

                svc.SetActive("Calc");
                var calcBrush = labels[0].Foreground;

                svc.SetActive("Prices");
                Assert.NotEqual(calcBrush, labels[0].Foreground);
                Assert.Equal(calcBrush, labels[2].Foreground);
            });
        }

        [Fact]
        public void Expand_DoesNotThrow()
        {
            RunOnStaThread(() =>
            {
                var (svc, _, _, _, _) = CreateNavigationService();
                // Expand starts animations — just verify no exception
                svc.Expand();
            });
        }

        [Fact]
        public void Collapse_DoesNotThrow_WhenMouseNotOver()
        {
            RunOnStaThread(() =>
            {
                var (svc, _, _, _, panel) = CreateNavigationService();
                // Panel.IsMouseOver will be false in test context
                svc.Collapse();
            });
        }

        [Fact]
        public void Shutdown_DoesNotThrow()
        {
            RunOnStaThread(() =>
            {
                var (svc, _, _, _, _) = CreateNavigationService();
                svc.Shutdown();
            });
        }
    }
}
