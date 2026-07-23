using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace MosquitoNetCalculator.Models
{
    public partial class OrderItem
    {
        private int _installationMode; // 0 = включён, 1 = без монтажа, 2 = в конструкцию
        private double _installationDeduction = -500;   // v3.46.1: signed convention (like Adjustment): positive adds, negative subtracts. Default −500 = subtract 500.
        private double _installationSurcharge = -500;   // v3.46.1: same signed convention.
        private double _installationAdjustment = 0;    // v3.43.2.11: для mode 0 (signed: +добавить, -вычесть) — интуитивная конвенция

        /// <summary>
        /// Default per-piece installation deduction for mode 1 (Без монтажа).
        /// Keyed by product name. Products not listed here use the standard 500 ₽
        /// fallback (<see cref="DefaultInstallationDeductionFallback"/>).
        /// v3.47.0: Отлив and Козырёк default to 0 ₽ because their installation
        /// is optional and starts disabled.
        /// </summary>
        private static readonly Dictionary<string, double> DefaultInstallationDeductions = new()
        {
            ["Дверная сетка"] = -600,
            ["Отлив"] = 0,
            ["Козырёк"] = 0,
        };

        /// <summary>Fallback deduction when the product is not in the dictionary.</summary>
        public const double DefaultInstallationDeductionFallback = -500;

        /// <summary>
        /// Default installation adjustment for mode 0 (Монтаж включён).
        /// For per-linear-meter products (Отлив, Козырёк) the rate is in ₽/м.п.
        /// </summary>
        private static readonly Dictionary<string, double> DefaultInstallationAdjustments = new()
        {
            ["Отлив"] = 500,
            ["Козырёк"] = 750,
        };

        /// <summary>
        /// Default installation surcharge for mode 2 (В конструкцию).
        /// For per-linear-meter products (Отлив, Козырёк) the rate is in ₽/м.п.
        /// </summary>
        private static readonly Dictionary<string, double> DefaultInstallationSurcharges = new()
        {
            ["Отлив"] = 500,
            ["Козырёк"] = 750,
        };

        // Per-linear-meter product list is owned by ProductCatalog.PerLinearMeterProducts.
        // No local HashSet is needed — IsInstallationPerLinearMeter delegates directly.

        /// <summary>
        /// Returns the default installation deduction for a given product name.
        /// </summary>
        public static double GetDefaultInstallationDeduction(string productName) =>
            DefaultInstallationDeductions.GetValueOrDefault(productName, DefaultInstallationDeductionFallback);

        /// <summary>
        /// Returns the default installation adjustment for mode 0 (Монтаж включён).
        /// For per-linear-meter products returns the rate in ₽/м.п.; otherwise 0.
        /// </summary>
        public static double GetDefaultInstallationAdjustment(string productName) =>
            DefaultInstallationAdjustments.GetValueOrDefault(productName, 0);

        /// <summary>
        /// Returns the default installation surcharge for a given product name.
        /// For per-linear-meter products returns the rate in ₽/м.п.; otherwise
        /// mirrors the deduction value.
        /// </summary>
        public static double GetDefaultInstallationSurcharge(string productName) =>
            DefaultInstallationSurcharges.GetValueOrDefault(productName,
                GetDefaultInstallationDeduction(productName));

        /// <summary>True when installation for this product is priced per linear meter.</summary>
        public bool IsInstallationPerLinearMeter => ProductCatalog.IsPerLinearMeter(Name);

        /// <summary>
        /// Linear meters used for installation cost calculation.
        /// For per-linear-meter products this is the perimeter in meters;
        /// for per-piece products it is 1.
        /// </summary>
        public double InstallationLinearMeters => IsInstallationPerLinearMeter
            ? Math.Round((Width + Height) * 2 / 1000.0, 3)
            : 1.0;

        /// <summary>
        /// Installation mode:
        /// 0 = монтаж включён (Total + InstallationAdjustment × Quantity)
        /// 1 = без монтажа (Total + InstallationDeduction × Quantity)
        /// 2 = в конструкцию (Total + InstallationSurcharge × Quantity)
        /// v3.46.1: all three modes use signed convention: positive adds, negative subtracts.
        /// </summary>
        public int InstallationMode
        {
            get => _installationMode;
            set
            {
                var clamped = Math.Clamp(value, 0, 2);
                if (_installationMode != clamped)
                {
                    _installationMode = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(InstallationDisplay));
                    OnPropertyChanged(nameof(KpInstallationDisplay));
                    OnPropertyChanged(nameof(InstallationLabel));
                    OnPropertyChanged(nameof(InstallationButtonLabel));
                    OnPropertyChanged(nameof(InstallationForegroundColor));
                    OnPropertyChanged(nameof(InstallationToolTip));
                    Recalculate();
                }
            }
        }

        /// <summary>v3.46.1: signed convention — positive adds to Total, negative subtracts. Default −500 ₽.</summary>
        public double InstallationDeduction
        {
            get => _installationDeduction;
            set
            {
                if (Math.Abs(_installationDeduction - value) > 0.01)
                {
                    _installationDeduction = value;
                    OnPropertyChanged();
                    Recalculate();
                }
            }
        }

        /// <summary>v3.46.1: signed convention — positive adds to Total, negative subtracts. Default −500 ₽.</summary>
        public double InstallationSurcharge
        {
            get => _installationSurcharge;
            set
            {
                if (Math.Abs(_installationSurcharge - value) > 0.01)
                {
                    _installationSurcharge = value;
                    OnPropertyChanged();
                    Recalculate();
                }
            }
        }

        /// <summary>
        /// v3.43.2.11: signed adjustment for mode 0 («Монтаж включён»), ИНТУИТИВНАЯ конвенция:
        /// положительное значение ДОБАВЛЯЕТСЯ к Total (надбавка), отрицательное — ВЫЧИТАЕТСЯ
        /// (как deduction в modes 1/2). v3.43.2.10 шипнул обратную convention (моделировал deduction),
        /// но фидбэк пользователя показал, что «+ добавляет, − вычитает» гораздо привычнее.
        /// Формула: <c>TotalWithDeduction = Math.Round(Math.Max(0, Total + InstallationAdjustment × Quantity), 2)</c>.
        /// Примеры:
        ///  • adjustment=500, Q=1, Total=1800 → TotalWithDeduction=2300 (добавили 500)
        ///  • adjustment=-200, Q=1, Total=1800 → TotalWithDeduction=1600 (вычли 200)
        ///  • adjustment=0 → TotalWithDeduction=Total (без изменений)
        /// Значения НЕ клампятся к ≥0 внутри setter'а — отрицательная сумма семантически
        /// имеет смысл для режима «вычесть». Modes 1/2 клампят в собственных setters.
        /// </summary>
        public double InstallationAdjustment
        {
            get => _installationAdjustment;
            set
            {
                if (Math.Abs(_installationAdjustment - value) > 0.01)
                {
                    _installationAdjustment = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentInstallationAmount));
                    OnPropertyChanged(nameof(InstallationToolTip));
                    Recalculate();
                }
            }
        }

        /// <summary>Returns the amount shown in the context-menu field for the current mode.</summary>
        public double CurrentInstallationAmount => _installationMode switch
        {
            0 => _installationAdjustment,
            1 => _installationDeduction,
            2 => _installationSurcharge,
            _ => 0
        };

        /// <summary>Sets the per-mode amount from the context-menu field.
        /// v3.46.1: all modes accept signed values (positive = add, negative = subtract).</summary>
        public void SetCurrentInstallationAmount(double value)
        {
            if (_installationMode == 0) InstallationAdjustment = value;
            else if (_installationMode == 1) InstallationDeduction = value;
            else if (_installationMode == 2) InstallationSurcharge = value;
        }

        /// <summary>
        /// Display icon for installation status (in the product list / grid).
        /// </summary>
        public string InstallationDisplay => !IsInstallationApplicable
            ? "\u2014"
            : _installationMode switch
            {
                0 => "\u2713",
                1 => "\u2717",
                _ => "В"
            };

        public string KpInstallationDisplay => !IsInstallationApplicable
            ? "—"
            : _installationMode switch
            {
                0 => "\u2713",
                1 => "\u2717",
                _ => "В"
            };

        /// <summary>
        /// The effective total after applying the installation adjustment.
        /// All three modes use the same signed convention: positive value adds,
        /// negative subtracts. Formula: Total + value × InstallationLinearMeters × Quantity.
        /// For per-piece products InstallationLinearMeters is 1; for Отлив/Козырёк
        /// it is the perimeter in meters.
        /// See GOTCHAS.md#12 for the historical per-piece scaling bug.
        /// </summary>
        public double TotalWithDeduction
        {
            get
            {
                if (!IsInstallationApplicable) return Total;
                double factor = InstallationLinearMeters * Quantity;
                return _installationMode switch
                {
                    // v3.46.1: all modes use signed convention: positive = add, negative = subtract.
                    0 => Math.Round(Math.Max(0, Total + InstallationAdjustment * factor), 2),
                    1 => Math.Round(Math.Max(0, Total + InstallationDeduction * factor), 2),
                    2 => Math.Round(Math.Max(0, Total + InstallationSurcharge * factor), 2),
                    _ => Total
                };
            }
        }

        /// <summary>Display string for total with deduction (empty when 0)</summary>
        public string TotalWithDeductionDisplay => TotalWithDeduction > 0
            ? Services.MoneyFormatService.Format(TotalWithDeduction)
            : "";

        /// <summary>Color for the installation toggle button — live themed brush.</summary>
        public Brush InstallationForegroundColor
        {
            get
            {
                if (!IsInstallationApplicable) return GetThemedBrush("InstallGray");
                return _installationMode == 1
                    ? GetThemedBrush("InstallRed")
                    : GetThemedBrush("InstallGreen");
            }
        }

        /// <summary>Short human-readable label for the current installation mode.</summary>
        public string InstallationLabel => !IsInstallationApplicable
            ? "Монтаж не предусмотрен"
            : _installationMode switch
            {
                0 => "Монтаж включён",
                1 => "Без монтажа",
                _ => "В конструкцию"
            };

        /// <summary>Short glyph shown on the installation toggle button in the DataGrid.</summary>
        public string InstallationButtonLabel => !IsInstallationApplicable
            ? "—"
            : _installationMode switch
            {
                0 => "V",
                1 => "X",
                _ => "В"
            };

        /// <summary>Tooltip for the installation toggle button.
        /// All modes use signed convention: positive adds, negative subtracts.
        /// v3.46.1: per-unit fee shown with «× Кол-во» so the user understands
        /// the deduction scales with Quantity.
        /// v3.47.0: per-linear-meter products show «× м.п. × Кол-во».</summary>
        public string InstallationToolTip => !IsInstallationApplicable
            ? "Монтаж не предусмотрен для данного товара"
            : GetSignedTooltip(InstallationLabel, CurrentInstallationAmount, IsInstallationPerLinearMeter, InstallationLinearMeters);

        private static string GetSignedTooltip(string label, double amount, bool perMeter, double linearMeters)
        {
            if (Math.Abs(amount) < 0.01)
                return $"{label} (нажмите для переключения)";

            string unit = perMeter ? "м.п." : "шт.";
            if (perMeter)
            {
                return amount > 0
                    ? $"{label}, +{Services.MoneyFormatService.FormatWhole(amount)} руб./{unit} × {linearMeters:F2} м.п. × Кол-во (добавляется к сумме)"
                    : $"{label}, −{Services.MoneyFormatService.FormatWhole(-amount)} руб./{unit} × {linearMeters:F2} м.п. × Кол-во (вычитается из суммы)";
            }

            return amount > 0
                ? $"{label}, +{Services.MoneyFormatService.FormatWhole(amount)} руб./{unit} × Кол-во (добавляется к сумме)"
                : $"{label}, −{Services.MoneyFormatService.FormatWhole(-amount)} руб./{unit} × Кол-во (вычитается из суммы)";
        }
    }
}
