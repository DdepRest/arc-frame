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
            string pricesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prices.json");
            if (File.Exists(pricesPath))
                File.Delete(pricesPath);
            LoadPrices();
        }

        public List<string> GetProductNames() => _priceService.GetProductNames(Prices.ToList());

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
