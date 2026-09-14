using System;
using System.Collections.ObjectModel;
using System.Linq;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Converters;
using MosquitoNetCalculator.Models;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// v3.51: юнит-тесты чистой логики «Истории обновлений»
    /// (<see cref="UpdatesListLogic"/>) и конвертера «Ваша версия»
    /// (<see cref="IsMyVersionConverter"/>). Без WPF — быстрые тесты.
    /// </summary>
    public class UpdatesListLogicTests
    {
        private static UpdateItem Item(string version, string type = "Улучшение",
            string title = "Заголовок", params string[] changes) =>
            new() { Version = version, Type = type, Title = title, Date = new DateTime(2026, 9, 1), Changes = changes.ToList() };

        // ─── BuildPredicate ─────────────────────────────────────────────

        [Fact]
        public void Predicate_NoFilter_PassesEverything()
        {
            var pred = UpdatesListLogic.BuildPredicate(null, null);
            Assert.True(pred(Item("3.49.0")));
            Assert.True(pred(Item("3.10", type: "Новинка")));
        }

        [Fact]
        public void Predicate_ByType_MatchesExactTypeOnly()
        {
            var pred = UpdatesListLogic.BuildPredicate("Новинка", null);
            Assert.True(pred(Item("3.50.0", type: "Новинка")));
            Assert.False(pred(Item("3.49.0", type: "Улучшение")));
            Assert.False(pred(Item("3.48.7", type: "Исправление")));
        }

        [Fact]
        public void Predicate_ByQuery_MatchesVersionTitleAndChanges()
        {
            var item = Item("3.49.0", title: "Копия «В производство»", changes: new[] { "Новый флажок в окне печати" });

            Assert.True(UpdatesListLogic.BuildPredicate(null, "3.49")(item));
            Assert.True(UpdatesListLogic.BuildPredicate(null, "производство")(item));   // title
            Assert.True(UpdatesListLogic.BuildPredicate(null, "ФЛАЖОК")(item));         // change, case-insensitive
            Assert.False(UpdatesListLogic.BuildPredicate(null, "откосы")(item));
        }

        [Fact]
        public void Predicate_Combined_TypeAndQuery_BothMustMatch()
        {
            var novelty = Item("3.50.0", type: "Новинка", title: "Фильтры");
            var improvement = Item("3.49.0", type: "Улучшение", title: "Фильтры истории");

            var pred = UpdatesListLogic.BuildPredicate("Новинка", "фильтр");
            Assert.True(pred(novelty));
            Assert.False(pred(improvement));
        }

        [Fact]
        public void Predicate_NullItem_NeverMatches()
        {
            Assert.False(UpdatesListLogic.BuildPredicate("Новинка", null)(null!));
            Assert.False(UpdatesListLogic.BuildPredicate(null, "x")(null!));
        }

        // ─── CountText ──────────────────────────────────────────────────

        [Theory]
        [InlineData(1, "1 версия")]
        [InlineData(2, "2 версии")]
        [InlineData(5, "5 версий")]
        [InlineData(11, "11 версий")]
        [InlineData(14, "14 версий")]
        [InlineData(21, "21 версия")]
        [InlineData(64, "64 версии")]
        public void CountText_Unfiltered_UsesRussianPluralization(int count, string expected)
        {
            Assert.Equal(expected, UpdatesListLogic.CountText(count, count));
        }

        [Fact]
        public void CountText_Filtered_ShowsNOutOfM()
        {
            Assert.Equal("3 из 64 версии", UpdatesListLogic.CountText(3, 64));
            Assert.Equal("1 из 2 версии", UpdatesListLogic.CountText(1, 2));
        }

        // ─── Expanded defaults ──────────────────────────────────────────

        [Fact]
        public void ApplyExpandedDefaults_FirstFiveExpanded_RestCollapsed()
        {
            var updates = new ObservableCollection<UpdateItem>(
                Enumerable.Range(0, 12).Select(i => Item($"3.{50 - i}.0")));

            UpdatesListLogic.ApplyExpandedDefaults(updates);

            Assert.True(updates[0].IsExpanded);
            Assert.True(updates[4].IsExpanded);
            Assert.False(updates[5].IsExpanded);
            Assert.False(updates[^1].IsExpanded);
        }

        [Fact]
        public void ApplyExpandedDefaults_ShortList_AllExpanded()
        {
            var updates = new ObservableCollection<UpdateItem>(new[] { Item("3.1.0"), Item("3.0.9") });
            UpdatesListLogic.ApplyExpandedDefaults(updates);
            Assert.All(updates, i => Assert.True(i.IsExpanded));
        }

        [Fact]
        public void SetAllExpanded_TogglesEverything()
        {
            var updates = new ObservableCollection<UpdateItem>(new[] { Item("3.1.0"), Item("3.0.9") });
            UpdatesListLogic.SetAllExpanded(updates, true);
            Assert.All(updates, i => Assert.True(i.IsExpanded));
            UpdatesListLogic.SetAllExpanded(updates, false);
            Assert.All(updates, i => Assert.False(i.IsExpanded));
        }

        // ─── BuildCopyText ──────────────────────────────────────────────

        [Fact]
        public void BuildCopyText_VersionTitleAndBulletedChanges()
        {
            var text = UpdatesListLogic.BuildCopyText(Item("3.49.0", title: "Копия «В производство»",
                changes: new[] { "Флажок в окне печати", "Штамп на первом листе" }));

            Assert.Equal("v 3.49.0 — Копия «В производство»\n• Флажок в окне печати\n• Штамп на первом листе", text);
        }

        [Fact]
        public void BuildCopyText_SkipsEmptyChanges_AndHandlesNoChanges()
        {
            Assert.Equal("v 3.1.0 — Заголовок", UpdatesListLogic.BuildCopyText(Item("3.1.0", changes: new[] { "", "  " })));
            Assert.Equal("v 3.0.9 — Заголовок", UpdatesListLogic.BuildCopyText(Item("3.0.9")));
        }

        [Fact]
        public void BuildCopyText_NullItem_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, UpdatesListLogic.BuildCopyText(null));
        }

        // ─── ResolveChipSelection (v3.51.1: баг «оба чипа активны») ───

        [Theory]
        [InlineData("Все", "", "")]           // клик по «Все» — без фильтра
        [InlineData("Все", "Новинка", "")]    // клик по «Все» снимает тип
        [InlineData("Новинка", "", "Новинка")]
        [InlineData("Новинка", "Улучшение", "Новинка")]
        [InlineData("Новинка", "Новинка", "")]  // повторный клик — снять
        [InlineData("Исправление", "Исправление", "")]
        public void ResolveChipSelection_Exclusive(string clicked, string active, string expected)
        {
            Assert.Equal(expected, UpdatesListLogic.ResolveChipSelection(clicked, active));
        }

        // ─── IsMyVersionConverter ───────────────────────────────────────

        [Fact]
        public void MyVersion_MatchesExactly_AndLeniently()
        {
            var conv = new IsMyVersionConverter(new Version(3, 49, 0));
            Assert.True((bool)conv.Convert("3.49.0", typeof(bool), null!, null!));
            Assert.True((bool)conv.Convert("3.49", typeof(bool), null!, null!));   // «3.49» == «3.49.0»
            Assert.False((bool)conv.Convert("3.48.7", typeof(bool), null!, null!));
        }

        [Fact]
        public void MyVersion_ZeroCurrentVersion_NeverMatches()
        {
            var conv = new IsMyVersionConverter(new Version(0, 0, 0));
            Assert.False((bool)conv.Convert("0.0.0", typeof(bool), null!, null!));
            Assert.False((bool)conv.Convert("3.49.0", typeof(bool), null!, null!));
        }

        [Fact]
        public void MyVersion_GarbageInput_ReturnsFalse()
        {
            var conv = new IsMyVersionConverter(new Version(3, 49, 0));
            Assert.False((bool)conv.Convert(null!, typeof(bool), null!, null!));
            Assert.False((bool)conv.Convert("не версия", typeof(bool), null!, null!));
        }

        [Fact]
        public void MyVersion_GitSuffix_StrippedBeforeCompare()
        {
            // «3.49.0+abc123» (InformationalVersion) — та же версия, что 3.49.0.
            var conv = new IsMyVersionConverter(new Version(3, 49, 0));
            Assert.True((bool)conv.Convert("3.49.0+abc123", typeof(bool), null!, null!));
        }
    }
}
