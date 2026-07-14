using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator
{
    public partial class MainWindow
    {
        /// <summary>
        /// Attaches or detaches hover-animation event handlers for a DataGrid row.
        /// Replaces the 6 identical LoadingRow/UnloadingRow pairs with one call site.
        /// </summary>
        internal static void AttachRowHover(DataGridRow row, bool attach)
        {
            if (attach)
            {
                row.Background = new SolidColorBrush(Colors.Black) { Opacity = 0 };
                row.MouseEnter += OrderRow_MouseEnter;
                row.MouseLeave += OrderRow_MouseLeave;
            }
            else
            {
                row.MouseEnter -= OrderRow_MouseEnter;
                row.MouseLeave -= OrderRow_MouseLeave;
            }
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

        // v3.43.3: УДАЛЁН Grid_PreviewMouseLeftButtonDown — он ломал видимый
        // тогл чекбокса:
        //   1. Событие срабатывало ДО Toggle, сразу вызывало item.IsActive = !item.IsActive
        //      и ставило e.Handled=true → WPF НЕ переключал CheckBox.IsChecked визуально.
        //   2. Юзер кликал → CheckBox визуально оставался в прежнем состоянии →
        //      казалось, что «тумблер не работает», хотя IsActive в модели реально менялся.
        // Естественный two-way binding IsChecked="{Binding IsActive, UpdateSourceTrigger=PropertyChanged}"
        // (см. OrderItemsControl.xaml) сам корректно: клик → IsChecked flip → binding Push → IsActive setter
        // → RecalculateRequested → UpdateTotal. Никаких ручных toggle-ов больше не нужно.
    }
}
