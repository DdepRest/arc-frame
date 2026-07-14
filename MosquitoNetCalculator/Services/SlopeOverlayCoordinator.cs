using System;
using System.Linq;
using System.Windows.Controls;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.ViewModels;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Coordinates the slope-overlay lifecycle: showing the SlopePanelControl
    /// for new or existing slope items, loading prices, finding the paired
    /// "Работа за откос" row, and closing the overlay with animation.
    ///
    /// Extracted from <see cref="MainWindow"/> as part of Phase 1 refactoring
    /// (REFACTORING_PLAN.md §3.2 — SlopeOverlayCoordinator component).
    ///
    /// Business logic (SlopeCalculatorService formulas, price defaults) is NOT
    /// touched — this class only orchestrates UI + data loading.
    /// </summary>
    public sealed class SlopeOverlayCoordinator
    {
        private readonly SlopePanelControl _slopePanel;
        private readonly Func<PricesViewModel> _getPricesVM;
        private readonly Func<CalculationViewModel> _getCalcVM;
        private readonly OverlayManager _overlayManager;
        private readonly Action<string> _onSetActiveNav;

        /// <summary>
        /// The slope overlay entry shared with <see cref="OverlayManager"/>.
        /// Created externally and passed to both OverlayManager and this coordinator
        /// to ensure a single consistent reference.
        /// </summary>
        public OverlayManager.OverlayEntry Entry { get; }

        /// <summary>
        /// Creates a SlopeOverlayCoordinator.
        /// </summary>
        /// <param name="slopePanel">The SlopePanelControl hosted inside the overlay.</param>
        /// <param name="entry">The slope overlay entry (shared with OverlayManager).</param>
        /// <param name="getPricesVM">Provides access to PricesViewModel for price loading.</param>
        /// <param name="getCalcVM">Provides access to CalculationViewModel for order items.</param>
        /// <param name="overlayManager">The OverlayManager for hiding other overlays.</param>
        /// <param name="onSetActiveNav">Callback to set active nav button.</param>
        public SlopeOverlayCoordinator(
            SlopePanelControl slopePanel,
            OverlayManager.OverlayEntry entry,
            Func<PricesViewModel> getPricesVM,
            Func<CalculationViewModel> getCalcVM,
            OverlayManager overlayManager,
            Action<string> onSetActiveNav)
        {
            _slopePanel = slopePanel;
            _getPricesVM = getPricesVM;
            _getCalcVM = getCalcVM;
            _overlayManager = overlayManager;
            _onSetActiveNav = onSetActiveNav;
            Entry = entry;
        }

        // ── Show (new slope) ─────────────────────────────────────────

        /// <summary>
        /// Opens the slope calculator panel for creating a new slope.
        /// Loads current prices from PriceService, resets the panel, and
        /// shows the overlay with animation.
        /// </summary>
        public void Show()
        {
            int totalFromOrder = _getCalcVM().OrderItems
                .Where(i => i.SlopeData != null)
                .Sum(i => i.SlopeData!.WindowCount);

            LoadSlopePrices(totalFromOrder);
            _slopePanel.Reset();

            // Show() already hides all other visible overlays internally
            _overlayManager.Show(Entry);
        }

        // ── Edit (existing slope row) ────────────────────────────────

        /// <summary>
        /// Opens the slope calculator in edit mode for an existing "Откос" row.
        /// Finds the paired "Работа за откос" row using a robust ordinal-based
        /// lookup (resilient to sorting/deletion).
        /// </summary>
        /// <param name="materialItem">The "Откос" OrderItem to edit.</param>
        /// <returns>true if the paired labor item was found and the overlay opened; false if not found.</returns>
        public bool Edit(OrderItem materialItem)
        {
            // HideInstant — not Close — because Close starts a 220ms animation
            // with a Completed callback that sets Visibility=Collapsed. If we then
            // immediately Show the overlay, the old callback would collapse the
            // freshly opened overlay 220ms later.
            // HideInstant cancels all animations and instantly hides without callbacks.
            OverlayManager.HideInstant(Entry.Grid);

            var items = _getCalcVM().OrderItems;
            var laborItem = FindPairedLaborItem(items, materialItem);
            if (laborItem == null) return false;

            int totalFromOtherSlopes = items
                .Where(i => i.SlopeData != null && i != materialItem)
                .Sum(i => i.SlopeData!.WindowCount);

            LoadSlopePrices(totalFromOtherSlopes);
            _slopePanel.LoadForEdit(materialItem, laborItem);

            // Show() already hides all other visible overlays internally
            _overlayManager.Show(Entry);
            return true;
        }

        // ── Close ────────────────────────────────────────────────────

        /// <summary>
        /// Animates the slope overlay closed and resets active nav to "Calc".
        /// Called from SlopePanelControl after adding/editing a slope.
        /// Delegates to <see cref="OverlayManager.CloseSingle"/> (fallback width 480).
        /// </summary>
        public void Close()
        {
            _overlayManager.CloseSingle(Entry, fallbackWidth: 480);
        }

        // ── Helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Finds the paired "Работа за откос" row for a given "Откос" material item.
        /// First tries the immediate next row (common case), then falls back to
        /// an ordinal-based lookup that is resilient to row sorting/deletion.
        /// </summary>
        internal static OrderItem? FindPairedLaborItem(
            System.Collections.ObjectModel.ObservableCollection<OrderItem> items,
            OrderItem materialItem)
        {
            int idx = items.IndexOf(materialItem);
            if (idx < 0) return null;

            // Fast path: immediate neighbour
            if (idx + 1 < items.Count && items[idx + 1].Name == "Работа за откос")
                return items[idx + 1];

            // Fallback: ordinal-based lookup (resilient to sort/delete)
            int slopeOrdinal = items.Take(idx + 1).Count(i => i.Name == "Откос");
            return items.Where(i => i.Name == "Работа за откос")
                       .Skip(slopeOrdinal - 1)
                       .FirstOrDefault();
        }

        /// <summary>
        /// Loads slope material prices from PriceService into SlopePanelControl.
        /// Falls back to hardcoded defaults if a price entry is 0 or missing.
        /// </summary>
        internal void LoadSlopePrices(int totalWindowCount)
        {
            var prices = _getPricesVM();
            _slopePanel.TotalWindowCountInOrder = totalWindowCount;
            _slopePanel.Prices = (
                Sandwich:   prices.GetPrice("Сэндвич", "") > 0       ? prices.GetPrice("Сэндвич", "")       : 1200,
                Foam:       prices.GetPrice("Пена (откос)", "") > 0  ? prices.GetPrice("Пена (откос)", "")  : 750,
                Sealant:    prices.GetPrice("Герметик (откос)", "") > 0 ? prices.GetPrice("Герметик (откос)", "") : 350,
                Tape:       prices.GetPrice("Скотч (откос)", "") > 0 ? prices.GetPrice("Скотч (откос)", "") : 135,
                Start:      prices.GetPrice("Старт (откос)", "") > 0 ? prices.GetPrice("Старт (откос)", "") : 135,
                FProfile:   prices.GetPrice("F-планка (откос)", "") > 0 ? prices.GetPrice("F-планка (откос)", "") : 250,
                Penoplex:   prices.GetPrice("Пеноплекс (откос)", "") > 0 ? prices.GetPrice("Пеноплекс (откос)", "") : 450,
                Labor:      prices.GetPrice("Работа за откос", "") > 0 ? prices.GetPrice("Работа за откос", "") : 600
            );
        }
    }
}
