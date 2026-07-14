using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.ViewModels
{
    public class PricesViewModel
    {
        private readonly PriceService _priceService = new();

        public ObservableCollection<PriceItem> Prices { get; } = new();

        public void LoadPrices()
        {
            var prices = _priceService.LoadPrices();
            Prices.Clear();
            foreach (var p in prices)
                Prices.Add(p);
        }

        public void SavePrices()
        {
            _priceService.SavePrices(Prices.ToList());
        }

        public void ResetPrices()
        {
            // prices.json lives in %AppData%\MosquitoNetCalculator\ (see PriceService.PricesPath),
            // NOT in AppDomain.CurrentDomain.BaseDirectory — auto-update replaces the install
            // directory and price edits must survive upgrades. Reset must delete the actual
            // AppData file, otherwise the next LoadPrices() reads stale user-edited prices.
            if (File.Exists(PriceService.PricesPath))
                File.Delete(PriceService.PricesPath);
            LoadPrices();
        }

        /// <summary>
        /// v3.43.3: внутренние материалы расчёта откосов НЕ должны показываться
        /// в QuickAdd → ComboBox «Тип». Это суб-материалы (используются внутри
        /// одного расчёта откоса для Старта/F-планки/Пеноплекса и т.п.), а не
        /// самостоятельные товары, которые пользователь добавляет в заказ.
        /// Полный список цен остаётся во вкладке «Цены» для редактирования.
        /// </summary>
        private static readonly HashSet<string> SlopeMaterialsHiddenFromQuickAdd = new(StringComparer.Ordinal)
        {
            "Сэндвич",
            "Пена (откос)",
            "Герметик (откос)",
            "Скотч (откос)",
            "Старт (откос)",
            "F-планка (откос)",
            "Пеноплекс (откос)",
            // "Работа за откос" оставлен — он отдельной строкой попадает в КП
            // и пользователь может добавлять его вручную как Работа без откоса.
        };

        public List<string> GetProductNames()
        {
            var all = _priceService.GetProductNames(Prices.ToList());
            var visible = new List<string>(all.Count);
            foreach (var name in all)
                if (!SlopeMaterialsHiddenFromQuickAdd.Contains(name))
                    visible.Add(name);
            return visible;
        }

        public List<string> GetColorsForProduct(string productName) => _priceService.GetColorsForProduct(Prices.ToList(), productName);

        public double GetPrice(string name, string color) => _priceService.GetPrice(Prices.ToList(), name, color);

        /// <summary>
        /// Applies current prices to all named order items.
        /// Returns the number of items updated.
        /// </summary>
        public int ApplyPricesToOrderItems(ObservableCollection<OrderItem> orderItems)
        {
            int count = 0;
            foreach (var item in orderItems.Where(x => !string.IsNullOrEmpty(x.Name)))
            {
                item.Price = _priceService.GetPrice(Prices.ToList(), item.Name, item.Color);
                count++;
            }
            return count;
        }
    }
}
