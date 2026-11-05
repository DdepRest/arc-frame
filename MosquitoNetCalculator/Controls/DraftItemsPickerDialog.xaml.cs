using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    public class DraftItemPickerViewModel : INotifyPropertyChanged
    {
        public OrderItemData ItemData { get; }
        private bool _isSelected = true;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public string Name => ItemData.Name;
        public string ColorSuffix => string.IsNullOrWhiteSpace(ItemData.Color) ? "" : $" ({ItemData.Color})";
        public string Details
        {
            get
            {
                string dim = (ItemData.Width > 0 && ItemData.Height > 0)
                    ? $"{ItemData.Width}×{ItemData.Height} мм"
                    : "";
                string qty = $"{ItemData.Quantity} {(ItemData.Name == "Уплотнение" && ItemData.Width == 0 ? "м.п." : "шт.")}";
                return string.IsNullOrEmpty(dim) ? qty : $"{dim} · {qty}";
            }
        }

        public string TotalDisplay => $"{MoneyFormatService.Format(ItemData.ToOrderItem().Total)} ₽";

        public DraftItemPickerViewModel(OrderItemData data)
        {
            ItemData = data;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class DraftItemsPickerDialog : Window
    {
        public ObservableCollection<DraftItemPickerViewModel> Items { get; } = new();
        public List<OrderItemData> SelectedItems { get; private set; } = new();

        public DraftItemsPickerDialog(OrderData draft)
        {
            InitializeComponent();
            TxtDraftDateInfo.Text = $"Черновик от {draft.UpdatedAt:dd.MM HH:mm}";

            if (draft.Items != null)
            {
                foreach (var item in draft.Items)
                {
                    Items.Add(new DraftItemPickerViewModel(item));
                }
            }

            ItemsList.ItemsSource = Items;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void ChkSelectAll_Checked(object sender, RoutedEventArgs e)
        {
            foreach (var item in Items) item.IsSelected = true;
        }

        private void ChkSelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (var item in Items) item.IsSelected = false;
        }

        private void BtnAddSelected_Click(object sender, RoutedEventArgs e)
        {
            SelectedItems = Items.Where(i => i.IsSelected).Select(i => i.ItemData).ToList();
            if (SelectedItems.Count == 0)
            {
                ToastService.ShowToast("Выберите хотя бы одну позицию.", ToastType.Info);
                return;
            }

            try { DialogResult = true; } catch (InvalidOperationException) { }
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            try { DialogResult = false; } catch (InvalidOperationException) { }
            Close();
        }
    }
}
