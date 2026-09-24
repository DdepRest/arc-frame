using System;
using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Система шаблонов заказа (v3.54, замена кнопки «На завод»).
    /// Каталог — данные: новый шаблон = новая запись <see cref="OrderTemplate"/>,
    /// UI (TemplatesWindow) и логика не меняются.
    /// <para/>
    /// Логика — ЧИСТАЯ, без WPF (образец — AiPlanValidator/QuickAdd decision
    /// matrix): каталог и матрица валидации покрываются unit-тестами
    /// (OrderTemplateServiceTests). UI применяет результаты. Экземпляр класса —
    /// состояние чек-листа одного открытия окна.
    /// </summary>
    public class OrderTemplateService
    {
        // ─────────────────────────────────────────────────────────
        // Модели каталога
        // ─────────────────────────────────────────────────────────

        /// <summary>Шаблон заказа: имя + строки чек-листа.</summary>
        public sealed class OrderTemplate
        {
            public string Id { get; init; } = "";
            public string Name { get; init; } = "";
            public string Subtitle { get; init; } = "";
            /// <summary>Доступен ли шаблон. false = карточка-заглушка «Скоро».</summary>
            public bool IsAvailable { get; init; }
            public List<TemplateRow> Rows { get; init; } = new();
        }

        /// <summary>
        /// Одна строка чек-листа шаблона: позиция, включённая по умолчанию.
        /// <see cref="IsCheckedLocked"/> = строку нельзя выключить (обязательная).
        /// </summary>
        public sealed class TemplateRow
        {
            /// <summary>Имя товара из каталога цен (Name в PriceService).</summary>
            public string ProductName { get; init; } = "";
            /// <summary>Пояснение под названием строки (зачем позиция).</summary>
            public string Hint { get; init; } = "";
            public bool DefaultChecked { get; init; }
            /// <summary>true — чекбокс задизейблен (позиция обязательна).</summary>
            public bool IsCheckedLocked { get; init; }
        }

        // ─────────────────────────────────────────────────────────
        // Каталог
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Каталог шаблонов. «Окно» — доступен, остальные — заглушки
        /// (видны в витрине, открываются тостом «Скоро»).
        /// </summary>
        public static readonly List<OrderTemplate> All = new()
        {
            new OrderTemplate
            {
                Id = "window",
                Name = "Окно",
                Subtitle = "Сетка + ПСУЛ + Доставка + Отлив",
                IsAvailable = true,
                Rows = new List<TemplateRow>
                {
                    // Сетка — обязательная основа: главная строка шаблона.
                    new()
                    {
                        ProductName = OrderTemplateGridProduct,
                        Hint = "Москитная сетка на окно — выберите тип и размеры",
                        DefaultChecked = true,
                        IsCheckedLocked = true
                    },
                    // ПСУЛ — уплотнительная лента по периметру монтажного шва.
                    new()
                    {
                        ProductName = "ПСУЛ",
                        Hint = "Уплотнительная лента (м.п.)",
                        DefaultChecked = true,
                        IsCheckedLocked = false
                    },
                    // Доставка — услуга (сумма вручную).
                    new()
                    {
                        ProductName = "Доставка",
                        Hint = "Доставка до объекта",
                        DefaultChecked = true,
                        IsCheckedLocked = false
                    },
                    // Отлив — opt-in (как в прежнем «На завод»: готовый подоконник
                    // не изготавливается, пользователь включает осознанно).
                    new()
                    {
                        ProductName = "Отлив",
                        Hint = "Подоконник отлив (металл/пластик)",
                        DefaultChecked = false,
                        IsCheckedLocked = false
                    }
                }
            },
            // ── Заглушки: витрина расширяема, наполнение — следующие циклы. ──
            new OrderTemplate
            {
                Id = "balcony",
                Name = "Балконный блок",
                Subtitle = "Скоро",
                IsAvailable = false,
                Rows = new List<TemplateRow>()
            },
            new OrderTemplate
            {
                Id = "frame",
                Name = "Рама",
                Subtitle = "Скоро",
                IsAvailable = false,
                Rows = new List<TemplateRow>()
            },
            new OrderTemplate
            {
                Id = "french",
                Name = "Француз",
                Subtitle = "Скоро",
                IsAvailable = false,
                Rows = new List<TemplateRow>()
            }
        };

        /// <summary>Виртуальное имя строки «Сетка» шаблона: реальный товар выбирается выпадающим списком.</summary>
        public const string OrderTemplateGridProduct = "Сетка";

        /// <summary>Типы сеток, доступные в шаблоне «Окно» (порядок — каталог UX).</summary>
        public static readonly string[] GridProductChoices =
        {
            "Anwis",
            "На навесах",
            "Оконная на метал. крепл.",
            "Дверная сетка"
        };

        /// <summary>Цвет по умолчанию для товара (первый цвет прайса уже даёт это UI; здесь — fallback).</summary>
        public const string DefaultColor = "Белый";

        // ─────────────────────────────────────────────────────────
        // Состояние чек-листа (заполняет UI)
        // ─────────────────────────────────────────────────────────

        /// <summary>Выбранный тип сетки: индекс в <see cref="GridProductChoices"/>, -1 = не выбран.</summary>
        public int GridProductIndex { get; set; } = -1;

        /// <summary>Режим Anwis (применим только для товара «Anwis»).</summary>
        public AnwisSizeMode GridAnwisMode { get; set; } = AnwisSizeService.DefaultMode;

        public bool GridAnticat { get; set; }

        /// <summary>Цвет сетки.</summary>
        public string GridColor { get; set; } = DefaultColor;

        /// <summary>Ширина сетки, мм (ввод).</summary>
        public int GridWidth { get; set; }

        /// <summary>Высота сетки, мм (ввод).</summary>
        public int GridHeight { get; set; }

        /// <summary>Количество сеток.</summary>
        public int GridQuantity { get; set; } = 1;

        public bool PsulEnabled { get; set; } = true;
        public int PsulWidth { get; set; }
        public int PsulHeight { get; set; }
        public int PsulQuantity { get; set; } = 1;

        public bool DeliveryEnabled { get; set; } = true;
        /// <summary>Сумма доставки (ручной ввод; 0 = «уточнить позже», строка всё равно добавится).</summary>
        public double DeliveryAmount { get; set; }
        public int DeliveryQuantity { get; set; } = 1;

        public bool OtlivEnabled { get; set; }
        public string OtlivColor { get; set; } = DefaultColor;
        public int OtlivWidth { get; set; }
        public int OtlivHeight { get; set; }
        public int OtlivQuantity { get; set; } = 1;
        /// <summary>Монтаж отлива: 0 = включён (ставка по умолчанию), 1 = без монтажа.</summary>
        public int OtlivInstallationMode { get; set; } = 1;

        /// <summary>Какую строку чек-листа подсветить при ошибке (последняя проверка).</summary>
        public string? LastErrorRow { get; private set; }

        // ─────────────────────────────────────────────────────────
        // Валидация (матрица обязательных полей)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Возвращает null, если включённые строки заполнены корректно,
        /// иначе текст ошибки для тоста (и заполняет <see cref="LastErrorRow"/>).
        /// Проверяются ТОЛЬКО включённые строки — выключенные («Не требуется»)
        /// не мешают добавлению.
        /// </summary>
        public string? GetValidationError()
        {
            LastErrorRow = null;

            // Сетка: тип + размеры обязательны всегда (строка locked-on).
            if (GridProductIndex < 0 || GridProductIndex >= GridProductChoices.Length)
                return Fail("grid-type", "Выберите тип сетки.");
            if (GridWidth <= 0 || GridHeight <= 0)
                return Fail("grid-size", "Укажите размеры сетки.");
            if (GridQuantity <= 0)
                return Fail("grid-size", "Количество сеток должно быть больше нуля.");

            if (PsulEnabled)
            {
                // ПСУЛ допускает ввод без размеров (расчёт по количеству, см.
                // OrderItem.Recalculate: W=H=0 → Quantity × Price). Отрицательные — нет.
                if (PsulWidth < 0 || PsulHeight < 0)
                    return Fail("psul", "Размеры ПСУЛ не могут быть отрицательными.");
                if (PsulQuantity <= 0)
                    return Fail("psul", "Количество ПСУЛ должно быть больше нуля.");
            }

            if (DeliveryEnabled && DeliveryQuantity <= 0)
                return Fail("delivery", "Количество доставки должно быть больше нуля.");

            if (OtlivEnabled)
            {
                if (OtlivWidth <= 0 || OtlivHeight <= 0)
                    return Fail("otliv-size", "Укажите размеры отлива.");
                if (OtlivQuantity <= 0)
                    return Fail("otliv-size", "Количество отливов должно быть больше нуля.");
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

        /// <summary>
        /// Реальное имя товара для строки шаблона: «Сетка» → выбранный тип,
        /// остальные — как в каталоге.
        /// </summary>
        public string ResolveProductName(TemplateRow row) =>
            row.ProductName == OrderTemplateGridProduct
                ? GridProductChoices[Math.Clamp(GridProductIndex, 0, GridProductChoices.Length - 1)]
                : row.ProductName;

        /// <summary>
        /// Резолвит цену строки по каталогу. Для ManualPiece-товаров (Доставка)
        /// каталог даёт 0 — сумма вводится вручную, фолбэка нет.
        /// Чистая функция — семантика <c>QuickAddControl.ResolveQuickAddPrice</c>.
        /// </summary>
        public static double ResolveRowPrice(string productName, string color, Func<string, string, double> catalogPrice)
        {
            if (OrderItem.ManualPieceProducts.Contains(productName))
                return 0; // ручной товар — цену задаёт пользователь
            return catalogPrice(productName, color ?? string.Empty);
        }

        /// <summary>
        /// Строит список позиций (тип/цвет/размеры/кол-во/цена) для включённых
        /// строк. UI добавляет их через <c>CalculationViewModel.AddItem</c> —
        /// тот же пайплайн, что QuickAdd: Anwis-коррекция режима, импост,
        /// монтаж по умолчанию, Undo.
        /// </summary>
        public List<TemplateItemSpec> BuildItemSpecs(OrderTemplate template, Func<string, string, double> catalogPrice)
        {
            var specs = new List<TemplateItemSpec>();
            foreach (var row in template.Rows)
            {
                string product = ResolveProductName(row);
                switch (product)
                {
                    case "ПСУЛ" when row.ProductName == "ПСУЛ":
                        if (PsulEnabled)
                        {
                            specs.Add(new TemplateItemSpec
                            {
                                RowKey = "psul",
                                Type = "ПСУЛ",
                                Color = string.Empty,
                                Width = PsulWidth,
                                Height = PsulHeight,
                                Quantity = PsulQuantity,
                                Price = ResolveRowPrice("ПСУЛ", string.Empty, catalogPrice)
                            });
                        }
                        break;

                    case "Доставка" when row.ProductName == "Доставка":
                        if (DeliveryEnabled)
                        {
                            specs.Add(new TemplateItemSpec
                            {
                                RowKey = "delivery",
                                Type = "Доставка",
                                Color = string.Empty,
                                Width = 0,
                                Height = 0,
                                Quantity = DeliveryQuantity,
                                Price = DeliveryAmount
                            });
                        }
                        break;

                    case "Отлив" when row.ProductName == "Отлив":
                        if (OtlivEnabled)
                        {
                            specs.Add(new TemplateItemSpec
                            {
                                RowKey = "otliv",
                                Type = "Отлив",
                                Color = OtlivColor,
                                Width = OtlivWidth,
                                Height = OtlivHeight,
                                Quantity = OtlivQuantity,
                                Price = ResolveRowPrice("Отлив", OtlivColor, catalogPrice),
                                InstallationMode = OtlivInstallationMode
                            });
                        }
                        break;

                    default:
                        // Строка «Сетка» → реальный товар (Anwis / На навесах / …).
                        // Сетка обязательна и всегда включена.
                        bool isAnwis = AnwisSizeService.IsApplicable(product);
                        string color = isAnwis || !OrderItem.NoColorProducts.Contains(product) ? GridColor : string.Empty;
                        specs.Add(new TemplateItemSpec
                        {
                            RowKey = "grid",
                            Type = product,
                            Color = color,
                            Width = GridWidth,
                            Height = GridHeight,
                            Quantity = GridQuantity,
                            Price = ResolveRowPrice(product, color, catalogPrice),
                            AnwisMode = isAnwis ? GridAnwisMode : null,
                            Anticat = isAnwis && GridAnticat
                        });
                        break;
                }
            }
            return specs;
        }

        /// <summary>Параметры одной будущей позиции заказа (применяет UI).</summary>
        public sealed class TemplateItemSpec
        {
            public string RowKey { get; init; } = "";
            public string Type { get; init; } = "";
            public string Color { get; init; } = "";
            public int Width { get; init; }
            public int Height { get; init; }
            public double Quantity { get; init; }
            public double Price { get; init; }
            /// <summary>Режим Anwis (только для Anwis; null — товар не Anwis).</summary>
            public AnwisSizeMode? AnwisMode { get; init; }
            public bool Anticat { get; init; }
            /// <summary>Монтаж (0/1/2); null — по умолчанию AddItem.</summary>
            public int? InstallationMode { get; init; }
        }
    }
}
