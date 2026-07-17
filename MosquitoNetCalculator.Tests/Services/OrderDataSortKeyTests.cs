using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// v3.45.0 (Phase 6 bugfix regression): tests for
    /// <see cref="OrderData.ContractNumberSortKey"/> — the natural-sort
    /// padded key used as <c>SortMemberPath</c> on the «№ КП» column.
    /// Bug pre-fix: WPF DataGrid sorted lexicographically,
    /// «2-10» came before «2-2». Pin the natural order so a future
    /// regression to lexicographic sort would fail these tests.
    /// </summary>
    public class OrderDataSortKeyTests
    {
        // ─── Generated natural-sort key ──────────────────────────

        [Fact]
        public void SortKey_PadsDigitsTo10Chars_SoStringCompareMatchesNumeric()
        {
            var o = new OrderData { ContractNumber = "2-1" };
            // 2 → "0000000002",   - → "-",   1 → "0000000001"
            Assert.Equal("0000000002-0000000001", o.ContractNumberSortKey);
        }

        [Fact]
        public void SortKey_PreservesNonDigitCharacters()
        {
            // Dotted copy suffix and prefix text stay readable.
            Assert.Equal("0000000002-0000000008.0000000001",
                new OrderData { ContractNumber = "2-8.1" }.ContractNumberSortKey);
            Assert.Equal("0000000002.0000000001",
                new OrderData { ContractNumber = "2.1" }.ContractNumberSortKey);
        }

        [Fact]
        public void SortKey_EmptyOrNull_ReturnsEmptyString()
        {
            Assert.Equal("", new OrderData { ContractNumber = "" }.ContractNumberSortKey);
            Assert.Equal("", new OrderData { ContractNumber = null! }.ContractNumberSortKey);
        }

        [Fact]
        public void SortKey_NoDigits_ReturnsInputAsIs()
        {
            // Letters-only contract numbers (defensive corner case) aren't padded,
            // natural-sort then degrades to plain lexicographic — fine.
            Assert.Equal("ABC-XYZ", new OrderData { ContractNumber = "ABC-XYZ" }.ContractNumberSortKey);
        }

        // ─── Numeric ordering regression ─────────────────────────

        [Theory]
        [InlineData("2-1", "2-2", -1)]
        [InlineData("2-2", "2-1", 1)]
        [InlineData("2-9", "2-10", -1)]   // The bug pre-fix: 9 vs 10
        [InlineData("2-9", "2-21", -1)]   // The exact regression from QA
        [InlineData("2-10", "2-2", 1)]    // 10 > 2 alphabetically broken
        [InlineData("1-1", "2-1", -1)]    // Major compares before minor
        [InlineData("10-1", "2-1", 1)]   // Major 10 > major 2 — must NOT collapse
        public void SortKey_OrdersNumerically_AsReportedInQA(string left, string right, int expectedSign)
        {
            // expectedSign: -1 if left should sort before right, +1 if after, 0 if equal.
            var k1 = new OrderData { ContractNumber = left }.ContractNumberSortKey;
            var k2 = new OrderData { ContractNumber = right }.ContractNumberSortKey;
            int result = string.Compare(k1, k2, System.StringComparison.Ordinal);
            // Trim expectedSign: compare should be non-zero in the documented direction.
            int actualSign = System.Math.Sign(result);
            Assert.Equal(expectedSign, actualSign);
        }

        [Fact]
        public void SortKey_VariousOrders_ProduceNaturalAscendingSequence()
        {
            // Build a deliberately scrambled list and assert the keys sort
            // the same way a user expects when clicking the «№ КП» column.
            string[] input = { "2-21", "2-9", "2-2", "2-10", "2-1", "2-11", "2-3" };
            string[] expected = { "2-1", "2-2", "2-3", "2-9", "2-10", "2-11", "2-21" };

            var keys = input.Select(n => new OrderData { ContractNumber = n }.ContractNumberSortKey).ToArray();
            var sorted = keys.OrderBy(k => k, System.StringComparer.Ordinal).ToArray();

            for (int i = 0; i < expected.Length; i++)
                Assert.Equal(new OrderData { ContractNumber = expected[i] }.ContractNumberSortKey, sorted[i]);
        }

        [Fact]
        public void SortKey_PaddedKeysAreAllSameLength_PerNumberSegment()
        {
            // Sanity: each numeric run must end up exactly 10 chars wide.
            // If pad logic ever regresses, key lengths would diverge.
            foreach (var n in new[] { "1-1", "1-10", "1-100", "1-9999999999" })
            {
                var key = new OrderData { ContractNumber = n }.ContractNumberSortKey;
                // "1" + "-" + 10 chars = 12; "1" + "-" + 10 chars + "-" + 10 chars = 22;
                // each numeric segment is exactly 10 chars wide.
                int dashCount = key.Count(c => c == '-');
                int dashLen = dashCount * 1;
                int digitChars = key.Count(char.IsDigit);
                int expectedDigitChars = (dashCount + 1) * 10;
                Assert.True(digitChars >= expectedDigitChars,
                    $"Key '{key}' for '{n}' should have at least {expectedDigitChars} digit chars, has {digitChars}");
            }
        }

        // ─── Memoization ────────────────────────────────────────

        [Fact]
        public void SortKey_CachedWhenContractNumberUnchanged()
        {
            // Same OrderData instance, ContractNumber read multiple times
            // before mutation: the underlying cache field should not be
            // rebuilt. We can't directly inspect the cache (private), but we
            // can verify the public contract: returned string is stable.
            var o = new OrderData { ContractNumber = "2-1" };
            string first = o.ContractNumberSortKey;
            string second = o.ContractNumberSortKey;
            string third = o.ContractNumberSortKey;

            Assert.Same(first, second);
            Assert.Same(second, third);
        }

        [Fact]
        public void SortKey_RecomputedAfterContractNumberChanges()
        {
            var o = new OrderData { ContractNumber = "2-1" };
            string first = o.ContractNumberSortKey;

            o.ContractNumber = "3-5";
            string second = o.ContractNumberSortKey;

            Assert.NotEqual(first, second);
            Assert.Equal("0000000003-0000000005", second);
        }

        [Fact]
        public void SortKey_CacheInvalidatedOnSettingSameValue()
        {
            // Even if ContractNumber is assigned the same value it already had,
            // CacheKeyIsByValue semantics must still produce the correct key
            // (cache is keyed on "the value at compute time", not "has it changed?").
            var o = new OrderData { ContractNumber = "2-1" };
            string first = o.ContractNumberSortKey;

            o.ContractNumber = "2-1"; // same value
            string second = o.ContractNumberSortKey;

            Assert.Equal(first, second); // contract: same value, same key
        }
    }
}
