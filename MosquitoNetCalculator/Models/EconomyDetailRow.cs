namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// v3.44.9: строка таблицы «Детали экономии» для откосов.
    /// Показывает, сколько материала ушло бы без экономии, сколько с экономией
    /// и какая суммарная/на откос экономия получилась.
    /// </summary>
    public class EconomyDetailRow
    {
        public string MaterialName { get; set; } = "";
        public string Unit { get; set; } = "";
        public string QtyWithoutEconomy { get; set; } = "";
        public string QtyWithEconomy { get; set; } = "";
        public string QtySaved { get; set; } = "";
        public double AmountSaved { get; set; }
        public string AmountSavedDisplay => AmountSaved > 0 ? $"−{AmountSaved:N0} ₽" : "0 ₽";
        public double AverageSavedPerSlope { get; set; }
        public string AverageSavedDisplay => AverageSavedPerSlope > 0 ? $"−{AverageSavedPerSlope:N0} ₽" : "0 ₽";

        /// <summary>
        /// Подробный tooltip с расшифровкой расчёта экономии для строки.
        /// </summary>
        public string Tooltip { get; set; } = "";
    }
}
