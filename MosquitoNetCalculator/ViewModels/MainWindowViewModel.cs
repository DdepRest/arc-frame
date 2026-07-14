using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        // Services & Sub-ViewModels
        public PrintService PrintService { get; } = new();
        public PricesViewModel PricesVM { get; } = new();
        public CalculationViewModel CalcVM { get; } = new();
        public OrdersHistoryViewModel OrdersVM { get; } = new();
        public UndoRedoService UndoRedo => UndoRedoService.Instance;

        // Data Properties
        public ClientInfo ClientInfo { get; } = new();
        public ObservableCollection<UpdateItem> Updates { get; } = new();
        public ObservableCollection<OrderItem> OrderItems => CalcVM.OrderItems;
        public ObservableCollection<PriceItem> Prices => PricesVM.Prices;

        private List<string> _productNames = new();
        public List<string> ProductNames
        {
            get => _productNames;
            private set { _productNames = value; OnPropertyChanged(); }
        }

        // State
        public string CurrentOrderId { get; set; } = Guid.NewGuid().ToString();
        public bool IsNewOrder { get; set; } = true;
        public bool SuppressPrefixSave { get; set; }

        public MainWindowViewModel()
        {
            foreach (var entry in UpdateLog.AllNewestFirst())
                Updates.Add(entry);
        }

        /// <summary>
        /// Runtime-добавление новой записи обновления.
        /// Снимает признак <see cref="UpdateItem.IsLatest"/> со старой
        /// новейшей записи и проставляет новой, затем вставляет её в начало
        /// коллекции (index 0). Старые карточки остаются в своих позициях
        /// (с индексами 1..N до сдвига), WPF лишь вставляет новый контейнер
        /// сверху — никаких ре-триггеров анимаций появления на старых.
        /// </summary>
        /// <param name="newItem">Новая запись для отображения.</param>
        public void AddNewUpdate(UpdateItem newItem)
        {
            if (newItem == null) return;

            // Pre-set the new item to true BEFORE clearing the previous one:
            // in a single block (no UI-binding flicker window where no item
            // is marked as latest). WPF bindings see: at-most-one-false then
            // at-most-one-true, never "empty".
            newItem.IsLatest = true;

            // Clear IsLatest on all current entries (normally exactly one had it).
            foreach (var existing in Updates)
                if (existing.IsLatest && !ReferenceEquals(existing, newItem))
                    existing.IsLatest = false;

            // Insert at the top — this is the NEW entry, by definition;
            // older items get re-indexed by WPF (no per-card animation replay
            // because their Tag no longer drives anything).
            Updates.Insert(0, newItem);
        }

        public void StartNewOrder()
        {
            CurrentOrderId = Guid.NewGuid().ToString();
            IsNewOrder = true;

            ClientInfo.ContractDate = DateTime.Today;
            string defaultPrefix = AppSettingsService.LoadContractPrefix();
            ClientInfo.ClientName = "";
            ClientInfo.ClientPhone = "";
            ClientInfo.ClientAddress = "";
            ClientInfo.Notes = "";
            ClientInfo.HasAdditionalKp = false;
            ClientInfo.AdditionalKps.Clear();
            ClientInfo.ContractNumber = "";

            CalcVM.UnsubscribeAll(() => { });
            CalcVM.ClearAll();
        }

        public string GenerateContractNumber(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) prefix = "1";
            return OrdersVM.GenerateContractNumber(prefix);
        }

        public void LoadPrices()
        {
            PricesVM.LoadPrices();
        }

        public void RefreshComboBoxColumns()
        {
            ProductNames = PricesVM.GetProductNames();
        }

        public List<OrderData> LoadAllOrders() => OrdersVM.LoadAllOrders();

        public void SaveOrder(OrderData order) => OrdersVM.SaveOrder(order);

        public void DeleteOrder(string orderId) => OrdersVM.DeleteOrder(orderId);

        public void ExportOrders(List<OrderData> orders, string filePath) => OrdersVM.ExportOrders(orders, filePath);

        public List<OrderData>? ReadOrdersFromFile(string filePath) => OrdersVM.ReadOrdersFromFile(filePath);

        public List<OrderData> MergeImport(List<OrderData> fileOrders) => OrdersVM.MergeImport(fileOrders);

        public string SanitizeFileName(string input) => OrdersVM.SanitizeFileName(input);

        public OrderSnapshot SnapshotItems() => new()
        {
            Items = CalcVM.SnapshotItems(),
            AdditionalKps = ClientInfo.AdditionalKps.Select(kp => kp.Clone()).ToList()
        };

        public void RestoreFromSnapshot(OrderSnapshot snapshot, Action recalculateCallback)
        {
            UndoRedo.SuppressDirtyChanges(() =>
            {
                CalcVM.UnsubscribeAll(recalculateCallback);
                CalcVM.RestoreFromSnapshot(snapshot.Items, recalculateCallback);

                ClientInfo.AdditionalKps.Clear();
                foreach (var kp in snapshot.AdditionalKps)
                    ClientInfo.AdditionalKps.Add(new AdditionalKpItem { Number = kp.Number, Amount = kp.Amount, IsActive = kp.IsActive });
            });
        }

        public void Undo(Action recalculateCallback)
        {
            if (!UndoRedo.CanUndo) return;
            var prev = UndoRedo.Undo(SnapshotItems);
            if (prev != null) RestoreFromSnapshot(prev, recalculateCallback);
        }

        public void Redo(Action recalculateCallback)
        {
            if (!UndoRedo.CanRedo) return;
            var next = UndoRedo.Redo(SnapshotItems);
            if (next != null) RestoreFromSnapshot(next, recalculateCallback);
        }

        public void MarkDirty() => UndoRedo.MarkDirty();
        public void MarkClean() => UndoRedo.MarkClean();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
