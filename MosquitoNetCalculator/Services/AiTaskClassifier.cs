using System;
using System.Linq;

namespace MosquitoNetCalculator.Services
{
    /// <summary>Broad request categories used by the automatic free-model selector.</summary>
    public enum AiTaskType
    {
        Calculator,
        Help,
        General
    }

    /// <summary>
    /// Lightweight, deterministic classifier. It deliberately runs locally and
    /// never sends the user's text to another service before model selection.
    /// </summary>
    public static class AiTaskClassifier
    {
        private static readonly string[] CalculatorTerms =
        {
            "добав", "сделай", "создай", "добавь", "удал", "очист", "измени", "поменя",
            "смени", "обнови", "текущ", "заказ", "расчёт", "расчет", "позици", "товар",
            "сетка", "anwis", "анвис", "отлив", "козыр", "откос", "короб", "псул",
            "уплотнен", "брус", "пояс", "доставк", "монтаж", "размер", "ширин", "высот",
            "глубин", "бб60", "бб70", "профипласт", "проём", "проем", "габарит",
            "цену", "цена", "руб", "штук", "шт"
        };

        private static readonly string[] HelpTerms =
        {
            "как ", "как?", "помощ", "объясн", "что уме", "где найти", "инструкц",
            "обновлен", "верси", "настройк", "ключ", "модел", "программ", "функци"
        };

        public static AiTaskType Classify(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return AiTaskType.General;

            var normalized = text.Trim().ToLowerInvariant();
            if (CalculatorTerms.Any(normalized.Contains))
                return AiTaskType.Calculator;
            if (HelpTerms.Any(normalized.Contains))
                return AiTaskType.Help;
            return AiTaskType.General;
        }
    }
}
