using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

using PrintSettings = MosquitoNetCalculator.Models.PrintSettings;

namespace MosquitoNetCalculator
{
    public partial class MainWindow
    {
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

                // Reset print settings for the new order (in-memory only, not serialized)
                LastPrintSettings = new PrintSettings();

                ViewModel.CalcVM.UnsubscribeAll(UpdateTotal);
                ViewModel.CalcVM.ClearAll();

                UpdateTotal();
                UpdateCurrentOrderInfo();
            });
            MarkClean();
            ViewModel.UndoRedo.Clear();
            UpdateUndoRedoHint();
        }

        internal void BtnSaveOrder_Click(object sender, RoutedEventArgs e)
        {
            ActionBarControl.BtnSaveOrder_Click(sender, e);
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
                Width = 50,
                Height = 24,
                FontSize = 11,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };

            // v3.46.1: +/- toggle for all modes.
            // Placed BEFORE the amount field: [+/−] [500] ₽.
            var chkSign = new CheckBox
            {
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Отмечено = добавить к сумме (+), не отмечено = вычесть из суммы (−)"
            };
            chkSign.SetResourceReference(FrameworkElement.StyleProperty, "SignToggleCheckBox");
            chkSign.Click += (_, _) =>
            {
                CommitDeductionIfPending();
                RefreshDeductionField();
            };

            void RefreshDeductionField()
            {
                bool applicable = item.IsInstallationApplicable;
                txtDeduction.IsEnabled = applicable;
                if (applicable)
                {
                    // v3.46.1: all modes show absolute amount + sign checkbox.
                    // Convention: positive = add (+), negative = subtract (−).
                    double currentVal = item.CurrentInstallationAmount;
                    double absVal = Math.Abs(currentVal);
                    txtDeduction.Text = absVal > 0.01 ? absVal.ToString("F0") : "0";
                    bool isAdd = currentVal >= 0;
                    chkSign.Visibility = Visibility.Visible;
                    chkSign.IsChecked = isAdd;
                    txtDeduction.ToolTip = "Введите сумму корректировки и выберите знак (+ добавить, − вычесть).";
                }
                else
                {
                    txtDeduction.Text = "0";
                    chkSign.Visibility = Visibility.Collapsed;
                    txtDeduction.ToolTip = "Не применяется в этом режиме";
                }
            }

            void CommitDeductionIfPending()
            {
                if (!txtDeduction.IsEnabled) return;
                if (double.TryParse(txtDeduction.Text, out double absVal) && absVal >= 0)
                {
                    // v3.46.1: all modes use signed convention (+ = add, − = subtract).
                    double val = chkSign.IsChecked == true ? absVal : -absVal;
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
                Margin = new Thickness(0, 0, 4, 0),
                FontSize = 12
            });
            txtDeduction.LostFocus += (_, _) =>
            {
                CommitDeductionIfPending();
                RefreshDeductionField();
            };
            txtDeduction.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    CommitDeductionIfPending();
                    RefreshDeductionField();
                    menu.IsOpen = false;
                }
            };
            deductionPanel.Children.Add(chkSign);
            deductionPanel.Children.Add(txtDeduction);
            deductionPanel.Children.Add(new TextBlock
            {
                Text = "₽",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0),
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
    }
}
