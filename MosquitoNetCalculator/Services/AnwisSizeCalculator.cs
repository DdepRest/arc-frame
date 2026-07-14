using System;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Pure calculation functions for Anwis size conversions.
    /// Extracted from <see cref="AnwisSize"/> during Phase 5 refactoring so the
    /// value object (AnwisSize) delegates to a dedicated calculator.
    /// </summary>
    public static class AnwisSizeCalculator
    {
        /// <summary>
        /// Applies the mode-specific calculation adjustment to the raw width.
        /// </summary>
        public static double ApplyCalcWidth(double rawW, AnwisSizeMode mode) => mode switch
        {
            AnwisSizeMode.Брусбокс60 => rawW + 2,
            AnwisSizeMode.Брусбокс70 => Math.Max(0, rawW - 2),
            AnwisSizeMode.РазмерПроёма => rawW + 20,
            _ => rawW // Профипласт, Габаритный, не-Anwis — identity
        };

        /// <summary>
        /// Applies the mode-specific calculation adjustment to the raw height.
        /// </summary>
        public static double ApplyCalcHeight(double rawH, AnwisSizeMode mode) => mode switch
        {
            AnwisSizeMode.Брусбокс60 => Math.Max(0, rawH - 30),
            AnwisSizeMode.Брусбокс70 => Math.Max(0, rawH - 30),
            AnwisSizeMode.РазмерПроёма => rawH + 20,
            _ => rawH // Профипласт, Габаритный, не-Anwis — identity
        };

        /// <summary>
        /// Reverses the mode-specific calculation adjustment from the stored calc width.
        /// </summary>
        public static double ReverseCalcWidth(double calcW, AnwisSizeMode mode) => mode switch
        {
            AnwisSizeMode.Брусбокс60 => calcW - 2,
            AnwisSizeMode.Брусбокс70 => calcW + 2,
            AnwisSizeMode.РазмерПроёма => calcW - 20,
            _ => calcW // Профипласт, Габаритный, не-Anwis — identity
        };

        /// <summary>
        /// Reverses the mode-specific calculation adjustment from the stored calc height.
        /// </summary>
        public static double ReverseCalcHeight(double calcH, AnwisSizeMode mode) => mode switch
        {
            AnwisSizeMode.Брусбокс60 => calcH + 30,
            AnwisSizeMode.Брусбокс70 => calcH + 30,
            AnwisSizeMode.РазмерПроёма => calcH - 20,
            _ => calcH // Профипласт, Габаритный, не-Anwis — identity
        };
    }
}
