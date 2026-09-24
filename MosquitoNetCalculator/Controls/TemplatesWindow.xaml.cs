using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MosquitoNetCalculator.Helpers;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// Окно «Шаблоны» (v3.54, замена «На завод»): витрина карточек-шаблонов →
    /// чек-лист позиций → атомарное добавление в заказ (один Undo-шаг).
    /// Строки чек-листа с полями строятся в code-behind (паттерн
    /// BuildCheckboxes из прежнего SendToFactoryWindow): каталог — данные,
    /// но поля строк зависят от товара, поэтому декларативные шаблоны
    /// данных здесь дали бы больше кода, чем они экономят.
    /// </summary>
    public partial class TemplatesWindow : Window
    {
        private readonly MainWindow _mw;
        private readonly OrderTemplateService _state = new();
        private OrderTemplateService.OrderTemplate? _template;

        // Контролы строк чек-листа (ключ → контрол) для валидации-подсветки
        // и пересчёта сводки.
        private readonly Dictionary<string, TextBox> _widthBoxes = new();
        private readonly Dictionary<string, TextBox> _heightBoxes = new();
        private readonly Dictionary<string, CheckBox> _rowToggles = new();
        private readonly Dictionary<string, TextBlock> _statusTexts = new();
        private readonly Dictionary<string, StackPanel> _fieldPanelsByProduct = new();

        // Верхняя часть полей «Сетки» (тип/цвет/режим/антикошка): пересобирается
        // при смене типа, чтобы не двигать Grid.Row у живых детей (см. GOTCHAS).
        private StackPanel? _gridTopPanel;

        public TemplatesWindow(MainWindow mw)
        {
            InitializeComponent();
            _mw = mw;
            BuildGallery();

            Loaded += (_, _) =>
            {
                var storyboard = (Storyboard)FindResource("WindowOpenAnimation");
                Storyboard.SetTarget(storyboard, RootBorder);
                Motion.Run(storyboard);
            };
        }

        // ─────────────────────────────────────────────────────────
        // VIEW 1 — витрина шаблонов
        // ─────────────────────────────────────────────────────────

        private void BuildGallery()
        {
            GalleryPanel.Children.Clear();
            foreach (var template in OrderTemplateService.All)
            {
                var card = new Button { Style = (Style)FindResource("TemplateCard"), Margin = new Thickness(0, 0, 0, 10) };

                var stack = new StackPanel();
                var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
                titleRow.Children.Add(new TextBlock
                {
                    Text = template.Name,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextPrimary"),
                    VerticalAlignment = VerticalAlignment.Center
                });

                if (!template.IsAvailable)
                {
                    titleRow.Children.Add(new Border
                    {
                        Background = (Brush)FindResource("BadgeWarningBg"),
                        CornerRadius = (CornerRadius)FindResource("Radius.Capsule"),
                        Padding = new Thickness(8, 2, 8, 2),
                        Margin = new Thickness(8, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = "Скоро",
                            FontSize = 11,
                            Foreground = (Brush)FindResource("BadgeWarningFg")
                        }
                    });
                }
                stack.Children.Add(titleRow);

                stack.Children.Add(new TextBlock
                {
                    Text = template.IsAvailable ? template.Subtitle : "Шаблон в подготовке",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("TextMuted"),
                    Margin = new Thickness(0, 3, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });

                card.Content = stack;
                card.IsEnabled = template.IsAvailable;
                card.Click += (_, _) => OpenChecklist(template);
                card.ToolTip = template.IsAvailable
                    ? $"Открыть шаблон «{template.Name}»"
                    : "Шаблон пока недоступен";
                AutomationProperties.SetName(card, $"Шаблон {template.Name}");
                GalleryPanel.Children.Add(card);
            }
        }

        private void OpenChecklist(OrderTemplateService.OrderTemplate template)
        {
            _template = template;
            TxtChecklistTitle.Text = $"Шаблон «{template.Name}»";
            BuildChecklist();
            ShowChecklistView();
            RefreshSummary();
        }

        private void ShowChecklistView()
        {
            GalleryView.Visibility = Visibility.Collapsed;
            ChecklistView.Visibility = Visibility.Visible;
        }

        private void ShowGalleryView()
        {
            ChecklistView.Visibility = Visibility.Collapsed;
            GalleryView.Visibility = Visibility.Visible;
            _template = null;
        }

        // ─────────────────────────────────────────────────────────
        // VIEW 2 — чек-лист
        // ─────────────────────────────────────────────────────────

        private void BuildChecklist()
        {
            ChecklistPanel.Children.Clear();
            _widthBoxes.Clear();
            _heightBoxes.Clear();
            _rowToggles.Clear();
            _statusTexts.Clear();
            _fieldPanelsByProduct.Clear();
            _gridTopPanel = null;

            if (_template == null) return;
            foreach (var row in _template.Rows)
            {
                bool isGrid = row.ProductName == OrderTemplateService.OrderTemplateGridProduct;
                bool enabled = isGrid || row.DefaultChecked;

                // Шапка строки: [switch] Название+подсказка … статус справа.
                var header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Переключатель строки — implicit CheckBox (Win11-switch приложения),
                // а не голый ToggleButton: у того в дизайн-системе стиля нет.
                var toggle = new CheckBox
                {
                    IsChecked = enabled,
                    IsEnabled = !row.IsCheckedLocked,
                    VerticalAlignment = VerticalAlignment.Center,
                    FocusVisualStyle = (Style)FindResource("FluentFocusVisual")
                };
                toggle.Checked += (_, _) => OnRowToggled(row.ProductName, true);
                toggle.Unchecked += (_, _) => OnRowToggled(row.ProductName, false);
                _rowToggles[row.ProductName] = toggle;
                header.Children.Add(toggle); // column 0

                var titleStack = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                var title = new TextBlock
                {
                    Text = isGrid ? "Сетка" : row.ProductName,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextPrimary")
                };
                titleStack.Children.Add(title);
                if (!string.IsNullOrEmpty(row.Hint))
                {
                    titleStack.Children.Add(new TextBlock
                    {
                        Text = row.Hint,
                        FontSize = 11,
                        Foreground = (Brush)FindResource("TextMuted"),
                        Margin = new Thickness(0, 1, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                Grid.SetColumn(titleStack, 1);
                header.Children.Add(titleStack);

                var status = new TextBlock
                {
                    FontSize = 11,
                    Foreground = (Brush)FindResource("TextSecondary"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0)
                };
                Grid.SetColumn(status, 2);
                header.Children.Add(status);
                _statusTexts[row.ProductName] = status;

                // Поля строки — с отступом под текст названия (switch 40 + 8).
                var fields = new StackPanel { Margin = new Thickness(48, 6, 4, 2) };
                _fieldPanelsByProduct[row.ProductName] = fields;

                if (isGrid) BuildGridFields(fields);
                else if (row.ProductName == "ПСУЛ") BuildPsulFields(fields);
                else if (row.ProductName == "Доставка") BuildDeliveryFields(fields);
                else if (row.ProductName == "Отлив") BuildOtlivFields(fields);

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

                ApplyRowEnabledState(row.ProductName, enabled);
            }
        }

        // ── Строка «Сетка» ──
        private void BuildGridFields(StackPanel fields)
        {
            // Верхняя часть (тип/цвет/режим/антикошка) живёт в отдельной панели:
            // при смене типа она пересобирается целиком, а введённые размеры
            // в соседней dims-сетке не теряются.
            _gridTopPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            RebuildGridTopFields();
            fields.Children.Add(_gridTopPanel);

            var dims = MakeDimsGrid();
            AddDimField(dims, 0, "Ширина, мм", v => _state.GridWidth = v, "grid-size");
            AddDimField(dims, 2, "Высота, мм", v => _state.GridHeight = v, "grid-size");
            AddDimField(dims, 4, "Кол-во", v => _state.GridQuantity = v, "grid-size");
            fields.Children.Add(dims);
        }

        private void RebuildGridTopFields() =>
            RebuildGridTopFields(t =>
                _mw?.PricesVM?.GetColorsForProduct(t));

        /// <summary>
        /// Тестовый шов (internal, см. InternalsVisibleTo): источник цветов
        /// вводится параметром, чтобы STA-тест мог построить поля без
        /// MainWindow (цена этого бага: валидация «Выберите тип сетки»
        /// при визуально заполненной форме — начальная установка SelectedIndex
        /// ДО подписки на SelectionChanged не синхронизировала _state).
        /// </summary>
        internal void RebuildGridTopFields(Func<string, List<string>?> colorSource)
        {
            var panel = _gridTopPanel;
            if (panel == null) return;
            panel.Children.Clear();

            var grid = MakeFieldGrid();

            // Тип — смена пересобирает панель: списки цветов и режим Anwis
            // всегда соответствуют выбранному типу.
            var cmbType = new ComboBox { MinWidth = 170, Margin = new Thickness(0, 0, 12, 6) };
            foreach (var t in OrderTemplateService.GridProductChoices) cmbType.Items.Add(t);
            cmbType.SelectedIndex =
                (_state.GridProductIndex >= 0 && _state.GridProductIndex < cmbType.Items.Count)
                    ? _state.GridProductIndex : 0;
            // Первичная установка — ДО подписки на SelectionChanged, поэтому
            // событие не сработает: синхронизируем состояние явно, иначе
            // GridProductIndex остаётся −1 («не выбран») и валидация требует
            // «Выберите тип сетки» на визуально заполненной форме.
            _state.GridProductIndex = cmbType.SelectedIndex;
            string selectedType = cmbType.SelectedItem as string ?? "";
            cmbType.SelectionChanged += (_, _) =>
            {
                _state.GridProductIndex = cmbType.SelectedIndex;
                RebuildGridTopFields();
                RefreshSummary();
            };
            AddFieldRow(grid, "Тип", cmbType);

            // Цвет — список из прайса под выбранный тип; сохранённый цвет
            // остаётся выбранным, если он есть в новом списке.
            var cmbColor = new ComboBox { MinWidth = 120, Margin = new Thickness(0, 0, 12, 6) };
            var colors = colorSource(selectedType) ?? new List<string> { OrderTemplateService.DefaultColor };
            foreach (var c in colors) cmbColor.Items.Add(c);
            int colorIdx = colors.IndexOf(_state.GridColor);
            cmbColor.SelectedIndex = colorIdx >= 0 ? colorIdx : 0;
            if (cmbColor.SelectedItem is string pickedColor) _state.GridColor = pickedColor;
            cmbColor.SelectionChanged += (_, _) =>
            {
                if (cmbColor.SelectedItem is string c) _state.GridColor = c;
                RefreshSummary();
            };
            AddFieldRow(grid, "Цвет", cmbColor);

            // Режим Anwis — только для типов, где он применим.
            if (AnwisSizeService.IsApplicable(selectedType))
            {
                var modes = Enum.GetValues<AnwisSizeMode>();
                var cmbMode = new ComboBox
                {
                    MinWidth = 110,
                    Margin = new Thickness(0, 0, 12, 6),
                    ToolTip = "Как вводятся размеры (см. подсказку в быстром добавлении)"
                };
                foreach (var mode in modes) cmbMode.Items.Add(AnwisSizeService.ShortLabels[mode]);
                int modeIdx = Array.IndexOf(modes, _state.GridAnwisMode);
                cmbMode.SelectedIndex = modeIdx >= 0
                    ? modeIdx
                    : Array.IndexOf(modes, AnwisSizeService.DefaultMode);
                // Та же ловушка, что и у «Тип»: начальное значение выставлено
                // до подписки — состояние синхронизируем явно.
                _state.GridAnwisMode = modes[cmbMode.SelectedIndex];
                cmbMode.SelectionChanged += (_, _) =>
                {
                    if (cmbMode.SelectedIndex >= 0) _state.GridAnwisMode = modes[cmbMode.SelectedIndex];
                    RefreshSummary();
                };
                AddFieldRow(grid, "Режим", cmbMode);
            }

            // Антикошка — тот же switch, что и у строк списка.
            var chkAnticat = new CheckBox
            {
                IsChecked = _state.GridAnticat,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Ткань «Антикошка» — +2000 ₽/м²"
            };
            chkAnticat.Click += (_, _) =>
            {
                _state.GridAnticat = chkAnticat.IsChecked == true;
                RefreshSummary();
            };
            AddFieldRow(grid, "Антикошка", chkAnticat);

            panel.Children.Add(grid);
        }

        // ── Строка «ПСУЛ» ──
        private void BuildPsulFields(StackPanel fields)
        {
            fields.Children.Add(new TextBlock
            {
                Text = "Без размеров — расчёт по количеству (цена за м.п. из прайса)",
                FontSize = 11,
                Foreground = (Brush)FindResource("TextMuted"),
                Margin = new Thickness(0, 0, 0, 6)
            });
            var dims = MakeDimsGrid();
            AddDimField(dims, 0, "Ширина, мм", v => _state.PsulWidth = v, "psul");
            AddDimField(dims, 2, "Высота, мм", v => _state.PsulHeight = v, "psul");
            AddDimField(dims, 4, "Кол-во", v => _state.PsulQuantity = v, "psul");
            fields.Children.Add(dims);
        }

        // ── Строка «Доставка» ──
        private void BuildDeliveryFields(StackPanel fields)
        {
            var grid = MakeFieldGrid();
            var txtAmount = new TextBox
            {
                Style = (Style)FindResource("QuickInput"),
                Width = 110,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 12, 6)
            };
            txtAmount.TextChanged += (_, _) =>
            {
                _state.DeliveryAmount = MoneyFormatService.TryParse(txtAmount.Text, out double v) ? v : 0;
                RefreshSummary();
            };
            AddFieldRow(grid, "Сумма, ₽", txtAmount);
            fields.Children.Add(grid);

            var qty = MakeDimsGrid();
            AddDimField(qty, 0, "Кол-во", v => _state.DeliveryQuantity = v, "delivery");
            fields.Children.Add(qty);
        }

        // ── Строка «Отлив» ──
        private void BuildOtlivFields(StackPanel fields)
        {
            var grid = MakeFieldGrid();

            var cmbColor = new ComboBox { MinWidth = 120, Margin = new Thickness(0, 0, 12, 6) };
            foreach (var c in _mw.PricesVM.GetColorsForProduct("Отлив")) cmbColor.Items.Add(c);
            cmbColor.SelectedIndex = 0;
            if (cmbColor.SelectedItem is string initialColor) _state.OtlivColor = initialColor;
            cmbColor.SelectionChanged += (_, _) =>
            {
                if (cmbColor.SelectedItem is string c) _state.OtlivColor = c;
                RefreshSummary();
            };
            AddFieldRow(grid, "Цвет", cmbColor);

            var cmbInstall = new ComboBox { MinWidth = 130, Margin = new Thickness(0, 0, 12, 6) };
            cmbInstall.Items.Add("Монтаж включён");
            cmbInstall.Items.Add("Без монтажа");
            cmbInstall.SelectedIndex = 1; // дефолт отлива — без монтажа (как в QuickAdd)
            cmbInstall.SelectionChanged += (_, _) =>
            {
                _state.OtlivInstallationMode = cmbInstall.SelectedIndex;
                RefreshSummary();
            };
            AddFieldRow(grid, "Монтаж", cmbInstall);
            fields.Children.Add(grid);

            var dims = MakeDimsGrid();
            AddDimField(dims, 0, "Ширина, мм", v => _state.OtlivWidth = v, "otliv-size");
            AddDimField(dims, 2, "Высота, мм", v => _state.OtlivHeight = v, "otliv-size");
            AddDimField(dims, 4, "Кол-во", v => _state.OtlivQuantity = v, "otliv-size");
            fields.Children.Add(dims);
        }

        // ── Фабрики мелких контролов ──

        /// <summary>
        /// Сетка «метка | контрол | хвост»: строка на каждое поле.
        /// Строки ДОЛЖНЫ задаваться явно (Grid.SetRow) — WPF по умолчанию
        /// кладёт всё в строку 0, и подписи наезжают друг на друга.
        /// </summary>
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
            FontSize = 11,
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

        private void AddDimField(Grid grid, int column, string label, Action<int> setValue, string rowKey)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = (Brush)FindResource("TextSecondary"),
                Margin = new Thickness(0, 0, 0, 2)
            });
            var box = new TextBox { Style = (Style)FindResource("QuickInput") };
            box.TextChanged += (_, _) =>
            {
                setValue(TryParseInt(box.Text));
                if (box.Text.Length > 0) box.Tag = null; // снять красную рамку при вводе
                RefreshSummary();
            };
            stack.Children.Add(box);
            Grid.SetColumn(stack, column);
            Grid.SetColumnSpan(stack, 2);
            grid.Children.Add(stack);

            if (label.Contains("Ширина")) _widthBoxes[rowKey] = box;
            else if (label.Contains("Высота")) _heightBoxes[rowKey] = box;
        }

        private static int TryParseInt(string text) =>
            int.TryParse(text?.Trim(), out int v) ? Math.Max(0, v) : 0;

        // ─────────────────────────────────────────────────────────
        // Включение/выключение строк («Не требуется»)
        // ─────────────────────────────────────────────────────────

        private void OnRowToggled(string productName, bool isEnabled)
        {
            if (productName == OrderTemplateService.OrderTemplateGridProduct) return; // сетка всегда включена
            switch (productName)
            {
                case "ПСУЛ": _state.PsulEnabled = isEnabled; break;
                case "Доставка": _state.DeliveryEnabled = isEnabled; break;
                case "Отлив": _state.OtlivEnabled = isEnabled; break;
            }
            ApplyRowEnabledState(productName, isEnabled);
            RefreshSummary();
        }

        private void ApplyRowEnabledState(string productName, bool isEnabled)
        {
            if (_fieldPanelsByProduct.TryGetValue(productName, out var fields))
            {
                fields.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
            }

            if (_statusTexts.TryGetValue(productName, out var status))
            {
                status.Text = isEnabled
                    ? (productName == OrderTemplateService.OrderTemplateGridProduct ? "" : "Добавится")
                    : "Не требуется";
                status.Foreground = (Brush)FindResource(isEnabled ? "TextSecondary" : "TextMuted");
            }
        }

        // ─────────────────────────────────────────────────────────
        // Сводка и добавление
        // ─────────────────────────────────────────────────────────

        private void RefreshSummary()
        {
            if (_template == null) return;
            var catalogPrice = (string t, string c) => _mw.PricesVM.GetPrice(t, c);
            var specs = _state.BuildItemSpecs(_template, catalogPrice);

            int count = specs.Count;
            double total = specs.Sum(s =>
            {
                // Грубая сумма строки: цена × кол-во (+монтаж отлива, − вычет).
                // Точные суммы посчитает OrderItem.Recalculate после добавления.
                double base_ = s.Price * s.Quantity;
                if (s.InstallationMode == 0 && s.Type == "Отлив")
                    base_ += OrderItem.GetDefaultInstallationAdjustment("Отлив") * Math.Max(s.Width, s.Height) / 1000.0 * s.Quantity;
                if (s.InstallationMode == 1 && s.Type == "Отлив")
                    base_ += 0; // вычет отлива по умолчанию 0
                if (s.Type == "ПСУЛ" && s.Width > 0 && s.Height > 0)
                    base_ = s.Price * Math.Round((s.Width + s.Height) * 2 / 1000.0, 3) * s.Quantity;
                return base_;
            });

            TxtSummaryCount.Text = $"Добавится позиций: {count}";
            TxtSummaryTotal.Text = total > 0 ? $"≈ {MoneyFormatService.Format(total)} ₽" : "";
            BtnApply.IsEnabled = true;
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (_template == null) return;

            string? error = _state.GetValidationError();
            if (error != null)
            {
                HighlightValidationError();
                ToastService.ShowToast(error, ToastType.Info);
                return;
            }

            var catalogPrice = (string t, string c) => _mw.PricesVM.GetPrice(t, c);
            var specs = _state.BuildItemSpecs(_template, catalogPrice);
            if (specs.Count == 0)
            {
                ToastService.ShowToast("Выберите хотя бы одну позицию.", ToastType.Info);
                return;
            }

            // Атомарное добавление: один снимок Undo на весь шаблон
            // (образец — AiPlanExecutor: один вызов = всё или ничего).
            _mw.PushUndo();

            var added = new List<OrderItem>();
            foreach (var spec in specs)
            {
                var item = _mw.CalcVM.AddItem(
                    spec.Type, spec.Color, spec.Width, spec.Height,
                    spec.Quantity, spec.Price,
                    spec.AnwisMode ?? AnwisSizeMode.Брусбокс60);
                if (item == null) continue;
                added.Add(item);

                if (spec.AnwisMode.HasValue)
                    item.SetAnwisModeQuiet(spec.AnwisMode.Value);

                if (spec.Anticat && OrderItem.AnticatApplicableProducts.Contains(spec.Type))
                    item.IsAnticat = true;

                // Цена из каталога + надбавка Антикошки — как в QuickAdd:
                // SetDefaultPrice фиксирует «каталожную» цену, чтобы ручные
                // правки в таблице распознавались как override.
                double defaultPrice = _mw.PricesVM.GetPrice(spec.Type, spec.Color);
                if (item.IsAnticat)
                    defaultPrice += OrderItem.AnticatSurcharge;
                item.SetDefaultPrice(defaultPrice);

                if (spec.InstallationMode.HasValue &&
                    ProductCatalog.IsInstallationApplicable(spec.Type))
                {
                    item.InstallationMode = spec.InstallationMode.Value;
                }

                item.RecalculateRequested += _mw.RecalculateAndUpdateTotal;
            }

            _mw.RecalculateAndUpdateTotal();
            _mw.MarkDirty();

            ToastService.ShowToast($"Шаблон «{_template.Name}» добавлен — позиций: {added.Count}.", ToastType.Success);
            Close();
        }

        private void HighlightValidationError()
        {
            // Красная рамка на конкретном поле (attempt-driven, как QuickAdd):
            // LastErrorRow — ключ строки, выставленный GetValidationError.
            string? row = _state.LastErrorRow;
            if (row == null) return;
            if (_widthBoxes.TryGetValue(row, out var w) && w != null) w.Tag = "Invalid";
            if (_heightBoxes.TryGetValue(row, out var h) && h != null) h.Tag = "Invalid";
        }

        // ─────────────────────────────────────────────────────────
        // Оболочка окна
        // ─────────────────────────────────────────────────────────

        private void BtnBack_Click(object sender, RoutedEventArgs e) => ShowGalleryView();

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
