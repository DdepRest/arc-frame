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
            "Откос" => "шт.",
            "Работа за откос" => "шт.",
            "Работа" => "шт.",
            "Брус" => "шт.",
            "Пояс" => "шт.",
            "Доставка" => "шт.",
            "Материал" => "шт.",
            _ => "м²"
        };

        /// <summary>Unit of measurement for this item.</summary>
        public string Unit => GetUnit(Name);

        /// <summary>Display string for the full calculated quantity with unit.</summary>
        public string CalculatedValueDisplay
        {
            get
            {
                if (IsAmountOnly) return "";
                if (IsQuantityOptional && Quantity <= 1) return "";
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

        /// <summary>Display string for quantity (empty when 0, amount-only product, or optional-quantity product with default quantity)</summary>
        public string QuantityDisplay => Quantity > 0 && !IsAmountOnly && !(IsQuantityOptional && Quantity <= 1) ? Quantity.ToString() : "";

        /// <summary>
        /// v3.44.1: internal для принудительного пересчёта из внешних сервисов
        /// (например, после распределения общих материалов в RecalculateSealantAndTape).
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal void Recalculate()
        {
            bool totalOverridden = false;

            // Откосы: Total берётся из SlopeData
            if (SlopeData != null)
            {
                if (Name == "Откос")
                {
                    CalculatedValue = 1;
                    // v3.43.5 (bugfix): герметик/скотч — ОБЩИЕ на весь заказ, НЕ умножаются на Quantity.
                    // v3.44.1: Старт/F-планка также общие, если включена экономия (IsProfileEconomyApplied).
                    // per-window материалы: сэндвич, пена, пеноплекс, ламинатина
                    // общие: герметик, скотч, Старт, F-планка (при IsProfileEconomyApplied)
                    double perWindowSum = SlopeData.Sandwich.Sum + SlopeData.Foam.Sum
                                           + SlopeData.Penoplex.Sum + SlopeData.Laminatina.Sum;
                    if (!SlopeData.IsProfileEconomyApplied)
                    {
                        perWindowSum += SlopeData.StartProfile.Sum + SlopeData.FProfile.Sum;
                    }
                    // DistributedSharedSum — доля общей стоимости shared-материалов,
                    // распределённая пропорционально WindowCount в RecalculateSealantAndTape.
                    double sharedSum = SlopeData.DistributedSharedSum;
                    _total = Math.Round(perWindowSum * Quantity + sharedSum, 2);
                    totalOverridden = true;
                }
                else if (Name == "Работа за откос")
                {
                    CalculatedValue = 1;
                    _total = Math.Round(SlopeData.TotalLabor * Quantity, 2);
                    totalOverridden = true;
                }
            }
            else if (Name == "ПСУЛ")
            {
                if (Width == 0 && Height == 0)
                {
                    // Quantity-based: each unit = 100 ₽
                    CalculatedValue = 0;
                    _total = Math.Round(Quantity * 100.0, 2);
                    totalOverridden = true;
                }
                else
                {
                    CalculatedValue = Math.Round((Width + Height) * 2 / 1000.0, 3);
                }
            }
            else if (Name == "Уплотнение")
            {
                if (Width == 0 && Height == 0)
                {
                    // Quantity-based: Total = Quantity × Price
                    CalculatedValue = 0;
                    _total = Math.Round(Quantity * Price, 2);
                    totalOverridden = true;
                }
                else
                {
                    CalculatedValue = Math.Round((Width + Height) * 2 / 1000.0, 3);
                }
            }
            else if (Name is "Откос" or "Работа за откос" or "Работа" or "Брус" or "Пояс" or "Доставка" or "Материал")
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

            if (!totalOverridden)
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
            OnPropertyChanged(nameof(IsAmountOnly));
            OnPropertyChanged(nameof(IsWidthOnly));
            OnPropertyChanged(nameof(IsAnwis));
            OnPropertyChanged(nameof(AnwisSizeShortLabel));
            OnPropertyChanged(nameof(AnwisSizeToolTip));
            OnPropertyChanged(nameof(TotalWithDeduction));
            OnPropertyChanged(nameof(TotalWithDeductionDisplay));
            OnPropertyChanged(nameof(InstallationDisplay));
            OnPropertyChanged(nameof(KpInstallationDisplay));
            OnPropertyChanged(nameof(InstallationLabel));
            OnPropertyChanged(nameof(InstallationButtonLabel));
            OnPropertyChanged(nameof(InstallationForegroundColor));
            OnPropertyChanged(nameof(InstallationToolTip));

            RecalculateRequested?.Invoke();
        }
    }
}
