using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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

        public MainWindow()
        {
            InitializeComponent();

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

            Closed += (_, _) => UpdateService.ProgressChanged -= OnUpdateProgressChanged;

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

        private Storyboard? _progressBarStoryboard;

        private void OnProgressBarFadeOutCompleted(object? sender, EventArgs e)
        {
            if (!UpdateService.IsDownloading && UpdateDownloadBar != null)
                UpdateDownloadBar.Visibility = Visibility.Collapsed;
        }

        private void OnUpdateProgressChanged(object? sender, EventArgs e)
        {
            if (UpdateDownloadBar == null) return;

            UpdateDownloadBar.Value = UpdateService.DownloadProgress;

            bool shouldBeVisible = UpdateService.IsDownloading;
            bool currentlyVisible = UpdateDownloadBar.Visibility == Visibility.Visible;

            if (shouldBeVisible == currentlyVisible)
                return; // No state change — avoid re-triggering animation

            if (_progressBarStoryboard != null)
            {
                _progressBarStoryboard.Completed -= OnProgressBarFadeOutCompleted;
                _progressBarStoryboard.Stop(UpdateDownloadBar);
            }

            if (shouldBeVisible)
            {
                // Fade in — storyboard defined in XAML (UpdateBarFadeIn)
                UpdateDownloadBar.Visibility = Visibility.Visible;
                UpdateDownloadBar.Opacity = 0;

                _progressBarStoryboard = ((Storyboard)FindResource("UpdateBarFadeIn")).Clone();
            }
            else
            {
                // Fade out — storyboard defined in XAML (UpdateBarFadeOut).
                // From-value is set dynamically so the fade starts from the
                // bar's current opacity (handles interruption mid-fade-in).
                _progressBarStoryboard = ((Storyboard)FindResource("UpdateBarFadeOut")).Clone();
                if (_progressBarStoryboard.Children.OfType<DoubleAnimation>().FirstOrDefault() is { } fadeOutAnim)
                    fadeOutAnim.From = UpdateDownloadBar.Opacity;
                _progressBarStoryboard.Completed += OnProgressBarFadeOutCompleted;
            }

            _progressBarStoryboard.Begin(UpdateDownloadBar);
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
                OrderItems.Select(i => i.IsAmountOnly ? "" : i.Color));
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Ширина"), "Ширина",
                OrderItems.Select(i => i.IsAmountOnly ? "" : i.Width > 0 ? $"{i.Width:F0} мм" : ""));
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Высота"), "Высота",
                OrderItems.Select(i => i.IsAmountOnly ? "" : i.Height > 0 ? $"{i.Height:F0} мм" : ""));
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Монтаж"), "Монтаж",
                OrderItems.Select(i => i.InstallationDisplay),
                contentPad: 40, contentWeight: FontWeights.Bold, contentFontSize: 14);
            DataGridColumnAutoSizer.SetColumnMinWidth(grid, DataGridColumnAutoSizer.FindCol(grid, "Кол-во"), "Кол-во",
                OrderItems.Select(i => i.IsAmountOnly ? "" : i.Quantity > 0 ? i.Quantity.ToString() : ""),
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


    }
}
