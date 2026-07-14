using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// Одна позиция расчёта откоса (материал или работа).
    /// </summary>
    public class SlopeMaterial : INotifyPropertyChanged
    {
        private string _name = "";
        private double _quantity;
        private double _price;
        private string _unit = "";
        private bool _isQuantityOverridden;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        /// <summary>Расчётное количество (автоматическое).</summary>
        public double Quantity
        {
            get => _quantity;
            set { _quantity = value; OnPropertyChanged(); OnPropertyChanged(nameof(Sum)); OnPropertyChanged(nameof(QuantityDisplay)); }
        }

        /// <summary>Цена за единицу (из прайс-листа или ручная).</summary>
        public double Price
        {
            get => _price;
            set { _price = value; OnPropertyChanged(); OnPropertyChanged(nameof(Sum)); }
        }

        /// <summary>Единица измерения (м², шт, м.п., баллон, тюбик, моток, полоса 3 м, лист).</summary>
        public string Unit
        {
            get => _unit;
            set { _unit = value; OnPropertyChanged(); }
        }

        /// <summary>Сумма = Quantity × Price.</summary>
        public double Sum => Math.Round(Quantity * Price, 2);

        /// <summary>Отображаемое количество.</summary>
        public string QuantityDisplay => Unit == "шт."
            ? ((int)Math.Ceiling(Quantity)).ToString()
            : Quantity.ToString("F2");

        /// <summary>Примечание для отображения (напр. "было 4" для герметика с экономией).</summary>
        private string _note = "";
        public string Note
        {
            get => _note;
            set { _note = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// True, если пользователь вручную изменил количество или цену,
        /// и авто-пересчёт не должен сбрасывать эти значения.
        /// </summary>
        public bool IsQuantityOverridden
        {
            get => _isQuantityOverridden;
            set
            {
                if (_isQuantityOverridden == value) return;
                _isQuantityOverridden = value;
                OnPropertyChanged();
                // v3.43.5 (bugfix #6): SlopeCalculation.OnChildMaterialChanged уже
                // реагирует на IsQuantityOverridden и поднимает TotalMaterials/
                // TotalLabor/GrandTotal, поэтому дополнительное уведомление Sum
                // не требуется (Sum от самого флага не меняется).
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }    /// <summary>
    /// Полный расчёт откосов для одного окна/конструкции.
    /// </summary>
    public class SlopeCalculation : INotifyPropertyChanged
    {
        private double _widthMm;
        private double _heightMm;
        private double _depthM;
        private int _windowCount = 1;
        private bool _isManualOverride;

        // 10 позиций
        public SlopeMaterial Sandwich { get; } = new();    // Сэндвич (м² × ₽/м²)
        public SlopeMaterial Foam { get; } = new();         // Пена (шт × ₽)
        public SlopeMaterial Sealant { get; } = new();      // Герметик (шт × ₽)
        public SlopeMaterial Tape { get; } = new();         // Скотч (шт × ₽)
        public SlopeMaterial StartProfile { get; } = new(); // Старт (полоса 3 м × ₽)
        public SlopeMaterial FProfile { get; } = new();     // F-планка (полоса 3 м × ₽)
        public SlopeMaterial Penoplex { get; } = new();     // Пеноплекс (лист × ₽)
        public SlopeMaterial Laminatina { get; } = new();   // Ламинат (шт × ₽)
        public SlopeMaterial Labor { get; } = new();        // Работа (м.п. × ₽/м.п.)
        public SlopeMaterial LaminatinaLabor { get; } = new(); // Работа за ламинатину (шт × ₽)

        /// <summary>
        /// v3.43.3: конструктор подписывается на PropertyChanged каждого материала,
        /// чтобы при ручном редактировании Quantity/Price пользователем в панели
        /// каскадно обновлялись TotalMaterials/TotalLabor/GrandTotal и связанные
        /// с ними OrderItem.Total в DataGrid и печатном КП.
        /// </summary>
        public SlopeCalculation()
        {
            // Подписываемся на каждого ребёнка — любые изменения Quantity/Price/Sum
            // прокидываются наверх через единый обработчик.
            Sandwich.PropertyChanged += OnChildMaterialChanged;
            Foam.PropertyChanged += OnChildMaterialChanged;
            Sealant.PropertyChanged += OnChildMaterialChanged;
            Tape.PropertyChanged += OnChildMaterialChanged;
            StartProfile.PropertyChanged += OnChildMaterialChanged;
            FProfile.PropertyChanged += OnChildMaterialChanged;
            Penoplex.PropertyChanged += OnChildMaterialChanged;
            Laminatina.PropertyChanged += OnChildMaterialChanged;
            Labor.PropertyChanged += OnChildMaterialChanged;
            LaminatinaLabor.PropertyChanged += OnChildMaterialChanged;
        }

        private void OnChildMaterialChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Реагируем на изменения Quantity/Price/Sum, а также на
            // IsQuantityOverridden — переключение флага влияет на то, будет ли
            // авто-пересчёт менять количество, и UI агрегатов должен обновиться.
            if (e.PropertyName != nameof(SlopeMaterial.Quantity)
                && e.PropertyName != nameof(SlopeMaterial.Price)
                && e.PropertyName != nameof(SlopeMaterial.Sum)
                && e.PropertyName != nameof(SlopeMaterial.IsQuantityOverridden))
                return;

            // v3.43.3 (review fix #3): ребёнок поменялся → только агрегирующие суммы.
            // StartStrips/FProfileStrips зависят ТОЛЬКО от WidthMm/HeightMm (не от Quantity/Price),
            // потому в каскаде на правки юзера они не нужны — лишний шум в биндингах.
            OnPropertyChanged(nameof(TotalMaterials));
            OnPropertyChanged(nameof(TotalLabor));
            OnPropertyChanged(nameof(GrandTotal));
        }

        /// <summary>
        /// v3.43.3: сбрасывает флаги IsQuantityOverridden на всех материалах.
        /// Используется из кнопки «Пересчитать» панели откоса — пользователь
        /// решил вернуть авто-формулы вместо своих ручных правок.
        /// </summary>
        public void ResetOverrides()
        {
            Sandwich.IsQuantityOverridden = false;
            Foam.IsQuantityOverridden = false;
            Sealant.IsQuantityOverridden = false;
            Tape.IsQuantityOverridden = false;
            StartProfile.IsQuantityOverridden = false;
            FProfile.IsQuantityOverridden = false;
            Penoplex.IsQuantityOverridden = false;
            Laminatina.IsQuantityOverridden = false;
            Labor.IsQuantityOverridden = false;
            LaminatinaLabor.IsQuantityOverridden = false;
        }

        // ─── Входные параметры ───

        /// <summary>Ширина окна, мм.</summary>
        public double WidthMm
        {
            get => _widthMm;
            set { _widthMm = Math.Max(0, value); OnPropertyChanged(); OnPropertyChanged(nameof(P3)); OnPropertyChanged(nameof(P4)); OnPropertyChanged(nameof(Area)); }
        }

        /// <summary>Высота окна, мм.</summary>
        public double HeightMm
        {
            get => _heightMm;
            set { _heightMm = Math.Max(0, value); OnPropertyChanged(); OnPropertyChanged(nameof(P3)); OnPropertyChanged(nameof(P4)); OnPropertyChanged(nameof(Area)); }
        }

        /// <summary>Глубина откоса, метры.</summary>
        public double DepthM
        {
            get => _depthM;
            set { _depthM = Math.Max(0, value); OnPropertyChanged(); OnPropertyChanged(nameof(S)); }
        }

        /// <summary>Количество окон.</summary>
        public int WindowCount
        {
            get => _windowCount;
            set { _windowCount = Math.Max(1, value); OnPropertyChanged(); }
        }

        /// <summary>
        /// Доля общих материалов (герметик + скотч), отнесённая к этому откосу.
        /// Вычисляется в <see cref="SlopeCalculatorService.RecalculateSealantAndTape"/>
        /// пропорционально WindowCount, чтобы общая сумма по заказу считалась
        /// ровно один раз, а не умножалась на количество строк «Откос».
        /// </summary>
        private double _distributedSharedSum;
        public double DistributedSharedSum
        {
            get => _distributedSharedSum;
            set
            {
                // v3.44.8 (bugfix): suppress redundant PropertyChanged events to avoid
                // cascading RecalculateRequested loops when shared material distribution
                // is recomputed for multiple slope rows.
                if (Math.Abs(_distributedSharedSum - value) < 0.0001) return;
                _distributedSharedSum = value;
                OnPropertyChanged();
            }
        }

        /// <summary>True, если пользователь вручную правил расчёт.</summary>
        public bool IsManualOverride
        {
            get => _isManualOverride;
            set { _isManualOverride = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// True, если для данного откоса включена экономия на Старт/F-планку
        /// (оптимизация раскроя по всем окнам заказа).
        /// </summary>
        private bool _isProfileEconomyApplied;
        public bool IsProfileEconomyApplied
        {
            get => _isProfileEconomyApplied;
            set { _isProfileEconomyApplied = value; OnPropertyChanged(); }
        }

        // ─── Производные величины ───

        /// <summary>Периметр трёх сторон (верх + 2 бока), метры.</summary>
        public double P3 => (WidthMm + 2 * HeightMm) / 1000.0;

        /// <summary>Периметр четырёх сторон, метры.</summary>
        public double P4 => 2 * (WidthMm + HeightMm) / 1000.0;

        /// <summary>Площадь поверхности откосов, м².</summary>
        public double S => P3 * DepthM;

        /// <summary>Площадь окна, м² (для определения типа).</summary>
        public double Area => WidthMm * HeightMm / 1_000_000.0;

        /// <summary>Сумма материалов (позиции 1-7 + ламинатина).</summary>
        public double TotalMaterials =>
            Sandwich.Sum + Foam.Sum + Sealant.Sum + Tape.Sum +
            StartProfile.Sum + FProfile.Sum + Penoplex.Sum + Laminatina.Sum;

        /// <summary>Работа (позиция 8 + работа за ламинатину).</summary>
        public double TotalLabor => Labor.Sum + LaminatinaLabor.Sum;

        /// <summary>Общая сумма за один откос (материалы + работа).</summary>
        public double GrandTotal => TotalMaterials + TotalLabor;

        /// <summary>
        /// v3.43.3: Старт идёт по ПОЛНОМУ периметру (4 стороны), а не по 3.
        /// Прежняя формула OptimizeStrips(W, H) считала 3 стороны → для W=2150,H=1500
        /// выдавала 2 полосы вместо физически правильных 3.
        /// </summary>
        public int StartStrips => Services.SlopeCalculatorService.OptimizeStripsForPerimeter((int)WidthMm, (int)HeightMm);

        /// <summary>Количество полос F-планки для текущих размеров.</summary>
        public int FProfileStrips => Services.SlopeCalculatorService.OptimizeStripsForPerimeter((int)WidthMm, (int)HeightMm);

        /// <summary>Количество листов пеноплекса.</summary>
        public int PenoplexSheets => Services.SlopeCalculatorService.GetPenoplexSheets(Area);

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// DTO для сериализации SlopeCalculation.
    /// </summary>
    public class SlopeCalculationData
    {
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        public double DepthM { get; set; }
        public int WindowCount { get; set; }
        public bool IsManualOverride { get; set; }

        /// <summary>
        /// True, если для данного откоса включена экономия на Старт/F-планку.
        /// </summary>
        public bool IsProfileEconomyApplied { get; set; }

        // Все 8 позиций: количество и цена (пересчёт суммы при загрузке)
        public double SandwichQuantity { get; set; }
        public double SandwichPrice { get; set; }
        public double FoamQuantity { get; set; }
        public double FoamPrice { get; set; }
        public double SealantQuantity { get; set; }
        public double SealantPrice { get; set; }
        public double TapeQuantity { get; set; }
        public double TapePrice { get; set; }
        public double StartProfileQuantity { get; set; }
        public double StartProfilePrice { get; set; }
        public double FProfileQuantity { get; set; }
        public double FProfilePrice { get; set; }
        public double PenoplexQuantity { get; set; }
        public double PenoplexPrice { get; set; }
        public double LaminatinaQuantity { get; set; }
        public double LaminatinaPrice { get; set; }
        public double LaborQuantity { get; set; }
        public double LaborPrice { get; set; }
        public double LaminatinaLaborQuantity { get; set; }
        public double LaminatinaLaborPrice { get; set; }

        /// <summary>
        /// Создаёт SlopeCalculation из DTO.
        /// </summary>
        public SlopeCalculation ToSlopeCalculation()
        {
            var calc = new SlopeCalculation
            {
                WidthMm = WidthMm,
                HeightMm = HeightMm,
                DepthM = DepthM,
                WindowCount = WindowCount,
                IsManualOverride = IsManualOverride,
                IsProfileEconomyApplied = IsProfileEconomyApplied
            };

            calc.Sandwich.Quantity = SandwichQuantity;
            calc.Sandwich.Price = SandwichPrice;
            calc.Sandwich.Unit = "м²";
            calc.Sandwich.Name = "Сэндвич";

            calc.Foam.Quantity = FoamQuantity;
            calc.Foam.Price = FoamPrice;
            calc.Foam.Unit = "баллон";
            calc.Foam.Name = "Пена";

            calc.Sealant.Quantity = SealantQuantity;
            calc.Sealant.Price = SealantPrice;
            calc.Sealant.Unit = "тюбик";
            calc.Sealant.Name = "Герметик";

            calc.Tape.Quantity = TapeQuantity;
            calc.Tape.Price = TapePrice;
            calc.Tape.Unit = "моток";
            calc.Tape.Name = "Скотч";

            calc.StartProfile.Quantity = StartProfileQuantity;
            calc.StartProfile.Price = StartProfilePrice;
            calc.StartProfile.Unit = "полоса 3 м";
            calc.StartProfile.Name = "Старт";

            calc.FProfile.Quantity = FProfileQuantity;
            calc.FProfile.Price = FProfilePrice;
            calc.FProfile.Unit = "полоса 3 м";
            calc.FProfile.Name = "F-планка";

            calc.Penoplex.Quantity = PenoplexQuantity;
            calc.Penoplex.Price = PenoplexPrice;
            calc.Penoplex.Unit = "лист";
            calc.Penoplex.Name = "Пеноплекс";

            calc.Laminatina.Quantity = LaminatinaQuantity;
            calc.Laminatina.Price = LaminatinaPrice;
            calc.Laminatina.Unit = "шт.";
            calc.Laminatina.Name = "Ламинат";

            calc.Labor.Quantity = LaborQuantity;
            calc.Labor.Price = LaborPrice;
            calc.Labor.Unit = "м.п.";
            calc.Labor.Name = "Работа за откос";

            calc.LaminatinaLabor.Quantity = LaminatinaLaborQuantity;
            calc.LaminatinaLabor.Price = LaminatinaLaborPrice;
            calc.LaminatinaLabor.Unit = "шт.";
            calc.LaminatinaLabor.Name = "Работа за ламинат";

            return calc;
        }

        /// <summary>
        /// Создаёт DTO из SlopeCalculation.
        /// </summary>
        public static SlopeCalculationData FromSlopeCalculation(SlopeCalculation calc)
        {
            return new SlopeCalculationData
            {
                WidthMm = calc.WidthMm,
                HeightMm = calc.HeightMm,
                DepthM = calc.DepthM,
                WindowCount = calc.WindowCount,
                IsManualOverride = calc.IsManualOverride,
                IsProfileEconomyApplied = calc.IsProfileEconomyApplied,

                SandwichQuantity = calc.Sandwich.Quantity,
                SandwichPrice = calc.Sandwich.Price,
                FoamQuantity = calc.Foam.Quantity,
                FoamPrice = calc.Foam.Price,
                SealantQuantity = calc.Sealant.Quantity,
                SealantPrice = calc.Sealant.Price,
                TapeQuantity = calc.Tape.Quantity,
                TapePrice = calc.Tape.Price,
                StartProfileQuantity = calc.StartProfile.Quantity,
                StartProfilePrice = calc.StartProfile.Price,
                FProfileQuantity = calc.FProfile.Quantity,
                FProfilePrice = calc.FProfile.Price,
                PenoplexQuantity = calc.Penoplex.Quantity,
                PenoplexPrice = calc.Penoplex.Price,
                LaminatinaQuantity = calc.Laminatina.Quantity,
                LaminatinaPrice = calc.Laminatina.Price,
                LaborQuantity = calc.Labor.Quantity,
                LaborPrice = calc.Labor.Price,
                LaminatinaLaborQuantity = calc.LaminatinaLabor.Quantity,
                LaminatinaLaborPrice = calc.LaminatinaLabor.Price
            };
        }
    }
}
