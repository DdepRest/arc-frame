using System;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class PrintQueueManagerTests
    {
        [Fact]
        public void GetInstalledPrinterNames_DoesNotThrow()
        {
            var names = PrintQueueManager.GetInstalledPrinterNames();
            Assert.NotNull(names);
        }

        [Fact]
        public void GetDefaultPrinterName_DoesNotThrow()
        {
            var name = PrintQueueManager.GetDefaultPrinterName();
            // null is acceptable when no default printer exists
        }

        [Fact]
        public void ResolvePrintQueue_NullOrEmpty_ReturnsDefaultOrNull()
        {
            var queue = PrintQueueManager.ResolvePrintQueue(null);
            // Result depends on environment; just ensure no exception and name is non-empty when present.
            Assert.True(queue is null || !string.IsNullOrWhiteSpace(queue.Name));
        }

        [Fact]
        public void SendToQueue_ThrowsArgumentNullException_ForNullArguments()
        {
            Assert.Throws<ArgumentNullException>(() =>
                PrintQueueManager.SendToQueue(null!, "job", null!, null!));
        }

        [Fact]
        public void PrintResult_Ok_ReturnsSuccess()
        {
            var result = PrintResult.Ok();
            Assert.Equal(PrintResultType.Success, result.Type);
            Assert.False(result.IsRetryable);
            Assert.Equal("", result.UserMessage);
        }

        [Fact]
        public void PrintResultType_AllValues_Defined()
        {
            var values = Enum.GetValues<PrintResultType>().ToList();
            Assert.Contains(PrintResultType.Success, values);
            Assert.Contains(PrintResultType.PrinterOffline, values);
            Assert.Contains(PrintResultType.Unknown, values);
        }
    }
}
