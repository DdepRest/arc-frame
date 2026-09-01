namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// Строка сводки расхода материалов откосов.
    /// Это display-only DTO: расчётные суммы остаются в SlopeCalculation.
    /// </summary>
    public sealed class MaterialSummaryRow
    {
        public string Name { get; set; } = "";

        /// <summary>Детализация количества для одной позиции/окна.</summary>
        public string PerDetail { get; set; } = "";

        /// <summary>Итоговое количество для сводки.</summary>
        public string TotalDisplay { get; set; } = "";

        /// <summary>Чип экономии, если оптимизация уменьшила расход.</summary>
        public string Note { get; set; } = "";

        public bool HasNote => !string.IsNullOrEmpty(Note);

        /// <summary>Подробный tooltip с расчётом экономии.</summary>
        public string? EconomyTooltip { get; set; }

        public bool HasEconomyTooltip => !string.IsNullOrEmpty(EconomyTooltip);
    }
}
