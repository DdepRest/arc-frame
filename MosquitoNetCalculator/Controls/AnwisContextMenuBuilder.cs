using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// Builds a ContextMenu with radio-style MenuItems for every
    /// <see cref="AnwisSizeMode"/> value, wired to a single callback.
    /// Shared between QuickAddControl (right-click on Тип dropdown)
    /// and OrderItemsControl (right-click on the per-row mode pill).
    /// </summary>
    public static class AnwisContextMenuBuilder
    {
        /// <summary>
        /// Creates a ContextMenu with one <see cref="MenuItem"/> per
        /// <c>AnwisSizeMode</c> enum value, each showing the full label,
        /// description, and a check mark for the currently active mode.
        /// </summary>
        /// <param name="currentMode">Mode currently selected (drives IsChecked).</param>
        /// <param name="onModeSelected">Callback invoked when the user picks a mode.</param>
        /// <param name="placementTarget">Element the menu positions itself relative to.</param>
        /// <param name="menuStyle">
        /// Optional explicit <see cref="Style"/> for the <see cref="ContextMenu"/>.
        /// When <c>null</c> (production default), resolved via
        /// <c>Application.Current.FindResource(typeof(ContextMenu))</c>.
        /// Pass a concrete <see cref="Style"/> in unit tests to avoid the
        /// <see cref="Application.Current"/> dependency.
        /// </param>
        public static ContextMenu Build(
            AnwisSizeMode currentMode,
            Action<AnwisSizeMode> onModeSelected,
            FrameworkElement placementTarget,
            Style? menuStyle = null)
        {
            var menu = new ContextMenu
            {
                Style = menuStyle ?? (Style)Application.Current.FindResource(typeof(ContextMenu))
            };

            foreach (AnwisSizeMode mode in Enum.GetValues<AnwisSizeMode>())
            {
                var mi = new MenuItem
                {
                    Header = $"{AnwisSizeService.FullLabels[mode]} — {AnwisSizeService.Descriptions[mode]}",
                    ToolTip = AnwisSizeService.HintTexts.TryGetValue(mode, out var hint) ? hint : "",
                    IsCheckable = true,
                    IsChecked = currentMode == mode
                };
                var capturedMode = mode;
                mi.Click += (_, _) => onModeSelected(capturedMode);
                menu.Items.Add(mi);
            }

            menu.PlacementTarget = placementTarget;
            menu.Placement = PlacementMode.Bottom;
            return menu;
        }
    }
}
