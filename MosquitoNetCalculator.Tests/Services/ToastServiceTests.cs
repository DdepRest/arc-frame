using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Unit tests for <see cref="ToastService"/> scope-routing dictionary.
    /// Focuses on RegisterCanvas / UnregisterCanvas / ShowToast(scope, ...)
    /// behavior. WPF-dependent rendering (fade animations, Style lookup) is
    /// not asserted directly — those run on STA inside MainWindow; we only
    /// need the routing state machine to be correct.
    ///
    /// ─── Cleanup pattern ───────────────────────────────────────────────
    /// Tests do NOT implement <see cref="IDisposable"/>: WPF <see cref="Panel"/>
    /// objects are affinity-bound to the thread that created them, but xUnit
    /// would call Dispose on the test runner thread (not the STA thread that
    /// called <see cref="ToastService.RegisterCanvas"/>), causing
    /// <c>InvalidOperationException: calling thread cannot access this object
    /// because it is owned by another thread</c>. Instead, each test wraps its
    /// body in <c>try/finally</c> INSIDE <see cref="WpfTestHelper.RunOnSta"/>
    /// so cleanup runs on the same thread that registered the canvas. A
    /// fresh per-test scope id (via <see cref="NewScope"/>) prevents
    /// cross-test pollution even when xUnit parallelizes test classes.
    /// </summary>
    public class ToastServiceTests
    {
        /// <summary>Fresh scope id so each test's mappings don't leak.</summary>
        private static string NewScope() => "TestScope_" + Guid.NewGuid().ToString("N");

        [Fact]
        public void Initialize_RegistersCanvasUnderMainScope()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var canvas = new Grid();
                try
                {
                    ToastService.Initialize(canvas);
                    ToastService.ShowToast("hello main");
                    Assert.Single(canvas.Children);
                }
                finally
                {
                    // Cleanup Main scope so other tests aren't affected.
                    ToastService.UnregisterCanvas(ToastService.MainScope);
                }
            });
        }

        [Fact]
        public void RegisterCanvas_NewScope_AddsEntryAndRoutesToasts()
        {
            var scope = NewScope();
            WpfTestHelper.RunOnSta(() =>
            {
                var canvas = new Grid();
                try
                {
                    ToastService.RegisterCanvas(scope, canvas);
                    ToastService.ShowToast(scope, "hello scoped");
                    Assert.Single(canvas.Children);
                }
                finally
                {
                    ToastService.UnregisterCanvas(scope);
                }
            });
        }

        [Fact]
        public void ShowToast_UnregisteredScope_IsSilentNoOp()
        {
            var scope = NewScope();
            WpfTestHelper.RunOnSta(() =>
            {
                var canvas = new Grid();
                // Scope was never registered — ShowToast must silently no-op.
                ToastService.ShowToast(scope, "nowhere to land", ToastType.Warning);
                Assert.Empty(canvas.Children);
                // No finally needed: nothing was registered.
            });
        }

        [Fact]
        public void RegisterCanvas_SameScopeSecondCall_ReplacesPriorCanvas()
        {
            var scope = NewScope();
            WpfTestHelper.RunOnSta(() =>
            {
                var canvas1 = new Grid();
                var canvas2 = new Grid();
                try
                {
                    ToastService.RegisterCanvas(scope, canvas1);
                    ToastService.ShowToast(scope, "first canvas");
                    Assert.Single(canvas1.Children);
                    Assert.Empty(canvas2.Children);

                    // Re-register under the same scope with a different physical canvas.
                    ToastService.RegisterCanvas(scope, canvas2);
                    // Hygiene contract: both sides of the swap must be clean at this
                    // exact point — the prior canvas drained AND the new canvas
                    // still empty (no ShowToast has run on it yet). A regression that
                    // left toasts in canvas1 or pre-populated canvas2 would fail here.
                    Assert.Empty(canvas1.Children);
                    Assert.Empty(canvas2.Children);
                    ToastService.ShowToast(scope, "second canvas");
                    // New canvas now hosts the new toast; routing switched cleanly.
                    Assert.Single(canvas2.Children);
                }
                finally
                {
                    ToastService.UnregisterCanvas(scope);
                }
            });
        }

        [Fact]
        public void UnregisterCanvas_RemovesAllScopeEntries()
        {
            var scope = NewScope();
            WpfTestHelper.RunOnSta(() =>
            {
                var canvas = new Grid();
                try
                {
                    ToastService.RegisterCanvas(scope, canvas);
                    ToastService.ShowToast(scope, "before unregister");
                    Assert.Single(canvas.Children); // sanity: scope is wired
                    ToastService.UnregisterCanvas(scope);

                    // Pump dispatcher so any pending cleanup anims/timers can settle
                    // before we re-register under the same scope.
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

                    // After unregister, ShowToast on the same scope silently no-ops.
                    var tracker = new Grid();
                    ToastService.RegisterCanvas(scope, tracker);
                    ToastService.ShowToast(scope, "after re-register");
                    Assert.Single(tracker.Children);
                }
                finally
                {
                    ToastService.UnregisterCanvas(scope);
                }
            });
        }

        [Fact]
        public void ShowToast_Scoped_WithMessage_PassesThrough()
        {
            // Sanity: scoping doesn't silently drop messages when scope IS registered.
            var scope = NewScope();
            WpfTestHelper.RunOnSta(() =>
            {
                var canvas = new Grid();
                try
                {
                    ToastService.RegisterCanvas(scope, canvas);
                    ToastService.ShowToast(scope, "Тест с кириллицей и emoji \uD83D\uDD14", ToastType.Warning);
                    Assert.Single(canvas.Children);
                }
                finally
                {
                    ToastService.UnregisterCanvas(scope);
                }
            });
        }

        [Fact]
        public void RegisterCanvas_ReplacingCanvas_DrainsAllActiveToasts()
        {
            // Stronger hygiene check: when canvas1 has SEVERAL toasts active and we
            // re-register under canvas2, ALL of canvas1's toast visuals must be
            // drained — not just the most recent one. Without this guarantee, ghost
            // toasts stay in canvas1 forever (timer ticks fire RemoveToast against
            // the new canvas which doesn't contain them).
            var scope = NewScope();
            WpfTestHelper.RunOnSta(() =>
            {
                var canvas1 = new Grid();
                var canvas2 = new Grid();
                try
                {
                    ToastService.RegisterCanvas(scope, canvas1);
                    ToastService.ShowToast(scope, "toast A on canvas1");
                    ToastService.ShowToast(scope, "toast B on canvas1");
                    ToastService.ShowToast(scope, "toast C on canvas1");
                    Assert.Equal(3, canvas1.Children.Count);
                    Assert.Empty(canvas2.Children);

                    // Re-register and assert ALL three toasts are drained from canvas1.
                    ToastService.RegisterCanvas(scope, canvas2);
                    Assert.Empty(canvas1.Children);
                    Assert.Empty(canvas2.Children);

                    // Subsequent ShowToast must land on canvas2, never canvas1.
                    ToastService.ShowToast(scope, "toast D on canvas2");
                    Assert.Empty(canvas1.Children);
                    Assert.Single(canvas2.Children);
                }
                finally
                {
                    ToastService.UnregisterCanvas(scope);
                }
            });
        }
    }
}
