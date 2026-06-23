using MosquitoNetCalculator.Models;
using Xunit;

namespace MosquitoNetCalculator.Tests.Models
{
    public class AdditionalKpItemTests
    {
        [Fact]
        public void DefaultValues()
        {
            var item = new AdditionalKpItem();
            Assert.Equal("", item.Number);
            Assert.Equal(0, item.Amount);
            Assert.True(item.IsActive);
        }

        [Fact]
        public void Number_SetAndGet()
        {
            var item = new AdditionalKpItem { Number = "2-3" };
            Assert.Equal("2-3", item.Number);
        }

        [Fact]
        public void Amount_SetAndGet()
        {
            var item = new AdditionalKpItem { Amount = 1500.50 };
            Assert.Equal(1500.50, item.Amount);
        }

        [Fact]
        public void IsActive_SetAndGet()
        {
            var item = new AdditionalKpItem { IsActive = false };
            Assert.False(item.IsActive);
        }

        [Theory]
        [InlineData("Number")]
        [InlineData("Amount")]
        [InlineData("IsActive")]
        public void PropertyChanged_Fired_OnPropertyChange(string propertyName)
        {
            var item = new AdditionalKpItem();
            string? lastProperty = null;
            item.PropertyChanged += (s, e) => lastProperty = e.PropertyName;

            switch (propertyName)
            {
                case "Number":
                    item.Number = "2-3";
                    break;
                case "Amount":
                    item.Amount = 500;
                    break;
                case "IsActive":
                    item.IsActive = false;
                    break;
            }

            Assert.Equal(propertyName, lastProperty);
        }

        [Fact]
        public void Clone_CreatesDeepCopy()
        {
            var original = new AdditionalKpItem
            {
                Number = "2-3",
                Amount = 1500,
                IsActive = false
            };

            var clone = original.Clone();

            Assert.Equal(original.Number, clone.Number);
            Assert.Equal(original.Amount, clone.Amount);
            Assert.Equal(original.IsActive, clone.IsActive);
        }

        [Fact]
        public void Clone_IsIndependent()
        {
            var original = new AdditionalKpItem
            {
                Number = "1",
                Amount = 1000,
                IsActive = true
            };

            var clone = original.Clone();
            clone.Number = "2";
            clone.Amount = 2000;
            clone.IsActive = false;

            Assert.Equal("1", original.Number);
            Assert.Equal(1000, original.Amount);
            Assert.True(original.IsActive);
        }

        [Fact]
        public void Number_ChangeTriggersPropertyChanged()
        {
            var item = new AdditionalKpItem();
            string? changed = null;
            item.PropertyChanged += (s, e) => changed = e.PropertyName;
            item.Number = "КП-Доп";
            Assert.Equal("Number", changed);
        }

        [Fact]
        public void Amount_Zero_DoesNotThrow()
        {
            var item = new AdditionalKpItem { Amount = 0 };
            Assert.Equal(0, item.Amount);
        }

        [Fact]
        public void Amount_Negative_StoredAsIs()
        {
            // Amount can be negative if needed (credit/refund)
            var item = new AdditionalKpItem { Amount = -500 };
            Assert.Equal(-500, item.Amount);
        }
    }
}
