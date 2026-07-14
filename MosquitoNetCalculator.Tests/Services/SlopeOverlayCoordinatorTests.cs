using System.Collections.ObjectModel;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Unit tests for <see cref="SlopeOverlayCoordinator.FindPairedLaborItem"/>.
    /// This is a pure static method that finds the paired "Работа за откос"
    /// row for a given "Откос" material item, using an ordinal-based lookup
    /// that is resilient to row sorting and deletion.
    ///
    /// No WPF/STA required — the method operates on plain data structures.
    /// </summary>
    public class SlopeOverlayCoordinatorTests
    {
        /// <summary>
        /// Creates a minimal ObservableCollection with paired slope/labor rows.
        /// </summary>
        private static ObservableCollection<OrderItem> CreateItemsWithSlopes(int slopeCount)
        {
            var items = new ObservableCollection<OrderItem>();
            for (int i = 0; i < slopeCount; i++)
            {
                items.Add(new OrderItem { Name = "Откос", Price = 1000 });
                items.Add(new OrderItem { Name = "Работа за откос", Price = 600 });
            }
            return items;
        }

        [Fact]
        public void FindPairedLaborItem_ImmediateNeighbour_ReturnsNextRow()
        {
            var items = CreateItemsWithSlopes(1);
            var slopeItem = items[0];

            var labor = SlopeOverlayCoordinator.FindPairedLaborItem(items, slopeItem);

            Assert.NotNull(labor);
            Assert.Equal("Работа за откос", labor!.Name);
        }

        [Fact]
        public void FindPairedLaborItem_MultipleSlopes_FindsCorrectPair()
        {
            var items = CreateItemsWithSlopes(3);
            var secondSlope = items[2]; // index 2 = second "Откос"

            var labor = SlopeOverlayCoordinator.FindPairedLaborItem(items, secondSlope);

            Assert.NotNull(labor);
            Assert.Same(items[3], labor); // index 3 = second "Работа за откос"
        }

        [Fact]
        public void FindPairedLaborItem_LaborNotImmediateNeighbour_UsesOrdinalLookup()
        {
            // Simulate a re-sorted collection: Откос, Откос, Работа за откос, Работа за откос
            var items = new ObservableCollection<OrderItem>
            {
                new() { Name = "Откос", Price = 1000 },
                new() { Name = "Откос", Price = 1200 },
                new() { Name = "Работа за откос", Price = 600 },
                new() { Name = "Работа за откос", Price = 600 },
            };

            var firstSlope = items[0];
            var labor = SlopeOverlayCoordinator.FindPairedLaborItem(items, firstSlope);

            Assert.NotNull(labor);
            Assert.Same(items[2], labor); // first labor by ordinal
        }

        [Fact]
        public void FindPairedLaborItem_ItemNotInCollection_ReturnsNull()
        {
            var items = CreateItemsWithSlopes(1);
            var orphanItem = new OrderItem { Name = "Откос", Price = 999 };

            var labor = SlopeOverlayCoordinator.FindPairedLaborItem(items, orphanItem);

            Assert.Null(labor);
        }

        [Fact]
        public void FindPairedLaborItem_NoLaborItem_ReturnsNull()
        {
            var items = new ObservableCollection<OrderItem>
            {
                new() { Name = "Откос", Price = 1000 },
            };

            var labor = SlopeOverlayCoordinator.FindPairedLaborItem(items, items[0]);

            Assert.Null(labor);
        }

        [Fact]
        public void FindPairedLaborItem_DeletedLaborRow_OrdinalFallbackReturnsNextAvailable()
        {
            // Three slopes, but the second labor row was deleted
            var items = new ObservableCollection<OrderItem>
            {
                new() { Name = "Откос", Price = 1000 },
                new() { Name = "Работа за откос", Price = 600 },
                new() { Name = "Откос", Price = 1200 },
                // second "Работа за откос" was deleted
                new() { Name = "Откос", Price = 800 },
                new() { Name = "Работа за откос", Price = 600 },
            };

            var secondSlope = items[2]; // second "Откос"
            var labor = SlopeOverlayCoordinator.FindPairedLaborItem(items, secondSlope);

            // Ordinal lookup: 2nd slope → Skip(1) → returns the remaining labor item
            Assert.NotNull(labor);
            Assert.Equal("Работа за откос", labor!.Name);
        }
    }
}
