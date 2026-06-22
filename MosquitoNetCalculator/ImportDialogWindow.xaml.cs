using System.Windows.Input;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator
{
    public partial class ImportDialogWindow : Window
    {
        private readonly List<OrderData> _fileOrders;
        private readonly List<CheckBox> _checkboxes = new();

        public List<OrderData> SelectedOrders { get; private set; } = new();
        public bool DialogResultOk { get; private set; }

        public ImportDialogWindow(List<OrderData> fileOrders, Window owner)
        {
            InitializeComponent();
            Owner = owner;
            _fileOrders = fileOrders;

            HeaderText.Text = $"В файле найдено {fileOrders.Count} заказов.\nОтметьте заказы для импорта:";

            foreach (var order in fileOrders)
            {
                var cb = new CheckBox
                {
                    IsChecked = true,
                    FontSize = 11.5,
                    Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary"),
                    Margin = new Thickness(0, 2, 0, 2),
                    Content = $"№{order.ContractNumber} — {order.ClientName} — {order.ClientAddress} — {MoneyFormatService.Format(order.TotalAmount)} руб. — {order.UpdatedAt:dd.MM.yy HH:mm}"
                };
                _checkboxes.Add(cb);
                ListStack.Children.Add(cb);
            }

            BtnSelectAll.Click += (_, _) =>
            {
                foreach (var cb in _checkboxes) cb.IsChecked = true;
            };

            BtnDeselectAll.Click += (_, _) =>
            {
                foreach (var cb in _checkboxes) cb.IsChecked = false;
            };
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResultOk = false;
            Close();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < _checkboxes.Count; i++)
            {
                if (_checkboxes[i].IsChecked == true)
                    SelectedOrders.Add(_fileOrders[i]);
            }
            DialogResultOk = true;
            Close();
        }
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TitleBarBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
