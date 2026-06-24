using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.ViewModels
{
    public class OrdersHistoryViewModel
    {
        private readonly OrderStorageService _orderStorage = new();

        public List<OrderData> LoadAllOrders() => _orderStorage.LoadAllOrders();

        public void DeleteOrder(string orderId) => _orderStorage.DeleteOrder(orderId);

        public void SaveOrder(OrderData order) => _orderStorage.SaveOrder(order);

        public string GenerateContractNumber(string prefix) => _orderStorage.GenerateContractNumber(prefix);

        public string GenerateCopyContractNumber(string sourceNumber) => _orderStorage.GenerateCopyContractNumber(sourceNumber);

        public void ExportOrders(List<OrderData> orders, string filePath) =>
            _orderStorage.ExportOrders(orders, filePath);

        /// <summary>
        /// Reads orders from a JSON file.
        /// Returns the deserialized list or null on error.
        /// </summary>
        public List<OrderData>? ReadOrdersFromFile(string filePath)
        {
            var json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            // Use the same JsonOptions as the write path (ExportOrders / SaveOrder)
            // so read and write stay symmetric. If write options are ever extended
            // (e.g. PropertyNamingPolicy, NumberHandling), the import path picks
            // up the change automatically — no risk of one-sided drift.
            return JsonSerializer.Deserialize<List<OrderData>>(json, OrderStorageService.JsonOptions);
        }

        /// <summary>
        /// Merges file orders into storage: skips duplicates (by Id) unless the
        /// file version is newer. Returns the list of actually imported orders.
        /// </summary>
        public List<OrderData> MergeImport(List<OrderData> fileOrders)
        {
            var imported = new List<OrderData>();
            var existing = _orderStorage.LoadAllOrders().ToDictionary(o => o.Id);

            foreach (var order in fileOrders)
            {
                if (existing.TryGetValue(order.Id, out var existingOrder))
                {
                    if (order.UpdatedAt > existingOrder.UpdatedAt)
                    {
                        _orderStorage.SaveOrder(order);
                        imported.Add(order);
                    }
                }
                else
                {
                    _orderStorage.SaveOrder(order);
                    imported.Add(order);
                }
            }

            return imported;
        }

        public string SanitizeFileName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(input.Where(c => !invalid.Contains(c)).ToArray()).Trim();
            if (sanitized.Length > 60) sanitized = sanitized.Substring(0, 60).TrimEnd();
            return sanitized;
        }
    }
}
