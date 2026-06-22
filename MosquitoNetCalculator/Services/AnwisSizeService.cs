using System;
using System.Collections.Generic;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Сервис UI-данных Anwis (словари меток, подсказок, заголовков).
    /// ВСЯ логика пересчёта размеров вынесена в <see cref="AnwisSize"/>.
    /// </summary>
    public static class AnwisSizeService
    {
        /// <summary>Короткая метка режима для segmented control (5 кнопок-пилюль).</summary>
        public static readonly Dictionary<AnwisSizeMode, string> ShortLabels = new()
        {
            [AnwisSizeMode.Брусбокс60] = "ББ 60",
            [AnwisSizeMode.Брусбокс70] = "ББ 70",
            [AnwisSizeMode.Профипласт] = "ПП",
            [AnwisSizeMode.РазмерПроёма] = "Проём",
            [AnwisSizeMode.Габаритный] = "Габарит"
        };

        /// <summary>Полное название режима (для тултипов и копирования).</summary>
        public static readonly Dictionary<AnwisSizeMode, string> FullLabels = new()
        {
            [AnwisSizeMode.Брусбокс60] = "Брусбокс 60",
            [AnwisSizeMode.Брусбокс70] = "Брусбокс 70",
            [AnwisSizeMode.Профипласт] = "Профипласт",
            [AnwisSizeMode.РазмерПроёма] = "Размер проёма",
            [AnwisSizeMode.Габаритный] = "Габаритный"
        };

        /// <summary>Описание эффекта режима (короткое, для контекстного меню).</summary>
        public static readonly Dictionary<AnwisSizeMode, string> HintTexts = new()
        {
            [AnwisSizeMode.Брусбокс60] = "W+2 / H-30. Копия: -20 мм.",
            [AnwisSizeMode.Брусбокс70] = "W-2 / H-30. Копия: -20 мм.",
            [AnwisSizeMode.Профипласт] = "Без изменений. Копия: -20 мм.",
            [AnwisSizeMode.РазмерПроёма] = "W+20 / H+20. Копия: -20 мм.",
            [AnwisSizeMode.Габаритный] = "Без изменений. Копия: -20 мм."
        };

        /// <summary>Описание эффекта режима (короткое, для контекстного меню).</summary>
        public static readonly Dictionary<AnwisSizeMode, string> Descriptions = new()
        {
            [AnwisSizeMode.Брусбокс60] = "W+2 / H-30. Копия: -20 мм",
            [AnwisSizeMode.Брусбокс70] = "W-2 / H-30. Копия: -20 мм",
            [AnwisSizeMode.Профипласт] = "Без изменений. Копия: -20 мм",
            [AnwisSizeMode.РазмерПроёма] = "W+20 / H+20. Копия: -20 мм",
            [AnwisSizeMode.Габаритный] = "Без изменений. Копия: -20 мм"
        };

        /// <summary>
        /// Заголовок секции Anwis для FactoryTextService.
        /// Пример: "Anwis, размер проёма (Режим: Брусбокс 60)".
        /// </summary>
        public static string GetSectionHeader(AnwisSizeMode mode)
            => $"Anwis, размер проёма (Режим: {FullLabels[mode]})";

        /// <summary>
        /// Генерирует объяснение для выбранного режима Anwis в формате
        /// (ввод, расчёт, завод) на живом примере 1000×1000.
        /// Числа рассчитываются через <see cref="AnwisSize"/> — поэтому
        /// объяснение автоматически синхронно с формулами.
        /// </summary>
        public static (string Input, string Calc, string Factory) GetExplanation(AnwisSizeMode mode)
        {
            // Живой пример: пользователь вводит 1000×1000.
            var sample = AnwisSize.ОтВвода(1000, 1000, mode);

            // Концептуальная подпись: что именно вводит пользователь.
            string inputConcept = mode switch
            {
                AnwisSizeMode.РазмерПроёма => "Ввод: чистые размеры от уплотнения до уплотнения",
                AnwisSizeMode.Габаритный   => "Ввод: размеры с +20 мм от проёма",
                _ => "Ввод: размеры, которые показывает программа (то, что на экране)"
            };

            // Короткая формула расчёта в скобках.
            string calcRule = mode switch
            {
                AnwisSizeMode.Брусбокс60   => "(W+2, H−30)",
                AnwisSizeMode.Брусбокс70   => "(W−2, H−30)",
                AnwisSizeMode.РазмерПроёма => "(W+20, H+20)",
                _ => "(без изменений)"
            };

            return (
                inputConcept,
                $"Расчёт (площадь, цена): 1000×1000 → {(int)sample.ШиринаРасчёт}×{(int)sample.ВысотаРасчёт} {calcRule}",
                $"На завод (копия): расчёт − 20 мм → {(int)sample.ШиринаЗавод}×{(int)sample.ВысотаЗавод}"
            );
        }

        /// <summary>
        /// Возвращает true, если для данного товара применим выбор режима Anwis.
        /// </summary>
        public static bool IsApplicable(string? productName)
            => string.Equals(productName, "Anwis", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Возвращает значение AnwisSizeMode по умолчанию для новых позиций Anwis.
        /// </summary>
        public const AnwisSizeMode DefaultMode = AnwisSizeMode.Брусбокс60;
    }
}
