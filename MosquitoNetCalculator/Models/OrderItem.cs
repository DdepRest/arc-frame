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
        // v3.45.0 (Phase 5 refactoring): product category HashSets moved to
        // <see cref="ProductCatalog"/>. The static fields below are kept as
        // thin proxies for backward compatibility with existing callers/tests.
        // Each HashSet is a separate read-only snapshot so accidental mutation
        // of an OrderItem proxy cannot corrupt the global catalog.

        /// <summary>
        /// Products eligible for the installation toggle.
        /// </summary>
        public static readonly HashSet<string> InstallationApplicableProducts = new(ProductCatalog.InstallationApplicableProducts);

        /// <summary>
        /// Products measured by square meters: CalculatedValue = W * H / 1_000_000.
        /// </summary>
        public static readonly HashSet<string> AreaBasedProducts = new(ProductCatalog.AreaBasedProducts);

        /// <summary>
        /// Products that need only Quantity + Price (manual sum).
        /// </summary>
        public static readonly HashSet<string> ManualPieceProducts = new(ProductCatalog.ManualPieceProducts);

        /// <summary>
        /// Manual-piece products that display ONLY the sum.
        /// </summary>
        public static readonly HashSet<string> AmountOnlyProducts = new(ProductCatalog.AmountOnlyProducts);

        /// <summary>
        /// Manual-piece products where quantity is optional.
        /// </summary>
        public static readonly HashSet<string> OptionalQuantityProducts = new(ProductCatalog.OptionalQuantityProducts);

        /// <summary>
        /// Manual-piece products that ALSO record Width as a per-row spec.
        /// </summary>
        public static readonly HashSet<string> WidthOnlyProducts = new(ProductCatalog.WidthOnlyProducts);

        /// <summary>
        /// Products that support the anti-cat fabric surcharge.
        /// </summary>
        public static readonly HashSet<string> AnticatApplicableProducts = new(ProductCatalog.AnticatApplicableProducts);

        /// <summary>Fixed surcharge for anti-cat fabric (₽ per m²).</summary>
        public const double AnticatSurcharge = 2000;
        /// <summary>Window screen products that participate in the automatic impost system.</summary>
        public static readonly HashSet<string> ImpostApplicableProducts = new(ProductCatalog.ImpostApplicableProducts);

        /// <summary>Fixed impost surcharge rate (RUB per linear meter of WIDTH).</summary>
        public const double ImpostRatePerLinearMeter = 200;

        /// <summary>Impost triggers when the ENTERED width reaches this threshold (mm).</summary>
        public const double ImpostMinWidthMm = 500;

        /// <summary>Impost triggers when the ENTERED height reaches this threshold (mm).</summary>
        public const double ImpostMinHeightMm = 1500;

        /// <summary>
        /// Products that do not have color variants.
        /// </summary>
        public static readonly HashSet<string> NoColorProducts = new(ProductCatalog.NoColorProducts);

        /// <summary>True when Color/Width/Height columns should be hidden.</summary>
        public bool IsManualPiece => ProductCatalog.IsManualPiece(Name);

        /// <summary>True when only the sum should be displayed — Qty, Width, Height, Area are hidden.</summary>
        public bool IsAmountOnly => ProductCatalog.IsAmountOnly(Name);

        /// <summary>
        /// True for products where quantity is optional and hidden in the grid
        /// when it equals the default value (1). Only the sum is shown by default;
        /// if quantity is explicitly increased, both quantity and sum are displayed.
        /// «Свой товар» rows are always quantity-optional (see
        /// <see cref="IsQuantityOptional"/>).
        /// </summary>
        public bool IsQuantityOptional =>
            IsCustomProduct || ProductCatalog.IsQuantityOptional(Name);

        /// <summary>
        /// True for ManualPiece products that additionally record Width as
        /// a per-row spec. UI gates allow Width editing for these rows while
        /// still blocking Color/Height (and Total remains 1 шт. × Price × Qty).
        /// </summary>
        public bool IsWidthOnly => ProductCatalog.IsWidthOnly(Name);

        /// <summary>
        /// True when this row is a user-added «Свой товар» (custom product):
        /// a free-form name with optional dimensions and quantity. The total is
        /// always Price × Quantity; an empty quantity treats the manual sum
        /// (Price) as the row total. Persisted so saved orders keep the behavior.
        /// Set once at creation — never toggled after.
        /// </summary>
        public bool IsCustomProduct
        {
            get => _isCustomProduct;
            internal set
            {
                if (_isCustomProduct != value)
                {
                    _isCustomProduct = value;
                    OnPropertyChanged(nameof(IsCustomProduct));
                    OnPropertyChanged(nameof(IsQuantityOptional));
                    OnPropertyChanged(nameof(Unit));
                    Recalculate();
                }
            }
        }

        /// <summary>True for products that support the installation toggle (Anwis, На навесах).</summary>
        public bool IsInstallationApplicable => ProductCatalog.IsInstallationApplicable(Name);

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
        private double _quantity = 1;
        private double _price;
        private bool _isActive = true;
        private AnwisSizeMode _anwisSizeMode = AnwisSizeMode.Брусбокс60;
        private bool _isAnticat;
        private SlopeCalculation? _slopeData;
        private double _defaultPrice = -1;             // -1 = не зафиксирована (первая установка через SetDefaultPrice)
        private bool _isCustomProduct;

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
        /// The setter applies the Anwis calc adjustment before storing for Anwis
        /// products, but stores identity (raw=calc) for non-Anwis products.
        /// </summary>
        public double ШиринаВвод
        {
            get => Размеры.ШиринаОтображение;
            set
            {
                var raw = Math.Max(0, value);
                _width = IsAnwis
                    ? AnwisSize.ОтВвода(raw, Размеры.ВысотаОтображение, _anwisSizeMode).ШиринаРасчёт
                    : raw;
                OnPropertyChanged(nameof(Размеры));
                OnPropertyChanged(nameof(ШиринаВвод));
                Recalculate();
            }
        }

        /// <summary>
        /// Height as entered by the user (raw input). For Anwis products this
        /// reverses the calc adjustment from the stored Height; for all other
        /// products it returns the stored Height unchanged.
        /// The setter applies the Anwis calc adjustment before storing for Anwis
        /// products, but stores identity (raw=calc) for non-Anwis products.
        /// </summary>
        public double ВысотаВвод
        {
            get => Размеры.ВысотаОтображение;
            set
            {
                var raw = Math.Max(0, value);
                _height = IsAnwis
                    ? AnwisSize.ОтВвода(Размеры.ШиринаОтображение, raw, _anwisSizeMode).ВысотаРасчёт
                    : raw;
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
        public AnwisSize Размеры => IsAnwis
            ? AnwisSize.ОтХранимого(_width, _height, _anwisSizeMode)
            : AnwisSize.Identity(_width, _height);

        public double Quantity
        {
            get => _quantity;
            set
            {
                // «Свой товар» rows may carry quantity 0/empty, meaning «не указано»
                // → the row shows the manually entered sum (Price × 1). All other
                // products keep the strict 0.001 floor.
                var clamped = Math.Max(IsCustomProduct ? 0 : 0.001, value);
                if (Math.Abs(_quantity - clamped) > 0.0001)
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
        /// Для не-Anwis товаров — no-op: режим не применяется к размерам.
        /// </summary>
        /// <summary>
        /// Anti-cat fabric flag. When true the product name is displayed
        /// with a "(Антикошка)" suffix and the catalog price includes the surcharge.
        /// </summary>
        public bool IsAnticat
        {
            get => _isAnticat;
            set
            {
                if (_isAnticat != value)
                {
                    _isAnticat = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(AnticatSurchargeTotal));
                    OnPropertyChanged(nameof(AnticatToolTip));
                }
            }
        }

        /// <summary>
        /// Display name shown in the grid, КП and factory text.
        /// Appends "(Антикошка)" when <see cref="IsAnticat"/> is true.
        /// </summary>
        /// <summary>
        /// Anti-cat fabric surcharge included in this row total:
        /// 2000 RUB/m2 x area (CalculatedValue) x Quantity. 0 when anti-cat is off.
        /// </summary>
        public double AnticatSurchargeTotal => IsAnticat
            ? Math.Round(AnticatSurcharge * CalculatedValue * Quantity, 2)
            : 0;

        /// <summary>
        /// Tooltip for the "AK" badge in the order grid. Shows the rate, the
        /// charged area and the total surcharge. Empty when anti-cat is off.
        /// </summary>
        public string AnticatToolTip => !IsAnticat
            ? ""
            : $"Антикошка: {AnticatSurcharge:0} ₽/м² × {(CalculatedValue * Quantity).ToString("F2", Services.MoneyFormatService.RuCulture)} м² = {Services.MoneyFormatService.Format(AnticatSurchargeTotal)} ₽ — учтена в сумме строки";

        /// <summary>
        /// Display name shown in the grid, КП and factory text.
        /// Appends "(Антикошка)" when <see cref="IsAnticat"/> is true and
        /// "(Импост)" when <see cref="HasImpost"/> is true.
        /// </summary>
        public string DisplayName =>
            (IsAnticat ? $"{Name} (Антикошка)" : Name)
            + (HasImpost ? " (Импост)" : "");

        /// <summary>
        /// True when the screen automatically receives an impost bar (импост).
        /// Derived from Name + Width + Height (calculated for Anwis, entered
        /// for the rest) — there is no way to remove the impost.
        /// </summary>
        public bool HasImpost =>
            ImpostApplies(Name, Width, Height);

        /// <summary>
        /// True when an impost applies: product eligible AND (calculated width >= 500
        /// mm OR calculated height >= 1500 mm). Single criterion helper used by both
        /// the row and the QuickAdd preview.
        /// </summary>
        public static bool ImpostApplies(string? name, double calcWidthMm, double calcHeightMm) =>
            ProductCatalog.IsImpostApplicable(name)
            && (calcWidthMm >= ImpostMinWidthMm || calcHeightMm >= ImpostMinHeightMm);

        /// <summary>
        /// Linear meters used for the impost surcharge calculation.
        /// Always based on WIDTH, regardless of which criterion (width or height)
        /// triggered the impost.
        /// </summary>
        public double ImpostLinearMeters => Math.Round(Width / 1000.0, 3);

        /// <summary>
        /// Impost surcharge for CALCULATED dimensions and quantity — single formula
        /// shared by Recalculate and the QuickAdd preview.
        /// </summary>
        public static double ImpostSurchargeFor(double calcWidthMm, double quantity) =>
            Math.Round(ImpostRatePerLinearMeter * Math.Round(calcWidthMm / 1000.0, 3) * quantity, 2);

        /// <summary>
        /// Impost surcharge included in this row Total:
        /// 200 RUB/m.p. x linear meters of CALCULATED WIDTH x Quantity. 0 when no impost.
        /// </summary>
        public double ImpostSurchargeTotal => HasImpost
            ? ImpostSurchargeFor(Width, Quantity)
            : 0;

        /// <summary>
        /// Tooltip for the "ИМ" badge in the order grid. Shows the calculated width,
        /// linear meters, rate and the total surcharge. Empty when there is no impost.
        /// </summary>
        public string ImpostToolTip => !HasImpost
            ? ""
            : $"Импост: {Width.ToString("F0", Services.MoneyFormatService.RuCulture)} мм ({ImpostLinearMeters.ToString("F3", Services.MoneyFormatService.RuCulture)} м.п.) × {ImpostRatePerLinearMeter:0} ₽/м.п. × {Quantity.ToString("G", Services.MoneyFormatService.RuCulture)} = {Services.MoneyFormatService.Format(ImpostSurchargeTotal)} ₽ — учтён в сумме строки";

        public AnwisSizeMode AnwisSizeMode
        {
            get => _anwisSizeMode;
            set
            {
                if (_anwisSizeMode == value)
                    return;

                // Only recalculate dimensions for Anwis products.
                if (IsAnwis)
                {
                    // Capture old mode before overwriting, then reverse to raw
                    // and apply new mode.
                    var oldMode = _anwisSizeMode;
                    _anwisSizeMode = value;

                    var oldSize = AnwisSize.ОтХранимого(_width, _height, oldMode);
                    var newSize = oldSize.СРежимом(value);
                    _width = newSize.ШиринаРасчёт;
                    _height = newSize.ВысотаРасчёт;

                    OnPropertyChanged(nameof(Width));
                    OnPropertyChanged(nameof(Height));
                    Recalculate();
                }
                else
                {
                    _anwisSizeMode = value;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(AnwisSizeShortLabel));
                OnPropertyChanged(nameof(AnwisSizeToolTip));
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
                InstallationAdjustment = InstallationAdjustment,
                IsAnticat = IsAnticat,
                _defaultPrice = _defaultPrice,
                _isCustomProduct = _isCustomProduct,
            };
            // v3.43.3 (review fix #1): SlopeData идёт через property setter, чтобы
            // подписаться на PropertyChanged каскад (иначе клон не будет реагировать
            // на ручные правки Количества/Цены в панели откоса). Кроме того,
            // раньше клон ШАРИЛ тот же SlopeData instance с оригиналом — любая правка
            // материала затрагивала ОБА, что очень неочевидно. Теперь setter вызывается,
            // а engine сделает deep-clone через SlopeCalculationData.
            copy.SlopeData = _slopeData != null ? _slopeData.DeepClone() : null;
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

        /// <summary>
        /// Данные расчёта откоса (null для обычных товаров).
        /// </summary>
        public SlopeCalculation? SlopeData
        {
            get => _slopeData;
            set
            {
                // v3.43.3: при смене SlopeData отписываемся от старого и подписываемся на новый.
                // Cascade — когда SlopeCalculation.TotalMaterials/TotalLabor меняются
                // (пользователь руками правит Кол-во/Цену в панели откоса), мы
                // обязаны пересчитать собственный Total, иначе DataGrid и КП
                // покажут устаревшие суммы.
                if (_slopeData != null)
                    _slopeData.PropertyChanged -= OnSlopeDataPropertyChanged;
                _slopeData = value;
                if (_slopeData != null)
                    _slopeData.PropertyChanged += OnSlopeDataPropertyChanged;
                OnPropertyChanged();
                Recalculate();
            }
        }

        /// <summary>
        /// v3.43.3: каскадный обработчик PropertyChanged от SlopeData.
        /// Форсирует Recalculate() при изменениях агрегированных сумм, чтобы
        /// Total актуально отражал ручные правки в панели откоса.
        /// </summary>
        private void OnSlopeDataPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SlopeCalculation.GrandTotal)
                || e.PropertyName == nameof(SlopeCalculation.TotalMaterials)
                || e.PropertyName == nameof(SlopeCalculation.TotalLabor)
                || e.PropertyName == nameof(SlopeCalculation.DistributedSharedSum))
            {
                Recalculate();
            }
        }

        /// <summary>True, если товар является откосом (имеет SlopeData).</summary>
        public bool IsSlope => SlopeData != null;

        /// <summary>True when the current Price differs from the price-list default for this Name+Color.</summary>
        public bool IsPriceOverridden => _defaultPrice >= 0 && Math.Abs(Price - _defaultPrice) > 0.01;

        /// <summary>
        /// Converts this OrderItem to a serializable OrderItemData,
        /// including SlopeData when present.
        /// </summary>
        public OrderItemData ToOrderItemData()
        {
            var data = new OrderItemData
            {
                Name = Name,
                Color = Color,
                Width = Width,
                Height = Height,
                Quantity = Quantity,
                Price = Price,
                InstallationMode = InstallationMode,
                HasInstallation = InstallationMode == 0,
                InstallationDeduction = InstallationDeduction,
                InstallationSurcharge = InstallationSurcharge,
                InstallationAdjustment = InstallationAdjustment,
                IsActive = IsActive,
                AnwisSizeMode = (int)AnwisSizeMode,
                IsAnticat = IsAnticat,
                IsCustomProduct = IsCustomProduct
            };
            if (SlopeData != null)
                data.SlopeData = SlopeCalculationData.FromSlopeCalculation(SlopeData);
            return data;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? RecalculateRequested;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }


    }
}
