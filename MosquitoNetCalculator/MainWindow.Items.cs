using System.Collections.Generic;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Helpers;

using PrintSettings = MosquitoNetCalculator.Models.PrintSettings;

namespace MosquitoNetCalculator
{
    public partial class MainWindow
    {
        /// <summary>
        /// Converts the amount entered in the installation popup to the signed
        /// value used by the calculation model. The explicit minus in the text
        /// wins over the toggle state; otherwise the toggle supplies the sign.
        /// </summary>
        internal static double NormalizeInstallationAmount(double rawValue, bool isAdd)
        {
            if (!double.IsFinite(rawValue))
                throw new ArgumentOutOfRangeException(nameof(rawValue), "Installation amount must be finite.");

            // Avoid Math.Abs(double.MinValue) overflow while keeping the
            // popup's normal numeric-input path exception-free.
            if (rawValue == double.MinValue)
                return -double.MaxValue;

            double absoluteValue = Math.Abs(rawValue);
            return isAdd && rawValue >= 0 ? absoluteValue : -absoluteValue;
        }

        internal static bool ShouldRefreshInstallationSign(double currentValue)
            => Math.Abs(currentValue) > 0.01;

        internal void StartNewOrder(bool discardDraft = true)
        {
            ViewModel.UndoRedo.SuppressDirtyChanges(() =>
            {
                CurrentOrderId = Guid.NewGuid().ToString();
                IsNewOrder = true;

                ClientInfo.ContractDate = DateTime.Today;
                SyncContractPrefix(AppSettingsService.LoadContractPrefix());

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
            if (discardDraft)
            {
                DraftService.DiscardDraft();
                DraftBanner?.HideBanner();
            }
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

                // v3.50: undo toast — the push above means Ctrl+Z/«Отменить» restores
                // the row; the toast surfaces that path (prototype .toast with .tact).
                ToastService.ShowToast(
                    $"Позиция «{item.DisplayName}» удалена",
                    ToastType.Info,
                    "Отменить",
                    () => Undo());
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
                Style = (Style)FindResource(typeof(ContextMenu)),
                MinWidth = 400,
                MaxWidth = 460,
                Padding = new Thickness(8)
            };

            var staticItemTemplate = CreateTransparentMenuItemTemplate();

            // ─── Mode Items: order & styling unified with InstallationChoiceDialog ───
            var (sub0, sub2, sub1) = GetModeSubtitles(item);

            var miMode0 = CreateInstallationModeMenuItem(0, "Монтаж включён", sub0, item.InstallationMode == 0);
            var miMode2 = CreateInstallationModeMenuItem(2, "В конструкцию", sub2, item.InstallationMode == 2);
            var miMode1 = CreateInstallationModeMenuItem(1, "Без монтажа", sub1, item.InstallationMode == 1);

            var txtDeduction = new TextBox
            {
                Width = 76,
                Height = 28,
                FontSize = Typography.Body,
                TextAlignment = TextAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 0, 6, 0),
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Введите доплату (+) или скидку (−), например: +200 или -150. Нажмите Enter для сохранения."
            };
            txtDeduction.PreviewMouseLeftButtonDown += (sender, e) =>
            {
                var tb = (TextBox)sender;
                tb.Focus();
                Keyboard.Focus(tb);
                tb.SelectAll();
                e.Handled = true;
            };
            txtDeduction.GotKeyboardFocus += (sender, e) =>
            {
                ((TextBox)sender).SelectAll();
            };

            void RefreshDeductionField()
            {
                bool applicable = item.IsInstallationApplicable;
                txtDeduction.IsEnabled = applicable;
                if (applicable)
                {
                    double currentVal = item.CurrentInstallationAmount;
                    if (Math.Abs(currentVal) > 0.01)
                        txtDeduction.Text = currentVal > 0 ? $"+{currentVal:F0}" : $"{currentVal:F0}";
                    else
                        txtDeduction.Text = "0";
                    txtDeduction.ToolTip = "Введите доплату (+) или скидку (−), например: +200 или -150. Нажмите Enter для сохранения.";
                }
                else
                {
                    txtDeduction.Text = "0";
                    txtDeduction.ToolTip = "Не применяется в этом режиме";
                }
            }

            void CommitDeductionIfPending()
            {
                if (!txtDeduction.IsEnabled) return;
                string rawText = (txtDeduction.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(rawText)) return;

                bool hasExplicitMinus = rawText.StartsWith("-") || rawText.StartsWith("−");
                string cleanText = rawText.TrimStart('+', '-', '−', ' ').Trim();

                if (MoneyFormatService.TryParse(cleanText, out double numVal) && double.IsFinite(numVal))
                {
                    double finalVal = hasExplicitMinus ? -Math.Abs(numVal) : Math.Abs(numVal);
                    if (Math.Abs(item.CurrentInstallationAmount - finalVal) > 0.01)
                    {
                        item.SetCurrentInstallationAmount(finalVal);
                        _lastSumEditTime = DateTime.Now;
                        MarkDirty();
                    }
                }
            }

            RefreshDeductionField();

            void SetModeAndClose(int mode)
            {
                PushUndo();
                CommitDeductionIfPending();
                item.InstallationMode = mode;

                if (mode == 0 && Math.Abs(item.InstallationAdjustment) < 0.01)
                    item.InstallationAdjustment = OrderItem.GetDefaultInstallationAdjustment(item.Name);
                else if (mode == 2 && Math.Abs(item.InstallationSurcharge) < 0.01)
                    item.InstallationSurcharge = OrderItem.GetDefaultInstallationSurcharge(item.Name);

                menu.IsOpen = false;
            }

            void FocusDeductionField()
            {
                txtDeduction.Focus();
                Keyboard.Focus(txtDeduction);
                if (txtDeduction.IsEnabled)
                    txtDeduction.SelectAll();
            }

            miMode0.Click += (_, _) => SetModeAndClose(0);
            miMode2.Click += (_, _) => SetModeAndClose(2);
            miMode1.Click += (_, _) => SetModeAndClose(1);

            menu.Items.Add(miMode0);
            menu.Items.Add(miMode2);
            menu.Items.Add(miMode1);

            menu.Items.Add(new Separator());

            var deductionItem = new MenuItem
            {
                StaysOpenOnClick = true,
                Focusable = false,
                FocusVisualStyle = null,
                Template = staticItemTemplate,
                Padding = Spacings.None
            };

            var deductionGrid = new Grid
            {
                Margin = new Thickness(8, 6, 8, 6)
            };
            deductionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            deductionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            deductionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string unitSuffix = item.IsInstallationPerLinearMeter ? "₽/м.п." : "₽";
            var lblCaption = new TextBlock
            {
                Text = $"Корректировка, {unitSuffix}:",
                FontSize = Typography.Body,
                Foreground = (Brush)FindResource("TextSecondary"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(lblCaption, 0);
            deductionGrid.Children.Add(lblCaption);

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
                    args.Handled = true;
                }
            };

            Grid.SetColumn(txtDeduction, 1);
            deductionGrid.Children.Add(txtDeduction);

            var btnApply = new Button
            {
                Height = 28,
                Padding = new Thickness(14, 0, 14, 0),
                Cursor = Cursors.Hand,
                Content = "Применить",
                ToolTip = "Сохранить корректировку и закрыть (Enter)"
            };
            btnApply.SetResourceReference(FrameworkElement.StyleProperty, "DialogPrimaryButton");
            btnApply.Click += (_, _) =>
            {
                CommitDeductionIfPending();
                RefreshDeductionField();
                menu.IsOpen = false;
            };
            Grid.SetColumn(btnApply, 2);
            deductionGrid.Children.Add(btnApply);

            deductionItem.Header = deductionGrid;
            menu.Items.Add(deductionItem);

            menu.Opened += (_, _) =>
            {
                bool recentlyEdited = _lastSumEditTime.HasValue
                    && DateTime.Now - _lastSumEditTime.Value < SmartFocusWindow;
                if (recentlyEdited && txtDeduction.IsEnabled)
                    txtDeduction.Dispatcher.BeginInvoke(FocusDeductionField);
            };

            menu.PreviewKeyDown += (_, args) =>
            {
                if (txtDeduction.IsFocused || txtDeduction.IsKeyboardFocusWithin)
                    return;

                if (args.Key == Key.D1 || args.Key == Key.NumPad1)
                {
                    SetModeAndClose(0);
                    args.Handled = true;
                }
                else if (args.Key == Key.D2 || args.Key == Key.NumPad2)
                {
                    SetModeAndClose(2);
                    args.Handled = true;
                }
                else if (args.Key == Key.D3 || args.Key == Key.NumPad3)
                {
                    SetModeAndClose(1);
                    args.Handled = true;
                }
            };

            menu.PlacementTarget = btn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private static ControlTemplate CreateTransparentMenuItemTemplate()
        {
            var cpFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            cpFactory.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            return new ControlTemplate(typeof(MenuItem))
            {
                VisualTree = cpFactory
            };
        }

        /// <summary>
        /// Пункт меню режима монтажа: чистая карточка с радио-индикатором слева,
        /// заголовком, описанием и тихим номером клавиши (1 / 2 / 3) справа.
        /// Закрывается сразу при выборе в 1 клик.
        /// </summary>
        private MenuItem CreateInstallationModeMenuItem(int mode, string title, string subtitle, bool isChecked)
        {
            var mi = new MenuItem
            {
                Tag = mode,
                IsCheckable = true,
                IsChecked = isChecked,
                StaysOpenOnClick = false,
                Padding = Spacings.None,
                FocusVisualStyle = null,
                Template = CreateTransparentMenuItemTemplate()
            };

            var card = new Border
            {
                CornerRadius = Radii.Control,
                BorderThickness = new Thickness(1),
                BorderBrush = isChecked ? (Brush)FindResource("Accent") : (Brush)FindResource("Border"),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(4, 3, 4, 3),
                Background = isChecked ? (Brush)FindResource("AccentLight") : (Brush)FindResource("Surface"),
                Cursor = Cursors.Hand,
                FocusVisualStyle = null
            };

            card.MouseEnter += (_, _) =>
            {
                if (!mi.IsChecked)
                {
                    card.Background = (Brush)FindResource("RowHover");
                    card.BorderBrush = (Brush)FindResource("BorderHover");
                }
                else
                {
                    card.Opacity = 0.92;
                }
            };
            card.MouseLeave += (_, _) =>
            {
                if (!mi.IsChecked)
                {
                    card.Background = (Brush)FindResource("Surface");
                    card.BorderBrush = (Brush)FindResource("Border");
                }
                else
                {
                    card.Background = (Brush)FindResource("AccentLight");
                    card.BorderBrush = (Brush)FindResource("Accent");
                    card.Opacity = 1.0;
                }
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string keycapText = mode switch
            {
                0 => "1",
                2 => "2",
                _ => "3"
            };

            var radioIndicator = new Border
            {
                Tag = "RadioIndicator",
                Width = 16,
                Height = 16,
                CornerRadius = Radii.Pill,
                BorderThickness = new Thickness(1.5),
                BorderBrush = isChecked ? (Brush)FindResource("Accent") : (Brush)FindResource("Border"),
                Background = isChecked ? (Brush)FindResource("Accent") : Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Child = new Ellipse
                {
                    Tag = "RadioDot",
                    Width = 6,
                    Height = 6,
                    Fill = (Brush)FindResource("OnAccent"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed
                }
            };
            Grid.SetColumn(radioIndicator, 0);
            grid.Children.Add(radioIndicator);

            var textStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            textStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = Typography.Body,
                FontWeight = isChecked ? FontWeights.SemiBold : FontWeights.Medium,
                Foreground = (Brush)FindResource("TextPrimary")
            });
            textStack.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = Typography.Caption,
                Foreground = (Brush)FindResource("TextMuted"),
                TextWrapping = TextWrapping.Wrap,
                Margin = Spacings.BlockTopSm
            });
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            var hotkeyText = new TextBlock
            {
                Text = keycapText,
                FontSize = 11,
                Opacity = 0.4,
                Foreground = (Brush)FindResource("TextMuted"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(hotkeyText, 2);
            grid.Children.Add(hotkeyText);

            card.Child = grid;
            mi.Header = card;
            return mi;
        }

        private void UpdateModeItemVisual(MenuItem mi, bool isChecked)
        {
            mi.IsChecked = isChecked;

            if (mi.Header is not Border card || card.Child is not Grid grid) return;

            card.Background = isChecked ? (Brush)FindResource("AccentLight") : (Brush)FindResource("Surface");
            card.BorderThickness = new Thickness(1);
            card.BorderBrush = isChecked ? (Brush)FindResource("Accent") : (Brush)FindResource("Border");
            card.Opacity = 1.0;

            if (grid.Children.OfType<Border>().FirstOrDefault(b => Equals(b.Tag, "RadioIndicator")) is Border radio)
            {
                radio.BorderBrush = isChecked ? (Brush)FindResource("Accent") : (Brush)FindResource("Border");
                radio.Background = isChecked ? (Brush)FindResource("Accent") : Brushes.Transparent;
                if (radio.Child is Ellipse dot)
                {
                    dot.Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            if (grid.Children.OfType<StackPanel>().FirstOrDefault() is StackPanel stack
                && stack.Children.OfType<TextBlock>().FirstOrDefault() is TextBlock title)
            {
                title.FontWeight = isChecked ? FontWeights.SemiBold : FontWeights.Medium;
                title.Foreground = (Brush)FindResource("TextPrimary");
            }
        }

        private static (string sub0, string sub2, string sub1) GetModeSubtitles(OrderItem itm)
        {
            string unitStr = itm.IsInstallationPerLinearMeter ? "руб./м.п." : "руб.";

            if (itm.IsInstallationPerLinearMeter)
            {
                double baseRate = OrderItem.GetBaseInstallationRate(itm.Name);
                string sub2Text = itm.InstallationSurcharge > 0
                    ? $"{itm.DisplayName} монтируется в конструкцию (+{MoneyFormatService.FormatWhole(itm.InstallationSurcharge)} {unitStr})"
                    : $"{itm.DisplayName} монтируется при сборке конструкции (0 {unitStr})";

                return (
                    $"Монтаж рассчитывается отдельно (+{MoneyFormatService.FormatWhole(baseRate)} {unitStr} к стоимости и ЗП)",
                    sub2Text,
                    $"Клиент монтирует самостоятельно (0 {unitStr})"
                );
            }

            double labor0 = OrderItem.GetDefaultInstallationLaborRate(itm.Name, 0);
            double ded1 = Math.Abs(OrderItem.GetDefaultInstallationDeduction(itm.Name));
            double labor2 = OrderItem.GetDefaultInstallationLaborRate(itm.Name, 2);

            return (
                $"Монтаж заложен в стоимость (+{MoneyFormatService.FormatWhole(labor0)} {unitStr} к ЗП)",
                $"Сетка ставится в раму при сборке конструкции (+{MoneyFormatService.FormatWhole(labor2)} {unitStr} к ЗП)",
                $"Клиент монтирует самостоятельно (−{MoneyFormatService.FormatWhole(ded1)} {unitStr})"
            );
        }
            internal void DeleteOrderItems(List<OrderItem> items)
        {
            if (items == null || items.Count == 0) return;

            PushUndo();
            foreach (var item in items)
            {
                item.RecalculateRequested -= UpdateTotal;
                ViewModel.CalcVM.DeleteItem(item);
            }

            UpdateTotal();
            UpdateEmptyState();

            string msg = items.Count == 1
                ? $"Товар «{items[0].DisplayName}» удалён"
                : $"Удалено позиций: {items.Count}";

            ToastService.ShowToast(
                msg,
                ToastType.Info,
                "Отменить",
                () => Undo());
        }

        internal void BatchChangeColor(List<OrderItem> items, string newColor)
        {
            if (items == null || items.Count == 0 || string.IsNullOrEmpty(newColor)) return;

            PushUndo();
            int modified = 0;
            foreach (var item in items)
            {
                if (item.Color != newColor)
                {
                    item.Color = newColor;
                    modified++;
                }
            }

            if (modified > 0)
            {
                UpdateTotal();
                ToastService.ShowToast($"Цвет изменён на «{newColor}» ({modified} поз.)", ToastType.Success);
            }
        }

        internal void BatchChangeInstallation(List<OrderItem> items, int mode)
        {
            if (items == null || items.Count == 0) return;

            PushUndo();
            int modified = 0;
            foreach (var item in items)
            {
                if (item.IsInstallationApplicable && item.InstallationMode != mode)
                {
                    item.InstallationMode = mode;
                    modified++;
                }
            }

            if (modified > 0)
            {
                UpdateTotal();
                string modeName = mode == 0 ? "Учтён" : (mode == 2 ? "В конструкцию" : "Без монтажа");
                ToastService.ShowToast($"Режим монтажа: «{modeName}» ({modified} поз.)", ToastType.Success);
            }
        }

        internal void BatchToggleActive(List<OrderItem> items)
        {
            if (items == null || items.Count == 0) return;

            PushUndo();
            // If all are active -> deactivate all, otherwise activate all
            bool targetState = !items.All(i => i.IsActive);
            foreach (var item in items)
            {
                item.IsActive = targetState;
            }

            UpdateTotal();
            string stateMsg = targetState ? "включены" : "выключены";
            ToastService.ShowToast($"Выбранные позиции {stateMsg} ({items.Count} поз.)", ToastType.Info);
        }
    }
}