using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.ViewModels;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// v3.45.0 (Phase 6 refactoring): orchestrates file-IO operations on
    /// orders — export the full list or a single order to JSON, import
    /// from JSON with multi-select dialog, and deep-clone for the
    /// «Копировать» action. Builds on <see cref="OrderStorageService"/>
    /// (raw IO) and <see cref="OrdersHistoryViewModel"/> (state layer).
    /// </summary>
    public class OrderImportExportService
    {
        private readonly OrdersHistoryViewModel _ordersVM;

        public OrderImportExportService(OrdersHistoryViewModel ordersVM)
        {
            _ordersVM = ordersVM ?? throw new ArgumentNullException(nameof(ordersVM));
        }

        // ─── Export ──────────────────────────────────────────

        /// <summary>
        /// Shows a SaveFileDialog and writes the supplied <paramref name="orders"/>
        /// to JSON. Shows status toast on every exit path. Returns <c>true</c>
        /// only if the file was actually written (caller can refresh the list
        /// in response).
        /// </summary>
        public bool ExportAllOrders(IReadOnlyList<OrderData>? orders, Window? owner = null)
        {
            if (orders == null || orders.Count == 0)
            {
                ToastService.ShowToast("Нет заказов для экспорта.", ToastType.Info);
                return false;
            }

            var dlg = new SaveFileDialog
            {
                Title = "Экспорт заказов",
                Filter = "JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*",
                DefaultExt = ".json",
                FileName = $"orders_{DateTime.Now:yyyy-MM-dd}.json"
            };
            if (dlg.ShowDialog(owner) != true) return false;

            try
            {
                _ordersVM.ExportOrders(orders.ToList(), dlg.FileName);
                ToastService.ShowToast($"Экспортировано {orders.Count} заказов.", ToastType.Success);
                return true;
            }
            catch (Exception ex)
            {
                ToastService.ShowToast($"Ошибка экспорта: {ex.Message}", ToastType.Error);
                return false;
            }
        }

        /// <summary>
        /// Single-order export with a smart default filename derived from
        /// the order's address (uppercased, sanitized) + contract number.
        /// </summary>
        public bool ExportSingleOrder(OrderData? order, Window? owner = null)
        {
            if (order == null) return false;
            var dlg = new SaveFileDialog
            {
                Title = "Экспорт заказа",
                Filter = "JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*",
                DefaultExt = ".json",
                FileName = BuildSingleOrderFileName(order)
            };
            if (dlg.ShowDialog(owner) != true) return false;

            try
            {
                _ordersVM.ExportOrders(new List<OrderData> { order }, dlg.FileName);
                ToastService.ShowToast($"Заказ {order.ContractNumber} экспортирован!", ToastType.Success);
                return true;
            }
            catch (Exception ex)
            {
                ToastService.ShowToast($"Ошибка экспорта: {ex.Message}", ToastType.Error);
                return false;
            }
        }

        /// <summary>
        /// Pure: builds the default filename for a single-order export.
        /// Address is sanitized (no invalid chars, trimmed, ≤60 chars),
        /// uppercased, single-spaced, then suffixed with the contract
        /// number. Returns «order {ContractNumber}.json» when the
        /// address is empty. <c>null</c> order returns
        /// <see cref="string.Empty"/> (defensive — callers normally
        /// always have a non-null order, but the test bundle asserts it).
        /// </summary>
        public static string BuildSingleOrderFileName(OrderData? order)
        {
            if (order == null) return string.Empty;
            string raw = (order.ClientAddress ?? string.Empty).Replace('/', ' ');
            string sanitized = SanitizeForFileName(raw);
            string address = string.IsNullOrEmpty(sanitized)
                ? string.Empty
                : string.Join(" ", sanitized.ToUpperInvariant()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return string.IsNullOrEmpty(address)
                ? $"order {order.ContractNumber}.json"
                : $"{address} {order.ContractNumber}.json";
        }

        private static string SanitizeForFileName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(input.Where(c => !invalid.Contains(c)).ToArray()).Trim();
            if (cleaned.Length > 60) cleaned = cleaned.Substring(0, 60).TrimEnd();
            return cleaned;
        }

        // ─── Import ──────────────────────────────────────────

        /// <summary>
        /// Asks the user to pick a JSON file, reads orders from it, and
        /// merges them into storage. Multiple-order files pop up an
        /// <see cref="ImportDialogWindow"/>. Returns <c>true</c> if any
        /// orders were actually imported (caller should then refresh the
        /// orders list); <c>false</c> on cancel, parse error, or empty
        /// selection. Shows toast on every exit path.
        /// </summary>
        public bool ImportOrders(Window? owner = null)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Импорт заказов",
                Filter = "JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*",
                DefaultExt = ".json",
                Multiselect = false
            };
            if (dlg.ShowDialog(owner) != true) return false;

            List<OrderData>? fileOrders;
            try
            {
                fileOrders = _ordersVM.ReadOrdersFromFile(dlg.FileName);
            }
            catch
            {
                ToastService.ShowToast("Не удалось прочитать файл. Проверьте формат.", ToastType.Error);
                return false;
            }

            if (fileOrders == null || fileOrders.Count == 0)
            {
                ToastService.ShowToast("Файл не содержит заказов.", ToastType.Info);
                return false;
            }

            List<OrderData> selected;
            if (fileOrders.Count == 1)
            {
                selected = fileOrders;
            }
            else
            {
                var importDlg = new ImportDialogWindow(fileOrders, owner!);
                importDlg.ShowDialog();
                if (!importDlg.DialogResultOk) return false;
                selected = importDlg.SelectedOrders ?? new List<OrderData>();
            }

            if (selected.Count == 0)
            {
                ToastService.ShowToast("Не выбрано ни одного заказа.", ToastType.Info);
                return false;
            }

            try
            {
                var imported = _ordersVM.MergeImport(selected);
                if (imported.Count > 0)
                    ToastService.ShowToast($"Импортировано {imported.Count} заказов.", ToastType.Success);
                else
                    ToastService.ShowToast("Все выбранные заказы уже существуют в актуальной версии.", ToastType.Info);
                return imported.Count > 0;
            }
            catch (Exception ex)
            {
                ToastService.ShowToast($"Ошибка импорта: {ex.Message}", ToastType.Error);
                return false;
            }
        }

        // ─── Deep clone (copy) ──────────────────────────────

        /// <summary>
        /// Deep-clones <paramref name="source"/>, mutates identity fields
        /// (Id, CreatedAt, UpdatedAt, Status, ContractNumber) and saves the
        /// result. Returns the new contract number on success, or
        /// <c>null</c> on null source / deserialize failure.
        /// </summary>
        public string? CopyOrder(OrderData? source)
        {
            if (source == null) return null;
            var copy = DeepCloneOrder(source);
            if (copy == null) return null;

            copy.Id = Guid.NewGuid().ToString();
            copy.CreatedAt = DateTime.Now;
            copy.UpdatedAt = DateTime.Now;
            copy.Status = OrderStatuses.All[0]; // «Новый»
            copy.ContractNumber = _ordersVM.GenerateCopyContractNumber(source.ContractNumber);

            _ordersVM.SaveOrder(copy);
            return copy.ContractNumber;
        }

        /// <summary>
        /// Pure: round-trips an order through JSON using the shared
        /// <see cref="OrderStorageService.JsonOptions"/> to produce an
        /// independent deep copy with no shared mutable references.
        /// Does NOT mutate identity — <see cref="CopyOrder"/> does that
        /// explicitly so the contract number generator continues to see
        /// the original (pre-mutation) state. <c>null</c> in → <c>null</c>
        /// out (defensive — callers normally already null-check the
        /// source, but leaking null through is safer than NRE-throwing).
        /// </summary>
        public static OrderData? DeepCloneOrder(OrderData? source)
        {
            if (source == null) return null;
            string json = System.Text.Json.JsonSerializer.Serialize(source, OrderStorageService.JsonOptions);
            return System.Text.Json.JsonSerializer.Deserialize<OrderData>(json, OrderStorageService.JsonOptions);
        }
    }
}
