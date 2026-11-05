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
            _splitPreviewPanels.Clear();
            _rowCards.Clear();
            _stepBadges.Clear();
            _titleBlocks.Clear();
            _gridTopPanel = null;
            _frameGeometryCard = null;
            _sharedGeometryNotice = null;
            _txtSharedGeometryDesc = null;
            _kozyrekGeometrySlot = null;
            _otlivGeometrySlot = null;

            if (_template == null) return;

            bool isAutoSplit = OrderTemplateService.SupportsAutoSplit(_template.Id) && _state.SplitConfig.IsAutoSplitMode;
            int stepNumber = 0;

            foreach (var row in _template.Rows)
            {
                // В авто-режиме пропускаем ручные строки козырьков и отливов — они собираются в виде авто-блоков раскроя
                if (isAutoSplit && OrderTemplateService.IsSplitRow(row.RowKey))
                    continue;

                stepNumber++;
                BuildStandardRowCard(row, stepNumber);

                // Если это сетка (первая строка) в шаблонах Рама/Француз с авторасчётом — добавляем блоки раскроя
                if (isAutoSplit && row.RowKey == "grid")
                {
                    stepNumber++;
                    BuildAutoProductCard("Козырёк", stepNumber, 350, "Автоматический раскрой козырька с нахлёстами стыков (+50) и углов (+W)");

                    if (_template.Id == "frame")
                    {
                        stepNumber++;
                        BuildAutoProductCard("Отлив", stepNumber, 150, "Автоматический раскрой отлива по габаритам рамы (макс. 2000 мм)");
                    }

                    BuildFrameGeometryCard();
                    UpdateFrameGeometryCardVisibility();
                }
            }

            RefreshSummary();
        }

        private void BuildStandardRowCard(OrderTemplateService.TemplateRow row, int stepNumber)
        {
            var rowState = _state.GetOrCreateRowState(row);
            bool enabled = rowState.IsEnabled;

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 0: toggle
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 1: step
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 2: title
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 3: price
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 4: status pill

            var toggle = new CheckBox
            {
                IsChecked = enabled,
                IsEnabled = !row.IsCheckedLocked,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)FindResource("FluentToggleSwitch"),
                FocusVisualStyle = (Style)FindResource("FluentFocusVisual")
            };
            toggle.Checked += (_, _) => OnRowToggled(row.RowKey, true);
            toggle.Unchecked += (_, _) => OnRowToggled(row.RowKey, false);
            _rowToggles[row.RowKey] = toggle;
            header.Children.Add(toggle);

            var stepBadge = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = (CornerRadius)FindResource("Radius.Pill"),
                Background = (Brush)FindResource("AccentText"),
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

            var priceLabel = new TextBlock
            {
                FontSize = Typography.Body,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("AccentText"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 10, 0)
            };
            Grid.SetColumn(priceLabel, 3);
            header.Children.Add(priceLabel);
            _rowPriceLabels[row.RowKey] = priceLabel;

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
            _rowCards[row.RowKey] = rowCard;
            _stepBadges[row.RowKey] = stepBadge;
            _titleBlocks[row.RowKey] = title;
            AutomationProperties.SetName(rowCard, $"Позиция шаблона {title.Text}");
            ChecklistPanel.Children.Add(rowCard);

            ApplyRowEnabledState(row.RowKey, enabled);
        }

        internal Border? _frameGeometryCard;
        internal Border? _sharedGeometryNotice;
        internal TextBlock? _txtSharedGeometryDesc;
        private Border? _kozyrekGeometrySlot;
        private Border? _otlivGeometrySlot;

        internal void UpdateFrameGeometryCardVisibility()
        {
            if (_frameGeometryCard == null) return;

            bool isKozyrek = _state.SplitConfig.KozyrekEnabled;
            bool isOtliv = _template?.Id == "frame" && _state.SplitConfig.OtlivEnabled;

            if (_frameGeometryCard.Parent is Border cardParent)
                cardParent.Child = null;
            if (_sharedGeometryNotice?.Parent is Border noticeParent)
                noticeParent.Child = null;

            if (isKozyrek)
            {
                if (_kozyrekGeometrySlot != null)
                    _kozyrekGeometrySlot.Child = _frameGeometryCard;

                if (_sharedGeometryNotice != null && _otlivGeometrySlot != null)
                {
                    _otlivGeometrySlot.Child = _sharedGeometryNotice;
                    _sharedGeometryNotice.Visibility = isOtliv ? Visibility.Visible : Visibility.Collapsed;
                    UpdateSharedGeometryNoticeText();
                }

                _frameGeometryCard.Visibility = Visibility.Visible;
            }
            else if (isOtliv)
            {
                if (_otlivGeometrySlot != null)
                    _otlivGeometrySlot.Child = _frameGeometryCard;

                if (_sharedGeometryNotice != null)
                    _sharedGeometryNotice.Visibility = Visibility.Collapsed;

                _frameGeometryCard.Visibility = Visibility.Visible;
            }
            else
            {
                // Если оба выключены — привязываем к слоту козырька, но скрываем
                if (_kozyrekGeometrySlot != null)
                    _kozyrekGeometrySlot.Child = _frameGeometryCard;

                _frameGeometryCard.Visibility = Visibility.Collapsed;
                if (_sharedGeometryNotice != null)
                    _sharedGeometryNotice.Visibility = Visibility.Collapsed;
            }
        }

        internal void UpdateSharedGeometryNoticeText()
        {
            if (_txtSharedGeometryDesc == null) return;
            string sides = _state.SplitConfig.SideCount switch
            {
                0 => "0 углов (прямая)",
                1 => "1 угол (Г-образная)",
                2 => "2 угла (П-образная)",
                _ => $"{_state.SplitConfig.SideCount} угла"
            };
            string sideLen = _state.SplitConfig.SideCount > 0 ? $" · LS = {_state.SplitConfig.SideLength} мм" : "";
            _txtSharedGeometryDesc.Text = $"L = {_state.SplitConfig.FrameLength} мм · {sides}{sideLen} (общие для всей рамы)";
        }

        private void BuildFrameGeometryCard()
        {
            var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 0: icon
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 1: title
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 2: hint

            var iconBox = new TextBlock
            {
                Text = "\uE7B8",
                FontFamily = (FontFamily)FindResource("Font.Icon"),
                FontSize = Typography.Caption,
                Foreground = (Brush)FindResource("AccentText"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            header.Children.Add(iconBox);

            var title = new TextBlock
            {
                Text = "Размеры конструкции (рамы)",
                FontSize = Typography.Caption,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimary"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 1);
            header.Children.Add(title);

            var hint = new TextBlock
            {
                Text = "длина фасада и боковин",
                FontSize = Typography.Caption,
                Foreground = (Brush)FindResource("TextMuted"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(hint, 2);
            header.Children.Add(hint);

            // Компактная 3-колоночная сетка: L | Боковины (углы) | LS
            var dims = new Grid();
            dims.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            dims.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
            dims.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            dims.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
            dims.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

            // 1. Длина стороны L
            var colL = new StackPanel();
            colL.Children.Add(new TextBlock
            {
                Text = "Длина стороны L, мм",
                FontSize = Typography.Caption,
                Foreground = (Brush)FindResource("TextMuted"),
                Margin = new Thickness(0, 0, 0, 4)
            });
            var txtL = new TextBox
            {
                Text = _state.SplitConfig.FrameLength > 0 ? _state.SplitConfig.FrameLength.ToString() : "3000",
                Height = 32
            };
            txtL.TextChanged += (_, _) =>
            {
                if (int.TryParse(txtL.Text.Trim(), out int val))
                {
                    _state.SplitConfig.FrameLength = val;
                    txtL.Tag = null;
                }
                UpdateSharedGeometryNoticeText();
                RefreshSummary();
            };
            _widthBoxes["frame_l"] = txtL;
            colL.Children.Add(txtL);
            Grid.SetColumn(colL, 0);
            dims.Children.Add(colL);

            // 2. Конфигурация боковин
            var colSides = new StackPanel();
            colSides.Children.Add(new TextBlock
            {
                Text = "Боковые части (углы)",
                FontSize = Typography.Caption,
                Foreground = (Brush)FindResource("TextMuted"),
                Margin = new Thickness(0, 0, 0, 4)
            });
            var cmbSides = new ComboBox { Height = 32 };
            cmbSides.Items.Add("0 — прямая (0 углов)");
            cmbSides.Items.Add("1 — Г-образная (1 угол)");
            cmbSides.Items.Add("2 — П-образная (2 угла)");
            cmbSides.SelectedIndex = Math.Clamp(_state.SplitConfig.SideCount, 0, 2);
            colSides.Children.Add(cmbSides);
            Grid.SetColumn(colSides, 2);
            dims.Children.Add(colSides);

            // 3. Длина боковины LS
            var colLS = new StackPanel();
            colLS.Children.Add(new TextBlock
            {
                Text = "Длина боковины LS, мм",
                FontSize = Typography.Caption,
                Foreground = (Brush)FindResource("TextMuted"),
                Margin = new Thickness(0, 0, 0, 4)
            });
            var txtLS = new TextBox
            {
                Text = _state.SplitConfig.SideLength > 0 ? _state.SplitConfig.SideLength.ToString() : "800",
                Height = 32,
                IsEnabled = _state.SplitConfig.SideCount > 0
            };
            txtLS.TextChanged += (_, _) =>
            {
                if (int.TryParse(txtLS.Text.Trim(), out int val))
                {
                    _state.SplitConfig.SideLength = val;
                    txtLS.Tag = null;
                }
                UpdateSharedGeometryNoticeText();
                RefreshSummary();
            };
            _widthBoxes["side_l"] = txtLS;
            colLS.Children.Add(txtLS);
            Grid.SetColumn(colLS, 4);
            dims.Children.Add(colLS);

            cmbSides.SelectionChanged += (_, _) =>
            {
                _state.SplitConfig.SideCount = cmbSides.SelectedIndex;
                txtLS.IsEnabled = _state.SplitConfig.SideCount > 0;
                UpdateSharedGeometryNoticeText();
                RefreshSummary();
            };

            _frameGeometryCard = new Border
            {
                Background = (Brush)FindResource("SectionBg"),
                BorderBrush = (Brush)FindResource("SubtleBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = (CornerRadius)FindResource("Radius.Control"),
                Padding = new Thickness(12, 8, 12, 10),
                Margin = new Thickness(0, 0, 0, 10),
                Child = new StackPanel { Children = { header, dims } }
            };
            AutomationProperties.SetName(_frameGeometryCard, "Габариты рамы");

            BuildSharedGeometryNotice();
        }

        private void BuildSharedGeometryNotice()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // icon
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // text
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // button

            var icon = new TextBlock
            {
                Text = "\uE71B",
                FontFamily = (FontFamily)FindResource("Font.Icon"),
                FontSize = Typography.Body,
                Foreground = (Brush)FindResource("AccentText"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var lblTitle = new TextBlock
            {
                Text = "Связанные размеры рамы (общие с козырьком)",
                FontSize = Typography.Caption,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimary")
            };
            textStack.Children.Add(lblTitle);

            _txtSharedGeometryDesc = new TextBlock
            {
                FontSize = Typography.Caption,
                Foreground = (Brush)FindResource("TextMuted"),
                Margin = new Thickness(0, 2, 0, 0)
            };
            textStack.Children.Add(_txtSharedGeometryDesc);
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            var btnEdit = new Button
            {
                Style = (Style)FindResource("GhostButton"),
                Padding = new Thickness(8, 4, 8, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "Перейти к редактированию габаритов рамы"
            };
            var btnStack = new StackPanel { Orientation = Orientation.Horizontal };
            btnStack.Children.Add(new TextBlock
            {
                Text = "\uE70F",
                FontFamily = (FontFamily)FindResource("Font.Icon"),
                FontSize = Typography.Caption,
                Foreground = (Brush)FindResource("AccentText"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            });
            btnStack.Children.Add(new TextBlock
            {
                Text = "Изменить габариты",
                FontSize = Typography.Caption,
                Foreground = (Brush)FindResource("AccentText"),
                VerticalAlignment = VerticalAlignment.Center
            });
            btnEdit.Content = btnStack;
            btnEdit.Click += (_, _) =>
            {
                if (!_state.SplitConfig.KozyrekEnabled && _rowToggles.TryGetValue("auto_kozyrek", out var toggleK))
                {
                    toggleK.IsChecked = true;
                }
                if (_widthBoxes.TryGetValue("frame_l", out var txtBox))
                {
                    txtBox.BringIntoView();
                    txtBox.Focus();
                    txtBox.SelectAll();
                }
            };
            Grid.SetColumn(btnEdit, 2);
            grid.Children.Add(btnEdit);

            _sharedGeometryNotice = new Border
            {
                Background = (Brush)FindResource("SectionBg"),
                BorderBrush = (Brush)FindResource("SubtleBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = (CornerRadius)FindResource("Radius.Control"),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 10),
                Child = grid
            };
            AutomationProperties.SetName(_sharedGeometryNotice, "Общие габариты рамы");

            UpdateSharedGeometryNoticeText();
        }

        private void BuildAutoProductCard(string product, int stepNumber, int defaultWidth, string hintText)
        {
            string key = product == "Козырёк" ? "auto_kozyrek" : "auto_otliv";
            bool enabled = product == "Козырёк" ? _state.SplitConfig.KozyrekEnabled : _state.SplitConfig.OtlivEnabled;

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 0: toggle
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 1: step
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 2: title
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 3: price
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 4: status pill

            var toggle = new CheckBox
            {
                IsChecked = enabled,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)FindResource("FluentToggleSwitch"),
                FocusVisualStyle = (Style)FindResource("FluentFocusVisual")
            };
            _rowToggles[key] = toggle;
            header.Children.Add(toggle);

            var stepBadge = new Border
            {
                Width = 24, Height = 24,
                CornerRadius = (CornerRadius)FindResource("Radius.Pill"),
                Background = (Brush)FindResource("AccentText"),
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

            var titleStack = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock
            {
                Text = product,
                FontSize = Typography.Body,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimary")
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = hintText,
                FontSize = Typography.Caption,
                Foreground = (Brush)FindResource("TextMuted"),
                Margin = new Thickness(0, 1, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(titleStack, 2);
            header.Children.Add(titleStack);

            var priceLabel = new TextBlock
            {
                FontSize = Typography.Body,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("AccentText"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 10, 0)
            };
            Grid.SetColumn(priceLabel, 3);
            header.Children.Add(priceLabel);
            _rowPriceLabels[key] = priceLabel;

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
            _statusPills[key] = statusPill;

            var fields = new StackPanel { Margin = new Thickness(48, 6, 4, 2) };
            _fieldPanelsByProduct[key] = fields;

            // Слот для размещения блока габаритов рамы или информационного баннера общих габаритов
            var geomSlot = new Border { Margin = new Thickness(0, 0, 0, 4) };
            if (product == "Козырёк")
                _kozyrekGeometrySlot = geomSlot;
            else
                _otlivGeometrySlot = geomSlot;
            fields.Children.Add(geomSlot);

            int currentW = product == "Козырёк" ? _state.SplitConfig.KozyrekWidth : _state.SplitConfig.OtlivWidth;
            if (currentW <= 0) currentW = defaultWidth;
            if (product == "Козырёк") _state.SplitConfig.KozyrekWidth = currentW;
            else _state.SplitConfig.OtlivWidth = currentW;

            // Симметричная 3-колоночная сетка параметров материала: W | Цвет | Монтаж
            var matGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            matGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            matGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
            matGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            matGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
            matGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });

            // 1. Ширина материала W
            var colW = new StackPanel();
            colW.Children.Add(new TextBlock
            {
                Text = "Ширина материала W, мм",
                FontSize = Typography.Caption,
                Foreground = (Brush)FindResource("TextMuted"),
                Margin = new Thickness(0, 0, 0, 4)
            });
            var txtW = new TextBox
            {
                Text = currentW.ToString(),
                Height = 32
            };
            string wBoxKey = product == "Козырёк" ? "koz_w" : "otl_w";
            _widthBoxes[wBoxKey] = txtW;
            txtW.TextChanged += (_, _) =>
            {
                if (int.TryParse(txtW.Text.Trim(), out int val))
                {
                    if (product == "Козырёк") _state.SplitConfig.KozyrekWidth = val;
                    else _state.SplitConfig.OtlivWidth = val;
                    txtW.Tag = null;
                }
                RefreshSummary();
            };
            colW.Children.Add(txtW);
            Grid.SetColumn(colW, 0);
            matGrid.Children.Add(colW);

            // 2. Цвет
            var colColor = new StackPanel();
            colColor.Children.Add(new TextBlock
            {
                Text = "Цвет",
                FontSize = Typography.Caption,
                Foreground = (Brush)FindResource("TextMuted"),
                Margin = new Thickness(0, 0, 0, 4)
            });
            var cmbColor = new ComboBox { Height = 32 };
            var colors = _mw?.PricesVM?.GetColorsForProduct(product) ?? new List<string> { OrderTemplateService.DefaultColor };
            foreach (var c in colors) cmbColor.Items.Add(c);
            string currentColor = product == "Козырёк" ? _state.SplitConfig.KozyrekColor : _state.SplitConfig.OtlivColor;
            int colorIdx = colors.IndexOf(currentColor);
            cmbColor.SelectedIndex = colorIdx >= 0 ? colorIdx : 0;
            cmbColor.SelectionChanged += (_, _) =>
            {
                if (cmbColor.SelectedItem is string c)
                {
                    if (product == "Козырёк") _state.SplitConfig.KozyrekColor = c;
                    else _state.SplitConfig.OtlivColor = c;
                }
                RefreshSummary();
            };
            colColor.Children.Add(cmbColor);
            Grid.SetColumn(colColor, 2);
            matGrid.Children.Add(colColor);

            // 3. Монтаж
            var colInstall = new StackPanel();
            colInstall.Children.Add(new TextBlock
            {
                Text = "Монтаж",
                FontSize = Typography.Caption,
                Foreground = (Brush)FindResource("TextMuted"),
                Margin = new Thickness(0, 0, 0, 4)
            });
            var cmbInstall = new ComboBox { Height = 32 };
            cmbInstall.Items.Add("Монтаж включён");
            cmbInstall.Items.Add("Без монтажа");
            cmbInstall.Items.Add("В конструкцию");
            int installMode = product == "Козырёк" ? _state.SplitConfig.KozyrekInstallMode : _state.SplitConfig.OtlivInstallMode;
            cmbInstall.SelectedIndex = Math.Clamp(installMode, 0, 2);
            cmbInstall.SelectionChanged += (_, _) =>
            {
                if (product == "Козырёк") _state.SplitConfig.KozyrekInstallMode = cmbInstall.SelectedIndex;
                else _state.SplitConfig.OtlivInstallMode = cmbInstall.SelectedIndex;
                RefreshSummary();
            };
            colInstall.Children.Add(cmbInstall);
            Grid.SetColumn(colInstall, 4);
            matGrid.Children.Add(colInstall);

            fields.Children.Add(matGrid);

            var previewPanel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            _splitPreviewPanels[key] = previewPanel;
            fields.Children.Add(previewPanel);

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
            _rowCards[key] = rowCard;
            _stepBadges[key] = stepBadge;
            _titleBlocks[key] = (TextBlock)titleStack.Children[0];
            AutomationProperties.SetName(rowCard, $"Позиция {product}");
            ChecklistPanel.Children.Add(rowCard);

            toggle.Checked += (_, _) =>
            {
                if (product == "Козырёк") _state.SplitConfig.KozyrekEnabled = true;
                else _state.SplitConfig.OtlivEnabled = true;
                ApplyRowEnabledState(key, true);
                UpdateFrameGeometryCardVisibility();
                RefreshSummary();
            };
            toggle.Unchecked += (_, _) =>
            {
                if (product == "Козырёк") _state.SplitConfig.KozyrekEnabled = false;
                else _state.SplitConfig.OtlivEnabled = false;
                ApplyRowEnabledState(key, false);
                UpdateFrameGeometryCardVisibility();
                RefreshSummary();
            };

            ApplyRowEnabledState(key, enabled);
        }

        private void UpdateSplitPreview(string product, StackPanel previewPanel, OrderTemplateService.SplitProductSummary summary)
        {
            previewPanel.Children.Clear();
            if (!summary.Enabled) return;

            if (summary.HasError)
            {
                var errCard = new Border
                {
                    Background = (Brush)FindResource("DangerLight"),
                    BorderBrush = (Brush)FindResource("DangerGhostBorder"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = (CornerRadius)FindResource("Radius.Control"),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 4, 0, 4)
                };
                var errStack = new StackPanel { Orientation = Orientation.Horizontal };
                var icon = new TextBlock
                {
                    Text = "\uE783",
                    FontFamily = (FontFamily)FindResource("Font.Icon"),
                    FontSize = Typography.Body,
                    Foreground = (Brush)FindResource("Danger"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                var errText = new TextBlock
                {
                    Text = summary.ErrorMessage ?? "Ошибка раскроя: проверьте введённые размеры",
                    FontSize = Typography.Caption,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("Danger"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                errStack.Children.Add(icon);
                errStack.Children.Add(errText);
                errCard.Child = errStack;
                previewPanel.Children.Add(errCard);
                return;
            }

            var specCard = new Border
            {
                Background = (Brush)FindResource("SectionBg"),
                BorderBrush = (Brush)FindResource("SubtleBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = (CornerRadius)FindResource("Radius.Control"),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 4, 0, 4)
            };

            var contentStack = new StackPanel();

            // Шапка спецификации: Иконка + Заголовок слева, итоговая сводка (дет., м.п., м²) справа
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock
            {
                Text = "\uE7C3",
                FontFamily = (FontFamily)FindResource("Font.Icon"),
                FontSize = Typography.Caption,
                Foreground = (Brush)FindResource("AccentText"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "Спецификация раскроя",
                FontSize = Typography.Caption,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimary"),
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(titleStack, 0);
            headerGrid.Children.Add(titleStack);

            var metricsText = new TextBlock
            {
                Text = $"Итого: {summary.TotalPieceCount} дет.  ·  {summary.TotalLinearMeters} м.п. ({summary.TotalSquareMeters:F2} м²)",
                FontSize = Typography.Caption,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("AccentText"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(metricsText, 2);
            headerGrid.Children.Add(metricsText);
            contentStack.Children.Add(headerGrid);

            var badgesPanel = new WrapPanel { Orientation = Orientation.Horizontal };

            for (int i = 0; i < summary.FrontResult.Pieces.Count; i++)
            {
                var p = summary.FrontResult.Pieces[i];
                var badge = new Border
                {
                    Background = (Brush)FindResource("AccentLight"),
                    BorderBrush = (Brush)FindResource("Accent"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = (CornerRadius)FindResource("Radius.Sm"),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 0, 6, 4),
                    ToolTip = $"Перед #{i + 1}: Номинал {p.Nominal} мм" +
                              (p.JointLeft > 0 ? $" + стык {p.JointLeft}" : "") +
                              (p.JointRight > 0 ? $" + стык {p.JointRight}" : "") +
                              (p.CornerStart > 0 ? $" + угол {p.CornerStart}" : "") +
                              (p.CornerEnd > 0 ? $" + угол {p.CornerEnd}" : "") +
                              $" = {p.Length} мм",
                    Child = new TextBlock
                    {
                        Text = $"Перед {i + 1}: {p.Length} мм",
                        FontSize = Typography.Caption,
                        FontWeight = FontWeights.Medium,
                        Foreground = (Brush)FindResource("TextPrimary")
                    }
                };
                badgesPanel.Children.Add(badge);
            }

            if (summary.SideCount > 0 && summary.SideResult.Success)
            {
                for (int s = 0; s < summary.SideCount; s++)
                {
                    string sideLabel = s == 0 ? "Бок Л" : "Бок П";
                    for (int i = 0; i < summary.SideResult.Pieces.Count; i++)
                    {
                        var p = summary.SideResult.Pieces[i];
                        string pieceTitle = summary.SideResult.Pieces.Count > 1 ? $"{sideLabel} {i + 1}" : sideLabel;
                        var badge = new Border
                        {
                            Background = (Brush)FindResource("GhostBg"),
                            BorderBrush = (Brush)FindResource("SubtleBorder"),
                            BorderThickness = new Thickness(1),
                            CornerRadius = (CornerRadius)FindResource("Radius.Sm"),
                            Padding = new Thickness(8, 3, 8, 3),
                            Margin = new Thickness(0, 0, 6, 4),
                            ToolTip = $"{pieceTitle}: Номинал {p.Nominal} мм" +
                                      (p.JointLeft > 0 ? $" + стык {p.JointLeft}" : "") +
                                      (p.JointRight > 0 ? $" + стык {p.JointRight}" : "") +
                                      (p.CornerStart > 0 ? $" + угол {p.CornerStart}" : "") +
                                      $" = {p.Length} мм",
                            Child = new TextBlock
                            {
                                Text = $"{pieceTitle}: {p.Length} мм",
                                FontSize = Typography.Caption,
                                FontWeight = FontWeights.Medium,
                                Foreground = (Brush)FindResource("TextPrimary")
                            }
                        };
                        badgesPanel.Children.Add(badge);
                    }
                }
            }

            contentStack.Children.Add(badgesPanel);
            specCard.Child = contentStack;
            previewPanel.Children.Add(specCard);
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

            // Режим размера (если Anwis или 2-режимные сетки)
            if (AnwisSizeService.SupportsSizeMode(rowState.SelectedProductType))
            {
                var modes = AnwisSizeService.GetAvailableModes(rowState.SelectedProductType);
                var cmbMode = new ComboBox
                {
                    MinWidth = 110,
                    Margin = new Thickness(0, 0, 12, 6),
                    ToolTip = "Как вводятся размеры (см. подсказку в быстром добавлении)"
                };
                foreach (var mode in modes) cmbMode.Items.Add(AnwisSizeService.GetShortLabel(rowState.SelectedProductType, mode));
                int modeIdx = -1;
                for (int i = 0; i < modes.Count; i++)
                {
                    if (modes[i] == rowState.AnwisMode) { modeIdx = i; break; }
                }
                var defaultMode = AnwisSizeService.GetDefaultMode(rowState.SelectedProductType);
                if (modeIdx < 0)
                {
                    for (int i = 0; i < modes.Count; i++)
                    {
                        if (modes[i] == defaultMode) { modeIdx = i; break; }
                    }
                    if (modeIdx < 0 && modes.Count > 0) modeIdx = 0;
                }
                cmbMode.SelectedIndex = modeIdx >= 0 ? modeIdx : 0;
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

            // Антикошка (если применима) — тумблер Вкл/Выкл
            if (ProductCatalog.IsAnticatApplicable(rowState.SelectedProductType))
            {
                var chkAnticat = new CheckBox
                {
                    IsChecked = rowState.Anticat,
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = (Style)FindResource("FluentToggleSwitch"),
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

            if (_stepBadges.TryGetValue(rowKey, out var stepBadge))
            {
                stepBadge.Background = (Brush)FindResource(isEnabled ? "Accent" : "ChipBg");
                if (stepBadge.Child is TextBlock tb)
                {
                    tb.Foreground = (Brush)FindResource(isEnabled ? "OnAccentPrimary" : "TextMuted");
                    tb.FontWeight = isEnabled ? FontWeights.Bold : FontWeights.Normal;
                }
            }

            if (_titleBlocks.TryGetValue(rowKey, out var titleBlock))
            {
                titleBlock.Foreground = (Brush)FindResource(isEnabled ? "TextPrimary" : "TextSecondary");
            }

            if (_rowCards.TryGetValue(rowKey, out var card))
            {
                card.BorderBrush = (Brush)FindResource(isEnabled ? "Border" : "SubtleBorder");
            }
        }
    }
}
