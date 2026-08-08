using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Manages navigation highlighting and the explicit expanded/collapsed state
    /// of the left navigation panel. The panel is expanded by default; it never
    /// changes size merely because the mouse passes over it.
    /// </summary>
    public sealed class NavigationService
    {
        private readonly Button[] _navButtons;
        private readonly TextBlock[] _navIcons;
        private readonly TextBlock[] _navLabels;
        private readonly FrameworkElement _navPanel;
        private readonly FrameworkElement _resourceOwner;

        public string ActiveTag { get; private set; } = "Calc";
        public bool IsExpanded { get; private set; } = true;

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
        }

        /// <summary>
        /// Kept as a lifecycle hook for callers that used the old hover-driven
        /// implementation. There are no timers or event subscriptions anymore.
        /// </summary>
        public void Shutdown()
        {
            _navPanel.BeginAnimation(FrameworkElement.WidthProperty, null);
            foreach (var label in _navLabels)
                label?.BeginAnimation(UIElement.OpacityProperty, null);
        }

        public void SetActive(string tag)
        {
            ActiveTag = tag;

            var accentBrush = TryFindAccentBrush();
            var inactiveBrush = TryFindInactiveBrush();

            for (int i = 0; i < _navButtons.Length; i++)
            {
                bool isActive = _navButtons[i].Tag?.ToString() == tag;
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

        /// <summary>Switches between the expanded and compact states.</summary>
        public void Toggle()
        {
            if (IsExpanded) Collapse();
            else Expand();
        }

        /// <summary>Expands the panel and shows navigation labels.</summary>
        public void Expand()
        {
            IsExpanded = true;
            AnimatePanel(160, 1.0, 250, EasingMode.EaseOut);
        }

        /// <summary>Collapses the panel to icons only.</summary>
        public void Collapse()
        {
            IsExpanded = false;
            AnimatePanel(52, 0.0, 200, EasingMode.EaseIn);
        }

        private void AnimatePanel(double width, double labelOpacity, int durationMs, EasingMode easingMode)
        {
            if (_navPanel == null) return;

            var widthAnim = new DoubleAnimation(width, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = new CubicEase { EasingMode = easingMode }
            };
            _navPanel.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);

            foreach (var label in _navLabels)
            {
                if (label == null) continue;
                var fade = new DoubleAnimation(labelOpacity, TimeSpan.FromMilliseconds(durationMs - 50))
                {
                    EasingFunction = new CubicEase { EasingMode = easingMode }
                };
                label.BeginAnimation(UIElement.OpacityProperty, fade);
            }
        }

        private Brush TryFindAccentBrush()
            => (Brush)(_resourceOwner.TryFindResource("Accent") ?? Brushes.Black);

        private Brush TryFindInactiveBrush()
            => (Brush)(_resourceOwner.TryFindResource("TextSecondary") ?? Brushes.Gray);
    }
}
