using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Helpers;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator
{
    public partial class MainWindow
    {
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

            string raw = (order.ClientAddress ?? string.Empty).Replace('/', ' ');
            string address = ViewModel.OrdersVM.SanitizeFileName(raw);
            if (!string.IsNullOrEmpty(address))
            {
                address = address.ToUpperInvariant();
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
            Dispatcher.BeginInvoke(new Action(UpdateSortIndicatorsFromSortDescriptions),
                System.Windows.Threading.DispatcherPriority.Background);
        }

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
    }
}
