using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// Unit tests for <see cref="AnwisContextMenuBuilder.Build"/>.
    /// Verifies that the static helper produces a well-formed ContextMenu
    /// with one radio-style MenuItem per <see cref="AnwisSizeMode"/> value,
    /// correct headers/tooltips, accurate IsChecked state, callback wiring,
    /// and Placement configuration.
    ///
    /// All tests pass an explicit <see cref="Style"/> to avoid the
    /// <see cref="Application.Current"/> dependency (which is fragile in
    /// xUnit — Application can only be created once per AppDomain, and
    /// <see cref="AppLifecycleTests"/> may have already shut one down).
    ///
    /// WPF <see cref="FrameworkElement"/> subtypes (Border, ContextMenu,
    /// MenuItem) require an STA thread. xUnit defaults to MTA, so every
    /// test body is dispatched to a dedicated STA thread via
    /// <see cref="RunOnStaThread"/>.
    /// </summary>
    public class AnwisContextMenuBuilderTests
    {
        /// <summary>
        /// Minimal ContextMenu style — a thin shim that satisfies the
        /// <c>Style</c> property assignment in <c>Build()</c> without
        /// requiring <see cref="Application.Current"/> or a full theme
        /// ResourceDictionary load.
        /// </summary>
        private static readonly Style MenuStyle = new Style(typeof(ContextMenu));

        /// <summary>
        /// Runs <paramref name="action"/> on a dedicated STA thread,
        /// re-throwing any exception on the calling thread. Times out
        /// after 10 seconds.
        /// </summary>
        private static void RunOnStaThread(Action action)
        {
            Exception? caught = null;
            using var gate = new ManualResetEventSlim(false);

            var t = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex) { caught = ex; }
                finally { gate.Set(); }
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();

            if (!gate.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("STA thread did not finish within 10 seconds.");

            if (caught != null)
                throw caught;
        }

        /// <summary>
        /// Lightweight element passed as placement target. Created inside
        /// <see cref="RunOnStaThread"/> because <see cref="Border"/> requires STA.
        /// </summary>
        private static FrameworkElement DummyTarget() => new Border();

        // ─── Structural assertions ───────────────────────────────

        [Fact]
        public void Build_Returns_ContextMenu()
        {
            RunOnStaThread(() =>
            {
                var menu = AnwisContextMenuBuilder.Build(AnwisSizeMode.Брусбокс60, _ => { }, DummyTarget(), MenuStyle);
                Assert.NotNull(menu);
                Assert.IsType<ContextMenu>(menu);
            });
        }

        [Fact]
        public void Build_Creates_Five_MenuItems()
        {
            RunOnStaThread(() =>
            {
                var menu = AnwisContextMenuBuilder.Build(AnwisSizeMode.Брусбокс60, _ => { }, DummyTarget(), MenuStyle);
                Assert.Equal(5, menu.Items.Count);
            });
        }

        [Fact]
        public void Build_Items_Are_All_MenuItem()
        {
            RunOnStaThread(() =>
            {
                var menu = AnwisContextMenuBuilder.Build(AnwisSizeMode.Брусбокс60, _ => { }, DummyTarget(), MenuStyle);
                Assert.All(menu.Items.Cast<object>(), item => Assert.IsType<MenuItem>(item));
            });
        }

        [Fact]
        public void Build_MenuItems_Follow_Enum_Declaration_Order()
        {
            // Enum order: Брусбокс60=0, Брусбокс70=1, Профипласт=2,
            // РазмерПроёма=3, Габаритный=4.
            RunOnStaThread(() =>
            {
                var menu = AnwisContextMenuBuilder.Build(AnwisSizeMode.Брусбокс60, _ => { }, DummyTarget(), MenuStyle);

                var headers = menu.Items.Cast<MenuItem>().Select(mi => mi.Header.ToString()).ToList();
                Assert.Contains("Брусбокс 60", headers[0]);
                Assert.Contains("Брусбокс 70", headers[1]);
                Assert.Contains("Профипласт",   headers[2]);
                Assert.Contains("Размер проёма", headers[3]);
                Assert.Contains("Габаритный",   headers[4]);
            });
        }

        // ─── IsCheckable / IsChecked ─────────────────────────────

        [Fact]
        public void Build_All_Items_Are_Checkable()
        {
            RunOnStaThread(() =>
            {
                var menu = AnwisContextMenuBuilder.Build(AnwisSizeMode.Брусбокс60, _ => { }, DummyTarget(), MenuStyle);
                Assert.All(menu.Items.Cast<MenuItem>(), mi => Assert.True(mi.IsCheckable));
            });
        }

        [Theory]
        [InlineData(AnwisSizeMode.Брусбокс60)]
        [InlineData(AnwisSizeMode.Брусбокс70)]
        [InlineData(AnwisSizeMode.Профипласт)]
        [InlineData(AnwisSizeMode.РазмерПроёма)]
        [InlineData(AnwisSizeMode.Габаритный)]
        public void Build_Exactly_One_Item_IsChecked(AnwisSizeMode current)
        {
            RunOnStaThread(() =>
            {
                var menu = AnwisContextMenuBuilder.Build(current, _ => { }, DummyTarget(), MenuStyle);
                var checkedCount = menu.Items.Cast<MenuItem>().Count(mi => mi.IsChecked);
                Assert.Equal(1, checkedCount);
            });
        }

        [Theory]
        [InlineData(AnwisSizeMode.Брусбокс60)]
        [InlineData(AnwisSizeMode.Брусбокс70)]
        [InlineData(AnwisSizeMode.Профипласт)]
        [InlineData(AnwisSizeMode.РазмерПроёма)]
        [InlineData(AnwisSizeMode.Габаритный)]
        public void Build_Only_CurrentMode_IsChecked(AnwisSizeMode current)
        {
            RunOnStaThread(() =>
            {
                var menu = AnwisContextMenuBuilder.Build(current, _ => { }, DummyTarget(), MenuStyle);

                foreach (MenuItem mi in menu.Items)
                {
                    bool isCurrent = mi.Header.ToString()!
                        .StartsWith(AnwisSizeService.FullLabels[current], StringComparison.Ordinal);
                    Assert.Equal(isCurrent, mi.IsChecked);
                }
            });
        }

        // ─── Header / ToolTip content ────────────────────────────

        [Fact]
        public void Build_MenuItem_Headers_Contain_FullLabel_And_Description()
        {
            RunOnStaThread(() =>
            {
                var menu = AnwisContextMenuBuilder.Build(AnwisSizeMode.Брусбокс60, _ => { }, DummyTarget(), MenuStyle);

                foreach (MenuItem mi in menu.Items)
                {
                    var header = mi.Header.ToString()!;
                    // Format: "FullLabel — Description"
                    Assert.Contains(" — ", header);

                    // At least one known FullLabel or Description should appear
                    bool hasKnownLabel = Enum.GetValues<AnwisSizeMode>()
                        .Any(m => header.StartsWith(AnwisSizeService.FullLabels[m], StringComparison.Ordinal));
                    Assert.True(hasKnownLabel, $"Header '{header}' does not start with any known FullLabel");
                }
            });
        }

        [Fact]
        public void Build_MenuItem_ToolTips_From_HintTexts()
        {
            // HintTexts is a full dictionary — every mode has a non-empty hint.
            RunOnStaThread(() =>
            {
                var menu = AnwisContextMenuBuilder.Build(AnwisSizeMode.Брусбокс60, _ => { }, DummyTarget(), MenuStyle);

                var itemsByHeader = menu.Items.Cast<MenuItem>().ToList();
                foreach (AnwisSizeMode mode in Enum.GetValues<AnwisSizeMode>())
                {
                    var mi = itemsByHeader.Find(
                        i => i.Header.ToString()!.StartsWith(
                            AnwisSizeService.FullLabels[mode], StringComparison.Ordinal));
                    Assert.NotNull(mi);

                    var expectedHint = AnwisSizeService.HintTexts[mode];
                    Assert.Equal(expectedHint, mi!.ToolTip);
                }
            });
        }

        [Fact]
        public void Build_Every_MenuItem_Has_NonEmpty_Header()
        {
            RunOnStaThread(() =>
            {
                var menu = AnwisContextMenuBuilder.Build(AnwisSizeMode.Брусбокс60, _ => { }, DummyTarget(), MenuStyle);
                Assert.All(menu.Items.Cast<MenuItem>(),
                    mi => Assert.False(string.IsNullOrWhiteSpace(mi.Header?.ToString())));
            });
        }

        // ─── Callback invocation ─────────────────────────────────

        [Fact]
        public void Build_Click_On_First_Item_Invokes_Callback_With_First_Mode()
        {
            RunOnStaThread(() =>
            {
                AnwisSizeMode? received = null;
                var menu = AnwisContextMenuBuilder.Build(
                    AnwisSizeMode.Брусбокс60,
                    mode => received = mode,
                    DummyTarget(),
                    MenuStyle);

                var firstItem = (MenuItem)menu.Items[0];
                firstItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                // First enum value is Брусбокс60.
                Assert.Equal(AnwisSizeMode.Брусбокс60, received);
            });
        }

        [Fact]
        public void Build_Click_On_Third_Item_Invokes_Callback_With_Third_Mode()
        {
            RunOnStaThread(() =>
            {
                AnwisSizeMode? received = null;
                var menu = AnwisContextMenuBuilder.Build(
                    AnwisSizeMode.Брусбокс60,
                    mode => received = mode,
                    DummyTarget(),
                    MenuStyle);

                var thirdItem = (MenuItem)menu.Items[2]; // Профипласт
                thirdItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                Assert.Equal(AnwisSizeMode.Профипласт, received);
            });
        }

        [Fact]
        public void Build_Click_On_Last_Item_Invokes_Callback_With_Last_Mode()
        {
            RunOnStaThread(() =>
            {
                AnwisSizeMode? received = null;
                var menu = AnwisContextMenuBuilder.Build(
                    AnwisSizeMode.Брусбокс60,
                    mode => received = mode,
                    DummyTarget(),
                    MenuStyle);

                var lastItem = (MenuItem)menu.Items[4]; // Габаритный
                lastItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                Assert.Equal(AnwisSizeMode.Габаритный, received);
            });
        }

        [Fact]
        public void Build_Each_Item_Invokes_Callback_With_Its_Own_Mode()
        {
            // Verify that every menu item's closure captures the correct mode
            // — the "foreach capturedMode" anti-pattern does not apply.
            RunOnStaThread(() =>
            {
                var receivedModes = new List<AnwisSizeMode>();
                var menu = AnwisContextMenuBuilder.Build(
                    AnwisSizeMode.Брусбокс60,
                    mode => receivedModes.Add(mode),
                    DummyTarget(),
                    MenuStyle);

                foreach (MenuItem mi in menu.Items)
                    mi.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                Assert.Equal(5, receivedModes.Count);
                Assert.Equal(AnwisSizeMode.Брусбокс60,   receivedModes[0]);
                Assert.Equal(AnwisSizeMode.Брусбокс70,   receivedModes[1]);
                Assert.Equal(AnwisSizeMode.Профипласт,   receivedModes[2]);
                Assert.Equal(AnwisSizeMode.РазмерПроёма, receivedModes[3]);
                Assert.Equal(AnwisSizeMode.Габаритный,   receivedModes[4]);
            });
        }

        // ─── Placement configuration ─────────────────────────────

        [Fact]
        public void Build_Sets_PlacementTarget()
        {
            RunOnStaThread(() =>
            {
                var target = new Border();
                var menu = AnwisContextMenuBuilder.Build(AnwisSizeMode.Брусбокс60, _ => { }, target, MenuStyle);
                Assert.Same(target, menu.PlacementTarget);
            });
        }

        [Fact]
        public void Build_Sets_Placement_To_Bottom()
        {
            RunOnStaThread(() =>
            {
                var menu = AnwisContextMenuBuilder.Build(AnwisSizeMode.Брусбокс60, _ => { }, DummyTarget(), MenuStyle);
                Assert.Equal(PlacementMode.Bottom, menu.Placement);
            });
        }

        [Fact]
        public void Build_ContextMenu_Has_Style()
        {
            RunOnStaThread(() =>
            {
                var menu = AnwisContextMenuBuilder.Build(AnwisSizeMode.Брусбокс60, _ => { }, DummyTarget(), MenuStyle);
                Assert.NotNull(menu.Style);
                Assert.Same(MenuStyle, menu.Style);
            });
        }
    }
}
