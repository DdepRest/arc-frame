using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.ViewModels;
using Xunit;

namespace MosquitoNetCalculator.Tests.ViewModels
{
    /// <summary>
    /// Regression test for the "сброс цен не работает" bug.
    /// Root cause: <see cref="PricesViewModel.ResetPrices"/> was deleting
    /// <c>prices.json</c> from <c>AppDomain.CurrentDomain.BaseDirectory</c>
    /// (the install folder), while <see cref="PriceService"/> reads/writes
    /// from <c>%AppData%\MosquitoNetCalculator\prices.json</c>. After reset,
    /// <see cref="PriceService.LoadPrices"/> would re-read the user's stale
    /// edits from AppData and the UI would appear unchanged.
    /// </summary>
    [Collection("FileSystem")]
    public class PricesViewModelResetTests : IDisposable
    {
        private readonly string _tempPricesPath;
        private readonly string _originalPricesPath;

        public PricesViewModelResetTests()
        {
            // Same redirect-and-restore pattern as PriceServiceTests.
            _originalPricesPath = PriceService.PricesPath;

            _tempPricesPath = Path.Combine(
                Path.GetTempPath(),
                $"prices-{Guid.NewGuid():N}.json");
            PriceService.PricesPath = _tempPricesPath;

            // Clean up anything left over from a previous failed run.
            if (File.Exists(_tempPricesPath))
                File.Delete(_tempPricesPath);
        }

        public void Dispose()
        {
            PriceService.PricesPath = _originalPricesPath;
            if (File.Exists(_tempPricesPath))
                File.Delete(_tempPricesPath);
        }

        [Fact]
        public void ResetPrices_DeletesSentinelFile_AndReloadsDefaults()
        {
            // Sentinel: a non-default price entry that would survive a load.
            // If ResetPrices still deletes the wrong path, LoadPrices will
            // return this sentinel (with migrations) and the test will fail.
            var sentinel = new[]
            {
                new PriceItem { Name = "SentinelProduct", Color = "Розовый", Price = 999.99 }
            };
            File.WriteAllText(_tempPricesPath, JsonSerializer.Serialize(sentinel));

            var vm = new PricesViewModel();
            vm.LoadPrices(); // sanity: sentinel is loaded before reset
            Assert.Contains(vm.Prices, p => p.Name == "SentinelProduct");

            vm.ResetPrices();

            // After reset: sentinel must be gone, defaults restored.
            Assert.DoesNotContain(vm.Prices, p => p.Name == "SentinelProduct");
            Assert.Contains(vm.Prices, p => p.Name == "Anwis" && p.Color == "Белый" && p.Price == 1800);
            Assert.Contains(vm.Prices, p => p.Name == "ПСУЛ");
            Assert.Contains(vm.Prices, p => p.Name == "Уплотнение");
        }

        [Fact]
        public void ResetPrices_DoesNotTouchBaseDirectoryFile()
        {
            // Inverse guard: even if someone accidentally puts a prices.json
            // in BaseDirectory, ResetPrices must NOT delete it (it lives in AppData).
            var baseDirFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prices.json");
            if (File.Exists(baseDirFile))
                File.Delete(baseDirFile);
            File.WriteAllText(baseDirFile, "{\"sentinel\":\"base-dir\"}");

            try
            {
                var vm = new PricesViewModel();
                vm.ResetPrices();

                // BaseDirectory file must still exist — ResetPrices must ignore it.
                Assert.True(File.Exists(baseDirFile),
                    "ResetPrices must not delete prices.json from BaseDirectory " +
                    "(that path is wrong; the real file lives in AppData).");
            }
            finally
            {
                if (File.Exists(baseDirFile))
                    File.Delete(baseDirFile);
            }
        }

        [Fact]
        public void ResetPrices_RepositoryFileIsRecreatedWithDefaults()
        {
            // After reset, PriceService.LoadPrices() must have re-saved defaults
            // at PricesPath (the AppData file). The temp file should now contain
            // a real catalog, not the stale sentinel.
            var sentinel = new[]
            {
                new PriceItem { Name = "SentinelProduct", Color = "#FF00FF", Price = 1.0 }
            };
            File.WriteAllText(_tempPricesPath, JsonSerializer.Serialize(sentinel));

            var vm = new PricesViewModel();
            vm.ResetPrices();

            Assert.True(File.Exists(_tempPricesPath),
                "ResetPrices via LoadPrices must recreate the file with defaults.");
            var json = File.ReadAllText(_tempPricesPath);
            Assert.DoesNotContain("SentinelProduct", json);
            Assert.Contains("Anwis", json);
        }
    }
}
