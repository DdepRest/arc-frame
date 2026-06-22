using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// Represents a single additional КП (commercial proposal) attached to an order.
    /// Each entry has a number (e.g. "2-3") and a monetary amount.
    /// </summary>
    public class AdditionalKpItem : INotifyPropertyChanged
    {
        private string _number = string.Empty;
        private double _amount;
        private bool _isActive = true;

        /// <summary>Number/reference of the additional КП (e.g. "2-3", "КП-Доп")</summary>
        public string Number
        {
            get => _number;
            set { _number = value; OnPropertyChanged(); }
        }

        /// <summary>Monetary amount of the additional КП in rubles</summary>
        public double Amount
        {
            get => _amount;
            set { _amount = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Whether this additional KP is active (included in totals).
        /// </summary>
        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); }
        }

        public AdditionalKpItem Clone() => new()
        {
            Number = Number,
            Amount = Amount,
            IsActive = IsActive
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
