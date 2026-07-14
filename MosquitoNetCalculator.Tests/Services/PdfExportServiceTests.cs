using System;
using System.Collections.Generic;
using System.IO;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class PdfExportServiceTests
    {
        [Fact]
        public void Export_ThrowsArgumentException_WhenFilePathIsEmpty()
        {
            var service = new PdfExportService();
            Assert.Throws<ArgumentException>(() =>
                service.Export("", new List<OrderItem>(), new ClientInfo(), 0, ""));
        }

        [Fact]
        public void Export_CreatesPdfFile_WithValidItems()
        {
            var path = Path.Combine(Path.GetTempPath(), $"arc-test-{Guid.NewGuid()}.pdf");
            try
            {
                var service = new PdfExportService();
                var items = new List<OrderItem>
                {
                    new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1800 }
                };
                service.Export(path, items, new ClientInfo { ContractNumber = "1-1" }, 1800, "");
                Assert.True(File.Exists(path));
                var fileInfo = new FileInfo(path);
                Assert.True(fileInfo.Length > 0);
                var header = File.ReadAllBytes(path).AsSpan(0, Math.Min(4, (int)fileInfo.Length));
                Assert.True(header.SequenceEqual(new byte[] { 0x25, 0x50, 0x44, 0x46 }));
            }
            finally
            {
                try { File.Delete(path); } catch { /* ignore cleanup errors */ }
            }
        }
    }
}
