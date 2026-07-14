using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    [Collection("FileSystem")]
    public class PriceServiceTests : IDisposable
    {
        private readonly PriceService _service = new();
        private readonly string _pricesPath;
        private readonly string _originalPricesPath;

        // File-IO isolation: PriceService.PricesPath is a static property
        // in the production type. Without the redirect, LoadPrices reads
        // (and SavePrices writes) %AppData%\MosquitoNetCalculator\prices.json
        // — the user's real price catalog. Tests asserting
        // "File.Exists(BaseDirectory/prices.json)" would silently pass
        // because the AppData file exists, or fail because the
        // BaseDirectory file was never created. We snapshot the original,
        // redirect to a per-test path, and restore on Dispose so this
        // test class doesn't pollute production data and other test
        // classes (ManualChecklistTests, etc.) see the original path.
        // Same pattern as ManualChecklistTests.

        public PriceServiceTests()
        {
            _originalPricesPath = PriceService.PricesPath;

            _pricesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prices.json");
            if (File.Exists(_pricesPath))
                File.Delete(_pricesPath);

            PriceService.PricesPath = _pricesPath;
        }

        public void Dispose()
        {
            PriceService.PricesPath = _originalPricesPath;
            if (File.Exists(_pricesPath))
                File.Delete(_pricesPath);
        }

        [Fact]
        public void LoadPrices_ReturnsDefaults_WhenNoFileExists()
        {
            var prices = _service.LoadPrices();
            Assert.NotEmpty(prices);
            Assert.Contains(prices, p => p.Name == "Anwis" && p.Color == "Белый");
            Assert.Contains(prices, p => p.Name == "ПСУЛ");
            Assert.Contains(prices, p => p.Name == "Уплотнение");
        }

        [Fact]
        public void LoadPrices_SavesDefaults_WhenNoFileExists()
        {
            Assert.False(File.Exists(_pricesPath));
            _service.LoadPrices();
            Assert.True(File.Exists(_pricesPath));
        }

        [Fact]
        public void LoadPrices_MigratesAnvis_ToAnwis()
        {
            var oldPrices = new List<PriceItem>
            {
                new() { Name = "Anvis", Color = "Белый", Price = 1800 }
            };
            File.WriteAllText(_pricesPath, JsonSerializer.Serialize(oldPrices));

            var prices = _service.LoadPrices();
            Assert.Contains(prices, p => p.Name == "Anwis" && p.Color == "Белый");
            Assert.DoesNotContain(prices, p => p.Name == "Anvis");
        }

        [Fact]
        public void LoadPrices_MigratesOldWindowProductName()
        {
            var oldPrices = new List<PriceItem>
            {
                new() { Name = "Оконная на мет. крепл.", Color = "Белый", Price = 3200 }
            };
            File.WriteAllText(_pricesPath, JsonSerializer.Serialize(oldPrices));

            var prices = _service.LoadPrices();
            Assert.Contains(prices, p => p.Name == "Оконная на метал. крепл.");
            Assert.DoesNotContain(prices, p => p.Name == "Оконная на мет. крепл.");
        }

        [Fact]
        public void LoadPrices_MigratesNavesyBrownPrice()
        {
            var oldPrices = new List<PriceItem>
            {
                new() { Name = "На навесах", Color = "Коричневый", Price = 2900 }
            };
            File.WriteAllText(_pricesPath, JsonSerializer.Serialize(oldPrices));

            var prices = _service.LoadPrices();
            var item = prices.Find(p => p.Name == "На навесах" && p.Color == "Коричневый");
            Assert.NotNull(item);
            Assert.Equal(3000, item!.Price);
        }

        [Fact]
        public void LoadPrices_RemovesAntratsit_ForUnsupportedProducts()
        {
            var oldPrices = new List<PriceItem>
            {
                new() { Name = "Anwis", Color = "Белый", Price = 1800 },
                new() { Name = "Anwis", Color = "Антрацит", Price = 2000 },
                new() { Name = "На навесах", Color = "Антрацит", Price = 3100 }
            };
            File.WriteAllText(_pricesPath, JsonSerializer.Serialize(oldPrices));

            var prices = _service.LoadPrices();
            Assert.DoesNotContain(prices, p => p.Name == "Anwis" && p.Color == "Антрацит");
            Assert.DoesNotContain(prices, p => p.Name == "На навесах" && p.Color == "Антрацит");
        }

        [Fact]
        public void LoadPrices_KeepsAntratsit_ForOtliv()
        {
            var oldPrices = new List<PriceItem>
            {
                new() { Name = "Отлив", Color = "Антрацит", Price = 2150 }
            };
            File.WriteAllText(_pricesPath, JsonSerializer.Serialize(oldPrices));

            var prices = _service.LoadPrices();
            Assert.Contains(prices, p => p.Name == "Отлив" && p.Color == "Антрацит");
        }

        [Fact]
        public void LoadPrices_AddsMissingDefaultProducts()
        {
            var partialPrices = new List<PriceItem>
            {
                new() { Name = "Anwis", Color = "Белый", Price = 1800 }
            };
            File.WriteAllText(_pricesPath, JsonSerializer.Serialize(partialPrices));

            var prices = _service.LoadPrices();
            Assert.Contains(prices, p => p.Name == "ПСУЛ");
            Assert.Contains(prices, p => p.Name == "Отлив");
            Assert.Contains(prices, p => p.Name == "Уплотнение");
            Assert.Contains(prices, p => p.Name == "Дверная сетка");
        }

        [Fact]
        public void LoadPrices_HandlesCorruptedFile()
        {
            File.WriteAllText(_pricesPath, "not valid json{{{");
            var prices = _service.LoadPrices();
            Assert.NotEmpty(prices); // Falls back to defaults
        }

        [Fact]
        public void LoadPrices_HandlesEmptyArray()
        {
            File.WriteAllText(_pricesPath, "[]");
            var prices = _service.LoadPrices();
            Assert.NotEmpty(prices); // Falls back to defaults (count == 0)
        }

        [Fact]
        public void GetPrice_ExactMatch()
        {
            var prices = _service.LoadPrices();
            double price = _service.GetPrice(prices, "Anwis", "Белый");
            Assert.Equal(1800, price);
        }

        [Fact]
        public void GetPrice_ExactMatch_Brown()
        {
            var prices = _service.LoadPrices();
            double price = _service.GetPrice(prices, "Anwis", "Коричневый");
            Assert.Equal(1900, price);
        }

        [Fact]
        public void GetPrice_FallbackToEmptyColor()
        {
            var prices = _service.LoadPrices();
            // ПСУЛ has empty color in defaults, so any color should fall back
            double price = _service.GetPrice(prices, "ПСУЛ", "anything");
            Assert.Equal(100, price);
        }

        [Fact]
        public void GetPrice_ReturnsZero_ForUnknownProduct()
        {
            var prices = _service.LoadPrices();
            double price = _service.GetPrice(prices, "NonExistent", "Белый");
            Assert.Equal(0, price);
        }

        [Fact]
        public void GetPrice_ReturnsZero_ForUnknownColor()
        {
            var prices = _service.LoadPrices();
            double price = _service.GetPrice(prices, "Anwis", "Розовый");
            Assert.Equal(0, price);
        }

        [Fact]
        public void GetProductNames_ReturnsDistinct()
        {
            var prices = _service.LoadPrices();
            var names = _service.GetProductNames(prices);
            Assert.Equal(names.Count, names.Distinct().Count());
            Assert.Contains("Anwis", names);
            Assert.Contains("ПСУЛ", names);
        }

        [Fact]
        public void GetColorsForProduct_ReturnsColorsForOtliv()
        {
            var prices = _service.LoadPrices();
            var colors = _service.GetColorsForProduct(prices, "Отлив");
            Assert.Contains("Белый", colors);
            Assert.Contains("Коричневый", colors);
            Assert.Contains("Антрацит", colors);
            Assert.Contains("Золотой дуб", colors);
            Assert.Equal(4, colors.Count);
        }

        [Fact]
        public void GetColorsForProduct_ReturnsTwoColors_ForAnwis()
        {
            var prices = _service.LoadPrices();
            var colors = _service.GetColorsForProduct(prices, "Anwis");
            Assert.Contains("Белый", colors);
            Assert.Contains("Коричневый", colors);
            Assert.Equal(2, colors.Count);
        }

        [Fact]
        public void GetColorsForProduct_ReturnsEmpty_ForColorlessProduct()
        {
            var prices = _service.LoadPrices();
            var colors = _service.GetColorsForProduct(prices, "ПСУЛ");
            Assert.Empty(colors);
        }

        [Fact]
        public void SavePrices_AndLoad_Roundtrip()
        {
            var prices = new List<PriceItem>
            {
                new() { Name = "TestProduct", Color = "Red", Price = 42 }
            };
            _service.SavePrices(prices);

            var loaded = _service.LoadPrices();
            Assert.Contains(loaded, p => p.Name == "TestProduct" && p.Color == "Red" && p.Price == 42);
        }

        // ─── Bug #5 fix regression: comprehensive migration test ──────
        // After the v3.22.0 consolidation of the 4 migration loops into
        // a single ApplyMigrations method, this end-to-end test loads a
        // file that triggers ALL 4 migrations in one go and asserts
        // each one is applied. Guards against the consolidation
        // accidentally dropping or reordering a migration.

        [Fact]
        public void LoadPrices_AppliesAllMigrations_InOnePass()
        {
            // Fixture crafted to trigger every migration step:
            //   1. "Anvis" typo → "Anwis"
            //   2. "Оконная на мет. крепл." → "Оконная на метал. крепл."
            //   3. "На навесах — Коричневый" 2900 → 3000
            //   4. Антрацит removed for Anwis/На навесах/Оконная
            //   5. Missing default products added (e.g. ПСУЛ, Отлив)
            // Keep the file minimal: just enough to trigger each step.
            var legacy = new List<PriceItem>
            {
                new() { Name = "Anvis", Color = "Белый", Price = 1800 },                         // → 1
                new() { Name = "Оконная на мет. крепл.", Color = "Белый", Price = 3200 },         // → 2
                new() { Name = "На навесах", Color = "Коричневый", Price = 2900 },                 // → 3
                new() { Name = "Anwis", Color = "Антрацит", Price = 2000 },                       // → 4 removed
                new() { Name = "На навесах", Color = "Антрацит", Price = 3100 },                  // → 4 removed
                // ПСУЛ, Отлив, Уплотнение etc. are intentionally missing → 5
            };
            File.WriteAllText(_pricesPath, JsonSerializer.Serialize(legacy));

            var prices = _service.LoadPrices();

            // Migration 1: Anvis → Anwis
            Assert.Contains(prices, p => p.Name == "Anwis" && p.Color == "Белый" && p.Price == 1800);
            Assert.DoesNotContain(prices, p => p.Name == "Anvis");

            // Migration 2: Оконная на мет. → Оконная на метал.
            Assert.Contains(prices, p => p.Name == "Оконная на метал. крепл." && p.Color == "Белый" && p.Price == 3200);
            Assert.DoesNotContain(prices, p => p.Name == "Оконная на мет. крепл.");

            // Migration 3: На навесах Коричневый 2900 → 3000
            var brownNavesy = prices.Find(p => p.Name == "На навесах" && p.Color == "Коричневый");
            Assert.NotNull(brownNavesy);
            Assert.Equal(3000, brownNavesy!.Price);

            // Migration 4: Антрацит removed for Anwis/На навесах/Оконная
            Assert.DoesNotContain(prices, p => p.Name == "Anwis" && p.Color == "Антрацит");
            Assert.DoesNotContain(prices, p => p.Name == "На навесах" && p.Color == "Антрацит");
            Assert.DoesNotContain(prices, p => p.Name == "Оконная на метал. крепл." && p.Color == "Антрацит");
            // ...but preserved for products that still offer it
            Assert.Contains(prices, p => p.Name == "Отлив" && p.Color == "Антрацит");

            // Migration 5: missing default products added
            Assert.Contains(prices, p => p.Name == "ПСУЛ");
            Assert.Contains(prices, p => p.Name == "Отлив");
            Assert.Contains(prices, p => p.Name == "Уплотнение");
            Assert.Contains(prices, p => p.Name == "Козырёк");
            Assert.Contains(prices, p => p.Name == "Короб");
            Assert.Contains(prices, p => p.Name == "Дверная сетка");
        }
    }
}
