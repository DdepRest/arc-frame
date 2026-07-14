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
    }
}
