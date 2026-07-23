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

        [Fact]
        public void AddItem_SetsInstallationMode1_AndZeroDeduction_ForOtliv()
        {
            // v3.47.0: Отлив defaults to "Без монтажа" with 0 ₽ deduction.
            var item = _vm.AddItem("Отлив", "Белый", 1000, 500, 1, 2150);
            Assert.Equal(1, item!.InstallationMode);
            Assert.Equal(0, item.InstallationDeduction);
            Assert.Equal(500, item.InstallationAdjustment);
            Assert.Equal(item.Total, item.TotalWithDeduction);
        }

        [Fact]
        public void AddItem_SetsInstallationMode1_AndZeroDeduction_ForKozyrek()
        {
            // v3.47.0: Козырёк defaults to "Без монтажа" with 0 ₽ deduction.
            var item = _vm.AddItem("Козырёк", "Белый", 1000, 500, 1, 2150);
            Assert.Equal(1, item!.InstallationMode);
            Assert.Equal(0, item.InstallationDeduction);
            Assert.Equal(750, item.InstallationAdjustment);
            Assert.Equal(item.Total, item.TotalWithDeduction);
        }

        [Fact]
        public void AddItem_DecimalQuantity_Allowed()
        {
            // v3.47.0: Quantity accepts decimal values.
            var item = _vm.AddItem("Anwis", "Белый", 1000, 1000, 2.5, 1800);
            Assert.Equal(2.5, item!.Quantity);
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

        [Fact]
        public void LoadFromOrderData_PreservesIsAnticat()
        {
            var order = new OrderData
            {
                Items = new System.Collections.Generic.List<OrderItemData>
                {
                    new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 3800, IsAnticat = true }
                }
            };
            _vm.LoadFromOrderData(order, () => { });
            Assert.Single(_vm.OrderItems);
            Assert.True(_vm.OrderItems[0].IsAnticat);
            Assert.Equal("Anwis (Антикошка)", _vm.OrderItems[0].DisplayName);
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

        // ─── v3.47.3: legacy Отлив/Козырёк order migration regression ────
        // URGENT bug: pre-v3.47.0 saved JSON for Отлив/Козырёк contains DTO
        // defaults (mode=0, ded=-500, sur=-500, adj=0) because these products
        // were not installation-aware before v3.47.0. The per-linear-meter
        // formula `Total + value × linear × Quantity` then clamps
        // TotalWithDeduction to 0 when X (mode 1) or B (mode 2) is selected:
        //   For Отлив 1000×500 (linear=3 м.п., Q=1, Total=1075):
        //   Max(0, 1075 + (-500)×3×1) = Max(0, −425) = 0  ← BUG
        // The fix detects the legacy defaults pattern and replaces them with
        // v3.47.0 per-linear-meter defaults so totals stay non-zero.

        [Fact]
        public void LoadFromOrderData_Otliv_LegacyDefaults_ResetsToV347Defaults_NoZeroTotal()
        {
            var order = new OrderData
            {
                Items = new System.Collections.Generic.List<OrderItemData>
                {
                    new()
                    {
                        Name = "Отлив", Color = "Белый",
                        Width = 1000, Height = 500, Quantity = 1, Price = 2150,
                        // Pre-v3.47.0 JSON: DTO defaults inherited because Otliv
                        // was not installation-applicable.
                        InstallationMode = 0, InstallationDeduction = -500,
                        InstallationSurcharge = -500, InstallationAdjustment = 0
                    }
                }
            };

            _vm.LoadFromOrderData(order, () => { });

            var item = _vm.OrderItems[0];
            Assert.Equal("Отлив", item.Name);
            // v3.47.0 defaults applied by the migration:
            Assert.Equal(1, item.InstallationMode);          // auto-switched to "Без монтажа"
            Assert.Equal(0, item.InstallationDeduction);    // 0 ₽ (no deduction)
            Assert.Equal(500, item.InstallationSurcharge);  // 500 ₽/м.п.
            Assert.Equal(500, item.InstallationAdjustment);  // 500 ₽/м.п.
            // Total = 0.5 м² × 2150 = 1075; mode 1 → 1075 + 0×3×1 = 1075. NOT 0.
            Assert.Equal(1075, item.TotalWithDeduction, 2);
        }

        [Fact]
        public void LoadFromOrderData_Kozyrek_LegacyDefaults_ResetsToV347Defaults_NoZeroTotal()
        {
            var order = new OrderData
            {
                Items = new System.Collections.Generic.List<OrderItemData>
                {
                    new()
                    {
                        Name = "Козырёк", Color = "Белый",
                        Width = 1000, Height = 500, Quantity = 1, Price = 2150,
                        InstallationMode = 0, InstallationDeduction = -500,
                        InstallationSurcharge = -500, InstallationAdjustment = 0
                    }
                }
            };

            _vm.LoadFromOrderData(order, () => { });

            var item = _vm.OrderItems[0];
            Assert.Equal("Козырёк", item.Name);
            Assert.Equal(1, item.InstallationMode);
            Assert.Equal(0, item.InstallationDeduction);
            Assert.Equal(750, item.InstallationSurcharge);  // Козырёк: 750 ₽/м.п.
            Assert.Equal(750, item.InstallationAdjustment);
            // TotalWithDeduction (mode 1) = 1075 + 0×3×1 = 1075.
            Assert.Equal(1075, item.TotalWithDeduction, 2);
        }

        [Theory]
        [InlineData("Отлив")]
        [InlineData("Козырёк")]
        public void LoadFromOrderData_LegacyOtlivKozyrek_TotalWithDeduction_NonZero_InAllThreeModes(string product)
        {
            // Indirect regression: even if user toggles to mode 2 (B) AFTER load,
            // the per-linear-meter formula must use v3.47.0 defaults, not legacy -500.
            // Without the fix, ded=-500 × linear > Total → clamps to 0 in modes
            // 1 (X) and 2 (В) when Total is typical (1000 ish).
            var order = new OrderData
            {
                Items = new System.Collections.Generic.List<OrderItemData>
                {
                    new()
                    {
                        Name = product, Width = 1000, Height = 500, Quantity = 1, Price = 2150,
                        InstallationMode = 0, InstallationDeduction = -500,
                        InstallationSurcharge = -500, InstallationAdjustment = 0
                    }
                }
            };
            _vm.LoadFromOrderData(order, () => { });
            var item = _vm.OrderItems[0];

            // TotalWithDeduction in modes 0, 1, 2 must all be > 0 (the bug clamped them to 0).
            item.InstallationMode = 0;
            Assert.True(item.TotalWithDeduction > 0, $"mode 0 (V) for legacy {product} must produce non-zero total");
            item.InstallationMode = 1;
            Assert.True(item.TotalWithDeduction > 0, $"mode 1 (X) for legacy {product} must produce non-zero total");
            item.InstallationMode = 2;
            Assert.True(item.TotalWithDeduction > 0, $"mode 2 (В) for legacy {product} must produce non-zero total");
        }

        [Fact]
        public void LoadFromOrderData_Otliv_NewV347Order_NotAffectedByMigration()
        {
            // v3.47.0+ saved orders already carry correct per-linear-meter defaults.
            // The legacy migration must NOT override them.
            var order = new OrderData
            {
                Items = new System.Collections.Generic.List<OrderItemData>
                {
                    new()
                    {
                        Name = "Отлив", Color = "Белый",
                        Width = 1000, Height = 500, Quantity = 1, Price = 2150,
                        InstallationMode = 1,               // explicit mode (NOT DTO default 0)
                        InstallationDeduction = 0,           // v3.47.0 default
                        InstallationSurcharge = 500,         // v3.47.0 default per m.п.
                        InstallationAdjustment = 500         // v3.47.0 default per m.п.
                    }
                }
            };
            _vm.LoadFromOrderData(order, () => { });
            var item = _vm.OrderItems[0];

            // Migration should NOT re-fire (mode ≠ 0): values preserved as-loaded.
            Assert.Equal(1, item.InstallationMode);
            Assert.Equal(0, item.InstallationDeduction);
            Assert.Equal(500, item.InstallationSurcharge);
            Assert.Equal(500, item.InstallationAdjustment);
        }

        [Fact]
        public void LoadFromOrderData_Otliv_CustomDeduction_NotOverridden()
        {
            // Edge case: user has legitimately set ded=200 (NOT -500 DTO default).
            // The stricter isLegacyLoad check requires |ded|-500 ≈ 0; this test ensures
            // we don't accidentally reset a user-custom value that happens to fall
            // outside the legacy-conditions.
            var order = new OrderData
            {
                Items = new System.Collections.Generic.List<OrderItemData>
                {
                    new()
                    {
                        Name = "Отлив", Color = "Белый",
                        Width = 1000, Height = 500, Quantity = 1, Price = 2150,
                        InstallationMode = 0,
                        InstallationDeduction = 200,        // user-custom (NOT ±500)
                        InstallationSurcharge = 500,
                        InstallationAdjustment = 0
                    }
                }
            };
            _vm.LoadFromOrderData(order, () => { });
            var item = _vm.OrderItems[0];

            // isLegacyLoad = false (ded != -500): value-reset skipped.
            // The original v3.47.0 auto-switch (mode==0 + adj==0) DOES fire here.
            Assert.Equal(1, item.InstallationMode);
            Assert.Equal(200, item.InstallationDeduction);
            Assert.Equal(500, item.InstallationSurcharge);
            Assert.Equal(0, item.InstallationAdjustment);
        }

        [Theory]
        [InlineData("Отлив",   500,   500, 0)]   // pre-v3.46.1 positive convention legacy
        [InlineData("Отлив",  -500,  -500, 0)]   // v3.46.1+ signed convention legacy
        [InlineData("Козырёк", 500,   500, 0)]   // pre-v3.46.1 positive convention legacy
        [InlineData("Козырёк",-500,  -500, 0)]   // v3.46.1+ signed convention legacy
        public void LoadFromOrderData_PerLinearMeter_LegacyDefaults_BothSigns_ResetToV347Defaults(
            string product, double ded, double sur, double adj)
        {
            // v3.47.3 broadened heuristic: Math.Abs(Math.Abs(x) - 500) matches BOTH
            // pre-v3.46.1 (+500 = "subtract" under OLD convention) and v3.46.1+ (-500
            // = "subtract" under NEW signed convention) legacy defaults.
            var order = new OrderData
            {
                Items = new System.Collections.Generic.List<OrderItemData>
                {
                    new()
                    {
                        Name = product, Color = "Белый",
                        Width = 1000, Height = 500, Quantity = 1, Price = 2150,
                        InstallationMode = 0,
                        InstallationDeduction = ded,
                        InstallationSurcharge = sur,
                        InstallationAdjustment = adj
                    }
                }
            };

            _vm.LoadFromOrderData(order, () => { });

            var item = _vm.OrderItems[0];
            // Both sign variations of legacy defaults must trigger the migration.
            Assert.Equal(1, item.InstallationMode);
            Assert.Equal(0, item.InstallationDeduction);
            Assert.Equal(product == "Отлив" ? 500 : 750, item.InstallationSurcharge);
            Assert.Equal(product == "Отлив" ? 500 : 750, item.InstallationAdjustment);
            // TotalWithDeduction in all modes must be > 0.
            Assert.True(item.TotalWithDeduction > 0);
        }

        [Fact]
        public void LoadFromOrderData_PerLinearMeter_SignFlipExclusion_KeepsV347PositiveValues()
        {
            // v3.46.1 sign-flip (pos ⇒ neg) must NOT apply to per-linear-meter
            // products. Here we load a v3.47.0+ order with POSITIVE surcharge
            // (the new convention) and verify it's NOT flipped to negative.
            var order = new OrderData
            {
                Items = new System.Collections.Generic.List<OrderItemData>
                {
                    new()
                    {
                        Name = "Отлив", Color = "Белый",
                        Width = 1000, Height = 500, Quantity = 1, Price = 2150,
                        InstallationMode = 1,           // mode != 0 (explicit user choice)
                        InstallationDeduction = 0,
                        InstallationSurcharge = 500,    // POSITIVE convention since v3.47.0
                        InstallationAdjustment = 500
                    }
                }
            };
            _vm.LoadFromOrderData(order, () => { });
            var item = _vm.OrderItems[0];

            Assert.Equal(1, item.InstallationMode);
            Assert.Equal(0, item.InstallationDeduction);
            Assert.Equal(500, item.InstallationSurcharge);     // NOT flipped to -500
            Assert.Equal(500, item.InstallationAdjustment);
        }

        [Fact]
        public void LoadFromOrderData_Anvis_LegacyPositiveConvention_FlippedToNegative()
        {
            // v3.46.1 sign-flip MUST still apply to non-per-linear-meter products.
            // Anvis with mode=1 + ded=+500 (positive OLD convention) → flip → ded=-500.
            // This locks that the IsPerLinearMeter exclusion does NOT over-extend.
            var order = new OrderData
            {
                Items = new System.Collections.Generic.List<OrderItemData>
                {
                    new()
                    {
                        Name = "Anwis", Color = "Белый",
                        Width = 1000, Height = 1000, Quantity = 1, Price = 1800,
                        InstallationMode = 1,
                        InstallationDeduction = 500,    // OLD positive convention — must flip
                        InstallationSurcharge = 500,
                        InstallationAdjustment = 0
                    }
                }
            };
            _vm.LoadFromOrderData(order, () => { });
            var item = _vm.OrderItems[0];

            Assert.Equal(1, item.InstallationMode);
            Assert.Equal(-500, item.InstallationDeduction);   // flipped to NEW negative convention
            Assert.Equal(-500, item.InstallationSurcharge);
        }

        [Fact]
        public void ProductCatalog_IsPerLinearMeter_ConsistentWithOrderItemInstanceProperty()
        {
            // Locks the refactor: the migration logic in CalculationViewModel
            // delegates to ProductCatalog.IsPerLinearMeter, and
            // OrderItem.IsInstallationPerLinearMeter reads the same catalog.
            Assert.True(ProductCatalog.IsPerLinearMeter("Отлив"));
            Assert.True(ProductCatalog.IsPerLinearMeter("Козырёк"));
            Assert.False(ProductCatalog.IsPerLinearMeter("Anwis"));
            Assert.False(ProductCatalog.IsPerLinearMeter(null));
            Assert.False(ProductCatalog.IsPerLinearMeter(""));

            Assert.True(new OrderItem { Name = "Отлив" }.IsInstallationPerLinearMeter);
            Assert.True(new OrderItem { Name = "Козырёк" }.IsInstallationPerLinearMeter);
            Assert.False(new OrderItem { Name = "Anwis" }.IsInstallationPerLinearMeter);
        }

        // ─── v3.35.0: non-Anwis AddItem regression tests ─────────


        [Theory]
        [InlineData("Откос")]
        [InlineData("Работа за откос")]
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
            var item = _vm.AddItem("Откос", "", 250, 100, 1, 500,
                anwisMode: AnwisSizeMode.Брусбокс70)!;

            Assert.Equal(250, item.Width);
            Assert.Equal(100, item.Height);
            Assert.Equal(AnwisSizeMode.Брусбокс70, item.AnwisSizeMode);
        }

        [Theory]
        [InlineData("Откос")]
        [InlineData("Работа за откос")]
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
