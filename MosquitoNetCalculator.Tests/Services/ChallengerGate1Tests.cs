using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    [Collection("FileSystem")]
    public class ChallengerGate1Tests : IDisposable
    {
        private readonly OrderStorageService _service;
        private readonly string _ordersDir;
        private readonly string _trashDir;
        private readonly string _originalOrdersDir;
        private readonly string _originalTrashDir;

        public ChallengerGate1Tests()
        {
            _originalOrdersDir = OrderStorageService.OrdersDir;
            _originalTrashDir = OrderStorageService.TrashDir;

            _ordersDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "challenger_orders_" + Guid.NewGuid().ToString("N"));
            _trashDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "challenger_trash_" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(_ordersDir);
            Directory.CreateDirectory(_trashDir);

            OrderStorageService.OrdersDir = _ordersDir;
            OrderStorageService.TrashDir = _trashDir;
            _service = new OrderStorageService();
        }

        public void Dispose()
        {
            OrderStorageService.OrdersDir = _originalOrdersDir;
            OrderStorageService.TrashDir = _originalTrashDir;

            if (Directory.Exists(_ordersDir))
                Directory.Delete(_ordersDir, true);
            if (Directory.Exists(_trashDir))
                Directory.Delete(_trashDir, true);
        }

        [Fact]
        public void IsEmptyDummy_ExactBoundaryCases_MatchSpecification()
        {
            // Case 1: Items == null and TotalAmount == 0 -> true
            var order1 = new OrderData { Items = null!, TotalAmount = 0.0 };
            Assert.True(order1.IsEmptyDummy, "Items == null and TotalAmount == 0 must be true");

            // Case 2: Items == [] and TotalAmount == 0 -> true
            var order2 = new OrderData { Items = new List<OrderItemData>(), TotalAmount = 0.0 };
            Assert.True(order2.IsEmptyDummy, "Items == [] and TotalAmount == 0 must be true");

            // Case 3: Items == [] and TotalAmount == 100 -> false
            var order3 = new OrderData { Items = new List<OrderItemData>(), TotalAmount = 100.0 };
            Assert.False(order3.IsEmptyDummy, "Items == [] and TotalAmount == 100 must be false");

            // Case 4: Items == [item] and TotalAmount == 0 -> false
            var order4 = new OrderData
            {
                Items = new List<OrderItemData> { new() { Name = "Sample", Price = 0 } },
                TotalAmount = 0.0
            };
            Assert.False(order4.IsEmptyDummy, "Items == [item] and TotalAmount == 0 must be false");

            // Case 5: Items == null and TotalAmount == 100 -> false
            var order5 = new OrderData { Items = null!, TotalAmount = 100.0 };
            Assert.False(order5.IsEmptyDummy, "Items == null and TotalAmount == 100 must be false");

            // Case 6: Negative total amount with empty items -> false
            var order6 = new OrderData { Items = new List<OrderItemData>(), TotalAmount = -50.0 };
            Assert.False(order6.IsEmptyDummy, "Items == [] and TotalAmount == -50 must be false");

            // Case 7: Floating point epsilon around 0 (< 0.001)
            var order7 = new OrderData { Items = new List<OrderItemData>(), TotalAmount = 0.0005 };
            Assert.True(order7.IsEmptyDummy, "Items == [] and TotalAmount == 0.0005 (< 0.001) should evaluate true");

            var order8 = new OrderData { Items = new List<OrderItemData>(), TotalAmount = 0.005 };
            Assert.False(order8.IsEmptyDummy, "Items == [] and TotalAmount == 0.005 (>= 0.001) should evaluate false");
        }

        [Fact]
        public void SoftDeleteOrder_EmptyDummy_DeletesFromOrdersDir_LeavesTrashDirEmpty()
        {
            var dummyOrder = new OrderData
            {
                Id = Guid.NewGuid().ToString(),
                ClientName = "DummyCustomer",
                TotalAmount = 0.0,
                Items = new List<OrderItemData>()
            };

            _service.SaveOrder(dummyOrder);
            string orderFile = Path.Combine(_ordersDir, $"{dummyOrder.Id}.json");
            string trashFile = Path.Combine(_trashDir, $"{dummyOrder.Id}.json");

            Assert.True(File.Exists(orderFile), "Order file should exist before delete");
            Assert.False(File.Exists(trashFile), "Trash file should not exist before delete");

            _service.SoftDeleteOrder(dummyOrder.Id);

            Assert.False(File.Exists(orderFile), "Order file should be deleted from OrdersDir");
            Assert.False(File.Exists(trashFile), "Order file should NOT exist in TrashDir");
            Assert.Empty(Directory.GetFiles(_trashDir, "*.json"));
        }

        [Fact]
        public void SoftDeleteOrder_RealOrderWithItemsAndNonZeroAmount_MovesToTrashDir()
        {
            var realOrder = new OrderData
            {
                Id = Guid.NewGuid().ToString(),
                ClientName = "RealCustomer",
                TotalAmount = 4500.0,
                Items = new List<OrderItemData>
                {
                    new() { Name = "Standard", Price = 4500.0, Quantity = 1 }
                }
            };

            _service.SaveOrder(realOrder);
            string orderFile = Path.Combine(_ordersDir, $"{realOrder.Id}.json");
            string trashFile = Path.Combine(_trashDir, $"{realOrder.Id}.json");

            Assert.True(File.Exists(orderFile), "Order file should exist before delete");
            Assert.False(File.Exists(trashFile), "Trash file should not exist before delete");

            _service.SoftDeleteOrder(realOrder.Id);

            Assert.False(File.Exists(orderFile), "Real order file should be removed from OrdersDir");
            Assert.True(File.Exists(trashFile), "Real order file MUST exist in TrashDir");
            Assert.Single(Directory.GetFiles(_trashDir, "*.json"));

            // Verify content in trash
            string trashJson = File.ReadAllText(trashFile);
            var restored = JsonSerializer.Deserialize<OrderData>(trashJson, OrderStorageService.JsonOptions);
            Assert.NotNull(restored);
            Assert.Equal("RealCustomer", restored.ClientName);
            Assert.Equal(4500.0, restored.TotalAmount);
            Assert.Single(restored.Items);
        }

        [Fact]
        public void SoftDeleteOrder_ItemWithZeroAmount_IsPreservedInTrash_NotTreatedAsDummy()
        {
            // An order with 1 item but 0 total amount (e.g. warranty / free replacement) is NOT a dummy!
            var warrantyOrder = new OrderData
            {
                Id = Guid.NewGuid().ToString(),
                ClientName = "WarrantyCustomer",
                TotalAmount = 0.0,
                Items = new List<OrderItemData>
                {
                    new() { Name = "FreeWarrantyReplacement", Price = 0.0, Quantity = 1 }
                }
            };

            _service.SaveOrder(warrantyOrder);
            string orderFile = Path.Combine(_ordersDir, $"{warrantyOrder.Id}.json");
            string trashFile = Path.Combine(_trashDir, $"{warrantyOrder.Id}.json");

            _service.SoftDeleteOrder(warrantyOrder.Id);

            Assert.False(File.Exists(orderFile));
            Assert.True(File.Exists(trashFile), "Warranty order with items MUST enter trash, even if TotalAmount == 0");
        }

        [Fact]
        public void SoftDeleteOrder_ExistingTrashFileCollision_OverwritesSafely()
        {
            var order = new OrderData
            {
                Id = Guid.NewGuid().ToString(),
                ClientName = "CollisionCustomer",
                TotalAmount = 1500.0,
                Items = new List<OrderItemData>
                {
                    new() { Name = "OldVersion", Price = 1500.0, Quantity = 1 }
                }
            };

            // Pre-create an old file in trash with the same name
            string trashFile = Path.Combine(_trashDir, $"{order.Id}.json");
            File.WriteAllText(trashFile, "{ \"ClientName\": \"StaleGhost\" }");

            _service.SaveOrder(order);
            _service.SoftDeleteOrder(order.Id);

            Assert.True(File.Exists(trashFile));
            string json = File.ReadAllText(trashFile);
            Assert.Contains("CollisionCustomer", json);
            Assert.DoesNotContain("StaleGhost", json);
        }

        [Fact]
        public void CleanEmptyTrashOrders_PurgesDummiesAndCorruptedFiles_RetainsValidOrders()
        {
            // 1. Valid order
            var valid = new OrderData
            {
                Id = Guid.NewGuid().ToString(),
                ClientName = "KeepMe",
                TotalAmount = 2000.0,
                Items = new List<OrderItemData> { new() { Name = "Net", Price = 2000.0 } }
            };
            File.WriteAllText(Path.Combine(_trashDir, $"{valid.Id}.json"),
                JsonSerializer.Serialize(valid, OrderStorageService.JsonOptions));

            // 2. Dummy order (0 items, 0 sum)
            var dummy = new OrderData
            {
                Id = Guid.NewGuid().ToString(),
                ClientName = "DeleteMe",
                TotalAmount = 0.0,
                Items = new List<OrderItemData>()
            };
            File.WriteAllText(Path.Combine(_trashDir, $"{dummy.Id}.json"),
                JsonSerializer.Serialize(dummy, OrderStorageService.JsonOptions));

            // 3. Corrupt json
            File.WriteAllText(Path.Combine(_trashDir, $"{Guid.NewGuid():D}.json"), "NOT JSON AT ALL");

            // 4. Empty file
            File.WriteAllText(Path.Combine(_trashDir, $"{Guid.NewGuid():D}.json"), "");

            Assert.Equal(4, Directory.GetFiles(_trashDir, "*.json").Length);

            OrderStorageService.CleanEmptyTrashOrders(_trashDir);

            var remaining = Directory.GetFiles(_trashDir, "*.json");
            Assert.Single(remaining);
            Assert.Equal(Path.Combine(_trashDir, $"{valid.Id}.json"), remaining[0]);
        }
    }
}
