namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// Настройки печати, сохраняемые в памяти на время жизни заказа.
    /// Живут в MainWindow; НЕ сериализуются.
    /// </summary>
    public sealed class PrintSettings
    {
        /// <summary>Имя выбранного принтера (null = принтер по умолчанию).</summary>
        public string? PrinterName { get; set; }

        /// <summary>Кол-во копий (≥ 1).</summary>
        public int Copies { get; set; } = 1;

        /// <summary>Разбивка по копиям (collation).</summary>
        public bool Collated { get; set; } = true;

        /// <summary>true = цветная, false = ч/б.</summary>
        public bool Color { get; set; } = true;

        /// <summary>Режим печати страниц: All / Range / Single.</summary>
        public PageMode Pages { get; set; } = PageMode.All;

        /// <summary>Начальная страница диапазона (1-based, только при Pages=Range).</summary>
        public int PageFrom { get; set; } = 1;

        /// <summary>Конечная страница диапазона (1-based, только при Pages=Range).</summary>
        public int PageTo { get; set; } = 1;

        /// <summary>Отдельная страница (1-based, только при Pages=Single).</summary>
        public int SinglePage { get; set; } = 1;

        /// <summary>Создаёт копию настроек (для snapshot при открытии окна).</summary>
        public PrintSettings Clone() => (PrintSettings)MemberwiseClone();
    }
}
