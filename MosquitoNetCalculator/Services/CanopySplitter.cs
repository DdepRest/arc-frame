using System;
using System.Collections.Generic;
using System.Linq;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Представляет отдельный раскроенный кусок козырька или отлива.
    /// </summary>
    public sealed class CutPiece
    {
        /// <summary>Порядковый индекс куска (0..n-1).</summary>
        public int Index { get; init; }

        /// <summary>Номинальная база длины (RoundUp(L / n, step)).</summary>
        public double Nominal { get; init; }

        /// <summary>Нахлёст на стык слева (0 или Overlap, мм).</summary>
        public double JointLeft { get; init; }

        /// <summary>Нахлёст на стык справа (0 или Overlap, мм).</summary>
        public double JointRight { get; init; }

        /// <summary>Нахлёст на угол в начале (0 или W, мм).</summary>
        public double CornerStart { get; init; }

        /// <summary>Нахлёст на угол в конце (0 или W, мм).</summary>
        public double CornerEnd { get; init; }

        /// <summary>Итоговая длина реза готового куска со всеми нахлёстами (мм).</summary>
        public double Length { get; init; }
    }

    /// <summary>
    /// Результат расчёта раскроя стороны на куски.
    /// </summary>
    public sealed class SplitResult
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
        public int Count { get; init; }
        public double Base { get; init; }
        public IReadOnlyList<CutPiece> Pieces { get; init; } = Array.Empty<CutPiece>();
        public double TotalLength { get; init; }
    }

    /// <summary>
    /// Статический сервис расчёта раскроя козырька и отлива по ТЗ.
    /// Чистая функция без UI, без БД и без внешних зависимостей.
    /// </summary>
    public static class CanopySplitter
    {
        public const int DefaultOverlap = 50;     // стыковой нахлёст на деталь (суммарный стык = 100 мм)
        public const int DefaultMaxPiece = 2000;  // максимальная длина готового куска (мм)
        public const int DefaultRoundStep = 10;   // округление вверх до 10 мм; 0 = без округления (до 1 мм)
        public const int MaxPieces = 200;         // предохранитель от бесконечного цикла

        /// <summary>
        /// Округление вверх до кратного шага (step). Если step <= 0 — до целого миллиметра.
        /// </summary>
        public static double RoundUp(double value, int step)
        {
            if (step > 0)
                return Math.Ceiling(value / step) * step;
            return Math.Ceiling(value);
        }

        /// <summary>
        /// Рассчитывает оптимальный раскрой стороны рамы на куски с нахлёстами.
        /// </summary>
        /// <param name="L">Длина стороны рамы, мм (> 0).</param>
        /// <param name="W">Ширина материала / глубина изделия, мм (> 0).</param>
        /// <param name="cornerStart">Есть ли угол в начале стороны.</param>
        /// <param name="cornerEnd">Есть ли угол в конце стороны.</param>
        /// <param name="roundStep">Шаг округления базы вверх (по умолчанию 10 мм).</param>
        /// <param name="maxPiece">Максимальная длина готового куска (по умолчанию 2000 мм).</param>
        /// <param name="overlap">Нахлёст на стыке с каждой стороны (по умолчанию 50 мм).</param>
        /// <returns>Результат раскроя SplitResult с коллекцией Pieces или сообщением Error.</returns>
        public static SplitResult CalcSplit(
            double L,
            double W,
            bool cornerStart,
            bool cornerEnd,
            int roundStep = DefaultRoundStep,
            double maxPiece = DefaultMaxPiece,
            double overlap = DefaultOverlap)
        {
            // ── Валидация входных данных по разделу 7 ТЗ ──
            if (L <= 0)
            {
                return new SplitResult
                {
                    Success = false,
                    Error = "Длина должна быть больше нуля"
                };
            }

            if (W <= 0)
            {
                return new SplitResult
                {
                    Success = false,
                    Error = "Ширина материала должна быть больше нуля"
                };
            }

            if (W > maxPiece)
            {
                return new SplitResult
                {
                    Success = false,
                    Error = "Ширина материала превышает максимальную длину куска"
                };
            }

            if (W + overlap > maxPiece && (cornerStart || cornerEnd))
            {
                return new SplitResult
                {
                    Success = false,
                    Error = "Раскрой невозможен: материал + нахлёст на угол больше 2000 мм"
                };
            }

            // ── Алгоритм подбора n снизу вверх (раздел 5 ТЗ) ──
            for (int n = 1; n <= MaxPieces; n++)
            {
                double nominal = RoundUp(L / n, roundStep);
                var pieces = new List<CutPiece>(n);
                bool fitsLimit = true;

                for (int i = 0; i < n; i++)
                {
                    double jointLeft = (n > 1 && i > 0) ? overlap : 0;
                    double jointRight = (n > 1 && i < n - 1) ? overlap : 0;
                    double cStart = (i == 0 && cornerStart) ? W : 0;
                    double cEnd = (i == n - 1 && cornerEnd) ? W : 0;

                    double len = nominal + jointLeft + jointRight + cStart + cEnd;
                    if (len > maxPiece)
                    {
                        fitsLimit = false;
                    }

                    pieces.Add(new CutPiece
                    {
                        Index = i,
                        Nominal = nominal,
                        JointLeft = jointLeft,
                        JointRight = jointRight,
                        CornerStart = cStart,
                        CornerEnd = cEnd,
                        Length = len
                    });
                }

                if (fitsLimit)
                {
                    return new SplitResult
                    {
                        Success = true,
                        Count = n,
                        Base = nominal,
                        Pieces = pieces,
                        TotalLength = pieces.Sum(p => p.Length)
                    };
                }
            }

            return new SplitResult
            {
                Success = false,
                Error = "Не удалось подобрать раскрой"
            };
        }
    }
}
