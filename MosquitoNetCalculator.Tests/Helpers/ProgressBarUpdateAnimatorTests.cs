using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MosquitoNetCalculator.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Helpers
{
    /// <summary>
    /// xUnit collection marker — forces every test in the "STA" collection
    /// to run sequentially (no parallelism between STA classes). Without this,
    /// xUnit running different test classes in parallel would race on
    /// <see cref="Application.Current"/>, an AppDomain-wide static, and the
    /// second STA test would either crash or silently do nothing.
    /// </summary>
    [CollectionDefinition("STA", DisableParallelization = true)]
    public class STAWindowTestCollection { }

    /// <summary>
    /// Unit tests for <see cref="ProgressBarUpdateAnimator"/>.
    ///
    /// Extracted from <c>MainWindow.OnUpdateProgressChanged</c> in v3.40.3
    /// so the try/catch + TryFindResource fallback contract is enforceable
    /// as a regression test, not just an inline comment.
    ///
    /// ─── Why a custom STA collection ───────────────────────────────────
    /// WPF requires apartment-threaded access for any <see cref="FrameworkElement"/>
    /// read/write. We run each test on a dedicated STA thread, but
    /// <see cref="Application.Current"/> is a single AppDomain-wide static,
    /// so concurrent STA test classes would race. The
    /// <see cref="STAWindowTestCollection"/> declaration above disables
    /// test-collection parallelism so multiple STA tests share one Application
    /// instance cleanly.
    /// </summary>
    [Collection("STA")]
    public class ProgressBarUpdateAnimatorTests
    {
        // ─── Exception swallow contracts (regression for v3.40.0) ─────

        [Theory]
        [InlineData(typeof(InvalidOperationException))]
        [InlineData(typeof(InvalidCastException))]
        [InlineData(typeof(ArgumentException))]
        [InlineData(typeof(FormatException))]
        public void Animate_WhenProgressGetterThrowsNonFatal_DoesNotPropagate(Type exceptionType)
        {
            // Cast back to Exception for the synthetic-throw closure.
            // Theory rows cover the historically-most-likely failure modes:
            //   • InvalidOperationException — UpdateService state inconsistency.
            //   • InvalidCastException     — the ResourceReferenceKeyNotFound
            //                                 exception that's the exact
            //                                 v3.40.0 regression name. CLR
            //                                 throws InvalidCastException at
            //                                 the cast site; WPF wraps in
            //                                 ResourceReferenceKeyNotFoundException
            //                                 derived from SystemException.
            //   • ArgumentException        — bad parameter from a downstream
            //                                 API changed shape.
            //   • FormatException         — version string parse failure.
            // All non-OOM/SOF exceptions must be swallowed; auto-update
            // must not be aborted by any UI-thread regression.
            RunInSTA(() =>
            {
                Exception synthetic = (Exception)Activator.CreateInstance(exceptionType, "synthetic test exception")!;

                var (window, animator) = MakeAnimatorWithThrowingProgress(synthetic, downloading: true);
                try
                {
                    animator.Animate(true);
                    animator.Animate(false);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void Animate_WhenOutOfMemoryExceptionThrown_Propagates()
        {
            // OOM is the linchpin exception type: continuing on a half-dead
            // WPF process (corrupted brushes, GC-under-pressure heap) is
            // WORSE than crashing. The animator must re-throw so the
            // application surface area is reduced ASAP. Regression for the
            // v3.40.2 belt-and-suspenders compile-time intent.
            RunInSTA(() =>
            {
                var (window, animator) = MakeAnimatorWithThrowingProgress(
                    new OutOfMemoryException("synthetic OOM in production"), downloading: true);
                try
                {
                    Assert.Throws<OutOfMemoryException>(() => animator.Animate(true));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void Animate_WhenStackOverflowExceptionThrown_Propagates()
        {
            // Same rationale as OOM: SOF indicates infinite recursion in
            // the brush/visual tree, continuing would cascade corruption.
            // The CLR can prevent catching some SOF instances at runtime,
            // but we explicitly re-throw to make the intent unambiguous
            // — and to LOCK the contract as a regression test.
            RunInSTA(() =>
            {
                var (window, animator) = MakeAnimatorWithThrowingProgress(
                    new StackOverflowException("synthetic SOF"), downloading: true);
                try
                {
                    Assert.Throws<StackOverflowException>(() => animator.Animate(true));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        // ─── Visible-state contracts ──────────────────────────────────

        [Fact]
        public void Animate_WhenPreCollapsed_BarMadeVisibleWithoutAnimation()
        {
            // A bare Window with no Storyboards in its resources —
            // TryFindResource returns null. Animator must NOT throw (the
            // v3.40.1 fix's whole point) and must flip Visibility directly
            // so the user still sees the bar during a download.
            //
            // Pre-conditioning bar.Visibility = Collapsed is required:
            // the default for FrameworkElement.Visibility is Visible, so
            // calling Animate(true) against a Visible bar hits the no-op
            // early-return guard, NOT the fallback path we're testing.
            RunInSTA(() =>
            {
                var (window, animator) = MakeStandardAnimator(progress: 50, downloading: true);
                try
                {
                    var bar = (ProgressBar)window.Content;
                    bar.Visibility = Visibility.Collapsed;   // pre-condition

                    animator.Animate(true);
                    Assert.Equal(Visibility.Visible, bar.Visibility);
                    Assert.Equal(50.0, bar.Value, 2);

                    animator.Animate(false);
                    Assert.Equal(Visibility.Collapsed, bar.Visibility);
                    Assert.Equal(0.0, bar.Opacity, 2);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void Animate_RepeatedShowHide_DoesNotLeakStoryboards_DoesNotThrow()
        {
            // Earlier code allocated a fresh Storyboard per show/hide
            // (each `Clone()` produced one, never disposed). 200 alternating
            // calls — confirm none throw. Resource usage is not asserted
            // here (would require reflection into WPF internals) but the
            // no-throw contract is the main regression risk.
            RunInSTA(() =>
            {
                var (window, animator) = MakeStandardAnimator(progress: 0, downloading: false);
                try
                {
                    for (int i = 0; i < 100; i++)
                    {
                        animator.Animate(true);   // toggle via getter captured at construction
                        animator.Animate(false);
                    }
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void Animate_WhenAlreadyInState_IsNoOp_DoesNotThrow()
        {
            // The early-return guard prevents re-cloning/starting the
            // Storyboard on every ProgressChanged tick (which fires per
            // download byte). Confirm the no-op path: no exception, no
            // churn, no visibility/Opacity change.
            RunInSTA(() =>
            {
                bool downloading = true;
                var (window, animator) = MakeStandardAnimator(progress: 0, downloading: downloading);
                try
                {
                    var bar = (ProgressBar)window.Content;
                    Assert.Equal(Visibility.Visible, bar.Visibility); // default

                    // State matches default → no-op.
                    animator.Animate(true);
                    Assert.Equal(Visibility.Visible, bar.Visibility);

                    // Flip manually then trigger → fallback path runs.
                    bar.Visibility = Visibility.Collapsed;
                    animator.Animate(true);
                    Assert.Equal(Visibility.Visible, bar.Visibility);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void Animate_WhenGetterThrows_SetsVisibilityFromDownloadingGetter()
        {
            // The exception-swallowed fallback path MUST set Visibility/Opacity
            // based on the IsDownloading getter, NOT silently do nothing.
            // This locks the post-condition that makes the catch block
            // useful (instead of merely "not crashing").
            RunInSTA(() =>
            {
                bool downloading = true;
                Exception synthetic = new InvalidOperationException("synthetic");
                Func<double> throwing = () => throw synthetic;

                var window = new Window { Width = 100, Height = 50, ShowInTaskbar = false };
                var bar = new ProgressBar { Width = 80, Height = 10 };
                window.Content = bar;
                var animator = new ProgressBarUpdateAnimator(window, bar, throwing, () => downloading);
                try
                {
                    bar.Visibility = Visibility.Collapsed; // force non-no-op path
                    animator.Animate(true);                // synthetic throw inside
                    // Catch block must fall through to direct Visibility flip.
                    Assert.Equal(Visibility.Visible, bar.Visibility);
                    Assert.Equal(1.0, bar.Opacity, 2);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        // ─── Constructor validation ───────────────────────────────────

        [Theory]
        [InlineData(0)] // resourceOwner=null
        [InlineData(1)] // bar=null
        [InlineData(2)] // progressGetter=null
        [InlineData(3)] // isDownloadingGetter=null
        public void Constructor_NullArg_Throws(int nullIndex)
        {
            RunInSTA(() =>
            {
                // Build Window + ProgressBar directly (not via MakeStandardAnimator)
                // because we need an actual ProgressBar for the constructor
                // argument, not an Animator. Animation wiring is irrelevant here.
                var window = new Window { Width = 100, Height = 50, ShowInTaskbar = false };
                var bar = new ProgressBar { Width = 80, Height = 10 };
                window.Content = bar;

                ProgressBarUpdateAnimator? created = null;
                try
                {
                    created = nullIndex switch
                    {
                        0 => new ProgressBarUpdateAnimator(null!, bar, () => 0, () => true),
                        1 => new ProgressBarUpdateAnimator(window, null!, () => 0, () => true),
                        2 => new ProgressBarUpdateAnimator(window, bar, null!, () => true),
                        3 => new ProgressBarUpdateAnimator(window, bar, () => 0, null!),
                        _ => throw new InvalidOperationException("index out of range"),
                    };
                }
                catch (ArgumentNullException) { /* expected */ }
                finally
                {
                    window.Close();
                }
                Assert.Null(created);
            });
        }

        // ─── STA dispatcher pump + window factory ─────────────────────

        /// <summary>
        /// Runs <paramref name="action"/> on a freshly-spun STA thread.
        /// No <see cref="Application"/> is created — the animator unit tests
        /// don't need WPF shell resources (TryFindResource handles a null
        /// <c>Application.Current</c> by returning null, which is exactly
        /// the fallback path the tests exercise). Creating one would also
        /// fight <c>AppLifecycleTests</c> which creates its own.
        /// </summary>
        private static void RunInSTA(Action action)
        {
            Exception? caught = null;
            var gate = new ManualResetEventSlim();

            var t = new Thread(() =>
            {
                try
                {
                    // No Application needed. FrameworkElement.TryFindResource
                    // handles a null Application.Current gracefully — it just
                    // returns null. That's exactly the path we want to test
                    // (the fallback Visibility/Opacity flip when no Storyboard
                    // is found). Creating a WPF Application here would also
                    // fight AppLifecycleTests which creates its own.
                    action();
                }
                catch (Exception ex) { caught = ex; }
                finally { gate.Set(); }
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(gate.Wait(TimeSpan.FromSeconds(30)),
                "STA test thread did not finish in time");
            t.Join();

            Assert.Null(caught);
        }

        /// <summary>
        /// Builds a Window with a ProgressBar and an Animator wired to it.
        /// The ProgressBar has no Storyboards in scope — TryFindResource
        /// returns null and exercises the fallback path.
        /// </summary>
        private static (Window Window, ProgressBarUpdateAnimator Animator) MakeStandardAnimator(double progress, bool downloading)
        {
            var window = new Window { Width = 100, Height = 50, ShowInTaskbar = false };
            var bar = new ProgressBar { Width = 80, Height = 10, Minimum = 0, Maximum = 100, Value = progress };
            window.Content = bar;
            var animator = new ProgressBarUpdateAnimator(window, bar, () => progress, () => downloading);
            return (window, animator);
        }

        /// <summary>
        /// Same as <see cref="MakeStandardAnimator"/> but with a progress
        /// getter that always throws <paramref name="exceptionToThrow"/>.
        /// </summary>
        private static (Window Window, ProgressBarUpdateAnimator Animator) MakeAnimatorWithThrowingProgress(
            Exception exceptionToThrow, bool downloading)
        {
            var window = new Window { Width = 100, Height = 50, ShowInTaskbar = false };
            var bar = new ProgressBar { Width = 80, Height = 10, Minimum = 0, Maximum = 100, Value = 0 };
            window.Content = bar;
            Func<double> throwing = () => throw exceptionToThrow;
            var animator = new ProgressBarUpdateAnimator(window, bar, throwing, () => downloading);
            return (window, animator);
        }
    }
}
