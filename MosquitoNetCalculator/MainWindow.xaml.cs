using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Helpers;
using MosquitoNetCalculator.ViewModels;

namespace MosquitoNetCalculator
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public MainWindowViewModel ViewModel { get; } = new();

        public PrintService PrintService => ViewModel.PrintService;
        public PricesViewModel PricesVM => ViewModel.PricesVM;
        public CalculationViewModel CalcVM => ViewModel.CalcVM;
        public OrdersHistoryViewModel OrdersVM => ViewModel.OrdersVM;
        public UndoRedoService UndoRedo => ViewModel.UndoRedo;

        public ObservableCollection<OrderItem> OrderItems => ViewModel.OrderItems;
        public ObservableCollection<PriceItem> Prices => ViewModel.Prices;
        public ClientInfo ClientInfo => ViewModel.ClientInfo;
        public ObservableCollection<UpdateItem> Updates => ViewModel.Updates;
        public SidebarControl Sidebar { get; private set; }

        public List<string> ProductNames => ViewModel.ProductNames;

        public string CurrentOrderId { get => ViewModel.CurrentOrderId; set => ViewModel.CurrentOrderId = value; }
        public bool IsNewOrder { get => ViewModel.IsNewOrder; set => ViewModel.IsNewOrder = value; }
        public bool SuppressPrefixSave { get => ViewModel.SuppressPrefixSave; set => ViewModel.SuppressPrefixSave = value; }

        private bool _columnRecalcPending;
        private bool _initialLoadDone;
        internal bool _suppressContractNumberUpdate;
        public new bool IsInitialized => _initialLoadDone;

        private static DateTime? _lastSumEditTime;
        private static readonly TimeSpan SmartFocusWindow = TimeSpan.FromMinutes(5);

        // Cached at construction — used by UpdateBaseTitle when the user re-opens
        // the WelcomeWindow dialog to change their location.
        private System.Version? _appVersion;

        // Cached location portion of the title. Holds the base text WITHOUT the
        // trailing dirty marker so toggling dirty on every edit doesn't have to
        // re-read settings or re-interpolate the version string. Refreshed by
        // UpdateBaseTitle() only when the user actually changes their location
        // (or on first construction). Read by ApplyTitle().
        private string _cachedTitleLocation = string.Empty;

        private System.Windows.Threading.DispatcherTimer? _updateTotalDebounceTimer;
        private System.Windows.Threading.DispatcherTimer? _navBadgeTimer;
        private Action? _onNavToCalc;
        private string _activeNavTag = "Calc";

        // ─── Idle/periodic update check scheduler ───────────────────────────
        // Запускается в Loaded после startup-проверки. Триггерит CheckInBackgroundAsync
        // каждые CheckInterval (30 мин) или после IdleThreshold (10 мин) простоя.
        // Activity-tracker: PreviewMouseMove + PreviewKeyDown сбрасывают idle.
        private UpdateCheckScheduler? _updateCheckScheduler;

        public MainWindow()
        {
            InitializeComponent();

            // ── Progress bar animator (v3.40.3 testability refactor) ──
            // Extracted the body of OnUpdateProgressChanged into a helper so
            // unit tests can exercise the try/catch + TryFindResource fallback
            // logic without spinning up the full MainWindow tree.
            // The Func delegates decouple the animator from UpdateService
            // entirely — tests pass simple lambdas to drive its state.
            _progressAnimator = new ProgressBarUpdateAnimator(
                this, UpdateDownloadBar,
                () => UpdateService.DownloadProgress,
                () => UpdateService.IsDownloading);

            ToastService.Initialize(ToastCanvas);
            ViewModel.UndoRedo.SetDirtyCallback(UpdateDirtyIndicator);

            UpdateService.ProgressChanged += OnUpdateProgressChanged;

            _appVersion = UpdateService.CurrentVersion;
            UpdateBaseTitle();

            // Theme is now driven entirely from the settings-menu radio items in
            // ActionBarControl. Only OnThemeChanged stays wired here so the
            // chrome (title-bar) repaints correctly on every theme flip
            // regardless of trigger source.
            ThemeService.ThemeChanged += OnThemeChanged;

            var chrome = WindowChrome.GetWindowChrome(this);
            chrome.CaptionHeight = 32;
            TitleBarControl.TitleBarBorder.Height = 32;

            void FixTitleBarButton(Button btn, string fontIcon, double fontSize)
            {
                btn.Width = btn.Height = 32;
                if (btn.Content is TextBlock tb)
                {
                    tb.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
                    tb.FontSize = fontSize;
                    tb.Text = fontIcon;
                }
            }
            FixTitleBarButton(TitleBarControl.BtnMin, "\uE738", 12);
            FixTitleBarButton(TitleBarControl.BtnMax, WindowState == WindowState.Maximized ? "\uE923" : "\uE739", 12);
            FixTitleBarButton(TitleBarControl.BtnCls, "\uE8BB", 12);
            TitleBarControl.BtnSettingsGear.Width = TitleBarControl.BtnSettingsGear.Height = 32;

            DataContext = this;

            Sidebar = SidebarControl;

            ClientInfo.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ClientInfo.HasAdditionalKp)
                    || e.PropertyName == nameof(ClientInfo.AdditionalKpsTotal))
                    UpdateTotal();
                MarkDirty();
            };
            ClientInfo.AdditionalKps.CollectionChanged += (s, e) => { UpdateTotal(); MarkDirty(); };

            Sidebar.CmbOrderStatus.ItemsSource = OrderStatuses.All;
            Sidebar.CmbOrderStatus.SelectedItem = OrderStatuses.All[0];


            LoadPrices();
            StartNewOrder();
            RefreshComboBoxColumns();



            OrderItems.CollectionChanged += (s, e) =>
            {
                UpdateEmptyState();
                RecalculateOrderGridColumnWidths();
                MarkDirty();
            };

            PreviewKeyDown += (_, args) =>
            {
                if (Keyboard.Modifiers == ModifierKeys.Control && args.Key == Key.Z) { Undo(); args.Handled = true; }
                else if (Keyboard.Modifiers == ModifierKeys.Control && args.Key == Key.Y) { Redo(); args.Handled = true; }
            };

            // NavigationView: focus QuickAdd when switching back to Calc view
            void OnNavToCalc()
            {
                if (!_initialLoadDone) return;
                Dispatcher.BeginInvoke(() =>
                {
                    if (QuickAddControl.CmbType != null && QuickAddControl.CmbType.IsEnabled)
                        QuickAddControl.CmbType.Focus();
                });
            }
            _onNavToCalc = OnNavToCalc;

            OrderItemsControl.Grid.BeginningEdit += (_, e) =>
            {
                if (e.Row.DataContext is OrderItem item)
                {
                    string header = e.Column.Header?.ToString() ?? "";
                    // Manual-piece products (Брус, Пояс, Работа, Доставка) have
                    // Цвет/Ширина/Высота blocked because those fields don't
                    // enter the Total formula. WidthOnly products (Откос материал)
                    // additionally record Width as a per-row spec but ignore it
                    // in Total — Width alone stays editable for those.
                    if (item.IsAmountOnly)
                    {
                        if (header is "Цвет" or "Ширина" or "Высота" or "Кол-во")
                        {
                            e.Cancel = true;
                            return;
                        }
                    }
                    else if (item.IsWidthOnly)
                    {
                        if (header is "Цвет" or "Высота")
                        {
                            e.Cancel = true;
                            return;
                        }
                    }
                    else if (item.IsManualPiece)
                    {
                        if (header is "Цвет" or "Ширина" or "Высота")
                        {
                            e.Cancel = true;
                            return;
                        }
                    }
                }
                PushUndo();
            };

            OrderItemsControl.Grid.PreviewMouseLeftButtonDown += Grid_PreviewMouseLeftButtonDown;
            OrderItemsControl.Grid.LoadingRow += (_, e) => AttachRowHover(e.Row, true);
            OrderItemsControl.Grid.UnloadingRow += (_, e) => AttachRowHover(e.Row, false);
            OrdersHistoryControl.OrdersGrid.LoadingRow += (_, e) => AttachRowHover(e.Row, true);
            OrdersHistoryControl.OrdersGrid.UnloadingRow += (_, e) => AttachRowHover(e.Row, false);
            PricesControl.PriceDataGrid.LoadingRow += (_, e) => AttachRowHover(e.Row, true);
            PricesControl.PriceDataGrid.UnloadingRow += (_, e) => AttachRowHover(e.Row, false);

            Loaded += MainWindow_Loaded;
            SourceInitialized += (_, _) =>
            {
                var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                source?.AddHook(WndProc);
            };

            OrdersHistoryControl.OrdersGrid.MouseDoubleClick += OrdersList_MouseDoubleClick;
            RefreshStatusSubMenu();
            OrdersHistoryControl.OrdersGrid.SelectionChanged += (_, _) =>
            {
                var hasSelection = OrdersHistoryControl.OrdersGrid.SelectedItem is OrderData;
                if (OrdersHistoryControl.CtxOpenMenu != null)
                    OrdersHistoryControl.CtxOpenMenu.IsEnabled = hasSelection;
                if (OrdersHistoryControl.CtxStatusMenu != null)
                    OrdersHistoryControl.CtxStatusMenu.IsEnabled = hasSelection;
                if (OrdersHistoryControl.CtxCopyMenu != null)
                    OrdersHistoryControl.CtxCopyMenu.IsEnabled = hasSelection;
                if (OrdersHistoryControl.CtxExportMenu != null)
                    OrdersHistoryControl.CtxExportMenu.IsEnabled = hasSelection;
                if (OrdersHistoryControl.CtxDeleteMenu != null)
                    OrdersHistoryControl.CtxDeleteMenu.IsEnabled = hasSelection;
            };

            UpdateEmptyState();

            PricesControl.PriceDataGrid.PreparingCellForEdit += (s, e) =>
            {
                if (e.EditingElement is TextBox tb)
                    tb.Dispatcher.BeginInvoke(() => tb.SelectAll());
            };
            PricesControl.PriceDataGrid.BeginningEdit += (_, e) => PushUndo();

            OrderItemsControl.Grid.PreparingCellForEdit += (s, e) =>
            {
                if (e.EditingElement is TextBox tb)
                    tb.Dispatcher.BeginInvoke(() => tb.SelectAll());
            };

            SizeChanged += (s, e) =>
            {
                ToastService.RepositionToasts();
            };

            Closed += (_, _) =>
            {
                UpdateService.ProgressChanged -= OnUpdateProgressChanged;
                _updateCheckScheduler?.Stop();
                _navBadgeTimer?.Stop();
            };

            StateChanged += (s, e) =>
            {
                if (TitleBarControl.MaxIcon != null)
                    TitleBarControl.MaxIcon.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE739";
            };

            // NavigationView badge refresh timer
            StartNavBadgeTimer();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshOrdersList();
            RecalculatePriceGridColumnWidths();
            _initialLoadDone = true;

            // ── Запуск фонового планировщика проверок обновлений ────────
            // Создаём здесь (а не в ctor) — нам нужен реальный Window owner
            // и Activity-tracker на PreviewMouseMove/PreviewKeyDown уже подписан.
            // Startup-проверка уже отработала в App.CheckOnStartupAsync — поэтому
            // Start() сбрасывает _lastCheckTime = Now, и первая фоновая проверка
            // случится не раньше 10 мин idle / 30 мин интервала.
            _updateCheckScheduler = new UpdateCheckScheduler
            {
                ShouldSkipCheck = () => UpdateService.IsChecking || UpdateService.IsDownloading,
                OnCheckDue = () => UpdateService.CheckInBackgroundAsync(),
                GetSystemIdleTime = () => UpdateService.GetIdleTime(),
            };
            _updateCheckScheduler.Start();

            // ── Стартовый баннер для известных сломанных версий (v3.40.2) ──
            // Если по какой-то причине CurrentVersion оказалась в диапазоне
            // [3.40.0, 3.40.2) — у пользователя проблемная сборка автообновления.
            // Показываем toast-предупреждение с прямой ссылкой на GitHub Releases,
            // чтобы пользователь мог обновиться вручную. Это no-op для
            // нормальных версий (CurrentVersion >= 3.40.2).
            if (UpdateService.IsCurrentVersionBrokenForAutoUpdate(UpdateService.CurrentVersion))
            {
                string v = UpdateService.CurrentVersion.ToString();
                string url = "https://github.com/DdepRest/arc-frame/releases/latest";
                ToastService.ShowToast(
                    $"Версия {v} имеет известную проблему автообновления. Обновите вручную: {url}",
                    ToastType.Warning,
                    durationMs: 15000);
            }

            // Defer the entrance cascade animation until AFTER the initial
            // layout/render pass has stabilized. Dispatching into the Loaded
            // priority queue keeps Storyboard.Begin + 9 simultaneous timer
            // subscriptions from competing with the very first paint — on
            // weaker GPUs / VMs the inline call drops the first frame at
            // startup. Schedule it, don't run it inline.
            Dispatcher.BeginInvoke(new Action(AnimateCardsOnLoad),
                System.Windows.Threading.DispatcherPriority.Loaded);

            // Refresh nav badges after orders are loaded
            RefreshNavBadges();

            // Set initial active nav button (templates are now applied)
            SetActiveNavButton("Calc");
        }

        internal void UpdateEmptyState()
        {
            if (OrderItemsControl?.Empty == null || OrderItemsControl?.Grid == null) return;
            OrderItemsControl.Empty.Visibility = OrderItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ═══════════════════════════════════════════════════════════════
        // NAVIGATION VIEW
        // ═══════════════════════════════════════════════════════════════

        internal void NavigateToCalculation()
        {
            CloseAllOverlays();
            SetActiveNavButton("Calc");
            _onNavToCalc?.Invoke();
        }

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tag) return;

            switch (tag)
            {
                case "Calc":
                    CloseAllOverlays();
                    SetActiveNavButton("Calc");
                    _onNavToCalc?.Invoke();
                    break;
                case "Orders":
                    if (OrdersOverlay.Visibility == Visibility.Visible)
                        CloseAllOverlays();
                    else
                        ShowOverlay(OrdersOverlay, OrdersPanel, OrdersBackdrop, OrdersSlideTransform);
                    break;
                case "Prices":
                    if (PricesOverlay.Visibility == Visibility.Visible)
                        CloseAllOverlays();
                    else
                        ShowOverlay(PricesOverlay, PricesPanel, PricesBackdrop, PricesSlideTransform);
                    break;
                case "Updates":
                    if (UpdatesOverlay.Visibility == Visibility.Visible)
                        CloseAllOverlays();
                    else
                        ShowOverlay(UpdatesOverlay, UpdatesPanel, UpdatesBackdrop, UpdatesSlideTransform);
                    break;
            }
        }

        // Keyboard shortcut handlers (Ctrl+1..4)
        private void NavCalculation_Click(object s, ExecutedRoutedEventArgs e) { NavigateToCalculation(); }
        private void NavOrders_Click(object s, ExecutedRoutedEventArgs e)     { NavButton_Click(NavBtnOrders, new RoutedEventArgs()); }
        private void NavPrices_Click(object s, ExecutedRoutedEventArgs e)     { NavButton_Click(NavBtnPrices, new RoutedEventArgs()); }
        private void NavUpdates_Click(object s, ExecutedRoutedEventArgs e)    { NavButton_Click(NavBtnUpdates, new RoutedEventArgs()); }

        private void SetActiveNavButton(string tag)
        {
            _activeNavTag = tag;
            var allButtons = new[] { NavBtnCalc, NavBtnOrders, NavBtnPrices, NavBtnUpdates };
            var allIcons = new[] { NavIconCalc, NavIconOrders, NavIconPrices, NavIconUpdates };

            for (int i = 0; i < allButtons.Length; i++)
            {
                bool isActive = allButtons[i].Tag?.ToString() == tag;

                // Find ActivePill through template (same pattern as TitleBar UpdateBadge)
                var pill = allButtons[i].Template?.FindName("ActivePill", allButtons[i]) as Border;
                if (pill != null)
                    pill.Opacity = isActive ? 1 : 0;

                allIcons[i].Foreground = isActive
                    ? (Brush)(TryFindResource("Accent") ?? Brushes.Black)
                    : (Brush)(TryFindResource("TextMuted") ?? Brushes.Gray);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // OVERLAY PANELS
        // ═══════════════════════════════════════════════════════════════

        private void ShowOverlay(Grid overlay, Border panel, Border backdrop, TranslateTransform slideTransform)
        {
            // Close any other open overlay first
            foreach (var ov in new[] { OrdersOverlay, PricesOverlay, UpdatesOverlay })
            {
                if (ov != overlay && ov.Visibility == Visibility.Visible)
                    HideOverlayInstant(ov);
            }

            overlay.Visibility = Visibility.Visible;

            // Reset backdrop for clean fade-in from 0
            backdrop.Opacity = 0;

            // Force measure to get correct ActualWidth on first open
            panel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double panelWidth = panel.ActualWidth > 0 ? panel.ActualWidth : 800;
            slideTransform.X = panelWidth;

            var slideAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            slideTransform.BeginAnimation(TranslateTransform.XProperty, slideAnim);

            // Fade in backdrop
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            backdrop.BeginAnimation(OpacityProperty, fadeIn);

            // Set active nav button
            if (overlay == OrdersOverlay) SetActiveNavButton("Orders");
            else if (overlay == PricesOverlay) SetActiveNavButton("Prices");
            else if (overlay == UpdatesOverlay) SetActiveNavButton("Updates");
        }

        private void CloseOverlay_Click(object sender, RoutedEventArgs e)
        {
            CloseAllOverlays();
        }

        private void Backdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CloseAllOverlays();
        }

        private void CloseAllOverlays()
        {
            var overlays = new[]
            {
                (Grid: OrdersOverlay, Panel: OrdersPanel, Backdrop: OrdersBackdrop, Slide: OrdersSlideTransform),
                (Grid: PricesOverlay, Panel: PricesPanel, Backdrop: PricesBackdrop, Slide: PricesSlideTransform),
                (Grid: UpdatesOverlay, Panel: UpdatesPanel, Backdrop: UpdatesBackdrop, Slide: UpdatesSlideTransform),
            };

            foreach (var (grid, panel, backdrop, slide) in overlays)
            {
                if (grid.Visibility != Visibility.Visible) continue;

                // Cancel any running animations before starting close
                slide.BeginAnimation(TranslateTransform.XProperty, null);
                backdrop.BeginAnimation(OpacityProperty, null);

                // Animate slide-out to right
                double panelWidth = panel.ActualWidth > 0 ? panel.ActualWidth : 800;
                var slideOut = new DoubleAnimation(panelWidth, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                slideOut.Completed += (_, _) =>
                {
                    grid.Visibility = Visibility.Collapsed;
                };
                slide.BeginAnimation(TranslateTransform.XProperty, slideOut);

                // Fade out backdrop
                var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                backdrop.BeginAnimation(OpacityProperty, fadeOut);
            }

            SetActiveNavButton("Calc");
        }

        private static void HideOverlayInstant(Grid overlay)
        {
            overlay.Visibility = Visibility.Collapsed;
            // Cancel any running animations so they don't interfere with re-open
            foreach (var child in overlay.Children)
            {
                if (child is Border border)
                {
                    border.BeginAnimation(OpacityProperty, null);
                    if (border.RenderTransform is TranslateTransform t)
                        t.BeginAnimation(TranslateTransform.XProperty, null);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // NAV BADGE REFRESH
        // ═══════════════════════════════════════════════════════════════

        internal void RefreshNavBadges()
        {
            // Orders count badge on nav button
            int orderCount = OrdersHistoryControl?.OrdersGrid?.Items?.Count ?? 0;
            if (NavOrdersBadge != null)
            {
                NavOrdersBadge.Visibility = orderCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                NavOrdersBadgeText.Text = orderCount > 99 ? "99+" : orderCount.ToString();
            }

            // Orders count chip in overlay header (Russian pluralization + chip visibility).
            // Russian rule: last-two-digits drive the form. 11–14 are ALWAYS
            // "заказов" (special case). Otherwise the last digit decides:
            // 1 → заказ, 2..4 → заказа, 0/5..9 → заказов.
            // Without the 11–14 carve-out, "11 заказов" / "21 заказ" were wrong.
            // Hide the entire chip when count==0 — an empty chip floating next to
            // «Заказы» title is visual noise.
            if (OrdersCountBadge != null && OrdersCountText != null)
            {
                int m100 = orderCount % 100;
                int m10 = orderCount % 10;
                string suffix = (m100 >= 11 && m100 <= 14)
                    ? "заказов"
                    : m10 == 1
                        ? "заказ"
                        : (m10 >= 2 && m10 <= 4)
                            ? "заказа"
                            : "заказов";
                if (orderCount > 0)
                {
                    OrdersCountText.Text = $"• {orderCount} {suffix}";
                    OrdersCountBadge.Visibility = Visibility.Visible;
                }
                else
                {
                    OrdersCountText.Text = "";
                    OrdersCountBadge.Visibility = Visibility.Collapsed;
                }
            }

            // Updates dot
            if (NavUpdatesDot != null)
                NavUpdatesDot.Visibility = UpdateService.HasPendingUpdate() ? Visibility.Visible : Visibility.Collapsed;
        }

        private void StartNavBadgeTimer()
        {
            // 1.5s fallback timer (was 4s). RefreshNavBadges is called explicitly
            // from RefreshOrdersList/save/delete paths, so this timer is only a
            // safety net for cases that bypass RefreshOrdersList (e.g. file-system
            // changes from sync). 1.5s reduces the window during which the badge
            // shows a stale count after an out-of-band operation.
            _navBadgeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _navBadgeTimer.Tick += (s, e) =>
            {
                _navBadgeTimer.Stop();
                RefreshNavBadges();
                _navBadgeTimer.Start();
            };
            _navBadgeTimer.Start();
        }
    }
}
