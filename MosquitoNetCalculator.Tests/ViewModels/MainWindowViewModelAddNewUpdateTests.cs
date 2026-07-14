using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.ViewModels;
using Xunit;

namespace MosquitoNetCalculator.Tests.ViewModels
{
    /// <summary>
    /// v3.43.3: принудительная сериализация xUnit для всех тестов класса.
    /// Без этого xUnit параллелизует тесты этого класса с другими, которые
    /// дёргают <see cref="UpdateLog.AllNewestFirst"/> и мутируют общий
    /// <c>_entries</c> Lazy-cache. Конкурентные вызовы
    /// <c>UpdateLog.AllNewestFirst()</c> перетирали <c>IsLatest</c> на одних
    /// и тех же экземплярах <see cref="UpdateItem"/>, и
    /// <c>AddNewUpdate_ZeroIsLatestFrame_neverObserved</c> видел
    /// мгновенное окно с count=0 — ложноположительный flicker-фейл.
    ///
    /// Паттерн из <c>AppLifecycleTests.WpfUiTestCollection</c>:
    /// xUnit сериализует все тесты классов с одинаковым [Collection("…")].
    /// </summary>
    [CollectionDefinition("UpdateLogState", DisableParallelization = true)]
    public class UpdateLogStateTestCollection { }

    /// <summary>
    /// Integration coverage for <see cref="MainWindowViewModel.AddNewUpdate"/>
    /// — the contract that "old card doesn't flicker" when an auto-update
    /// brings a new release into <c>Updates</c>.
    ///
    /// Three guarantees we want to lock in:
    /// 1) Atomic ordering — observable <c>IsLatest</c> state never drops to
    ///    "zero items have IsLatest=true" during the call.
    /// 2) Minimal PropertyChanged surface on old items — only the OLD card
    ///    that previously held IsLatest=true fires PropertyChanged, and
    ///    only with PropertyName="IsLatest" (no other property involvement).
    /// 3) ObservableCollection.Insert(0, …) re-indexes but does NOT
    ///    mutate any existing item's properties — old items keep their
    ///    identity/ref.
    /// </summary>
    [Collection("UpdateLogState")]
    public class MainWindowViewModelAddNewUpdateTests
    {
        /// <summary>
        /// Helper: collect every PropertyChanged fired on `item` into a
        /// list of (propertyName, currentIsLatest). Used to assert that
        /// no item transitions through a "zero cards marked latest" frame.
        /// </summary>
        private static List<(string prop, bool isLatest)> RecordTransitions(
            UpdateItem item)
        {
            var list = new List<(string, bool)>();
            void Handler(object? s, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(UpdateItem.IsLatest))
                    list.Add((e.PropertyName!, item.IsLatest));
            }
            item.PropertyChanged += Handler;
            // Snapshot state on subscribe so the first list entry reflects
            // the value AT the moment of attachment.
            list.Add((nameof(UpdateItem.IsLatest), item.IsLatest));
            return list;
        }

        /// <summary>
        /// Convenience given a VM + a candidate new item not yet in Updates:
        /// list every "live" IsLatest=true across existing items plus the
        /// candidate's OWN flag IF it isn't yet in the collection.
        ///
        /// Why the <c>!vm.Updates.Contains(candidate)</c> guard:
        /// <see cref="MainWindowViewModel.AddNewUpdate"/> performs
        /// <c>Insert(0, newItem)</c>; after that <c>candidate</c> is BOTH
        /// in <c>vm.Updates</c> AND referenced by the caller. If any
        /// PropertyChanged fires on the candidate AFTER Insert, a naive
        /// <c>vm.Updates.Count(IsLatest) + candidate.IsLatest</c> would
        /// double-count and silently mask a real flicker. The guard
        /// ensures the count is exact at every moment of the synchronous
        /// call, regardless of whether the candidate is in the collection
        /// yet.
        /// </summary>
        private static int LiveIsLatestCount(MainWindowViewModel vm, UpdateItem? candidate)
        {
            int count = vm.Updates.Count(u => u.IsLatest);
            if (candidate is not null
                && candidate.IsLatest
                && !vm.Updates.Contains(candidate))
            {
                count++;
            }
            return count;
        }

        [Fact]
        public void AddNewUpdate_ZeroIsLatestFrame_neverObserved()
        {
            // Arrange — VM is constructed once; it pre-loads Updates from
            // UpdateLog.AllNewestFirst() (embedded update-log.json). At
            // least one item has IsLatest=true by that contract.
            var vm = new MainWindowViewModel();
            var initialLatest = vm.Updates.Single(u => u.IsLatest);
            var newItem = new UpdateItem
            {
                Version = "99.99.99",
                Type = "Доступно",
                Title = "Test",
                Date = DateTime.UtcNow,
                Changes = new System.Collections.Generic.List<string> { "stub" }
            };

            // Attach PropertyChanged listeners to BOTH existing items and
            // the candidate new item. Each recorded IsLatest transition
            // snapshots LiveIsLatestCount — must never be 0.
            var snapshots = new List<int>();
            var allItems = vm.Updates.ToList();
            var subscriptions = new List<(UpdateItem item, PropertyChangedEventHandler h)>();
            var handler = new PropertyChangedEventHandler((s, e) =>
            {
                if (e.PropertyName == nameof(UpdateItem.IsLatest))
                    snapshots.Add(LiveIsLatestCount(vm, newItem));
            });
            foreach (var ex in allItems) ex.PropertyChanged += handler;
            newItem.PropertyChanged += handler;

            // Snapshot BEFORE the call.
            snapshots.Add(LiveIsLatestCount(vm, newItem));

            // Act
            vm.AddNewUpdate(newItem);

            // Cleanup
            foreach (var ex in allItems) ex.PropertyChanged -= handler;
            newItem.PropertyChanged -= handler;

            // Assert — at every observable instant (initial + each transition),
            // at least one item had IsLatest=true.
            Assert.NotEmpty(snapshots);
            Assert.All(snapshots, n => Assert.True(
                n >= 1,
                $"Flicker detected: snapshot reported zero cards with IsLatest=true. Snapshots: [{string.Join(", ", snapshots)}]"));

            // Final state — exactly one latest, and it's the new entry at index 0.
            Assert.True(newItem.IsLatest);
            Assert.False(initialLatest.IsLatest);
            Assert.Single(vm.Updates.Where(u => u.IsLatest));
            Assert.Same(newItem, vm.Updates[0]);
        }

        [Fact]
        public void AddNewUpdate_OldItemsFire_OnlyIsLatest_PropertyChanged()
        {
            // Each old card inspected: only the formerly-latest card may
            // fire PropertyChanged, and only for PropertyName="IsLatest".
            // No other fields (Version, Date, Title, Type, Changes) and
            // no other cards should fire anything. This locks the
            // "old card doesn't flicker" invariant on data-layer side;
            // the WPF binding layer then has zero delta to re-render.
            //
            // Capture "oldLatestBefore" BEFORE the AddNewUpdate call —
            // after the call only newItem has IsLatest=true, so a
            // post-call Single(.IsLatest=true) would throw at runtime.
            var vm = new MainWindowViewModel();
            var fired = new List<(UpdateItem item, string prop)>();

            var existing = vm.Updates.ToList();
            var subscriptions = existing.Select(ex =>
            {
                PropertyChangedEventHandler h = (s, e) =>
                    fired.Add((ex, e.PropertyName ?? ""));
                ex.PropertyChanged += h;
                return (ex, h);
            }).ToList();

            var oldLatestBefore = existing.Single(u => u.IsLatest);

            var newItem = new UpdateItem
            {
                Version = "99.99.99",
                Type = "Доступно",
                Title = "Test",
                Date = DateTime.UtcNow,
                Changes = new System.Collections.Generic.List<string> { "stub" }
            };
            vm.AddNewUpdate(newItem);

            // Unsubscribe
            foreach (var (ex, h) in subscriptions) ex.PropertyChanged -= h;

            // Filter — include only items that fired for old cards.
            var oldFired = fired.Where(t => existing.Contains(t.item)).ToList();

            // Assert: only the old card that previously held IsLatest=true may have fired,
            // and only for the IsLatest property (no Title/Type/Version/etc).
            Assert.All(oldFired, t =>
            {
                Assert.Same(oldLatestBefore, t.item);
                Assert.Equal(nameof(UpdateItem.IsLatest), t.prop);
            });
        }

        [Fact]
        public void AddNewUpdate_OldItems_ReferenceIdentity_Unchanged()
        {
            // ObservableCollection<T>.Insert does NOT mutate existing
            // items in any way — it's just an index/move. Verify our
            // wrapper preserves reference identity for old cards after
            // the call. This is the structural basis for "no UI unmount
            // / re-mount" of existing UpdateItem containers — the WPF
            // ItemsControl reuses the same ContentPresenter for each
            // unchanged item.
            var vm = new MainWindowViewModel();
            var beforeRefs = vm.Updates.ToList();
            var beforeFingerprints = beforeRefs.ToDictionary(
                u => u,
                u => $"{u.Version}|{u.Date:o}|{u.Type}|{u.Title}|{string.Join(",", u.Changes)}");

            var newItem = new UpdateItem
            {
                Version = "99.99.99",
                Type = "Доступно",
                Title = "Test",
                Date = DateTime.UtcNow,
                Changes = new System.Collections.Generic.List<string> { "stub" }
            };
            vm.AddNewUpdate(newItem);

            var afterRefs = vm.Updates.Where(u => !ReferenceEquals(u, newItem)).ToList();
            // Same number of older items.
            Assert.Equal(beforeRefs.Count, afterRefs.Count);
            // Each old reference is still in the collection at the same logical position
            // (now shifted by +1 because of the Insert(0) prepend).
            for (int i = 0; i < beforeRefs.Count; i++)
            {
                Assert.Same(beforeRefs[i], afterRefs[i]);
                // Fingerprint also identical — no mutation of any field.
                Assert.Equal(
                    beforeFingerprints[beforeRefs[i]],
                    $"{afterRefs[i].Version}|{afterRefs[i].Date:o}|{afterRefs[i].Type}|{afterRefs[i].Title}|{string.Join(",", afterRefs[i].Changes)}");
            }
            // New item at the head.
            Assert.Same(newItem, vm.Updates[0]);
        }

        [Fact]
        public void AddNewUpdate_NullInput_NoOp()
        {
            var vm = new MainWindowViewModel();
            int countBefore = vm.Updates.Count;
            vm.AddNewUpdate(null!);
            Assert.Equal(countBefore, vm.Updates.Count);
            // Exactly one IsLatest=true must still hold (invariant preserved).
            Assert.Single(vm.Updates.Where(u => u.IsLatest));
        }

        [Fact]
        public void AddNewUpdate_CalledTwice_SecondCall_DemotesFirst()
        {
            // Verify the property-based IsLatest logic composes correctly
            // across multiple consecutive releases (the realistic scenario
            // for an auto-update flow that runs every 30 minutes).
            var vm = new MainWindowViewModel();
            var a = new UpdateItem
            {
                Version = "99.99.99",
                Type = "Доступно",
                Title = "A",
                Date = DateTime.UtcNow,
                Changes = new System.Collections.Generic.List<string> { "x" }
            };
            var b = new UpdateItem
            {
                Version = "100.0.0",
                Type = "Доступно",
                Title = "B",
                Date = DateTime.UtcNow,
                Changes = new System.Collections.Generic.List<string> { "y" }
            };

            vm.AddNewUpdate(a);
            Assert.True(a.IsLatest);
            Assert.Same(a, vm.Updates[0]);

            vm.AddNewUpdate(b);
            Assert.True(b.IsLatest);
            Assert.False(a.IsLatest);
            Assert.Same(b, vm.Updates[0]);
            Assert.Same(a, vm.Updates[1]);
            Assert.Single(vm.Updates.Where(u => u.IsLatest));
        }
    }
}
