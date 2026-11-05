using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace MosquitoNetCalculator.Models
{
    public partial class OrderItem
    {
        private int _installationMode; // 0 = включён, 1 = без монтажа, 2 = в конструкцию
        private double _installationDeduction = -500;   // v3.46.1: signed convention (like Adjustment): positive adds, negative subtracts. Default −500 = subtract 500.
        private double _installationSurcharge = 0;      // Mode 2 («В конструкцию»): default 0 ₽ (цена сетки не меняется, надбавка 0)
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
            ["Уплотнение"] = 0,
        };

        /// <summary>Fallback deduction when the product is not in the dictionary.</summary>
        public const double DefaultInstallationDeductionFallback = -500;

        /// <summary>
        /// Default installation adjustment for mode 0 (Монтаж включён).
        /// For per-linear-meter products (Отлив, Козырёк) the rate is in ₽/м.п.
        /// длины изделия (v3.48.2: большая сторона, НЕ периметр).
        /// </summary>
        private static readonly Dictionary<string, double> DefaultInstallationAdjustments = new()
        {
            ["Отлив"] = 500,
            ["Козырёк"] = 750,
            ["Уплотнение"] = 160,
        };

        // Ставки Отлив/Козырёк — ₽/м.п. ДЛИНЫ изделия (большая сторона; до v3.48.2
        // был периметр). Цена товара по-прежнему площадная — см. Recalculate.

        /// <summary>
        /// Returns the default installation deduction for a given product name.
        /// </summary>
        public static double GetDefaultInstallationDeduction(string productName) =>
            DefaultInstallationDeductions.GetValueOrDefault(productName, DefaultInstallationDeductionFallback);

        /// <summary>
        /// Returns the default installation adjustment for mode 0 (Монтаж включён).
        /// For per-linear-meter products returns the rate in ₽/м.п. длины; otherwise 0.
        /// </summary>
        public static double GetDefaultInstallationAdjustment(string productName) =>
            DefaultInstallationAdjustments.GetValueOrDefault(productName, 0);

        private static readonly Dictionary<string, double> DefaultInstallationLaborRatesMode0 = new()
        {
            ["Дверная сетка"] = 600,
            ["Отлив"] = 500,
            ["Козырёк"] = 750,
            ["Уплотнение"] = 160,
        };
        public const double DefaultInstallationLaborRateMode0Fallback = 400; // 400 ₽ за сетку в режиме «Вкл» (было 500)

        private static readonly Dictionary<string, double> DefaultInstallationLaborRatesMode2 = new()
        {
            ["Уплотнение"] = 0,
        };
        public const double DefaultInstallationLaborRateMode2Fallback = 200; // 200 ₽ за сетку в режиме «В конструкцию»

        /// <summary>
        /// Базовая ставка монтажа для ЗП монтажника по товару и режиму:
        /// Режим 0 («Вкл»): 400 ₽/шт для сеток (Дверная: 600), 500/750/160 для погонажных
        /// Режим 1 («Без монтажа»): 0 ₽
        /// Режим 2 («В конструкцию»): 200 ₽/шт для сеток, 0 ₽ для погонажных
        /// </summary>
        public static double GetDefaultInstallationLaborRate(string productName, int mode)
        {
            if (mode == 1) return 0;
            if (mode == 2)
            {
                if (ProductCatalog.IsPerLinearMeter(productName)) return 0;
                return DefaultInstallationLaborRatesMode2.GetValueOrDefault(productName, DefaultInstallationLaborRateMode2Fallback);
            }
            return DefaultInstallationLaborRatesMode0.GetValueOrDefault(productName, DefaultInstallationLaborRateMode0Fallback);
        }

        /// <summary>
        /// Базовая ставка монтажа для товара (₽/шт или ₽/м.п.) в режиме 0.
        /// </summary>
        public static double GetBaseInstallationRate(string productName)
        {
            if (ProductCatalog.IsPerLinearMeter(productName))
                return DefaultInstallationAdjustments.GetValueOrDefault(productName, 0);
            return GetDefaultInstallationLaborRate(productName, 0);
        }

        private static readonly Dictionary<string, double> DefaultInstallationSurcharges = new()
        {
            ["Уплотнение"] = 160,
        };

        /// <summary>
        /// Returns the default installation surcharge for a given product name.
        /// Signed convention: positive adds, negative subtracts.
        /// For "Уплотнение": +160 ₽/м.п. (режим 2: в конструкцию).
        /// For other products (сетки, Отлив, Козырёк): 0 ₽ (цена изделия в режиме «В конструкцию» не меняется).
        /// </summary>
        public static double GetDefaultInstallationSurcharge(string productName) =>
            DefaultInstallationSurcharges.TryGetValue(productName, out double surcharge)
                ? surcharge
                : 0;

        /// <summary>
        /// True when installation for this product is priced per linear meter
        /// of the product LENGTH (longer side): Отлив/Козырёк.
        /// v3.48.2: метры монтажа считаются по длине изделия (большая сторона),
        /// а не по периметру как в v3.47.0–v3.48.1: козырёк 350×1000 → 1,0 м.п.,
        /// монтаж 750 ₽/м.п. = ровно 750 ₽. Остальные товары — поштучно.
        /// </summary>
        public bool IsInstallationPerLinearMeter => ProductCatalog.IsPerLinearMeter(Name);

        /// <summary>
        /// Linear meters used for installation cost calculation.
        /// v3.48.2: для Отлив/Козырёк это ДЛИНА изделия — большая из сторон в метрах
        /// (НЕ периметр, как было в v3.47.0–v3.48.1); для остальных товаров 1.
        /// </summary>
        public double InstallationLinearMeters
        {
            get
            {
                if (!IsInstallationPerLinearMeter) return 1.0;
                if (Name == "Уплотнение")
                {
                    return (Width == 0 && Height == 0)
                        ? 1.0
                        : Math.Round((Width + Height) * 2 / 1000.0, 3);
                }
                return Math.Round(Math.Max(Width, Height) / 1000.0, 3);
            }
        }

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
                    OnPropertyChanged(nameof(InstallationLaborTotal));
                    OnPropertyChanged(nameof(InstallationLaborDisplay));
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
                    OnPropertyChanged(nameof(CurrentInstallationAmount));
                    OnPropertyChanged(nameof(InstallationToolTip));
                    Recalculate();
                }
            }
        }

        /// <summary>v3.46.1: signed convention — positive adds to Total, negative subtracts. Default 0 ₽ for nets (no surcharge).</summary>
        public double InstallationSurcharge
        {
            get => _installationSurcharge;
            set
            {
                if (Math.Abs(_installationSurcharge - value) > 0.01)
                {
                    _installationSurcharge = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentInstallationAmount));
                    OnPropertyChanged(nameof(InstallationToolTip));
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

        public string KpInstallationDisplay => (IsLabor || ProductCatalog.IsLabor(Name))
            ? "Вкл"
            : (!IsInstallationApplicable
                ? "—"
                : _installationMode switch
                {
                    0 => "Вкл",
                    1 => "Без",
                    _ => "ВК"
                });

        /// <summary>
        /// v3.48.3: money value of installation for the whole ROW:
        /// per-mode amount × linear meters (Отлив/Козырёк — длина изделия) × Quantity.
        /// 0 when installation is not applicable or the per-mode amount is zero.
        /// Informational only — TotalWithDeduction already includes it.
        /// </summary>
        public double InstallationRowTotal => !IsInstallationApplicable || Math.Abs(CurrentInstallationAmount) < 0.01
            ? 0
            : CurrentInstallationAmount * InstallationLinearMeters * Quantity;

        /// <summary>
        /// Compact signed amount for the «Монтаж» cell (grid sub-line and КП print):
        /// "+750" / "−500" / "+2 025". Empty when not applicable or zero,
        /// so cells without installation look exactly as before v3.48.3.
        /// </summary>
        public string InstallationAmountDisplay
        {
            get
            {
                if (IsLabor || ProductCatalog.IsLabor(Name))
                {
                    return Total > 0 ? "+" + Services.MoneyFormatService.FormatWhole(Total) : "";
                }
                if (InstallationRowTotal == 0) return "";
                return InstallationRowTotal > 0
                    ? "+" + Services.MoneyFormatService.FormatWhole(InstallationRowTotal)
                    : "−" + Services.MoneyFormatService.FormatWhole(-InstallationRowTotal);
            }
        }

        /// <summary>
        /// Стоимость монтажа (ЗП) для строки заказа:
        /// Режим 0 («Вкл»): (Базовая ставка 400 ₽/шт для сеток + InstallationAdjustment) × Quantity.
        /// Режим 1 («Без монтажа»): 0 ₽.
        /// Режим 2 («В конструкцию»): 200 ₽/шт за каждую сетку × Quantity.
        /// Для погонажных (Отлив, Козырёк, Уплотнение): монтаж начисляется только в режиме 0.
        /// </summary>
        public double InstallationLaborTotal
        {
            get
            {
                if (IsLabor || ProductCatalog.IsLabor(Name))
                {
                    return Math.Round(Math.Max(0, Total), 2);
                }

                if (!IsInstallationApplicable) return 0;

                if (IsInstallationPerLinearMeter)
                {
                    if (_installationMode != 0) return 0;
                    double rate = _installationAdjustment != 0 ? _installationAdjustment : GetBaseInstallationRate(Name);
                    return Math.Round(Math.Max(0, rate * InstallationLinearMeters * Quantity), 2);
                }
                else
                {
                    if (_installationMode == 1) return 0;

                    double baseRate = GetDefaultInstallationLaborRate(Name, _installationMode);
                    double adjustment = _installationMode == 0 ? _installationAdjustment : 0;
                    double unitRate = Math.Max(0, baseRate + adjustment);
                    return Math.Round(Math.Max(0, unitRate * Quantity), 2);
                }
            }
        }

        /// <summary>
        /// Отображение суммы монтажа по строке («частное» для КП и подсказок).
        /// </summary>
        public string InstallationLaborDisplay => InstallationLaborTotal > 0
            ? Services.MoneyFormatService.FormatWhole(InstallationLaborTotal) + " руб."
            : "—";

        /// <summary>Single-line text of the КП print column: label + row amount ("Вкл +750", "Без −500", "ВК −500");
        /// bare label when amount is 0; "—" when not applicable.</summary>
        public string KpInstallationCellText => InstallationAmountDisplay == ""
            ? KpInstallationDisplay
            : $"{KpInstallationDisplay} {InstallationAmountDisplay}";

        /// <summary>
        /// The effective total after applying the installation adjustment.
        /// All three modes use the same signed convention: positive value adds,
        /// negative subtracts. Formula: Total + value × InstallationLinearMeters × Quantity,
        /// где InstallationLinearMeters = длина изделия (м) для Отлив/Козырёк (v3.48.2), иначе 1.
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
                if (IsLabor || ProductCatalog.IsLabor(Name)) return GetThemedBrush("InstallGreen");
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

        /// <summary>Short readable label shown on the installation toggle button in the DataGrid.
        /// UX-06: cryptic V/X/В glyphs forced users into tooltips; short words are
        /// self-explanatory. Print column in КП is unified with these labels (KpInstallationDisplay).
        /// «ВК» = «в конструкцию», disambiguated by tooltip + accent color.</summary>
        public string InstallationButtonLabel
        {
            get
            {
                if (IsLabor || ProductCatalog.IsLabor(Name)) return "Вкл";
                if (!IsInstallationApplicable) return "—";
                return _installationMode switch
                {
                    0 => "Вкл",
                    1 => "Без",
                    _ => "ВК"
                };
            }
        }

        /// <summary>Tooltip for the installation toggle button.
        /// All modes use signed convention: positive adds, negative subtracts.
        /// v3.46.1: per-unit fee shown with «× Кол-во» so the user understands
        /// the deduction scales with Quantity.
        /// v3.47.0: per-linear-meter products show «× м.п. × Кол-во».</summary>
        public string InstallationToolTip
        {
            get
            {
                if (IsLabor || ProductCatalog.IsLabor(Name))
                    return $"Монтажные работы: +{Services.MoneyFormatService.FormatWhole(Total)} руб. (включено в общую сумму монтажа)";
                if (!IsInstallationApplicable)
                    return "Монтаж не предусмотрен для данного товара";
                return GetSignedTooltip(InstallationLabel, CurrentInstallationAmount, IsInstallationPerLinearMeter, InstallationLinearMeters);
            }
        }

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
