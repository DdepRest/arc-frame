using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MosquitoNetCalculator.Controls
{
    public partial class TitleBarControl : UserControl
    {
        public Border TitleBarBorder => TitleBar;
        public Button BtnMin => BtnMinimize;
        public Button BtnMax => BtnMaximize;
        public Button BtnCls => BtnClose;
        public TextBlock MaxIcon => TxtMaximizeIcon;

        public TitleBarControl()
        {
            InitializeComponent();
        }

        private Window? GetParentWindow() => Window.GetWindow(this);

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var win = GetParentWindow();
            if (win == null) return;

            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                e.Handled = true;
                return;
            }

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

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            GetParentWindow()?.Close();
        }

        private void ToggleMaximize()
        {
            var win = GetParentWindow();
            if (win == null) return;

            win.WindowState = win.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }
}
