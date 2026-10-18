using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MosquitoNetCalculator.Helpers;

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

        // Pre-first-render width cache per panel (cleared never: overlay panels
        // have fixed XAML Width, so the value cannot go stale).
        private readonly System.Collections.Generic.Dictionary<OverlayEntry, double> _measuredWidths = new();

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
        /// <summary>
        /// Shows an overlay with a slide-in animation and backdrop fade-in.
        /// Instantly hides any other visible overlay first.
        /// Optionally sets the active nav button via the callback.
        /// </summary>
        public void Show(OverlayEntry entry, string? navTag = null)
        {
            bool anyPreviouslyVisible = false;
            // Close any other open overlay first
            foreach (var ov in _overlays)
            {
                if (ov != entry && ov.Grid.Visibility == Visibility.Visible)
                {
                    anyPreviouslyVisible = true;
                    HideInstant(ov.Grid);
                }
            }

            entry.Grid.Visibility = Visibility.Visible;

            double panelWidth = GetTargetPanelWidth(entry);

            if (anyPreviouslyVisible)
            {
                // Если оверлей уже был открыт — пользователь уже в режиме боковой панели:
                // не мигаем бэкдропом и не тащим 800px панель издалека. Снимаем остаточные анимации
                // и выставляем открытое состояние мгновенно без артефактов и задержек.
                entry.Backdrop.BeginAnimation(UIElement.OpacityProperty, null);
                entry.SlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
                entry.Backdrop.Opacity = 1;
                entry.SlideTransform.X = 0;
            }
            else
            {
                entry.Backdrop.Opacity = 0;
                entry.SlideTransform.X = panelWidth;

                var slideAnim = new DoubleAnimation(0, Motion.Slow)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                entry.SlideTransform.BeginAnimation(TranslateTransform.XProperty, slideAnim);

                var fadeIn = new DoubleAnimation(1, Motion.Base)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                entry.Backdrop.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }

            if (navTag != null)
                _onSetActiveNav(navTag);
        }

        private double GetTargetPanelWidth(OverlayEntry entry)
        {
            if (entry.Panel.ActualWidth > 0)
                return entry.Panel.ActualWidth;

            if (_measuredWidths.TryGetValue(entry, out double cachedWidth) && cachedWidth > 0)
                return cachedWidth;

            double targetWidth = entry.Panel.Width > 0 && !double.IsNaN(entry.Panel.Width)
                ? entry.Panel.Width
                : (entry.Panel.MaxWidth > 0 && !double.IsInfinity(entry.Panel.MaxWidth) ? entry.Panel.MaxWidth : 800);

            _measuredWidths[entry] = targetWidth;
            return targetWidth;
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
                var slideOut = new DoubleAnimation(panelWidth, Motion.Base)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                slideOut.Completed += (_, _) => { grid.Visibility = Visibility.Collapsed; };
                slide.BeginAnimation(TranslateTransform.XProperty, slideOut);

                var fadeOut = new DoubleAnimation(0, Motion.Base)
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
            var slideOut = new DoubleAnimation(panelWidth, Motion.Slow)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            slideOut.Completed += (_, _) => { entry.Grid.Visibility = Visibility.Collapsed; };
            entry.SlideTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);

            var fadeOut = new DoubleAnimation(0, Motion.Base)
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
