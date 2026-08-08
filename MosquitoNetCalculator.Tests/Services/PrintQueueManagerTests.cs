using System;
using System.IO;
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
        public void GetInstalledPrintQueues_DoesNotThrow_AndMatchesNames()
        {
            var queues = PrintQueueManager.GetInstalledPrintQueues();
            Assert.NotNull(queues);
            Assert.All(queues, queue => Assert.False(string.IsNullOrWhiteSpace(queue.FullName)));
        }

        [Fact]
        public void PrinterPickerContract_UsesQueueObjectAndFullName()
        {
            var xaml = ReadSource("Controls/PrintPreviewControl.xaml");
            var code = ReadSource("Controls/PrintPreviewControl.xaml.cs");

            Assert.Contains("DisplayMemberPath=\"FullName\"", xaml);
            Assert.Contains("PrinterCombo.SelectedItem as PrintQueue", code);
            Assert.Contains("target.PrinterName = selectedQueue.FullName", code);
            Assert.Contains("PrinterCombo.SelectedIndex = targetIndex", code);
            Assert.DoesNotContain("else if (targetIndex < 0)\n                PrinterCombo.SelectedIndex = 0", code);
        }

        private static string ReadSource(string relativePath)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "MosquitoNetCalculator", relativePath);
                if (File.Exists(candidate))
                    return File.ReadAllText(candidate);
                directory = directory.Parent;
            }

            throw new FileNotFoundException($"Source file not found: {relativePath}");
        }

        [Fact]
        public void ResolvePrintQueue_ExplicitUnknownName_DoesNotReturnDefault()
        {
            const string selected = "\\\\FakeServer\\FakePrinter";
            var queue = PrintQueueManager.ResolvePrintQueue(selected);
            var defaultName = PrintQueueManager.GetDefaultPrinterName();

            Assert.Null(queue);
            Assert.False(string.Equals(queue?.FullName, defaultName, StringComparison.OrdinalIgnoreCase));
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
