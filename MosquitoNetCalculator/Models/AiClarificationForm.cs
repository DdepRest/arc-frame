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

        /// <summary>Placeholder option selected until the user picks a real Anwis profile.</summary>
        public const string UnspecifiedAnwisMode = "Выберите профиль";

        /// <summary>Installation options. «Не указывать» keeps the program default.</summary>
        public static IReadOnlyList<string> AllInstallationOptions { get; } =
            new[] { "Не указывать", "С монтажом", "Без монтажа", "В конструкцию" };

        /// <summary>Color palette per product, sourced from
        /// <see cref="AiFactsProvider.ColorsByProduct"/>; kept here as a
        /// thin facade so callers like <c>AiPlanValidator</c> do not need
        /// to know which subsystem owns the source.</summary>
        public static IReadOnlyDictionary<string, string[]> KnownColors
            => AiFactsProvider.ColorsByProduct;

        private readonly IReadOnlyList<string> _productTypes;
        private string _selectedType = "Anwis";
        private string _selectedColor = "Белый";
        private string _selectedAnwisMode = UnspecifiedAnwisMode;
        private string _selectedInstallation = "Не указывать";
        private string _widthText = string.Empty;
        private string _heightText = string.Empty;
        private string _quantityText = "1";

        // Dimension / quantity / leading-number regexes moved to
        // AiKeywordLexicon (the single source of truth for AI keyword
        // detection). The form's PreFill*/preferences work through it.

        /// <summary>
        /// Creates the form. When <paramref name="userRequest"/> mentions a product
        /// family («сетки», «отлив», «козырёк»…), only the relevant products are
        /// offered; otherwise the full catalog is shown. <paramref name="replyText"/>
        /// is the model's prose answer — used as a last-resort pre-fill source when
        /// a vision model read parameters off a photo but the local OCR/user text
        /// didn't capture them.
        /// </summary>
        public AiClarificationForm(string? userRequest = null, AiCommandParams? knownParams = null, string? replyText = null)
        {
            _productTypes = FilterProductsForRequest(userRequest);
            // Keep the default selection inside the filtered list.
            if (_productTypes.Count > 0 && !_productTypes.Contains(_selectedType))
                SelectedType = _productTypes[0];
            // The model's already-parsed parameters are the most complete source:
            // apply them first so the card is never blank when the raw user text
            // doesn't spell out every field.
            PreFillFromCommand(knownParams);
            // Then the model's prose (it may have seen the photo itself), and
            // finally the user's own words — the user always wins.
            PreFillFromReply(replyText);
            PreFillFromRequest(userRequest);
        }

        public IReadOnlyList<string> ProductTypes => _productTypes;
        public IReadOnlyList<string> AnwisModeOptions { get; } =
            new[] { UnspecifiedAnwisMode, "ББ 60", "ББ 70", "ПП", "Проём", "Габарит" };
        public IReadOnlyList<string> InstallationOptions => AllInstallationOptions;

        /// <summary>Colors available for the currently selected product.</summary>
        public IReadOnlyList<string> Colors => AiFactsProvider.GetColorsFor(SelectedType);

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
            => AiKeywordLexicon.ContainsAny(text, keywords);

        /// <summary>
        /// Fills the fields the user already gave in <paramref name="request"/>,
        /// so the card needs only the still-missing parameter (e.g. the Anwis
        /// mode) instead of making the user retype everything: «ПМС Anwis. бел
        /// 4 739х1116» → Anwis + Белый + 739×1116 + 4 шт.
        /// </summary>
        private void PreFillFromRequest(string? request)
        {
            if (string.IsNullOrWhiteSpace(request)) return;
            var t = request.ToLowerInvariant();

            var color = AiKeywordLexicon.DetectColor(t);
            if (color != null && Colors.Contains(color))
                SelectedColor = color;

            var mode = AiKeywordLexicon.DetectAnwisMode(t);
            if (mode != null && IsAnwis)
                SelectedAnwisMode = mode;

            // Pre-fill the installation choice the user already named
            // («с монтажом»/«без монтажа»/«в конструкцию») so the card only
            // asks for what's genuinely still unknown.
            var installation = AiKeywordLexicon.DetectInstallationMode(request);
            if (installation >= 0)
                SelectedInstallation = AiKeywordLexicon.InstallationLabel(installation);

            var dim = AiKeywordLexicon.DimensionRegex.Match(request);
            if (!dim.Success) return;

            WidthText = dim.Groups[1].Value;
            HeightText = dim.Groups[2].Value;

            // «4 шт 739х1116» wins over a bare leading number; otherwise a
            // number right before the size («4 739х1116») is treated as count.
            var q = AiKeywordLexicon.QuantityRegex.Match(request);
            if (q.Success)
            {
                QuantityText = q.Groups[1].Value;
            }
            else
            {
                var beforeSize = request.Substring(0, dim.Index);
                var leading = AiKeywordLexicon.LeadingNumberRegex.Match(beforeSize);
                if (leading.Success)
                    QuantityText = leading.Groups[1].Value;
            }
        }

        /// <summary>
        /// Pre-fills the card from the model's prose reply. A vision model often
        /// reads parameters straight off an attached photo but answers with a
        /// plain-text question («Вижу 700×1400, белый. Какой режим?») instead of
        /// a structured add_item. Without this, a photo that the local OCR couldn't
        /// read leaves the card blank even though the reply spells out the values.
        /// Deliberately does NOT read the Anwis mode here — the reply's
        /// «ББ60, ББ70, ПП…» is an options list, not a user choice, so the
        /// profile stays «Выберите профиль».
        /// </summary>
        private void PreFillFromReply(string? reply)
        {
            if (string.IsNullOrWhiteSpace(reply)) return;
            var t = reply.ToLowerInvariant();

            // Narrow the selected product only when the reply names exactly one
            // family (e.g. «Отлив»). Mesh requests resolve to several products,
            // so the default selection stays untouched.
            var family = FilterProductsForRequest(reply);
            if (family.Count == 1 && ProductTypes.Contains(family[0]))
                SelectedType = family[0];

            var color = AiKeywordLexicon.DetectColor(t);
            if (color != null && Colors.Contains(color))
                SelectedColor = color;

            var dim = AiKeywordLexicon.DimensionRegex.Match(reply);
            if (dim.Success)
            {
                WidthText = dim.Groups[1].Value;
                HeightText = dim.Groups[2].Value;
            }

            var q = AiKeywordLexicon.QuantityRegex.Match(reply);
            if (q.Success)
                QuantityText = q.Groups[1].Value;
        }

        /// <summary>
        /// Pre-fills the card from an already-parsed AddItem command. The model
        /// can recover size/color/quantity from earlier context or an attachment
        /// even when the raw user text doesn't contain them — without this the
        /// clarification card would come up blank despite the program already
        /// knowing the values. The Anwis size mode is deliberately NOT copied:
        /// the card exists precisely because that mode was guessed and must be
        /// re-picked by the user.
        /// </summary>
        private void PreFillFromCommand(AiCommandParams? p)
        {
            if (p == null) return;

            if (!string.IsNullOrWhiteSpace(p.Type) && ProductTypes.Contains(p.Type))
                SelectedType = p.Type;

            if (!string.IsNullOrWhiteSpace(p.Color) && Colors.Contains(p.Color))
                SelectedColor = p.Color;

            if (p.Width > 0)
                WidthText = p.Width.ToString(CultureInfo.InvariantCulture);
            if (p.Height > 0)
                HeightText = p.Height.ToString(CultureInfo.InvariantCulture);

            if (p.Quantity > 0)
                QuantityText = FormatQuantity(p.Quantity);
        }

        private static string InstallationLabel(int mode) => mode switch
        {
            0 => "С монтажом",
            1 => "Без монтажа",
            2 => "В конструкцию",
            _ => "Не указывать"
        };

        // DetectColor / DetectAnwisMode removed — see AiKeywordLexicon.

        /// <summary>
        /// True when the reply text looks like a request for product parameters
        /// (the AI couldn't add the item because required data is missing).
        /// Used to attach the form card to the assistant message.
        /// </summary>
        public static bool LooksLikeClarification(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.ToLowerInvariant();

            if (t.Contains("уточнит", StringComparison.Ordinal)
                || t.Contains("укажите", StringComparison.Ordinal)
                || t.Contains("укажи", StringComparison.Ordinal)
                || t.Contains("выберите", StringComparison.Ordinal)
                || t.Contains("напишите параметры", StringComparison.Ordinal)
                || t.Contains("какие параметры", StringComparison.Ordinal))
                return true;

            // The model often phrases the missing parameter as a question rather
            // than an imperative: «Какой режим использовать?», «Какие размеры?».
            // Catch those too, so the interactive card appears instead of a dead-end
            // text answer.
            if (t.Contains("какой режим", StringComparison.Ordinal)
                || t.Contains("какой профиль", StringComparison.Ordinal)
                || t.Contains("какие размеры", StringComparison.Ordinal)
                || t.Contains("какой размер", StringComparison.Ordinal)
                || t.Contains("какой цвет", StringComparison.Ordinal)
                || t.Contains("какая глубина", StringComparison.Ordinal)
                || t.Contains("не хватает режим", StringComparison.Ordinal)
                || t.Contains("не хватает размер", StringComparison.Ordinal)
                || t.Contains("не хватает цвет", StringComparison.Ordinal)
                || t.Contains("не хватает глубин", StringComparison.Ordinal))
                return true;

            return t.Contains("⚠", StringComparison.Ordinal)
                && (t.Contains("режим", StringComparison.Ordinal)
                    || t.Contains("размер", StringComparison.Ordinal)
                    || t.Contains("глубин", StringComparison.Ordinal));
        }

        /// <summary>
        /// True when the form card should be attached to the assistant's reply:
        /// either the reply itself asks for missing parameters, or the user's
        /// request was an Anwis add-item that already has dimensions but lacks
        /// the size mode — the only remaining choice. This is the local safety
        /// net for models that answer «Какой режим использовать?» as plain text
        /// instead of a structured clarification.
        /// </summary>
        public static bool ShouldShowForm(string? userRequest, string? reply)
        {
            if (LooksLikeClarification(reply)) return true;
            return IsIncompleteAnwisAddRequest(userRequest);
        }

        /// <summary>Add-intent verb stems («добавь», «сделай», «вставь»…).</summary>
        private static readonly string[] AddVerbStems =
            { "добав", "сдела", "встав", "внес", "запиш", "прибав", "занес" };

        /// <summary>
        /// «Добавь сетку Anwis белый 739×1116 4 шт» — the request mentions Anwis
        /// and a dimension, but no ББ60/ББ70/ПП/Проём/Габарит. The card is
        /// pre-filled with everything else and the user only picks the profile.
        /// A request without dimensions, or one that already names the mode,
        /// does not force the card.
        /// </summary>
        private static bool IsIncompleteAnwisAddRequest(string? request)
        {
            if (string.IsNullOrWhiteSpace(request)) return false;
            var t = request.ToLowerInvariant();

            // Only add-intent phrases force the card — a price/help question like
            // «Сколько стоит Anwis 739×1116?» must not open a form.
            if (!ContainsAny(t, AddVerbStems)) return false;

            bool mentionsMesh = t.Contains("анвис", StringComparison.Ordinal)
                || t.Contains("anwis", StringComparison.Ordinal)
                || ContainsAny(t, "сетк", "москит", "дверная", "навес", "оконная");
            if (!mentionsMesh) return false;

            if (!AiKeywordLexicon.DimensionRegex.IsMatch(request)) return false;
            return AiKeywordLexicon.DetectAnwisMode(t) == null;
        }

        /// <summary>
        /// True when the request explicitly names an Anwis size mode
        /// (ББ60/ББ70/ПП/Проём/Габарит). Used to refuse silently
        /// guessing a profile when the user never specified a mode.
        /// Delegates to <see cref="AiKeywordLexicon.AnwisModeSpecified"/>.
        /// </summary>
        public static bool AnwisModeSpecified(string? text)
            => AiKeywordLexicon.AnwisModeSpecified(text);

        /// <summary>
        /// Detects an explicit installation choice in free text. Returns
        /// -1 when the user never mentioned installation, 0/1/2 otherwise.
        /// Delegates to <see cref="AiKeywordLexicon.DetectInstallationMode"/>.
        /// </summary>
        public static int DetectInstallationMode(string? text)
            => AiKeywordLexicon.DetectInstallationMode(text);

        /// <summary>
        /// True when the request explicitly names an installation mode
        /// («с монтажом»/«без монтажа»/«в конструкцию»). Delegates to
        /// <see cref="AiKeywordLexicon.InstallationModeSpecified"/>.
        /// </summary>
        public static bool InstallationModeSpecified(string? text)
            => AiKeywordLexicon.InstallationModeSpecified(text);

        /// <summary>
        /// True when <paramref name="commands"/> add an Anwis product but the
        /// user's request does not name the size mode — i.e. the model guessed
        /// it. The mode is a critical parameter and must never
        /// be invented, so the caller should show the clarification card instead
        /// of executing the plan.
        /// </summary>
        public static bool ShouldAskAnwisModeFor(IReadOnlyList<AiCommand> commands, string? userRequest)
        {
            if (AnwisModeSpecified(userRequest)) return false;
            return commands.Any(c => c.Type == AiCommandType.AddItem
                && AnwisSizeService.IsApplicable(c.Params.Type));
        }

        /// <summary>
        /// Generalized safety net (superset of <see cref="ShouldAskAnwisModeFor"/>):
        /// a parsed AddItem must never execute with invented critical data.
        /// Kept on this class because the form has always owned it; the new
        /// <see cref="AiPlanSafetyPolicy"/> is the canonical caller for every
        /// plan-building pipeline (and additionally covers untargeted updates).
        /// </summary>
        public static bool ShouldAskForMissingParams(IReadOnlyList<AiCommand> commands, string? userRequest)
        {
            if (commands == null) return false;
            foreach (var c in commands)
            {
                if (c.Type != AiCommandType.AddItem) continue;
                var p = c.Params;
                if (AnwisSizeService.IsApplicable(p.Type) && !AnwisModeSpecified(userRequest))
                    return true;
                if (!ProductCatalog.IsManualPiece(p.Type) && (p.Width <= 0 || p.Height <= 0))
                    return true;
                // Монтаж is price-affecting and must never be silently defaulted
                // («Без монтажа» for Отлив/Козырёк, «Монтаж включён» for Anwis):
                // ask the user instead of guessing.
                if (ProductCatalog.IsInstallationApplicable(p.Type) && !InstallationModeSpecified(userRequest))
                    return true;
            }
            return false;
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

            // The Anwis size mode is a critical parameter: never silently guess
            // the profile. The user must pick a real profile before the command is
            // built (matches the local no-guess rule for the model's replies).
            if (IsAnwis && SelectedAnwisMode == UnspecifiedAnwisMode)
            {
                error = "⚠ Выберите режим Anwis (профиль): ББ 60, ББ 70, ПП, Проём или Габарит.";
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
            if (IsAnwis && SelectedAnwisMode != UnspecifiedAnwisMode)
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
