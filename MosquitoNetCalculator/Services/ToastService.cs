using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MosquitoNetCalculator.Services
{
    public enum ToastType { Success, Error, Info, Warning }

    public static class ToastService
    {
        private static Grid? _toastCanvas;
        private static readonly List<Border> _activeToasts = new();

        private const double ToastBottomMargin = 16;
        private const double ToastRightMargin = 16;
        private const double ToastSpacing = 8;
        private const double ToastMaxWidth = 360;
        public const string TabIndicatorTag = "TabIndicator";



        public static void Initialize(Grid toastCanvas)
        {
            _toastCanvas = toastCanvas;
        }

        public static void ShowToast(string message, ToastType type = ToastType.Info, int durationMs = 3500)
        {
            if (_toastCanvas == null) return;

            var accentBrush = GetAccentBrush(type);
            var iconChar = GetIconChar(type);

            var accentBar = new Border
            {
                Width = 4,
                CornerRadius = new CornerRadius(2),
                Background = accentBrush,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var iconBorder = new Border
            {
                Width = 24, Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = accentBrush,
                Child = new TextBlock
                {
                    Text = iconChar,
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var textBlock = new TextBlock
            {
                Text = message,
                FontSize = 12.5,
                Foreground = (Brush?)Application.Current?.FindResource("TextPrimary") ?? Brushes.Black,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 300,
                TextWrapping = TextWrapping.Wrap
            };

            var contentPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            contentPanel.Children.Add(iconBorder);
            contentPanel.Children.Add(textBlock);

            var rootPanel = new DockPanel
            {
                LastChildFill = true
            };
            DockPanel.SetDock(accentBar, Dock.Left);
            rootPanel.Children.Add(accentBar);
            rootPanel.Children.Add(contentPanel);

            var toast = new Border
            {
                Child = rootPanel,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                MaxWidth = ToastMaxWidth
            };
            // Apply the ToastBorder style only if it's actually defined — an empty
            // Style() would render the toast as naked text with no background.
            if (Application.Current?.FindResource("ToastBorder") is Style toastStyle)
            {
                toast.Style = toastStyle;
            }

            double existingHeight = 0;
            double canvasHeight = _toastCanvas.ActualHeight > 0 ? _toastCanvas.ActualHeight : 800;
            foreach (UIElement child in _toastCanvas.Children)
            {
                if (child is Border existingToast && !TabIndicatorTag.Equals(existingToast.Tag))
                {
                    existingHeight += existingToast.ActualHeight + ToastSpacing;
                }
            }

            if (ToastBottomMargin + existingHeight + 60 > canvasHeight && _toastCanvas.Children.Count > 0)
            {
                int removeIdx = 0;
                while (removeIdx < _toastCanvas.Children.Count && _toastCanvas.Children[removeIdx] is Border tb && TabIndicatorTag.Equals(tb.Tag)) removeIdx++;
                if (removeIdx < _toastCanvas.Children.Count) _toastCanvas.Children.RemoveAt(removeIdx);
                existingHeight = 0;
                foreach (UIElement child in _toastCanvas.Children)
                {
                    if (child is Border existingToast && !TabIndicatorTag.Equals(existingToast.Tag))
                    {
                        existingHeight += existingToast.ActualHeight + ToastSpacing;
                    }
                }
            }
            toast.Margin = new Thickness(0, 0, ToastRightMargin, ToastBottomMargin + existingHeight);

            _toastCanvas.Children.Add(toast);
            _activeToasts.Add(toast);

            AnimateToastIn(toast);
            ScheduleToastRemoval(toast, durationMs);
        }

        public static void RepositionToasts()
        {
            if (_toastCanvas == null) return;

            double currentBottom = ToastBottomMargin;
            for (int i = _toastCanvas.Children.Count - 1; i >= 0; i--)
            {
                if (_toastCanvas.Children[i] is Border toast && !TabIndicatorTag.Equals(toast.Tag))
                {
                    double h = toast.ActualHeight;
                    if (h <= 0)
                    {
                        toast.Measure(new Size(ToastMaxWidth, double.PositiveInfinity));
                        h = toast.DesiredSize.Height;
                    }

                    var anim = new ThicknessAnimation
                    {
                        From = toast.Margin,
                        To = new Thickness(0, 0, ToastRightMargin, currentBottom),
                        Duration = TimeSpan.FromMilliseconds(200),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    toast.BeginAnimation(Border.MarginProperty, anim);

                    currentBottom += h + ToastSpacing;
                }
            }
        }

        private static Brush GetAccentBrush(ToastType type)
        {
            return type switch
            {
                ToastType.Success => (Brush?)Application.Current?.FindResource("Success") ?? Brushes.Green,
                ToastType.Error => (Brush?)Application.Current?.FindResource("Danger") ?? Brushes.Red,
                ToastType.Warning => (Brush?)Application.Current?.FindResource("Warning") ?? Brushes.Orange,
                _ => (Brush?)Application.Current?.FindResource("Accent") ?? Brushes.SteelBlue
            };
        }

        private static string GetIconChar(ToastType type)
        {
            return type switch
            {
                ToastType.Success => "\u2713",
                ToastType.Error => "\u2717",
                ToastType.Warning => "\u26A0",
                _ => "\u2139"
            };
        }

        private static void AnimateToastIn(Border toast)
        {
            var fadeIn = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            toast.BeginAnimation(Border.OpacityProperty, fadeIn);
        }

        private static void ScheduleToastRemoval(Border toast, int durationMs)
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(durationMs)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var anim = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(250),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                anim.Completed += (s2, e2) => RemoveToast(toast);
                toast.BeginAnimation(Border.OpacityProperty, anim);
            };
            timer.Start();
        }

        private static void RemoveToast(Border toast)
        {
            if (_toastCanvas == null) return;
            _toastCanvas.Children.Remove(toast);
            _activeToasts.Remove(toast);
            RepositionToasts();
        }
    }
}