using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MosquitoNetCalculator.Models
{
    public class ClientInfo : INotifyPropertyChanged
    {
        private string _clientName = string.Empty;
        private string _clientPhone = string.Empty;
        private string _clientAddress = string.Empty;
        private string _contractNumber = string.Empty;
        private DateTime _contractDate = DateTime.Today;
        private string _notes = string.Empty;
        private bool _hasAdditionalKp;
        private bool _syncingAdditionalKps;

        public ClientInfo()
        {
            AdditionalKps = new ObservableCollection<AdditionalKpItem>();
            AdditionalKps.CollectionChanged += OnAdditionalKpsChanged;
        }

        public string ClientName
        {
            get => _clientName;
            set { if (string.Equals(_clientName, value)) return; _clientName = value; OnPropertyChanged(); }
        }

        public string ClientPhone
        {
            get => _clientPhone;
            set { if (string.Equals(_clientPhone, value)) return; _clientPhone = value; OnPropertyChanged(); }
        }

        public string ClientAddress
        {
            get => _clientAddress;
            set { if (string.Equals(_clientAddress, value)) return; _clientAddress = value; OnPropertyChanged(); }
        }

        public string ContractNumber
        {
            get => _contractNumber;
            set { if (string.Equals(_contractNumber, value)) return; _contractNumber = value; OnPropertyChanged(); }
        }

        public DateTime ContractDate
        {
            get => _contractDate;
            set { if (_contractDate == value) return; _contractDate = value; OnPropertyChanged(); }
        }

        /// <summary>Free-form notes/remarks to be displayed in the printed КП</summary>
        public string Notes
        {
            get => _notes;
            set { if (string.Equals(_notes, value)) return; _notes = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Whether the additional КП section is visible.
        /// When set to true and the list is empty, auto-adds one entry.
        /// When set to false, hides the section but preserves all КП data.
        /// </summary>
        public bool HasAdditionalKp
        {
            get => _hasAdditionalKp;
            set
            {
                if (_hasAdditionalKp == value) return;
                _hasAdditionalKp = value;
                OnPropertyChanged();

                if (!_syncingAdditionalKps)
                {
                    _syncingAdditionalKps = true;
                    if (value && AdditionalKps.Count == 0)
                        AdditionalKps.Add(new AdditionalKpItem());
                    _syncingAdditionalKps = false;
                }

                OnPropertyChanged(nameof(AdditionalKpsTotal));
            }
        }

        /// <summary>
        /// List of additional КП entries. Each has a Number and Amount.
        /// Bound to the UI via ItemsControl for add/remove/edit.
        /// </summary>
        public ObservableCollection<AdditionalKpItem> AdditionalKps { get; }

        /// <summary>
        /// Sum of all active additional КП amounts. Single source of truth for total calculation.
        /// </summary>
        public double AdditionalKpsTotal => HasAdditionalKp
            ? AdditionalKps.Where(kp => kp.IsActive).Sum(kp => kp.Amount)
            : 0;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void OnAdditionalKpsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Notify that the total depends on the list contents
            OnPropertyChanged(nameof(AdditionalKpsTotal));

            // Sync HasAdditionalKp with list state (only if not already syncing)
            if (!_syncingAdditionalKps)
            {
                _syncingAdditionalKps = true;
                bool shouldBeOn = AdditionalKps.Count > 0;
                if (shouldBeOn != _hasAdditionalKp)
                {
                    _hasAdditionalKp = shouldBeOn;
                    OnPropertyChanged(nameof(HasAdditionalKp));
                }
                _syncingAdditionalKps = false;
            }

            // Unsubscribe from removed items
            if (e.OldItems != null)
            {
                foreach (AdditionalKpItem item in e.OldItems)
                    item.PropertyChanged -= OnAdditionalKpItemChanged;
            }

            // Subscribe to new items
            if (e.NewItems != null)
            {
                foreach (AdditionalKpItem item in e.NewItems)
                    item.PropertyChanged += OnAdditionalKpItemChanged;
            }
        }

        private void OnAdditionalKpItemChanged(object? sender, PropertyChangedEventArgs e)
        {
            // When any item's Amount or IsActive changes, recalculate the total
            if (e.PropertyName == nameof(AdditionalKpItem.Amount) || e.PropertyName == nameof(AdditionalKpItem.IsActive))
                OnPropertyChanged(nameof(AdditionalKpsTotal));
        }
    }
}
