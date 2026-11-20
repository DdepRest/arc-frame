using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
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
        private readonly Dictionary<string, StackPanel> _splitPreviewPanels = new();
        private readonly Dictionary<string, Border> _rowCards = new();
        private readonly Dictionary<string, Border> _stepBadges = new();
        private readonly Dictionary<string, TextBlock> _titleBlocks = new();

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
                CenterOnOwner();
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

            bool isSplitApplicable = OrderTemplateService.SupportsAutoSplit(template.Id);
            PanelSplitModeSwitch.Visibility = isSplitApplicable ? Visibility.Visible : Visibility.Collapsed;
            if (isSplitApplicable)
            {
                RadModeAuto.IsChecked = _state.SplitConfig.IsAutoSplitMode;
                RadModeManual.IsChecked = !_state.SplitConfig.IsAutoSplitMode;
            }

            BuildChecklist();
            ShowChecklistView();
            RefreshSummary();
        }

        private void RadMode_Click(object sender, RoutedEventArgs e)
        {
            if (_template == null || !OrderTemplateService.SupportsAutoSplit(_template.Id))
                return;

            bool autoMode = RadModeAuto.IsChecked == true;
            if (_state.SplitConfig.IsAutoSplitMode != autoMode)
            {
                _state.SplitConfig.IsAutoSplitMode = autoMode;
                AppSettingsService.SaveTemplateAutoSplitMode(autoMode);
                BuildChecklist();
                RefreshSummary();
            }
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e) => SetAllPositionsEnabled(true);

        private void BtnClearAll_Click(object sender, RoutedEventArgs e) => SetAllPositionsEnabled(false);

        private void SetAllPositionsEnabled(bool isEnabled)
        {
            if (_template == null) return;
            bool isAutoSplit = OrderTemplateService.SupportsAutoSplit(_template.Id) && _state.SplitConfig.IsAutoSplitMode;

            foreach (var row in _template.Rows)
            {
                if (isAutoSplit && OrderTemplateService.IsSplitRow(row.RowKey))
                    continue;

                if (!row.IsCheckedLocked)
                {
                    var rowState = _state.GetOrCreateRowState(row);
                    rowState.IsEnabled = isEnabled;
                    if (_rowToggles.TryGetValue(row.RowKey, out var toggle))
                        toggle.IsChecked = isEnabled;
                    ApplyRowEnabledState(row.RowKey, isEnabled);
                }
            }

            if (isAutoSplit)
            {
                _state.SplitConfig.KozyrekEnabled = isEnabled;
                if (_rowToggles.TryGetValue("auto_kozyrek", out var toggleK))
                    toggleK.IsChecked = isEnabled;
                ApplyRowEnabledState("auto_kozyrek", isEnabled);

                if (_template.Id == "frame")
                {
                    _state.SplitConfig.OtlivEnabled = isEnabled;
                    if (_rowToggles.TryGetValue("auto_otliv", out var toggleO))
                        toggleO.IsChecked = isEnabled;
                    ApplyRowEnabledState("auto_otliv", isEnabled);
                }

                UpdateFrameGeometryCardVisibility();
            }

            RefreshSummary();
        }

        private void ShowChecklistView()
        {
            GalleryView.Visibility = Visibility.Collapsed;
            ChecklistView.Visibility = Visibility.Visible;
            SizeToContent = SizeToContent.Manual;
            double desiredH = _checklistHeight > 500 ? _checklistHeight : 700;
            CenterOnOwner(desiredH);
        }

        private void ShowGalleryView()
        {
            if (SizeToContent == SizeToContent.Manual) _checklistHeight = ActualHeight;
            ChecklistView.Visibility = Visibility.Collapsed;
            GalleryView.Visibility = Visibility.Visible;
            _template = null;
            SizeToContent = SizeToContent.Height;
            Height = double.NaN;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => CenterOnOwner());
        }

        internal static (double Left, double Top, double Height) ComputeCenteredBounds(
            double ownerLeft, double ownerTop, double ownerWidth, double ownerHeight,
            double windowWidth, double desiredHeight,
            double workAreaLeft, double workAreaTop, double workAreaRight, double workAreaBottom)
        {
            double availableHeight = workAreaBottom - workAreaTop;
            double clampedHeight = Math.Min(desiredHeight, Math.Max(380, availableHeight - 40));

            double centerX = ownerLeft + ownerWidth / 2.0;
            double centerY = ownerTop + ownerHeight / 2.0;

            double left = centerX - windowWidth / 2.0;
            double top = centerY - clampedHeight / 2.0;

            left = Math.Max(workAreaLeft, Math.Min(left, workAreaRight - windowWidth));
            top = Math.Max(workAreaTop, Math.Min(top, workAreaBottom - clampedHeight));

            return (left, top, clampedHeight);
        }

        public void CenterOnOwner(double? targetHeight = null)
        {
            Window? owner = _mw ?? Owner as MainWindow;
            double winWidth = ActualWidth > 0 ? ActualWidth : (double.IsNaN(Width) ? 640 : Width);
            double desiredH = targetHeight ?? (ActualHeight > 0 ? ActualHeight : 500);

            double dpiScaleX = 1.0;
            double dpiScaleY = 1.0;
            if (owner != null)
            {
                var dpi = VisualTreeHelper.GetDpi(owner);
                if (dpi.DpiScaleX > 0) dpiScaleX = dpi.DpiScaleX;
                if (dpi.DpiScaleY > 0) dpiScaleY = dpi.DpiScaleY;
            }

            Rect workAreaDip;
            if (owner != null)
            {
                var helper = new WindowInteropHelper(owner);
                IntPtr hwnd = helper.Handle == IntPtr.Zero ? helper.EnsureHandle() : helper.Handle;
                var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
                if (screen != null)
                {
                    var wa = screen.WorkingArea;
                    workAreaDip = new Rect(
                        wa.Left / dpiScaleX,
                        wa.Top / dpiScaleY,
                        wa.Width / dpiScaleX,
                        wa.Height / dpiScaleY);
                }
                else
                {
                    workAreaDip = SystemParameters.WorkArea;
                }
            }
            else
            {
                workAreaDip = SystemParameters.WorkArea;
            }

            double oLeft, oTop, oWidth, oHeight;
            if (owner != null)
            {
                if (owner.WindowState == WindowState.Maximized)
                {
                    Point screenOrigin;
                    try
                    {
                        screenOrigin = owner.PointToScreen(new Point(0, 0));
                    }
                    catch
                    {
                        screenOrigin = new Point(workAreaDip.Left * dpiScaleX, workAreaDip.Top * dpiScaleY);
                    }
                    oLeft = screenOrigin.X / dpiScaleX;
                    oTop = screenOrigin.Y / dpiScaleY;
                    oWidth = owner.ActualWidth > 0 ? owner.ActualWidth : workAreaDip.Width;
                    oHeight = owner.ActualHeight > 0 ? owner.ActualHeight : workAreaDip.Height;
                }
                else
                {
                    oLeft = owner.Left;
                    oTop = owner.Top;
                    oWidth = owner.ActualWidth > 0 ? owner.ActualWidth : (double.IsNaN(owner.Width) ? 900 : owner.Width);
                    oHeight = owner.ActualHeight > 0 ? owner.ActualHeight : (double.IsNaN(owner.Height) ? 600 : owner.Height);
                }
            }
            else
            {
                oLeft = workAreaDip.Left;
                oTop = workAreaDip.Top;
                oWidth = workAreaDip.Width;
                oHeight = workAreaDip.Height;
            }

            var (newLeft, newTop, newHeight) = ComputeCenteredBounds(
                oLeft, oTop, oWidth, oHeight,
                winWidth, desiredH,
                workAreaDip.Left, workAreaDip.Top, workAreaDip.Right, workAreaDip.Bottom);

            Left = newLeft;
            Top = newTop;
            if (targetHeight.HasValue)
            {
                Height = newHeight;
            }
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
