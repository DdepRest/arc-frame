using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Manages navigation button highlighting and slide-out panel expand/collapse.
    /// Extracted from <see cref="MainWindow"/> as part of Phase 1 refactoring
    /// (REFACTORING_PLAN.md §3.2 — NavigationService component).
    ///
    /// This class holds references to WPF controls but contains no business logic,
    /// making it safe to test in isolation on an STA thread.
    /// </summary>
    public sealed class NavigationService
    {
        private readonly Button[] _navButtons;
        private readonly TextBlock[] _navIcons;
        private readonly TextBlock[] _navLabels;
        private readonly FrameworkElement _navPanel;
        private readonly FrameworkElement _resourceOwner;
        private readonly DispatcherTimer _collapseTimer;
        private readonly MouseEventHandler _onMouseEnter;
        private readonly MouseEventHandler _onMouseLeave;

        /// <summary>
        /// Currently active navigation tag (e.g. "Calc", "Orders", "Prices",
        /// "Updates", "Slope", "Print"). Set by <see cref="SetActive"/>.
        /// </summary>
        public string ActiveTag { get; private set; } = "Calc";

        /// <summary>
        /// Creates a NavigationService bound to the specified WPF controls.
        /// All arrays must be the same length (currently 6: Calc, Orders,
        /// Prices, Updates, Slope, Print).
        /// </summary>
        /// <param name="navButtons">Navigation buttons ordered by index.</param>
        /// <param name="navIcons">Icon TextBlocks inside each button.</param>
        /// <param name="navLabels">Label TextBlocks that fade in/out on expand/collapse.</param>
        /// <param name="navPanel">The panel (Border or Grid) whose Width is animated on hover.</param>
        /// <param name="resourceOwner">A FrameworkElement (typically the Window) used for TryFindResource lookups. Window-level resources are searched before Application-level.</param>
        public NavigationService(
            Button[] navButtons,
            TextBlock[] navIcons,
            TextBlock[] navLabels,
            FrameworkElement navPanel,
            FrameworkElement resourceOwner)
        {
            _navButtons = navButtons;
            _navIcons = navIcons;
            _navLabels = navLabels;
            _navPanel = navPanel;
            _resourceOwner = resourceOwner;

            _collapseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _collapseTimer.Tick += (_, _) =>
            {
                _collapseTimer.Stop();
                Collapse();
            };

            // Wire hover events — store handlers for proper unwiring in Shutdown()
            _onMouseEnter = (_, _) =>
            {
                _collapseTimer.Stop();
                Expand();
            };
            _onMouseLeave = (_, _) =>
            {
                _collapseTimer.Start();
            };
            _navPanel.MouseEnter += _onMouseEnter;
            _navPanel.MouseLeave += _onMouseLeave;
        }

        /// <summary>
        /// Stops the collapse timer and unwires hover events.
        /// Call from MainWindow.Closed to prevent a timer Tick after the window is gone.
        /// </summary>
        public void Shutdown()
        {
            _collapseTimer.Stop();
            _navPanel.MouseEnter -= _onMouseEnter;
            _navPanel.MouseLeave -= _onMouseLeave;
        }

        // ── Active button highlighting ───────────────────────────────

        /// <summary>
        /// Highlights the nav button whose Tag matches <paramref name="tag"/>
        /// and dims all others. Also updates label font weight and icon colour.
        /// </summary>
        public void SetActive(string tag)
        {
            ActiveTag = tag;

            var accentBrush = TryFindAccentBrush();
            var inactiveBrush = TryFindInactiveBrush();

            for (int i = 0; i < _navButtons.Length; i++)
            {
                bool isActive = _navButtons[i].Tag?.ToString() == tag;

                // Find ActivePill through template (same pattern as TitleBar UpdateBadge)
                var pill = _navButtons[i].Template?.FindName("ActivePill", _navButtons[i]) as Border;
                if (pill != null)
                    pill.Opacity = isActive ? 1 : 0;

                var iconBrush = isActive ? accentBrush : inactiveBrush;
                _navIcons[i].Foreground = iconBrush;

                if (i < _navLabels.Length && _navLabels[i] != null)
                {
                    _navLabels[i].Foreground = iconBrush;
                    _navLabels[i].FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Regular;
                }
            }
        }

        // ── Slide-out expand / collapse ──────────────────────────────

        /// <summary>
        /// Animates the nav panel Width from 52 → 160 and fades in labels.
        /// </summary>
        public void Expand()
        {
            if (_navPanel == null) return;

            var widthAnim = new DoubleAnimation(160, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            _navPanel.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);

            foreach (var label in _navLabels)
            {
                if (label == null) continue;
                var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                label.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }
        }

        /// <summary>
        /// Animates the nav panel Width from 160 → 52 and fades out labels.
        /// Aborts if the mouse is still over the panel.
        /// </summary>
        public void Collapse()
        {
            if (_navPanel == null) return;
            if (_navPanel.IsMouseOver) return;

            var widthAnim = new DoubleAnimation(52, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            _navPanel.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);

            foreach (var label in _navLabels)
            {
                if (label == null) continue;
                var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                label.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────

        private Brush TryFindAccentBrush()
        {
            return (Brush)(_resourceOwner.TryFindResource("Accent") ?? Brushes.Black);
        }

        private Brush TryFindInactiveBrush()
        {
            return (Brush)(_resourceOwner.TryFindResource("TextSecondary") ?? Brushes.Gray);
        }
    }
}
