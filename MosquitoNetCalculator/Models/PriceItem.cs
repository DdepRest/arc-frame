using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MosquitoNetCalculator.Models
{
    public class PriceItem : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _color = string.Empty;
        private double _price;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Color
        {
            get => _color;
            set { _color = value; OnPropertyChanged(); }
        }

        public double Price
        {
            get => _price;
            set
            {
                // Clamp negative prices to 0 — same pattern as OrderItem.Price.
                // Without this, a user could enter a negative price in the prices
                // grid, save it to prices.json, and ApplyPricesToOrderItems would
                // then propagate it to every order item, breaking the grand total.
                // NaN/Infinity are also rejected because Math.Max(0, NaN) returns
                // NaN in .NET (NaN comparisons are always false), and Infinity
                // would corrupt all downstream totals.
                // Silent reject on bad input: NaN/Infinity never reach the field,
                // so no PropertyChanged fires — UI doesn't flicker on garbage input.
                if (double.IsNaN(value) || double.IsInfinity(value)) return;
                var clamped = Math.Max(0, value);
                if (_price != clamped)
                {
                    _price = clamped;
                    OnPropertyChanged();
                }
            }
        }

        public string DisplayName => string.IsNullOrEmpty(Color) ? Name : $"{Name} ({Color})";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
