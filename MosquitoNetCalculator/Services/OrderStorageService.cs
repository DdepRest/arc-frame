using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    public class OrderStorageService
    {
        // Data lives in %AppData%\MosquitoNetCalculator\, NOT in the app directory.
        // App updates may replace the install directory, so any
        // user data stored alongside the .exe gets lost on every update.
        // %AppData% is persistent across updates.
        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MosquitoNetCalculator");
        // Mutable (NOT readonly) so test code can redirect to a temp directory
        // per-test. See AppSettingsService.SettingsPath comment for the rationale —
        // .NET 8 throws FieldAccessException on FieldInfo.SetValue against initonly.
        public static string OrdersDir { get; set; } = Path.Combine(AppDataDir, "orders");
        private static readonly string CounterPath = Path.Combine(AppDataDir, "order_counter.json");

        // Shared JSON options for ALL order read AND write paths.
        // `internal` (not `private`) so the VM's import path can reuse
        // the same options — keeps read/write perfectly symmetric. If
        // write options are ever extended (e.g. PropertyNamingPolicy,
        // NumberHandling), every read site automatically picks it up
        // without one-sided drift.
        internal static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly object _cacheLock = new();
        private List<OrderData>? _cachedOrders;

        public OrderStorageService()
        {
            if (!Directory.Exists(OrdersDir))
                Directory.CreateDirectory(OrdersDir);
        }

        public string SaveOrder(OrderData order)
        {
            order.UpdatedAt = DateTime.Now;
            string filePath = Path.Combine(OrdersDir, $"{order.Id}.json");
            string json = JsonSerializer.Serialize(order, JsonOptions);
            // File IO is performed under the cache lock so a concurrent LoadAllOrders
            // can't read files, then have its result overwritten by a SaveOrder that
            // completes before the load assigns the cache. (Previously: SaveOrder
            // wrote the file OUTSIDE the lock, then nulled the cache — a concurrent
            // LoadAllOrders could finish reading the old files, then clobber the
            // cache with a stale list after SaveOrder had already invalidated it.)
            lock (_cacheLock)
            {
                File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
                _cachedOrders = null;
            }
            return filePath;
        }

        public OrderData? LoadOrder(string orderId)
        {
            string filePath = Path.Combine(OrdersDir, $"{orderId}.json");
            if (!File.Exists(filePath)) return null;

            string json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            // Use the shared JsonOptions for symmetry with SaveOrder / ExportOrders
            // — if a future change adds read-affecting options (e.g. PropertyNamingPolicy),
            // every read site picks it up automatically.
            return JsonSerializer.Deserialize<OrderData>(json, JsonOptions);
        }

        public List<OrderData> LoadAllOrders()
        {
            // Entire load+cache-assign happens under the lock so SaveOrder / DeleteOrder
            // can't interleave between "read files" and "assign cache" with stale data.
            // This serialises all order operations; acceptable for a single-user desktop
            // app where contention is rare and human-paced.
            lock (_cacheLock)
            {
                if (_cachedOrders != null) return _cachedOrders;

                var orders = new List<OrderData>();
                if (!Directory.Exists(OrdersDir))
                {
                    _cachedOrders = orders;
                    return _cachedOrders;
                }

                foreach (var file in Directory.GetFiles(OrdersDir, "*.json"))
                {
                    try
                    {
                        string json = File.ReadAllText(file, System.Text.Encoding.UTF8);
                        // Symmetric with the write path — see JsonOptions comment.
                        var order = JsonSerializer.Deserialize<OrderData>(json, JsonOptions);
                        if (order != null)
                            orders.Add(order);
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[OrderStorage] skip corrupted: {file} — {ex.Message}"); }
                }

                _cachedOrders = orders.OrderByDescending(o => o.UpdatedAt).ToList();
                return _cachedOrders;
            }
        }

        public void DeleteOrder(string orderId)
        {
            string filePath = Path.Combine(OrdersDir, $"{orderId}.json");
            // Same lock-during-IO rationale as SaveOrder: invalidate the cache atomically
            // with the file deletion so a concurrent LoadAllOrders can't return a list
            // that still contains the just-deleted order.
            lock (_cacheLock)
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
                _cachedOrders = null;
            }
        }

        // ──── Auto-increment contract number ────

        public int GetNextOrderNumber(string prefix)
        {
            var orders = LoadAllOrders();
            int maxNum = 0;

            foreach (var o in orders)
            {
                // Parse "1-5" → prefix=1, num=5
                if (!string.IsNullOrEmpty(o.ContractNumber) && o.ContractNumber.Contains('-'))
                {
                    var parts = o.ContractNumber.Split('-', 2);
                    if (parts.Length >= 2 && parts[0].Trim() == prefix && int.TryParse(parts[1].Trim(), out int num))
                    {
                        if (num > maxNum) maxNum = num;
                    }
                }
            }

            return maxNum + 1;
        }

        public string GenerateContractNumber(string prefix)
        {
            int next = GetNextOrderNumber(prefix);
            return $"{prefix}-{next}";
        }

        // ──── Export ────

        public string ExportOrders(List<OrderData> orders, string filePath)
        {
            try
            {
                string json = JsonSerializer.Serialize(orders, JsonOptions);
                File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderStorage] export failed: {ex.Message}");
                throw new InvalidOperationException($"Не удалось экспортировать заказы: {ex.Message}", ex);
            }
            return filePath;
        }
    }
}
