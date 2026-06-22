using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    public partial class SendToFactoryWindow : Window
    {
        private readonly string _address;
        private readonly List<FactoryTextService.SelectableItem> _items;
        private readonly List<CheckBox> _allCheckboxes = new();
        private bool _suppressEvents;

        public SendToFactoryWindow(string address,
            List<FactoryTextService.SelectableItem> items)
        {
            InitializeComponent();
            _address = address;
            _items = items;
            BuildCheckboxes();
            UpdateCounter();
            RefreshPreview();

            Loaded += (_, _) =>
            {
                var storyboard = (Storyboard)FindResource("WindowOpenAnimation");
                Storyboard.SetTarget(storyboard, RootBorder);
                storyboard.Begin();
            };
        }

        private void BuildCheckboxes()
        {
            var productItems = _items.Where(i => i.IsOrderItem).ToList();
            var kpItems = _items.Where(i => i.IsAdditionalKp).ToList();

            // Products section
            if (productItems.Count == 0)
            {
                ProductsHeader.Visibility = Visibility.Collapsed;
            }
            else
            {
                foreach (var item in productItems)
                {
                    var cb = CreateCheckbox(item);
                    ProductsPanel.Children.Add(cb);
                }
            }

            // Additional KPs section
            if (kpItems.Count == 0)
            {
                KpHeader.Visibility = Visibility.Collapsed;
            }
            else
            {
                foreach (var item in kpItems)
                {
                    var cb = CreateCheckbox(item);
                    KpPanel.Children.Add(cb);
                }
            }
        }

        private CheckBox CreateCheckbox(FactoryTextService.SelectableItem item)
        {
            var cb = new CheckBox
            {
                DataContext = item,
                IsChecked = item.IsSelected,
                Margin = new Thickness(4, 4, 4, 4),
                Cursor = Cursors.Hand
            };

            // Single TextBlock with combined text — wraps naturally
            string fullText = string.IsNullOrEmpty(item.Detail)
                ? item.DisplayName
                : $"{item.DisplayName} — {item.Detail}";

            var panel = new TextBlock
            {
                Text = fullText,
                FontSize = 12,
                Foreground = (Brush?)Application.Current?.FindResource("TextPrimary"),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = fullText
            };

            cb.Content = panel;
            cb.Checked += (_, _) =>
            {
                if (_suppressEvents) return;
                item.IsSelected = true;
                UpdateCounter();
                RefreshPreview();
            };
            cb.Unchecked += (_, _) =>
            {
                if (_suppressEvents) return;
                item.IsSelected = false;
                UpdateCounter();
                RefreshPreview();
            };

            _allCheckboxes.Add(cb);
            return cb;
        }

        private void UpdateCounter()
        {
            int selected = _items.Count(i => i.IsSelected);
            int total = _items.Count;
            CounterTxt.Text = $"Выбрано: {selected} из {total}";
        }

        private void RefreshPreview()
        {
            string text = FactoryTextService.Generate(_address, _items);
            PreviewBox.Text = text;

            // Update stats
            int lines = string.IsNullOrEmpty(text) ? 0 : text.Count(c => c == '\n') + 1;
            LineCountTxt.Text = lines > 0 ? $"({lines} стр.)" : "";
            CharCountTxt.Text = $"Символов: {text.Length}";
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            _suppressEvents = true;
            try
            {
            foreach (var item in _items)
                item.IsSelected = true;
            foreach (var cb in _allCheckboxes)
                cb.IsChecked = true;
            }
            finally
            {
            _suppressEvents = false;
            }
            UpdateCounter();
            RefreshPreview();
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            _suppressEvents = true;
            try
            {
            foreach (var item in _items)
                item.IsSelected = false;
            foreach (var cb in _allCheckboxes)
                cb.IsChecked = false;
            }
            finally
            {
            _suppressEvents = false;
            }
            UpdateCounter();
            RefreshPreview();
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            string text = FactoryTextService.Generate(_address, _items);
            if (string.IsNullOrWhiteSpace(text))
            {
                ToastService.ShowToast("Нет выбранных позиций.", ToastType.Warning);
                return;
            }
            try
            {
                Clipboard.SetText(text);
                ToastService.ShowToast("Текст скопирован в буфер обмена!", ToastType.Success);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                ToastService.ShowToast("Не удалось скопировать текст.", ToastType.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
        private void BtnCloseFooter_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
