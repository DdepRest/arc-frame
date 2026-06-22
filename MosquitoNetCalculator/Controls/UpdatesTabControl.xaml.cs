using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// Вкладка «Обновления» — список версий с историей изменений.
    /// Вынесена из MainWindow.xaml для уменьшения размера главного окна.
    /// Биндится к Updates через DataContext (унаследованный от MainWindow).
    /// </summary>
    public partial class UpdatesTabControl : UserControl
    {
        private MainWindow? _boundWindow;
        private INotifyCollectionChanged? _boundCollection;

        public UpdatesTabControl()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Unloaded += (_, _) => UnsubscribeFromCollection();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UnsubscribeFromCollection();
            _boundWindow = DataContext as MainWindow;
            SubscribeToCollection();
            UpdateCount();
        }

        private void SubscribeToCollection()
        {
            if (_boundWindow?.Updates == null) return;
            _boundCollection = _boundWindow.Updates;
            _boundCollection.CollectionChanged += OnUpdatesChanged;
        }

        private void UnsubscribeFromCollection()
        {
            if (_boundCollection == null) return;
            _boundCollection.CollectionChanged -= OnUpdatesChanged;
            _boundCollection = null;
        }

        private void OnUpdatesChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateCount();

        private void UpdateCount()
        {
            if (TxtUpdatesCount == null) return;
            int count = _boundWindow?.Updates?.Count ?? 0;
            TxtUpdatesCount.Text = CountText(count);
        }

        /// <summary>
        /// Русская плюрализация: 1 версия, 2-4 версии, 5+ версий
        /// (с учётом особой формы 11-14: 11 версий, 12 версий, …)
        /// </summary>
        private static string CountText(int count)
        {
            int rem10 = count % 10;
            int rem100 = count % 100;
            if (rem100 is >= 11 and <= 14) return $"{count} версий";
            return rem10 switch
            {
                1 => $"{count} версия",
                2 or 3 or 4 => $"{count} версии",
                _ => $"{count} версий"
            };
        }
    }
}
