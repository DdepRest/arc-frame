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

    /// <summary>
    /// Static toast service with support for multiple short-lived canvases
    /// keyed by a "scope" id. Default scope is "Main" for backwards compat with
    /// every existing non-scoped call site. Other callers register their own
    /// canvas (e.g. "AI" for the AI assistant) so toasts anchor near the
    /// relevant control instead of floating in the corner of the main window.
    /// </summary>
    public static class ToastService
    {
        /// <summary>Default scope id for toasts anchored to MainWindow's full-window ToastCanvas.</summary>
        public const string MainScope = "Main";
        /// <summary>Scope id reserved for AI-anchored toasts (in-panel control or docked window).</summary>
        public const string AiScope = "AI";

        private const double ToastBottomMargin = 16;
        private const double ToastRightMargin = 16;
        // AI scope uses tighter padding so toasts don’t overlap the panel’s close
        // button or chrome. The AI panel sits inside a Margin 8,8,0,8 Border so
        // bumping the visible toast outward by 8 px keeps it visually flush.
        private const double AiToastBottomMargin = 8;
        private const double AiToastRightMargin = 8;
        private const double ToastSpacing = 8;
        private const double ToastMaxWidth = 360;
        public const string TabIndicatorTag = "TabIndicator";

        // Panels keyed by scope id. Multiple scopes coexist; same scope replaces the canvas.
        private static readonly Dictionary<string, Panel> _canvases = new();
        // Active toasts per scope (kept as list so RepositionToasts can iterate in stack order).
        private static readonly Dictionary<string, List<Border>> _activeToastsByScope = new();
        // Reverse-lookup so RemoveToast(Border) knows which scope it belongs to.
        private static readonly Dictionary<Border, string> _toastScopeMap = new();
        // Per-toast DispatcherTimer so we can stop it explicitly on RemoveToast
        // and on UnregisterCanvas (otherwise orphaned timers keep ticking until
        // their durationMs elapses, wasting dispatcher cycles on dead scopes).
        private static readonly Dictionary<Border, System.Windows.Threading.DispatcherTimer> _toastTimers = new();

        /// <summary>
        /// Backwards-compatible single-canvas initialization. Registers the
        /// supplied canvas under the "Main" scope. Existing call sites that
        /// pass the global <c>ToastCanvas</c> in MainWindow.xaml continue to work.
        /// </summary>
        public static void Initialize(Grid toastCanvas)
            => RegisterCanvas(MainScope, toastCanvas);

        /// <summary>
        /// Registers (or replaces) the canvas for the given scope. MainWindow
        /// registers "Main" once at startup; the AI handler registers "AI"
        /// each time the user toggles AI mode (in-panel or docked) and
        /// unregisters it on close.
        ///
        /// If the scope is already registered with a DIFFERENT canvas, the
        /// prior mapping is drained first so the previous canvas's already-
        /// rendered toasts don't become ghost visuals attached to a scope
        /// that no longer points at them. The caller path that always
        /// unregisters first (RefreshAiMode) is unaffected because
        /// UnregisterCanvas wipes the dictionary entry — TryGetValue
        /// returns false here and the drain branch is skipped.
        /// </summary>
        public static void RegisterCanvas(string scope, Panel canvas)
        {
            if (scope == null || canvas == null) return;

            if (_canvases.TryGetValue(scope, out var existing) && existing != null
                && !ReferenceEquals(existing, canvas))
            {
                UnregisterCanvas(scope);
            }

            _canvases[scope] = canvas;
            if (!_activeToastsByScope.ContainsKey(scope))
                _activeToastsByScope[scope] = new List<Border>();
        }

        /// <summary>
        /// Unregisters the canvas for the given scope, removing any active
        /// toasts that were anchored to it. Safe to call when the scope was
        /// never registered.
        /// </summary>
        public static void UnregisterCanvas(string scope)
        {
            if (scope == null) return;
            if (_activeToastsByScope.TryGetValue(scope, out var toasts))
            {
                // Stop every active timer for this scope BEFORE we drop the
                // canvas — otherwise the timers keep ticking on a scope that
                // has no canvas mapping, wasting dispatcher cycles and trying
                // to animate Borders no longer in the visual tree.
                foreach (var toast in toasts)
                {
                    if (_toastTimers.TryGetValue(toast, out var t))
                    {
                        t.Stop();
                        _toastTimers.Remove(toast);
                    }
                }
                if (_canvases.TryGetValue(scope, out var canvas) && canvas != null)
                {
                    foreach (var t in toasts)
                        canvas.Children.Remove(t);
                }
                toasts.Clear();
            }
            _canvases.Remove(scope);
            _activeToastsByScope.Remove(scope);
            // Drop scope mapping for any orphans (defensive)
            var orphans = new List<Border>();
            foreach (var kv in _toastScopeMap)
                if (kv.Value == scope) orphans.Add(kv.Key);
            foreach (var b in orphans) _toastScopeMap.Remove(b);
        }

        public static void ShowToast(string message, ToastType type = ToastType.Info, int durationMs = 3500)
            => ShowToast(MainScope, message, type, durationMs);

        /// <summary>
        /// Shows a short-lived toast on the canvas registered for the given scope.
        /// Silently no-ops if the scope has no registered canvas.
        /// </summary>
        public static void ShowToast(string scope, string message, ToastType type = ToastType.Info, int durationMs = 3500)
        {
            if (!_canvases.TryGetValue(scope, out var canvas) || canvas == null) return;

            var toast = BuildSimpleToast(message, type);
            PositionToast(toast, canvas, scope);
            canvas.Children.Add(toast);
            TrackToast(toast, scope);
            ScheduleToastRemoval(toast, durationMs);
            AnimateToastIn(toast);
        }

        /// <summary>Builds the simple Border used by <see cref="ShowToast(string, string, ToastType, int)"/>. Extracted so scope- and persistent-toast code can share layout.</summary>
        private static Border BuildSimpleToast(string message, ToastType type)
        {
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

            var rootPanel = new DockPanel { LastChildFill = true };
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
            if (Application.Current?.FindResource("ToastBorder") is Style toastStyle)
            {
                toast.Style = toastStyle;
            }
            return toast;
        }

        /// <summary>
        /// Computes the bottom-margin for a new toast so it stacks above any
        /// existing toasts in the same canvas. Trims the bottom-most toast
        /// if overflow is imminent. Shared by ShowToast and ShowUpdateNotification.
        /// </summary>
        private static void PositionToast(Border toast, Panel canvas, string scope)
        {
            double existingHeight = 0;
            double canvasHeight = canvas.ActualHeight > 0 ? canvas.ActualHeight : 800;
            foreach (UIElement child in canvas.Children)
            {
                if (child is Border existingToast && !TabIndicatorTag.Equals(existingToast.Tag))
                {
                    existingHeight += existingToast.ActualHeight + ToastSpacing;
                }
            }

            if (BottomMarginFor(scope) + existingHeight + 60 > canvasHeight && canvas.Children.Count > 0)
            {
                int removeIdx = 0;
                while (removeIdx < canvas.Children.Count && canvas.Children[removeIdx] is Border tb && TabIndicatorTag.Equals(tb.Tag)) removeIdx++;
                if (removeIdx < canvas.Children.Count) canvas.Children.RemoveAt(removeIdx);
                existingHeight = 0;
                foreach (UIElement child in canvas.Children)
                {
                    if (child is Border existingToast && !TabIndicatorTag.Equals(existingToast.Tag))
                    {
                        existingHeight += existingToast.ActualHeight + ToastSpacing;
                    }
                }
            }
            toast.Margin = new Thickness(0, 0, RightMarginFor(scope), BottomMarginFor(scope) + existingHeight);
        }

        // Per-scope margin pickers so the AI panel/window doesn’t tuck toasts
        // too close to its chrome (close button, edge). Main keeps the historical 16.
        private static double BottomMarginFor(string scope) => scope == AiScope ? AiToastBottomMargin : ToastBottomMargin;
        private static double RightMarginFor(string scope) => scope == AiScope ? AiToastRightMargin : ToastRightMargin;

        private static void TrackToast(Border toast, string scope)
        {
            if (_activeToastsByScope.TryGetValue(scope, out var list))
                list.Add(toast);
            _toastScopeMap[toast] = scope;
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
            if (!_canvases.TryGetValue(MainScope, out var canvas) || canvas == null) return;

            // NOTE: persistent toast — no DispatcherTimer is scheduled here.
            // Cleanup works because UnregisterCanvas iterates
            // _activeToastsByScope[scope] and removes the toasts from
            // canvas.Children directly; the timer-stop loop in UnregisterCanvas
            // is a no-op for persistent toasts. Any future change that
            // schedules a timer for the persistent toast must also revisit
            // this contract.
            var toast = BuildUpdateToast(version, changelogCount, out var updateBtn, out var laterBtn);

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

            PositionToast(toast, canvas, MainScope);
            canvas.Children.Add(toast);
            TrackToast(toast, MainScope);
            AnimateToastIn(toast);
            // Persistent — нет авто-удаления.
        }

        /// <summary>
        /// Builds the larger persistent update-notification toast (header + details + 2 action buttons).
        /// Returns the Border and the two Button references so the caller can wire Click handlers
        /// AFTER the toast is fully constructed.
        /// </summary>
        private static Border BuildUpdateToast(string version, int changelogCount, out Button updateBtn, out Button laterBtn)
        {
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

            updateBtn = new Button
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

            laterBtn = new Button
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

            var toast = new Border
            {
                Child = rootPanel,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                MaxWidth = UpdateToastWidth,
            };
            if (Application.Current?.FindResource("ToastBorder") is Style toastStyle)
                toast.Style = toastStyle;
            return toast;
        }

        public static void RepositionToasts() => RepositionToasts(MainScope);

        /// <summary>
        /// Re-marginates all active toasts in the given scope so the bottom-most
        /// toast sits at the per-scope bottom margin and the stack grows upward.
        /// Unaffected canvases stay put so unrelated toasts (e.g. Main while AI
        /// canvas moves) keep their current layout.
        /// </summary>
        public static void RepositionToasts(string scope)
        {
            if (!_canvases.TryGetValue(scope, out var canvas) || canvas == null) return;

            double right = RightMarginFor(scope);
            double currentBottom = BottomMarginFor(scope);
            for (int i = canvas.Children.Count - 1; i >= 0; i--)
            {
                if (canvas.Children[i] is Border toast && !TabIndicatorTag.Equals(toast.Tag))
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
                        To = new Thickness(0, 0, right, currentBottom),
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
                _toastTimers.Remove(toast);
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
            _toastTimers[toast] = timer;
            timer.Start();
        }

        private static void RemoveToast(Border toast)
        {
            // Reverse-lookup the scope; if missing the canvas was unregistered
            // (race between ScheduleTimer and UnregisterCanvas), the toast is an
            // orphan — leave it alone rather than guessing a fallback scope.
            if (!_toastScopeMap.TryGetValue(toast, out var scope))
            {
                System.Diagnostics.Debug.WriteLine(
                    "[ToastService] RemoveToast called for orphan toast (no scope map entry).");
                return;
            }

            // Stop the auto-removal timer if it’s still running so it can’t
            // resurrect or re-animate this toast after we’ve torn it down.
            if (_toastTimers.TryGetValue(toast, out var t))
            {
                t.Stop();
                _toastTimers.Remove(toast);
            }

            if (_canvases.TryGetValue(scope, out var canvas) && canvas != null)
                canvas.Children.Remove(toast);

            if (_activeToastsByScope.TryGetValue(scope, out var list))
                list.Remove(toast);
            _toastScopeMap.Remove(toast);

            RepositionToasts(scope);
        }
    }
}