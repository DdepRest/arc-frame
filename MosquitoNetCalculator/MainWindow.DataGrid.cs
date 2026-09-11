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
                // v3.50: delete button reveals on row hover (prototype .del).
                // Best-effort cosmetic: the button template stays untouched.
                row.MouseEnter += RevealDeleteRow_MouseEnter;
                row.MouseLeave += RevealDeleteRow_MouseLeave;
            }
            else
            {
                row.MouseEnter -= OrderRow_MouseEnter;
                row.MouseLeave -= OrderRow_MouseLeave;
                row.MouseEnter -= RevealDeleteRow_MouseEnter;
                row.MouseLeave -= RevealDeleteRow_MouseLeave;
            }
        }

        // v3.50: finds the delete button in the row visual tree and fades it
        // in/out. WPF Triggers can't reach "row is hovered" from inside a cell
        // template cheaply, and the existing hover infrastructure is already here.
        private static void RevealDeleteRow_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not DataGridRow row) return;
            var btn = FindChildByName<Button>(row, "DelBtn");
            if (btn != null)
                btn.BeginAnimation(UIElement.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        }

        private static void RevealDeleteRow_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is not DataGridRow row) return;
            var btn = FindChildByName<Button>(row, "DelBtn");
            if (btn != null && !btn.IsMouseOver)
                btn.BeginAnimation(UIElement.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
        }

        private static T? FindChildByName<T>(DependencyObject parent, string name) where T : class
        {
            if (parent == null) return null;
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typed && (child as FrameworkElement)?.Name == name) return typed;
                var deep = FindChildByName<T>(child, name);
                if (deep != null) return deep;
            }
            return null;
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
