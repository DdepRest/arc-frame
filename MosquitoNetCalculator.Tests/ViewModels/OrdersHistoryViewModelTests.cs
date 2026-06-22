using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.ViewModels;
using Xunit;

namespace MosquitoNetCalculator.Tests.ViewModels
{
    [Collection("FileSystem")]
    public class OrdersHistoryViewModelTests : IDisposable
    {
        private readonly OrdersHistoryViewModel _vm = new();
        private readonly string _ordersDir;
        private readonly string _originalOrdersDir;

        // File-IO isolation: OrderStorageService.OrdersDir is a static
        // property in the production type. We redirect it to a per-test
        // temp dir on construction and restore the original on Dispose so
        // the test doesn't leak its override into other test classes in
        // the same process, and so a polluted AppData doesn't make the
        // assertions like "expect first contract number == 1-1" trip
        // over pre-existing production orders.
        // Same pattern as MosquitoNetCalculator.Tests.App.ManualChecklistTests.

        public OrdersHistoryViewModelTests()
        {
            _originalOrdersDir = OrderStorageService.OrdersDir;

            _ordersDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "orders");
            if (Directory.Exists(_ordersDir))
                Directory.Delete(_ordersDir, true);
            Directory.CreateDirectory(_ordersDir);

            OrderStorageService.OrdersDir = _ordersDir;
        }

        public void Dispose()
        {
            OrderStorageService.OrdersDir = _originalOrdersDir;
            if (Directory.Exists(_ordersDir))
                Directory.Delete(_ordersDir, true);
        }

        // ─── SanitizeFileName tests ──────────────────────────

        [Fact]
        public void SanitizeFileName_RemovesInvalidChars()
        {
            var result = _vm.SanitizeFileName("test<>:\"/\\|?*file");
            Assert.DoesNotContain("<", result);
            Assert.DoesNotContain(">", result);
            Assert.DoesNotContain(":", result);
            Assert.DoesNotContain("\"", result);
            Assert.DoesNotContain("|", result);
            Assert.DoesNotContain("?", result);
            Assert.DoesNotContain("*", result);
        }

        [Fact]
        public void SanitizeFileName_TrimsToMaxLength()
        {
            var longName = new string('a', 100);
            var result = _vm.SanitizeFileName(longName);
            Assert.True(result.Length <= 60);
        }

        [Fact]
        public void SanitizeFileName_ReturnsEmpty_ForEmpty()
        {
            Assert.Equal("", _vm.SanitizeFileName(""));
        }

        [Fact]
        public void SanitizeFileName_ReturnsEmpty_ForWhitespace()
        {
            Assert.Equal("", _vm.SanitizeFileName("   "));
        }

        [Fact]
        public void SanitizeFileName_TrimsWhitespace()
        {
            var result = _vm.SanitizeFileName("  test  ");
            Assert.Equal("test", result);
        }

        [Fact]
        public void SanitizeFileName_PreservesValidChars()
        {
            var result = _vm.SanitizeFileName("Заказ-123 (Москва)");
            Assert.Contains("Заказ", result);
            Assert.Contains("123", result);
        }

        // ─── MergeImport tests ───────────────────────────────

        [Fact]
        public void MergeImport_ImportsNewOrders()
        {
            var fileOrders = new List<OrderData>
            {
                new() { Id = "new-1", ClientName = "New Client" }
            };
            var imported = _vm.MergeImport(fileOrders);
            Assert.Single(imported);
            Assert.Equal("New Client", imported[0].ClientName);
        }

        [Fact]
        public void MergeImport_SkipsOlderDuplicates()
        {
            var existing = new OrderData { Id = "same-id", UpdatedAt = DateTime.Now };
            _vm.SaveOrder(existing);

            var fileOrders = new List<OrderData>
            {
                new() { Id = "same-id", UpdatedAt = DateTime.Now.AddHours(-1) }
            };
            var imported = _vm.MergeImport(fileOrders);
            Assert.Empty(imported);
        }

        [Fact]
        public void MergeImport_ImportsNewerDuplicates()
        {
            var existing = new OrderData { Id = "same-id", UpdatedAt = DateTime.Now.AddHours(-2), ClientName = "Old" };
            _vm.SaveOrder(existing);

            var fileOrders = new List<OrderData>
            {
                new() { Id = "same-id", UpdatedAt = DateTime.Now, ClientName = "Updated" }
            };
            var imported = _vm.MergeImport(fileOrders);
            Assert.Single(imported);
            Assert.Equal("Updated", imported[0].ClientName);
        }

        [Fact]
        public void MergeImport_HandlesEmptyList()
        {
            var imported = _vm.MergeImport(new List<OrderData>());
            Assert.Empty(imported);
        }

        // ─── ReadOrdersFromFile tests ────────────────────────

        [Fact]
        public void ReadOrdersFromFile_DeserializesCorrectly()
        {
            var orders = new List<OrderData>
            {
                new() { Id = "test-1", ClientName = "Client A" },
                new() { Id = "test-2", ClientName = "Client B" }
            };
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_import_temp.json");
            try
            {
                File.WriteAllText(filePath, JsonSerializer.Serialize(orders));
                var loaded = _vm.ReadOrdersFromFile(filePath);
                Assert.NotNull(loaded);
                Assert.Equal(2, loaded!.Count);
                Assert.Equal("Client A", loaded[0].ClientName);
            }
            finally
            {
                if (File.Exists(filePath)) File.Delete(filePath);
            }
        }

        // ─── Bug #3 fix regression: read/write symmetry ───────────
        // ReadOrdersFromFile used to call JsonSerializer.Deserialize with
        // NO options, while ExportOrders used OrderStorageService.JsonOptions
        // (which includes UnsafeRelaxedJsonEscaping). The current options
        // are write-only (deserialise ignores them), so the asymmetry was
        // cosmetic — but a real maintenance hazard: any future option
        // change (e.g. PropertyNamingPolicy) would silently break the
        // import path because it reads with a different options object.
        // The round-trip test below goes through the VM on BOTH sides
        // (ExportOrders writes, ReadOrdersFromFile reads) so the test
        // cannot pass unless both VM methods use compatible options.

        [Fact]
        public void ReadOrdersFromFile_OldJsonWithRemovedFields_StillDeserializes()
        {
            // Backward-compat regression: v3.22.0 removed `CalculatedValue`
            // and `Total` from OrderItemData (they were dead — always
            // recomputed from W × H × P × Q on load). Old JSON files saved
            // before the change still contain these fields. System.Text.Json
            // ignores unknown fields by default, so the load must still
            // succeed and produce correct OrderData (with Items populated,
            // even though the dead fields are stripped).
            //
            // ReadOrdersFromFile expects a List<OrderData> (array of orders).
            string oldJson = @"[{
                ""Id"": ""legacy-1"",
                ""ContractNumber"": ""1-7"",
                ""ClientName"": ""Legacy Client"",
                ""Items"": [
                    {
                        ""Name"": ""Anwis"",
                        ""Color"": ""Белый"",
                        ""Width"": 1000,
                        ""Height"": 1500,
                        ""Quantity"": 2,
                        ""CalculatedValue"": 999.999,
                        ""Price"": 1800,
                        ""Total"": 99999.99,
                        ""InstallationMode"": 0,
                        ""HasInstallation"": true
                    }
                ]
            }]";

            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_legacy_temp.json");
            try
            {
                File.WriteAllText(filePath, oldJson, System.Text.Encoding.UTF8);
                var loaded = _vm.ReadOrdersFromFile(filePath);

                Assert.NotNull(loaded);
                Assert.Single(loaded!);
                Assert.Equal("legacy-1", loaded![0].Id);
                Assert.Equal("Legacy Client", loaded[0].ClientName);
                Assert.Single(loaded[0].Items);
                Assert.Equal("Anwis", loaded[0].Items[0].Name);
                Assert.Equal(1000, loaded[0].Items[0].Width);
                Assert.Equal(1500, loaded[0].Items[0].Height);
                Assert.Equal(2, loaded[0].Items[0].Quantity);
                Assert.Equal(1800, loaded[0].Items[0].Price);
                // The dead fields (CalculatedValue=999.999, Total=99999.99)
                // are stripped on load — they're not part of OrderItemData
                // anymore. The load itself must not throw.
            }
            finally
            {
                if (File.Exists(filePath)) File.Delete(filePath);
            }
        }

        [Fact]
        public void ReadOrdersFromFile_OldJsonWithRemovedFields_AndCyrillic_StillDeserializes()
        {
            // Closes the gap flagged in the v3.22.0 review: legacy JSON
            // with Cyrillic text + the now-removed dead fields must load
            // in one pass (System.Text.Json ignores unknown fields AND
            // preserves Cyrillic in the same load).
            string oldJson = @"[{
                ""Id"": ""legacy-cyr-1"",
                ""ContractNumber"": ""1-9"",
                ""ClientName"": ""Иванов <И.И.>"",
                ""Items"": [
                    {
                        ""Name"": ""Антимоскитная сетка"",
                        ""Color"": ""Белый"",
                        ""Width"": 1200,
                        ""Height"": 1800,
                        ""Quantity"": 1,
                        ""CalculatedValue"": 999.999,
                        ""Price"": 2500,
                        ""Total"": 99999.99,
                        ""InstallationMode"": 0
                    }
                ]
            }]";

            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_legacy_cyr_temp.json");
            try
            {
                File.WriteAllText(filePath, oldJson, System.Text.Encoding.UTF8);
                var loaded = _vm.ReadOrdersFromFile(filePath);

                Assert.NotNull(loaded);
                Assert.Single(loaded!);
                Assert.Equal("legacy-cyr-1", loaded![0].Id);
                Assert.Equal("Иванов <И.И.>", loaded[0].ClientName);
                Assert.Single(loaded[0].Items);
                Assert.Equal("Антимоскитная сетка", loaded[0].Items[0].Name);
                Assert.Equal(2500, loaded[0].Items[0].Price);
            }
            finally
            {
                if (File.Exists(filePath)) File.Delete(filePath);
            }
        }

        [Fact]
        public void SaveOrder_DoesNotWriteRemovedFields_ToJson()
        {
            // Load-bearing regression for the "dead bytes" rationale:
            // after the v3.22.0 removal, saved JSON must NOT contain the
            // removed fields. If a future refactor re-adds them, the
            // JSON files balloon with dead data and the whole point of
            // the cleanup is lost.
            //
            // Uses JsonDocument (not regex) per the v3.22.0 review:
            // regex over JSON is fragile (nested arrays, escapes),
            // and the same code length gives a proper structural check.
            var order = new OrderData
            {
                Id = Guid.NewGuid().ToString(),
                ContractNumber = "1-99",
                ClientName = "Test",
                Items = new List<OrderItemData>
                {
                    new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800 }
                }
            };
            _vm.SaveOrder(order);

            var filePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "orders", $"{order.Id}.json");
            try
            {
                Assert.True(File.Exists(filePath), $"SaveOrder did not create {filePath}");
                string json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);

                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                // CalculatedValue: removed entirely, must not appear anywhere.
                // Check via JsonDocument (not string match) to be precise
                // about JSON structure — avoid false positives from text
                // inside notes/client name.
                Assert.False(HasPropertyRecursive(root, "CalculatedValue"),
                    "Removed field 'CalculatedValue' must not appear in saved JSON");

                // Total: still present at OrderData.TotalAmount (intentional,
                // not removed). Must NOT appear inside any Items[] element.
                // Walk the Items array and check each element.
                Assert.True(root.TryGetProperty("Items", out var items), "Items array missing");
                Assert.Equal(System.Text.Json.JsonValueKind.Array, items.ValueKind);
                foreach (var item in items.EnumerateArray())
                {
                    Assert.False(item.TryGetProperty("Total", out _),
                        "Removed field 'Total' must not appear in saved items");
                }
            }
            finally
            {
                if (File.Exists(filePath)) File.Delete(filePath);
            }
        }

        // Recursive helper: returns true if any property in the JSON tree
        // (at any depth) has the given name. Used to assert the removed
        // 'CalculatedValue' is absent from the entire document, not just
        // at the root.
        private static bool HasPropertyRecursive(System.Text.Json.JsonElement element, string propertyName)
        {
            if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.NameEquals(propertyName)) return true;
                    if (HasPropertyRecursive(prop.Value, propertyName)) return true;
                }
            }
            else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (HasPropertyRecursive(item, propertyName)) return true;
                }
            }
            return false;
        }

        [Fact]
        public void ReadOrdersFromFile_RoundtripsExportOrders_WithCyrillicAndSpecialChars()
        {
            // Write through the VM's ExportOrders (storage service's
            // write path), then read through the VM's ReadOrdersFromFile
            // (read path). If the two VM call sites use different
            // JsonOptions, the round-trip either throws on deserialise
            // or produces wrong data.
            var orders = new List<OrderData>
            {
                new()
                {
                    Id = Guid.NewGuid().ToString(),
                    ClientName = "Иванов <И.И.> & \"Петров\"",
                    ClientPhone = "+7 (999) 123-45-67",
                    ClientAddress = "г. Москва, ул. Пушкина, д. 10 — кв. 5",
                    Notes = "Заметка: <script>alert(1)</script> должна быть сохранена как текст",
                    ContractNumber = "1-42",
                    Items = new List<OrderItemData>
                    {
                        new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1500, Quantity = 2, Price = 1800 }
                    }
                }
            };

            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_roundtrip_temp.json");
            try
            {
                // Write through the VM's public ExportOrders (not
                // manual JsonSerializer.Serialize) so the test exercises
                // the same code path a real export uses.
                _vm.ExportOrders(orders, filePath);

                // Read through the VM's ReadOrdersFromFile. If a future
                // options change diverges read and write, this throws
                // or returns wrong data.
                var loaded = _vm.ReadOrdersFromFile(filePath);
                Assert.NotNull(loaded);
                Assert.Single(loaded!);
                Assert.Equal("Иванов <И.И.> & \"Петров\"", loaded![0].ClientName);
                Assert.Contains("<script>", loaded[0].Notes);
                Assert.Equal("1-42", loaded[0].ContractNumber);
                Assert.Single(loaded[0].Items);
                Assert.Equal("Anwis", loaded[0].Items[0].Name);
            }
            finally
            {
                if (File.Exists(filePath)) File.Delete(filePath);
            }
        }

        // ─── GenerateContractNumber tests ────────────────────

        [Fact]
        public void GenerateContractNumber_ReturnsFirstNumber_WhenNoOrders()
        {
            var number = _vm.GenerateContractNumber("1");
            Assert.Equal("1-1", number);
        }

        [Fact]
        public void GenerateContractNumber_Increments()
        {
            _vm.SaveOrder(new OrderData { ContractNumber = "1-3" });
            var number = _vm.GenerateContractNumber("1");
            Assert.Equal("1-4", number);
        }

        // ─── Delegation tests ────────────────────────────────

        [Fact]
        public void LoadAllOrders_ReturnsEmpty_WhenNoOrders()
        {
            var orders = _vm.LoadAllOrders();
            Assert.Empty(orders);
        }

        [Fact]
        public void SaveAndDelete_Roundtrip()
        {
            var order = new OrderData { Id = Guid.NewGuid().ToString(), ClientName = "Test" };
            _vm.SaveOrder(order);
            _vm.DeleteOrder(order.Id);
            // After delete, loading all should not contain it
            // Note: need fresh VM or check via storage
            var freshVm = new OrdersHistoryViewModel();
            var all = freshVm.LoadAllOrders();
            Assert.DoesNotContain(all, o => o.Id == order.Id);
        }
    }
}
