using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace MosquitoNetCalculator.Models
{
    public partial class OrderItem
    {
        private int _installationMode; // 0 = включён, 1 = без монтажа, 2 = в конструкцию
        private double _installationDeduction = 500;   // для mode 1 (вычет)
        private double _installationSurcharge = 500;   // для mode 2 (вычет)
        private double _installationAdjustment = 0;    // v3.43.2.11: для mode 0 (signed: +добавить, -вычесть) — интуитивная конвенция

        /// <summary>
        /// Default per-piece installation deduction for mode 1 (Без монтажа).
        /// Keyed by product name. Products not listed here use the standard 500 ₽
        /// fallback (<see cref="DefaultInstallationDeductionFallback"/>).
        /// </summary>
        private static readonly Dictionary<string, double> DefaultInstallationDeductions = new()
        {
            ["Дверная сетка"] = 600,
        };

        /// <summary>Fallback deduction when the product is not in the dictionary.</summary>
        public const double DefaultInstallationDeductionFallback = 500;

        /// <summary>
        /// Returns the default installation deduction for a given product name.
        /// </summary>
        public static double GetDefaultInstallationDeduction(string productName) =>
            DefaultInstallationDeductions.GetValueOrDefault(productName, DefaultInstallationDeductionFallback);

        /// <summary>
        /// Returns the default installation surcharge for a given product name.
        /// (Same value as deduction — mirrored by design.)
        /// </summary>
        public static double GetDefaultInstallationSurcharge(string productName) =>
            GetDefaultInstallationDeduction(productName);

        /// <summary>
        /// Installation mode:
        /// 0 = монтаж включён (без изменений)
        /// 1 = без монтажа (Total - InstallationDeduction)
        /// 2 = в конструкцию (Total - InstallationSurcharge)
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
                    OnPropertyChanged(nameof(InstallationForegroundColor));
                    OnPropertyChanged(nameof(InstallationToolTip));
                    Recalculate();
                }
            }
        }

        /// <summary>Deduction applied to Total in mode 1 (Без монтажа). Default 500 ₽.</summary>
        public double InstallationDeduction
        {
            get => _installationDeduction;
            set
            {
                var clamped = Math.Max(0, value);
                if (Math.Abs(_installationDeduction - clamped) > 0.01)
                {
                    _installationDeduction = clamped;
                    OnPropertyChanged();
                    Recalculate();
                }
            }
        }

        /// <summary>Amount deducted from Total in mode 2 (В конструкцию). Default 500 ₽.</summary>
        public double InstallationSurcharge
        {
            get => _installationSurcharge;
            set
            {
                var clamped = Math.Max(0, value);
                if (Math.Abs(_installationSurcharge - clamped) > 0.01)
                {
                    _installationSurcharge = clamped;
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

        /// <summary>Sets the per-mode amount from the context-menu field.</summary>
        public void SetCurrentInstallationAmount(double value)
        {
            // v3.43.2.10: убран `if (value < 0) value = 0`. Отрицательные значения
            // теперь допустимы — mode 0 (InstallationAdjustment) использует их для
            // добавления к сумме. Modes 1/2 клампят в собственных property setters
            // (Math.Max(0, value)), так что передача отрицательного числа в режиме
            // 1 или 2 просто установит deduction/surcharge в 0.
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
        /// The deduction is multiplied by Quantity: skipping installation work
        /// on N units subtracts the per-unit fee N times (one per piece, not
        /// once per row). See GOTCHAS.md#12 for the historical bug that
        /// returned a flat fee regardless of Quantity.
        /// </summary>
        public double TotalWithDeduction
        {
            get
            {
                if (!IsInstallationApplicable) return Total;
                return _installationMode switch
                {
                    // v3.43.2.11: mode 0 supports signed adjustment (интуитивная конвенция: + добавляет, − вычитает).
                    // Положительное значение InstallationAdjustment ДОБАВЛЯЕТСЯ к Total.
                    // Отрицательное значение ВЫЧИТАЕТСЯ из Total (формула сама инвертирует знак).
                    0 => Math.Round(Math.Max(0, Total + InstallationAdjustment * Quantity), 2),
                    1 => Math.Round(Math.Max(0, Total - InstallationDeduction * Quantity), 2),
                    2 => Math.Round(Math.Max(0, Total - InstallationSurcharge * Quantity), 2),
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

        /// <summary>Tooltip for the installation toggle button.
        /// For modes 1/2 the per-unit fee is shown explicitly with «× Кол-во»
        /// so the user understands that the displayed fee is PER PIECE and the
        /// final deduction scales with Quantity. See GOTCHAS.md#12 for context.
        /// v3.43.2.11: mode 0 with non-zero adjustment shows signed amount and
        /// explains the sign convention (intuitive): positive adds, negative subtracts.</summary>
        public string InstallationToolTip => !IsInstallationApplicable
            ? "Монтаж не предусмотрен для данного товара"
            : (_installationMode == 0 && _installationAdjustment != 0
                ? (_installationAdjustment > 0
                    ? $"{InstallationLabel}, +{Services.MoneyFormatService.FormatWhole(_installationAdjustment)} руб./шт. × Кол-во (положит. — добавляется к сумме)"
                    : $"{InstallationLabel}, −{Services.MoneyFormatService.FormatWhole(-_installationAdjustment)} руб./шт. × Кол-во (отрицат. — вычитается из суммы)")
                : _installationMode == 0
                    ? $"{InstallationLabel} (нажмите для переключения)"
                    : _installationMode == 1
                        ? $"{InstallationLabel}, −{Services.MoneyFormatService.FormatWhole(InstallationDeduction)} руб./шт. × Кол-во (нажмите для переключения)"
                        : $"{InstallationLabel}, \u2212{Services.MoneyFormatService.FormatWhole(InstallationSurcharge)} руб./шт. × Кол-во (нажмите для переключения)");
    }
}
