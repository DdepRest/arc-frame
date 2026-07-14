using System;
using MosquitoNetCalculator.Services;

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
    ///
    /// v3.45.0 (Phase 5 refactoring): pure calculation functions moved to
    /// <see cref="AnwisSizeCalculator"/>; this struct remains a thin value object.
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
            double calcW = AnwisSizeCalculator.ApplyCalcWidth(rawW, mode);
            double calcH = AnwisSizeCalculator.ApplyCalcHeight(rawH, mode);
            return new AnwisSize(rawW, rawH, calcW, calcH, mode);
        }

        /// <summary>
        /// Создаёт AnwisSize из хранимых расчётных размеров.
        /// Обратным пересчётом восстанавливает сырые размеры.
        /// Используется при загрузке заказов (JSON) и для computed свойства OrderItem.Размеры.
        /// </summary>
        public static AnwisSize ОтХранимого(double calcW, double calcH, AnwisSizeMode mode)
        {
            double rawW = AnwisSizeCalculator.ReverseCalcWidth(calcW, mode);
            double rawH = AnwisSizeCalculator.ReverseCalcHeight(calcH, mode);
            return new AnwisSize(rawW, rawH, calcW, calcH, mode);
        }

        /// <summary>
        /// Создаёт новый AnwisSize с тем же сырым размером, но другим режимом.
        /// Расчётные размеры пересчитываются под новый режим.
        /// </summary>
        public AnwisSize СРежимом(AnwisSizeMode newMode)
            => ОтВвода(ШиринаОтображение, ВысотаОтображение, newMode);

        /// <summary>
        /// Identity AnwisSize where all three layers (Отображение, Расчёт, Завод) are equal.
        /// Used for non-Anwis products where no size conversion is needed.
        /// </summary>
        public static AnwisSize Identity(double w, double h)
            => new AnwisSize(w, h, w, h, AnwisSizeMode.Габаритный);
    }
}
