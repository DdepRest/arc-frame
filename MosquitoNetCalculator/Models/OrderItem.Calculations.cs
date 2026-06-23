using System;

namespace MosquitoNetCalculator.Models
{
    public partial class OrderItem
    {
        private double _calculatedValue;
        private double _total;

        public double CalculatedValue
        {
            get => _calculatedValue;
            set { _calculatedValue = value; OnPropertyChanged(); }
        }

        public double Total
        {
            get => _total;
            set { _total = value; OnPropertyChanged(); }
        }

        /// <summary>Unit of measurement for a given product name.</summary>
        public static string GetUnit(string name) => name switch
        {
            "ПСУЛ" => "м.п.",
            "Уплотнение" => "м.п.",
            "Откос материал" => "шт.",
            "Работа" => "шт.",
            "Брус" => "шт.",
            "Пояс" => "шт.",
            "Доставка" => "шт.",
            _ => "м²"
        };

        /// <summary>Unit of measurement for this item.</summary>
        public string Unit => GetUnit(Name);

        /// <summary>Display string for the full calculated quantity with unit.</summary>
        public string CalculatedValueDisplay
        {
            get
            {
                if (CalculatedValue <= 0) return "";
                double total = CalculatedValue * Quantity;
                return Unit == "шт."
                    ? $"{(int)total} {Unit}"
                    : $"{total.ToString("F3", Services.MoneyFormatService.RuCulture)} {Unit}";
            }
        }

        /// <summary>Display string for price (empty when 0)</summary>
        public string PriceDisplay => Price > 0 ? Services.MoneyFormatService.Format(Price) : "";

        /// <summary>Display string for effective total (with installation deduction applied)</summary>
        public string TotalDisplay => TotalWithDeduction > 0 ? Services.MoneyFormatService.Format(TotalWithDeduction) : "";

        /// <summary>Display string for quantity (empty when 0)</summary>
        public string QuantityDisplay => Quantity > 0 ? Quantity.ToString() : "";

        private void Recalculate()
        {
            if (Name == "ПСУЛ")
            {
                CalculatedValue = Math.Round((Width + Height) * 2 / 1000.0, 3);
            }
            else if (Name == "Уплотнение")
            {
                CalculatedValue = Math.Round((Width + Height) * 2 / 1000.0, 3);
            }
            else if (Name is "Откос материал" or "Работа" or "Брус" or "Пояс" or "Доставка")
            {
                CalculatedValue = 1;
            }
            else if (!string.IsNullOrEmpty(Name))
            {
                CalculatedValue = Math.Round(Width * Height / 1000000.0, 3);
            }
            else
            {
                CalculatedValue = 0;
            }

            _total = Math.Round(CalculatedValue * Price * Quantity, 2);

            OnPropertyChanged(nameof(Unit));
            OnPropertyChanged(nameof(ШиринаВвод));
            OnPropertyChanged(nameof(ВысотаВвод));
            OnPropertyChanged(nameof(Размеры));
            OnPropertyChanged(nameof(CalculatedValueDisplay));
            OnPropertyChanged(nameof(PriceDisplay));
            OnPropertyChanged(nameof(TotalDisplay));

            OnPropertyChanged(nameof(IsInstallationApplicable));
            OnPropertyChanged(nameof(IsManualPiece));
            OnPropertyChanged(nameof(IsWidthOnly));
            OnPropertyChanged(nameof(IsAnwis));
            OnPropertyChanged(nameof(AnwisSizeShortLabel));
            OnPropertyChanged(nameof(AnwisSizeToolTip));
            OnPropertyChanged(nameof(TotalWithDeduction));
            OnPropertyChanged(nameof(TotalWithDeductionDisplay));
            OnPropertyChanged(nameof(InstallationDisplay));
            OnPropertyChanged(nameof(KpInstallationDisplay));
            OnPropertyChanged(nameof(InstallationLabel));
            OnPropertyChanged(nameof(InstallationForegroundColor));
            OnPropertyChanged(nameof(InstallationToolTip));

            RecalculateRequested?.Invoke();
        }
    }
}
