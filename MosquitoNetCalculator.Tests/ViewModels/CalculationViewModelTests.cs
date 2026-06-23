using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.ViewModels;
using Xunit;

namespace MosquitoNetCalculator.Tests.ViewModels
{
    public class CalculationViewModelTests
    {
        private readonly CalculationViewModel _vm = new();

        // ─── AddItem tests ───────────────────────────────────

        [Fact]
        public void AddItem_AddsToCollection()
        {
            _vm.AddItem("Anwis", "Белый", 1000, 1000, 1, 1800);
            Assert.Single(_vm.OrderItems);
        }

        [Fact]
        public void AddItem_SetsCorrectProperties()
        {
            // AddItem applies Anwis calc: ББ60 default → W+2, H-30
            var item = _vm.AddItem("Anwis", "Белый", 1000, 2000, 2, 1800);
            Assert.NotNull(item);
            Assert.Equal("Anwis", item!.Name);
            Assert.Equal("Белый", item.Color);
            Assert.Equal(1002, item.Width);   // 1000 + 2 (ББ60)
            Assert.Equal(1970, item.Height);  // 2000 - 30 (ББ60)
            Assert.Equal(2, item.Quantity);
            Assert.Equal(1800, item.Price);
        }

        [Fact]
        public void AddItem_SetsRowNumber_Sequentially()
        {
            _vm.AddItem("Anwis", "Белый", 1000, 1000, 1, 1800);
            _vm.AddItem("Отлив", "Коричневый", 1000, 500, 1, 2150);
            Assert.Equal(1, _vm.OrderItems[0].RowNumber);
            Assert.Equal(2, _vm.OrderItems[1].RowNumber);
        }

        [Fact]
        public void AddItem_MinQuantityIsOne()
        {
            var item = _vm.AddItem("Anwis", "Белый", 1000, 1000, 0, 1800);
            Assert.Equal(1, item!.Quantity);
        }

        [Fact]
        public void AddItem_NegativeQuantity_BecomesOne()
        {
            var item = _vm.AddItem("Anwis", "Белый", 1000, 1000, -5, 1800);
            Assert.Equal(1, item!.Quantity);
        }

        [Fact]
        public void AddItem_SetsInstallationMode_ForOtliv()
        {
            var item = _vm.AddItem("Отлив", "Белый", 1000, 500, 1, 2150);
            Assert.Equal(1, item!.InstallationMode);
        }

        [Fact]
        public void AddItem_SetsInstallationMode0_ForAnwis()
        {
            var item = _vm.AddItem("Anwis", "Белый", 1000, 1000, 1, 1800);
            Assert.Equal(0, item!.InstallationMode);
        }

        // ─── DeleteItem tests ────────────────────────────────

        [Fact]
        public void DeleteItem_RemovesAndRenumbers()
        {
            var item1 = _vm.AddItem("Anwis", "Белый", 1000, 1000, 1, 1800);
            var item2 = _vm.AddItem("Отлив", "Белый", 1000, 500, 1, 2150);
            var item3 = _vm.AddItem("Козырёк", "Белый", 1000, 500, 1, 2150);

            _vm.DeleteItem(item2!);
            Assert.Equal(2, _vm.OrderItems.Count);
            Assert.Equal(1, _vm.OrderItems[0].RowNumber);
            Assert.Equal(2, _vm.OrderItems[1].RowNumber);
        }

        [Fact]
        public void DeleteItem_LastItem()
        {
            var item = _vm.AddItem("Anwis", "Белый", 1000, 1000, 1, 1800);
            _vm.DeleteItem(item!);
            Assert.Empty(_vm.OrderItems);
        }

        // ─── ClearAll tests ──────────────────────────────────

        [Fact]
        public void ClearAll_RemovesAllItems()
        {
            _vm.AddItem("Anwis", "Белый", 1000, 1000, 1, 1800);
            _vm.AddItem("Отлив", "Белый", 1000, 500, 1, 2150);
            _vm.ClearAll();
            Assert.Empty(_vm.OrderItems);
        }

        // ─── RenumberRows tests ──────────────────────────────

        [Fact]
        public void RenumberRows_UpdatesAllRowNumbers()
        {
            _vm.AddItem("Anwis", "Белый", 1000, 1000, 1, 1800);
            _vm.AddItem("Отлив", "Белый", 1000, 500, 1, 2150);
            _vm.AddItem("Козырёк", "Белый", 1000, 500, 1, 2150);
            _vm.RenumberRows();

            for (int i = 0; i < _vm.OrderItems.Count; i++)
                Assert.Equal(i + 1, _vm.OrderItems[i].RowNumber);
        }

        // ─── CalculateTotal tests ────────────────────────────

        [Fact]
        public void CalculateTotal_SumsActiveItems()
        {
            _vm.AddItem("Anwis", "Белый", 1000, 1000, 1, 1800); // 0.972 * 1800 = 1749.6 (ББ60 default)
            _vm.AddItem("Отлив", "Белый", 1000, 500, 1, 2150);  // 0.5 * 2150 = 1075
            var total = _vm.CalculateTotal(0);
            Assert.Equal(2824.6, total.Total, 2);
            Assert.Equal(2, total.Count);
        }

        [Fact]
        public void CalculateTotal_ExcludesInactiveItems()
        {
            var item1 = _vm.AddItem("Anwis", "Белый", 1000, 1000, 1, 1800);
            _vm.AddItem("Отлив", "Белый", 1000, 500, 1, 2150);
            item1!.IsActive = false;
            var total = _vm.CalculateTotal(0);
            Assert.Equal(1, total.Count);
        }

        [Fact]
        public void CalculateTotal_AddsAdditionalKpTotal()
        {
            _vm.AddItem("Anwis", "Белый", 1000, 1000, 1, 1800);
            var total = _vm.CalculateTotal(500);
            Assert.Equal(2249.6, total.Total, 2);
        }

        [Fact]
        public void CalculateTotal_CalculatesArea()
        {
            _vm.AddItem("Anwis", "Белый", 1000, 1000, 1, 1800); // 0.972 м² (ББ60 default)
            _vm.AddItem("Отлив", "Белый", 1000, 500, 1, 2150);  // 0.5 м²
            var total = _vm.CalculateTotal(0);
            Assert.Equal(1.472, total.TotalArea, 3);
        }

        [Fact]
        public void CalculateTotal_CalculatesLinear()
        {
            _vm.AddItem("ПСУЛ", "", 1000, 2000, 1, 100); // (1000+2000)*2/1000 = 6.0 м.п.
            var total = _vm.CalculateTotal(0);
            Assert.Equal(6.0, total.TotalLinear, 3);
        }

        [Fact]
        public void CalculateTotal_CalculatesPieces()
        {
            _vm.AddItem("Работа", "", 0, 0, 1, 5000);
            _vm.AddItem("Доставка", "", 0, 0, 1, 1000);
            var total = _vm.CalculateTotal(0);
            Assert.Equal(2, total.TotalPieces);
        }

        [Fact]
        public void CalculateTotal_TotalPieces_MultipliedByQuantity()
        {
            // Bug #2 fix: TotalPieces = Sum(Quantity), not Count(rows).
            _vm.AddItem("Работа", "", 0, 0, 3, 5000);
            _vm.AddItem("Доставка", "", 0, 0, 2, 1000);
            var total = _vm.CalculateTotal(0);
            Assert.Equal(5, total.TotalPieces);
        }

        [Fact]
        public void CalculateTotal_TotalArea_MultipliedByQuantity()
        {
            // Bug #2 fix: TotalArea = Sum(CalculatedValue * Quantity), not Sum(CalculatedValue).
            _vm.AddItem("Anwis", "Белый", 1000, 1000, 3, 1800);
            var total = _vm.CalculateTotal(0);
            // Anwis ББ60 default: calcW=1002, calcH=970, area=0.972, ×3 = 2.916
            Assert.Equal(2.916, total.TotalArea, 3);
        }

        [Fact]
        public void CalculateTotal_TotalLinear_MultipliedByQuantity()
        {
            // Bug #2 fix: TotalLinear = Sum(CalculatedValue * Quantity) for perimeter products.
            _vm.AddItem("ПСУЛ", "", 1000, 2000, 2, 100);
            var total = _vm.CalculateTotal(0);
            // (1000+2000)*2/1000 = 6.0 м.п. × 2 = 12.0
            Assert.Equal(12.0, total.TotalLinear, 3);
        }

        [Fact]
        public void CalculateTotal_HandlesEmptyItems()
        {
            var total = _vm.CalculateTotal(0);
            Assert.Equal(0, total.Total);
            Assert.Equal(0, total.Count);
            Assert.Equal(0, total.TotalArea);
            Assert.Equal(0, total.TotalLinear);
            Assert.Equal(0, total.TotalPieces);
        }

        [Fact]
        public void CalculateTotal_ExcludesItemsWithZeroTotal()
        {
            _vm.AddItem("", "", 0, 0, 1, 0);
            var total = _vm.CalculateTotal(0);
            Assert.Equal(0, total.Count);
        }

        // ─── SnapshotItems / RestoreFromSnapshot tests ───────

        [Fact]
        public void SnapshotItems_CreatesDeepCopy()
        {
            _vm.AddItem("Anwis", "Белый", 1000, 1000, 1, 1800);
            var snapshot = _vm.SnapshotItems();
            Assert.Single(snapshot);
            Assert.Equal("Anwis", snapshot[0].Name);
            Assert.Equal(1800, snapshot[0].Price);
        }

        [Fact]
        public void SnapshotItems_CapturesCurrentState()
        {
            _vm.AddItem("Anwis", "Белый", 1000, 1000, 1, 1800);
            var snapshot = _vm.SnapshotItems();

            // Modify after snapshot
            _vm.AddItem("Отлив", "Белый", 1000, 500, 1, 2150);

            // Snapshot should still have 1 item
            Assert.Single(snapshot);
        }

        [Fact]
        public void RestoreFromSnapshot_RestoresItems()
        {
            _vm.AddItem("Anwis", "Белый", 1000, 1000, 1, 1800);
            var snapshot = _vm.SnapshotItems();

            _vm.ClearAll();
            Assert.Empty(_vm.OrderItems);

            _vm.RestoreFromSnapshot(snapshot, () => { });
            Assert.Single(_vm.OrderItems);
            Assert.Equal("Anwis", _vm.OrderItems[0].Name);
            Assert.Equal(1800, _vm.OrderItems[0].Price);
        }

        [Fact]
        public void RestoreFromSnapshot_RenumbersCorrectly()
        {
            _vm.AddItem("Anwis", "Белый", 1000, 1000, 1, 1800);
            _vm.AddItem("Отлив", "Белый", 1000, 500, 1, 2150);
            var snapshot = _vm.SnapshotItems();

            _vm.ClearAll();
            _vm.RestoreFromSnapshot(snapshot, () => { });

            Assert.Equal(1, _vm.OrderItems[0].RowNumber);
            Assert.Equal(2, _vm.OrderItems[1].RowNumber);
        }

        [Fact]
        public void UnsubscribeAll_RemovesCallbacks()
        {
            int callCount = 0;
            void callback() => callCount++;

            _vm.AddItem("Anwis", "Белый", 1000, 1000, 1, 1800);
            // Subscribe
            _vm.OrderItems[0].RecalculateRequested += callback;

            _vm.UnsubscribeAll(callback);

            // Trigger recalculate — should not call callback
            _vm.OrderItems[0].Width = 2000;
            Assert.Equal(0, callCount);
        }

        // ─── LoadFromOrderData tests ─────────────────────────

        [Fact]
        public void LoadFromOrderData_LoadsItems()
        {
            var order = new OrderData
            {
                Items = new System.Collections.Generic.List<OrderItemData>
                {
                    new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800 },
                    new() { Name = "Отлив", Color = "Коричневый", Width = 1500, Height = 100, Quantity = 2, Price = 2150 }
                }
            };

            _vm.LoadFromOrderData(order, () => { });
            Assert.Equal(2, _vm.OrderItems.Count);
            Assert.Equal("Anwis", _vm.OrderItems[0].Name);
            Assert.Equal("Отлив", _vm.OrderItems[1].Name);
        }

        [Fact]
        public void LoadFromOrderData_SetsRowNumbers()
        {
            var order = new OrderData
            {
                Items = new System.Collections.Generic.List<OrderItemData>
                {
                    new() { Name = "Anwis" },
                    new() { Name = "Отлив" },
                    new() { Name = "Козырёк" }
                }
            };

            _vm.LoadFromOrderData(order, () => { });
            Assert.Equal(1, _vm.OrderItems[0].RowNumber);
            Assert.Equal(2, _vm.OrderItems[1].RowNumber);
            Assert.Equal(3, _vm.OrderItems[2].RowNumber);
        }

        [Fact]
        public void LoadFromOrderData_PreservesIsActive()
        {
            var order = new OrderData
            {
                Items = new System.Collections.Generic.List<OrderItemData>
                {
                    new() { Name = "Anwis", IsActive = false }
                }
            };

            _vm.LoadFromOrderData(order, () => { });
            Assert.False(_vm.OrderItems[0].IsActive);
        }

        // ─── Bug #4 fix regression: consumer-path test ───────────
        // v3.22.0 removed `CalculatedValue` and `Total` from
        // OrderItemData because they were dead bytes — never read on
        // load. This test asserts the actual consumer
        // (CalculationViewModel.LoadFromOrderData) recomputes them
        // correctly from W × H × Price × Quantity via OrderItem.Recalculate.
        // Without this, the DTO removal could silently produce a 0-total
        // item on round-trip if a future refactor breaks the recompute
        // chain.
        //
        // Consolidated into a single Theory per the v3.22.0 review: the
        // two original Facts differed only in product type. A Theory
        // covers both area-based (Anwis) and perimeter-based (ПСУЛ)
        // recompute paths in one test.

        [Theory]
        [InlineData("Anwis", 1000, 1500, 2, 1800, 1.5, 5400)]   // LoadFromOrderData uses stored values directly: W*H/1M = 1.5 м²
        [InlineData("ПСУЛ", 1500, 2000, 3, 100, 7.0, 2100)]     // (W+H)*2/1000 = 7.0 м.п., * P * Q = 2100
        public void LoadFromOrderData_RecomputesCalculatedValueAndTotal_FromDtoFields(
            string name, int width, int height, int qty, double price,
            double expectedCalc, double expectedTotal)
        {
            // The DTO no longer CalculatedValue/Total, so we only
            // set the inputs (W, H, Price, Quantity). The consumer must
            // recompute the rest via OrderItem.Recalculate.
            var order = new OrderData
            {
                Items = new System.Collections.Generic.List<OrderItemData>
                {
                    new() { Name = name, Width = width, Height = height, Quantity = qty, Price = price }
                }
            };

            _vm.LoadFromOrderData(order, () => { });
            var item = _vm.OrderItems[0];

            Assert.Equal(expectedCalc, item.CalculatedValue, 3);
            Assert.Equal(expectedTotal, item.Total, 2);
        }

        // ─── AnwisSizeMode persistence (v3.29.2 regression) ─────
        // Two coupled bugs were found:
        //   1. ActionBarControl.BtnSaveOrder_Click was dropping AnwisSizeMode
        //      from the OrderItemData mapper, so saved orders silently reset
        //      to ББ 60 on reopen.
        //   2. OrderItem.AnwisSizeMode setter performs a reverse/apply from
        //      OLD mode when called, which during LoadFromOrderData would
        //      corrupt stored Width/Height by running them through ББ 60's
        //      reverse formula even if the saved mode was non-default.
        // These tests pin both invariants: the loaded mode matches the saved
        // mode, and stored dimensions survive the load untouched.

        [Theory]
        [InlineData(AnwisSizeMode.Брусбокс60, 1002,  970)]  // raw 1000×1000 + ББ60 = +2 / −30
        [InlineData(AnwisSizeMode.Брусбокс70,  998,  970)]  // raw 1000×1000 + ББ70 = −2 / −30
        [InlineData(AnwisSizeMode.Профипласт, 1000, 1000)]  // identity
        [InlineData(AnwisSizeMode.РазмерПроёма, 1020, 1020)] // raw 1000×1000 + Проём = +20 / +20
        [InlineData(AnwisSizeMode.Габаритный, 1000, 1000)]  // identity
        public void LoadFromOrderData_PreservesSavedAnwisSizeMode_AndDimensions(
            AnwisSizeMode saveMode, double expectedWidth, double expectedHeight)
        {
            var order = new OrderData
            {
                Items = new System.Collections.Generic.List<OrderItemData>
                {
                    new()
                    {
                        Name = "Anwis", Color = "Белый",
                        Width = expectedWidth, Height = expectedHeight,
                        Quantity = 1, Price = 1800,
                        AnwisSizeMode = (int)saveMode
                    }
                }
            };

            _vm.LoadFromOrderData(order, () => { });

            var item = _vm.OrderItems[0];
            Assert.Equal(saveMode, item.AnwisSizeMode);
            Assert.Equal(expectedWidth, item.Width);
            Assert.Equal(expectedHeight, item.Height);
        }

        [Theory]
        [InlineData(AnwisSizeMode.Брусбокс60)]   // Defaults also need to round-trip
        [InlineData(AnwisSizeMode.Брусбокс70)]
        [InlineData(AnwisSizeMode.Профипласт)]
        [InlineData(AnwisSizeMode.РазмерПроёма)]
        [InlineData(AnwisSizeMode.Габаритный)]
        public void Clone_PreservesAnwisSizeMode_AndDimensions(AnwisSizeMode mode)
        {
            // Build a source item with known stored values for the given mode
            var source = new OrderItem
            {
                Name = "Anwis", Color = "Белый",
                // Use values that would survive a buggy reverse-apply (i.e.
                // values that ARE stored for ББ 60 by chance).
                Width = 1002, Height = 970,
                Quantity = 1, Price = 1800
            };
            source.SetAnwisModeQuiet(mode);

            var clone = source.Clone();
            Assert.Equal(mode, clone.AnwisSizeMode);
            Assert.Equal(1002, clone.Width);
            Assert.Equal(970, clone.Height);
        }

        [Fact]
        public void AddItem_WithNonDefaultMode_DoesNotCorruptDimensions()
        {
            // Before the fix, AddItem with mode=ББ 70 stored Width=998, then the
            // setter reverse-applied through default ББ 60, producing Width=994.
            // After the fix, the stored Width should equal GetCalcWidth(raw, mode).
            var item = _vm.AddItem("Anwis", "Белый", 1000, 1000, 1, 1800,
                anwisMode: AnwisSizeMode.Брусбокс70)!;

            Assert.Equal(AnwisSizeMode.Брусбокс70, item.AnwisSizeMode);
            Assert.Equal(998, item.Width);   // 1000 − 2 (ББ70 stored formula)
            Assert.Equal(970, item.Height);  // 1000 − 30 (ББ70/ББ60 share H formula)
        }

        // ─── v3.35.0: non-Anwis AddItem regression tests ─────────

        [Theory]
        [InlineData("Откос материал")]
        [InlineData("Работа")]
        [InlineData("Пояс")]
        [InlineData("Брус")]
        [InlineData("Доставка")]
        [InlineData("Отлив")]
        [InlineData("ПСУЛ")]
        [InlineData("Уплотнение")]
        [InlineData("Козырёк")]
        [InlineData("Короб")]
        public void AddItem_NonAnwis_StoresWidthHeightAsIs(string productName)
        {
            // v3.35.0 regression: Anwis calc formulas (W+2, H−30 for ББ60)
            // must NOT leak into non-Anwis products. Width/Height are stored
            // exactly as passed, no adjustment.
            var item = _vm.AddItem(productName, "", 250, 100, 1, 500,
                anwisMode: AnwisSizeMode.Брусбокс60)!;

            Assert.Equal(productName, item.Name);
            Assert.Equal(250, item.Width);
            Assert.Equal(100, item.Height);
            Assert.Equal(AnwisSizeMode.Брусбокс60, item.AnwisSizeMode);
            Assert.False(item.IsAnwis);
        }

        [Fact]
        public void AddItem_NonAnwis_NonDefaultMode_StillIdentity()
        {
            // Even with a non-default Anwis mode (ББ70), non-Anwis
            // products must store dimensions as-is.
            var item = _vm.AddItem("Откос материал", "", 250, 100, 1, 500,
                anwisMode: AnwisSizeMode.Брусбокс70)!;

            Assert.Equal(250, item.Width);
            Assert.Equal(100, item.Height);
            Assert.Equal(AnwisSizeMode.Брусбокс70, item.AnwisSizeMode);
        }

        [Theory]
        [InlineData("Откос материал")]
        [InlineData("Работа")]
        [InlineData("Пояс")]
        public void AddItem_NonAnwis_ZeroHeight_StaysZero(string productName)
        {
            // The original 30mm bug: non-Anwis with Height=0 would
            // show 30 via ВысотаВвод. This test verifies the full
            // add pipeline stores and displays Height=0 correctly.
            var item = _vm.AddItem(productName, "", 250, 0, 1, 500)!;

            Assert.Equal(0, item.Height);
            Assert.Equal(0, item.ВысотаВвод);
        }
    }
}
