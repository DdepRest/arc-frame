using System.Windows;
using MosquitoNetCalculator;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Tests.Helpers;
using MosquitoNetCalculator.ViewModels;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// STA tests for <see cref="AiAssistantWindow"/>.
    /// Covers the positioning helper and basic window lifecycle.
    /// </summary>
    public class AiAssistantWindowTests
    {
        [Fact]
        public void PositionNextToCore_DocksRight_AlignedWithOwnerTop_WhenSpaceAllows()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new AiAssistantWindow { Width = 400 };

                // Owner is at X=100,Y=50 with width 800. Screen right edge is at 2000.
                window.PositionNextToCore(
                    ownerLeft: 100, ownerTop: 50, ownerWidth: 800, ownerHeight: 700,
                    screenLeft: 0, screenTop: 0, screenRight: 2000, screenBottom: 1000);

                Assert.Equal(900, window.Left);  // docked flush right of the owner
                Assert.Equal(50, window.Top);    // aligned with the owner's top
                Assert.Equal(700, window.Height); // mirrors the owner's height
            });
        }

        [Fact]
        public void PositionNextToCore_ClampsToRightEdge_WhenRightDockOverflows()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new AiAssistantWindow { Width = 400 };

                // Owner at X=1500 with width 800. Right-edge placement would be 2300,
                // whose right edge (2700) exceeds the screen right edge (2000).
                // The window stays BOUND to the program and clamps flush to the
                // screen's right edge: 2000 − 400 = 1600. It never flips left and
                // never self-centers (whole-program centering is MainWindow's job).
                window.PositionNextToCore(
                    ownerLeft: 1500, ownerTop: 50, ownerWidth: 800, ownerHeight: 700,
                    screenLeft: 0, screenTop: 0, screenRight: 2000, screenBottom: 1000);

                Assert.Equal(1600, window.Left);
                Assert.Equal(50, window.Top);
            });
        }

        [Fact]
        public void PositionNextToCore_FitsExactlyAtRightEdge_WhenSpaceAllows()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new AiAssistantWindow { Width = 400 };

                // Owner at X=100, width 800, assistant width 400.
                // Right edge of assistant is exactly 1300, screen right is 1300.
                window.PositionNextToCore(
                    ownerLeft: 100, ownerTop: 50, ownerWidth: 800, ownerHeight: 700,
                    screenLeft: 0, screenTop: 0, screenRight: 1300, screenBottom: 1000);

                Assert.Equal(900, window.Left);
                Assert.Equal(50, window.Top);
            });
        }

        [Fact]
        public void PositionNextToCore_ClampsToRightEdge_WhenOverflowsByOnePixel()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new AiAssistantWindow { Width = 400 };

                // Right placement at 900 overflows (900+400=1300 > 1299), so the
                // window clamps flush to the screen's right edge: 1299 − 400 = 899.
                window.PositionNextToCore(
                    ownerLeft: 100, ownerTop: 50, ownerWidth: 800, ownerHeight: 700,
                    screenLeft: 0, screenTop: 0, screenRight: 1299, screenBottom: 1000);

                Assert.Equal(899, window.Left);
                Assert.Equal(50, window.Top);
            });
        }

        [Fact]
        public void PositionNextToCore_ClampsToRightEdge_WhenOwnerSpansNearlyWholeScreen()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new AiAssistantWindow { Width = 400 };

                // Owner spans nearly the whole screen (X=100, width 1500 on a
                // 1600-wide screen). Right placement overflows → the window clamps
                // flush to the screen's right edge: 1600 − 400 = 1200 — fully
                // on-screen, never on the left edge.
                window.PositionNextToCore(
                    ownerLeft: 100, ownerTop: 50, ownerWidth: 1500, ownerHeight: 700,
                    screenLeft: 0, screenTop: 0, screenRight: 1600, screenBottom: 1000);

                Assert.Equal(1200, window.Left);
                Assert.Equal(50, window.Top);
                Assert.True(window.Left + window.Width <= 1600);
            });
        }

        [Fact]
        public void PositionNextToCore_AlignsTopWithOwnerTop()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new AiAssistantWindow { Width = 400 };

                // The dock is BOUND to the program: its top matches the owner's
                // top exactly (no screen-centering, no alignment to owner bottom).
                window.PositionNextToCore(
                    ownerLeft: 100, ownerTop: 220, ownerWidth: 800, ownerHeight: 700,
                    screenLeft: 0, screenTop: 0, screenRight: 2000, screenBottom: 1000);

                Assert.Equal(220, window.Top);
            });
        }

        [Fact]
        public void PositionNextToCore_ClampsTop_WhenOwnerNearBottom()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new AiAssistantWindow { Width = 400 };

                // Owner sits low (top=560) on a 600-tall working area with a
                // 500-tall dock → the dock clamps so its bottom stays on-screen.
                window.PositionNextToCore(
                    ownerLeft: 100, ownerTop: 560, ownerWidth: 800, ownerHeight: 500,
                    screenLeft: 0, screenTop: 0, screenRight: 1920, screenBottom: 600);

                Assert.Equal(100, window.Top); // 600 − 500
            });
        }

        [Fact]
        public void PositionNextToCore_ClampsBottom_WhenOwnerHeightExceedsScreen()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new AiAssistantWindow { Width = 400 };

                // Owner is 900 tall on an 800-tall screen → height clamps to 800;
                // with Top=0 the bottom edge lands exactly at the screen bottom.
                window.PositionNextToCore(
                    ownerLeft: 100, ownerTop: 100, ownerWidth: 800, ownerHeight: 900,
                    screenLeft: 0, screenTop: 0, screenRight: 2000, screenBottom: 800);

                Assert.Equal(800, window.Height);
                Assert.Equal(0, window.Top);
                Assert.Equal(800, window.Top + window.Height); // bottom on-screen
            });
        }

        [Fact]
        public void PositionNextToCore_MirrorsOwnerHeight()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new AiAssistantWindow { Width = 400 };

                // AI window should match the program's height so the docked panel
                // feels like part of the main window, not a floating stub.
                window.PositionNextToCore(
                    ownerLeft: 100, ownerTop: 50, ownerWidth: 1200, ownerHeight: 760,
                    screenLeft: 0, screenTop: 0, screenRight: 2560, screenBottom: 1400);

                Assert.Equal(760, window.Height);
            });
        }

        [Fact]
        public void PositionNextToCore_HeightNeverBelowMinHeight()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new AiAssistantWindow { Width = 400 };

                // Owner is tiny (300 tall) — the docked window keeps its minimum height.
                window.PositionNextToCore(
                    ownerLeft: 100, ownerTop: 50, ownerWidth: 800, ownerHeight: 300,
                    screenLeft: 0, screenTop: 0, screenRight: 2000, screenBottom: 1000);

                Assert.True(window.Height >= 400); // MinHeight
            });
        }

        [Fact]
        public void PositionNextTo_NullOwner_DoesNotThrow()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new AiAssistantWindow { Width = 400 };

                var ex = Record.Exception(() => window.PositionNextTo(null!));

                Assert.Null(ex);
            });
        }

        [Fact]
        public void AiAssistantWindow_CanBeShownAndClosed()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new AiAssistantWindow();

                window.Show();
                Assert.True(window.IsVisible);

                window.Close();
                Assert.False(window.IsVisible);
            });
        }

        [Fact]
        public void AiAssistantWindow_ViewModel_CanBeSetAndRead()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new AiAssistantWindow();
                var viewModel = new AiAssistantViewModel();

                window.ViewModel = viewModel;

                Assert.Same(viewModel, window.ViewModel);
                Assert.Same(viewModel, window.DataContext);
            });
        }

        [Fact]
        public void AiAssistantWindow_IsFixedSizeAndBorderless()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new AiAssistantWindow();

                // The AI window should be a non-movable panel attached to the
                // main window, not an independent resizable window.
                Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
                Assert.Equal(WindowStyle.None, window.WindowStyle);
                Assert.False(window.ShowInTaskbar);
            });
        }
    }

    /// <summary>
    /// Pure-math tests for <see cref="MainWindow.ComputeCenteredGroupLeft"/> —
    /// how the MAIN window shifts so the group «program + AI dock» fits on the
    /// screen and stays centered when the docked AI can't sit to the right.
    /// </summary>
    public class MainWindowAiDockCenteringTests
    {
        [Fact]
        public void ComputeCenteredGroupLeft_CentersGroup_WhenDockFits()
        {
            // 1200-wide program + 420-wide dock = 1620 group on a 1920 screen:
            // the main window shifts left so the whole group is centered.
            double left = MainWindow.ComputeCenteredGroupLeft(0, 1920, 1200, 420);

            Assert.Equal(150, left); // (1920 − 1620) / 2
        }

        [Fact]
        public void ComputeCenteredGroupLeft_ClampsToScreenLeft_WhenGroupWiderThanScreen()
        {
            // 1620 group on a 1600 screen → the group can't be centered without
            // going off-screen; the main window clamps to the left edge (0).
            double left = MainWindow.ComputeCenteredGroupLeft(0, 1600, 1200, 420);

            Assert.Equal(0, left);
        }

        [Fact]
        public void ComputeCenteredGroupLeft_RespectsNonZeroScreenLeft()
        {
            // Working area starts at 100 (multi-monitor): the group is centered
            // relative to the whole working area, not the origin.
            double left = MainWindow.ComputeCenteredGroupLeft(100, 1900, 1200, 420);

            Assert.Equal(190, left); // 100 + (1800 − 1620) / 2
        }

        [Fact]
        public void ComputeCenteredGroupLeft_ClampsToScreenLeft_WhenMainWiderThanScreen()
        {
            // Main window alone is wider than the screen (1200 on 1000) → clamp to 0.
            double left = MainWindow.ComputeCenteredGroupLeft(0, 1000, 1200, 420);

            Assert.Equal(0, left);
        }

        [Fact]
        public void ComputeCenteredGroupTop_CentersVertically_WhenWindowSmallerThanScreen()
        {
            // 760-tall program on a 1400-tall working area → top = (1400−760)/2.
            double top = MainWindow.ComputeCenteredGroupTop(0, 1400, 760);

            Assert.Equal(320, top);
        }

        [Fact]
        public void ComputeCenteredGroupTop_RespectsNonZeroScreenTop()
        {
            // Working area starts at 50 (taskbar offset on secondary monitor).
            double top = MainWindow.ComputeCenteredGroupTop(50, 1050, 760);

            Assert.Equal(170, top); // 50 + (1000 − 760) / 2
        }

        [Fact]
        public void ComputeCenteredGroupTop_ClampsToScreenTop_WhenWindowTallerThanScreen()
        {
            // 900-tall program on a 600-tall screen → top cannot be negative; clamp to 0.
            double top = MainWindow.ComputeCenteredGroupTop(0, 600, 900);

            Assert.Equal(0, top);
        }
    }
}
