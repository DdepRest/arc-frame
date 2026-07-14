using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    public partial class PricesControl : UserControl, INotifyPropertyChanged
    {
        public DataGrid PriceDataGrid => PriceGrid;

        /// <summary>True when the bound Prices collection is empty. Drives the
        /// empty-state placeholder Visibility via the BoolToVis converter.</summary>
        public bool IsEmpty
        {
            get => _isEmpty;
            set
            {
                if (_isEmpty != value)
                {
                    _isEmpty = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEmpty)));
                }
            }
        }
        private bool _isEmpty = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public PricesControl()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is INotifyCollectionChanged oldCollection)
                oldCollection.CollectionChanged -= OnCollectionChanged;
            if (e.NewValue is MainWindow mw && mw.Prices is INotifyCollectionChanged nc)
            {
                nc.CollectionChanged -= OnCollectionChanged;
                nc.CollectionChanged += OnCollectionChanged;
                IsEmpty = mw.Prices.Count == 0;
            }
            else
            {
                IsEmpty = true;
            }
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (DataContext is MainWindow mw)
                IsEmpty = mw.Prices.Count == 0;
        }

        private void BtnSavePrices_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindow mw) return;
            mw.PricesVM.SavePrices();
            mw.RefreshComboBoxColumns();
            mw.PricesVM.ApplyPricesToOrderItems(mw.OrderItems);
            mw.UpdateTotal();
            ToastService.ShowToast("Цены сохранены в prices.json", ToastType.Success);
        }

        private void BtnResetPrices_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindow mw) return;
            if (DialogService.ShowConfirm("Сбросить все цены к значениям по умолчанию?", "Подтверждение", mw))
            {
                // ResetPrices() deletes PriceService.PricesPath (AppData) and recreates
                // defaults via LoadPrices (which auto-saves them). No extra SavePrices needed.
                mw.PricesVM.ResetPrices();
                mw.RefreshComboBoxColumns();
                mw.PricesVM.ApplyPricesToOrderItems(mw.OrderItems);
                mw.UpdateTotal();
                ToastService.ShowToast("Цены сброшены к значениям по умолчанию", ToastType.Success);
            }
        }

        private void TxtSearchPrices_TextChanged(object sender, TextChangedEventArgs e)
        {
            BtnClearPricesSearch.Visibility = string.IsNullOrEmpty(TxtSearchPrices.Text)
                ? Visibility.Collapsed : Visibility.Visible;

            var view = CollectionViewSource.GetDefaultView(PriceGrid.ItemsSource);
            if (view == null) return;
            string filter = TxtSearchPrices.Text.Trim();
            view.Filter = string.IsNullOrEmpty(filter)
                ? null
                : item =>
                {
                    if (item is PriceItem price)
                        return price.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                            || (price.Color?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
                    return true;
                };
        }

        private void BtnClearPricesSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtSearchPrices.Text = string.Empty;
            TxtSearchPrices.Focus();
        }
    }
}
