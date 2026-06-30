using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MosquitoNetCalculator
{
    public partial class MainWindow
    {
        private void AnimateCardsOnLoad()
        {
            var cards = new (FrameworkElement element, double delay)[] {
                (Sidebar.CardClientBorder, 0),
                (Sidebar.CardContractBorder, 60),
                (Sidebar.CardNotesBorder, 120),
                (SidebarControl, 180),
                (ActionBarControl.CardBarBorder, 300),
                (QuickAddControl.CardQuickAddBorder, 360),
                (OrderItemsControl.CardTableBorder, 420),
                (TotalCardControl.CardTotalBorder, 480),
            };

            // Pre-roll: pin all 8 cards to Opacity=0 BEFORE the Storyboard
            // schedules its timers. Without this pre-roll the cards remain at
            // their XAML default Opacity=1 for one or more frames after Loaded
            // completes, then snap to From=0.0 on the very next animation
            // tick — a visible flash on the 0ms-delay card. Setting Opacity=0
            // synchronously below the dispatch-Loaded priority makes the
            // Storyboard.From=0 transition a no-op (already at 0) and the
            // animation visibly fades them in.
            foreach (var (element, _) in cards)
                if (element != null) element.Opacity = 0;

            var storyboard = new Storyboard();

            foreach (var (element, delay) in cards)
            {
                if (element == null) continue;

                var animation = new DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    BeginTime = TimeSpan.FromMilliseconds(delay),
                    Duration = TimeSpan.FromMilliseconds(400),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                Storyboard.SetTarget(animation, element);
                Storyboard.SetTargetProperty(animation, new PropertyPath(FrameworkElement.OpacityProperty));
                storyboard.Children.Add(animation);
            }

            storyboard.Begin(this);
        }

        private void OnThemeChanged()
        {
            if (TitleBarControl?.TitleBarBorder == null) return;

            // No explicit SetResourceReference needed: TitleBarControl.xaml already
            // declares Background="{DynamicResource Surface}" on the TitleBar border,
            // and ThemeService.ApplyTheme either animates the existing brush's Color
            // in place (preserving the DynamicResource binding) or replaces the
            // resource with a fresh brush (WPF's DP propagation automatically
            // re-wires every DynamicResource consumer in the visual tree).
            // InvalidateVisual stays as a defensive safety net for any custom
            // visual that might miss the Freezable invalidation cascade.
            TitleBarControl.TitleBarBorder.InvalidateVisual();
        }
    }
}
