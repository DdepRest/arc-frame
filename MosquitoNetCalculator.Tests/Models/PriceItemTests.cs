using MosquitoNetCalculator.Models;
using Xunit;

namespace MosquitoNetCalculator.Tests.Models
{
    public class PriceItemTests
    {
        [Fact]
        public void Price_PositiveValue_AssignedAsIs()
        {
            var item = new PriceItem { Name = "Anwis", Color = "Белый", Price = 1800 };
            Assert.Equal(1800, item.Price);
        }

        [Fact]
        public void Price_Zero_AssignedAsIs()
        {
            var item = new PriceItem { Price = 0 };
            Assert.Equal(0, item.Price);
        }

        [Fact]
        public void Price_NegativeValue_ClampedToZero()
        {
            // Bug #1 from the v3.22.0 analysis: PriceItem.Price setter did not clamp
            // negative values (unlike OrderItem.Price), so a user could enter a
            // negative price in the prices grid, save it to prices.json, and have
            // ApplyPricesToOrderItems propagate it to every order — breaking totals.
            var item = new PriceItem { Price = -100 };
            Assert.Equal(0, item.Price);
        }

        [Fact]
        public void Price_LargeNegativeValue_ClampedToZero()
        {
            var item = new PriceItem { Price = -99999.99 };
            Assert.Equal(0, item.Price);
        }

        [Fact]
        public void Price_PropertyChangedFired_OnActualChange()
        {
            var item = new PriceItem { Price = 100 };
            bool fired = false;
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PriceItem.Price)) fired = true;
            };
            item.Price = 200;
            Assert.True(fired);
        }

        [Fact]
        public void Price_PropertyChangedNotFired_WhenNegativeClampedToCurrentZero()
        {
            // When Price is already 0, setting another negative value should not
            // re-fire PropertyChanged (we already early-returned on the equality check).
            var item = new PriceItem { Price = 0 };
            int fireCount = 0;
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PriceItem.Price)) fireCount++;
            };
            item.Price = -50;
            Assert.Equal(0, fireCount);
        }

        [Fact]
        public void Price_NaN_RejectedStaysAtCurrentValue()
        {
            // Math.Max(0, NaN) returns NaN in .NET, so the setter must explicitly
            // reject NaN. Otherwise a NaN price would corrupt the value and
            // equality check (_price != NaN is always false) would silently
            // leave a NaN in the price grid.
            var item = new PriceItem { Price = 100 };
            item.Price = double.NaN;
            Assert.Equal(100, item.Price);
        }

        [Fact]
        public void Price_PositiveInfinity_RejectedStaysAtCurrentValue()
        {
            var item = new PriceItem { Price = 100 };
            item.Price = double.PositiveInfinity;
            Assert.Equal(100, item.Price);
        }

        [Fact]
        public void Price_NegativeInfinity_RejectedStaysAtCurrentValue()
        {
            // Math.Max(0, -Infinity) is 0 — but we still reject for consistency
            // and to keep the validation surface tight.
            var item = new PriceItem { Price = 100 };
            item.Price = double.NegativeInfinity;
            Assert.Equal(100, item.Price);
        }

        [Fact]
        public void DisplayName_EmptyColor_ShowsName()
        {
            var item = new PriceItem { Name = "Работа", Color = "", Price = 0 };
            Assert.Equal("Работа", item.DisplayName);
        }

        [Fact]
        public void DisplayName_WithColor_ShowsNameAndColor()
        {
            var item = new PriceItem { Name = "Anwis", Color = "Белый", Price = 1800 };
            Assert.Equal("Anwis (Белый)", item.DisplayName);
        }
    }
}
