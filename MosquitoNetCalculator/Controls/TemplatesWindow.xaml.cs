using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using MosquitoNetCalculator.Helpers;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// Окно «Шаблоны» (v3.54, замена «На завод»): витрина карточек-шаблонов →
    /// чек-лист позиций → атомарное добавление в заказ (один Undo-шаг).
    /// Часть 1: Жизненный цикл, галерея (витрина), навигация.
    /// </summary>
    public partial class TemplatesWindow : Window
    {
        private readonly MainWindow _mw;
        private readonly OrderTemplateService _state = new();
        private OrderTemplateService.OrderTemplate? _template;

        /// <summary>Высота чек-листа (запоминается между показами).</summary>
        private double _checklistHeight = 700;

        // Контролы строк чек-листа (ключ RowKey -> контрол)
        private readonly Dictionary<string, TextBox> _widthBoxes = new();
        private readonly Dictionary<string, TextBox> _heightBoxes = new();
        private readonly Dictionary<string, TextBox> _qtyBoxes = new();
        private readonly Dictionary<string, TextBox> _amountBoxes = new();
        private readonly Dictionary<string, CheckBox> _rowToggles = new();
        private readonly Dictionary<string, Border> _statusPills = new();
        private readonly Dictionary<string, TextBlock> _rowPriceLabels = new();
        private readonly Dictionary<string, StackPanel> _fieldPanelsByProduct = new();

        // Верхняя часть полей «Сетки» (тип/цвет/режим/монтаж/антикошка)
        private StackPanel? _gridTopPanel;

        public TemplatesWindow(MainWindow mw)
        {
            InitializeComponent();
            _mw = mw;
            SizeToContent = SizeToContent.Height;
            Height = double.NaN;
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

        private static readonly IReadOnlyDictionary<string, string> TemplateIconPaths = new Dictionary<string, string>
        {
            // Окно: двухстворчатое окно с подоконником/отливом снизу и одной активной створкой
            ["window"] = "M3,4 H21 V18 H3 Z M12,4 V18 M3,12 H12 M1,19 H23",

            // Балконный блок: слева высокое окно, справа высокая дверь с перемычкой и ручкой
            ["balcony"] = "M2,3 H12 V15 H2 Z M2,9 H12 M14,3 H22 V21 H14 Z M14,13 H22 M15.5,12 V14",

            // Рама (остекление / балконная рама): длинная 3-створчатая рама с козырьком сверху и отливом снизу
            ["frame"] = "M1,5 H23 M2,7 H22 V17 H2 Z M8.6,7 V17 M15.3,7 V17 M1,19 H23",

            // Француз (французский балкон / панорамные двери в пол): высокие распашные створки в пол + защитное декоративное ограждение
            ["french"] = "M3,2 H21 V22 H3 Z M12,2 V22 M9.5,12 V14 M14.5,12 V14 M2,17 H22 M4,17 V22 M8,17 V22 M12,17 V22 M16,17 V22 M20,17 V22",
        };

        private Path BuildTemplateIcon(string templateId)
        {
            var data = TemplateIconPaths.TryGetValue(templateId, out var path)
                ? path
                : TemplateIconPaths["window"];
            var icon = new Path
            {
                Data = Geometry.Parse(data),
                StrokeThickness = 1.7,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Stretch = Stretch.Uniform,
                Width = 22,
                Height = 22,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            icon.SetResourceReference(Shape.StrokeProperty, "Accent");
            return icon;
        }

        private void BuildGallery()
        {
            GalleryPanel.Children.Clear();

            foreach (var template in OrderTemplateService.All)
            {
                var card = BuildGalleryCard(template);
                GalleryPanel.Children.Add(card);
            }
        }

        private Button BuildGalleryCard(OrderTemplateService.OrderTemplate template)
        {
            var card = new Button
            {
                Style = (Style)FindResource("TemplateCard"),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Левая колонка: 36×36 иконка с подложкой
            var iconBorder = new Border
            {
                Style = (Style)FindResource("TemplateCardIcon"),
                Child = BuildTemplateIcon(template.Id)
            };
            Grid.SetColumn(iconBorder, 0);
            grid.Children.Add(iconBorder);

            // Центральная колонка: заголовок, подзаголовок и теги состава
            var contentStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 12, 0)
            };

            var headerRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var titleBlock = new TextBlock
            {
                Text = template.Name,
                FontSize = Typography.BodyLg,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimary"),
                VerticalAlignment = VerticalAlignment.Center
            };
            headerRow.Children.Add(titleBlock);

            var subtitleRow = template.IsAvailable ? template.Subtitle : "Шаблон в подготовке";
            var subtitleBlock = new TextBlock
            {
                Text = $"—  {subtitleRow}",
                FontSize = Typography.Caption,
                Foreground = (Brush)FindResource("TextMuted"),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            headerRow.Children.Add(subtitleBlock);
            contentStack.Children.Add(headerRow);

            var tagsPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 6, 0, 0)
            };

            var productCounts = new Dictionary<string, int>();
            foreach (var r in template.Rows)
            {
                string name = r.ProductName == OrderTemplateService.OrderTemplateGridProduct
                    ? (!string.IsNullOrWhiteSpace(r.DefaultProductType) ? r.DefaultProductType : "Сетка")
                    : r.ProductName;
                productCounts[name] = productCounts.TryGetValue(name, out int cnt) ? cnt + 1 : 1;
            }

            foreach (var kvp in productCounts)
            {
                string chipText = kvp.Value > 1 ? $"{kvp.Key} ×{kvp.Value}" : kvp.Key;
                var tag = new Border
                {
                    Background = (Brush)FindResource("Surface"),
                    BorderBrush = (Brush)FindResource("Border"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = (CornerRadius)FindResource("Radius.Sm"),
                    Padding = new Thickness(7, 2, 7, 2),
                    Margin = new Thickness(0, 0, 6, 2),
                    Child = new TextBlock
                    {
                        Text = chipText,
                        FontSize = Typography.Caption,
                        FontWeight = FontWeights.Medium,
                        Foreground = (Brush)FindResource("TextPrimary")
                    }
                };
                tagsPanel.Children.Add(tag);
            }
            contentStack.Children.Add(tagsPanel);
            Grid.SetColumn(contentStack, 1);
            grid.Children.Add(contentStack);

            // Правая колонка: счётчик позиций и шеврон
            var rightStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var countPill = new Border
            {
                Background = (Brush)FindResource("GhostBg"),
                BorderBrush = (Brush)FindResource("Border"),
                BorderThickness = new Thickness(1),
                CornerRadius = (CornerRadius)FindResource("Radius.Capsule"),
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = $"{template.Rows.Count} поз.",
                    FontSize = Typography.Caption,
                    FontWeight = FontWeights.Medium,
                    Foreground = (Brush)FindResource("TextMuted")
                }
            };
            rightStack.Children.Add(countPill);

            var chevron = new TextBlock
            {
                Text = "\uE76C",
                FontFamily = (FontFamily)FindResource("Font.Icon"),
                FontSize = Typography.Caption,
                Foreground = (Brush)FindResource("TextMuted"),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            rightStack.Children.Add(chevron);

            Grid.SetColumn(rightStack, 2);
            grid.Children.Add(rightStack);

            card.Content = grid;
            card.IsEnabled = template.IsAvailable;
            card.Click += (_, _) => OpenChecklist(template);
            card.ToolTip = template.IsAvailable
                ? $"Открыть шаблон «{template.Name}»"
                : "Шаблон пока недоступен";
            AutomationProperties.SetName(card, $"Шаблон {template.Name}");
            return card;
        }

        private void OpenChecklist(OrderTemplateService.OrderTemplate template)
        {
            _template = template;
            _state.InitForTemplate(template);
            TxtChecklistTitle.Text = $"Шаблон «{template.Name}»";
            TxtChecklistSubtitle.Text = template.Subtitle;
            BuildChecklist();
            ShowChecklistView();
            RefreshSummary();
        }

        private void ShowChecklistView()
        {
            GalleryView.Visibility = Visibility.Collapsed;
            ChecklistView.Visibility = Visibility.Visible;
            SizeToContent = SizeToContent.Manual;
            Height = _checklistHeight > 500 ? _checklistHeight : 700;
        }

        private void ShowGalleryView()
        {
            if (SizeToContent == SizeToContent.Manual) _checklistHeight = ActualHeight;
            ChecklistView.Visibility = Visibility.Collapsed;
            GalleryView.Visibility = Visibility.Visible;
            _template = null;
            SizeToContent = SizeToContent.Height;
            Height = double.NaN;
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
