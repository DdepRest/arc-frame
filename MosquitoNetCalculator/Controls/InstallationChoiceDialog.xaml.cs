using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    public partial class InstallationChoiceDialog : Window
    {
        private readonly OrderItem _item;
        private bool _userManuallyEditedAmount = false;
        public int SelectedMode { get; private set; } = 0;
        public double? CustomAmount { get; private set; }
        public bool RememberAsDefault => ChkRememberDefault?.IsChecked == true;
        public bool DefaultAcceptedOnEscape { get; private set; }

        public InstallationChoiceDialog(OrderItem item, int initialMode = -1)
        {
            InitializeComponent();
            _item = item;

            int preselected = (initialMode is 0 or 1 or 2)
                ? initialMode
                : (item?.InstallationMode is 0 or 1 or 2 ? item.InstallationMode : 0);
            SelectedMode = preselected;

            if (item != null)
            {
                TxtItemTitle.Text = $"{item.DisplayName} {(string.IsNullOrWhiteSpace(item.Color) ? "" : $"({item.Color})")}".Trim();
                string dim = (item.Width > 0 && item.Height > 0)
                    ? $"{item.Width}×{item.Height} мм · {item.Quantity} шт."
                    : (item.Name == "Уплотнение" && item.Width == 0 && item.Height == 0)
                        ? $"{item.Quantity} м.п."
                        : $"{item.Quantity} шт.";
                TxtItemDetails.Text = dim;

                double baseRate = OrderItem.GetBaseInstallationRate(item.Name);
                string unitStr = item.IsInstallationPerLinearMeter ? "руб./м.п." : "руб.";

                if (item.IsInstallationPerLinearMeter)
                {
                    TxtMode0Desc.Text = $"Монтаж рассчитывается отдельно (+{MoneyFormatService.FormatWhole(baseRate)} {unitStr} к стоимости и ЗП)";
                    TxtMode1Desc.Text = "Клиент монтирует самостоятельно (0 руб.)";
                    if (item.InstallationSurcharge > 0)
                    {
                        TxtMode2Desc.Text = $"{item.DisplayName} монтируется в конструкцию (+{MoneyFormatService.FormatWhole(item.InstallationSurcharge)} {unitStr})";
                    }
                    else
                    {
                        TxtMode2Desc.Text = $"{item.DisplayName} монтируется в конструкцию при сборке конструкции (0 руб.)";
                    }
                }
                else
                {
                    double laborRate0 = OrderItem.GetDefaultInstallationLaborRate(item.Name, 0);
                    double deduction1 = Math.Abs(OrderItem.GetDefaultInstallationDeduction(item.Name));
                    double laborRate2 = OrderItem.GetDefaultInstallationLaborRate(item.Name, 2);

                    TxtMode0Desc.Text = $"Монтаж заложен в стоимость (+{MoneyFormatService.FormatWhole(laborRate0)} {unitStr} к ЗП)";
                    TxtMode1Desc.Text = $"Клиент монтирует самостоятельно (−{MoneyFormatService.FormatWhole(deduction1)} {unitStr})";
                    TxtMode2Desc.Text = $"Сетка ставится в раму при сборке конструкции (+{MoneyFormatService.FormatWhole(laborRate2)} {unitStr} к ЗП)";
                }
            }

            string unitSuffix = item?.IsInstallationPerLinearMeter == true ? "₽/м.п." : "₽";
            LblAdjustment.Text = $"Корректировка, {unitSuffix}:";

            double currentVal = item?.CurrentInstallationAmount ?? 0;
            if (ChkSign != null)
                ChkSign.IsChecked = currentVal >= 0;

            if (Math.Abs(currentVal) > 0.01)
                TxtAdjustment.Text = currentVal > 0 ? $"+{currentVal:F0}" : $"{currentVal:F0}";
            else
                TxtAdjustment.Text = "0";

            if (ChkSign != null)
            {
                ChkSign.Click += (_, _) =>
                {
                    _userManuallyEditedAmount = true;
                    string curText = (TxtAdjustment.Text ?? "").Trim();
                    string clean = curText.TrimStart('+', '-', '−', ' ').Trim();
                    if (string.IsNullOrEmpty(clean)) clean = "0";
                    TxtAdjustment.Text = (ChkSign.IsChecked == true) ? $"+{clean}" : $"-{clean}";
                    UpdateTotalPreview();
                };
            }

            TxtAdjustment.TextChanged += (_, _) =>
            {
                if (TxtAdjustment.IsKeyboardFocusWithin)
                {
                    _userManuallyEditedAmount = true;
                    string t = (TxtAdjustment.Text ?? "").Trim();
                    if (t.StartsWith("-") || t.StartsWith("−"))
                    {
                        if (ChkSign != null) ChkSign.IsChecked = false;
                    }
                    else if (t.StartsWith("+"))
                    {
                        if (ChkSign != null) ChkSign.IsChecked = true;
                    }
                }
                UpdateTotalPreview();
            };

            TxtAdjustment.PreviewMouseLeftButtonDown += (sender, e) =>
            {
                var tb = (TextBox)sender;
                if (!tb.IsKeyboardFocusWithin)
                {
                    tb.Focus();
                    Keyboard.Focus(tb);
                    tb.SelectAll();
                    e.Handled = true;
                }
            };
            TxtAdjustment.GotKeyboardFocus += (sender, e) =>
            {
                ((TextBox)sender).SelectAll();
            };
            TxtAdjustment.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    PickMode(SelectedMode);
                }
            };

            UpdateSelectionVisual(SelectedMode);
            UpdateTotalPreview();

            // Фокус с клавиатуры (Tab) также обновляет выбор
            BtnMode0.GotFocus += (_, _) => SelectMode(0);
            BtnMode2.GotFocus += (_, _) => SelectMode(2);
            BtnMode1.GotFocus += (_, _) => SelectMode(1);

            Loaded += (_, _) =>
            {
                switch (SelectedMode)
                {
                    case 1:
                        BtnMode1.Focus();
                        break;
                    case 2:
                        BtnMode2.Focus();
                        break;
                    default:
                        BtnMode0.Focus();
                        break;
                }
            };
        }

        private void SelectMode(int mode)
        {
            SelectedMode = mode;
            UpdateSelectionVisual(mode);

            if (!_userManuallyEditedAmount && _item != null)
            {
                double defVal = mode switch
                {
                    0 => OrderItem.GetDefaultInstallationAdjustment(_item.Name),
                    1 => OrderItem.GetDefaultInstallationDeduction(_item.Name),
                    2 => OrderItem.GetDefaultInstallationSurcharge(_item.Name),
                    _ => 0
                };
                if (Math.Abs(defVal) > 0.01)
                {
                    TxtAdjustment.Text = defVal > 0 ? $"+{defVal:F0}" : $"{defVal:F0}";
                    if (ChkSign != null) ChkSign.IsChecked = defVal >= 0;
                }
                else
                {
                    TxtAdjustment.Text = "0";
                    if (ChkSign != null) ChkSign.IsChecked = true;
                }
            }

            UpdateTotalPreview();
        }

        private void UpdateSelectionVisual(int mode)
        {
            ApplyModeCardVisual(BtnMode0, RadioOuterMode0, RadioDotMode0, TxtMode0Title, mode == 0);
            ApplyModeCardVisual(BtnMode2, RadioOuterMode2, RadioDotMode2, TxtMode2Title, mode == 2);
            ApplyModeCardVisual(BtnMode1, RadioOuterMode1, RadioDotMode1, TxtMode1Title, mode == 1);
        }

        private void ApplyModeCardVisual(Button btn, Border radio, System.Windows.Shapes.Ellipse dot, TextBlock title, bool isSelected)
        {
            if (btn == null || radio == null || dot == null || title == null) return;

            btn.Tag = isSelected ? "On" : "Off";

            if (isSelected)
            {
                radio.Background = (Brush)FindResource("Accent");
                radio.BorderBrush = (Brush)FindResource("Accent");
                dot.Visibility = Visibility.Visible;
                title.FontWeight = FontWeights.SemiBold;
                title.Foreground = (Brush)FindResource("TextPrimary");
            }
            else
            {
                radio.Background = Brushes.Transparent;
                radio.BorderBrush = (Brush)FindResource("Border");
                dot.Visibility = Visibility.Collapsed;
                title.FontWeight = FontWeights.Medium;
                title.Foreground = (Brush)FindResource("TextPrimary");
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            try { DialogResult = false; } catch (InvalidOperationException) { }
            Close();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e) => PickMode(SelectedMode);

        private void BtnMode0_Click(object sender, RoutedEventArgs e) => SelectMode(0);
        private void BtnMode2_Click(object sender, RoutedEventArgs e) => SelectMode(2);
        private void BtnMode1_Click(object sender, RoutedEventArgs e) => SelectMode(1);

        private void BtnMode0_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                PickMode(0);
                e.Handled = true;
            }
        }

        private void BtnMode2_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                PickMode(2);
                e.Handled = true;
            }
        }

        private void BtnMode1_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                PickMode(1);
                e.Handled = true;
            }
        }

        private double ParseCurrentAdjustment()
        {
            if (TxtAdjustment == null) return 0;
            string rawText = (TxtAdjustment.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(rawText)) return 0;

            bool hasExplicitMinus = rawText.StartsWith("-") || rawText.StartsWith("−") || (ChkSign?.IsChecked == false && !rawText.StartsWith("+"));
            string cleanText = rawText.TrimStart('+', '-', '−', ' ').Trim();

            if (MoneyFormatService.TryParse(cleanText, out double numVal) && double.IsFinite(numVal))
            {
                return hasExplicitMinus ? -Math.Abs(numVal) : Math.Abs(numVal);
            }
            return 0;
        }

        private void UpdateTotalPreview()
        {
            if (TxtTotalPreview == null) return;
            if (_item == null)
            {
                TxtTotalPreview.Text = "—";
                return;
            }

            double factor = (_item.InstallationLinearMeters > 0)
                ? (_item.InstallationLinearMeters * (_item.Quantity > 0 ? _item.Quantity : 1.0))
                : 1.0;
            double adjustment = ParseCurrentAdjustment();
            double preview = Math.Round(Math.Max(0, _item.Total + adjustment * factor), 2);
            TxtTotalPreview.Text = $"{MoneyFormatService.Format(preview)} ₽";
        }

        private void CommitAdjustment()
        {
            if (TxtAdjustment == null) return;
            string rawText = (TxtAdjustment.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(rawText)) return;

            double finalVal = ParseCurrentAdjustment();
            CustomAmount = finalVal;
            if (_item != null)
            {
                _item.SetCurrentInstallationAmount(finalVal);
            }
        }

        private void PickMode(int mode)
        {
            SelectedMode = mode;
            CommitAdjustment();
            try { DialogResult = true; } catch (InvalidOperationException) { }
            Close();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DefaultAcceptedOnEscape = true;
                PickMode(SelectedMode);
                e.Handled = true;
                return;
            }

            if (TxtAdjustment != null && (TxtAdjustment.IsFocused || TxtAdjustment.IsKeyboardFocusWithin))
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    PickMode(SelectedMode);
                }
                return;
            }

            if (e.Key == Key.D1 || e.Key == Key.NumPad1)
            {
                SelectMode(0);
                e.Handled = true;
            }
            else if (e.Key == Key.D2 || e.Key == Key.NumPad2)
            {
                SelectMode(2);
                e.Handled = true;
            }
            else if (e.Key == Key.D3 || e.Key == Key.NumPad3)
            {
                SelectMode(1);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                PickMode(SelectedMode);
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e) => Window_PreviewKeyDown(sender, e);
    }
}
