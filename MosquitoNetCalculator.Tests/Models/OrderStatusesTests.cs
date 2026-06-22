using MosquitoNetCalculator.Models;
using Xunit;

namespace MosquitoNetCalculator.Tests.Models
{
    /// <summary>
    /// Regression suite for <see cref="OrderStatuses.GetRank"/> — the
    /// workflow-order rank used by the Orders grid «Статус» column
    /// SortMemberPath. Sort by «Статус» in the production UI now
    /// scrolls through the lifecycle in meaningful order
    /// (Новый → Подтверждён → … → Оплачен / Отменён) instead of by
    /// Cyrillic alphabetical collation. Pin the rank values so a
    /// future tweak (reordering, adding a new status, etc.) surfaces
    /// in tests instead of visually scrambling the user's sort.
    /// </summary>
    public class OrderStatusesTests
    {
        [Theory]
        [InlineData("Новый",               0)]
        [InlineData("Подтверждён",         1)]
        [InlineData("Отправлен на завод",  2)]
        [InlineData("В производстве",      3)]
        [InlineData("Готов к установке",   4)]
        [InlineData("Установлен",          5)]
        [InlineData("Оплачен",             6)]
        [InlineData("Отменён",             7)]
        public void GetRank_ReturnsLifecyclePosition(string status, int expectedRank)
        {
            Assert.Equal(expectedRank, OrderStatuses.GetRank(status));
        }

        [Fact]
        public void GetRank_UnknownStatus_ReturnsHighRank_SoItSinksToBottom()
        {
            // int.MaxValue pushes unrecognised statuses past every known
            // rank so they never interleave into the ordered workflow
            // (which would happen if we returned any small number like 8).
            Assert.Equal(int.MaxValue, OrderStatuses.GetRank(""));
            Assert.Equal(int.MaxValue, OrderStatuses.GetRank("something else"));
        }

        [Fact]
        public void Rank_IsStrictlyMonotonic_AcrossTheWorkflow()
        {
            // Defensive: every adjacent pair in OrderStatuses.All must
            // produce strictly monotonic ranks. A regression that swaps
            // two rows in the switch expression (e.g. «Отменён» before
            // «Новый») would invert the sort and silently break the UI.
            for (int i = 0; i < OrderStatuses.All.Length - 1; i++)
            {
                int a = OrderStatuses.GetRank(OrderStatuses.All[i]);
                int b = OrderStatuses.GetRank(OrderStatuses.All[i + 1]);
                Assert.True(a < b,
                    $"Rank must be strictly increasing: '{OrderStatuses.All[i]}' ({a}) < '{OrderStatuses.All[i + 1]}' ({b})");
            }
        }

        [Fact]
        public void All_Statuses_HaveADefinedRank()
        {
            // Defensive: every status listed in OrderStatuses.All must
            // have an explicit rank (not int.MaxValue). Otherwise it
            // would collapse into the "unknown" bucket and break the
            // monotonic chain.
            foreach (var status in OrderStatuses.All)
            {
                int rank = OrderStatuses.GetRank(status);
                Assert.True(rank >= 0 && rank < int.MaxValue,
                    $"Status '{status}' must have an explicit low rank, got {rank}");
            }
        }
    }
}
