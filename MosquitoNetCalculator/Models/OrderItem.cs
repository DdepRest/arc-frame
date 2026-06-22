using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Models
{
    public class OrderItem : INotifyPropertyChanged
    {
        // Products eligible for the installation toggle
        private static readonly HashSet<string> InstallationApplicableProducts = new()
        {
            "Anwis", "На навесах"
        };

        /// <summary>
        /// Products measured by square meters: CalculatedValue = W * H / 1_000_000.
        /// Source of truth for the Quick-Add preview in MainWindow.xaml.cs.
        /// Add a new m²-based product here so the preview shows "X.XX м² × price × qty"
        /// instead of falling through to the per-piece default.
        /// </summary>
        public static readonly HashSet<string> AreaBasedProducts = new()
        {
            "Anwis",
            "На навесах",
            "Оконная на метал. крепл.",
            "Отлив",
            "Козырёк",
            "Короб"
        };

        /// <summary>
        /// Products that need only Quantity + Price (manual sum).
        /// Color, Width, Height columns are hidden for these products.
        /// </summary>
        public static readonly HashSet<string> ManualPieceProducts = new()
        {
            "Работа",
            "Откос материал",
            "Брус",
            "Пояс",
            "Доставка"
        };

        /// <summary>
        /// Manual-piece products that ALSO record Width as a per-row spec —
        /// Width is captured (e.g. slope material 250 mm) but does NOT enter
        /// the Total formula because CalculatedValue = 1 шт. for these products.
        /// UI gate sites (QuickAddControl, MainWindow.xaml.cs) use this set to
        /// allow Width editing while still blocking Color/Height, on top of
        /// the standard ManualPiece rules.
        /// Kept as a SEPARATE set rather than a flag on ManualPieceProducts so
        /// that the semantics of "ManualPiece" remain a simple yes/no
        /// (no width, no height, no color) and WidthOnly can grow independently.
        /// </summary>
        public static readonly HashSet<string> WidthOnlyProducts = new()
        {
            "Откос материал",
        };

        /// <summary>
        /// Products that do not have color variants.
        /// Color dropdown is disabled for these products in QuickAdd.
        /// ПСУЛ is included because it ships without a color choice (perimeter-based).
        /// </summary>
        public static readonly HashSet<string> NoColorProducts = new()
        {
            "Работа",
            "Откос материал",
            "Брус",
            "Пояс",
            "Доставка",
            "ПСУЛ"
        };

        /// <summary>True when Color/Width/Height columns should be hidden.</summary>
        public bool IsManualPiece => ManualPieceProducts.Contains(Name);

        /// <summary>
        /// True for ManualPiece products that additionally record Width as
        /// a per-row spec. UI gates allow Width editing for these rows while
        /// still blocking Color/Height (and Total remains 1 шт. × Price × Qty).
        /// </summary>
        public bool IsWidthOnly => WidthOnlyProducts.Contains(Name);

        /// <summary>
        /// Returns a themed brush from application resources so it updates automatically when the theme changes.
        /// </summary>
        private static Brush GetThemedBrush(string resourceKey)
        {
            if (System.Windows.Application.Current?.Resources[resourceKey] is Brush brush)
                return brush;
            return Brushes.Gray;
        }

        private int _rowNumber;
        private string _name = string.Empty;
        private string _color = string.Empty;
        private double _width;
        private double _height;
        private int _quantity = 1;
        private double _calculatedValue;
        private double _price;
        private double _total;
        private bool _isActive = true;
        private int _installationMode; // 0 = включён, 1 = без монтажа, 2 = в конструкцию
        private double _installationDeduction = 500;   // для mode 1 (вычет)
        private double _installationSurcharge = 500;   // для mode 2 (вычет)
        private AnwisSizeMode _anwisSizeMode = AnwisSizeMode.Брусбокс60;
        private double _defaultPrice = -1;             // -1 = не зафиксирована (первая установка через SetDefaultPrice)

        public int RowNumber
        {
            get => _rowNumber;
            set { _rowNumber = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                    Recalculate();
                }
            }
        }

        public string Color
        {
            get => _color;
            set
            {
                if (_color != value)
                {
                    _color = value;
                    OnPropertyChanged();
                    Recalculate();
                }
            }
        }

        public double Width
        {
            get => _width;
            set
            {
                var clamped = Math.Max(0, value);
                if (_width != clamped)
                {
                    _width = clamped;
                    OnPropertyChanged();
                    Recalculate();
                }
            }
        }

        public double Height
        {
            get => _height;
            set
            {
                var clamped = Math.Max(0, value);
                if (_height != clamped)
                {
                    _height = clamped;
                    OnPropertyChanged();
                    Recalculate();
                }
            }
        }

        /// <summary>
        /// Width as entered by the user (raw input). For Anwis products this
        /// reverses the calc adjustment from the stored Width; for all other
        /// products it returns the stored Width unchanged.
        /// The setter applies the Anwis calc adjustment before storing, so
        /// editing in the DataGrid preserves correct calculations.
        /// </summary>
        public double ШиринаВвод
        {
            get => Размеры.ШиринаОтображение;
            set
            {
                var raw = Math.Max(0, value);
                _width = AnwisSize.ОтВвода(raw, Размеры.ВысотаОтображение, _anwisSizeMode).ШиринаРасчёт;
                OnPropertyChanged(nameof(Размеры));
                OnPropertyChanged(nameof(ШиринаВвод));
                Recalculate();
            }
        }

        /// <summary>
        /// Height as entered by the user (raw input). For Anwis products this
        /// reverses the calc adjustment from the stored Height; for all other
        /// products it returns the stored Height unchanged.
        /// The setter applies the Anwis calc adjustment before storing, so
        /// editing in the DataGrid preserves correct calculations.
        /// </summary>
        public double ВысотаВвод
        {
            get => Размеры.ВысотаОтображение;
            set
            {
                var raw = Math.Max(0, value);
                _height = AnwisSize.ОтВвода(Размеры.ШиринаОтображение, raw, _anwisSizeMode).ВысотаРасчёт;
                OnPropertyChanged(nameof(Размеры));
                OnPropertyChanged(nameof(ВысотаВвод));
                Recalculate();
            }
        }

        /// <summary>
        /// Единый объект размеров. Предоставляет три слоя:
        /// Отображение (сырые размеры пользователя), Расчёт (для площади/цены/КП),
        /// Завод (для текста «На завод»). Для не-Anwis товаров все слои равны.
        /// </summary>
        public AnwisSize Размеры => AnwisSize.ОтХранимого(_width, _height, _anwisSizeMode);

        public int Quantity
        {
            get => _quantity;
            set
            {
                var clamped = Math.Max(1, value);
                if (_quantity != clamped)
                {
                    _quantity = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(QuantityDisplay));
                    Recalculate();
                }
            }
        }

        /// <summary>
        /// Whether the item is active (included in totals). When false, the row stays
        /// in the list but its sum is excluded from the grand total.
        /// </summary>
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TotalDisplay));
                    OnPropertyChanged(nameof(TotalWithDeduction));
                    OnPropertyChanged(nameof(TotalWithDeductionDisplay));
                    RecalculateRequested?.Invoke();
                }
            }
        }

        public double CalculatedValue
        {
            get => _calculatedValue;
            set { _calculatedValue = value; OnPropertyChanged(); }
        }

        public double Price
        {
            get => _price;
            set
            {
                var clamped = Math.Max(0, value);
                if (_price != clamped)
                {
                    _price = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsPriceOverridden));
                    Recalculate();
                }
            }
        }

        public double Total
        {
            get => _total;
            set { _total = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Unit of measurement for a given product name.
        /// - "м.п." (perimeter/length) for ПСУЛ, Уплотнение
        /// - "шт." (per piece, manual sum) for Откос материал, Работа, Брус, Пояс, Доставка
        /// - "м²" (area) for all other products
        /// </summary>
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

        /// <summary>Unit of measurement for this item (see <see cref="GetUnit"/>).</summary>
        public string Unit => GetUnit(Name);

        /// <summary>
        /// Display string for the full calculated quantity (CalculatedValue × Quantity) with unit.
        /// Empty when CalculatedValue is 0. Uses Russian culture (comma decimal separator)
        /// to stay consistent with <see cref="MoneyFormatService"/>.
        /// </summary>
        public string CalculatedValueDisplay
        {
            get
            {
                if (CalculatedValue <= 0) return "";
                double total = CalculatedValue * Quantity;
                return Unit == "шт."
                    ? $"{(int)total} {Unit}"
                    : $"{total.ToString("F3", MoneyFormatService.RuCulture)} {Unit}";
            }
        }

        /// <summary>Display string for price (empty when 0)</summary>
        public string PriceDisplay => Price > 0 ? Services.MoneyFormatService.Format(Price) : "";

        /// <summary>Display string for effective total (with installation deduction applied)</summary>
        public string TotalDisplay => TotalWithDeduction > 0 ? Services.MoneyFormatService.Format(TotalWithDeduction) : "";

        /// <summary>Display string for quantity (empty when 0)</summary>
        public string QuantityDisplay => Quantity > 0 ? Quantity.ToString() : "";

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

        /// <summary>Amount deducted from Total in mode 2 (В конструкцию). Default 0 ₽.</summary>
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
        /// Whether the installation toggle is applicable for this product.
        /// Only applies to: Anwis, На навесах.
        /// </summary>
        public bool IsInstallationApplicable => InstallationApplicableProducts.Contains(Name);

        /// <summary>
        /// Display icon for installation status (in the product list / grid).
        /// Shows "—" for non-applicable products,
        /// "✓" (green) when included (mode 0),
        /// "В" (green) when in construction (mode 2) — Cyrillic Ve letter, matches
        /// the printed КП so the grid and the printed document stay in sync,
        /// "✕" (red) when without (mode 1).
        /// </summary>
        public string InstallationDisplay => !IsInstallationApplicable
            ? "\u2014"
            : _installationMode switch
            {
                0 => "\u2713", // ✓ Монтаж включён
                1 => "\u2717", // ✕ Без монтажа
                _ => "В"        // «В конструкцию»
            };

        /// <summary>Display icon for installation status in the printed KP table.
        /// Shows "✓" when included (mode 0),
        /// "В" (Cyrillic Ve, for «В конструкцию») when in construction (mode 2),
        /// "✕" when without (mode 1),
        /// "—" when not applicable.
        /// Mode 2 uses a distinct letter glyph instead of the same checkmark
        /// as mode 0 so the printed KP disambiguates the two "yes" modes at a
        /// glance without the reader having to check the title attribute.
        /// Rendered with the explicit .install-mark class in the KP template
        /// (pure black) so it doesn't drift with theme/page colour.
        /// </summary>
        public string KpInstallationDisplay => !IsInstallationApplicable
            ? "—"
            : _installationMode switch
            {
                0 => "\u2713", // ✓ Монтаж включён
                1 => "\u2717", // ✕ Без монтажа
                _ => "В"        // «В конструкцию»
            };

        /// <summary>
        /// The effective total after applying the installation adjustment.
        /// Mode 0 (монтаж включён): Total unchanged (0).
        /// Mode 1 (без монтажа): Total - InstallationDeduction (clamped to 0).
        /// Mode 2 (в конструкцию): Total - InstallationSurcharge (clamped to 0).
        /// For non-applicable products: Total unchanged.
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

        /// <summary>Color for the installation toggle button — live themed brush.
        /// Both "yes" modes (included / in construction) use green ✓;
        /// "no" mode uses red ✕.</summary>
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

        /// <summary>
        /// Short human-readable label for the current installation mode.
        /// Source of truth for both the grid tooltip and the printed КП title
        /// attribute — keep wording here so they never drift apart.
        /// </summary>
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

        /// <summary>
        /// Режим выбора типа размера Anwis.
        /// При изменении режима Width/Height пересчитываются:
        ///  1. Обратный пересчёт из текущего режима → сырые размеры
        ///  2. Прямой пересчёт сырых размеров в новый режим
        /// Для не-Anwis товаров формулы возвращают те же значения (холостой проход).
        /// </summary>
        public AnwisSizeMode AnwisSizeMode
        {
            get => _anwisSizeMode;
            set
            {
                if (_anwisSizeMode == value)
                    return;

                // Reverse current mode to get raw dimensions,
                // then apply new mode to store calc-adjusted values.
                // Uses AnwisSize for unified conversion.
                var size = AnwisSize.ОтХранимого(_width, _height, _anwisSizeMode);
                var newSize = size.СРежимом(value);

                _anwisSizeMode = value;
                _width = newSize.ШиринаРасчёт;
                _height = newSize.ВысотаРасчёт;

                OnPropertyChanged();
                OnPropertyChanged(nameof(Width));
                OnPropertyChanged(nameof(Height));
                OnPropertyChanged(nameof(AnwisSizeShortLabel));
                OnPropertyChanged(nameof(AnwisSizeToolTip));
                Recalculate();
            }
        }

        /// <summary>
        /// Sets AnwisSizeMode without triggering the reverse/apply dimensions recalc.
        /// Used by LoadFromOrderData / AddItem / Clone — every call site that
        /// constructs an OrderItem with already-final stored Width/Height.
        ///
        /// Why: the public <see cref="AnwisSizeMode"/> setter assumes the caller
        /// is changing modes at runtime (user clicks a menu item) and tries to
        /// keep raw dimensions stable by reversing stored values via the OLD mode
        /// then re-applying the NEW mode. During load/clone/init the OLD mode is
        /// the default (<see cref="AnwisSizeMode.Брусбокс60"/>) but the stored
        /// Width/Height are already final for the loaded mode — the reverse step
        /// corrupts them. Call this method instead of assigning the property
        /// whenever you already have the correct stored dimensions in hand.
        ///
        /// The user-mode-change path (MainWindow's mode pill context menu) still
        /// uses the public setter on purpose — there, the reverse/apply is the
        /// correct behaviour: keep raw dimensions stable while letting the stored
        /// value change with the mode.
        /// </summary>
        /// <summary>
        /// Sets AnwisSizeMode without triggering the reverse/apply dimensions recalc.
        /// Used by LoadFromOrderData / AddItem / Clone — every call site that
        /// constructs an OrderItem with already-final stored Width/Height.
        ///
        /// Why: the public <see cref="AnwisSizeMode"/> setter assumes the caller
        /// is changing modes at runtime (user clicks a menu item) and tries to
        /// keep raw dimensions stable by reversing stored values via the OLD mode
        /// then re-applying the NEW mode. During load/clone/init the OLD mode is
        /// the default (<see cref="AnwisSizeMode.Брусбокс60"/>) but the stored
        /// Width/Height are already final for the loaded mode — the reverse step
        /// corrupts them. Call this method instead of assigning the property
        /// whenever you already have the correct stored dimensions in hand.
        ///
        /// Precondition: Width and Height must already hold the correct STORED
        /// values for `mode` (callers typically achieve this by feeding Width/Height
        /// into the property setters BEFORE calling this method — the setters in
        /// turn trigger Recalculate() to keep Total/CalculatedValue in sync).
        /// This method then only fires notifications for the mode-derived
        /// properties (AnwisSizeMode, AnwisSizeShortLabel, AnwisSizeToolTip);
        /// it does NOT call Recalculate() itself because no derived numeric
        /// property depends on the mode alone.
        ///
        /// The user-mode-change path (MainWindow's mode pill context menu) still
        /// uses the public setter on purpose — there, the reverse/apply IS the
        /// correct behaviour: keep raw dimensions stable while letting the stored
        /// value change with the mode.
        /// </summary>
        public void SetAnwisModeQuiet(AnwisSizeMode mode)
        {
            if (_anwisSizeMode == mode) return;
            _anwisSizeMode = mode;
            OnPropertyChanged(nameof(AnwisSizeMode));
            OnPropertyChanged(nameof(AnwisSizeShortLabel));
            OnPropertyChanged(nameof(AnwisSizeToolTip));
            OnPropertyChanged(nameof(ШиринаВвод));
            OnPropertyChanged(nameof(ВысотаВвод));
            OnPropertyChanged(nameof(Размеры));
        }

        /// <summary>True, если товар — Anwis (показывать выбор режима размера).</summary>
        public bool IsAnwis => Services.AnwisSizeService.IsApplicable(Name);

        /// <summary>Короткая метка режима для отображения в строке DataGrid.</summary>
        public string AnwisSizeShortLabel => IsAnwis
            ? Services.AnwisSizeService.ShortLabels[AnwisSizeMode]
            : "";

        /// <summary>Tooltip с описанием режима.</summary>
        public string AnwisSizeToolTip => IsAnwis
            ? $"{Services.AnwisSizeService.FullLabels[AnwisSizeMode]}: {Services.AnwisSizeService.Descriptions[AnwisSizeMode]}"
            : "";

        /// <summary>Creates a deep copy of this row for undo/redo snapshots.</summary>
        public OrderItem Clone()
        {
            var copy = new OrderItem
            {
                RowNumber = RowNumber,
                Name = Name,
                Color = Color,
                Width = Width,
                Height = Height,
                Quantity = Quantity,
                Price = Price,
                IsActive = IsActive,
                InstallationMode = InstallationMode,
                InstallationDeduction = InstallationDeduction,
                InstallationSurcharge = InstallationSurcharge,
                _defaultPrice = _defaultPrice
            };
            // SetAnwisModeQuiet — Width/Height above are already stored for this
            // instance's AnwisSizeMode. The public setter would reverse-apply
            // through the default ББ 60 and shift the clone's dimensions.
            copy.SetAnwisModeQuiet(AnwisSizeMode);
            return copy;
        }

        /// <summary>Captures the price-list default so we can detect manual overrides later.</summary>
        public void SetDefaultPrice(double defaultPrice)
        {
            _defaultPrice = defaultPrice;
            OnPropertyChanged(nameof(IsPriceOverridden));
        }

        /// <summary>True when the current Price differs from the price-list default for this Name+Color.</summary>
        public bool IsPriceOverridden => _defaultPrice >= 0 && Math.Abs(Price - _defaultPrice) > 0.01;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? RecalculateRequested;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void Recalculate()
        {
            if (Name == "ПСУЛ")
            {
                // ПСУЛ: (Width+Height)*2/1000 м.п. (perimeter in meters)
                CalculatedValue = Math.Round((Width + Height) * 2 / 1000.0, 3);
            }
            else if (Name == "Уплотнение")
            {
                CalculatedValue = Math.Round((Width + Height) * 2 / 1000.0, 3);
            }
            else if (Name == "Откос материал" || Name == "Работа" || Name == "Брус" || Name == "Пояс" || Name == "Доставка")
            {
                // Откос материал / Работа / Брус / Пояс / Доставка: no auto-calculation, value = 1 шт.
                CalculatedValue = 1;
            }
            else if (!string.IsNullOrEmpty(Name))
            {
                // Area-based products (Anwis, На навесах, Оконная на метал. крепл., Отлив, Козырёк, Короб)
                // and any future product without explicit per-piece/linear handling: W*H/1000000 м²
                // Width/Height are already calc-adjusted for Anwis (applied at AddItem / mode change).
                CalculatedValue = Math.Round(Width * Height / 1000000.0, 3);
            }
            else
            {
                CalculatedValue = 0;
            }

            _total = Math.Round(CalculatedValue * Price * Quantity, 2);

            // Notify display properties
            OnPropertyChanged(nameof(Unit));
            OnPropertyChanged(nameof(ШиринаВвод));
            OnPropertyChanged(nameof(ВысотаВвод));
            OnPropertyChanged(nameof(Размеры));
            OnPropertyChanged(nameof(CalculatedValueDisplay));
            OnPropertyChanged(nameof(PriceDisplay));
            OnPropertyChanged(nameof(TotalDisplay));
            // QuantityDisplay is notified directly in the Quantity setter

            // Notify installation-related properties
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
