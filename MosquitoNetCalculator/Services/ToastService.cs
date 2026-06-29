using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        /// <summary>
        /// Показывает persistent (не auto-disappearing) плашку об обнаруженном
        /// обновлении. Используется фоновой проверкой из <see cref="UpdateService.CheckInBackgroundAsync"/>.
        ///
        /// ─── Зачем отличается от обычного <see cref="ShowToast"/> ───────────
        /// • У обычных toast'ов fixed lifetime (3500 ms по умолчанию) — для
        ///   уведомления «обнаружено обновление» это слишком коротко: пользователь
        ///   может пропустить плашку и забыть.
        /// • Persistent = пока пользователь явно не нажмёт «Обновить» или
        ///   «Позже» — плашка висит. Это согласуется с задачей
        ///   «можно отложить, но не рекомендуется»: пользователь не сможет
        ///   случайно «не заметить» обновление.
        ///
        /// ─── Layout ─────────────────────────────────────────────────────────
        /// ┌─────────────────────────────────────────────────────────────────┐
        /// │ ┃ ⓘ  Доступно обновление                          [ Обновить ]   │ │
        /// │ ┃    Версия 3.37.3 • 5 новых версий. Рекомендуем…  [ Позже ]    │ │
        /// └─────────────────────────────────────────────────────────────────┘
        /// Ширина 400 px (шире обычного 360 — нужно место под 2 кнопки).
        /// </summary>
        public static void ShowUpdateNotification(
            string version,
            int changelogCount,
            Action onUpdate,
            Action onLater)
        {
            if (_toastCanvas == null) return;

            // Detail-text занимает не всю ширину: слева accent-bar (4px) +
            // icon (24px) + margins (12+10) ≈ 50px; 10px запаса на textStack.
            const double UpdateToastWidth = 400;
            const double DetailTextMaxWidthOffset = 60;

            var accentBrush = GetAccentBrush(ToastType.Info);
            var iconChar = GetIconChar(ToastType.Info);

            var accentBar = new Border
            {
                Width = 4,
                CornerRadius = new CornerRadius(2),
                Background = accentBrush,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            var iconBorder = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = accentBrush,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = iconChar,
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                }
            };

            var titleBlock = new TextBlock
            {
                Text = "Доступно обновление",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush?)Application.Current?.FindResource("TextPrimary") ?? Brushes.Black,
                Margin = new Thickness(0, 0, 0, 1),
            };

            var detailBlock = new TextBlock
            {
                FontSize = 11,
                Foreground = (Brush?)Application.Current?.FindResource("TextSecondary") ?? Brushes.DarkSlateGray,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = UpdateToastWidth - DetailTextMaxWidthOffset,
            };
            detailBlock.Text = changelogCount > 0
                ? $"Версия {version} \u2022 {changelogCount} новых версий. Рекомендуем обновиться."
                : $"Версия {version}. Рекомендуем обновиться.";

            var textStack = new StackPanel();
            textStack.Children.Add(titleBlock);
            textStack.Children.Add(detailBlock);

            var headerStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Top,
            };
            headerStack.Children.Add(iconBorder);
            headerStack.Children.Add(textStack);

            var updateBtn = new Button
            {
                Content = "Обновить",
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                MinWidth = 80,
            };
            if (Application.Current?.FindResource("PrimaryButton") is Style primaryStyle)
                updateBtn.Style = primaryStyle;
            else
            {
                updateBtn.Background = accentBrush;
                updateBtn.Foreground = Brushes.White;
                updateBtn.BorderThickness = new Thickness(0);
            }

            var laterBtn = new Button
            {
                Content = "Позже",
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 0, 0),
                FontSize = 12,
                FontWeight = FontWeights.Normal,
                Cursor = Cursors.Hand,
                MinWidth = 70,
            };
            if (Application.Current?.FindResource("GhostButton") is Style ghostStyle)
                laterBtn.Style = ghostStyle;
            else
            {
                laterBtn.Background = (Brush?)Application.Current?.FindResource("GhostBg") ?? Brushes.Transparent;
                laterBtn.Foreground = (Brush?)Application.Current?.FindResource("TextPrimary") ?? Brushes.Black;
                laterBtn.BorderThickness = new Thickness(1);
                laterBtn.BorderBrush = (Brush?)Application.Current?.FindResource("Border") ?? Brushes.Gray;
            }

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0),
            };
            btnPanel.Children.Add(updateBtn);
            btnPanel.Children.Add(laterBtn);

            var contentStack = new StackPanel();
            contentStack.Children.Add(headerStack);
            contentStack.Children.Add(btnPanel);

            var rootPanel = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(accentBar, Dock.Left);
            rootPanel.Children.Add(accentBar);
            rootPanel.Children.Add(contentStack);

            // Явно ставим IsHitTestVisible=true: ToastCanvas в XAML объявлен с
            // IsHitTestVisible=False, и это inheritable DP — кнопки внутри
            // Border'а наследуют False и не получают Click. Override на этом
            // Border восстанавливает реактивность; дальше вниз по дереву
            // наследование работает нормально.
            var toast = new Border
            {
                Child = rootPanel,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                MaxWidth = UpdateToastWidth,
                IsHitTestVisible = true,
            };
            if (Application.Current?.FindResource("ToastBorder") is Style toastStyle)
                toast.Style = toastStyle;

            // Подписываемся на Click ПОСЛЕ полной сборки toast — замыкание
            // захватывает переменную, а не значение, так что к моменту первого
            // Click toast уже валиден. Убираем плашку ДО вызова callback,
            // чтобы новый modal-диалог (если onUpdate запускает CheckAndApplyAsync)
            // перекрывал её сразу, а не после репейнта.
            void CloseAndDispatch(Action body)
            {
                RemoveToast(toast);
                body();
            }
            updateBtn.Click += (_, _) => CloseAndDispatch(onUpdate);
            laterBtn.Click += (_, _) => CloseAndDispatch(onLater);

            // Позиционируем в стопке существующих toast'ов как обычные.
            double existingHeight = 0;
            foreach (UIElement child in _toastCanvas.Children)
            {
                if (child is Border existingToast && !TabIndicatorTag.Equals(existingToast.Tag))
                {
                    existingHeight += existingToast.ActualHeight + ToastSpacing;
                }
            }
            toast.Margin = new Thickness(0, 0, ToastRightMargin, ToastBottomMargin + existingHeight);

            _toastCanvas.Children.Add(toast);
            _activeToasts.Add(toast);

            AnimateToastIn(toast);

            // Умышленно НЕ вызываем ScheduleToastRemoval — плашка persistent,
            // закрывается только по нажатию на «Обновить» или «Позже»
            // (см. CloseAndDispatch выше).
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