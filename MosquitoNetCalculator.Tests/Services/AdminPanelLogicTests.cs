using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Контракт <see cref="AdminPanelLogic.JustUnboundReturned"/>: пересечение
    /// «недавно отвязанных» с приславшими отчёт. Чистая политика без WPF/IO —
    /// единственный владелец правила «кто считается вернувшимся» (MODULES.md);
    /// контрол (<c>AdminPanelControl.PruneJustUnbound</c>) только применяет
    /// результат к своему словарю сессии.
    /// </summary>
    public class AdminPanelLogicTests
    {
        [Fact]
        public void JustUnboundReturned_ReturnsOnlyDevicesThatReportedBack()
        {
            var returned = AdminPanelLogic.JustUnboundReturned(
                new[] { "id-A", "id-B", "id-C" },
                new[] { "id-B", "id-C", "id-OTHER" });

            Assert.Equal(new[] { "id-B", "id-C" }, returned.OrderBy(x => x));
        }

        [Fact]
        public void JustUnboundReturned_NoneReported_EmptyResult()
        {
            var returned = AdminPanelLogic.JustUnboundReturned(
                new[] { "id-A", "id-B" },
                new[] { "id-OTHER" });

            Assert.Empty(returned);
        }

        [Fact]
        public void JustUnboundReturned_EmptyJustUnbound_EmptyResult()
        {
            var returned = AdminPanelLogic.JustUnboundReturned(
                System.Array.Empty<string>(),
                new[] { "id-A" });

            Assert.Empty(returned);
        }

        [Fact]
        public void JustUnboundReturned_DuplicateReportingIds_CountedOnce()
        {
            var returned = AdminPanelLogic.JustUnboundReturned(
                new[] { "id-A" },
                new[] { "id-A", "id-A" });

            var list = Assert.IsType<List<string>>(returned);
            Assert.Single(list);
        }
    }
}
