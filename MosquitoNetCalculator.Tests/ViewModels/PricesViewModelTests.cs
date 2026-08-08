using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.ViewModels;
using Xunit;

namespace MosquitoNetCalculator.Tests.ViewModels
{
    public class PricesViewModelTests
    {
        // ────────────────────────────────────────────────────────────────
        // v3.43.3: внутренние материалы расчёта откосов НЕ должны попадать
        // в QuickAdd → ComboBox «Тип». Это суб-материалы для формул внутри
        // расчёта откоса; пользователь должен добавлять только «Откос»/«Работа
        // за откос» как агрегированные строки КП.
        // ────────────────────────────────────────────────────────────────

        [Fact]
        public void GetProductNames_HidesSlopeMaterials_FromQuickAdd()
        {
            var vm = new PricesViewModel();
            vm.LoadPrices();

            var names = vm.GetProductNames();

            // Slope-материалы НЕ должны быть в списке.
            Assert.DoesNotContain("Сэндвич", names);
            Assert.DoesNotContain("Пена (откос)", names);
            Assert.DoesNotContain("Герметик (откос)", names);
            Assert.DoesNotContain("Скотч (откос)", names);
            Assert.DoesNotContain("Старт (откос)", names);
            Assert.DoesNotContain("F-планка (откос)", names);
            Assert.DoesNotContain("Пеноплекс (откос)", names);
        }

        [Fact]
        public void GetProductNames_KeepsAggregateSlopeProducts_ForManualAdd()
        {
            // «Откос» и «Работа за откос» — агрегаты, должны остаться доступными.
            var vm = new PricesViewModel();
            vm.LoadPrices();

            var names = vm.GetProductNames();
            Assert.Contains("Откос", names);
            Assert.Contains("Работа за откос", names);
        }

        [Fact]
        public void GetProductNames_KeepsRegularProducts()
        {
            // Sanity: обычные товары (которые юзер добавляет в КП) — на месте.
            var vm = new PricesViewModel();
            vm.LoadPrices();

            var names = vm.GetProductNames();
            Assert.Contains("Anwis", names);
            Assert.Contains("На навесах", names);
            Assert.Contains("Дверная сетка", names);
            Assert.Contains("Отлив", names);
            Assert.Contains("ПСУЛ", names);
            Assert.Contains("Уплотнение", names);
        }

        // ────────────────────────────────────────────────────────────────
        // v3.4x: порядок каталога — НЕ алфавитный. Иерархия согласована с
        // пользователем: Сетки → Доборы → Комплектующие → Откосы → Услуги.
        // Алфавитная сортировка уводила «Anwis» в конец списка — регрессия.
        // ────────────────────────────────────────────────────────────────

        private static readonly string[] CatalogOrder =
        {
            "Anwis", "На навесах", "Оконная на метал. крепл.", "Дверная сетка",
            "Отлив", "Козырёк", "Короб",
            "ПСУЛ", "Уплотнение", "Брус", "Пояс", "Материал",
            "Откос", "Работа за откос",
            "Работа", "Доставка"
        };

        [Fact]
        public void GetProductNames_KeepsCatalogGroupOrder()
        {
            var vm = new PricesViewModel();
            vm.LoadPrices();

            var names = vm.GetProductNames();

            // Иерархия групп сохраняется: каждый товар идёт строго после предыдущего.
            for (int i = 1; i < CatalogOrder.Length; i++)
            {
                int prev = names.IndexOf(CatalogOrder[i - 1]);
                int cur = names.IndexOf(CatalogOrder[i]);
                Assert.True(prev >= 0, $"{CatalogOrder[i - 1]} отсутствует в списке");
                Assert.True(cur >= 0, $"{CatalogOrder[i]} отсутствует в списке");
                Assert.True(prev < cur, $"{CatalogOrder[i - 1]} должен идти раньше {CatalogOrder[i]}");
            }
        }

        [Fact]
        public void GetProductNames_AppendsUserProductsAfterCatalog()
        {
            // Пользовательские товары (которых нет в фиксированных группах)
            // добавляются В КОНЕЦ, а не вклиниваются в иерархию.
            var vm = new PricesViewModel();
            vm.LoadPrices();
            vm.Prices.Add(new PriceItem { Name = "Сетка Пользователя", Color = "", Price = 0 });

            var names = vm.GetProductNames();

            Assert.Contains("Сетка Пользователя", names);
            Assert.Equal(names.Count - 1, names.IndexOf("Сетка Пользователя"));
        }
    }
}
