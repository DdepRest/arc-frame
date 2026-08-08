using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Manages application light/dark theme switching.
    /// Persists the selected theme to settings.json and updates
    /// all named SolidColorBrush resources in App.xaml at runtime.
    /// </summary>
    public static class ThemeService
    {
        // Theme persistence is now handled by AppSettingsService.
        // ThemeService keeps its own file path for backward compatibility during transition.

        public static bool IsDarkTheme { get; private set; } = AppSettingsService.LoadTheme() == "dark";

        public static event Action? ThemeChanged;

        /// <summary>
        /// Loads the saved theme preference and applies it.
        /// Defaults to light theme on first run (when no saved preference exists).
        /// </summary>
        public static void LoadTheme()
        {
            IsDarkTheme = AppSettingsService.LoadTheme() == "dark";
            ApplyTheme();
        }

        /// <summary>
        /// Toggles between light and dark themes, saves preference, and notifies listeners.
        /// </summary>
        public static void ToggleTheme()
        {
            IsDarkTheme = !IsDarkTheme;
            AppSettingsService.SaveTheme(IsDarkTheme ? "dark" : "light");
            ApplyTheme();
            ThemeChanged?.Invoke();
        }

        // SaveTheme removed — theme persistence now handled by AppSettingsService.

        /// <summary>
        /// Default duration of the colour-transition animation when toggling themes
        /// at runtime. Long enough to feel smooth, short enough to never feel slow.
        /// </summary>
        public static TimeSpan TransitionDuration { get; set; } = TimeSpan.FromMilliseconds(280);

        /// <summary>
        /// Returns the TARGET Surface color for the current theme.
        ///
        /// This is used by callers (e.g. ApplyMicaTitleBar) that need to
        /// create a semi-transparent copy of the Surface colour. Those
        /// callers must NOT read FindResource("Surface") during a
        /// ThemeChanged callback — the brush may be mid-animation (still
        /// showing the OLD theme's colour), so FindResource would return
        /// the stale colour and the copy would be stuck on the wrong theme.
        ///
        /// This method reads the definitive target colour directly from
        /// the colour dictionary (Light/Dark), bypassing the animated brush.
        /// </summary>
        public static Color GetCurrentSurfaceColor()
        {
            var colors = IsDarkTheme ? DarkColors : LightColors;
            return ParseColor(colors["Surface"]);
        }

        /// <summary>
        /// Applies the current theme to the application resource dictionary.
        ///
        /// For SolidColorBrush resources that are still mutable (not frozen
        /// AND not sealed), we update their <see cref="SolidColorBrush.Color"/>
        /// in place through a <see cref="ColorAnimation"/>. This is the key
        /// to a smooth transition:
        ///   - The brush instance is preserved, so every DynamicResource
        ///     reference in the visual tree keeps pointing at the same object —
        ///     no re-binding happens.
        ///   - WPF only needs to invalidate the small set of DependencyProperties
        ///     that depend on the brush, and the GPU smoothly interpolates the
        ///     colour over the animation duration.
        ///
        /// IMPORTANT: WPF seals a Freezable (sets its internal read-only flag)
        /// the moment it ends up inside a frozen Style or ControlTemplate —
        /// for example, a brush used as <c>&lt;Setter Value="{DynamicResource X}"/&gt;</c>
        /// gets sealed when the Style is sealed during app startup. Sealed
        /// objects throw from <see cref="System.Windows.Media.Animation.Animatable.BeginAnimation"/>
        /// even though <see cref="Freezable.IsFrozen"/> still returns false,
        /// so we have to use a try/catch — the public IsFrozen property alone
        /// is not enough to detect a non-animatable brush.
        ///
        /// If the brush is frozen, sealed, or the resource is a plain
        /// <see cref="Color"/>, we fall back to creating a fresh brush,
        /// starting its animation BEFORE adding it to the dictionary (Add
        /// can freeze the value, and Freeze cancels running animations), and
        /// only then doing the dictionary swap.
        /// </summary>
        public static void ApplyTheme(TimeSpan? transitionDuration = null)
        {
            var app = Application.Current;
            if (app == null) return;

            // ── Tell Windows the process-level dark-mode preference has changed.
            App.NotifyThemeChanged(IsDarkTheme);

            var colors = IsDarkTheme ? DarkColors : LightColors;
            var duration = transitionDuration ?? TransitionDuration;
            bool animate = duration > TimeSpan.Zero;

            foreach (var pair in colors)
            {
                var oldValue = app.Resources[pair.Key];
                var newColor = ParseColor(pair.Value);

                // Fast path: animate the existing brush in place. Preserves
                // every DynamicResource binding in the visual tree.
                if (oldValue is SolidColorBrush brush && animate
                    && TryAnimateBrushColor(brush, newColor, duration))
                {
                    continue;
                }

                // Slow path: the brush is frozen/sealed (BeginAnimation would
                // throw), the resource is a plain Color (ColorAnimation can't
                // target a raw Color), or animation is disabled. Replace the
                // resource with a fresh brush, starting the animation BEFORE
                // Add so the brush is still mutable at the moment BeginAnimation
                // runs.
                Color? oldColor = oldValue switch
                {
                    SolidColorBrush sb => sb.Color,
                    Color c => c,
                    _ => null
                };

                if (oldValue != null)
                    app.Resources.Remove(pair.Key);

                if (oldValue is Color)
                {
                    // Plain Color resources (e.g. ShadowColor, GlowAccent)
                    // can't be animated in place — ColorAnimation targets
                    // a Brush's Color DependencyProperty, not a raw Color.
                    // Snap to the new value; the visual mismatch is small
                    // (subtle drop-shadow tint shifts).
                    app.Resources[pair.Key] = newColor;
                    continue;
                }

                // Build a fresh brush at the old colour so the animation
                // interpolates from the correct starting point.
                var newBrush = oldColor.HasValue
                    ? new SolidColorBrush(oldColor.Value)
                    : new SolidColorBrush(newColor);

                if (animate && oldColor.HasValue && oldColor.Value != newColor)
                {
                    // The new brush is freshly constructed and therefore
                    // guaranteed not to be frozen or sealed, so
                    // BeginAnimation is safe to call. Note that
                    // Application.Resources.Add does NOT freeze the value
                    // (only Style.Resources / ControlTemplate.Resources /
                    // DataTemplate.Resources do), so the animation keeps
                    // running after the dictionary swap.
                    var anim = new ColorAnimation
                    {
                        To = newColor,
                        Duration = duration,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                    };
                    newBrush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
                }

                app.Resources.Remove(pair.Key);
                app.Resources[pair.Key] = newBrush;
            }
        }

        /// <summary>
        /// Tries to animate <paramref name="brush"/>.Color in place from its
        /// current value to <paramref name="targetColor"/>. Returns false
        /// (without throwing) if the brush is frozen or sealed, so the caller
        /// can fall back to a replace strategy.
        ///
        /// We must catch <see cref="InvalidOperationException"/> rather than
        /// pre-checking <see cref="Freezable.IsFrozen"/>, because a brush
        /// that has been "sealed" by being used inside a frozen Style or
        /// ControlTemplate throws from BeginAnimation even though IsFrozen
        /// returns false — the public IsFrozen property does not surface
        /// the internal read-only flag that WPF sets on such brushes.
        /// </summary>
        private static bool TryAnimateBrushColor(SolidColorBrush brush, Color targetColor, Duration duration)
        {
            if (brush.IsFrozen) return false;
            if (brush.Color == targetColor) return true;

            try
            {
                var animation = new ColorAnimation
                {
                    To = targetColor,
                    Duration = duration,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                };
                brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
                return true;
            }
            catch (InvalidOperationException)
            {
                // Brush is sealed (read-only) — BeginAnimation refuses to
                // touch it. Caller must create a fresh brush to animate.
                return false;
            }
        }

        private static Color ParseColor(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        // ContractPrefix methods removed — use AppSettingsService.LoadContractPrefix / SaveContractPrefix instead.

        // ─────────────────────────────────────────────────────────
        // Light theme — modern card design with clear hierarchy
        //
        // Elevation system:
        //   L0  AppBg      #F5F5F7  — page background
        //   L1  Surface    #FFFFFF  — cards, panels
        //   L2  QuickBg    #FCFCFD  — interactive cards
        //   L3  HeaderBg   #F8F8FA  — column headers
        // ─────────────────────────────────────────────────────────
        private static readonly Dictionary<string, string> LightColors = new()
        {
            // Backgrounds
            ["AppBg"]        = "#F5F5F7",
            ["Surface"]       = "#FFFFFF",
            ["QuickBg"]       = "#FCFCFD",
            ["RowHover"]      = "#F0F0F5",
            ["RowAlt"]        = "#FAFAFD",
            ["RowAltHover"]   = "#F0F0F5",
            ["RowAltSelected"] = "#E8F0FA",
            // Row-hover/select animation targets (Color)
            ["RowHoverColor"]    = "#F0F0F5",
            ["AccentLightColor"] = "#E8F0FA",
            // Accent — professional blue
            ["Accent"]        = "#3878C8",
            ["AccentHover"]   = "#4A90E0",
            ["AccentPress"]   = "#2A60AA",
            ["AccentLight"]   = "#EBF3FC",
            ["AccentShadowColor"] = "#3878C8",
            // Text
            ["TextPrimary"]   = "#1A1A24",
            ["TextSecondary"] = "#585868",
            ["TextMuted"]     = "#90909C",
            // Borders
            ["Border"]        = "#E0E0E8",
            ["BorderHover"]   = "#C0C0CC",
            ["SubtleBorder"]  = "#EEEEF2",
            ["GridLine"]      = "#F0F0F5",
            ["TrackBg"]       = "#E0E0E8",
            ["ScrollBarThumb"] = "#B8B8C4",
            // Semantic
            ["Success"]       = "#0F7B3F",
            ["SuccessHover"]  = "#0A6333",
            ["Danger"]        = "#C42B1C",
            ["DangerHover"]   = "#A61B10",
            ["Warning"]       = "#D48C00",
            ["DangerLight"]   = "#FDE7E9",
            ["DangerGhostBorder"] = "#F0C6CA",
            // On-accent text
            ["OnAccent"]      = "#FFFFFF",
            ["OnSuccess"]     = "#FFFFFF",
            ["OnDanger"]      = "#FFFFFF",
            // Ghost button
            ["GhostBg"]       = "#FFFFFF",
            ["GhostBorder"]   = "#D4D4DC",
            // Section card
            ["SectionBg"]     = "#F8F9FB",
            ["SectionAccent"] = "#3878C8",
            // Glow / shadow
            ["GlowAccent"]    = "#3878C8",
            ["SuccessShadow"] = "#0F7B3F",
            ["DangerShadow"]  = "#C42B1C",
            // Total bar
            ["TotalBg"]       = "#1A1A24",
            ["TotalText"]     = "#FFFFFF",
            ["TotalTextMuted"] = "#A0A0AC",
            // Quick-add / chips
            ["ChipBg"]        = "#EBF3FC",
            // DataGrid headers — Fluent light
            ["HeaderBg"]      = "#F9F9FC",
            ["HeaderBorder"]  = "#E8E8F0",
            ["HeaderText"]    = "#484858",
            ["HeaderPress"]   = "#E8E8EC",
            // Menu
            ["MenuItemPressed"] = "#E8EDF5",
            // Shadow
            ["ShadowColor"]   = "#1A1A24",
            // Badges
            ["BadgeDefaultBg"]  = "#EBF3FC",
            ["BadgeDefaultFg"]  = "#3878C8",
            ["BadgeSuccessBg"]  = "#E6F5EC",
            ["BadgeSuccessFg"]  = "#0F7B3F",
            ["BadgeWarningBg"]  = "#FFF4CE",
            ["BadgeWarningFg"]  = "#D48C00",
            ["BadgeDangerBg"]   = "#FDE7E9",
            ["BadgeDangerFg"]   = "#C42B1C",
            // Install toggle
            ["InstallGreen"]  = "#0F7B3F",
            ["InstallRed"]    = "#C42B1C",
            ["InstallGray"]   = "#8A8A9A",
        };

        // ─────────────────────────────────────────────────────────
        // Dark theme — high-contrast modern card design
        //
        // Elevation system (4 visible levels):
        //   L0  AppBg      #0F0F12  — deepest page background
        //   L0.5 RowAlt    #16161A  — alternating row background
        //   L1  Surface    #1C1C22  — cards, panels, tables
        //   L2  QuickBg    #25252D  — elevated interactive cards
        //   L3  HeaderBg   #2E2E38  — column headers, prominent panels
        // ─────────────────────────────────────────────────────────
        private static readonly Dictionary<string, string> DarkColors = new()
        {
            // Backgrounds — 4-level elevation
            ["AppBg"]        = "#0F0F12",
            ["Surface"]       = "#1C1C22",
            ["QuickBg"]       = "#25252D",
            ["RowAlt"]        = "#16161A",
            ["RowHover"]      = "#2A2A35",
            ["RowAltHover"]   = "#2A2A35",
            ["RowAltSelected"] = "#1E3050",
            // Row-hover/select animation targets (Color)
            ["RowHoverColor"]    = "#2A2A35",
            ["AccentLightColor"] = "#1E3050",
            // Accent — modern blue, stands out on dark
            ["Accent"]        = "#5299E0",
            ["AccentHover"]   = "#6BAFEF",
            ["AccentPress"]   = "#4088CC",
            ["AccentLight"]   = "#1A3050",
            ["AccentShadowColor"] = "#5299E0",
            // Text — bright, high-contrast
            ["TextPrimary"]   = "#FFFFFF",
            ["TextSecondary"] = "#C8C8D0",
            ["TextMuted"]     = "#888896",
            // Borders — visible but not aggressive
            ["Border"]        = "#353540",
            ["BorderHover"]   = "#505060",
            ["SubtleBorder"]  = "#2A2A34",
            ["GridLine"]      = "#22222C",
            ["TrackBg"]       = "#30303A",
            ["ScrollBarThumb"] = "#606070",
            ["HeaderBorder"]  = "#3A3A48",
            // Semantic
            ["Success"]       = "#4CC97D",
            ["SuccessHover"]  = "#5DE08F",
            ["Danger"]        = "#FF6B6B",
            ["DangerHover"]   = "#FF8585",
            ["Warning"]       = "#FFB347",
            ["DangerLight"]   = "#2A1820",
            ["DangerGhostBorder"] = "#4A2838",
            // On-accent text — dark text on bright buttons
            ["OnAccent"]      = "#FFFFFF",
            ["OnSuccess"]     = "#0D1F14",
            ["OnDanger"]      = "#FFFFFF",
            // Ghost button — elevated from surface
            ["GhostBg"]       = "#2A2A34",
            ["GhostBorder"]   = "#404050",
            // Section card — sidebar sub-cards
            ["SectionBg"]     = "#22222C",
            ["SectionAccent"] = "#5299E0",
            // Glow / shadow
            ["GlowAccent"]    = "#5299E0",
            ["SuccessShadow"] = "#4CC97D",
            ["DangerShadow"]  = "#FF6B6B",
            // Total bar
            ["TotalBg"]       = "#141418",
            ["TotalText"]     = "#FFFFFF",
            ["TotalTextMuted"] = "#909098",
            // Quick-add / chips — visible accent
            ["ChipBg"]        = "#1A3050",
            // DataGrid headers — elevated
            ["HeaderBg"]      = "#2E2E38",
            ["HeaderBorder"]  = "#3A3A48",
            ["HeaderText"]    = "#D0D0D8",
            ["HeaderPress"]   = "#22222A",
            // Menu
            ["MenuItemPressed"] = "#1E3050",
            // Shadow
            ["ShadowColor"]   = "#000000",
            // Badges
            ["BadgeDefaultBg"]  = "#1A3050",
            ["BadgeDefaultFg"]  = "#5299E0",
            ["BadgeSuccessBg"]  = "#142820",
            ["BadgeSuccessFg"]  = "#5BC98A",
            ["BadgeWarningBg"]  = "#2A2014",
            ["BadgeWarningFg"]  = "#FFB347",
            ["BadgeDangerBg"]   = "#2A1820",
            ["BadgeDangerFg"]   = "#FF6B6B",
            // Install toggle
            ["InstallGreen"]  = "#4CC97D",
            ["InstallRed"]    = "#FF6B6B",
            ["InstallGray"]   = "#808090",
        };
    }
}
