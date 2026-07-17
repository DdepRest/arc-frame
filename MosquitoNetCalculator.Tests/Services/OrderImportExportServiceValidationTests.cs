using System;
using System.Collections.Generic;
using System.IO;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.ViewModels;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Parameter-validation tests for <see cref="OrderImportExportService"/>.
    /// These exercise the early-return paths BEFORE any SaveFileDialog /
    /// OpenFileDialog / IO — so they're fast and reliable, no STA host.
    /// Belongs to <c>FileSystem</c> collection because they instantiate
    /// <see cref="OrdersHistoryViewModel"/> (which constructs an
    /// <see cref="OrderStorageService"/> and redirects <c>OrdersDir</c>).
    /// </summary>
    [Collection("FileSystem")]
    public class OrderImportExportServiceValidationTests : IDisposable
    {
        private readonly string _originalOrdersDir;
        private readonly string _testOrdersDir;
        private readonly OrderImportExportService _service;

        public OrderImportExportServiceValidationTests()
        {
            _originalOrdersDir = OrderStorageService.OrdersDir;
            _testOrdersDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "orders-validation");
            if (Directory.Exists(_testOrdersDir))
                Directory.Delete(_testOrdersDir, true);
            // Don't pre-create the dir — let OrderStorageService's ctor
            // do it. Verifies it works on a fresh path too.
            OrderStorageService.OrdersDir = _testOrdersDir;
            _service = new OrderImportExportService(new OrdersHistoryViewModel());
        }

        public void Dispose()
        {
            OrderStorageService.OrdersDir = _originalOrdersDir;
            if (Directory.Exists(_testOrdersDir))
                Directory.Delete(_testOrdersDir, true);
        }

        // ─── ExportAllOrders ─────────────────────────────────

        [Fact]
        public void ExportAllOrders_NullOrders_ReturnsFalse_NoDialogOpen()
        {
            // We can't actually assert no-dialog-open (it would mean
            // headlessly clicking through WPF), but the early-return
            // path guarantees ShowDialog is never reached. Pass null
            // for owner — it is unused on this early-return path,
            // and WPF Window ctor requires STA, which xUnit doesn't use.
            var result = _service.ExportAllOrders(null, null);
            Assert.False(result);
        }

        [Fact]
        public void ExportAllOrders_EmptyList_ReturnsFalse()
        {
            var result = _service.ExportAllOrders(new List<OrderData>(), null);
            Assert.False(result);
        }

        // ─── ExportSingleOrder ───────────────────────────────

        [Fact]
        public void ExportSingleOrder_NullOrder_ReturnsFalse()
        {
            var result = _service.ExportSingleOrder(null, null);
            Assert.False(result);
        }

        // ─── CopyOrder (null source) ─────────────────────────

        [Fact]
        public void CopyOrder_NullSource_ReturnsNull()
        {
            // CopyOrder mirrors DeepCloneOrder's null contract: null in
            // → null out (no Id mutation, no save).
            var result = _service.CopyOrder(null);
            Assert.Null(result);
        }
    }
}
