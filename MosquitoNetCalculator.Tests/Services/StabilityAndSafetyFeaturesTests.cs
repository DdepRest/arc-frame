using System;
using System.Collections.Generic;
using System.IO;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    [Collection("FileSystem")]
    public class StabilityAndSafetyFeaturesTests : IDisposable
    {
        private readonly string _tempTestDir;
        private readonly string _originalOrdersDir;
        private readonly string _originalTrashDir;
        private readonly string _originalDraftFilePath;
        private readonly string _originalBackupsDir;
        private readonly string _originalBackupOrdersDir;
        private readonly string _originalCounterPath;
        private readonly string _originalPricesPath;

        public StabilityAndSafetyFeaturesTests()
        {
            _tempTestDir = Path.Combine(Path.GetTempPath(), "ArcSafetyTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempTestDir);

            _originalOrdersDir = OrderStorageService.OrdersDir;
            _originalTrashDir = OrderStorageService.TrashDir;
            _originalDraftFilePath = DraftRecoveryService.DraftFilePath;
            _originalBackupsDir = OrderBackupService.BackupsDir;
            _originalBackupOrdersDir = OrderBackupService.OrdersDir;
            _originalCounterPath = OrderBackupService.CounterPath;
            _originalPricesPath = OrderBackupService.PricesPath;

            OrderStorageService.OrdersDir = Path.Combine(_tempTestDir, "orders");
            OrderStorageService.TrashDir = Path.Combine(_tempTestDir, "orders_trash");
            DraftRecoveryService.DraftFilePath = Path.Combine(_tempTestDir, "draft.json");
            OrderBackupService.BackupsDir = Path.Combine(_tempTestDir, "backups");
            OrderBackupService.OrdersDir = Path.Combine(_tempTestDir, "orders");
            OrderBackupService.CounterPath = Path.Combine(_tempTestDir, "order_counter.json");
            OrderBackupService.PricesPath = Path.Combine(_tempTestDir, "prices.json");
        }

        public void Dispose()
        {
            OrderStorageService.OrdersDir = _originalOrdersDir;
            OrderStorageService.TrashDir = _originalTrashDir;
            DraftRecoveryService.DraftFilePath = _originalDraftFilePath;
            OrderBackupService.BackupsDir = _originalBackupsDir;
            OrderBackupService.OrdersDir = _originalBackupOrdersDir;
            OrderBackupService.CounterPath = _originalCounterPath;
            OrderBackupService.PricesPath = _originalPricesPath;

            try
            {
                if (Directory.Exists(_tempTestDir))
                    Directory.Delete(_tempTestDir, true);
            }
            catch { }
        }

        [Fact]
        public void DraftRecoveryService_SaveAndLoadDraft_WorksAtomically()
        {
            using var draftService = new DraftRecoveryService();
            var order = new OrderData
            {
                ContractNumber = "1-100",
                ClientName = "Тестовый Клиент",
                ClientPhone = "+7 (999) 000-00-00",
                Items = new List<OrderItemData>
                {
                    new OrderItemData { Name = "Anwis", Width = 1200, Height = 600, Quantity = 2, Price = 1500 }
                }
            };

            Assert.False(draftService.HasPendingDraft());

            draftService.SaveDraftNow(order);

            Assert.True(draftService.HasPendingDraft());
            var loaded = draftService.LoadDraft();
            Assert.NotNull(loaded);
            Assert.Equal("1-100", loaded!.ContractNumber);
            Assert.Equal("Тестовый Клиент", loaded.ClientName);
            Assert.Single(loaded.Items);
            Assert.Equal(1200, loaded.Items[0].Width);

            draftService.DiscardDraft();
            Assert.False(draftService.HasPendingDraft());
            Assert.Null(draftService.LoadDraft());
        }

        [Fact]
        public void OrderStorageService_SoftDeleteAndRestore_MovesFilesCorrectly()
        {
            var storage = new OrderStorageService();
            var order = new OrderData
            {
                ContractNumber = "2-55",
                ClientName = "Петров",
                TotalAmount = 4500
            };

            storage.SaveOrder(order);

            var activeOrders = storage.LoadAllOrders();
            Assert.Contains(activeOrders, o => o.Id == order.Id);

            var trashOrders = storage.LoadTrashOrders();
            Assert.DoesNotContain(trashOrders, o => o.Id == order.Id);

            // Soft delete
            storage.SoftDeleteOrder(order.Id);

            activeOrders = storage.LoadAllOrders();
            Assert.DoesNotContain(activeOrders, o => o.Id == order.Id);

            trashOrders = storage.LoadTrashOrders();
            Assert.Contains(trashOrders, o => o.Id == order.Id);

            // Restore
            storage.RestoreOrder(order.Id);

            activeOrders = storage.LoadAllOrders();
            Assert.Contains(activeOrders, o => o.Id == order.Id);

            trashOrders = storage.LoadTrashOrders();
            Assert.DoesNotContain(trashOrders, o => o.Id == order.Id);

            // Purge
            storage.SoftDeleteOrder(order.Id);
            storage.PurgeTrashOrder(order.Id);

            trashOrders = storage.LoadTrashOrders();
            Assert.DoesNotContain(trashOrders, o => o.Id == order.Id);
        }

        [Fact]
        public void OrderBackupService_CreatesZipArchiveWithOrders()
        {
            var storage = new OrderStorageService();
            var order = new OrderData
            {
                ContractNumber = "1-1",
                ClientName = "Архивный Заказ",
                TotalAmount = 3000
            };
            storage.SaveOrder(order);

            File.WriteAllText(OrderBackupService.CounterPath, "{\"counter\": 1}");
            File.WriteAllText(OrderBackupService.PricesPath, "{}");

            var backupService = new OrderBackupService();
            string zipPath = backupService.CreateManualBackup();

            Assert.True(File.Exists(zipPath));
            var info = backupService.GetLatestBackupInfo();
            Assert.NotNull(info);
            Assert.True(info!.Value.SizeBytes > 0);
        }

        [Fact]
        public void DraftRecoveryService_DraftSavedEvent_FiresOnSave()
        {
            using var draftService = new DraftRecoveryService();
            bool eventFired = false;
            DateTime savedTime = DateTime.MinValue;

            draftService.DraftSaved += (s, time) =>
            {
                eventFired = true;
                savedTime = time;
            };

            var order = new OrderData
            {
                ContractNumber = "3-77",
                Items = new List<OrderItemData>
                {
                    new OrderItemData { Name = "Anwis", Width = 1000, Height = 1000, Quantity = 1, Price = 1800 }
                }
            };

            draftService.SaveDraftNow(order);

            Assert.True(eventFired);
            Assert.True(savedTime > DateTime.MinValue);
        }

        [Fact]
        public void OrderItemData_ToOrderItem_ConvertsAndCalculatesTotal()
        {
            var dto = new OrderItemData
            {
                Name = "Anwis",
                Color = "Белый",
                Width = 1000,
                Height = 1000,
                Quantity = 2,
                Price = 1800,
                InstallationMode = 0
            };

            var item = dto.ToOrderItem();

            Assert.NotNull(item);
            Assert.Equal("Anwis", item.Name);
            Assert.Equal("Белый", item.Color);
            Assert.Equal(1000, item.Width);
            Assert.Equal(1000, item.Height);
            Assert.Equal(2, item.Quantity);
            Assert.Equal(1800, item.Price);
            Assert.True(item.Total > 0);
        }
    }
}
