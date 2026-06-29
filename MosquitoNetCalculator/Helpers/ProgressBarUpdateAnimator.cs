using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace MosquitoNetCalculator.Helpers
{
    /// <summary>
    /// Testable wrapper around the download ProgressBar's fade-in/out logic.
    ///
    /// Extracted from <c>MainWindow.OnUpdateProgressChanged</c> in v3.40.3 so
    /// unit tests can exercise the exception-swallowing behaviour without
    /// having to spin up a full WPF <c>MainWindow</c> (which has dozens of
    /// side-effects in its constructor: theme service, undo/redo, dispatcher
    /// hooks, orders load, prices load, etc.).
    ///
    /// ─── Contract ──────────────────────────────────────────────────────────
    /// • Constructor takes a resource owner (any FrameworkElement — Window,
    ///   UserControl, etc.) for <c>TryFindResource</c> walks and a target
    ///   <see cref="ProgressBar"/> to animate.
    /// • Pure-logic getters (<see cref="Func{TResult}"/> callbacks) for
    ///   "what's the current download progress" and "is anything being
    ///   downloaded right now" — keep the animator decoupled from
    ///   <see cref="Services.UpdateService"/> so tests don't need the full
    ///   app shell to drive it.
    /// • <see cref="Animate"/> is safe to call repeatedly on the UI thread;
    ///   it cancels any in-flight storyboard before starting a new one, so
    ///   the user can spam show/hide transitions without leaks.
    /// • Belt-and-suspenders: catches <see cref="Exception"/> but rethrows
    ///   <see cref="OutOfMemoryException"/> / <see cref="StackOverflowException"/>
    ///   so a half-dead WPF process doesn't continue its auto-update flow
    ///   on a corrupted heap (see v3.40.2 design notes).
    ///
    /// ─── Resource behaviour ───────────────────────────────────────────────
    /// <c>TryFindResource</c> is used (NOT <c>FindResource</c>) so a missing
    /// Storyboard never throws — we fall back to a direct
    /// Visibility/Opacity flip and emit a Debug log. This mirrors the
    /// pattern that fixed the v3.40.0 <c>ResourceReferenceKeyNotFoundException</c>
    /// crash; cloning it here ensures it isn't accidentally re-introduced
    /// by a future refactor.
    /// </summary>
    internal sealed class ProgressBarUpdateAnimator
    {
        private readonly FrameworkElement _resourceOwner;
        private readonly ProgressBar _bar;
        private readonly Func<double> _progressGetter;
        private readonly Func<bool> _isDownloadingGetter;

        private Storyboard? _activeStoryboard;

        public ProgressBarUpdateAnimator(
            FrameworkElement resourceOwner,
            ProgressBar bar,
            Func<double> progressGetter,
            Func<bool> isDownloadingGetter)
        {
            _resourceOwner = resourceOwner ?? throw new ArgumentNullException(nameof(resourceOwner));
            _bar = bar ?? throw new ArgumentNullException(nameof(bar));
            _progressGetter = progressGetter ?? throw new ArgumentNullException(nameof(progressGetter));
            _isDownloadingGetter = isDownloadingGetter ?? throw new ArgumentNullException(nameof(isDownloadingGetter));
        }

        /// <summary>
        /// Compares <paramref name="shouldBeVisible"/> against the bar's
        /// current visibility. If they differ, starts the corresponding
        /// fade Storyboard (or directly flips Visibility if the resource
        /// is missing). No-op if the state already matches.
        /// </summary>
        /// <remarks>
        /// Must be called on the UI thread (WPF dispatcher) — Storyboard.Begin
        /// and DependencyProperty writes require it. Tests using this method
        /// should run on an STA thread with a DispatcherFrame pump pattern
        /// (see <c>MosquitoNetCalculator.Tests/Helpers/ProgressBarUpdateAnimatorTests.cs</c>).
        /// </remarks>
        public void Animate(bool shouldBeVisible)
        {
            try
            {
                if (_bar == null) return;

                _bar.Value = _progressGetter();

                bool currentlyVisible = _bar.Visibility == Visibility.Visible;

                // No state change — avoid re-triggering the animation on
                // every tick (ProgressChanged fires on every download byte,
                // we only want to animate the show→hide and hide→show edges).
                if (shouldBeVisible == currentlyVisible) return;

                // Cancel any in-flight storyboard before starting a new one.
                if (_activeStoryboard != null)
                {
                    _activeStoryboard.Completed -= OnActiveStoryboardCompleted;
                    _activeStoryboard.Stop(_bar);
                }

                // TryFindResource (not FindResource): never throws.
                // If a missing resource ever surfaces here, fall back to
                // a direct Visibility flip — better UX than a crash, and
                // matches the v3.40.1 fix that originally added this guard.
                string key = shouldBeVisible ? "UpdateBarFadeIn" : "UpdateBarFadeOut";
                if (_resourceOwner.TryFindResource(key) is not Storyboard template)
                {
                    Debug.WriteLine($"[ProgressBarUpdateAnimator] Storyboard '{key}' not found — bar visibility set without animation.");
                    _bar.Visibility = shouldBeVisible ? Visibility.Visible : Visibility.Collapsed;
                    _bar.Opacity = shouldBeVisible ? 1.0 : 0.0;
                    return;
                }

                _activeStoryboard = template.Clone();
                if (!shouldBeVisible)
                {
                    // From-value is dynamic so the fade starts from the
                    // bar's CURRENT opacity (handles a hide→show→hide→show
                    // sequence where the previous fade-in was caught mid-way).
                    var fadeOutAnim = _activeStoryboard.Children.OfType<DoubleAnimation>().FirstOrDefault();
                    if (fadeOutAnim != null)
                        fadeOutAnim.From = _bar.Opacity;
                    _activeStoryboard.Completed += OnActiveStoryboardCompleted;
                }

                if (shouldBeVisible)
                {
                    _bar.Visibility = Visibility.Visible;
                    // Opacity=0 because the fade-in Storyboard animates FROM 0.
                    _bar.Opacity = 0;
                }

                _activeStoryboard.Begin(_bar);
            }
            catch (Exception ex)
            {
                // Belt-and-suspenders: do NOT let a UI exception abort the
                // auto-update flow. OOM/SOF are still re-thrown because
                // continuing on a half-dead WPF process is worse than crashing
                // (a degraded brush system would corrupt subsequent paints).
                if (ex is OutOfMemoryException or StackOverflowException)
                    throw;

                Debug.WriteLine($"[ProgressBarUpdateAnimator] Animate swallowed exception: {ex}");

                if (_bar != null)
                {
                    bool downloading = _isDownloadingGetter();
                    _bar.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
                    _bar.Opacity = downloading ? 1.0 : 0.0;
                }
            }
        }

        private void OnActiveStoryboardCompleted(object? sender, EventArgs e)
        {
            // Only truly collapse if no follow-up download was kicked off
            // during the fade-out animation. Otherwise we'd flicker.
            if (!_isDownloadingGetter())
                _bar.Visibility = Visibility.Collapsed;
        }
    }
}
