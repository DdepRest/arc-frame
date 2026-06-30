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

            MainTabControl.SelectionChanged += (_, _) =>
            {
                if (!_initialLoadDone) return;
                if (MainTabControl.SelectedIndex == 0)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (QuickAddControl.CmbType != null && QuickAddControl.CmbType.IsEnabled)
                            QuickAddControl.CmbType.Focus();
                    });
                }
            };

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
            };

            StateChanged += (s, e) =>
            {
                if (TitleBarControl.MaxIcon != null)
                    TitleBarControl.MaxIcon.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE739";
            };
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
        }

        internal void UpdateEmptyState()
        {
            if (OrderItemsControl?.Empty == null || OrderItemsControl?.Grid == null) return;
            OrderItemsControl.Empty.Visibility = OrderItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
