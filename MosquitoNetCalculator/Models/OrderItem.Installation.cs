using System;
using System.Windows.Media;

namespace MosquitoNetCalculator.Models
{
    public partial class OrderItem
    {
        private int _installationMode; // 0 = включён, 1 = без монтажа, 2 = в конструкцию
        private double _installationDeduction = 500;   // для mode 1 (вычет)
        private double _installationSurcharge = 500;   // для mode 2 (вычет)

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

        /// <summary>Returns the amount shown in the context-menu field for the current mode.</summary>
        public double CurrentInstallationAmount => _installationMode switch
        {
            1 => _installationDeduction,
            2 => _installationSurcharge,
            _ => 0
        };

        /// <summary>Sets the per-mode amount from the context-menu field.</summary>
        public void SetCurrentInstallationAmount(double value)
        {
            if (value < 0) value = 0;
            if (_installationMode == 1) InstallationDeduction = value;
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
        /// </summary>
        public double TotalWithDeduction
        {
            get
            {
                if (!IsInstallationApplicable) return Total;
                return _installationMode switch
                {
                    1 => Math.Round(Math.Max(0, Total - InstallationDeduction), 2),
                    2 => Math.Round(Math.Max(0, Total - InstallationSurcharge), 2),
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

        /// <summary>Tooltip for the installation toggle button</summary>
        public string InstallationToolTip => !IsInstallationApplicable
            ? "Монтаж не предусмотрен для данного товара"
            : (_installationMode == 0
                ? $"{InstallationLabel} (нажмите для переключения)"
                : _installationMode == 1
                    ? $"{InstallationLabel}, −{Services.MoneyFormatService.FormatWhole(InstallationDeduction)} руб. (нажмите для переключения)"
                    : $"{InstallationLabel}, \u2212{Services.MoneyFormatService.FormatWhole(InstallationSurcharge)} руб. (нажмите для переключения)");
    }
}
