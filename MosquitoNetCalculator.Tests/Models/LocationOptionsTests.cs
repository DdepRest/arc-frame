using MosquitoNetCalculator.Models;
using Xunit;

namespace MosquitoNetCalculator.Tests.Models
{
    public class LocationOptionsTests
    {
        [Fact]
        public void All_HasFiveLocations()
        {
            Assert.Equal(5, LocationOptions.All.Count);
        }

        [Fact]
        public void All_FirstIsDefault()
        {
            Assert.Equal("1", LocationOptions.All[0].Prefix);
            Assert.Contains("Красношапки", LocationOptions.All[0].LocationName);
        }

        [Fact]
        public void All_EachHasNonEmptyFields()
        {
            foreach (var loc in LocationOptions.All)
            {
                Assert.False(string.IsNullOrEmpty(loc.Prefix));
                Assert.False(string.IsNullOrEmpty(loc.LocationName));
            }
        }

        [Theory]
        [InlineData("1", true)]
        [InlineData("2", true)]
        [InlineData("3", true)]
        [InlineData("4", true)]
        [InlineData("5", true)]
        [InlineData("0", false)]
        [InlineData("6", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("abc", false)]
        public void IsValidPrefix_ReturnsCorrectResult(string? prefix, bool expected)
        {
            Assert.Equal(expected, LocationOptions.IsValidPrefix(prefix));
        }

        [Theory]
        [InlineData("1")]
        [InlineData("2")]
        [InlineData("3")]
        [InlineData("4")]
        [InlineData("5")]
        public void GetByPrefixOrDefault_KnownPrefix_ReturnsMatchingOption(string prefix)
        {
            var option = LocationOptions.GetByPrefixOrDefault(prefix);
            Assert.NotNull(option);
            Assert.Equal(prefix, option.Prefix);
        }

        [Fact]
        public void GetByPrefixOrDefault_UnknownPrefix_ReturnsDefault()
        {
            var option = LocationOptions.GetByPrefixOrDefault("99");
            Assert.NotNull(option);
            Assert.Equal("1", option.Prefix);
        }

        [Fact]
        public void GetByPrefixOrDefault_NullPrefix_ReturnsDefault()
        {
            var option = LocationOptions.GetByPrefixOrDefault(null);
            Assert.NotNull(option);
            Assert.Equal("1", option.Prefix);
        }

        [Fact]
        public void GetByPrefixOrDefault_EmptyPrefix_ReturnsDefault()
        {
            var option = LocationOptions.GetByPrefixOrDefault("");
            Assert.NotNull(option);
            Assert.Equal("1", option.Prefix);
        }

        [Fact]
        public void LocationOption_ToString_IncludesPrefixAndName()
        {
            var option = new LocationOption("1", "Test Location");
            Assert.Equal("1: Test Location", option.ToString());
        }
    }
}
