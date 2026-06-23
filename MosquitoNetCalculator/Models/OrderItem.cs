using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Models
{
    public partial class OrderItem : INotifyPropertyChanged
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

        /// <summary>True for products that support the installation toggle (Anwis, На навесах).</summary>
        public bool IsInstallationApplicable => InstallationApplicableProducts.Contains(Name);

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
        private double _price;
        private bool _isActive = true;
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


    }
}
