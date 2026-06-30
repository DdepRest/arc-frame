namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// DTO для сериализации позиции заказа (JSON).
    /// Не содержит бизнес-логики, UI-свойств, INotifyPropertyChanged.
    ///
    /// `CalculatedValue` and `Total` are NOT stored here — they're always
    /// recomputed from W × H × Price × Quantity on load via
    /// <see cref="OrderItem.Recalculate"/>. Old JSON files containing these
    /// fields still load (System.Text.Json ignores unknown fields).
    /// </summary>
    public class OrderItemData
    {
        public string Name { get; set; } = "";
        public string Color { get; set; } = "";
        public double Width { get; set; }
        public double Height { get; set; }
        public int Quantity { get; set; } = 1;
        public double Price { get; set; }
        public int InstallationMode { get; set; }
        public bool HasInstallation { get; set; } = true;
        public double InstallationDeduction { get; set; } = 500;
        public double InstallationSurcharge { get; set; } = 500;
        public bool IsActive { get; set; } = true;
        public int AnwisSizeMode { get; set; }
        public bool IsAnticat { get; set; }
    }
}