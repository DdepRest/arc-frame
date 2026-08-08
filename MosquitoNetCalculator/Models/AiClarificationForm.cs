using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// Interactive parameter card attached to an assistant message when the AI
    /// replies with a clarification instead of executing an add-item command
    /// (e.g. «Сделай сетку» → «Уточните: тип, размеры, цвет…»).
    ///
    /// The card lets the user pick every parameter with ComboBoxes and text
    /// fields — no need to compose a second prompt. «Добавить в расчёт»
    /// builds an <see cref="AiCommand"/> AddItem directly, without a second
    /// round-trip to the LLM.
    ///
    /// Runtime-only UI state: NOT persisted (see <c>[JsonIgnore]</c> on
    /// <see cref="AiChatMessage.ClarificationForm"/>).
    /// </summary>
    public sealed class AiClarificationForm : INotifyPropertyChanged
    {
        /// <summary>All catalog product names in the UX order (Сетки → Доборы → …).</summary>
        public static IReadOnlyList<string> AllProductTypes { get; } =
            ProductCatalog.UserGroups.SelectMany(g => g.Products).ToList();
        /// <summary>Anwis mode labels in enum order (ББ 60 … Габарит).</summary>
        public static IReadOnlyList<string> AllAnwisModes { get; } =
            new[] { "ББ 60", "ББ 70", "ПП", "Проём", "Габарит" };

        /// <summary>Installation options. «Не указывать» keeps the program default.</summary>
        public static IReadOnlyList<string> AllInstallationOptions { get; } =
            new[] { "Не указывать", "С монтажом", "Без монтажа", "В конструкцию" };

        /// <summary>Color palette per product, mirroring the price catalog.
        /// Public so the plan validator and local commands use the same palette.</summary>
        public static IReadOnlyDictionary<string, string[]> KnownColors => ColorMap;

        private static readonly Dictionary<string, string[]> ColorMap = new()
        {
            ["Anwis"] = new[] { "Белый", "Коричневый" },
            ["На навесах"] = new[] { "Белый", "Коричневый" },
            ["Оконная на метал. крепл."] = new[] { "Белый", "Коричневый" },
            ["Дверная сетка"] = new[] { "Белый" },
            ["Отлив"] = new[] { "Белый", "Коричневый", "Антрацит", "Золотой дуб" },
            ["Козырёк"] = new[] { "Белый", "Коричневый", "Антрацит", "Золотой дуб" },
            ["Короб"] = new[] { "Белый", "Коричневый", "Антрацит", "Золотой дуб" },
            ["Уплотнение"] = new[] { "Серый", "Чёрный" }
        };

        private readonly IReadOnlyList<string> _productTypes;
        private string _selectedType = "Anwis";
        private string _selectedColor = "Белый";
        private string _selectedAnwisMode = "ББ 60";
        private string _selectedInstallation = "Не указывать";
        private string _widthText = string.Empty;
        private string _heightText = string.Empty;
        private string _quantityText = "1";

        /// <summary>
        /// Creates the form. When <paramref name="userRequest"/> mentions a product
        /// family («сетки», «отлив», «козырёк»…), only the relevant products are
        /// offered; otherwise the full catalog is shown.
        /// </summary>
        public AiClarificationForm(string? userRequest = null)
        {
            _productTypes = FilterProductsForRequest(userRequest);
            // Keep the default selection inside the filtered list.
            if (_productTypes.Count > 0 && !_productTypes.Contains(_selectedType))
                SelectedType = _productTypes[0];
        }

        public IReadOnlyList<string> ProductTypes => _productTypes;
        public IReadOnlyList<string> AnwisModeOptions => AllAnwisModes;
        public IReadOnlyList<string> InstallationOptions => AllInstallationOptions;

        /// <summary>Colors available for the currently selected product.</summary>
        public IReadOnlyList<string> Colors =>
            ColorMap.TryGetValue(SelectedType, out var c) ? c : Array.Empty<string>();

        public string SelectedType
        {
            get => _selectedType;
            set
            {
                if (_selectedType == value) return;
                _selectedType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsAnwis));
                OnPropertyChanged(nameof(ShowColor));
                OnPropertyChanged(nameof(ShowInstallation));
                OnPropertyChanged(nameof(Colors));
                // Reset color to the first one available for the new product.
                if (Colors.Count > 0 && !Colors.Contains(SelectedColor))
                    SelectedColor = Colors[0];
            }
        }

        public string SelectedColor
        {
            get => _selectedColor;
            set { _selectedColor = value; OnPropertyChanged(); }
        }

        public string SelectedAnwisMode
        {
            get => _selectedAnwisMode;
            set { _selectedAnwisMode = value; OnPropertyChanged(); }
        }

        public string SelectedInstallation
        {
            get => _selectedInstallation;
            set { _selectedInstallation = value; OnPropertyChanged(); }
        }

        public string WidthText
        {
            get => _widthText;
            set { _widthText = value; OnPropertyChanged(); }
        }

        public string HeightText
        {
            get => _heightText;
            set { _heightText = value; OnPropertyChanged(); }
        }

        public string QuantityText
        {
            get => _quantityText;
            set { _quantityText = value; OnPropertyChanged(); }
        }

        /// <summary>True when the selected product uses Anwis size modes (only «Anwis»).</summary>
        public bool IsAnwis => AnwisSizeService.IsApplicable(SelectedType);

        /// <summary>True when the product has a color choice.</summary>
        public bool ShowColor => !ProductCatalog.IsNoColor(SelectedType) && Colors.Count > 0;

        /// <summary>True when the product supports the installation toggle.</summary>
        public bool ShowInstallation => ProductCatalog.IsInstallationApplicable(SelectedType);

        /// <summary>
        /// Filters the catalog to the product family the user asked for:
        /// «Сделай сетку» → only mesh products, «отлив» → Отлив, and so on.
        /// Returns the full catalog when no family keyword is detected.
        /// </summary>
        public static IReadOnlyList<string> FilterProductsForRequest(string? userRequest)
        {
            if (string.IsNullOrWhiteSpace(userRequest)) return AllProductTypes;
            var t = userRequest.ToLowerInvariant();

            // Keyword → product family. All matches are collected, so a request
            // like «короб для отлива» offers both Короб and Отлив.
            var matched = new List<string>();
            if (t.Contains("анвис", StringComparison.Ordinal) || t.Contains("anwis", StringComparison.Ordinal)
                || ContainsAny(t, "сетк", "москит", "дверная", "навес", "оконная"))
                matched.AddRange(MeshProducts);
            if (ContainsAny(t, "отлив", "отливы"))
                matched.Add("Отлив");
            if (ContainsAny(t, "козыр"))
                matched.Add("Козырёк");
            if (t.Contains("короб", StringComparison.Ordinal))
                matched.Add("Короб");
            if (ContainsAny(t, "уплотнен", "уплотнитель"))
                matched.Add("Уплотнение");
            if (t.Contains("псул", StringComparison.Ordinal))
                matched.Add("ПСУЛ");
            if (t.Contains("откос", StringComparison.Ordinal))
            {
                matched.Add("Откос");
                matched.Add("Работа за откос");
            }
            if (t.Contains("работа", StringComparison.Ordinal))
                matched.Add("Работа");
            if (t.Contains("доставк", StringComparison.Ordinal))
                matched.Add("Доставка");
            if (ContainsAny(t, "брус", "пояс"))
            {
                matched.Add("Брус");
                matched.Add("Пояс");
            }
            if (t.Contains("материал", StringComparison.Ordinal))
                matched.Add("Материал");

            if (matched.Count == 0) return AllProductTypes;

            // Dedupe preserving the catalog order (Сетки → Доборы → …).
            return AllProductTypes.Where(matched.Contains).ToList();
        }

        private static readonly IReadOnlyList<string> MeshProducts = new[]
        {
            "Anwis", "На навесах", "Оконная на метал. крепл.", "Дверная сетка"
        };

        private static bool ContainsAny(string text, params string[] keywords)
            => keywords.Any(k => text.Contains(k, StringComparison.Ordinal));

        /// <summary>
        /// True when the reply text looks like a request for product parameters
        /// (the AI couldn't add the item because required data is missing).
        /// Used to attach the form card to the assistant message.
        /// </summary>
        public static bool LooksLikeClarification(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.ToLowerInvariant();
            return t.Contains("уточнит", StringComparison.Ordinal)
                || t.Contains("укажите", StringComparison.Ordinal)
                || t.Contains("укажи", StringComparison.Ordinal)
                || t.Contains("выберите", StringComparison.Ordinal)
                || t.Contains("напишите параметры", StringComparison.Ordinal)
                || t.Contains("какие параметры", StringComparison.Ordinal)
                || (t.Contains("⚠", StringComparison.Ordinal)
                    && (t.Contains("режим", StringComparison.Ordinal) || t.Contains("размер", StringComparison.Ordinal) || t.Contains("глубин", StringComparison.Ordinal)));
        }

        /// <summary>
        /// Builds an AddItem command from the current form values.
        /// </summary>
        /// <param name="command">The resulting command, or null when invalid.</param>
        /// <param name="error">User-facing validation message, or null.</param>
        public bool TryBuildCommand(out AiCommand? command, out string? error)
        {
            command = null;
            error = null;

            if (string.IsNullOrWhiteSpace(SelectedType))
            {
                error = "⚠ Выберите тип товара.";
                return false;
            }

            if (!TryParsePositiveInt(WidthText, out int width) || !TryParsePositiveInt(HeightText, out int height))
            {
                error = "⚠ Укажите ширину и высоту в мм (например 700×1400).";
                return false;
            }

            if (!TryParseQuantity(QuantityText, out double qty))
            {
                error = "⚠ Количество должно быть положительным числом.";
                return false;
            }

            var color = ShowColor ? SelectedColor : "";
            var mode = IsAnwis ? ParseAnwisMode(SelectedAnwisMode) : AnwisSizeMode.Брусбокс60;
            var installation = ShowInstallation ? ParseInstallation(SelectedInstallation) : -1;
            var price = AiCommandParser.GetDefaultPrice(SelectedType, color);

            command = new AiCommand
            {
                Type = AiCommandType.AddItem,
                Params = new AiCommandParams
                {
                    Type = SelectedType,
                    Color = color,
                    Width = width,
                    Height = height,
                    Quantity = qty,
                    Price = price,
                    AnwisMode = mode,
                    InstallationMode = installation
                }
            };
            return true;
        }

        /// <summary>Short user-visible summary of the chosen parameters.</summary>
        public string BuildSummaryText()
        {
            var parts = new List<string> { SelectedType };
            if (ShowColor && !string.IsNullOrWhiteSpace(SelectedColor))
                parts.Add(SelectedColor);
            parts.Add($"{WidthText}×{HeightText} мм");
            if (TryParseQuantity(QuantityText, out var qty) && qty != 1)
                parts.Add(FormatQuantity(qty) + " шт.");
            if (IsAnwis)
                parts.Add(SelectedAnwisMode);
            parts.Add(SelectedInstallation switch
            {
                "С монтажом" => "с монтажом",
                "Без монтажа" => "без монтажа",
                "В конструкцию" => "в конструкцию",
                _ => ""
            });
            return "Добавить: " + string.Join(", ", parts.Where(p => p.Length > 0));
        }

        private static bool TryParsePositiveInt(string s, out int value)
            => int.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0
               || int.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out value) && value > 0;

        private static bool TryParseQuantity(string s, out double value)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                value = 1;
                return true;
            }
            return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0
                || double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value) && value > 0;
        }

        private static string FormatQuantity(double q)
            => q == Math.Floor(q) ? ((int)q).ToString(CultureInfo.InvariantCulture) : q.ToString("0.##", CultureInfo.InvariantCulture);

        private static AnwisSizeMode ParseAnwisMode(string label) => label switch
        {
            "ББ 70" => AnwisSizeMode.Брусбокс70,
            "ПП" => AnwisSizeMode.Профипласт,
            "Проём" => AnwisSizeMode.РазмерПроёма,
            "Габарит" => AnwisSizeMode.Габаритный,
            _ => AnwisSizeMode.Брусбокс60
        };

        private static int ParseInstallation(string label) => label switch
        {
            "С монтажом" => 0,
            "Без монтажа" => 1,
            "В конструкцию" => 2,
            _ => -1
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
