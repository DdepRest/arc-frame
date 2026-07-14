using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    public class PriceService
    {
        // Data lives in %AppData%\MosquitoNetCalculator\, NOT in the app directory.
        // App updates may replace the install directory — price edits must survive.
        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MosquitoNetCalculator");
        // Mutable (NOT readonly) so test code can redirect to a temp directory
        // per-test. See AppSettingsService.SettingsPath comment for the rationale —
        // .NET 8 throws FieldAccessException on FieldInfo.SetValue against initonly.
        public static string PricesPath { get; set; } = Path.Combine(AppDataDir, "prices.json");

        /// <summary>
        /// Default price catalog (19 products, 33 entries):
        /// - Anwis: Белый 1800 / Коричневый 1900
        /// - На навесах: Белый 2900 / Коричневый 3000
        /// - Оконная на метал. крепл.: Белый 3200 / Коричневый 3300
        /// - Дверная сетка: Белый 3000
        /// - Отлив: все 4 цвета, 2150/2650
        /// - Козырёк: все 4 цвета, 2150/2650
        /// - Короб: все 4 цвета, 2150/2650
        /// - ПСУЛ: без цвета, 100 руб/м.п.
        /// - Работа/Брус/Пояс/Доставка: без цвета, цена вручную (0)
        /// - Уплотнение: Серый/Чёрный 250 руб
        /// - Откос (calc): без цвета, цена вручную (0)
        /// - Работа за откос (calc): без цвета, цена вручную (0)
        /// - Сэндвич: без цвета, 1200 руб/м² (для откосов)
        /// - Пена (откос): без цвета, 750 руб/баллон
        /// - Герметик (откос): без цвета, 350 руб/тюбик
        /// - Скотч (откос): без цвета, 135 руб/моток
        /// - Старт (откос): без цвета, 135 руб/полоса 3 м
        /// - F-планка (откос): без цвета, 250 руб/полоса 3 м
        /// - Пеноплекс (откос): без цвета, 450 руб/лист
        /// - Работа за откос (цена за м.п.): без цвета, 600 руб/м.п.
        /// </summary>
        private static readonly List<PriceItem> DefaultPrices = new()
        {
            // Anwis — 2 цвета (без Антрацит, без Золотой дуб)
            new PriceItem { Name = "Anwis", Color = "Белый", Price = 1800 },
            new PriceItem { Name = "Anwis", Color = "Коричневый", Price = 1900 },

            // На навесах — 2 цвета (без Антрацит, без Золотой дуб)
            new PriceItem { Name = "На навесах", Color = "Белый", Price = 2900 },
            new PriceItem { Name = "На навесах", Color = "Коричневый", Price = 3000 },

            // Оконная на метал. крепл. — 2 цвета (без Антрацит, без Золотой дуб)
            new PriceItem { Name = "Оконная на метал. крепл.", Color = "Белый", Price = 3200 },
            new PriceItem { Name = "Оконная на метал. крепл.", Color = "Коричневый", Price = 3300 },

            // Дверная сетка — 1 цвет (только Белый)
            new PriceItem { Name = "Дверная сетка", Color = "Белый", Price = 3000 },

            // Отлив — все 4 цвета
            new PriceItem { Name = "Отлив", Color = "Белый", Price = 2150 },
            new PriceItem { Name = "Отлив", Color = "Коричневый", Price = 2150 },
            new PriceItem { Name = "Отлив", Color = "Антрацит", Price = 2150 },
            new PriceItem { Name = "Отлив", Color = "Золотой дуб", Price = 2650 },

            // Козырёк — все 4 цвета
            new PriceItem { Name = "Козырёк", Color = "Белый", Price = 2150 },
            new PriceItem { Name = "Козырёк", Color = "Коричневый", Price = 2150 },
            new PriceItem { Name = "Козырёк", Color = "Антрацит", Price = 2150 },
            new PriceItem { Name = "Козырёк", Color = "Золотой дуб", Price = 2650 },

            // Короб — все 4 цвета
            new PriceItem { Name = "Короб", Color = "Белый", Price = 2150 },
            new PriceItem { Name = "Короб", Color = "Коричневый", Price = 2150 },
            new PriceItem { Name = "Короб", Color = "Антрацит", Price = 2150 },
            new PriceItem { Name = "Короб", Color = "Золотой дуб", Price = 2650 },

            // ПСУЛ — без цвета, цена за м.п.
            new PriceItem { Name = "ПСУЛ", Color = "", Price = 100 },

            // Работа — без цвета, цена вручную
            new PriceItem { Name = "Работа", Color = "", Price = 0 },

            // Брус — без цвета, цена вручную
            new PriceItem { Name = "Брус", Color = "", Price = 0 },

            // Пояс — без цвета, цена вручную
            new PriceItem { Name = "Пояс", Color = "", Price = 0 },

            // Доставка — без цвета, цена вручную
            new PriceItem { Name = "Доставка", Color = "", Price = 0 },

            // Материал — без цвета, цена и количество вручную (количество опционально)
            new PriceItem { Name = "Материал", Color = "", Price = 0 },

            // Уплотнение — 2 цвета, 250 руб/м.п.
            new PriceItem { Name = "Уплотнение", Color = "Серый", Price = 250 },
            new PriceItem { Name = "Уплотнение", Color = "Чёрный", Price = 250 },

            // Откос (расчётный) — без цвета, цена вручную
            new PriceItem { Name = "Откос", Color = "", Price = 0 },

            // Работа за откос (расчётная) — без цвета, цена вручную
            new PriceItem { Name = "Работа за откос", Color = "", Price = 0 },

            // Материалы для расчёта откосов
            new PriceItem { Name = "Сэндвич", Color = "", Price = 1200 },
            new PriceItem { Name = "Пена (откос)", Color = "", Price = 750 },
            new PriceItem { Name = "Герметик (откос)", Color = "", Price = 350 },
            new PriceItem { Name = "Скотч (откос)", Color = "", Price = 135 },
            new PriceItem { Name = "Старт (откос)", Color = "", Price = 135 },
            new PriceItem { Name = "F-планка (откос)", Color = "", Price = 250 },
            new PriceItem { Name = "Пеноплекс (откос)", Color = "", Price = 450 },
            new PriceItem { Name = "Работа за откос", Color = "", Price = 600 },
        };

        public List<PriceItem> LoadPrices()
        {
            try
            {
                if (File.Exists(PricesPath))
                {
                    var json = File.ReadAllText(PricesPath);
                    var prices = JsonSerializer.Deserialize<List<PriceItem>>(json);
                if (prices != null && prices.Count > 0)
                {
                    // All schema migrations consolidated into ApplyMigrations
                    // (v3.22.0 cleanup — previously 4 separate foreach blocks
                    // with shared `bool changed` accumulator, hard to read and
                    // easy to drift out of order).
                    if (ApplyMigrations(prices))
                        SavePrices(prices);

                    return prices;
                }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PriceService] load failed, using defaults: {ex.Message}");
            }

            var defaults = DefaultPrices.Select(p => new PriceItem
            {
                Name = p.Name,
                Color = p.Color,
                Price = p.Price
            }).ToList();

            SavePrices(defaults);
            return defaults;
        }

        public void SavePrices(List<PriceItem> prices)
        {
            try
            {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var json = JsonSerializer.Serialize(prices, options);
            File.WriteAllText(PricesPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PriceService] save failed: {ex.Message}");
            }
        }

        public double GetPrice(List<PriceItem> prices, string name, string color)
        {
            // First try exact match (name + color)
            var item = prices.FirstOrDefault(p =>
                p.Name == name && p.Color == color);

            // Fallback: match by name with empty color
            if (item == null)
            {
                item = prices.FirstOrDefault(p =>
                    p.Name == name && string.IsNullOrEmpty(p.Color));
            }

            return item?.Price ?? 0;
        }

        public List<string> GetProductNames(List<PriceItem> prices)
        {
            return prices.Select(p => p.Name).Distinct().ToList();
        }

        /// <summary>
        /// Applies all schema migrations to a loaded price list.
        /// Returns true if any migration modified the list (caller
        /// should re-save to persist the change).
        ///
        /// Migrations are ordered: typo corrections first (so later
        /// migrations can rely on the corrected names), then price
        /// updates, then color/product removals, then additions. Each
        /// step is labelled so a future change can slot in cleanly.
        /// </summary>
        private static bool ApplyMigrations(List<PriceItem> prices)
        {
            bool changed = false;

            // ── Migration 1: rename old product-name typos ──────────
            // Anvis → Anwis, Оконная на мет. крепл. → Оконная на метал. крепл.
            foreach (var p in prices)
            {
                if (p.Name == "Anvis")
                {
                    p.Name = "Anwis";
                    changed = true;
                }
                if (p.Name == "Оконная на мет. крепл.")
                {
                    p.Name = "Оконная на метал. крепл.";
                    changed = true;
                }
            }

            // ── Migration 2: bump "На навесах — Коричневый" 2900 → 3000 ──
            foreach (var p in prices)
            {
                if (p.Name == "На навесах" && p.Color == "Коричневый" && p.Price == 2900)
                {
                    p.Price = 3000;
                    changed = true;
                }
            }

            // ── Migration 3: remove "Антрацит" entries for products that ──
            // no longer offer that color (Anwis, На навесах, Оконная на метал.).
            // Other products (Отлив, Козырёк, Короб) still offer Антрацит
            // and are preserved.
            //
            // Use RemoveAll (in-place), not `prices = prices.Where(...).ToList()`
            // — the latter rebinds the LOCAL parameter, not the caller's
            // reference in LoadPrices, and would silently lose Migration 3
            // and all subsequent modifications (Migration 4's Add).
            int removed = prices.RemoveAll(p =>
                (p.Name == "Anwis"
                 || p.Name == "На навесах"
                 || p.Name == "Оконная на метал. крепл.")
                && p.Color == "Антрацит");
            if (removed > 0)
                changed = true;

            // ── Migration 4: add any new default products missing from file ──
            // (e.g. when a new product type is introduced in a later release)
            var loadedNames = prices.Select(p => p.Name).Distinct().ToHashSet();
            var defaultNames = DefaultPrices.Select(p => p.Name).Distinct().ToHashSet();
            var missingNames = defaultNames.Except(loadedNames).ToList();

            if (missingNames.Count > 0)
            {
                foreach (var missingName in missingNames)
                {
                    var defaultItems = DefaultPrices.Where(p => p.Name == missingName);
                    foreach (var di in defaultItems)
                    {
                        prices.Add(new PriceItem { Name = di.Name, Color = di.Color, Price = di.Price });
                    }
                }
                changed = true;
            }

            // ── Migration 5: remove deprecated «Откос материал» ──────────
            // Replaced by the new Slope Calculator (v3.43.2+).
            int removedOtkos = prices.RemoveAll(p => p.Name == "Откос материал");
            if (removedOtkos > 0)
                changed = true;

            return changed;
        }

        /// <summary>
        /// Returns available colors for a product based on its price list entries.
        /// Each product only shows the colors that have price entries.
        /// </summary>
        public List<string> GetColorsForProduct(List<PriceItem> prices, string productName)
        {
            var colors = prices
                .Where(p => p.Name == productName && !string.IsNullOrEmpty(p.Color))
                .Select(p => p.Color)
                .Distinct()
                .ToList();

            return colors;
        }
    }
}
