using System.Linq;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using Xunit;

namespace MosquitoNetCalculator.Tests.Models
{
    /// <summary>
    /// Стражи композитного типа записи обновления (v3.53.1, решение владельца):
    /// смешанный релиз («Новинка + Исправление») не должен носить бейдж одного
    /// типа. Строка Type парсится в Types по «+»; сериализуется по-прежнему
    /// только строка — контракт JSON не меняется. Фильтр-чип «Новинки» обязан
    /// находить запись по ЛЮБОМУ из её типов.
    /// </summary>
    public class UpdateItemCompositeTypeTests
    {
        [Fact]
        public void CompositeType_ParsesIntoTwoBadges()
        {
            var item = new UpdateItem { Type = "Новинка + Исправление" };

            Assert.Equal(new[] { "Новинка", "Исправление" }, item.Types);
        }

        [Theory]
        [InlineData("Новинка")]
        [InlineData("Исправление")]
        public void CompositeType_HasTypeMatchesEitherPart(string filter)
        {
            var item = new UpdateItem { Type = "Новинка + Исправление" };

            Assert.True(item.HasType(filter));
        }

        [Fact]
        public void CompositeType_HasTypeRejectsForeignFilter()
        {
            var item = new UpdateItem { Type = "Новинка + Исправление" };

            Assert.False(item.HasType("Улучшение"));
            Assert.False(item.HasType(""));
            Assert.False(item.HasType(null!));
        }

        [Theory]
        [InlineData("Новинка")]
        [InlineData("Исправление")]
        [InlineData("Улучшение")]
        public void SingleType_BehavesAsBefore(string type)
        {
            var item = new UpdateItem { Type = type };

            Assert.Single(item.Types);
            Assert.Equal(type, item.Types[0]);
            Assert.True(item.HasType(type));
        }

        [Fact]
        public void EmptyType_FallsBackToSingleEmptyBadge()
        {
            var item = new UpdateItem { Type = "" };

            Assert.Single(item.Types);
        }

        [Fact]
        public void CompositeType_ExtraWhitespace_IsTrimmed()
        {
            var item = new UpdateItem { Type = "  Новинка  +  Исправление  " };

            Assert.Equal(new[] { "Новинка", "Исправление" }, item.Types);
        }

        // ─── Фильтр-чипы «Истории обновлений» ─────────────────────────────

        [Fact]
        public void UpdatesFilter_CompositeEntry_MatchesBothChips()
        {
            var composite = new UpdateItem { Version = "3.53.1", Type = "Новинка + Исправление" };
            var noveltyChip = UpdatesListLogic.BuildPredicate("Новинка", null);
            var fixChip = UpdatesListLogic.BuildPredicate("Исправление", null);

            Assert.True(noveltyChip(composite));
            Assert.True(fixChip(composite));
        }

        [Fact]
        public void UpdatesFilter_ImprovementChip_DoesNotMatchComposite()
        {
            var composite = new UpdateItem { Version = "3.53.1", Type = "Новинка + Исправление" };

            Assert.False(UpdatesListLogic.BuildPredicate("Улучшение", null)(composite));
        }

        [Fact]
        public void UpdatesFilter_AllChip_ShowsEverything()
        {
            var composite = new UpdateItem { Version = "3.53.1", Type = "Новинка + Исправление" };

            Assert.True(UpdatesListLogic.BuildPredicate("", null)(composite));
        }
    }
}
