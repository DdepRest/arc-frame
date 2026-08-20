using System;
using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    public sealed class AiStepValidationResult
    {
        public string StepId { get; init; } = "";
        public bool IsValid { get; init; }
        public List<string> Messages { get; } = new();
    }

    public sealed class AiPlanValidationResult
    {
        public bool IsValid { get; init; }
        public bool RequiresConfirmation { get; init; }
        /// <summary>
        /// True when the plan must surface a clarification card before
        /// execution (would invent Anwis mode / dimensions / монтаж /
        /// untargeted update). Computed by <see cref="AiPlanSafetyPolicy"/>.
        /// </summary>
        public bool NeedsClarification { get; init; }
        /// <summary>First failing safety guard (Anwis-mode > dimensions > монтаж > update-target).</summary>
        public AiPlanSafetyPolicy.MissingField MissingField { get; init; } = AiPlanSafetyPolicy.MissingField.None;
        public List<string> Messages { get; } = new();
        public List<AiStepValidationResult> StepResults { get; } = new();
    }

    /// <summary>
    /// Local validation of an <see cref="AiActionPlan"/> against the real
    /// catalog and product metadata. Runs BEFORE anything touches the order —
    /// the LLM reply is never trusted on its own (CONTROL: prompt is not a
    /// replacement for local validation).
    /// </summary>
    public static class AiPlanValidator
    {
        /// <summary>Product names in UX order (Сетки → Доборы → …).</summary>
        public static IReadOnlyList<string> AllProducts { get; } =
            ProductCatalog.UserGroups.SelectMany(g => g.Products).ToList();

        /// <summary>Public copy of the color palette so the validator agrees with the form.</summary>
        public static IReadOnlyDictionary<string, string[]> KnownColors => AiClarificationForm.KnownColors;

        public static AiPlanValidationResult Validate(AiActionPlan plan)
        {
            var messages = new List<string>();
            var steps = new List<AiStepValidationResult>();
            bool valid = plan.Steps.Count > 0;

            if (plan.Steps.Count == 0)
                messages.Add("План пуст: нет действий для выполнения.");

            foreach (var step in plan.Steps)
            {
                var stepMessages = ValidateCommand(step.ToCommand());
                var stepValid = stepMessages.Count == 0;
                if (!stepValid) valid = false;
                var stepResult = new AiStepValidationResult
                {
                    StepId = step.StepId,
                    IsValid = stepValid
                };
                stepResult.Messages.AddRange(stepMessages);
                steps.Add(stepResult);
            }

            plan.RequiresConfirmation = AiPlanBuilder.RequiresConfirmation(
                plan.Steps.Select(s => s.ToCommand()).ToArray());

            // Safety policy: «don't invent» — single source of truth.
            // The plan is structurally valid but may still need a clarification
            // card before it can be confirmed. Propagate the flag both into
            // the result (for the validator API) and onto the plan itself
            // (for the parser / executor pipeline to read).
            var commands = plan.Steps.Select(s => s.ToCommand()).ToArray();
            var missing = AiPlanSafetyPolicy.Classify(commands, plan.SourceUserText);
            plan.NeedsClarification = missing != AiPlanSafetyPolicy.MissingField.None;

            var result = new AiPlanValidationResult
            {
                IsValid = valid,
                RequiresConfirmation = plan.RequiresConfirmation,
                NeedsClarification = plan.NeedsClarification,
                MissingField = missing
            };
            result.Messages.AddRange(messages);
            result.StepResults.AddRange(steps);
            return result;
        }

        /// <summary>Validates a single command. Returns blocking messages (empty = OK).</summary>
        public static List<string> ValidateCommand(AiCommand command)
        {
            var errors = new List<string>();
            var p = command.Params;

            switch (command.Type)
            {
                case AiCommandType.AddItem:
                    ValidateAddItem(p, errors);
                    break;
                case AiCommandType.CalcSlope:
                    if (p.Width <= 0 || p.Height <= 0 || p.Depth <= 0)
                        errors.Add("Для просчёта откосов нужны положительные ширина, высота и глубина.");
                    if (p.Quantity <= 0)
                        errors.Add("Количество окон должно быть положительным.");
                    break;
                case AiCommandType.UpdateItems:
                    if (!HasAnyUpdate(p))
                        errors.Add("Не указано, что именно изменить (монтаж, цена, цвет, режим Anwis).");
                    if (!string.IsNullOrWhiteSpace(p.TargetProduct)
                        && !IsKnownTarget(p.TargetProduct))
                        errors.Add($"Нет товара или категории «{p.TargetProduct}».");
                    break;
                case AiCommandType.DeleteItems:
                    if (!string.IsNullOrWhiteSpace(p.TargetProduct)
                        && !IsKnownTarget(p.TargetProduct))
                        errors.Add($"Нет товара или категории «{p.TargetProduct}».");
                    break;
                case AiCommandType.DeleteLast:
                case AiCommandType.ClearAll:
                case AiCommandType.ListProducts:
                    break;
                default:
                    errors.Add("Неизвестный тип действия.");
                    break;
            }

            return errors;
        }

        private static void ValidateAddItem(AiCommandParams p, List<string> errors)
        {
            if (!AiOrderContext.IsKnownProduct(p.Type))
            {
                errors.Add($"Товар «{p.Type}» отсутствует в каталоге.");
                return; // nothing else can be trusted
            }

            bool manual = ProductCatalog.IsManualPiece(p.Type);
            if (!manual && (p.Width <= 0 || p.Height <= 0))
                errors.Add("Для этого товара нужны положительные ширина и высота (мм).");
            if (p.Quantity <= 0)
                errors.Add("Количество должно быть положительным.");
            if (p.InstallationMode is < -1 or > 2)
                errors.Add("Недопустимый режим монтажа.");

            // Color: known palette is a hint; an empty color means the default.
            if (KnownColors.TryGetValue(p.Type, out var colors)
                && !string.IsNullOrWhiteSpace(p.Color)
                && !colors.Contains(p.Color, StringComparer.OrdinalIgnoreCase)
                && !ProductCatalog.IsNoColor(p.Type))
            {
                errors.Add($"Цвет «{p.Color}» не предусмотрен для «{p.Type}». Доступно: {string.Join(", ", colors)}.");
            }
        }

        private static bool HasAnyUpdate(AiCommandParams p)
            => p.UpdateInstallationMode.HasValue
               || p.UpdatePrice.HasValue
               || p.UpdateInstallationAmount.HasValue
               || p.UpdateAnwisMode.HasValue
               || p.UpdateColor != null;

        /// <summary>Matches a target against product names and the UI categories.</summary>
        public static bool IsKnownTarget(string target)
        {
            var t = target.Trim();
            if (t.Equals("all", StringComparison.OrdinalIgnoreCase)) return true;
            if (Categories.ContainsKey(t.ToLowerInvariant())) return true;
            return AiOrderContext.IsKnownProduct(t);
        }

        /// <summary>Category keywords mirroring AiCommandExecutor.MatchesTarget.</summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> Categories { get; } =
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["сетки"] = new[] { "Anwis", "На навесах", "Оконная на метал. крепл.", "Дверная сетка" },
                ["фасадные"] = new[] { "Отлив", "Козырёк", "Короб" },
                ["комплектующие"] = new[] { "ПСУЛ", "Уплотнение", "Брус", "Пояс", "Материал" },
                ["услуги"] = new[] { "Работа", "Доставка" },
                ["откосы"] = new[] { "Откос", "Работа за откос" }
            };

        /// <summary>Returns the category key of a product, or "" when unknown.</summary>
        public static string GetCategory(string? name)
        {
            foreach (var kv in Categories)
                if (kv.Value.Contains(name ?? "", StringComparer.Ordinal))
                    return kv.Key;
            return "";
        }
    }
}
