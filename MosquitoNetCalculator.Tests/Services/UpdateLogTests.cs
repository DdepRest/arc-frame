using System.Linq;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class UpdateLogTests
    {
        [Fact]
        public void AllNewestFirst_ReturnsItemsFromEmbeddedJson()
        {
            var items = UpdateLog.AllNewestFirst();

            // Smoke test: we know the JSON has ~30 entries
            Assert.NotNull(items);
            Assert.True(items.Count >= 25, $"Expected ≥25 entries, got {items.Count}");
        }

        [Fact]
        public void AllNewestFirst_FirstItemIsNewest()
        {
            var items = UpdateLog.AllNewestFirst();

            // The latest version in the JSON is 3.34.3
            Assert.Equal("3.34.3", items[0].Version);
        }

        [Fact]
        public void AllNewestFirst_LastItemIsOldest()
        {
            var items = UpdateLog.AllNewestFirst();

            // The oldest version is 3.10
            Assert.Equal("3.10", items[^1].Version);
        }

        [Fact]
        public void AllNewestFirst_VersionsInDescendingOrder()
        {
            var items = UpdateLog.AllNewestFirst();

            for (int i = 1; i < items.Count; i++)
            {
                var result = string.Compare(items[i - 1].Version, items[i].Version, System.StringComparison.Ordinal);
                // Each version should be >= the next in ordinal comparison
                // (3.34.3 > 3.34.2 > ...)
                Assert.True(result >= 0,
                    $"Item {i - 1} ({items[i - 1].Version}) should be >= item {i} ({items[i].Version})");
            }
        }

        [Fact]
        public void AllNewestFirst_EachItemHasRequiredFields()
        {
            var items = UpdateLog.AllNewestFirst();

            foreach (var item in items)
            {
                Assert.False(string.IsNullOrEmpty(item.Version), "Version should not be empty");
                Assert.False(string.IsNullOrEmpty(item.Type), "Type should not be empty");
                Assert.False(string.IsNullOrEmpty(item.Title), "Title should not be empty");
                Assert.NotNull(item.Changes);
                Assert.NotEmpty(item.Changes);
            }
        }

        [Fact]
        public void AllNewestFirst_ReturnsNewCollectionEachTime()
        {
            var first = UpdateLog.AllNewestFirst();
            var second = UpdateLog.AllNewestFirst();

            Assert.NotSame(first, second);
            Assert.Equal(first.Count, second.Count);
            Assert.Equal(first[0].Version, second[0].Version);
        }

        [Fact]
        public void AllNewestFirst_ThreePointThirtyFourThree_HasExpectedChanges()
        {
            var items = UpdateLog.AllNewestFirst();
            var v3343 = items.FirstOrDefault(i => i.Version == "3.34.3");

            Assert.NotNull(v3343);
            Assert.Equal("Улучшение", v3343!.Type);
            Assert.Contains("Сегментированный", v3343.Title, System.StringComparison.Ordinal);
            Assert.Contains(v3343.Changes, c => c.Contains("segmented"));
        }

        [Fact]
        public void AllNewestFirst_KnownVersion_ThreePointTen_HasExpectedChanges()
        {
            var items = UpdateLog.AllNewestFirst();
            var v310 = items.FirstOrDefault(i => i.Version == "3.10");

            Assert.NotNull(v310);
            Assert.Equal("Улучшение", v310!.Type);
            Assert.Contains("Отключение позиций", v310.Title);
            Assert.Contains(v310.Changes, c => c.Contains("отключать"));
            Assert.Equal(4, v310.Changes.Count);
        }
    }
}
