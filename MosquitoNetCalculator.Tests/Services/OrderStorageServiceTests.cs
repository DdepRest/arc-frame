using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    [Collection("FileSystem")]
    public class OrderStorageServiceTests : IDisposable
    {
        private readonly OrderStorageService _service;
        private readonly string _ordersDir;
        private readonly string _originalOrdersDir;

        // File-IO isolation: OrderStorageService.OrdersDir is a static
        // property shared across the test process. Without the redirect,
        // LoadAllOrders / SaveOrder write to %AppData%\MosquitoNetCalculator\orders\
        // (the production path) instead of the per-test _ordersDir — so
        // tests asserting "expect 0 orders when fresh" see leftover
        // production data and tests asserting "File.Exists in _ordersDir"
        // see files written to AppData and fail. We snapshot the original,
        // override for the duration of the test, and restore on Dispose
        // so subsequent test classes are unaffected.
        // Same pattern as ManualChecklistTests.

        public OrderStorageServiceTests()
        {
            _originalOrdersDir = OrderStorageService.OrdersDir;

            _ordersDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "orders");
            if (Directory.Exists(_ordersDir))
                Directory.Delete(_ordersDir, true);
            Directory.CreateDirectory(_ordersDir);

            OrderStorageService.OrdersDir = _ordersDir;
            _service = new OrderStorageService();
        }

        public void Dispose()
        {
            OrderStorageService.OrdersDir = _originalOrdersDir;
            if (Directory.Exists(_ordersDir))
                Directory.Delete(_ordersDir, true);
        }

        [Fact]
        public void Constructor_CreatesOrdersDirectory()
        {
            Assert.True(Directory.Exists(_ordersDir));
        }

        [Fact]
        public void SaveOrder_AndLoad_Roundtrip()
        {
            var order = new OrderData
            {
                Id = Guid.NewGuid().ToString(),
                ClientName = "Тестовый Клиент",
                ContractNumber = "1-1",
                TotalAmount = 5000
            };
            _service.SaveOrder(order);
            var loaded = _service.LoadOrder(order.Id);

            Assert.NotNull(loaded);
            Assert.Equal("Тестовый Клиент", loaded!.ClientName);
            Assert.Equal("1-1", loaded.ContractNumber);
            Assert.Equal(5000, loaded.TotalAmount);
        }

        [Fact]
        public void SaveOrder_PreservesItems()
        {
            var order = new OrderData
            {
                Id = Guid.NewGuid().ToString(),
                Items = new List<OrderItemData>
                {
                    new() { Name = "Anwis", Color = "Белый", Price = 1800, Quantity = 2 }
                }
            };
            _service.SaveOrder(order);
            var loaded = _service.LoadOrder(order.Id);

            Assert.NotNull(loaded);
            Assert.Single(loaded!.Items);
            Assert.Equal("Anwis", loaded.Items[0].Name);
            Assert.Equal(2, loaded.Items[0].Quantity);
        }

        [Fact]
        public void LoadOrder_ReturnsNull_ForNonExistent()
        {
            var result = _service.LoadOrder("non-existent-id-12345");
            Assert.Null(result);
        }

        [Fact]
        public void LoadAllOrders_ReturnsAllSaved()
        {
            var order1 = new OrderData { Id = Guid.NewGuid().ToString(), ClientName = "A" };
            var order2 = new OrderData { Id = Guid.NewGuid().ToString(), ClientName = "B" };
            _service.SaveOrder(order1);
            _service.SaveOrder(order2);

            // Need new instance to clear cache
            var freshService = new OrderStorageService();
            var all = freshService.LoadAllOrders();
            Assert.True(all.Count >= 2);
        }

        [Fact]
        public void LoadAllOrders_ReturnsEmpty_WhenNoOrders()
        {
            var all = _service.LoadAllOrders();
            Assert.Empty(all);
        }

        [Fact]
        public void LoadAllOrders_ReturnsNewestFirst()
        {
            var order1 = new OrderData { Id = Guid.NewGuid().ToString(), UpdatedAt = DateTime.Now.AddHours(-2) };
            var order2 = new OrderData { Id = Guid.NewGuid().ToString(), UpdatedAt = DateTime.Now };
            _service.SaveOrder(order1);
            _service.SaveOrder(order2);

            var freshService = new OrderStorageService();
            var all = freshService.LoadAllOrders();
            // First element should be the most recently updated
            Assert.True(all[0].UpdatedAt >= all[1].UpdatedAt);
        }

        [Fact]
        public void DeleteOrder_RemovesOrder()
        {
            var order = new OrderData { Id = Guid.NewGuid().ToString() };
            _service.SaveOrder(order);
            _service.DeleteOrder(order.Id);
            Assert.Null(_service.LoadOrder(order.Id));
        }

        [Fact]
        public void DeleteOrder_DoesNotThrow_ForNonExistent()
        {
            var exception = Record.Exception(() => _service.DeleteOrder("non-existent-id"));
            Assert.Null(exception);
        }

        [Fact]
        public void GetNextOrderNumber_ReturnsOne_WhenNoOrders()
        {
            int next = _service.GetNextOrderNumber("1");
            Assert.Equal(1, next);
        }

        [Fact]
        public void GetNextOrderNumber_IncrementsCorrectly()
        {
            _service.SaveOrder(new OrderData { ContractNumber = "1-5" });
            _service.SaveOrder(new OrderData { ContractNumber = "1-10" });
            int next = _service.GetNextOrderNumber("1");
            Assert.Equal(11, next);
        }

        [Fact]
        public void GetNextOrderNumber_IgnoresOtherPrefixes()
        {
            _service.SaveOrder(new OrderData { ContractNumber = "2-5" });
            int next = _service.GetNextOrderNumber("1");
            Assert.Equal(1, next);
        }

        [Fact]
        public void GetNextOrderNumber_HandlesInvalidFormat()
        {
            _service.SaveOrder(new OrderData { ContractNumber = "invalid" });
            _service.SaveOrder(new OrderData { ContractNumber = "" });
            int next = _service.GetNextOrderNumber("1");
            Assert.Equal(1, next);
        }

        [Fact]
        public void GenerateContractNumber_FormatCorrect()
        {
            _service.SaveOrder(new OrderData { ContractNumber = "1-3" });
            string number = _service.GenerateContractNumber("1");
            Assert.Equal("1-4", number);
        }

        [Fact]
        public void SaveOrder_UpdatesTimestamp()
        {
            var order = new OrderData { Id = Guid.NewGuid().ToString() };
            var before = DateTime.Now.AddSeconds(-1);
            _service.SaveOrder(order);
            var loaded = _service.LoadOrder(order.Id);
            Assert.NotNull(loaded);
            Assert.True(loaded!.UpdatedAt >= before);
        }

        [Fact]
        public void ExportOrders_CreatesFile()
        {
            var orders = new List<OrderData> { new() { Id = "export-test" } };
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "export_test_temp.json");
            try
            {
                _service.ExportOrders(orders, path);
                Assert.True(File.Exists(path));
                var content = File.ReadAllText(path);
                Assert.Contains("export-test", content);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void SaveOrder_HandlesAdditionalKps()
        {
            var order = new OrderData
            {
                Id = Guid.NewGuid().ToString(),
                HasAdditionalKp = true,
                AdditionalKps = new List<AdditionalKpItem>
                {
                    new() { Number = "2-1", Amount = 500, IsActive = true },
                    new() { Number = "2-2", Amount = 300, IsActive = false }
                }
            };
            _service.SaveOrder(order);
            var loaded = _service.LoadOrder(order.Id);

            Assert.NotNull(loaded);
            Assert.True(loaded!.HasAdditionalKp);
            Assert.Equal(2, loaded.AdditionalKps.Count);
            Assert.Equal(500, loaded.AdditionalKps[0].Amount);
            Assert.False(loaded.AdditionalKps[1].IsActive);
        }
    }
}
