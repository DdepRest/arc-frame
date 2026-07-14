using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    public partial class TitleBarControl : UserControl
    {
        public Border TitleBarBorder => TitleBar;
        public Button BtnMin => BtnMinimize;
        public Button BtnMax => BtnMaximize;
        public Button BtnCls => BtnClose;
        public TextBlock MaxIcon => TxtMaximizeIcon;

        // ── ShowSettings: скрывает кнопку ⚙ (для модальных/прочих окон вроде PrintPreview).
        // Значение по умолчанию — true (полная функциональность для MainWindow).
        // Окна вроде PrintPreviewWindow выставляют ShowSettings="False" в XAML.
        public static readonly DependencyProperty ShowSettingsProperty =
            DependencyProperty.Register(
                nameof(ShowSettings),
                typeof(bool),
                typeof(TitleBarControl),
                new FrameworkPropertyMetadata(true, OnShowSettingsChanged));

        public bool ShowSettings
        {
            get => (bool)GetValue(ShowSettingsProperty);
            set => SetValue(ShowSettingsProperty, value);
        }

        private static void OnShowSettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TitleBarControl tc && tc.BtnSettingsGear != null)
                tc.BtnSettingsGear.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }

        private Action _themeChangedHandler = null!;

        public TitleBarControl()
        {
            InitializeComponent();
            // Apply ShowSettings at apply-time (covers InitialiseComponent-time defaults).
            BtnSettingsGear.Visibility = ShowSettings ? Visibility.Visible : Visibility.Collapsed;
            if (ShowSettings)
            {
                UpdateSettingsMenu();
                _themeChangedHandler = UpdateSettingsMenu;
                ThemeService.ThemeChanged += _themeChangedHandler;
                Unloaded += (_, _) => ThemeService.ThemeChanged -= _themeChangedHandler;

                var badgeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                badgeTimer.Tick += (s, e) => { badgeTimer.Stop(); RefreshUpdateBadge(); };
                badgeTimer.Start();
            }
        }

        public void UpdateSettingsMenu()
        {
            bool isDark = ThemeService.IsDarkTheme;
            if (MenuThemeLight != null) MenuThemeLight.IsChecked = !isDark;
            if (MenuThemeDark != null) MenuThemeDark.IsChecked = isDark;
            RefreshUpdateBadge();
        }

        public void RefreshUpdateBadge()
        {
            var badge = BtnSettingsGear?.Template?.FindName("UpdateBadge", BtnSettingsGear) as System.Windows.Shapes.Ellipse;
            if (badge != null)
                badge.Visibility = UpdateService.HasPendingUpdate() ? Visibility.Visible : Visibility.Collapsed;
        }

        private Window? GetParentWindow() => Window.GetWindow(this);

        private MainWindow? TryGetMainWindow()
        {
            if (DataContext is MainWindow mw) return mw;
            System.Diagnostics.Trace.WriteLine($"[TitleBarControl] Cannot resolve MainWindow.");
            return null;
        }

        private void BtnSettingsGear_Click(object sender, RoutedEventArgs e)
        {
            UpdateSettingsMenu();
            SettingsMenu.PlacementTarget = BtnSettingsGear;
            SettingsMenu.Placement = PlacementMode.Bottom;
            SettingsMenu.IsOpen = true;
        }

        private void MenuThemeLight_Click(object sender, RoutedEventArgs e)
        {
            if (ThemeService.IsDarkTheme) ThemeService.ToggleTheme();
        }

        private void MenuThemeDark_Click(object sender, RoutedEventArgs e)
        {
            if (!ThemeService.IsDarkTheme) ThemeService.ToggleTheme();
        }

        private void MenuChangeLocation_Click(object sender, RoutedEventArgs e)
        {
            TryGetMainWindow()?.OpenWelcomeWindow();
        }

        private async void MenuCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            var mw = TryGetMainWindow();
            if (mw == null) return;
            await UpdateService.CheckAndApplyAsync(mw);
            UpdateSettingsMenu();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var win = GetParentWindow();
            if (win == null) return;
            if (e.ClickCount == 2) { ToggleMaximize(); e.Handled = true; return; }
            if (win.WindowState == WindowState.Maximized)
            {
                Point mousePos = win.PointToScreen(e.GetPosition(win));
                win.WindowState = WindowState.Normal;
                win.Left = mousePos.X - (win.ActualWidth / 2);
                win.Top = mousePos.Y - (TitleBar.ActualHeight / 2);
            }
            win.DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            var win = GetParentWindow();
            if (win != null) win.WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

        private void BtnClose_Click(object sender, RoutedEventArgs e) => GetParentWindow()?.Close();

        private void ToggleMaximize()
        {
            var win = GetParentWindow();
            if (win == null) return;
            win.WindowState = win.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }
}
