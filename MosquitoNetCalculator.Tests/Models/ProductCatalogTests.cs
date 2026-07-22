using MosquitoNetCalculator.Models;
using Xunit;

namespace MosquitoNetCalculator.Tests.Models
{
    public class ProductCatalogTests
    {
        [Theory]
        [InlineData("Anwis", true)]
        [InlineData("На навесах", true)]
        [InlineData("Дверная сетка", true)]
        [InlineData("Оконная на метал. крепл.", true)]
        [InlineData("Отлив", true)]
        [InlineData("Козырёк", true)]
        [InlineData("Работа", false)]
        public void IsInstallationApplicable_ReturnsExpected(string name, bool expected)
        {
            Assert.Equal(expected, ProductCatalog.IsInstallationApplicable(name));
        }

        [Theory]
        [InlineData("Anwis", true)]
        [InlineData("Отлив", true)]
        [InlineData("Работа", false)]
        [InlineData("ПСУЛ", false)]
        public void IsAreaBased_ReturnsExpected(string name, bool expected)
        {
            Assert.Equal(expected, ProductCatalog.IsAreaBased(name));
        }

        [Theory]
        [InlineData("Работа", true)]
        [InlineData("Откос", true)]
        [InlineData("Anwis", false)]
        [InlineData("ПСУЛ", false)]
        public void IsManualPiece_ReturnsExpected(string name, bool expected)
        {
            Assert.Equal(expected, ProductCatalog.IsManualPiece(name));
        }

        [Theory]
        [InlineData("Брус", true)]
        [InlineData("Пояс", true)]
        [InlineData("Доставка", true)]
        [InlineData("Работа", false)]
        public void IsAmountOnly_ReturnsExpected(string name, bool expected)
        {
            Assert.Equal(expected, ProductCatalog.IsAmountOnly(name));
        }

        [Theory]
        [InlineData("Материал", true)]
        [InlineData("Работа", false)]
        public void IsQuantityOptional_ReturnsExpected(string name, bool expected)
        {
            Assert.Equal(expected, ProductCatalog.IsQuantityOptional(name));
        }

        [Theory]
        [InlineData("Откос", true)]
        [InlineData("Работа", false)]
        public void IsWidthOnly_ReturnsExpected(string name, bool expected)
        {
            Assert.Equal(expected, ProductCatalog.IsWidthOnly(name));
        }

        [Theory]
        [InlineData("Anwis", true)]
        [InlineData("Дверная сетка", true)]
        [InlineData("Отлив", false)]
        public void IsAnticatApplicable_ReturnsExpected(string name, bool expected)
        {
            Assert.Equal(expected, ProductCatalog.IsAnticatApplicable(name));
        }

        [Theory]
        [InlineData("ПСУЛ", true)]
        [InlineData("Работа", true)]
        [InlineData("Anwis", false)]
        public void IsNoColor_ReturnsExpected(string name, bool expected)
        {
            Assert.Equal(expected, ProductCatalog.IsNoColor(name));
        }

        [Fact]
        public void IsMethods_HandleNullAndEmpty()
        {
            Assert.False(ProductCatalog.IsInstallationApplicable(null));
            Assert.False(ProductCatalog.IsInstallationApplicable(""));
            Assert.False(ProductCatalog.IsAreaBased(null));
            Assert.False(ProductCatalog.IsManualPiece(""));
        }

        [Fact]
        public void OrderItem_Proxies_DelegateToProductCatalog()
        {
            // Backward-compat: OrderItem static HashSets must reflect ProductCatalog.
            Assert.True(OrderItem.InstallationApplicableProducts.SetEquals(ProductCatalog.InstallationApplicableProducts));
            Assert.True(OrderItem.AreaBasedProducts.SetEquals(ProductCatalog.AreaBasedProducts));
            Assert.True(OrderItem.ManualPieceProducts.SetEquals(ProductCatalog.ManualPieceProducts));
            Assert.True(OrderItem.AmountOnlyProducts.SetEquals(ProductCatalog.AmountOnlyProducts));
            Assert.True(OrderItem.OptionalQuantityProducts.SetEquals(ProductCatalog.OptionalQuantityProducts));
            Assert.True(OrderItem.WidthOnlyProducts.SetEquals(ProductCatalog.WidthOnlyProducts));
            Assert.True(OrderItem.AnticatApplicableProducts.SetEquals(ProductCatalog.AnticatApplicableProducts));
            Assert.True(OrderItem.NoColorProducts.SetEquals(ProductCatalog.NoColorProducts));
        }

        [Fact]
        public void OrderItem_InstanceProperties_DelegateToProductCatalog()
        {
            var item = new OrderItem { Name = "Откос" };
            Assert.True(item.IsManualPiece);
            Assert.True(item.IsWidthOnly);
            Assert.False(item.IsAmountOnly);
            Assert.False(item.IsInstallationApplicable);

            item.Name = "Отлив";
            Assert.True(item.IsInstallationApplicable);

            item.Name = "Anwis";
            Assert.False(item.IsManualPiece);
            Assert.False(item.IsWidthOnly);
            Assert.True(item.IsInstallationApplicable);
        }
    }
}
