using System;

namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// Единый объект-конвертер размеров Anwis.
    /// Хранит три представления размера: Отображение (сырое/пользовательское),
    /// Расчёт (для площади/цены/КП), Завод (для текста «На завод»).
    ///
    /// Для не-Anwis товаров все три слоя равны (identity).
    ///
    /// Использование:
    ///   var size = AnwisSize.ОтВвода(1000, 1000, AnwisSizeMode.Брусбокс60);
    ///   size.ШиринаОтображение  // 1000 (сырое)
    ///   size.ШиринаРасчёт       // 1002 (W+2)
    ///   size.ШиринаЗавод        // 982  (расчёт − 20)
    /// </summary>
    public readonly struct AnwisSize
    {
        // ─── Публичные свойства ──────────────────────────────────────

        /// <summary>Ширина, введённая пользователем (сырая).</summary>
        public double ШиринаОтображение { get; }

        /// <summary>Высота, введённая пользователем (сырая).</summary>
        public double ВысотаОтображение { get; }

        /// <summary>Ширина для расчёта площади и стоимости.</summary>
        public double ШиринаРасчёт { get; }

        /// <summary>Высота для расчёта площади и стоимости.</summary>
        public double ВысотаРасчёт { get; }

        /// <summary>Ширина для текста «На завод» (расчёт − 20 мм).</summary>
        public double ШиринаЗавод { get; }

        /// <summary>Высота для текста «На завод» (расчёт − 20 мм).</summary>
        public double ВысотаЗавод { get; }

        /// <summary>Режим размера Anwis.</summary>
        public AnwisSizeMode Режим { get; }

        // ─── Приватный конструктор ────────────────────────────────────

        private AnwisSize(double rawW, double rawH,
                          double calcW, double calcH,
                          AnwisSizeMode mode)
        {
            ШиринаОтображение = Math.Max(0, rawW);
            ВысотаОтображение = Math.Max(0, rawH);
            ШиринаРасчёт = Math.Max(0, calcW);
            ВысотаРасчёт = Math.Max(0, calcH);
            // Завод = расчёт − 20 мм (для Anwis); для не-Anwis = расчёт (identity)
            ШиринаЗавод = Math.Max(0, calcW - 20);
            ВысотаЗавод = Math.Max(0, calcH - 20);
            Режим = mode;
        }

        // ─── Фабрики ─────────────────────────────────────────────────

        /// <summary>
        /// Создаёт AnwisSize из сырых размеров, введённых пользователем.
        /// Применяет расчётные корректировки согласно режиму.
        /// Для не-Anwis товаров все три слоя равны.
        /// </summary>
        public static AnwisSize ОтВвода(double rawW, double rawH, AnwisSizeMode mode)
        {
            double calcW = ApplyCalcWidth(rawW, mode);
            double calcH = ApplyCalcHeight(rawH, mode);
            return new AnwisSize(rawW, rawH, calcW, calcH, mode);
        }

        /// <summary>
        /// Создаёт AnwisSize из хранимых расчётных размеров.
        /// Обратным пересчётом восстанавливает сырые размеры.
        /// Используется при загрузке заказов (JSON) и для computed свойства OrderItem.Размеры.
        /// </summary>
        public static AnwisSize ОтХранимого(double calcW, double calcH, AnwisSizeMode mode)
        {
            double rawW = ReverseCalcWidth(calcW, mode);
            double rawH = ReverseCalcHeight(calcH, mode);
            return new AnwisSize(rawW, rawH, calcW, calcH, mode);
        }

        /// <summary>
        /// Создаёт новый AnwisSize с тем же сырым размером, но другим режимом.
        /// Расчётные размеры пересчитываются под новый режим.
        /// </summary>
        public AnwisSize СРежимом(AnwisSizeMode newMode)
            => ОтВвода(ШиринаОтображение, ВысотаОтображение, newMode);

        // ─── Формулы расчётных корректировок ──────────────────────────

        private static double ApplyCalcWidth(double rawW, AnwisSizeMode mode) => mode switch
        {
            AnwisSizeMode.Брусбокс60 => rawW + 2,
            AnwisSizeMode.Брусбокс70 => Math.Max(0, rawW - 2),
            AnwisSizeMode.РазмерПроёма => rawW + 20,
            _ => rawW // Профипласт, Габаритный, не-Anwis — identity
        };

        private static double ApplyCalcHeight(double rawH, AnwisSizeMode mode) => mode switch
        {
            AnwisSizeMode.Брусбокс60 => Math.Max(0, rawH - 30),
            AnwisSizeMode.Брусбокс70 => Math.Max(0, rawH - 30),
            AnwisSizeMode.РазмерПроёма => rawH + 20,
            _ => rawH // Профипласт, Габаритный, не-Anwis — identity
        };

        private static double ReverseCalcWidth(double calcW, AnwisSizeMode mode) => mode switch
        {
            AnwisSizeMode.Брусбокс60 => calcW - 2,
            AnwisSizeMode.Брусбокс70 => calcW + 2,
            AnwisSizeMode.РазмерПроёма => calcW - 20,
            _ => calcW // Профипласт, Габаритный, не-Anwis — identity
        };

        private static double ReverseCalcHeight(double calcH, AnwisSizeMode mode) => mode switch
        {
            AnwisSizeMode.Брусбокс60 => calcH + 30,
            AnwisSizeMode.Брусбокс70 => calcH + 30,
            AnwisSizeMode.РазмерПроёма => calcH - 20,
            _ => calcH // Профипласт, Габаритный, не-Anwis — identity
        };
    }
}
