using System;
using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Система шаблонов заказа (v3.54, замена кнопки «На завод»).
    /// Каталог — данные: 4 готовых шаблона («Окно», «Балконный блок», «Рама», «Француз»).
    /// Чистая бизнес-логика без WPF: валидация, расчет цен строк и формирование спецификаций.
    /// </summary>
    public class OrderTemplateService
    {
        // ─────────────────────────────────────────────────────────
        // Модели каталога
        // ─────────────────────────────────────────────────────────

        public sealed class OrderTemplate
        {
            public string Id { get; init; } = "";
            public string Name { get; init; } = "";
            public string Subtitle { get; init; } = "";
            public bool IsAvailable { get; init; } = true;
            public List<TemplateRow> Rows { get; init; } = new();
        }

        public sealed class TemplateRow
        {
            /// <summary>Уникальный ключ строки в шаблоне (например: grid, otliv_1, kozyrek_1, korob).</summary>
            public string RowKey { get; init; } = "";

            /// <summary>Имя товара из каталога (или «Сетка» / «Свой товар»).</summary>
            public string ProductName { get; init; } = "";

            /// <summary>Отображаемый заголовок карточки в чек-листе (например: «Отлив 1»).</summary>
            public string Title { get; init; } = "";

            /// <summary>Пояснение под названием строки.</summary>
            public string Hint { get; init; } = "";

            public bool DefaultChecked { get; init; } = true;

            public bool IsCheckedLocked { get; init; }

            /// <summary>Флаг «Свой товар» (ручная сумма, IsCustomProduct = true).</summary>
            public bool IsCustomProduct { get; init; }

            /// <summary>Товар по умолчанию для строки-сетки («Anwis», «Дверная сетка» и т.д.).</summary>
            public string DefaultProductType { get; init; } = "";

            /// <summary>Режим монтажа по умолчанию (2 = «В конструкцию» с наценкой 0 руб. по ТЗ).</summary>
            public int DefaultInstallationMode { get; init; } = 2;
        }

        // ─────────────────────────────────────────────────────────
        // Каталог всех 4 шаблонов
        // ─────────────────────────────────────────────────────────

        public static readonly List<OrderTemplate> All = new()
        {
            new OrderTemplate
            {
                Id = "window",
                Name = "Окно",
                Subtitle = "Сетка, отлив, лента ПСУЛ и доставка — в порядке монтажа",
                IsAvailable = true,
                Rows = new List<TemplateRow>
                {
                    new()
                    {
                        RowKey = "grid",
                        ProductName = OrderTemplateGridProduct,
                        Title = "Сетка",
                        Hint = "Москитная сетка на окно — выберите тип и размеры",
                        DefaultProductType = "Anwis",
                        DefaultChecked = true,
                        DefaultInstallationMode = 0
                    },
                    new()
                    {
                        RowKey = "otliv",
                        ProductName = "Отлив",
                        Title = "Отлив",
                        Hint = "Отлив на окно — размеры и монтаж",
                        DefaultChecked = true,
                        DefaultInstallationMode = 2
                    },
                    new()
                    {
                        RowKey = "psul",
                        ProductName = "ПСУЛ",
                        Title = "ПСУЛ",
                        Hint = "Уплотнительная лента (м.п.)",
                        DefaultChecked = true
                    },
                    new()
                    {
                        RowKey = "delivery",
                        ProductName = "Доставка",
                        Title = "Доставка",
                        Hint = "Доставка до объекта",
                        DefaultChecked = true
                    }
                }
            },
            new OrderTemplate
            {
                Id = "balcony",
                Name = "Балконный блок",
                Subtitle = "Дверная сетка, отлив, ПСУЛ и доставка",
                IsAvailable = true,
                Rows = new List<TemplateRow>
                {
                    new()
                    {
                        RowKey = "grid",
                        ProductName = OrderTemplateGridProduct,
                        Title = "Дверная сетка",
                        Hint = "Москитная сетка на балконную дверь",
                        DefaultProductType = "Дверная сетка",
                        DefaultChecked = true,
                        DefaultInstallationMode = 0
                    },
                    new()
                    {
                        RowKey = "otliv",
                        ProductName = "Отлив",
                        Title = "Отлив",
                        Hint = "Отлив на балконный блок",
                        DefaultChecked = true,
                        DefaultInstallationMode = 2
                    },
                    new()
                    {
                        RowKey = "psul",
                        ProductName = "ПСУЛ",
                        Title = "ПСУЛ",
                        Hint = "Уплотнительная лента (м.п.)",
                        DefaultChecked = true
                    },
                    new()
                    {
                        RowKey = "delivery",
                        ProductName = "Доставка",
                        Title = "Доставка",
                        Hint = "Доставка до объекта",
                        DefaultChecked = true
                    }
                }
            },
            new OrderTemplate
            {
                Id = "frame",
                Name = "Рама",
                Subtitle = "Сетка Anwis, 2 отлива, 2 козырька, брус, короб и доставка",
                IsAvailable = true,
                Rows = new List<TemplateRow>
                {
                    new()
                    {
                        RowKey = "grid",
                        ProductName = OrderTemplateGridProduct,
                        Title = "Сетка Anwis",
                        Hint = "Москитная сетка Anwis на раму",
                        DefaultProductType = "Anwis",
                        DefaultChecked = true,
                        DefaultInstallationMode = 0
                    },
                    new()
                    {
                        RowKey = "otliv_1",
                        ProductName = "Отлив",
                        Title = "Отлив 1",
                        Hint = "Первый отлив — размеры и монтаж",
                        DefaultChecked = true,
                        DefaultInstallationMode = 2
                    },
                    new()
                    {
                        RowKey = "otliv_2",
                        ProductName = "Отлив",
                        Title = "Отлив 2",
                        Hint = "Второй отлив — размеры и монтаж",
                        DefaultChecked = true,
                        DefaultInstallationMode = 2
                    },
                    new()
                    {
                        RowKey = "kozyrek_1",
                        ProductName = "Козырёк",
                        Title = "Козырёк 1",
                        Hint = "Первый козырёк — размеры и монтаж",
                        DefaultChecked = true,
                        DefaultInstallationMode = 2
                    },
                    new()
                    {
                        RowKey = "kozyrek_2",
                        ProductName = "Козырёк",
                        Title = "Козырёк 2",
                        Hint = "Второй козырёк — размеры и монтаж",
                        DefaultChecked = true,
                        DefaultInstallationMode = 2
                    },
                    new()
                    {
                        RowKey = "brus",
                        ProductName = "Брус",
                        Title = "Брус",
                        Hint = "Комплектующие (сумма вручную)",
                        DefaultChecked = true
                    },
                    new()
                    {
                        RowKey = "korob",
                        ProductName = "Короб",
                        Title = "Короб",
                        Hint = "Свой товар (сумма вручную)",
                        IsCustomProduct = true,
                        DefaultChecked = true
                    },
                    new()
                    {
                        RowKey = "delivery",
                        ProductName = "Доставка",
                        Title = "Доставка",
                        Hint = "Доставка до объекта",
                        DefaultChecked = true
                    }
                }
            },
            new OrderTemplate
            {
                Id = "french",
                Name = "Француз",
                Subtitle = "Сетка Anwis, 2 козырька, пояс + короб и доставка",
                IsAvailable = true,
                Rows = new List<TemplateRow>
                {
                    new()
                    {
                        RowKey = "grid",
                        ProductName = OrderTemplateGridProduct,
                        Title = "Сетка Anwis",
                        Hint = "Москитная сетка Anwis на французский балкон",
                        DefaultProductType = "Anwis",
                        DefaultChecked = true,
                        DefaultInstallationMode = 0
                    },
                    new()
                    {
                        RowKey = "kozyrek_1",
                        ProductName = "Козырёк",
                        Title = "Козырёк 1",
                        Hint = "Первый козырёк — размеры и монтаж",
                        DefaultChecked = true,
                        DefaultInstallationMode = 2
                    },
                    new()
                    {
                        RowKey = "kozyrek_2",
                        ProductName = "Козырёк",
                        Title = "Козырёк 2",
                        Hint = "Второй козырёк — размеры и монтаж",
                        DefaultChecked = true,
                        DefaultInstallationMode = 2
                    },
                    new()
                    {
                        RowKey = "belt_box",
                        ProductName = "Пояс + Короб",
                        Title = "Пояс + Короб",
                        Hint = "Свой товар (сумма вручную)",
                        IsCustomProduct = true,
                        DefaultChecked = true
                    },
                    new()
                    {
                        RowKey = "delivery",
                        ProductName = "Доставка",
                        Title = "Доставка",
                        Hint = "Доставка до объекта",
                        DefaultChecked = true
                    }
                }
            }
        };

        public const string OrderTemplateGridProduct = "Сетка";

        public static readonly string[] GridProductChoices =
        {
            "Anwis",
            "На навесах",
            "Оконная на метал. крепл.",
            "Дверная сетка"
        };

        public const string DefaultColor = "Белый";

        // ─────────────────────────────────────────────────────────
        // Состояние отдельных строк чек-листа
        // ─────────────────────────────────────────────────────────

        public sealed class TemplateRowState
        {
            public string RowKey { get; init; } = "";
            public string BaseProductName { get; init; } = "";
            public string Title { get; init; } = "";
            public bool IsEnabled { get; set; } = true;
            public int ProductIndex { get; set; } = -1;
            public string SelectedProductType { get; set; } = "";
            public string Color { get; set; } = DefaultColor;
            public AnwisSizeMode AnwisMode { get; set; } = AnwisSizeService.DefaultMode;
            public bool Anticat { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public double Quantity { get; set; } = 0;
            public double ManualAmount { get; set; }
            public int InstallationMode { get; set; } = 2; // дефолт: 2 («В конструкцию»)
            public bool IsCustomProduct { get; set; }

            public string ResolveEffectiveProduct()
            {
                if (BaseProductName == OrderTemplateGridProduct)
                {
                    if (!string.IsNullOrEmpty(SelectedProductType)) return SelectedProductType;
                    if (ProductIndex >= 0 && ProductIndex < GridProductChoices.Length)
                        return GridProductChoices[ProductIndex];
                    return GridProductChoices[0];
                }
                return BaseProductName;
            }

            public double CalculateTotal(Func<string, string, double> catalogPrice)
            {
                if (!IsEnabled) return 0;
                string product = ResolveEffectiveProduct();

                if (product == "Доставка")
                {
                    if (ManualAmount <= 0) return 0;
                    double q = Quantity > 0 ? Quantity : 1;
                    return Math.Round(ManualAmount * q, 2);
                }

                if (Quantity <= 0) return 0;

                if (IsCustomProduct || ProductCatalog.IsManualPiece(product))
                {
                    return Math.Round(ManualAmount * Quantity, 2);
                }

                if (product == "ПСУЛ")
                {
                    double price = catalogPrice("ПСУЛ", string.Empty);
                    if (price <= 0) price = 100;
                    if (Width == 0 && Height == 0)
                        return Math.Round(Quantity * price, 2);
                    return Math.Round(price * Math.Round((Width + Height) * 2 / 1000.0, 3) * Quantity, 2);
                }

                if (Width <= 0 || Height <= 0) return 0;

                double basePrice = catalogPrice(product, Color ?? string.Empty);
                if (Anticat && ProductCatalog.IsAnticatApplicable(product))
                    basePrice += OrderItem.AnticatSurcharge;

                double area = Math.Round(Width * Height / 1000000.0, 3);
                double total = Math.Round(area * basePrice * Quantity, 2);

                if (ProductCatalog.IsImpostApplicable(product) && (Width >= 500 || Height >= 1500))
                {
                    total = Math.Round(total + Math.Round(200.0 * (Width / 1000.0) * Quantity, 2), 2);
                }

                if (ProductCatalog.IsInstallationApplicable(product))
                {
                    double factor = ProductCatalog.IsPerLinearMeter(product)
                        ? Math.Round(Math.Max(Width, Height) / 1000.0, 3) * Quantity
                        : Quantity;

                    if (InstallationMode == 0) // Монтаж включён
                    {
                        double adj = OrderItem.GetDefaultInstallationAdjustment(product);
                        total = Math.Round(Math.Max(0, total + adj * factor), 2);
                    }
                    else if (InstallationMode == 1) // Без монтажа
                    {
                        double ded = OrderItem.GetDefaultInstallationDeduction(product);
                        total = Math.Round(Math.Max(0, total + ded * factor), 2);
                    }
                    else if (InstallationMode == 2) // В конструкцию (наценка 0 руб.)
                    {
                        // наценка 0 руб.
                    }
                }

                return Math.Round(total, 2);
            }
        }

        public Dictionary<string, TemplateRowState> RowStates { get; } = new();

        public TemplateRowState GetOrCreateRowState(TemplateRow row)
        {
            if (!RowStates.TryGetValue(row.RowKey, out var state))
            {
                int pIndex = 0;
                if (!string.IsNullOrEmpty(row.DefaultProductType))
                {
                    pIndex = Array.IndexOf(GridProductChoices, row.DefaultProductType);
                    if (pIndex < 0) pIndex = 0;
                }

                state = new TemplateRowState
                {
                    RowKey = row.RowKey,
                    BaseProductName = row.ProductName,
                    Title = !string.IsNullOrEmpty(row.Title) ? row.Title : row.ProductName,
                    IsEnabled = row.DefaultChecked,
                    ProductIndex = row.ProductName == OrderTemplateGridProduct ? pIndex : -1,
                    SelectedProductType = row.ProductName == OrderTemplateGridProduct ? GridProductChoices[pIndex] : row.ProductName,
                    InstallationMode = row.DefaultInstallationMode,
                    IsCustomProduct = row.IsCustomProduct,
                    Quantity = row.ProductName == "Доставка" ? 1 : 0
                };
                RowStates[row.RowKey] = state;
            }
            return state;
        }

        public void InitForTemplate(OrderTemplate template)
        {
            RowStates.Clear();
            foreach (var r in template.Rows)
            {
                GetOrCreateRowState(r);
            }
            // Синхронизация обратной совместимости
            SyncLegacyProperties();
        }

        // ─────────────────────────────────────────────────────────
        // Свойства обратной совместимости для старых тестов и обращений
        // ─────────────────────────────────────────────────────────

        public bool GridEnabled
        {
            get => RowStates.TryGetValue("grid", out var s) ? s.IsEnabled : true;
            set { EnsureRow("grid", OrderTemplateGridProduct).IsEnabled = value; }
        }

        public int GridProductIndex
        {
            get => RowStates.TryGetValue("grid", out var s) ? s.ProductIndex : -1;
            set
            {
                var s = EnsureRow("grid", OrderTemplateGridProduct);
                s.ProductIndex = value;
                if (value >= 0 && value < GridProductChoices.Length)
                    s.SelectedProductType = GridProductChoices[value];
            }
        }

        public AnwisSizeMode GridAnwisMode
        {
            get => RowStates.TryGetValue("grid", out var s) ? s.AnwisMode : AnwisSizeService.DefaultMode;
            set { EnsureRow("grid", OrderTemplateGridProduct).AnwisMode = value; }
        }

        public bool GridAnticat
        {
            get => RowStates.TryGetValue("grid", out var s) ? s.Anticat : false;
            set { EnsureRow("grid", OrderTemplateGridProduct).Anticat = value; }
        }

        public string GridColor
        {
            get => RowStates.TryGetValue("grid", out var s) ? s.Color : DefaultColor;
            set { EnsureRow("grid", OrderTemplateGridProduct).Color = value; }
        }

        public int GridWidth
        {
            get => RowStates.TryGetValue("grid", out var s) ? s.Width : 0;
            set { EnsureRow("grid", OrderTemplateGridProduct).Width = value; }
        }

        public int GridHeight
        {
            get => RowStates.TryGetValue("grid", out var s) ? s.Height : 0;
            set { EnsureRow("grid", OrderTemplateGridProduct).Height = value; }
        }

        public int GridQuantity
        {
            get => RowStates.TryGetValue("grid", out var s) ? (int)s.Quantity : 0;
            set { EnsureRow("grid", OrderTemplateGridProduct).Quantity = value; }
        }

        public int GridInstallationMode
        {
            get => RowStates.TryGetValue("grid", out var s) ? s.InstallationMode : 0;
            set { EnsureRow("grid", OrderTemplateGridProduct).InstallationMode = value; }
        }

        public bool PsulEnabled
        {
            get => RowStates.TryGetValue("psul", out var s) ? s.IsEnabled : true;
            set { EnsureRow("psul", "ПСУЛ").IsEnabled = value; }
        }

        public int PsulWidth
        {
            get => RowStates.TryGetValue("psul", out var s) ? s.Width : 0;
            set { EnsureRow("psul", "ПСУЛ").Width = value; }
        }

        public int PsulHeight
        {
            get => RowStates.TryGetValue("psul", out var s) ? s.Height : 0;
            set { EnsureRow("psul", "ПСУЛ").Height = value; }
        }

        public int PsulQuantity
        {
            get => RowStates.TryGetValue("psul", out var s) ? (int)s.Quantity : 0;
            set { EnsureRow("psul", "ПСУЛ").Quantity = value; }
        }

        public bool DeliveryEnabled
        {
            get => RowStates.TryGetValue("delivery", out var s) ? s.IsEnabled : true;
            set { EnsureRow("delivery", "Доставка").IsEnabled = value; }
        }

        public double DeliveryAmount
        {
            get => RowStates.TryGetValue("delivery", out var s) ? s.ManualAmount : 0;
            set { EnsureRow("delivery", "Доставка").ManualAmount = value; }
        }

        public int DeliveryQuantity
        {
            get => RowStates.TryGetValue("delivery", out var s) ? (int)s.Quantity : 0;
            set { EnsureRow("delivery", "Доставка").Quantity = value; }
        }

        public bool OtlivEnabled
        {
            get => RowStates.TryGetValue("otliv", out var s) ? s.IsEnabled : false;
            set { EnsureRow("otliv", "Отлив").IsEnabled = value; }
        }

        public string OtlivColor
        {
            get => RowStates.TryGetValue("otliv", out var s) ? s.Color : DefaultColor;
            set { EnsureRow("otliv", "Отлив").Color = value; }
        }

        public int OtlivWidth
        {
            get => RowStates.TryGetValue("otliv", out var s) ? s.Width : 0;
            set { EnsureRow("otliv", "Отлив").Width = value; }
        }

        public int OtlivHeight
        {
            get => RowStates.TryGetValue("otliv", out var s) ? s.Height : 0;
            set { EnsureRow("otliv", "Отлив").Height = value; }
        }

        public int OtlivQuantity
        {
            get => RowStates.TryGetValue("otliv", out var s) ? (int)s.Quantity : 0;
            set { EnsureRow("otliv", "Отлив").Quantity = value; }
        }

        public int OtlivInstallationMode
        {
            get => RowStates.TryGetValue("otliv", out var s) ? s.InstallationMode : 2;
            set { EnsureRow("otliv", "Отлив").InstallationMode = value; }
        }

        private TemplateRowState EnsureRow(string rowKey, string productName)
        {
            if (!RowStates.TryGetValue(rowKey, out var s))
            {
                s = new TemplateRowState
                {
                    RowKey = rowKey,
                    BaseProductName = productName,
                    Title = productName,
                    IsEnabled = rowKey == "otliv" ? false : true,
                    ProductIndex = -1,
                    SelectedProductType = rowKey == "grid" ? GridProductChoices[0] : productName,
                    InstallationMode = rowKey == "grid" ? 0 : 2,
                    Quantity = productName == "Доставка" ? 1 : 0
                };
                RowStates[rowKey] = s;
            }
            return s;
        }

        private void SyncLegacyProperties()
        {
            // Метод гарантирует наличие базовых ключей при старте
            EnsureRow("grid", OrderTemplateGridProduct);
            EnsureRow("otliv", "Отлив");
            EnsureRow("psul", "ПСУЛ");
            EnsureRow("delivery", "Доставка");
        }

        public OrderTemplateService()
        {
            SyncLegacyProperties();
        }

        /// <summary>Какую строку чек-листа подсветить при ошибке (последняя проверка).</summary>
        public string? LastErrorRow { get; private set; }

        // ─────────────────────────────────────────────────────────
        // Валидация
        // ─────────────────────────────────────────────────────────

        public string? GetValidationError() => GetValidationError(null);

        public string? GetValidationError(OrderTemplate? template)
        {
            LastErrorRow = null;

            // Если шаблон передан — проверяем строго по его строкам
            IEnumerable<TemplateRowState> statesToValidate;
            if (template != null)
            {
                statesToValidate = template.Rows
                    .Select(r => RowStates.TryGetValue(r.RowKey, out var s) ? s : null)
                    .Where(s => s != null && s.IsEnabled)!;
            }
            else
            {
                statesToValidate = RowStates.Values.Where(s => s.IsEnabled);
            }

            foreach (var s in statesToValidate)
            {
                string prod = s.ResolveEffectiveProduct();
                bool isGrid = s.BaseProductName == OrderTemplateGridProduct;

                if (isGrid)
                {
                    if (s.ProductIndex < 0 || s.ProductIndex >= GridProductChoices.Length)
                        return Fail("grid-type", "Выберите тип сетки.");
                    if (s.Width <= 0 || s.Height <= 0)
                        return Fail("grid-size", "Укажите размеры сетки.");
                    if (s.Quantity <= 0)
                        return Fail("grid-size", "Укажите количество сеток.");
                }
                else if (prod == "ПСУЛ")
                {
                    if (s.Width < 0 || s.Height < 0)
                        return Fail("psul", "Размеры ПСУЛ не могут быть отрицательными.");
                    if (s.Quantity <= 0)
                        return Fail("psul", "Укажите количество ПСУЛ.");
                }
                else if (prod == "Доставка")
                {
                    if (s.ManualAmount <= 0)
                        return Fail(s.RowKey, "Укажите сумму (Доставка).");
                }
                else if (s.IsCustomProduct || ProductCatalog.IsManualPiece(prod))
                {
                    string label = !string.IsNullOrEmpty(s.Title) ? s.Title : prod;
                    if (s.ManualAmount <= 0)
                        return Fail(s.RowKey, $"Укажите сумму ({label}).");
                    if (s.Quantity <= 0)
                        return Fail(s.RowKey, $"Укажите количество ({label}).");
                }
                else if (ProductCatalog.IsAreaBased(prod))
                {
                    string label = !string.IsNullOrEmpty(s.Title) ? s.Title : prod;
                    string targetKey = s.RowKey == "otliv" ? "otliv-size" : s.RowKey;
                    if (s.Width <= 0 || s.Height <= 0)
                    {
                        string msg = s.RowKey == "otliv" ? "Укажите размеры отлива." : $"Укажите размеры ({label}).";
                        return Fail(targetKey, msg);
                    }
                    if (s.Quantity <= 0)
                    {
                        string msg = s.RowKey == "otliv" ? "Укажите количество отливов." : $"Укажите количество ({label}).";
                        return Fail(targetKey, msg);
                    }
                }
            }

            return null;
        }

        private string? Fail(string rowKey, string message)
        {
            LastErrorRow = rowKey;
            return message;
        }

        // ─────────────────────────────────────────────────────────
        // Резолв цен и сборка позиций
        // ─────────────────────────────────────────────────────────

        public string ResolveProductName(TemplateRow row)
        {
            if (RowStates.TryGetValue(row.RowKey, out var s))
                return s.ResolveEffectiveProduct();
            return row.ProductName == OrderTemplateGridProduct ? GridProductChoices[0] : row.ProductName;
        }

        public static double ResolveRowPrice(string productName, string color, Func<string, string, double> catalogPrice)
        {
            if (OrderItem.ManualPieceProducts.Contains(productName))
                return 0;
            return catalogPrice(productName, color ?? string.Empty);
        }

        public List<TemplateItemSpec> BuildItemSpecs(OrderTemplate template, Func<string, string, double> catalogPrice)
        {
            var specs = new List<TemplateItemSpec>();
            foreach (var row in template.Rows)
            {
                if (!RowStates.TryGetValue(row.RowKey, out var state) || !state.IsEnabled)
                    continue;

                string product = state.ResolveEffectiveProduct();

                if (state.IsCustomProduct)
                {
                    specs.Add(new TemplateItemSpec
                    {
                        RowKey = row.RowKey,
                        Type = product,
                        Color = string.Empty,
                        Width = 0,
                        Height = 0,
                        Quantity = state.Quantity > 0 ? state.Quantity : 1,
                        Price = state.ManualAmount,
                        IsCustomProduct = true
                    });
                    continue;
                }

                if (product == "ПСУЛ")
                {
                    specs.Add(new TemplateItemSpec
                    {
                        RowKey = row.RowKey,
                        Type = "ПСУЛ",
                        Color = string.Empty,
                        Width = state.Width,
                        Height = state.Height,
                        Quantity = state.Quantity > 0 ? state.Quantity : 1,
                        Price = ResolveRowPrice("ПСУЛ", string.Empty, catalogPrice)
                    });
                    continue;
                }

                if (ProductCatalog.IsManualPiece(product))
                {
                    specs.Add(new TemplateItemSpec
                    {
                        RowKey = row.RowKey,
                        Type = product,
                        Color = string.Empty,
                        Width = 0,
                        Height = 0,
                        Quantity = state.Quantity > 0 ? state.Quantity : 1,
                        Price = state.ManualAmount
                    });
                    continue;
                }

                bool isAnwis = AnwisSizeService.IsApplicable(product);
                string color = isAnwis || !OrderItem.NoColorProducts.Contains(product) ? state.Color : string.Empty;

                specs.Add(new TemplateItemSpec
                {
                    RowKey = row.RowKey,
                    Type = product,
                    Color = color,
                    Width = state.Width,
                    Height = state.Height,
                    Quantity = state.Quantity > 0 ? state.Quantity : 1,
                    Price = ResolveRowPrice(product, color, catalogPrice),
                    AnwisMode = isAnwis ? state.AnwisMode : null,
                    Anticat = ProductCatalog.IsAnticatApplicable(product) && state.Anticat,
                    InstallationMode = ProductCatalog.IsInstallationApplicable(product) ? state.InstallationMode : null
                });
            }
            return specs;
        }

        public sealed class TemplateItemSpec
        {
            public string RowKey { get; init; } = "";
            public string Type { get; init; } = "";
            public string Color { get; init; } = "";
            public int Width { get; init; }
            public int Height { get; init; }
            public double Quantity { get; init; }
            public double Price { get; init; }
            public AnwisSizeMode? AnwisMode { get; init; }
            public bool Anticat { get; init; }
            public int? InstallationMode { get; init; }
            public bool IsCustomProduct { get; init; }
        }
    }
}
