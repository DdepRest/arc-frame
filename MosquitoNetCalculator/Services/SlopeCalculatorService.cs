using System;
using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Сервис для автоматического расчёта откосов из сэндвич-панелей.
    /// Все формулы из spec: docs/slope-sandwich-calculator-spec.md
    /// </summary>
    public static class SlopeCalculatorService
    {
        // ─── Публичные методы ───

        /// <summary>
        /// Выполняет полный расчёт откосов для одного окна.
        /// </summary>
        /// <param name="widthMm">Ширина окна, мм.</param>
        /// <param name="heightMm">Высота окна, мм.</param>
        /// <param name="depthM">Глубина откоса, метры.</param>
        /// <param name="windowCount">Количество окон (Q).</param>
        /// <param name="totalWindowCount">Общее количество откосов в заказе (для экономии герметика/скотча).</param>
        /// <param name="sandwichPrice">Цена сэндвича за м².</param>
        /// <param name="foamPrice">Цена пены за баллон.</param>
        /// <param name="sealantPrice">Цена герметика за тюбик.</param>
        /// <param name="tapePrice">Цена скотча за моток.</param>
        /// <param name="startPrice">Цена старта за полосу 3 м.</param>
        /// <param name="fProfilePrice">Цена F-планки за полосу 3 м.</param>
        /// <param name="penoplexPrice">Цена пеноплекса за лист.</param>
        /// <param name="laborPrice">Цена работы за м.п.</param>
        /// <returns>Заполненный SlopeCalculation.</returns>
        public static SlopeCalculation Calculate(
            double widthMm,
            double heightMm,
            double depthM,
            int windowCount,
            int totalWindowCount,
            double sandwichPrice = 1200,
            double foamPrice = 750,
            double sealantPrice = 350,
            double tapePrice = 135,
            double startPrice = 135,
            double fProfilePrice = 250,
            double penoplexPrice = 450,
            double laborPrice = 600)
        {
            // v3.43.3: единый helper _ApplyDefaults покрывает все 10 материалов
            // и параметры размеров рамы. Calculate создаёт пустой SlopeCalculation,
            // UpdateInPlace — мутирует существующий.
            var calc = new SlopeCalculation();
            _ApplyDefaults(
                calc,
                widthMm, heightMm, depthM,
                windowCount, totalWindowCount,
                sandwichPrice, foamPrice, sealantPrice, tapePrice,
                startPrice, fProfilePrice, penoplexPrice, 500, laborPrice, 500);
            return calc;
        }

        /// <summary>
        /// Автоопределение глубины из строки ввода.
        /// Правило:
        /// - Если строка начинается с "0." → парсим как метры
        /// - Иначе: целое число → мм (конвертируем в метры)
        /// </summary>
        public static (double depthM, string hint) ParseDepth(string input)
        {
            input = (input ?? "").Trim().Replace(',', '.');

            if (string.IsNullOrEmpty(input))
                return (0, "");

            if (input.StartsWith("0."))
            {
                // Парсим как метры
                if (double.TryParse(input, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double meters))
                {
                    return (meters, $"{meters:F2} м");
                }
            }
            else
            {
                // Парсим как мм
                if (int.TryParse(input, out int mm))
                {
                    double meters = mm / 1000.0;
                    return (meters, $"{mm} мм → {meters:F2} м");
                }
            }

            return (0, "");
        }

        // ─── Внутренние методы ───

        /// <summary>
        /// Greedy-алгоритм оптимизации раскроя.
        /// Дано: pieces — массив длин в мм, полоса = 3000 мм.
        /// Задача: нарезать все куски из минимального числа полос.
        /// </summary>
        /// <remarks>
        /// v3.43.3: сигнатура изменена с (int, int) на params int[].
        /// Раньше метод хардкодил 3 стороны [W, H, H] — это соответствует
        /// формуле P3 для Сэндвича. Старт/F-планка физически идут по
        /// 4 сторонам (верх+низ+2 бока), поэтому для них вызываем
        /// OptimizeStrips(W, W, H, H). Старый 2-аргументный overload
        /// сохранён для backward compat и читаемости — он по-прежнему
        /// возвращает результат для 3 сторон, что соответствует P3-сэндвичу.
        /// </remarks>
        public static int OptimizeStrips(params int[] pieces)
        {
            if (pieces == null || pieces.Length == 0)
                return 0;

            const int stripLen = 3000;
            var toPack = new List<int>();
            int strips = 0;

            // v3.43.5 (bugfix #8): сначала отрезаем полные полосы для кусков ≥ 3000 мм.
            // Остатки (< 3000 мм) складываем в общую кучу и оптимизируем совместно
            // (Best Fit Decreasing). Раньше остаток каждого длинного куска шёл
            // только в свой offcut-пул, поэтому [3500, 3500] давало 4 полосы
            // вместо оптимальных 3 (500+500 в одной полосе).
            foreach (int p in pieces)
            {
                if (p <= 0) continue;

                if (p >= stripLen)
                {
                    strips += p / stripLen;
                    int remainder = p % stripLen;
                    if (remainder > 0)
                        toPack.Add(remainder);
                }
                else
                {
                    toPack.Add(p);
                }
            }

            // Сортируем по убыванию — большие куски первыми (меньше полос).
            var sorted = toPack.OrderByDescending(p => p).ToArray();
            if (sorted.Length == 0)
                return strips;

            var offcuts = new List<int>();

            foreach (int piece in sorted)
            {
                // Ищем наименьший подходящий остаток
                int best = -1;
                foreach (var o in offcuts)
                {
                    if (o >= piece && (best < 0 || o < best))
                        best = o;
                }

                if (best >= 0)
                {
                    offcuts.Remove(best);
                    int remainder = best - piece;
                    if (remainder > 0)
                        offcuts.Add(remainder);
                }
                else
                {
                    strips++;
                    int remainder = stripLen - piece;
                    if (remainder > 0)
                        offcuts.Add(remainder);
                }
            }

            return strips;
        }

        /// <summary>
        /// Backward-compat overload: тот же алгоритм для 3 сторон (P3 формула для Сэндвича).
        /// </summary>
        public static int OptimizeStrips(int widthMm, int heightMm)
            => OptimizeStrips(widthMm, heightMm, heightMm);

        /// <summary>
        /// v3.43.3: раскрой для Старт/F-планки — полный периметр (4 стороны: верх+низ+2 бока).
        /// Заменяет прежний вызов OptimizeStrips(W, H), который считал только 3 стороны.
        /// </summary>
        /// <remarks>
        /// Примеры:
        ///   W=700,  H=1500 → 7300мм → 3 полосы (физически по периметру)
        ///   W=1500, H=1500 → 6000мм → 2 полосы (2×W=3000 + 2×H=3000, идеальный раскрой)
        ///   W=2150, H=1500 → 7300мм → 3 полосы (юзер жаловался: было 2, ожидал 3)
        ///   W=3500, H=1500 → 10000мм → 5 полос (по ceil: 4×top/bottom + 1×side, но
        ///             greedy реально даст 5, см. OptimizeStrips edge case > 3000)
        /// </remarks>
        public static int OptimizeStripsForPerimeter(int widthMm, int heightMm)
            => OptimizeStrips(widthMm, widthMm, heightMm, heightMm);

        /// <summary>
        /// v3.43.5: раскрой Старт/F-планки для N окон СРАЗУ — общая оптимизация по всем окнам.
        /// Вызов: OptimizeStrips(W,W,H,H, W,W,H,H, ...) N раз (4 стороны).
        /// v3.43.8: больше не используется для Старт/F-планки (теперь 3 стороны).
        /// Оставлен для обратной совместимости.
        /// </summary>
        public static int OptimizeStripsForMultipleWindows(int widthMm, int heightMm, int totalWindowCount)
        {
            if (totalWindowCount <= 1)
                return OptimizeStripsForPerimeter(widthMm, heightMm);

            var pieces = new List<int>(totalWindowCount * 4);
            for (int i = 0; i < totalWindowCount; i++)
            {
                pieces.Add(widthMm);
                pieces.Add(widthMm);
                pieces.Add(heightMm);
                pieces.Add(heightMm);
            }
            return OptimizeStrips(pieces.ToArray());
        }

        /// <summary>
        /// v3.43.8: 3-сторонний раскрой [W, H, H] для N окон.
        /// Используется для Старта (верх + 2 бока, без низа).
        /// </summary>
        public static int OptimizeStripsForMultipleWindows3Sides(int widthMm, int heightMm, int totalWindowCount)
        {
            if (totalWindowCount <= 1)
                return OptimizeStrips(widthMm, heightMm);

            var pieces = new List<int>(totalWindowCount * 3);
            for (int i = 0; i < totalWindowCount; i++)
            {
                pieces.Add(widthMm);
                pieces.Add(heightMm);
                pieces.Add(heightMm);
            }
            return OptimizeStrips(pieces.ToArray());
        }

        /// <summary>
        /// Определяет количество листов пеноплекса по площади окна.
        /// </summary>
        public static int GetPenoplexSheets(double area)
        {
            // ≤ 2.0 м² → 1 лист (1-2 створки)
            // 2.0–3.5 м² → 2 листа (3 створки)
            // > 3.5 м² → 3 листа (балконный блок)
            if (area <= 2.0)
                return 1;
            if (area <= 3.5)
                return 2;
            return 3;
        }

        /// <summary>
        /// v3.43.3: in-place пересчёт существующего SlopeCalculation вместо создания нового.
        /// Позволяет сохранить ручные правки Quantity/Price пользователя (IsQuantityOverridden=true)
        /// при изменении размеров окна (W/H/D) или кол-ва окон (Q).
        /// </summary>
        /// <remarks>
        /// Логика зеркальна Calculate(), но:
        ///   • сначала ставим W/H/D (каскад пересчитывает P3/P4/S/Area),
        ///   • затем для каждого материала: если !IsQuantityOverridden — пересчитываем
        ///     Quantity по формуле, иначе оставляем как есть;
        ///   • Price всегда ставим по каталогу (если юзер не хочет сохранить свою цену
        ///     — он тоже может выставить IsQuantityOverridden, но для v3.43.3 мы этого
        ///     пока не делаем: цена легче перевыставляется через вкладку «Цены»,
        ///     а IsQuantityOverridden снимает оба флага).
        /// </remarks>
        public static void UpdateInPlace(
            SlopeCalculation calc,
            double widthMm,
            double heightMm,
            double depthM,
            int windowCount,
            int totalWindowCount,
            double sandwichPrice = 1200,
            double foamPrice = 750,
            double sealantPrice = 350,
            double tapePrice = 135,
            double startPrice = 135,
            double fProfilePrice = 250,
            double penoplexPrice = 450,
            double laborPrice = 600)
        {
            if (calc == null) throw new ArgumentNullException(nameof(calc));
            // v3.43.3: всё тело сведено в _ApplyDefaults — он же используется в Calculate.
            // Поведенческая разница: Calculate создаёт новый SlopeCalculation, UpdateInPlace
            // мутирует существующий. Helper сам делает `if (old != new)` для каждого поля,
            // чтобы избежать лишних PropertyChanged каскадов на идентичных значениях.
            // Override-логика (IsQuantityOverridden) применяется одинаково в обоих путях.
            _ApplyDefaults(
                calc,
                widthMm, heightMm, depthM,
                windowCount, totalWindowCount,
                sandwichPrice, foamPrice, sealantPrice, tapePrice,
                startPrice, fProfilePrice, penoplexPrice, 500, laborPrice, 500);
        }

        // ─── Приватный helper (v3.43.3) ────────────────────────────────

        /// <summary>
        /// Единая точка применения формул + каталожных цен к SlopeCalculation.
        /// Используется в Calculate (новый) и UpdateInPlace (мутация).
        /// Уважает <see cref="SlopeMaterial.IsQuantityOverridden"/> —
        /// если юзер правил Quantity/Price в панели вручную, helper не
        /// перетирает их при изменении W/H/D/Q/прайса.
        /// </summary>
        private static void _ApplyDefaults(
            SlopeCalculation calc,
            double widthMm,
            double heightMm,
            double depthM,
            int windowCount,
            int totalWindowCount,
            double sandwichPrice,
            double foamPrice,
            double sealantPrice,
            double tapePrice,
            double startPrice,
            double fProfilePrice,
            double penoplexPrice,
            double laminatinaPrice,
            double laborPrice,
            double laminatinaLaborPrice)
        {
            // 0. WindowCount — влияет на расчёт герметика/скотча (экономия по всему заказу).
            if (calc.WindowCount != windowCount) calc.WindowCount = windowCount;

            // 1. Размеры рамы. Их setter-ы автоматически ре-emit'ят P3/P4/S/Area
            //    (см. SlopeCalculation WidthMm/HeightMm/DepthM) → дальше
            //    calc.P3 / calc.P4 / calc.S / calc.Area возвращают актуальные значения
            //    для авто-формул материалов.
            if (calc.WidthMm != widthMm) calc.WidthMm = widthMm;
            if (calc.HeightMm != heightMm) calc.HeightMm = heightMm;
            if (calc.DepthM != depthM) calc.DepthM = depthM;

            // 2. Сэндвич: P3 × D × Price (м² × ₽/м²). 4 стороны не нужны — это площадь.
            if (!calc.Sandwich.IsQuantityOverridden)
            {
                calc.Sandwich.Quantity = Math.Round(calc.S, 4);
                calc.Sandwich.Price = sandwichPrice;
            }
            calc.Sandwich.Unit = "м²"; calc.Sandwich.Name = "Сэндвич";

            // 3. Пена: 1 баллон на окно.
            if (!calc.Foam.IsQuantityOverridden)
            {
                calc.Foam.Quantity = 1;
                calc.Foam.Price = foamPrice;
            }
            calc.Foam.Unit = "баллон"; calc.Foam.Name = "Пена";

            // 4. Герметик / Скотч — экономия по N_total (по всему заказу).
            if (!calc.Sealant.IsQuantityOverridden)
            {
                calc.Sealant.Quantity = Math.Ceiling(totalWindowCount / 4.0);
                calc.Sealant.Price = sealantPrice;
            }
            calc.Sealant.Unit = "тюбик"; calc.Sealant.Name = "Герметик";

            if (!calc.Tape.IsQuantityOverridden)
            {
                calc.Tape.Quantity = Math.Ceiling(totalWindowCount / 3.0);
                calc.Tape.Price = tapePrice;
            }
            calc.Tape.Unit = "моток"; calc.Tape.Name = "Скотч";

            // 5. Старт: 3 стороны (верх + 2 бока, без низа).
            if (!calc.StartProfile.IsQuantityOverridden)
            {
                calc.StartProfile.Quantity = OptimizeStrips((int)widthMm, (int)heightMm);
                calc.StartProfile.Price = startPrice;
            }
            calc.StartProfile.Unit = "полоса 3 м"; calc.StartProfile.Name = "Старт";

            // 6. F-планка: 3 стороны с запасом +100 мм на сторону.
            if (!calc.FProfile.IsQuantityOverridden)
            {
                calc.FProfile.Quantity = OptimizeStrips((int)widthMm + 100, (int)heightMm + 100);
                calc.FProfile.Price = fProfilePrice;
            }
            calc.FProfile.Unit = "полоса 3 м"; calc.FProfile.Name = "F-планка";

            // 7. Пеноплекс: по площади окна (≤2м²=1 лист, ≤3.5=2, иначе 3).
            if (!calc.Penoplex.IsQuantityOverridden)
            {
                calc.Penoplex.Quantity = GetPenoplexSheets(calc.Area);
                calc.Penoplex.Price = penoplexPrice;
            }
            calc.Penoplex.Unit = "лист"; calc.Penoplex.Name = "Пеноплекс";

            // 8. Ламинат: по умолчанию 0 шт, цена из прайса.
            if (!calc.Laminatina.IsQuantityOverridden)
            {
                calc.Laminatina.Quantity = 0;
                calc.Laminatina.Price = laminatinaPrice;
            }
            calc.Laminatina.Unit = "шт."; calc.Laminatina.Name = "Ламинат";

            // 9. Работа: P4 × Price (м.п. × ₽/м.п.). 4 стороны периметра.
            if (!calc.Labor.IsQuantityOverridden)
            {
                calc.Labor.Quantity = Math.Round(calc.P4, 4);
                calc.Labor.Price = laborPrice;
            }
            calc.Labor.Unit = "м.п."; calc.Labor.Name = "Работа за откос";

            // 10. Работа за ламинат: по умолчанию 0 шт, цена из прайса.
            if (!calc.LaminatinaLabor.IsQuantityOverridden)
            {
                calc.LaminatinaLabor.Quantity = 0;
                calc.LaminatinaLabor.Price = laminatinaLaborPrice;
            }
            calc.LaminatinaLabor.Unit = "шт."; calc.LaminatinaLabor.Name = "Работа за ламинат";

            // v3.43.5 (review fix): защитная инициализация для изолированного            // использования (unit-тесты, одиночный откос). В многопозиционном            // заказе RecalculateSealantAndTape перезапишет значение.
            if (windowCount > 0 && windowCount == totalWindowCount)
            {
                calc.DistributedSharedSum = calc.Sealant.Sum + calc.Tape.Sum;
            }
        }

        /// <summary>
        /// Пересчитывает общие (shared) материалы для ВСЕХ активных откосов в заказе.
        /// v3.43.4 (bugfix): Используем Distinct по ссылке SlopeData — если два
        /// OrderItem (Откос + Работа за откос) ссылаются на один и тот же
        /// SlopeCalculation (было до v3.43.4), без Distinct WindowCount
        /// суммируется дважды → sealant/tape Quantity завышается вдвойне.
        /// v3.43.5 (bugfix): герметик/скотч — общие материалы на весь заказ.
        /// Их сумма распределяется пропорционально WindowCount между строками
        /// «Откос», чтобы не множить общую стоимость на количество строк.
        /// v3.44.1: добавлена оптимизация Старт/F-планка по всем окнам заказа
        /// при включённой галочке экономии. Учитываются только IsActive строки.
        /// </summary>
        public static void RecalculateSealantAndTape(IEnumerable<OrderItem> items)
        {
            // Учитываем только активные материальные строки «Откос»; «Работа за откос»
            // не должна влиять на общее количество окон и на распределение.
            // v3.44.1: IsActive фильтр — выключенные чекбоксом строки исключаются
            // из расчёта общих материалов.
            var slopeItems = items.Where(i => i.SlopeData != null && i.Name == "Откос" && i.IsActive)
                                   .Select(i => i.SlopeData!)
                                   .Distinct()
                                   .ToList();

            // Все уникальные SlopeData строк «Откос» (независимо от IsActive),
            // чтобы сбросить DistributedSharedSum у выключенных позиций.
            var allSlopeData = items.Where(i => i.SlopeData != null && i.Name == "Откос")
                                    .Select(i => i.SlopeData!)
                                    .Distinct()
                                    .ToList();

            int totalWindowCount = slopeItems.Sum(s => s.WindowCount);

            // v3.44.2 (bugfix): экономия Старт/F-планка применяется только среди
            // откосов с IsProfileEconomyApplied=true. Глобальная оптимизация
            // раскроя и общая стоимость профилей распределяются только между
            // участниками экономии, чтобы не-экономящие откосы не платили за
            // чужие профили дважды.
            var economySlopes = slopeItems.Where(s => s.IsProfileEconomyApplied).ToList();
            bool applyProfileEconomy = economySlopes.Count > 0;

            int globalSealant = totalWindowCount > 0 ? (int)Math.Ceiling(totalWindowCount / 4.0) : 0;
            int globalTape = totalWindowCount > 0 ? (int)Math.Ceiling(totalWindowCount / 3.0) : 0;
            int globalStartStrips = 0;
            int globalFStrips = 0;

            if (applyProfileEconomy)
            {
                int economyWindowCount = economySlopes.Sum(s => s.WindowCount);
                var startPieces = new List<int>(economyWindowCount * 3);
                var fPieces = new List<int>(economyWindowCount * 3);
                foreach (var calc in economySlopes)
                {
                    for (int i = 0; i < calc.WindowCount; i++)
                    {
                        // Старт: 3 стороны (верх + 2 бока)
                        startPieces.Add((int)calc.WidthMm);
                        startPieces.Add((int)calc.HeightMm);
                        startPieces.Add((int)calc.HeightMm);
                        // F-планка: 3 стороны + 100 мм запаса на сторону
                        fPieces.Add((int)calc.WidthMm + 100);
                        fPieces.Add((int)calc.HeightMm + 100);
                        fPieces.Add((int)calc.HeightMm + 100);
                    }
                }
                globalStartStrips = OptimizeStrips(startPieces.ToArray());
                globalFStrips = OptimizeStrips(fPieces.ToArray());
            }

            foreach (var calc in slopeItems)
            {
                if (!calc.Sealant.IsQuantityOverridden)
                    calc.Sealant.Quantity = globalSealant;

                if (!calc.Tape.IsQuantityOverridden)
                    calc.Tape.Quantity = globalTape;

                if (calc.IsProfileEconomyApplied)
                {
                    // Экономия включена для этого откоса: используем глобально
                    // оптимизированное количество полос по всем участникам экономии.
                    if (!calc.StartProfile.IsQuantityOverridden)
                        calc.StartProfile.Quantity = globalStartStrips;
                    if (!calc.FProfile.IsQuantityOverridden)
                        calc.FProfile.Quantity = globalFStrips;
                }
                else
                {
                    // Экономия выключена: per-window количество профилей (3 стороны).
                    // НЕ умножаем на WindowCount — OrderItem.Total умножит per-window
                    // сумму на Quantity (=WindowCount) самостоятельно.
                    if (!calc.StartProfile.IsQuantityOverridden)
                        calc.StartProfile.Quantity = OptimizeStrips((int)calc.WidthMm, (int)calc.HeightMm);
                    if (!calc.FProfile.IsQuantityOverridden)
                        calc.FProfile.Quantity = OptimizeStrips((int)calc.WidthMm + 100, (int)calc.HeightMm + 100);
                }
            }

            // Распределяем общую стоимость shared-материалов между откосами
            // пропорционально WindowCount. Последний откос забирает остаток,
            // чтобы сумма по заказу совпадала с точностью до копейки.
            // v3.44.1: totalSharedSum считается от глобальных количеств, а не как
            // сумма по каждому calc — это устраняет двойной учёт sealant/tape
            // при нескольких строках «Откос».
            // v3.44.2: профили распределяются только между участниками экономии.
            if (totalWindowCount > 0 && slopeItems.Count > 0)
            {
                // v3.44.2: для цен shared-материалов используем референсный откос.
                // Для профилей берём референс из участников экономии, чтобы цены
                // соответствовали тому откосу, который платит за профили.
                var refCalc = slopeItems.First();
                var profileRefCalc = applyProfileEconomy ? economySlopes.First() : refCalc;
                double sealantTapeSharedSum = (globalSealant * refCalc.Sealant.Price)
                                            + (globalTape * refCalc.Tape.Price);
                double profileSharedSum = applyProfileEconomy
                    ? (globalStartStrips * profileRefCalc.StartProfile.Price)
                    + (globalFStrips * profileRefCalc.FProfile.Price)
                    : 0.0;

                // 1. Герметик + скотч — общие для всех активных откосов.
                double distributedSoFar = 0.0;
                for (int i = 0; i < slopeItems.Count; i++)
                {
                    var calc = slopeItems[i];
                    if (i == slopeItems.Count - 1)
                    {
                        calc.DistributedSharedSum = Math.Round(sealantTapeSharedSum - distributedSoFar, 2);
                    }
                    else
                    {
                        double share = Math.Round(sealantTapeSharedSum * calc.WindowCount / totalWindowCount, 2);
                        calc.DistributedSharedSum = share;
                        distributedSoFar += share;
                    }
                }

                // 2. Старт + F-планка — только между участниками экономии.
                if (applyProfileEconomy)
                {
                    int economyWindowCount = economySlopes.Sum(s => s.WindowCount);
                    double profileDistributedSoFar = 0.0;
                    for (int i = 0; i < economySlopes.Count; i++)
                    {
                        var calc = economySlopes[i];
                        if (i == economySlopes.Count - 1)
                        {
                            calc.DistributedSharedSum += Math.Round(profileSharedSum - profileDistributedSoFar, 2);
                        }
                        else
                        {
                            double share = Math.Round(profileSharedSum * calc.WindowCount / economyWindowCount, 2);
                            calc.DistributedSharedSum += share;
                            profileDistributedSoFar += share;
                        }
                    }
                }

            // Сбрасываем DistributedSharedSum у неактивных откосов, чтобы
            // выключенные чекбоксом строки не влияли на итоговую сумму.
            // NOTE: relies on reference equality; SlopeCalculation instances are
            // shared by reference between OrderItem.SlopeData and these lists.
            foreach (var calc in allSlopeData.Except(slopeItems))
            {
                calc.DistributedSharedSum = 0;
            }

            }
            else
            {
                // v3.44.x: broadened reset — DSS=0 для всех calcs, на которые ссылаются items.
                // Покрывает inactive «Откос» (зависал от defensive init) и orphan calcs после
                // rename (выпали из allSlopeData). Работа за откос calcs безопасны — используют TotalLabor.
                foreach (var calc in items.Where(i => i.SlopeData != null)
                                          .Select(i => i.SlopeData!)
                                          .Distinct())
                {
                    calc.DistributedSharedSum = 0;
                }
            }

            // Обновление DistributedSharedSum и Quantity материалов триггерит
            // PropertyChanged на SlopeCalculation → OrderItem.OnSlopeDataPropertyChanged
            // → OrderItem.Recalculate() (см. v3.44.1). Явный цикл Recalculate здесь
            // больше не нужен — если тесты вдруг упадут на propagation, сначала
            // проверить, что OnSlopeDataPropertyChanged всё ещё слушает и TotalMaterials,
            // и DistributedSharedSum.
        }
    }
}
