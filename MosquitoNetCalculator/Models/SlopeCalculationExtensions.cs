namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// Extension methods and helpers for <see cref="SlopeCalculation"/>.
    /// Extracted from <see cref="OrderItem"/> during Phase 5 refactoring.
    /// </summary>
    public static class SlopeCalculationExtensions
    {
        /// <summary>
        /// Creates a deep copy of a <see cref="SlopeCalculation"/>.
        /// Isolates the cloned calculation from the original so that manual edits
        /// in the slope panel do not leak back into the source instance.
        /// </summary>
        /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="src"/> is null.</exception>
        public static SlopeCalculation DeepClone(this SlopeCalculation src)
        {
            if (src == null)
                throw new System.ArgumentNullException(nameof(src));

            var dst = new SlopeCalculation
            {
                WidthMm = src.WidthMm,
                HeightMm = src.HeightMm,
                DepthM = src.DepthM,
                WindowCount = src.WindowCount,
                IsManualOverride = src.IsManualOverride,
                IsProfileEconomyApplied = src.IsProfileEconomyApplied,
            };

            CopyMaterial(src.Sandwich, dst.Sandwich);
            CopyMaterial(src.Foam, dst.Foam);
            CopyMaterial(src.Sealant, dst.Sealant);
            CopyMaterial(src.Tape, dst.Tape);
            CopyMaterial(src.StartProfile, dst.StartProfile);
            CopyMaterial(src.FProfile, dst.FProfile);
            CopyMaterial(src.Penoplex, dst.Penoplex);
            CopyMaterial(src.Laminatina, dst.Laminatina);
            CopyMaterial(src.Labor, dst.Labor);
            CopyMaterial(src.LaminatinaLabor, dst.LaminatinaLabor);

            return dst;
        }

        private static void CopyMaterial(SlopeMaterial source, SlopeMaterial destination)
        {
            destination.Name = source.Name;
            destination.Quantity = source.Quantity;
            destination.Price = source.Price;
            destination.Unit = source.Unit;
            destination.IsQuantityOverridden = source.IsQuantityOverridden;
        }
    }
}
