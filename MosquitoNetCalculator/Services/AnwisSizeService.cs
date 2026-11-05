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
        /// Заголовок секции Anwis по режиму (был нужен FactoryTextService,
        /// после v3.54 используется только тестами словаря).
        /// Пример: "Anwis, размер проёма (ББ 60)".
        /// </summary>
        public static string GetSectionHeader(AnwisSizeMode mode)
            => $"Anwis, размер проёма ({ShortLabels[mode]})";

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
        {
            if (string.IsNullOrWhiteSpace(productName)) return false;
            var norm = Models.ProductCatalog.NormalizeProductName(productName);
            return string.Equals(norm, "Anwis", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Возвращает true, если товар является 2-режимной сеткой («На навесах» или «Оконная на метал. крепл.»).
        /// Для этих сеток доступны только режимы «Габарит» (+0 мм) и «Проём» (+40 мм).
        /// </summary>
        public static bool IsTwoModeScreen(string? productName)
        {
            if (string.IsNullOrWhiteSpace(productName)) return false;
            var norm = Models.ProductCatalog.NormalizeProductName(productName);
            return string.Equals(norm, "На навесах", StringComparison.OrdinalIgnoreCase)
                || string.Equals(norm, "Оконная на метал. крепл.", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Возвращает true, если для данного товара применим выбор режима размеров (Anwis или 2-режимные сетки).
        /// </summary>
        public static bool SupportsSizeMode(string? productName)
            => IsApplicable(productName) || IsTwoModeScreen(productName);

        /// <summary>
        /// Возвращает список доступных режимов для указанного товара.
        /// Для 2-режимных сеток порядок: Габарит, Проём.
        /// Для Anwis: ББ 60, ББ 70, ПП, Проём, Габарит.
        /// </summary>
        public static IReadOnlyList<AnwisSizeMode> GetAvailableModes(string? productName)
        {
            if (IsTwoModeScreen(productName))
            {
                return new[] { AnwisSizeMode.Габаритный, AnwisSizeMode.РазмерПроёма };
            }
            if (IsApplicable(productName))
            {
                return new[]
                {
                    AnwisSizeMode.Брусбокс60,
                    AnwisSizeMode.Брусбокс70,
                    AnwisSizeMode.Профипласт,
                    AnwisSizeMode.РазмерПроёма,
                    AnwisSizeMode.Габаритный
                };
            }
            return Array.Empty<AnwisSizeMode>();
        }

        /// <summary>
        /// Возвращает режим по умолчанию для указанного товара:
        /// для Anwis — Профипласт (ПП), для 2-режимных сеток — Габаритный (Габарит).
        /// </summary>
        public static AnwisSizeMode GetDefaultMode(string? productName)
        {
            if (IsTwoModeScreen(productName))
                return AnwisSizeMode.Габаритный;
            return DefaultMode;
        }

        /// <summary>
        /// Возвращает короткую метку режима для строки заказа.
        /// </summary>
        public static string GetShortLabel(string? productName, AnwisSizeMode mode)
        {
            return ShortLabels.TryGetValue(mode, out var l) ? l : "";
        }

        /// <summary>
        /// Возвращает всплывающую подсказку для режима с учётом типа сетки.
        /// </summary>
        public static string GetToolTip(string? productName, AnwisSizeMode mode)
        {
            if (IsTwoModeScreen(productName))
            {
                return mode switch
                {
                    AnwisSizeMode.РазмерПроёма => "Размер проёма: от уплотнения до уплотнения (W+40 / H+40)",
                    AnwisSizeMode.Габаритный   => "Габаритный: готовая сетка (без изменений)",
                    _ => ""
                };
            }
            if (IsApplicable(productName))
            {
                return FullLabels.TryGetValue(mode, out var f) && Descriptions.TryGetValue(mode, out var d)
                    ? $"{f}: {d}"
                    : "";
            }
            return "";
        }

        /// <summary>
        /// Генерирует объяснение для выбранного режима в формате (ввод, расчёт, завод) на живом примере 1000×1000.
        /// </summary>
        public static (string Input, string Calc, string Factory) GetExplanation(AnwisSizeMode mode, string? productName = null)
        {
            if (IsTwoModeScreen(productName))
            {
                var sample = AnwisSize.ОтВвода(1000, 1000, mode, productName);
                if (mode == AnwisSizeMode.РазмерПроёма)
                {
                    return (
                        "Ввод: чистые размеры от уплотнения до уплотнения",
                        $"Расчёт (площадь, цена, импост): 1000×1000 → {(int)sample.ШиринаРасчёт}×{(int)sample.ВысотаРасчёт} (W+40, H+40)",
                        $"Готовая сетка: {(int)sample.ШиринаРасчёт}×{(int)sample.ВысотаРасчёт}"
                    );
                }
                return (
                    "Ввод: готовая сетка (без изменений)",
                    "Расчёт (площадь, цена): 1000×1000 → 1000×1000 (без изменений)",
                    "Готовая сетка: 1000×1000"
                );
            }

            return GetExplanation(mode);
        }

        /// <summary>
        /// Режим Anwis по умолчанию для новых позиций: ПП (Профипласт).
        /// Используется как начальный выбор в QuickAdd и при сбросе режима
        /// (StartNewOrder / OpenSelectedOrder).
        /// </summary>
        public const AnwisSizeMode DefaultMode = AnwisSizeMode.Профипласт;
    }
}
