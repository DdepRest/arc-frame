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

        // ──── Path safety (CWE-22) ────

        /// <summary>
        /// Canonicalizes an order id to the «D» GUID form used for file names.
        /// Single source of truth for the id rule, shared by
        /// <see cref="ResolveOrderFilePath"/> (throws on a bad id) and
        /// <see cref="LoadAllOrders"/> (skips such a file).
        /// </summary>
        private static bool TryCanonicalizeOrderId(string? orderId, out string canonicalId)
        {
            canonicalId = string.Empty;
            if (string.IsNullOrWhiteSpace(orderId) || !Guid.TryParse(orderId, out var parsedId))
                return false;

            canonicalId = parsedId.ToString("D");
            return true;
        }

        /// <summary>
        /// Maps an order id to its file under <see cref="OrdersDir"/> and
        /// guarantees the result stays inside that directory.
        /// <para>
        /// Ids must be GUIDs. Every production writer creates them with
        /// <c>Guid.NewGuid().ToString()</c> (canonical «D» form), so a
        /// non-GUID id can only arrive from hand-edited or hostile import
        /// JSON — where arbitrary text would allow separators, «..», NTFS
        /// alternate-data-stream names («a.json:stream») and reserved device
        /// names. Re-formatting the parsed GUID also unifies non-canonical
        /// spellings (uppercase, braces, «N»/«P» forms) onto one file.
        /// </para>
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// The id is empty / not a GUID, or the resolved path would escape
        /// <see cref="OrdersDir"/>.
        /// </exception>
        private static string ResolveOrderFilePath(string? orderId)
        {
            if (!TryCanonicalizeOrderId(orderId, out string canonicalId))
                throw new InvalidDataException($"Некорректный идентификатор заказа: «{orderId}».");

            string fileName = canonicalId + ".json";

            // OrdersDir is a mutable static (tests redirect it) and may be
            // relative — resolve it against the current directory first, then
            // drop trailing separators so the containment check below can
            // append exactly one.
            string directoryResolved = Path.GetFullPath(OrdersDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string filePath = Path.GetFullPath(Path.Combine(directoryResolved, fileName));

            // Defence in depth: a canonical GUID cannot contain separators, so
            // this cannot trigger today. Keep it so a future change to the
            // canonicalisation above can't silently reopen traversal.
            string directoryWithSeparator = directoryResolved + Path.DirectorySeparatorChar;
            if (!filePath.StartsWith(directoryWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Путь к файлу заказа выходит за пределы каталога заказов: «{orderId}».");

            return filePath;
        }

        public string SaveOrder(OrderData order)
        {
            // Validate + resolve the id BEFORE any mutation or IO, so a
            // rejected id leaves both the order object and the disk untouched.
            string filePath = ResolveOrderFilePath(order.Id);
            order.UpdatedAt = DateTime.Now;
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
            string filePath = ResolveOrderFilePath(orderId);
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
                        if (order == null) continue;

                        // Only list the orders LoadOrder / DeleteOrder can actually
                        // reach: both build the path from the id via
                        // ResolveOrderFilePath, so a file whose id is not a GUID —
                        // or whose id does not match its own file name (a hand-
                        // renamed copy) — would appear in the history yet never
                        // open or delete. Same policy as corrupted JSON below:
                        // skip it, log it, leave the file on disk.
                        if (!TryCanonicalizeOrderId(order.Id, out string canonicalId))
                        {
                            System.Diagnostics.Debug.WriteLine($"[OrderStorage] skip invalid id: {file}");
                            continue;
                        }
                        if (!string.Equals(canonicalId, Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase))
                        {
                            System.Diagnostics.Debug.WriteLine($"[OrderStorage] skip id/filename mismatch: {file}");
                            continue;
                        }

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
            string filePath = ResolveOrderFilePath(orderId);
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

        /// <summary>
        /// Generates a contract number for a copied order.
        /// Always starts from the base number: "2-8" → "2-8.1",
        /// "2-8.1" → "2-8.2", "2-8.5.3" → "2-8.6".
        /// Strips any existing suffix before scanning for the next free index.
        /// </summary>
        public string GenerateCopyContractNumber(string sourceNumber)
        {
            string baseNumber = (sourceNumber ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(baseNumber))
                baseNumber = "1";

            // Strip existing suffix to get the true base: "2-8.1" → "2-8"
            int dotIndex = baseNumber.IndexOf('.');
            if (dotIndex >= 0)
                baseNumber = baseNumber.Substring(0, dotIndex).Trim();

            // Load all orders to find max suffix for this base
            var allOrders = LoadAllOrders();
            int maxSuffix = 0;
            string prefix = baseNumber + ".";

            foreach (var o in allOrders)
            {
                if (o.ContractNumber != null && o.ContractNumber.StartsWith(prefix))
                {
                    string suffixPart = o.ContractNumber.Substring(prefix.Length);
                    if (int.TryParse(suffixPart, out int s) && s > maxSuffix)
                        maxSuffix = s;
                }
            }

            return baseNumber + "." + (maxSuffix + 1);
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
