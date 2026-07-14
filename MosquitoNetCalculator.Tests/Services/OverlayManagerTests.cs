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
    /// Unit tests for <see cref="OverlayManager"/>.
    /// Tests Show, CloseAll, HideInstant, Toggle, and HideAllExcept.
    ///
    /// WPF controls require an STA thread. xUnit defaults to MTA, so every test
    /// body is dispatched to a dedicated STA thread.
    /// </summary>
    public class OverlayManagerTests
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
        /// Creates a single overlay entry with a Grid, Border panel, backdrop,
        /// and TranslateTransform. All visibility starts Collapsed.
        /// </summary>
        private static OverlayManager.OverlayEntry CreateEntry()
        {
            var grid = new Grid { Visibility = Visibility.Collapsed };
            var backdrop = new Border { Opacity = 0, Background = Brushes.Black };
            var panel = new Border { Width = 400 };
            var slide = new TranslateTransform { X = 0 };
            panel.RenderTransform = slide;
            grid.Children.Add(backdrop);
            grid.Children.Add(panel);
            return new OverlayManager.OverlayEntry(grid, panel, backdrop, slide);
        }

        /// <summary>
        /// Mutable wrapper to capture the last nav tag set by OverlayManager callbacks.
        /// Required because string is returned by value in tuples — a lambda
        /// can't mutate the tuple element after return.
        /// </summary>
        private sealed class NavTagCapture
        {
            public string Value { get; set; } = "";
        }

        /// <summary>
        /// Creates an OverlayManager with the given entries and a simple
        /// callback that tracks the last nav tag set.
        /// </summary>
        private static (OverlayManager mgr, NavTagCapture navTag) CreateManager(
            OverlayManager.OverlayEntry[] entries,
            Action? onBeforeClosePrint = null)
        {
            var capture = new NavTagCapture();
            var mgr = new OverlayManager(
                entries,
                tag => capture.Value = tag,
                onBeforeClosePrint);
            return (mgr, capture);
        }

        [Fact]
        public void Show_SetsOverlayVisible()
        {
            RunOnStaThread(() =>
            {
                var entry = CreateEntry();
                var (mgr, _) = CreateManager(new[] { entry });

                Assert.Equal(Visibility.Collapsed, entry.Grid.Visibility);
                mgr.Show(entry);
                Assert.Equal(Visibility.Visible, entry.Grid.Visibility);
            });
        }

        [Fact]
        public void Show_SetsNavTag_WhenProvided()
        {
            RunOnStaThread(() =>
            {
                var entry = CreateEntry();
                var (mgr, navTag) = CreateManager(new[] { entry });

                mgr.Show(entry, "Orders");
                Assert.Equal("Orders", navTag.Value);
            });
        }

        [Fact]
        public void Show_DoesNotSetNavTag_WhenNull()
        {
            RunOnStaThread(() =>
            {
                var entry = CreateEntry();
                var (mgr, navTag) = CreateManager(new[] { entry });

                mgr.Show(entry);
                Assert.Equal("", navTag.Value);
            });
        }

        [Fact]
        public void Show_HidesOtherVisibleOverlays()
        {
            RunOnStaThread(() =>
            {
                var entryA = CreateEntry();
                var entryB = CreateEntry();
                var (mgr, _) = CreateManager(new[] { entryA, entryB });

                // Show A first
                mgr.Show(entryA);
                Assert.Equal(Visibility.Visible, entryA.Grid.Visibility);

                // Show B — A should be hidden
                mgr.Show(entryB);
                Assert.Equal(Visibility.Collapsed, entryA.Grid.Visibility);
                Assert.Equal(Visibility.Visible, entryB.Grid.Visibility);
            });
        }

        [Fact]
        public void HideInstant_SetsCollapsed()
        {
            RunOnStaThread(() =>
            {
                var entry = CreateEntry();
                entry.Grid.Visibility = Visibility.Visible;

                OverlayManager.HideInstant(entry.Grid);

                Assert.Equal(Visibility.Collapsed, entry.Grid.Visibility);
            });
        }

        [Fact]
        public void HideAllExcept_HidesAllExceptKept()
        {
            RunOnStaThread(() =>
            {
                var entryA = CreateEntry();
                var entryB = CreateEntry();
                var entryC = CreateEntry();
                var (mgr, _) = CreateManager(new[] { entryA, entryB, entryC });

                // Make all visible
                entryA.Grid.Visibility = Visibility.Visible;
                entryB.Grid.Visibility = Visibility.Visible;
                entryC.Grid.Visibility = Visibility.Visible;

                mgr.HideAllExcept(entryB);

                Assert.Equal(Visibility.Collapsed, entryA.Grid.Visibility);
                Assert.Equal(Visibility.Visible, entryB.Grid.Visibility);
                Assert.Equal(Visibility.Collapsed, entryC.Grid.Visibility);
            });
        }

        [Fact]
        public void Toggle_ShowWhenHidden()
        {
            RunOnStaThread(() =>
            {
                var entry = CreateEntry();
                var (mgr, _) = CreateManager(new[] { entry });

                mgr.Toggle(entry, "Prices");

                Assert.Equal(Visibility.Visible, entry.Grid.Visibility);
            });
        }

        [Fact]
        public void Toggle_CloseAllWhenVisible()
        {
            RunOnStaThread(() =>
            {
                var entry = CreateEntry();
                var (mgr, navTag) = CreateManager(new[] { entry });

                // First show
                mgr.Show(entry, "Orders");
                Assert.Equal(Visibility.Visible, entry.Grid.Visibility);

                // Toggle — should close all
                mgr.Toggle(entry, "Orders");
                Assert.Equal("Calc", navTag.Value); // CloseAll sets "Calc"
            });
        }

        [Fact]
        public void CloseAll_SetsNavToCalc()
        {
            RunOnStaThread(() =>
            {
                var entry = CreateEntry();
                var (mgr, navTag) = CreateManager(new[] { entry });

                mgr.Show(entry, "Updates");
                Assert.Equal("Updates", navTag.Value);

                mgr.CloseAll();
                Assert.Equal("Calc", navTag.Value);
            });
        }

        [Fact]
        public void CloseAll_InvokesBeforeClosePrintCallback()
        {
            RunOnStaThread(() =>
            {
                var entry = CreateEntry();
                bool callbackInvoked = false;
                var (mgr, _) = CreateManager(new[] { entry }, () => callbackInvoked = true);

                mgr.Show(entry);
                mgr.CloseAll();

                Assert.True(callbackInvoked);
            });
        }
    }
}
