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
        public double Quantity { get; set; } = 1;
        public double Price { get; set; }
        public int InstallationMode { get; set; }
        public bool HasInstallation { get; set; } = true;
        public double InstallationDeduction { get; set; } = -500;
        public double InstallationSurcharge { get; set; } = -500;
        // v3.43.2.10: signed adjustment for mode 0 («Монтаж включён»).
        // Default 0 → backward compatible (existing orders.json without this
        // field load cleanly as no-op adjustment).
        public double InstallationAdjustment { get; set; } = 0;
        public bool IsActive { get; set; } = true;
        public int AnwisSizeMode { get; set; }
        public bool IsAnticat { get; set; }
        // «Свой товар» marker — keeps manual-sum semantics after save/load.
        public bool IsCustomProduct { get; set; }
        // «Работа» marker — keeps labor semantics (1 шт, no dims, included in InstallationLaborTotal) even if renamed.
        public bool IsLabor { get; set; }
        public SlopeCalculationData? SlopeData { get; set; }
    }

    public static class OrderItemDataExtensions
    {
        public static OrderItem ToOrderItem(this OrderItemData od)
        {
            var item = new OrderItem
            {
                Name = od.Name == "Откос материал" ? "Откос" : od.Name,
                Color = od.Color,
                Width = od.Width,
                Height = od.Height,
                Quantity = od.Quantity,
                Price = od.Price,
                InstallationMode = od.InstallationMode != 0 ? od.InstallationMode : (od.HasInstallation ? 0 : 1),
                InstallationDeduction = (od.InstallationDeduction > 0 && od.InstallationMode != 0) && !ProductCatalog.IsPerLinearMeter(od.Name)
                    ? -od.InstallationDeduction : od.InstallationDeduction,
                InstallationSurcharge = (od.InstallationSurcharge > 0 && od.InstallationMode != 0) && !ProductCatalog.IsPerLinearMeter(od.Name)
                    ? -od.InstallationSurcharge : od.InstallationSurcharge,
                InstallationAdjustment = od.InstallationAdjustment,
                IsActive = od.IsActive,
                IsAnticat = od.IsAnticat,
                IsCustomProduct = od.IsCustomProduct,
                IsLabor = od.IsLabor,
                SlopeData = od.SlopeData?.ToSlopeCalculation()
            };
            item.Recalculate();
            return item;
        }
    }
}
