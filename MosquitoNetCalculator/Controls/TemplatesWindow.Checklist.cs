using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MosquitoNetCalculator.Helpers;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// Окно «Шаблоны». Часть 2: Построение строк чек-листа, полей ввода и контролов.
    /// </summary>
    public partial class TemplatesWindow
    {
        private void BuildChecklist()
        {
            ChecklistPanel.Children.Clear();
            _widthBoxes.Clear();
            _heightBoxes.Clear();
            _qtyBoxes.Clear();
            _amountBoxes.Clear();
            _rowToggles.Clear();
            _statusPills.Clear();
            _rowPriceLabels.Clear();
            _fieldPanelsByProduct.Clear();
            _gridTopPanel = null;

            if (_template == null) return;
            int stepNumber = 0;

            foreach (var row in _template.Rows)
            {
                stepNumber++;
                var rowState = _state.GetOrCreateRowState(row);
                bool enabled = rowState.IsEnabled;

                // Шапка строки: [switch] [номер] [название+подсказка] ... [цена блока] [статус-пилюля]
                var header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 0: toggle
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 1: step
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 2: title
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 3: price
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 4: status pill

                // Переключатель строки
                var toggle = new CheckBox
                {
                    IsChecked = enabled,
                    IsEnabled = !row.IsCheckedLocked,
                    VerticalAlignment = VerticalAlignment.Center,
                    FocusVisualStyle = (Style)FindResource("FluentFocusVisual")
                };
                toggle.Checked += (_, _) => OnRowToggled(row.RowKey, true);
                toggle.Unchecked += (_, _) => OnRowToggled(row.RowKey, false);
                _rowToggles[row.RowKey] = toggle;
                header.Children.Add(toggle); // Col 0

                // Номер шага — контрастный значок этапа (24×24 акцентный круг)
                var stepBadge = new Border
                {
                    Width = 24,
                    Height = 24,
                    CornerRadius = (CornerRadius)FindResource("Radius.Pill"),
                    Background = (Brush)FindResource("Accent"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0),
                    Child = new TextBlock
                    {
                        Text = stepNumber.ToString(),
                        FontSize = Typography.Caption,
                        FontWeight = FontWeights.Bold,
                        Foreground = (Brush)FindResource("OnAccentPrimary"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                Grid.SetColumn(stepBadge, 1);
                header.Children.Add(stepBadge);

                // Заголовок и пояснение
                var titleStack = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                var titleText = !string.IsNullOrEmpty(row.Title) ? row.Title : row.ProductName;
                var title = new TextBlock
                {
                    Text = titleText,
                    FontSize = Typography.Body,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextPrimary")
                };
                titleStack.Children.Add(title);
                if (!string.IsNullOrEmpty(row.Hint))
                {
                    titleStack.Children.Add(new TextBlock
                    {
                        Text = row.Hint,
                        FontSize = Typography.Caption,
                        Foreground = (Brush)FindResource("TextMuted"),
                        Margin = new Thickness(0, 1, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                Grid.SetColumn(titleStack, 2);
                header.Children.Add(titleStack);

                // Стоимость конкретного блока (выводится жирно перед статусом)
                var priceLabel = new TextBlock
                {
                    FontSize = Typography.Body,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("Accent"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 10, 0)
                };
                Grid.SetColumn(priceLabel, 3);
                header.Children.Add(priceLabel);
                _rowPriceLabels[row.RowKey] = priceLabel;

                // Статус-пилюля («Добавится» / «Не требуется»)
                var statusPill = new Border
                {
                    CornerRadius = (CornerRadius)FindResource("Radius.Capsule"),
                    Padding = new Thickness(10, 2, 10, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0),
                    Child = new TextBlock
                    {
                        FontSize = Typography.Caption,
                        TextAlignment = TextAlignment.Center
                    }
                };
                Grid.SetColumn(statusPill, 4);
                header.Children.Add(statusPill);
                _statusPills[row.RowKey] = statusPill;

                // Поля строки
                var fields = new StackPanel { Margin = new Thickness(48, 6, 4, 2) };
                _fieldPanelsByProduct[row.RowKey] = fields;

                if (row.ProductName == OrderTemplateService.OrderTemplateGridProduct)
                {
                    BuildGridFields(row, fields);
                }
                else if (row.ProductName is "Отлив" or "Козырёк")
                {
                    BuildLinearMeterFields(row, fields);
                }
                else if (row.ProductName == "ПСУЛ")
                {
                    BuildPsulFields(row, fields);
                }
                else
                {
                    BuildManualAmountFields(row, fields);
                }

                var rowCard = new Border
                {
                    Background = (Brush)FindResource("GhostBg"),
                    BorderBrush = (Brush)FindResource("SubtleBorder"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = (CornerRadius)FindResource("Radius.Md"),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 8),
                    Child = new StackPanel { Children = { header, fields } }
                };
                AutomationProperties.SetName(rowCard, $"Позиция шаблона {title.Text}");
                ChecklistPanel.Children.Add(rowCard);

                ApplyRowEnabledState(row.RowKey, enabled);
            }

            RefreshSummary();
        }

        // ── Строка «Сетка» ──
        private void BuildGridFields(OrderTemplateService.TemplateRow row, StackPanel fields)
        {
            var rowState = _state.GetOrCreateRowState(row);

            _gridTopPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            RebuildGridTopFieldsFor(row, _gridTopPanel);
            fields.Children.Add(_gridTopPanel);

            var dims = MakeDimsGrid();
            AddDimField(dims, 0, "Ширина, мм", v => rowState.Width = v, row.RowKey);
            AddDimField(dims, 2, "Высота, мм", v => rowState.Height = v, row.RowKey);
            AddQtyField(dims, 4, "Кол-во", v => rowState.Quantity = v, row.RowKey, (int)rowState.Quantity);
            fields.Children.Add(dims);
        }

        internal void RebuildGridTopFields() =>
            RebuildGridTopFields(_ => _mw?.PricesVM?.GetColorsForProduct(_state.RowStates["grid"].SelectedProductType));

        internal void RebuildGridTopFields(Func<string, List<string>?> colorSource)
        {
            if (_gridTopPanel == null) return;
            var windowTemplate = _template ?? OrderTemplateService.All[0];
            var gridRow = windowTemplate.Rows.FirstOrDefault(r => r.ProductName == OrderTemplateService.OrderTemplateGridProduct)
                          ?? windowTemplate.Rows[0];
            RebuildGridTopFieldsFor(gridRow, _gridTopPanel, colorSource);
        }

        private void RebuildGridTopFieldsFor(OrderTemplateService.TemplateRow row, StackPanel panel, Func<string, List<string>?>? colorSource = null)
        {
            colorSource ??= t => _mw?.PricesVM?.GetColorsForProduct(t);
            panel.Children.Clear();
            var rowState = _state.GetOrCreateRowState(row);

            var grid = MakeFieldGrid();

            // Выбор типа сетки: ComboBox со всеми 4 вариантами сеток (пользователь может сменить при необходимости)
            var cmbType = new ComboBox { MinWidth = 170, Margin = new Thickness(0, 0, 12, 6) };
            foreach (var t in OrderTemplateService.GridProductChoices) cmbType.Items.Add(t);

            int initialIndex = rowState.ProductIndex;
            if (initialIndex < 0 || initialIndex >= cmbType.Items.Count)
            {
                if (!string.IsNullOrEmpty(rowState.SelectedProductType))
                    initialIndex = Array.IndexOf(OrderTemplateService.GridProductChoices, rowState.SelectedProductType);
                if (initialIndex < 0 && !string.IsNullOrEmpty(row.DefaultProductType))
                    initialIndex = Array.IndexOf(OrderTemplateService.GridProductChoices, row.DefaultProductType);
                if (initialIndex < 0) initialIndex = 0;
            }

            cmbType.SelectedIndex = initialIndex;
            rowState.ProductIndex = cmbType.SelectedIndex;
            rowState.SelectedProductType = cmbType.SelectedItem as string ?? OrderTemplateService.GridProductChoices[0];

            cmbType.SelectionChanged += (_, _) =>
            {
                rowState.ProductIndex = cmbType.SelectedIndex;
                rowState.SelectedProductType = cmbType.SelectedItem as string ?? "";
                RebuildGridTopFieldsFor(row, panel, colorSource);
                RefreshSummary();
            };
            AddFieldRow(grid, "Тип", cmbType);

            // Цвет
            var cmbColor = new ComboBox { MinWidth = 120, Margin = new Thickness(0, 0, 12, 6) };
            var colors = colorSource(rowState.SelectedProductType) ?? new List<string> { OrderTemplateService.DefaultColor };
            foreach (var c in colors) cmbColor.Items.Add(c);
            int colorIdx = colors.IndexOf(rowState.Color);
            cmbColor.SelectedIndex = colorIdx >= 0 ? colorIdx : 0;
            if (cmbColor.SelectedItem is string pickedColor) rowState.Color = pickedColor;
            cmbColor.SelectionChanged += (_, _) =>
            {
                if (cmbColor.SelectedItem is string c) rowState.Color = c;
                RefreshSummary();
            };
            AddFieldRow(grid, "Цвет", cmbColor);

            // Режим Anwis (если Anwis)
            if (AnwisSizeService.IsApplicable(rowState.SelectedProductType))
            {
                var modes = Enum.GetValues<AnwisSizeMode>();
                var cmbMode = new ComboBox
                {
                    MinWidth = 110,
                    Margin = new Thickness(0, 0, 12, 6),
                    ToolTip = "Как вводятся размеры (см. подсказку в быстром добавлении)"
                };
                foreach (var mode in modes) cmbMode.Items.Add(AnwisSizeService.ShortLabels[mode]);
                int modeIdx = Array.IndexOf(modes, rowState.AnwisMode);
                cmbMode.SelectedIndex = modeIdx >= 0
                    ? modeIdx
                    : Array.IndexOf(modes, AnwisSizeService.DefaultMode);
                rowState.AnwisMode = modes[cmbMode.SelectedIndex];
                cmbMode.SelectionChanged += (_, _) =>
                {
                    if (cmbMode.SelectedIndex >= 0) rowState.AnwisMode = modes[cmbMode.SelectedIndex];
                    RefreshSummary();
                };
                AddFieldRow(grid, "Режим", cmbMode);
            }

            // Выбор «Монтажа» для сетки (дефолт: «В конструкцию» index 2)
            var cmbInstall = new ComboBox { MinWidth = 130, Margin = new Thickness(0, 0, 12, 6) };
            cmbInstall.Items.Add("Монтаж включён");
            cmbInstall.Items.Add("Без монтажа");
            cmbInstall.Items.Add("В конструкцию");
            cmbInstall.SelectedIndex = Math.Clamp(rowState.InstallationMode, 0, 2);
            cmbInstall.SelectionChanged += (_, _) =>
            {
                rowState.InstallationMode = cmbInstall.SelectedIndex;
                RefreshSummary();
            };
            AddFieldRow(grid, "Монтаж", cmbInstall);

            // Антикошка (если применима)
            if (ProductCatalog.IsAnticatApplicable(rowState.SelectedProductType))
            {
                var chkAnticat = new CheckBox
                {
                    IsChecked = rowState.Anticat,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Ткань «Антикошка» — +2000 ₽/м²"
                };
                chkAnticat.Click += (_, _) =>
                {
                    rowState.Anticat = chkAnticat.IsChecked == true;
                    RefreshSummary();
                };
                AddFieldRow(grid, "Антикошка", chkAnticat);
            }

            panel.Children.Add(grid);
        }

        // ── Поля для Отлива и Козырька ──
        private void BuildLinearMeterFields(OrderTemplateService.TemplateRow row, StackPanel fields)
        {
            var rowState = _state.GetOrCreateRowState(row);
            var grid = MakeFieldGrid();

            // Цвет
            var cmbColor = new ComboBox { MinWidth = 120, Margin = new Thickness(0, 0, 12, 6) };
            var colors = _mw?.PricesVM?.GetColorsForProduct(row.ProductName) ?? new List<string> { OrderTemplateService.DefaultColor };
            foreach (var c in colors) cmbColor.Items.Add(c);
            int colorIdx = colors.IndexOf(rowState.Color);
            cmbColor.SelectedIndex = colorIdx >= 0 ? colorIdx : 0;
            if (cmbColor.SelectedItem is string initialColor) rowState.Color = initialColor;
            cmbColor.SelectionChanged += (_, _) =>
            {
                if (cmbColor.SelectedItem is string c) rowState.Color = c;
                RefreshSummary();
            };
            AddFieldRow(grid, "Цвет", cmbColor);

            // Монтаж: 3 режима, дефолт 2 («В конструкцию»)
            var cmbInstall = new ComboBox { MinWidth = 130, Margin = new Thickness(0, 0, 12, 6) };
            cmbInstall.Items.Add("Монтаж включён");
            cmbInstall.Items.Add("Без монтажа");
            cmbInstall.Items.Add("В конструкцию");
            cmbInstall.SelectedIndex = Math.Clamp(rowState.InstallationMode, 0, 2);
            cmbInstall.SelectionChanged += (_, _) =>
            {
                rowState.InstallationMode = cmbInstall.SelectedIndex;
                RefreshSummary();
            };
            AddFieldRow(grid, "Монтаж", cmbInstall);
            fields.Children.Add(grid);

            var dims = MakeDimsGrid();
            AddDimField(dims, 0, "Ширина, мм", v => rowState.Width = v, row.RowKey);
            AddDimField(dims, 2, "Высота, мм", v => rowState.Height = v, row.RowKey);
            AddQtyField(dims, 4, "Кол-во", v => rowState.Quantity = v, row.RowKey, (int)rowState.Quantity);
            fields.Children.Add(dims);
        }

        // ── Строка «ПСУЛ» ──
        private void BuildPsulFields(OrderTemplateService.TemplateRow row, StackPanel fields)
        {
            var rowState = _state.GetOrCreateRowState(row);

            fields.Children.Add(new TextBlock
            {
                Text = "Размеры опциональны — расчёт по периметру или по количеству",
                FontSize = Typography.Caption,
                Foreground = (Brush)FindResource("TextMuted"),
                Margin = new Thickness(0, 0, 0, 6)
            });

            var dims = MakeDimsGrid();
            AddDimField(dims, 0, "Ширина, мм", v => rowState.Width = v, row.RowKey);
            AddDimField(dims, 2, "Высота, мм", v => rowState.Height = v, row.RowKey);
            AddQtyField(dims, 4, "Кол-во", v => rowState.Quantity = v, row.RowKey, (int)rowState.Quantity);
            fields.Children.Add(dims);
        }

        // ── Ручные товары: Доставка, Брус, Короб, Пояс + Короб ──
        private void BuildManualAmountFields(OrderTemplateService.TemplateRow row, StackPanel fields)
        {
            var rowState = _state.GetOrCreateRowState(row);

            var dims = MakeDimsGrid();
            AddAmountField(dims, 0, "Сумма, ₽", v => rowState.ManualAmount = v, row.RowKey, rowState.ManualAmount > 0 ? rowState.ManualAmount : 0);
            if (row.ProductName != "Доставка")
            {
                AddQtyField(dims, 2, "Кол-во", v => rowState.Quantity = v, row.RowKey, (int)rowState.Quantity);
            }
            fields.Children.Add(dims);
        }

        private void AddAmountField(Grid grid, int column, string label, Action<double> setValue, string rowKey, double initialValue = 0)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = Typography.Caption,
                Foreground = (Brush)FindResource("TextSecondary"),
                Margin = new Thickness(0, 0, 0, 2)
            });
            var box = new TextBox
            {
                Style = (Style)FindResource("QuickInput"),
                Height = 28
            };
            if (initialValue > 0) box.Text = initialValue.ToString();

            box.TextChanged += (_, _) =>
            {
                double val = MoneyFormatService.TryParse(box.Text, out double v) ? v : 0;
                setValue(val);
                if (box.Text.Length > 0 && val > 0) box.Tag = null;
                RefreshSummary();
            };
            stack.Children.Add(box);
            Grid.SetColumn(stack, column);
            Grid.SetColumnSpan(stack, 2);
            grid.Children.Add(stack);

            _amountBoxes[rowKey] = box;
        }

        // ── Вспомогательные фабрики UI ──

        private static Grid MakeFieldGrid()
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        private static void AddFieldRow(Grid grid, string label, FrameworkElement control, FrameworkElement? trailing = null)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var lbl = MakeFieldLabel(label);
            Grid.SetRow(lbl, row);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);

            if (trailing != null)
            {
                Grid.SetRow(trailing, row);
                Grid.SetColumn(trailing, 2);
                grid.Children.Add(trailing);
            }
        }

        private static TextBlock MakeFieldLabel(string text) => new()
        {
            Text = text,
            FontSize = Typography.Caption,
            Foreground = (Brush?)Application.Current?.TryFindResource("TextSecondary") ?? Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 6)
        };

        private static Grid MakeDimsGrid()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            for (int i = 0; i < 6; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        private void AddDimField(Grid grid, int column, string label, Action<int> setValue, string rowKey, int initialValue = 0)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = Typography.Caption,
                Foreground = (Brush)FindResource("TextSecondary"),
                Margin = new Thickness(0, 0, 0, 2)
            });
            var box = new TextBox
            {
                Style = (Style)FindResource("QuickInput"),
                Height = 28
            };
            if (initialValue > 0) box.Text = initialValue.ToString();

            box.TextChanged += (_, _) =>
            {
                setValue(TryParseInt(box.Text));
                if (box.Text.Length > 0) box.Tag = null;
                RefreshSummary();
            };
            stack.Children.Add(box);
            Grid.SetColumn(stack, column);
            Grid.SetColumnSpan(stack, 2);
            grid.Children.Add(stack);

            if (label.Contains("Ширина")) _widthBoxes[rowKey] = box;
            else if (label.Contains("Высота")) _heightBoxes[rowKey] = box;
        }

        private void AddQtyField(Grid grid, int column, string label, Action<int> setValue, string rowKey, int initialValue = 0)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = Typography.Caption,
                Foreground = (Brush)FindResource("TextSecondary"),
                Margin = new Thickness(0, 0, 0, 2)
            });

            var stepperGrid = new Grid();
            stepperGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            stepperGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            stepperGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var box = new TextBox
            {
                Style = (Style)FindResource("QuickInput"),
                TextAlignment = TextAlignment.Center,
                Height = 28,
                Margin = new Thickness(3, 0, 3, 0)
            };
            if (initialValue > 0) box.Text = initialValue.ToString();

            var btnMinus = new Button
            {
                Style = (Style)FindResource("GhostButton"),
                Content = "−",
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                FontSize = Typography.Body,
                FontWeight = FontWeights.Bold,
                ToolTip = "Уменьшить (ниже)"
            };

            var btnPlus = new Button
            {
                Style = (Style)FindResource("GhostButton"),
                Content = "+",
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                FontSize = Typography.Body,
                FontWeight = FontWeights.Bold,
                ToolTip = "Увеличить (выше)"
            };

            btnMinus.Click += (_, _) =>
            {
                int current = TryParseInt(box.Text);
                if (current > 1) box.Text = (current - 1).ToString();
                else box.Text = "";
            };

            btnPlus.Click += (_, _) =>
            {
                int current = TryParseInt(box.Text);
                box.Text = (current + 1).ToString();
            };

            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Up)
                {
                    int current = TryParseInt(box.Text);
                    box.Text = (current + 1).ToString();
                    e.Handled = true;
                }
                else if (e.Key == Key.Down)
                {
                    int current = TryParseInt(box.Text);
                    if (current > 1) box.Text = (current - 1).ToString();
                    else box.Text = "";
                    e.Handled = true;
                }
            };

            box.TextChanged += (_, _) =>
            {
                int val = TryParseInt(box.Text);
                setValue(val);
                if (box.Text.Length > 0 && val > 0) box.Tag = null;
                RefreshSummary();
            };

            Grid.SetColumn(btnMinus, 0);
            Grid.SetColumn(box, 1);
            Grid.SetColumn(btnPlus, 2);

            stepperGrid.Children.Add(btnMinus);
            stepperGrid.Children.Add(box);
            stepperGrid.Children.Add(btnPlus);

            stack.Children.Add(stepperGrid);
            Grid.SetColumn(stack, column);
            Grid.SetColumnSpan(stack, 2);
            grid.Children.Add(stack);

            _qtyBoxes[rowKey] = box;
        }

        private static int TryParseInt(string text) =>
            int.TryParse(text?.Trim(), out int v) ? Math.Max(0, v) : 0;

        private void OnRowToggled(string rowKey, bool isEnabled)
        {
            if (_state.RowStates.TryGetValue(rowKey, out var s))
            {
                s.IsEnabled = isEnabled;
            }
            ApplyRowEnabledState(rowKey, isEnabled);
            RefreshSummary();
        }

        private void ApplyRowEnabledState(string rowKey, bool isEnabled)
        {
            if (_fieldPanelsByProduct.TryGetValue(rowKey, out var fields))
            {
                fields.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
