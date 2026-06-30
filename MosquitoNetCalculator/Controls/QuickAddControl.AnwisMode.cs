using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    public partial class QuickAddControl
    {
        /// <summary>
        /// Right-click on the «Тип» dropdown opens a radio-style context menu
        /// for Anwis mode selection — kept as fallback for experienced users.
        /// </summary>
        private void CmbQuickType_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!AnwisSizeService.IsApplicable(CmbQuickType.SelectedItem as string))
                return;

            e.Handled = true; // Suppress native ComboBox context menu

            var menu = AnwisContextMenuBuilder.Build(
                SelectedAnwisMode,
                mode =>
                {
                    SelectedAnwisMode = mode;
                    UpdateAnwisModePills();
                    UpdateAnwisModeToolTip();
                    UpdateQuickPreview();
                },
                CmbQuickType);

            menu.IsOpen = true;
        }

        /// <summary>
        /// Left-click on an Anwis mode pill — sets the mode and highlights the pill.
        /// </summary>
        private void AnwisModePill_Click(object sender, MouseButtonEventArgs e)
        {
            var pill = sender as Border;
            if (pill == null) return;

            AnwisSizeMode? mode = pill.Name switch
            {
                nameof(PillBB60) => AnwisSizeMode.Брусбокс60,
                nameof(PillBB70) => AnwisSizeMode.Брусбокс70,
                nameof(PillPP)   => AnwisSizeMode.Профипласт,
                nameof(PillProem) => AnwisSizeMode.РазмерПроёма,
                nameof(PillGab)   => AnwisSizeMode.Габаритный,
                _ => null
            };

            if (!mode.HasValue) return;

            SelectedAnwisMode = mode.Value;
            UpdateAnwisModePills();
            UpdateAnwisModeToolTip();
            UpdateQuickPreview();

            e.Handled = true;
        }

        /// <summary>
        /// Shows or hides the Anwis mode pill panel with fade+slide animation.
        /// </summary>
        private void ToggleAnwisModePanel(bool show)
        {
            if (show)
            {
                // Cancel any in-progress hide animation before showing.
                PanelAnwisModes.BeginAnimation(OpacityProperty, null);
                var existingTransform = PanelAnwisModes.RenderTransform as TranslateTransform;
                if (existingTransform != null)
                    existingTransform.BeginAnimation(TranslateTransform.YProperty, null);

                PanelAnwisModes.Visibility = Visibility.Visible;
                PanelAnwisModes.Opacity = 0;
                PanelAnwisModes.RenderTransform = new TranslateTransform(0, -8);

                var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(250))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                PanelAnwisModes.BeginAnimation(OpacityProperty, fadeIn);

                var slideDown = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var t = (TranslateTransform)PanelAnwisModes.RenderTransform;
                t.BeginAnimation(TranslateTransform.YProperty, slideDown);

                UpdateAnwisModePills();
            }
            else
            {
                // Only animate out if currently visible.
                if (PanelAnwisModes.Visibility != Visibility.Visible)
                    return;

                var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                fadeOut.Completed += (_, _) =>
                {
                    PanelAnwisModes.Visibility = Visibility.Collapsed;
                };
                PanelAnwisModes.BeginAnimation(OpacityProperty, fadeOut);

                var slideUp = new DoubleAnimation(8, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var t = PanelAnwisModes.RenderTransform as TranslateTransform;
                if (t != null)
                    t.BeginAnimation(TranslateTransform.YProperty, slideUp);
            }
        }

        /// <summary>
        /// Updates the visual state of all Anwis mode segments to reflect
        /// <see cref="SelectedAnwisMode"/> — one active (Accent), rest transparent.
        /// Also hides separators adjacent to the active segment so the accent
        /// fill is not interrupted by divider lines.
        /// </summary>
        private void UpdateAnwisModePills()
        {
            var r = Application.Current.Resources;
            Brush activeBg = (Brush)r["Accent"];
            Brush activeFg = (Brush)r["OnAccent"];
            Brush inactiveFg = (Brush)r["TextSecondary"];
            Brush sepBrush = (Brush)r["SubtleBorder"];

            var pills = new[] { PillBB60, PillBB70, PillPP, PillProem, PillGab };
            var seps  = new[] { Sep1, Sep2, Sep3, Sep4 };
            var modes = new[]
            {
                AnwisSizeMode.Брусбокс60,
                AnwisSizeMode.Брусбокс70,
                AnwisSizeMode.Профипласт,
                AnwisSizeMode.РазмерПроёма,
                AnwisSizeMode.Габаритный
            };

            for (int i = 0; i < pills.Length; i++)
            {
                bool isActive = SelectedAnwisMode == modes[i];
                pills[i].Background = isActive ? activeBg : Brushes.Transparent;
                if (pills[i].Child is TextBlock tb)
                    tb.Foreground = isActive ? activeFg : inactiveFg;
            }

            // Hide separators adjacent to the active segment.
            for (int i = 0; i < seps.Length; i++)
            {
                // Sep[i] sits between pill[i] and pill[i+1] — hide if either neighbor is active.
                bool adjacent = SelectedAnwisMode == modes[i] || SelectedAnwisMode == modes[i + 1];
                seps[i].Fill = adjacent ? Brushes.Transparent : sepBrush;
            }

            // Visible hint below the segmented control.
            var (input, _, _) = AnwisSizeService.GetExplanation(SelectedAnwisMode);
            TxtAnwisModeHint.Text = input;
        }

        /// <summary>
        /// Applies hover styling to a segment — AccentHover for the active
        /// segment, AccentLight for inactive ones.
        /// </summary>
        private void HoverSegment(Border pill, AnwisSizeMode mode)
        {
            var r = Application.Current.Resources;
            bool isActive = SelectedAnwisMode == mode;
            pill.Background = isActive
                ? (Brush)r["AccentHover"]
                : (Brush)r["AccentLight"];
            if (pill.Child is TextBlock tb)
                tb.Foreground = isActive
                    ? (Brush)r["OnAccent"]
                    : (Brush)r["TextPrimary"];
        }

        /// <summary>
        /// Resets Anwis mode to <see cref="AnwisSizeService.DefaultMode"/>
        /// and clears the ToolTip. Called by <see cref="MainWindow.StartNewOrder"/>
        /// and <see cref="MainWindow.OpenSelectedOrder"/> so the user always
        /// starts a fresh order with the default mode.
        /// </summary>
        public void ResetAnwisMode()
        {
            SelectedAnwisMode = AnwisSizeService.DefaultMode;
            UpdateAnwisModePills();
            UpdateAnwisModeToolTip();
        }

        /// <summary>
        /// Sets/clears the ToolTip on <see cref="CmbQuickType"/> to indicate
        /// the current Anwis mode and the right-click gesture — 0 px of workspace.
        /// </summary>
        private void UpdateAnwisModeToolTip()
        {
            if (!AnwisSizeService.IsApplicable(CmbQuickType.SelectedItem as string))
            {
                ToolTipService.SetToolTip(CmbQuickType, null);
                return;
            }
            ToolTipService.SetToolTip(CmbQuickType,
                $"Текущий режим: {AnwisSizeService.ShortLabels[SelectedAnwisMode]} (ПКМ для изменения)");
        }
    }
}
