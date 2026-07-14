using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Manages overlay panels: show with slide-in animation, close all with
    /// slide-out, and instant hide for transitions.
    /// Extracted from <see cref="MainWindow"/> as part of Phase 1 refactoring
    /// (REFACTORING_PLAN.md §3.2 — OverlayManager component).
    /// </summary>
    public sealed class OverlayManager
    {
        /// <summary>
        /// Represents one overlay panel with its associated backdrop and slide transform.
        /// </summary>
        public sealed record OverlayEntry(
            Grid Grid,
            Border Panel,
            Border Backdrop,
            TranslateTransform SlideTransform);

        private readonly OverlayEntry[] _overlays;
        private readonly Action<string> _onSetActiveNav;
        private readonly Action? _onBeforeClosePrint;

        /// <summary>
        /// Creates an OverlayManager.
        /// </summary>
        /// <param name="overlays">All overlay entries managed by this instance.</param>
        /// <param name="onSetActiveNav">Callback to highlight the active nav button (e.g. "Calc" when closing).</param>
        /// <param name="onBeforeClosePrint">Optional callback invoked before closing the Print overlay (to save print settings).</param>
        public OverlayManager(
            OverlayEntry[] overlays,
            Action<string> onSetActiveNav,
            Action? onBeforeClosePrint = null)
        {
            _overlays = overlays;
            _onSetActiveNav = onSetActiveNav;
            _onBeforeClosePrint = onBeforeClosePrint;
        }

        // ── Toggle ──────────────────────────────────────────────────

        /// <summary>
        /// Toggles an overlay: if visible → closes all; if hidden → shows.
        /// Simplifies NavButton_Click switch cases.
        /// </summary>
        public void Toggle(OverlayEntry entry, string? navTag = null)
        {
            if (entry.Grid.Visibility == Visibility.Visible)
                CloseAll();
            else
                Show(entry, navTag);
        }

        // ── Show ─────────────────────────────────────────────────────

        /// <summary>
        /// Shows an overlay with a slide-in animation and backdrop fade-in.
        /// Instantly hides any other visible overlay first.
        /// Optionally sets the active nav button via the callback.
        /// </summary>
        public void Show(OverlayEntry entry, string? navTag = null)
        {
            // Close any other open overlay first
            foreach (var ov in _overlays)
            {
                if (ov != entry && ov.Grid.Visibility == Visibility.Visible)
                    HideInstant(ov.Grid);
            }

            entry.Grid.Visibility = Visibility.Visible;
            entry.Backdrop.Opacity = 0;

            // Force measure to get correct ActualWidth on first open
            entry.Panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double panelWidth = entry.Panel.ActualWidth > 0 ? entry.Panel.ActualWidth : 800;
            entry.SlideTransform.X = panelWidth;

            var slideAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            entry.SlideTransform.BeginAnimation(TranslateTransform.XProperty, slideAnim);

            var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            entry.Backdrop.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            if (navTag != null)
                _onSetActiveNav(navTag);
        }

        // ── CloseAll ─────────────────────────────────────────────────

        /// <summary>
        /// Animates slide-out + backdrop fade-out for every visible overlay,
        /// then collapses them. Sets active nav to "Calc".
        /// </summary>
        public void CloseAll()
        {
            _onBeforeClosePrint?.Invoke();

            foreach (var (grid, panel, backdrop, slide) in _overlays)
            {
                if (grid.Visibility != Visibility.Visible) continue;

                // Cancel any running animations before starting close
                slide.BeginAnimation(TranslateTransform.XProperty, null);
                backdrop.BeginAnimation(UIElement.OpacityProperty, null);

                double panelWidth = panel.ActualWidth > 0 ? panel.ActualWidth : 800;
                var slideOut = new DoubleAnimation(panelWidth, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                slideOut.Completed += (_, _) => { grid.Visibility = Visibility.Collapsed; };
                slide.BeginAnimation(TranslateTransform.XProperty, slideOut);

                var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                backdrop.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }

            _onSetActiveNav("Calc");
        }

        // ── CloseSingle ──────────────────────────────────────────────

        /// <summary>
        /// Animates slide-out for a single overlay (e.g. SlopeOverlay).
        /// Sets active nav to "Calc".
        /// </summary>
        /// <param name="fallbackWidth">Fallback panel width if ActualWidth is 0 (480 for slope, 800 default).</param>
        public void CloseSingle(OverlayEntry entry, double fallbackWidth = 480)
        {
            if (entry.Grid.Visibility != Visibility.Visible) return;

            entry.SlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
            entry.Backdrop.BeginAnimation(UIElement.OpacityProperty, null);

            double panelWidth = entry.Panel.ActualWidth > 0 ? entry.Panel.ActualWidth : fallbackWidth;
            var slideOut = new DoubleAnimation(panelWidth, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            slideOut.Completed += (_, _) => { entry.Grid.Visibility = Visibility.Collapsed; };
            entry.SlideTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);

            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            entry.Backdrop.BeginAnimation(UIElement.OpacityProperty, fadeOut);

            _onSetActiveNav("Calc");
        }

        // ── HideInstant ──────────────────────────────────────────────

        /// <summary>
        /// Instantly collapses an overlay without animation. Cancels any
        /// running animations so they don't interfere with a re-open.
        /// Used for transitions where the overlay will be immediately
        /// replaced by another.
        /// </summary>
        public static void HideInstant(Grid overlay)
        {
            overlay.Visibility = Visibility.Collapsed;
            foreach (var child in overlay.Children)
            {
                if (child is Border border)
                {
                    border.BeginAnimation(UIElement.OpacityProperty, null);
                    if (border.RenderTransform is TranslateTransform t)
                        t.BeginAnimation(TranslateTransform.XProperty, null);
                }
            }
        }

        /// <summary>
        /// Instantly hides all overlays except the specified one.
        /// Used before showing a new overlay (by Show internally).
        /// </summary>
        public void HideAllExcept(OverlayEntry? keep)
        {
            foreach (var ov in _overlays)
            {
                if (ov != keep && ov.Grid.Visibility == Visibility.Visible)
                    HideInstant(ov.Grid);
            }
        }
    }
}
