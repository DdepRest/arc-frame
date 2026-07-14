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
using System.Windows.Media.Imaging;
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

        /// <summary>
        /// Настройки печати, сохраняемые в памяти на время жизни заказа.
        /// НЕ сериализуются. Сбрасываются в StartNewOrder.
        /// </summary>
        public PrintSettings LastPrintSettings { get; set; } = new();

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

        // ─── Phase 1 refactoring: extracted services ───────────────────────
        private NavigationService? _navService;
        private OverlayManager? _overlayManager;
        private SlopeOverlayCoordinator? _slopeCoordinator;
        private SlopesProUpsellGate _slopesProUpsellGate = new();

        // Cached overlay entries for direct access in NavButton_Click / ShowPrintOverlay
        private OverlayManager.OverlayEntry? _ordersEntry;
        private OverlayManager.OverlayEntry? _pricesEntry;
        private OverlayManager.OverlayEntry? _updatesEntry;
        private OverlayManager.OverlayEntry? _sidebarEntry;
        private OverlayManager.OverlayEntry? _printEntry;

        // ─── Idle/periodic update check scheduler ───────────────────────────
        // Запускается в Loaded после startup-проверки. Триггерит CheckInBackgroundAsync
        // каждые CheckInterval (30 мин) или после IdleThreshold (10 мин) простоя.
        // Activity-tracker: PreviewMouseMove + PreviewKeyDown сбрасывают idle.
        private UpdateCheckScheduler? _updateCheckScheduler;

        public MainWindow()
        {
            InitializeComponent();

            // v3.44 bug-fix: Icon загружается в code-behind (а не в XAML),
            // потому что XAML-парсер резолвит pack:// URI через
            // Application.GetResourcePackage, который падает в тестовом
            // контексте (testhost.exe ≠ MosquitoNetCalculator.exe).
            // Assembly-qualified URI работает в обоих контекстах — явно
            // указываем сборку MosquitoNetCalculator, где лежит app_icon.ico.
            //
            // v3.44 bug-fix #2: BitmapImage с .ico загружает только первый
            // кадр (16×16) → иконка в панели задач микроскопическая.
            // Решение: используем PNG-версию (256×256) — BitmapImage грузит
            // полное разрешение, Windows отмасштабирует для taskbar.
            // BitmapDecoder.Create НЕ подходит — крашит процесс в тестовом
            // STA-контексте (требует полной WPF-инфраструктуры).
            try
            {
                Icon = new BitmapImage(new Uri(
                    "pack://application:,,,/MosquitoNetCalculator;component/Resources/app_icon.png",
                    UriKind.Absolute));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Failed to load icon: {ex.Message}");
            }

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
            UpdateService.UpdateDetected += OnUpdateDetected;

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

            // Global Escape — closes any open overlay panel. Backdrop click already
            // covers the mouse path; this covers the keyboard path for speed.
            // XAML also has PreviewKeyDown="MainWindow_PreviewKeyDown" for a named handler.
            KeyDown += (_, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    args.Handled = true;
                    CloseAllOverlays();
                }
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
                    else if (item.IsQuantityOptional)
                    {
                        // Материал: ширина/высота/цвет не редактируются, количество редактируется
                        if (header is "Цвет" or "Ширина" or "Высота")
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

            // v3.43.3: Grid.PreviewMouseLeftButtonDown handler удалён — тогл чекбокса «Вкл.»
            // теперь идёт через естественный two-way binding IsChecked={Binding IsActive}.
            OrderItemsControl.Grid.LoadingRow += (_, e) => AttachRowHover(e.Row, true);
            OrderItemsControl.Grid.UnloadingRow += (_, e) => AttachRowHover(e.Row, false);
            OrdersHistoryControl.OrdersGrid.LoadingRow += (_, e) => AttachRowHover(e.Row, true);
            OrdersHistoryControl.OrdersGrid.UnloadingRow += (_, e) => AttachRowHover(e.Row, false);
            PricesControl.PriceDataGrid.LoadingRow += (_, e) => AttachRowHover(e.Row, true);
            PricesControl.PriceDataGrid.UnloadingRow += (_, e) => AttachRowHover(e.Row, false);

            Loaded += MainWindow_Loaded;
            SourceInitialized += (_, _) =>
            {
                EnableMicaBackdrop();
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
                UpdateService.UpdateDetected -= OnUpdateDetected;
                ThemeService.ThemeChanged -= OnThemeChanged;
                ThemeService.ThemeChanged -= ApplyMicaTitleBar;
                _updateCheckScheduler?.Stop();
                _navBadgeTimer?.Stop();
                _navService?.Shutdown();
            };

            StateChanged += (s, e) =>
            {
                if (TitleBarControl.MaxIcon != null)
                    TitleBarControl.MaxIcon.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE739";
            };

            // NavigationView badge refresh timer
            StartNavBadgeTimer();

            // ── Phase 1: initialize extracted services ──────────────────
            InitializeServices();
        }

        // ═══════════════════════════════════════════════════════════════
        // PHASE 1 — SERVICE INITIALIZATION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates and wires NavigationService, OverlayManager, SlopeOverlayCoordinator.
        /// Called after InitializeComponent so XAML elements are available.
        /// The slope OverlayEntry is created once and shared between
        /// OverlayManager and SlopeOverlayCoordinator to ensure a single
        /// consistent reference (avoids stale Entry bugs).
        /// </summary>
        private void InitializeServices()
        {
            // ── NavigationService ──
            var navButtons = new[] { NavBtnCalc, NavBtnOrders, NavBtnPrices, NavBtnUpdates, NavBtnSlope, NavBtnPrint };
            var navIcons = new[] { NavIconCalc, NavIconOrders, NavIconPrices, NavIconUpdates, NavIconSlope, NavIconPrint };
            var navLabels = new[] { NavLabelCalc, NavLabelOrders, NavLabelPrices, NavLabelUpdates, NavLabelSlope, NavLabelPrint };

            _navService = new NavigationService(navButtons, navIcons, navLabels, NavPanel, this);

            // ── Overlay entries (shared between OverlayManager and coordinators) ──
            _ordersEntry   = new OverlayManager.OverlayEntry(OrdersOverlay,   OrdersPanel,   OrdersBackdrop,   OrdersSlideTransform);
            _pricesEntry   = new OverlayManager.OverlayEntry(PricesOverlay,   PricesPanel,   PricesBackdrop,   PricesSlideTransform);
            _updatesEntry  = new OverlayManager.OverlayEntry(UpdatesOverlay,  UpdatesPanel,  UpdatesBackdrop,  UpdatesSlideTransform);
            _sidebarEntry  = new OverlayManager.OverlayEntry(SidebarOverlay,  SidebarPanel,  SidebarBackdrop,  SidebarSlideTransform);
            _printEntry    = new OverlayManager.OverlayEntry(PrintOverlay,    PrintPanel,    PrintBackdrop,    PrintSlideTransform);
            var slopeEntry = new OverlayManager.OverlayEntry(SlopeOverlay,    SlopePanel,    SlopeBackdrop,    SlopeSlideTransform);

            var allOverlays = new[] { _ordersEntry, _pricesEntry, _updatesEntry, _sidebarEntry, slopeEntry, _printEntry };

            _overlayManager = new OverlayManager(
                allOverlays,
                SetActiveNavButton,
                OnBeforeClosePrint);

            // ── SlopeOverlayCoordinator (created once, with shared Entry) ──
            _slopeCoordinator = new SlopeOverlayCoordinator(
                SlopePanelControl, slopeEntry,
                () => ViewModel.PricesVM,
                () => ViewModel.CalcVM,
                _overlayManager,
                SetActiveNavButton);
        }

        /// <summary>
        /// Called by OverlayManager.CloseAll before closing overlays.
        /// Saves print settings if PrintOverlay is visible.
        /// </summary>
        private void OnBeforeClosePrint()
        {
            if (PrintOverlay.Visibility == Visibility.Visible)
            {
                PrintPreviewControl.CollectSettings();
                LastPrintSettings = PrintPreviewControl.GetSettings();
            }
            PrintPreviewControl.Closed -= OnPrintPreviewClosed;
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

            // UX#5: Auto-focus on Type field so user can start typing immediately
            Dispatcher.BeginInvoke(() => QuickAddControl.CmbType?.Focus(),
                System.Windows.Threading.DispatcherPriority.Input);

            // UX: Populate EmptyState product chips so beginners see available products
            OrderItemsControl.PopulateProductChips(ProductNames);
        }

        internal void UpdateEmptyState()
        {
            if (OrderItemsControl?.Empty == null || OrderItemsControl?.Grid == null) return;
            OrderItemsControl.Empty.Visibility = OrderItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Called when the user clicks a product chip in the EmptyState.
        /// Selects the product in QuickAdd's Type ComboBox, which triggers
        /// color/price population, then focuses the Width field.
        /// </summary>
        internal void SelectProductFromChip(string productName)
        {
            // Find and select the product in the Type ComboBox
            var cmb = QuickAddControl.CmbType;
            if (cmb == null) return;

            int idx = -1;
            for (int i = 0; i < cmb.Items.Count; i++)
            {
                if (cmb.Items[i] is string s && s == productName)
                {
                    idx = i;
                    break;
                }
            }
            if (idx < 0) return;

            cmb.SelectedIndex = idx;

            // Focus Width field so user can start typing immediately
            Dispatcher.BeginInvoke(() =>
            {
                if (QuickAddControl.TxtWidth != null && QuickAddControl.TxtWidth.IsEnabled)
                    QuickAddControl.TxtWidth.Focus();
            }, System.Windows.Threading.DispatcherPriority.Input);
        }

        // ═══════════════════════════════════════════════════════════════
        // NAVIGATION VIEW — thin delegates to NavigationService
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
                    _overlayManager!.Toggle(_ordersEntry!, "Orders");
                    break;
                case "Prices":
                    _overlayManager!.Toggle(_pricesEntry!, "Prices");
                    break;
                case "Updates":
                    _overlayManager!.Toggle(_updatesEntry!, "Updates");
                    break;
                case "Slope":
                    if (SlopeOverlay.Visibility == Visibility.Visible)
                        CloseAllOverlays();
                    else
                        ShowSlopeOverlay();
                    break;
                case "Print":
                    if (PrintOverlay.Visibility == Visibility.Visible)
                        CloseAllOverlays();
                    else
                        ShowPrintOverlay();
                    break;
            }
        }

        // Keyboard shortcut handlers (Ctrl+1..5)
        private void NavCalculation_Click(object s, ExecutedRoutedEventArgs e) { NavigateToCalculation(); }
        private void NavOrders_Click(object s, ExecutedRoutedEventArgs e)     { NavButton_Click(NavBtnOrders, new RoutedEventArgs()); }
        private void NavPrices_Click(object s, ExecutedRoutedEventArgs e)     { NavButton_Click(NavBtnPrices, new RoutedEventArgs()); }
        private void NavUpdates_Click(object s, ExecutedRoutedEventArgs e)    { NavButton_Click(NavBtnUpdates, new RoutedEventArgs()); }
        private void NavPrint_Click(object s, ExecutedRoutedEventArgs e)      { NavButton_Click(NavBtnPrint, new RoutedEventArgs()); }

        /// <summary>Thin delegate to NavigationService.SetActive.</summary>
        internal void SetActiveNavButton(string tag) => _navService?.SetActive(tag);

        // ═══════════════════════════════════════════════════════════════
        // OVERLAY PANELS — thin delegates to OverlayManager
        // ═══════════════════════════════════════════════════════════════

        internal void ToggleSidebarOverlay()
        {
            _overlayManager!.Toggle(_sidebarEntry!);
        }

        private void CloseOverlay_Click(object sender, RoutedEventArgs e)
        {
            CloseAllOverlays();
        }

        private void Backdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CloseAllOverlays();
        }

        internal void CloseAllOverlays() => _overlayManager?.CloseAll();

        // ═══════════════════════════════════════════════════════════════
        // SLOPE OVERLAY — thin delegates to SlopeOverlayCoordinator
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Thin delegate to SlopeOverlayCoordinator.Show.</summary>
        internal void ShowSlopeOverlay() => _slopeCoordinator?.Show();

        /// <summary>Thin delegate to SlopeOverlayCoordinator.Edit.</summary>
        internal void EditSlopeItem(OrderItem materialItem) => _slopeCoordinator?.Edit(materialItem);

        /// <summary>Thin delegate to SlopeOverlayCoordinator.Close.</summary>
        internal void CloseSlopeOverlay() => _slopeCoordinator?.Close();

        private void BackdropSlope_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CloseSlopeOverlay();
        }

        private void NavSlope_Click(object sender, RoutedEventArgs e)
        {
            // Toggle-close path — НЕ показываем шутку при закрытии уже открытого
            // оверлея: пользователь хочет закрыть, а не увидеть рекламу.
            if (SlopeOverlay.Visibility == Visibility.Visible)
            {
                CloseAllOverlays();
                return;
            }

            // EASTER-EGG v3.43.2.9 — шуточная «PRO подписка» в меню Откосы.
            // Триггер ТОЛЬКО на menu-click (не на Ctrl+5/Print и не из
            // EditSlopeItem — правка существующего откоса).
            //
            // Чтобы выпилить шутку: см. чеклист в SlopesProUpsellGate.cs
            bool canOpenSlopes = _slopesProUpsellGate.ShouldOpenSlopes(this, () =>
            {
                ToastService.ShowToast(
                    "Шуточная PRO-подписка не сработала — откосы всё равно бесплатны 😄",
                    ToastType.Info,
                    durationMs: 4000);
            });

            if (!canOpenSlopes) return;

            ShowSlopeOverlay();
        }

        // ═══════════════════════════════════════════════════════════════
        // PRINT OVERLAY — kept in MainWindow (custom panel width + init logic)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Строит FlowDocument КП и показывает PrintOverlay с PrintPreviewControl.
        /// </summary>
        internal void ShowPrintOverlay()
        {
            var validItems = OrderItems.Where(i => !string.IsNullOrEmpty(i.Name) && i.IsActive && i.Total > 0).ToList();
            if (validItems.Count == 0)
            {
                ToastService.ShowToast("Добавьте хотя бы одну позицию.", ToastType.Warning);
                return;
            }

            // Close any other open overlay first
            _overlayManager?.HideAllExcept(_printEntry);

            double total = validItems.Sum(i => i.TotalWithDeduction);
            string amountInWords = AmountInWordsService.Convert(total);

            var document = PrintService.BuildFlowDocument(validItems, ClientInfo, total, amountInWords);
            if (document == null)
            {
                ToastService.ShowToast("Нет данных для печати.", ToastType.Warning);
                return;
            }

            // Initialize the PrintPreviewControl with PDF export data
            PrintPreviewControl.Initialize(document, PrintService, ClientInfo.ContractNumber, LastPrintSettings,
                items: validItems, clientInfo: ClientInfo, totalAmount: total, amountInWords: amountInWords);

            // Subscribe to Closed event — save settings + close overlay
            PrintPreviewControl.Closed -= OnPrintPreviewClosed;  // unsubscribe first to avoid double-subscription
            PrintPreviewControl.Closed += OnPrintPreviewClosed;

            // Set panel width: use 90% of window width for ample preview space
            double availableWidth = this.ActualWidth - 52;  // minus nav panel
            PrintPanel.Width = Math.Min(1050, availableWidth);

            PrintOverlay.Visibility = Visibility.Visible;
            PrintBackdrop.Opacity = 0;

            // Slide from right
            PrintPanel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double panelWidth = PrintPanel.ActualWidth > 0 ? PrintPanel.ActualWidth : PrintPanel.Width;
            PrintSlideTransform.X = panelWidth;

            var slideAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            PrintSlideTransform.BeginAnimation(TranslateTransform.XProperty, slideAnim);

            var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            PrintBackdrop.BeginAnimation(OpacityProperty, fadeIn);

            SetActiveNavButton("Print");
        }

        private void OnPrintPreviewClosed(object? sender, EventArgs e)
        {
            // CloseAllOverlays already saves settings and unsubscribes safely.
            CloseAllOverlays();
        }

        // ═══════════════════════════════════════════════════════════════
        // NAV BADGE REFRESH
        // ═══════════════════════════════════════════════════════════════

        internal void RefreshNavBadges()
        {
            int orderCount = OrdersHistoryControl?.OrdersGrid?.Items?.Count ?? 0;

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
