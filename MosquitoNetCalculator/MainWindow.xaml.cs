using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Runtime.InteropServices;
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

        // ── Fix maximized window covering the taskbar (WindowStyle="None") ──
        private const int WM_GETMINMAXINFO = 0x0024;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMaxTrackSize;
            public POINT ptMinTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        public MainWindow()
        {
            InitializeComponent();

            ToastService.Initialize(ToastCanvas);
            ViewModel.UndoRedo.SetDirtyCallback(UpdateDirtyIndicator);

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
                    if (item.IsWidthOnly)
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

            OrderItemsControl.Grid.LoadingRow += OrderGrid_LoadingRow;
            OrderItemsControl.Grid.UnloadingRow += OrderGrid_UnloadingRow;
            OrderItemsControl.Grid.PreviewMouseLeftButtonDown += Grid_PreviewMouseLeftButtonDown;
            OrdersHistoryControl.OrdersGrid.LoadingRow += OrdersList_LoadingRow;
            OrdersHistoryControl.OrdersGrid.UnloadingRow += OrdersList_UnloadingRow;
            PricesControl.PriceDataGrid.LoadingRow += PriceGrid_LoadingRow;
            PricesControl.PriceDataGrid.UnloadingRow += PriceGrid_UnloadingRow;

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
                if (OrdersHistoryControl.CtxStatusMenu != null)
                    OrdersHistoryControl.CtxStatusMenu.IsEnabled = hasSelection;
                if (OrdersHistoryControl.CtxOpenMenu != null)
                    OrdersHistoryControl.CtxOpenMenu.IsEnabled = hasSelection;
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

            // Defer the entrance cascade animation until AFTER the initial
            // layout/render pass has stabilized. Dispatching into the Loaded
            // priority queue keeps Storyboard.Begin + 9 simultaneous timer
            // subscriptions from competing with the very first paint — on
            // weaker GPUs / VMs the inline call drops the first frame at
            // startup. Schedule it, don't run it inline.
            Dispatcher.BeginInvoke(new Action(AnimateCardsOnLoad),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

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

        internal void StartNewOrder()
        {
            ViewModel.UndoRedo.SuppressDirtyChanges(() =>
            {
                CurrentOrderId = Guid.NewGuid().ToString();
                IsNewOrder = true;

                ClientInfo.ContractDate = DateTime.Today;
                SyncContractPrefix(AppSettingsService.LoadContractPrefix());

                ClientInfo.ClientName = "";
                ClientInfo.ClientPhone = "";
                ClientInfo.ClientAddress = "";
                ClientInfo.Notes = "";
                ClientInfo.HasAdditionalKp = false;
                ClientInfo.AdditionalKps.Clear();
                Sidebar.CmbOrderStatus.SelectedItem = OrderStatuses.All[0];

                QuickAddControl.ResetAnwisMode();

                ViewModel.CalcVM.UnsubscribeAll(UpdateTotal);
                ViewModel.CalcVM.ClearAll();

                UpdateTotal();
                UpdateCurrentOrderInfo();
            });
            MarkClean();
            ViewModel.UndoRedo.Clear();
        }

        internal void UpdateContractNumber()
        {
            if (SidebarControl?.TxtPrefix == null || SidebarControl?.TxtNumber == null) return;

            string prefix = Sidebar.TxtPrefix.Text.Trim();
            if (string.IsNullOrEmpty(prefix)) prefix = "1";

            string contractNum = ViewModel.OrdersVM.GenerateContractNumber(prefix);
            ClientInfo.ContractNumber = contractNum;
        }

        internal void UpdateCurrentOrderInfo()
        {
            if (ActionBarControl?.OrderInfoRun == null) return;
            ActionBarControl.OrderInfoRun.Text = IsNewOrder
                ? "Новый заказ"
                : $"Ред.: {ClientInfo.ContractNumber}";
        }

        internal void BtnSaveOrder_Click(object sender, RoutedEventArgs e)
        {
            ActionBarControl.BtnSaveOrder_Click(sender, e);
        }

        /// <summary>
        /// Re-opens the location-picker dialog so the user can change their
        /// installation point. After it closes, refresh sidebar / title / and
        /// the in-progress contract number so prefix change takes effect.
        /// </summary>
        internal void OpenWelcomeWindow()
        {
            var welcome = new WelcomeWindow { Owner = this };
            if (welcome.ShowDialog() != true) return;

            // Sync the prefix textbox with the freshly saved value, then re-roll
            // the in-progress contract number against the new prefix. Pattern
            // mirrors StartNewOrder so UpdateContractNumber reads the right value.
            SyncContractPrefix(AppSettingsService.LoadContractPrefix());

            UpdateBaseTitle();
        }

        /// <summary>
        /// Sets the Title to the base text "A.R.C. Frame v{version} – {location}".
        /// Preserves the trailing dirty marker "•" if present.
        /// </summary>
        internal void UpdateBaseTitle()
        {
            // Refresh the cache first, then delegate to ApplyTitle so the new
            // base text + current dirty state are written in lock-step.
            // AppSettingsService.LoadLocationName is sync IO and cheap, but
            // doing it once on location-change beats re-reading on every
            // edit-triggered dirty toggle (the old SetTitle path).
            _cachedTitleLocation = AppSettingsService.LoadLocationName();
            ApplyTitle();
        }

        internal void Grid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Find a CheckBox in the visual tree starting from the click target
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null && hit is not CheckBox)
            {
                hit = VisualTreeHelper.GetParent(hit);
            }

            if (hit is CheckBox cb && cb.DataContext is OrderItem item)
            {
                // Toggle the bound IsActive property immediately and prevent the
                // DataGrid from starting cell selection. This gives single-click
                // checkbox toggling instead of the default 2-click pattern.
                item.IsActive = !item.IsActive;
                MarkDirty();
                e.Handled = true;
            }
        }

        internal void OrderGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
        {
            e.Row.Background = new SolidColorBrush(Colors.Black) { Opacity = 0 };
            e.Row.MouseEnter += OrderRow_MouseEnter;
            e.Row.MouseLeave += OrderRow_MouseLeave;
        }

        internal void OrderGrid_UnloadingRow(object? sender, DataGridRowEventArgs e)
        {
            e.Row.MouseEnter -= OrderRow_MouseEnter;
            e.Row.MouseLeave -= OrderRow_MouseLeave;
        }

        internal void OrdersList_LoadingRow(object? sender, DataGridRowEventArgs e)
        {
            e.Row.Background = new SolidColorBrush(Colors.Black) { Opacity = 0 };
            e.Row.MouseEnter += OrderRow_MouseEnter;
            e.Row.MouseLeave += OrderRow_MouseLeave;
        }

        internal void OrdersList_UnloadingRow(object? sender, DataGridRowEventArgs e)
        {
            e.Row.MouseEnter -= OrderRow_MouseEnter;
            e.Row.MouseLeave -= OrderRow_MouseLeave;
        }

        internal void PriceGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
        {
            e.Row.Background = new SolidColorBrush(Colors.Black) { Opacity = 0 };
            e.Row.MouseEnter += OrderRow_MouseEnter;
            e.Row.MouseLeave += OrderRow_MouseLeave;
        }

        internal void PriceGrid_UnloadingRow(object? sender, DataGridRowEventArgs e)
        {
            e.Row.MouseEnter -= OrderRow_MouseEnter;
            e.Row.MouseLeave -= OrderRow_MouseLeave;
        }

        private static void OrderRow_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not DataGridRow row || row.Background is not SolidColorBrush brush)
                return;
            var targetColor = (Color)Application.Current.Resources["RowHoverColor"];
            brush.Color = targetColor;
            brush.BeginAnimation(Brush.OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }

        private static void OrderRow_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is not DataGridRow row || row.Background is not SolidColorBrush brush)
                return;
            brush.BeginAnimation(Brush.OpacityProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }

        internal void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is OrderItem item)
            {
                PushUndo();
                item.RecalculateRequested -= UpdateTotal;
                ViewModel.CalcVM.DeleteItem(item);
                UpdateTotal();
                UpdateEmptyState();
            }
        }

        internal void AnwisModePillLeftClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not OrderItem item) return;
            if (!item.IsAnwis) return;

            var menu = Controls.AnwisContextMenuBuilder.Build(
                item.AnwisSizeMode,
                mode =>
                {
                    PushUndo();
                    item.AnwisSizeMode = mode;
                    MarkDirty();
                },
                fe);

            menu.IsOpen = true;
        }

        internal void AnwisModePillRightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not OrderItem item) return;
            if (!item.IsAnwis) return;

            var menu = Controls.AnwisContextMenuBuilder.Build(
                item.AnwisSizeMode,
                mode =>
                {
                    PushUndo();
                    item.AnwisSizeMode = mode;
                    MarkDirty();
                },
                fe);

            menu.IsOpen = true;
        }

        internal void BtnToggleInstallation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not OrderItem item) return;
            if (!item.IsInstallationApplicable) return;

            var menu = new ContextMenu
            {
                Style = (Style)FindResource(typeof(ContextMenu))
            };

            var txtDeduction = new TextBox
            {
                Width = 60,
                Height = 24,
                FontSize = 11,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };

            void RefreshDeductionField()
            {
                bool adjust = item.InstallationMode == 1 || item.InstallationMode == 2;
                txtDeduction.IsEnabled = adjust;
                txtDeduction.Text = adjust ? item.CurrentInstallationAmount.ToString("F0") : "0";
                txtDeduction.ToolTip = adjust
                    ? "Сумма корректировки: вычитается в ✕ и в В"
                    : "Не применяется в этом режиме";
            }

            void CommitDeductionIfPending()
            {
                if (!txtDeduction.IsEnabled) return;
                if (double.TryParse(txtDeduction.Text, out double val) && val >= 0)
                {
                    if (Math.Abs(item.CurrentInstallationAmount - val) > 0.01)
                    {
                        item.SetCurrentInstallationAmount(val);
                        _lastSumEditTime = DateTime.Now;
                        MarkDirty();
                    }
                }
            }

            RefreshDeductionField();

            void SetMode(int mode)
            {
                PushUndo();
                CommitDeductionIfPending();
                item.InstallationMode = mode;
                foreach (var m in menu.Items.OfType<MenuItem>().Take(3))
                    m.IsChecked = m == menu.Items[mode];
                RefreshDeductionField();
                txtDeduction.Dispatcher.BeginInvoke(FocusDeductionField);
            }

            void FocusDeductionField()
            {
                txtDeduction.Focus();
                Keyboard.Focus(txtDeduction);
                if (txtDeduction.IsEnabled)
                    txtDeduction.SelectAll();
            }

            var miMode0 = new MenuItem
            {
                Header = "— Монтаж включён",
                IsCheckable = true,
                IsChecked = item.InstallationMode == 0,
                StaysOpenOnClick = true
            };
            miMode0.Click += (_, _) => SetMode(0);
            menu.Items.Add(miMode0);

            var miMode1 = new MenuItem
            {
                Header = "✕ Без монтажа",
                IsCheckable = true,
                IsChecked = item.InstallationMode == 1,
                StaysOpenOnClick = true
            };
            miMode1.Click += (_, _) => SetMode(1);
            menu.Items.Add(miMode1);

            var miMode2 = new MenuItem
            {
                Header = "В конструкцию",
                IsCheckable = true,
                IsChecked = item.InstallationMode == 2,
                StaysOpenOnClick = true
            };
            miMode2.Click += (_, _) => SetMode(2);
            menu.Items.Add(miMode2);

            menu.Items.Add(new Separator());

            var deductionItem = new MenuItem
            {
                StaysOpenOnClick = true
            };
            var deductionPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(4, 2, 4, 2)
            };
            deductionPanel.Children.Add(new TextBlock
            {
                Text = "Сумма:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 12
            });
            txtDeduction.LostFocus += (_, _) =>
            {
                CommitDeductionIfPending();
                txtDeduction.Text = item.CurrentInstallationAmount.ToString("F0");
            };
            txtDeduction.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    CommitDeductionIfPending();
                    txtDeduction.Text = item.CurrentInstallationAmount.ToString("F0");
                    menu.IsOpen = false;
                }
            };
            deductionPanel.Children.Add(txtDeduction);
            deductionPanel.Children.Add(new TextBlock
            {
                Text = "₽",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
                FontSize = 11
            });
            deductionItem.Header = deductionPanel;
            menu.Items.Add(deductionItem);

            menu.Opened += (_, _) =>
            {
                bool recentlyEdited = _lastSumEditTime.HasValue
                    && DateTime.Now - _lastSumEditTime.Value < SmartFocusWindow;
                if (recentlyEdited && txtDeduction.IsEnabled)
                    txtDeduction.Dispatcher.BeginInvoke(FocusDeductionField);
            };

            menu.PlacementTarget = btn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        internal void UpdateEmptyState()
        {
            if (OrderItemsControl?.Empty == null || OrderItemsControl?.Grid == null) return;
            OrderItemsControl.Empty.Visibility = OrderItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        internal void RecalculateOrderGridColumnWidths()
        {
            if (_columnRecalcPending) return;
            _columnRecalcPending = true;

            Dispatcher.BeginInvoke(() =>
            {
                _columnRecalcPending = false;
                if (IsLoaded) RecalculateOrderGridColumnWidthsCore();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void RecalculateOrderGridColumnWidthsCore()
        {
            if (OrderItemsControl?.Grid == null) return;

            var grid = OrderItemsControl.Grid;
            if (grid.Columns.Count >= 12)
            {
                DataGridColumnAutoSizer.SetColumnMinWidth(grid, grid.Columns[0], "", null, 54, 0);
                DataGridColumnAutoSizer.SetColumnMinWidth(grid, grid.Columns[11], "", null, 32, 0);
            }

            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "№"), "№",
                OrderItems.Select(i => i.RowNumber.ToString()), headerPad: 20);
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Наименование"), "Наименование");
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Цвет"), "Цвет",
                OrderItems.Select(i => i.Color));
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Ширина"), "Ширина",
                OrderItems.Select(i => i.Width > 0 ? $"{i.Width:F0} мм" : ""));
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Высота"), "Высота",
                OrderItems.Select(i => i.Height > 0 ? $"{i.Height:F0} мм" : ""));
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Монтаж"), "Монтаж",
                OrderItems.Select(i => i.InstallationDisplay),
                contentPad: 40, contentWeight: FontWeights.Bold, contentFontSize: 14);
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Кол-во"), "Кол-во",
                OrderItems.Select(i => i.Quantity > 0 ? i.Quantity.ToString() : ""),
                contentPad: 24);
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Площ./Дл."), "Площ./Дл.",
                OrderItems.Select(i => i.CalculatedValueDisplay),
                contentPad: 24);
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Цена"), "Цена",
                OrderItems.Select(i => i.Price > 0 ? MoneyFormatService.Format(i.Price) : ""));
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Сумма"), "Сумма",
                OrderItems.Select(i => i.TotalDisplay),
                contentPad: 24, contentWeight: FontWeights.Medium);
        }

        internal void RefreshOrdersList()
        {
            var sortDescriptions = OrdersHistoryControl.OrdersGrid.Items.SortDescriptions.ToList();

            var orders = ViewModel.OrdersVM.LoadAllOrders();

            var grid = OrdersHistoryControl.OrdersGrid;
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "№ КП"), "№ КП",
                orders.Select(o => o.ContractNumber));
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Адрес"), "Адрес");
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Телефон"), "Телефон",
                orders.Select(o => o.ClientPhone));
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Дата"), "Дата",
                orders.Select(o => o.ContractDate.ToString("dd.MM.yyyy")));
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Сумма, руб."), "Сумма, руб.",
                orders.Select(o => MoneyFormatService.Format(o.TotalAmount)),
                contentWeight: FontWeights.Medium);
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Статус"), "Статус",
                orders.Select(o => o.Status).Distinct(),
                contentPad: 32, contentWeight: FontWeights.Medium, contentFontSize: 11);
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Обновлено"), "Обновлено",
                orders.Select(o => o.UpdatedAt.ToString("dd.MM.yy HH:mm")));

            grid.ItemsSource = orders;

            foreach (var sd in sortDescriptions)
            {
                grid.Items.SortDescriptions.Add(sd);
            }
            grid.Items.Refresh();

            UpdateSortIndicatorsFromSortDescriptions();

            if (OrdersHistoryControl?.OrdersCount != null)
                OrdersHistoryControl.OrdersCount.Text = $"Заказов: {orders.Count}";
            OrdersHistoryControl?.SetOrdersCount(orders.Count);
        }

        /// <summary>Update the persisted order status from a context-menu
        /// submenu item, skipping the modal dialog of <see cref="ChangeSelectedOrderStatus"/>.</summary>
        internal void ChangeSelectedOrderStatusInline(string newStatus)
        {
            if (OrdersHistoryControl.OrdersGrid.SelectedItem is not OrderData order) return;
            if (string.IsNullOrEmpty(newStatus) || newStatus == order.Status) return;

            order.Status = newStatus;
            ViewModel.OrdersVM.SaveOrder(order);
            RefreshOrdersList();
            ToastService.ShowToast($"Статус: {newStatus}", ToastType.Success);
        }

        /// <summary>Populate the «Изменить статус» submenu of the orders
        /// DataGrid context menu with one item per <see cref="OrderStatuses.All"/> entry.</summary>
        internal void RefreshStatusSubMenu()
        {
            if (OrdersHistoryControl?.CtxStatusMenu == null) return;
            OrdersHistoryControl.CtxStatusMenu.Items.Clear();
            foreach (var status in OrderStatuses.All)
            {
                var item = new MenuItem { Header = status };
                item.Click += (_, _) => ChangeSelectedOrderStatusInline(status);
                OrdersHistoryControl.CtxStatusMenu.Items.Add(item);
            }
        }

        internal void BtnRefreshOrders_Click(object sender, RoutedEventArgs e)
        {
            RefreshOrdersList();
        }

        internal void BtnExportOrders_Click(object sender, RoutedEventArgs e)
        {
            var allOrders = ViewModel.OrdersVM.LoadAllOrders();
            if (allOrders.Count == 0)
            {
                ToastService.ShowToast("Нет заказов для экспорта.", ToastType.Info);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "Экспорт заказов",
                Filter = "JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*",
                DefaultExt = ".json",
                FileName = $"orders_{DateTime.Now:yyyy-MM-dd}.json"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    ViewModel.OrdersVM.ExportOrders(allOrders, dlg.FileName);
                    ToastService.ShowToast($"Экспортировано {allOrders.Count} заказов.", ToastType.Success);
                }
                catch (Exception ex)
                {
                    ToastService.ShowToast($"Ошибка экспорта: {ex.Message}", ToastType.Error);
                }
            }
        }

        internal void BtnImportOrders_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Импорт заказов",
                Filter = "JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*",
                DefaultExt = ".json",
                Multiselect = false
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    List<OrderData>? fileOrders;
                    try
                    {
                        fileOrders = ViewModel.OrdersVM.ReadOrdersFromFile(dlg.FileName);
                    }
                    catch
                    {
                        ToastService.ShowToast("Не удалось прочитать файл. Проверьте формат.", ToastType.Error);
                        return;
                    }

                    if (fileOrders == null || fileOrders.Count == 0)
                    {
                        ToastService.ShowToast("Файл не содержит заказов.", ToastType.Info);
                        return;
                    }

                    List<OrderData>? selected;
                    if (fileOrders.Count == 1)
                    {
                        selected = fileOrders;
                    }
                    else
                    {
                        var importDlg = new ImportDialogWindow(fileOrders, this);
                        importDlg.ShowDialog();
                        if (!importDlg.DialogResultOk) return;
                        selected = importDlg.SelectedOrders;
                    }

                    if (selected.Count == 0)
                    {
                        ToastService.ShowToast("Не выбрано ни одного заказа.", ToastType.Info);
                        return;
                    }

                    var imported = ViewModel.OrdersVM.MergeImport(selected);

                    RefreshOrdersList();

                    if (imported.Count > 0)
                        ToastService.ShowToast($"Импортировано {imported.Count} заказов.", ToastType.Success);
                    else
                        ToastService.ShowToast("Все выбранные заказы уже существуют в актуальной версии.", ToastType.Info);
                }
                catch (Exception ex)
                {
                    ToastService.ShowToast($"Ошибка импорта: {ex.Message}", ToastType.Error);
                }
            }
        }



        private void OrdersList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // MouseDoubleClick fires on the entire DataGrid — including
            // the scrollbars and column headers. Double-clicking a column
            // header (to autofit its width, for example) must NOT silently
            // overwrite the user's currently-loaded order in the «Расчёт»
            // tab. Skip when the double-click originated from a header
            // chrome element (column header or row header).
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null)
            {
                if (hit is System.Windows.Controls.Primitives.DataGridColumnHeader
                    || hit is System.Windows.Controls.Primitives.DataGridRowHeader)
                    return;
                hit = VisualTreeHelper.GetParent(hit);
            }
            OpenSelectedOrder();
        }

        internal void OpenSelectedOrder()
        {
            if (OrdersHistoryControl.OrdersGrid.SelectedItem is not OrderData order) return;

            if (!DialogService.ShowConfirm($"Открыть заказ \u00AB{order.ContractNumber}\u00BB \u2014 {order.ClientName}?\n\nТекущие данные будут заменены.", "Открыть заказ", this)) return;

            CurrentOrderId = order.Id;
            IsNewOrder = false;
            _suppressContractNumberUpdate = true;
            try
            {
                SuppressPrefixSave = true;

                ViewModel.UndoRedo.SuppressDirtyChanges(() =>
                {
                    ClientInfo.ClientName = order.ClientName;
                    ClientInfo.ClientPhone = order.ClientPhone;
                    ClientInfo.ClientAddress = order.ClientAddress;
                    ClientInfo.ContractNumber = order.ContractNumber;
                    ClientInfo.ContractDate = order.ContractDate;
                    ClientInfo.Notes = order.Notes ?? "";
                    ClientInfo.AdditionalKps.Clear();
                    if (order.AdditionalKps != null && order.AdditionalKps.Count > 0)
                    {
                        foreach (var kp in order.AdditionalKps)
                            ClientInfo.AdditionalKps.Add(new AdditionalKpItem { Number = kp.Number ?? "", Amount = kp.Amount, IsActive = kp.IsActive });
                        ClientInfo.HasAdditionalKp = true;
                    }
                    else if (order.HasAdditionalKp && !string.IsNullOrEmpty(order.AdditionalKpNumber))
                    {
                        ClientInfo.AdditionalKps.Add(new AdditionalKpItem
                        {
                            Number = order.AdditionalKpNumber ?? "",
                            Amount = order.AdditionalKpAmount
                        });
                        ClientInfo.HasAdditionalKp = true;
                    }
                    else
                    {
                        ClientInfo.HasAdditionalKp = false;
                    }

                    if (order.ContractNumber.Contains('-'))
                    {
                        var parts = order.ContractNumber.Split('-', 2);
                        Sidebar.TxtPrefix.Text = parts[0];
                    }
                    else
                    {
                        Sidebar.TxtPrefix.Text = string.Empty;
                    }

                    Sidebar.CmbOrderStatus.SelectedItem = order.Status;

                    QuickAddControl.ResetAnwisMode();

                    ViewModel.CalcVM.UnsubscribeAll(UpdateTotal);
                    ViewModel.CalcVM.LoadFromOrderData(order, UpdateTotal);

                    UpdateTotal();
                    UpdateCurrentOrderInfo();
                    UpdateEmptyState();
                });
                MarkClean();
                ViewModel.UndoRedo.Clear();

                MainTabControl.SelectedIndex = 0;
            }
            finally
            {
                _suppressContractNumberUpdate = false;
            }
        }

        internal void ChangeSelectedOrderStatus()
        {
            if (OrdersHistoryControl.OrdersGrid.SelectedItem is not OrderData order) return;

            // Chromeless Fluent dialog matching DialogService.ShowConfirm design
            var window = new Window
            {
                Title = "Изменить статус заказа",
                Width = 380,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                SizeToContent = SizeToContent.Height
            };

            var card = new Border
            {
                Background = (Brush)FindResource("Surface") ?? Brushes.White,
                BorderBrush = (Brush)FindResource("Border") ?? Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = (Color)FindResource("ShadowColor"),
                    BlurRadius = 20,
                    ShadowDepth = 2,
                    Opacity = 0.25
                }
            };

            var rootStack = new StackPanel();

            // Custom title bar
            var titleBar = new Border
            {
                Background = (Brush)FindResource("HeaderBg") ?? Brushes.WhiteSmoke,
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                Height = 32,
                Padding = new Thickness(14, 0, 0, 0)
            };
            var titleBarGrid = new Grid();
            titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleText = new TextBlock
            {
                Text = "Изменить статус заказа",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimary") ?? Brushes.Black,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(titleText, 0);
            titleBarGrid.Children.Add(titleText);

            var closeBtn = DialogService.CreateFluentCloseButton(() => window.Close());
            Grid.SetColumn(closeBtn, 1);
            titleBarGrid.Children.Add(closeBtn);

            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left) window.DragMove();
            };

            titleBar.Child = titleBarGrid;
            rootStack.Children.Add(titleBar);

            // Content
            var content = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            content.Children.Add(new TextBlock
            {
                Text = $"Заказ: {order.ContractNumber}",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = (Brush)FindResource("TextPrimary") ?? Brushes.Black,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var combo = new ComboBox
            {
                ItemsSource = OrderStatuses.All,
                SelectedItem = order.Status,
                Margin = new Thickness(0, 0, 0, 16)
            };
            content.Children.Add(combo);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnCancel = new Button
            {
                Content = "Отмена",
                Style = (Style)FindResource("GhostButton") ?? null,
                Padding = new Thickness(16, 7, 16, 7),
                Margin = new Thickness(0, 0, 8, 0),
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                Cursor = Cursors.Hand,
                IsCancel = true
            };
            btnCancel.Click += (s, e) => window.Close();

            var btnOk = new Button
            {
                Content = "Сохранить",
                Style = (Style)FindResource("PrimaryButton") ?? null,
                Padding = new Thickness(16, 7, 16, 7),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                IsDefault = true
            };
            btnOk.Click += (s, e) =>
            {
                order.Status = combo.SelectedItem?.ToString() ?? order.Status;
                ViewModel.OrdersVM.SaveOrder(order);
                RefreshOrdersList();
                window.Close();
                ToastService.ShowToast("Статус обновлён.", ToastType.Success);
            };

            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnOk);
            content.Children.Add(btnPanel);

            rootStack.Children.Add(content);
            card.Child = rootStack;
            window.Content = card;

            window.ShowDialog();
        }

        internal void DeleteSelectedOrder()
        {
            if (OrdersHistoryControl.OrdersGrid.SelectedItem is not OrderData order) return;

            if (DialogService.ShowConfirm($"Удалить заказ \u00AB{order.ContractNumber}\u00BB \u2014 {order.ClientName}?\n\nЭто действие нельзя отменить.", "Удалить заказ", this))
            {
                ViewModel.OrdersVM.DeleteOrder(order.Id);
                RefreshOrdersList();
                ToastService.ShowToast("Заказ удалён.", ToastType.Info);
            }
        }

        internal void ExportSelectedOrder()
        {
            if (OrdersHistoryControl.OrdersGrid.SelectedItem is not OrderData order) return;

            // Build a filesystem-friendly default filename.
            //  • "/" is technically an invalid Windows filename char, so current
            //    SanitizeFileName() simply drops it ("Гагарина 39/25" → "Гагарина 3925").
            //    We replace it with a space so the address structure stays readable:
            //    "Гагарина 39/25" → "ГАГАРИНА 39 25".
            //  • "_" separator between address and contract number becomes " " so the
            //    resulting filename reads as "ГАГАРИНА 39 25 1-1.json" instead of
            //    "ГАГАРИНА 3925_1-1.json".
            //  • Collapse runs of spaces introduced by the above into a single space.
            string raw = (order.ClientAddress ?? string.Empty).Replace('/', ' ');
            string address = ViewModel.OrdersVM.SanitizeFileName(raw);
            if (!string.IsNullOrEmpty(address))
            {
                address = address.ToUpperInvariant();
                // Single-pass collapse of any run of spaces (introduced by the
                // '/'→' ' substitution or by stripped filename-invalid chars)
                // into a single space. Split + RemoveEmptyEntries + Join is O(n)
                // and a touch cleaner than a manual while-loop scan.
                address = string.Join(" ", address.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
            string defaultName = string.IsNullOrEmpty(address)
                ? $"order {order.ContractNumber}.json"
                : $"{address} {order.ContractNumber}.json";

            var dlg = new SaveFileDialog
            {
                Title = "Экспорт заказа",
                Filter = "JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*",
                DefaultExt = ".json",
                FileName = defaultName
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    ViewModel.OrdersVM.ExportOrders(new List<OrderData> { order }, dlg.FileName);
                    ToastService.ShowToast($"Заказ {order.ContractNumber} экспортирован!", ToastType.Success);
                }
                catch (Exception ex)
                {
                    ToastService.ShowToast($"Ошибка экспорта: {ex.Message}", ToastType.Error);
                }
            }
        }

        internal void OrdersList_Sorting(object sender, DataGridSortingEventArgs e)
        {
            // DataGrid raises the Sorting event BEFORE applying the new
            // SortDirection to e.Column. Reading col.SortDirection inside
            // this handler gives the OLD value, so any indicator update
            // here lags by one click (the previous glyph persists).
            // Defer the refresh until after WPF's internal Sorting pass
            // completes — Background priority is the lowest normal
            // priority and runs after default input processing.
            Dispatcher.BeginInvoke(new Action(UpdateSortIndicatorsFromSortDescriptions),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Resolves the property name WPF uses internally for sorting
        /// a column — explicit <c>SortMemberPath</c> takes priority,
        /// otherwise it falls back to the binding's <c>Path.Path</c> for
        /// <see cref="DataGridBoundColumn"/> children (TextColumn etc.).
        /// Returns null when neither is available — the column can't be
        /// matched to a <see cref="SortDescription"/> in that case and
        /// must be treated as un-sorted.
        ///
        /// We need this because OrdersHistoryControl.xaml does NOT set
        /// SortMemberPath on every column — WPF auto-derives it from the
        /// binding path AT SORT TIME and stores the result in
        /// <c>Items.SortDescriptions[i].PropertyName</c>. Reading
        /// <c>col.SortMemberPath</c> post-hoc returns an empty string for
        /// these columns, so naively matching it against the
        /// SortDescription wipes the sort indicator on every Refresh.
        /// </summary>
        private static string? GetColumnSortKey(System.Windows.Controls.DataGridColumn col)
        {
            if (!string.IsNullOrEmpty(col.SortMemberPath))
                return col.SortMemberPath;
            if (col is System.Windows.Controls.DataGridBoundColumn bound
                && bound.Binding is System.Windows.Data.Binding b
                && b.Path != null)
                return b.Path.Path;
            return null;
        }

        internal void UpdateSortIndicatorsFromSortDescriptions()
        {
            foreach (var col in OrdersHistoryControl.OrdersGrid.Columns)
            {
                string clean = DataGridColumnAutoSizer.StripSortIndicator(col.Header?.ToString());
                string? sortKey = GetColumnSortKey(col);
                var match = !string.IsNullOrEmpty(sortKey)
                    ? OrdersHistoryControl.OrdersGrid.Items.SortDescriptions
                        .FirstOrDefault(x => x.PropertyName == sortKey)
                    : default;
                if (!string.IsNullOrEmpty(match.PropertyName))
                {
                    col.Header = clean + (match.Direction == ListSortDirection.Ascending ? " \u25B2" : " \u25BC");
                    col.SortDirection = match.Direction;
                }
                else
                {
                    col.Header = clean;
                    col.SortDirection = null;
                }
            }
        }

        internal void LoadPrices()
        {
            ViewModel.PricesVM.LoadPrices();
            if (PricesControl?.PriceDataGrid != null)
            {
                PricesControl.PriceDataGrid.Items.Refresh();
                RecalculatePriceGridColumnWidths();
            }
        }

        internal void RefreshComboBoxColumns()
        {
            ViewModel.RefreshComboBoxColumns();
            QuickAddControl.CmbType.ItemsSource = ProductNames;
        }

        internal void RecalculatePriceGridColumnWidths()
        {
            if (PricesControl?.PriceDataGrid == null) return;

            var grid = PricesControl.PriceDataGrid;
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Наименование"), "Наименование");
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Цвет"), "Цвет",
                Prices.Select(p => p.Color));
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Цена, руб."), "Цена, руб.",
                Prices.Select(p => p.Price > 0 ? MoneyFormatService.Format(p.Price) : ""));
        }

        internal void UpdateTotal()
        {
            // Debounce: reset timer on every call so rapid property changes batch into one update
            if (_updateTotalDebounceTimer != null)
            {
                _updateTotalDebounceTimer.Stop();
                _updateTotalDebounceTimer.Start();
                return;
            }

            _updateTotalDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50),
                IsEnabled = true
            };
            _updateTotalDebounceTimer.Tick += (_, _) =>
            {
                _updateTotalDebounceTimer?.Stop();
                ExecuteUpdateTotal();
            };
            _updateTotalDebounceTimer.Start();
        }

        private void ExecuteUpdateTotal()
        {
            // Already on the UI thread (called from DispatcherTimer.Tick); no need to
            // re-dispatch via Dispatcher.Invoke — that was a no-op redirect to self
            // and a reentrancy hazard if the UI thread was already busy.
            var info = ViewModel.CalcVM.CalculateTotal(ClientInfo.AdditionalKpsTotal);

            if (TotalCardControl?.TotalRun != null)
                TotalCardControl.TotalRun.Text = MoneyFormatService.Format(info.Total);

            if (TotalCardControl?.TotalSub != null)
            {
                if (info.Count == 0 && ClientInfo.AdditionalKpsTotal <= 0)
                {
                    TotalCardControl.TotalSub.Text = "";
                }
                else
                {
                    var parts = new List<string>();
                    if (info.TotalArea > 0) parts.Add($"{info.TotalArea:F3} м\u00B2");
                    if (info.TotalLinear > 0) parts.Add($"{info.TotalLinear:F3} м.п.");
                    if (info.TotalPieces > 0) parts.Add($"{info.TotalPieces} шт.");
                    if (ClientInfo.AdditionalKpsTotal > 0) parts.Add($"доп. КП {MoneyFormatService.Format(ClientInfo.AdditionalKpsTotal)} руб.");
                    TotalCardControl.TotalSub.Text = $"{info.Count} поз." + (parts.Count > 0 ? ", " + string.Join(", ", parts) : "");
                }
            }

            if (TotalCardControl?.AmountWords != null)
                TotalCardControl.AmountWords.Text = info.Total > 0
                    ? AmountInWordsService.Convert(info.Total)
                    : "";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>
        /// Builds the base title text WITHOUT the trailing dirty marker from
        /// the cached location + cached assembly version. Pure function over
        /// cached state — touch-free of AppSettingsService and UndoRedo so it
        /// composes cleanly with the dirty-toggling ApplyTitle() path.
        /// </summary>
        private string BuildBaseTitle()
        {
            string version = _appVersion?.ToString() ?? "";
            return string.IsNullOrEmpty(_cachedTitleLocation)
                ? $"A.R.C. Frame v{version}"
                : $"A.R.C. Frame v{version} – {_cachedTitleLocation}";
        }

        /// <summary>
        /// Single apply path: writes <c>Title</c> + <c>ActionBarControl.DirtyChip</c>
        /// in lock-step from the cached base title and the current dirty flag.
        /// Replaces the old <c>SetTitle()</c> which parsed the existing Title
        /// via <c>Title.Split('•')[0].TrimEnd()</c> on every dirty toggle —
        /// zero string-parse round-trip per edit now. Two visible indicators
        /// remain for the user:
        ///   •  Window.Title carries a trailing "  •"
        ///   •  ActionBar.DirtyChip («Есть изменения» chip)
        /// A third in-the-title-bar dot was tried and removed — see
        /// TitleBarControl.xaml commit history.
        /// </summary>
        private void ApplyTitle()
        {
            bool dirty = ViewModel?.UndoRedo?.IsDirty ?? false;
            Title = dirty ? BuildBaseTitle() + "  •" : BuildBaseTitle();

            if (ActionBarControl?.DirtyChip != null)
                ActionBarControl.DirtyChip.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateDirtyIndicator() => ApplyTitle();

        internal void MarkDirty()
        {
            ViewModel.UndoRedo.MarkDirty();
        }

        internal void MarkClean()
        {
            ViewModel.UndoRedo.MarkClean();
        }

        internal void PushUndo()
        {
            var snapshot = ViewModel.SnapshotItems();
            // Skip duplicate: if the new snapshot matches the current top,
            // don't pollute the stack (e.g. user clicked a cell but made no changes).
            if (ViewModel.UndoRedo.TryPeekTopSnapshot(out var top))
            {
                try
                {
                    string newJson = System.Text.Json.JsonSerializer.Serialize(snapshot);
                    string topJson = System.Text.Json.JsonSerializer.Serialize(top!);
                    if (newJson == topJson) return;
                }
                catch { /* serialization failure is non-fatal; push anyway */ }
            }
            ViewModel.UndoRedo.PushUndo(snapshot);
        }



        private void RestoreFromSnapshot(OrderSnapshot snapshot)
        {
            ViewModel.RestoreFromSnapshot(snapshot, UpdateTotal);
            UpdateTotal();
            UpdateEmptyState();
        }

        private void Undo()
        {
            if (!ViewModel.UndoRedo.CanUndo)
            {
                ToastService.ShowToast("Нечего отменять.", ToastType.Info);
                return;
            }
            var prev = ViewModel.UndoRedo.Undo(ViewModel.SnapshotItems);
            if (prev != null) RestoreFromSnapshot(prev);
        }

        private void Redo()
        {
            if (!ViewModel.UndoRedo.CanRedo)
            {
                ToastService.ShowToast("Нечего повторять.", ToastType.Info);
                return;
            }
            var next = ViewModel.UndoRedo.Redo(ViewModel.SnapshotItems);
            if (next != null) RestoreFromSnapshot(next);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                    mmi.ptMaxPosition.X = mi.rcWork.Left;
                    mmi.ptMaxPosition.Y = mi.rcWork.Top;
                    mmi.ptMaxSize.X = mi.rcWork.Right - mi.rcWork.Left;
                    mmi.ptMaxSize.Y = mi.rcWork.Bottom - mi.rcWork.Top;
                    Marshal.StructureToPtr(mmi, lParam, true);
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        // ── Helpers (low-risk, mechanical; replaces verbose bool-flag-on/finally pattern) ──

        /// <summary>
        /// IDisposable scope that flips <see cref="_suppressContractNumberUpdate"/>
        /// to true for the duration of a batch update and resets on dispose.
        /// Replaces the verbose bool-flag-on / try-finally-off pattern used
        /// in StartNewOrder / OpenSelectedOrder / OpenWelcomeWindow with the
        /// idiomatic <c>using (SuppressContractNumberUpdates()) { ... }</c> form.
        /// </summary>
        internal IDisposable SuppressContractNumberUpdates() =>
            new SuppressContractNumberScope(this);

        /// <summary>
        /// Sets the contract-prefix textbox under a suppress scope and then
        /// re-rolls the in-progress contract number against the new prefix.
        /// Mirrors the historical pattern from StartNewOrder / OpenSelectedOrder /
        /// OpenWelcomeWindow so prefix changes take effect everywhere.
        /// </summary>
        internal void SyncContractPrefix(string newPrefix)
        {
            using (SuppressContractNumberUpdates())
            {
                Sidebar.TxtPrefix.Text = newPrefix;
            }
            UpdateContractNumber();
        }

        private sealed class SuppressContractNumberScope : IDisposable
        {
            private readonly MainWindow _owner;
            internal SuppressContractNumberScope(MainWindow owner)
            {
                // Assign the readonly backing field BEFORE using it; an
                // expression-bodied ctor that only writes through the
                // _owner reference leaves the field declared but
                // uninitialised and triggers CS8618 under <Nullable>enable.
                _owner = owner;
                _owner._suppressContractNumberUpdate = true;
            }
            public void Dispose() =>
                _owner._suppressContractNumberUpdate = false;
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!SuppressPrefixSave)
                AppSettingsService.SaveContractPrefix(Sidebar.TxtPrefix.Text);
            if (!ViewModel.UndoRedo.IsDirty) return;
            if (Application.Current == null || Application.Current.Dispatcher.HasShutdownStarted)
                return;
            var choice = DialogService.ShowSaveDiscardCancel("В текущем расчёте есть несохранённые изменения.\nСохранить перед выходом?", owner: this);
            if (choice == Services.DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (choice == Services.DialogResult.Save)
            {
                ApplicationCommands.Save.Execute(null, this);
                if (ViewModel.UndoRedo.IsDirty)
                {
                    e.Cancel = true;
                    return;
                }
            }
        }
    }
}
