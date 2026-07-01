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
        // Light theme colors — Windows 11 Fluent Design
        // ─────────────────────────────────────────────────────────
        private static readonly Dictionary<string, string> LightColors = new()
        {
            // Backgrounds
            ["AppBg"]        = "#F3F3F3",
            ["Surface"]       = "#FFFFFF",
            ["QuickBg"]       = "#F9F9F9",
            ["RowHover"]      = "#F5F5F5",
            ["RowAlt"]        = "#FAFAFA",
            ["RowAltHover"]   = "#F0F0F0",
            ["RowAltSelected"] = "#EBF3FD",
            // Row-hover/select animation targets (Color, not Brush — used by
            // DataGridRow Style to animate Background smoothly in light theme)
            ["RowHoverColor"]    = "#F5F5F5",
            ["AccentLightColor"] = "#EBF3FD",
            // Accent (Windows 11 Blue)
            ["Accent"]        = "#005FB8",
            ["AccentHover"]   = "#004E99",
            ["AccentPress"]   = "#003E7A",
            ["AccentLight"]   = "#EBF3FD",
            // AccentShadowColor — mirror of Accent (brush) as Color for NavOrdersBadge DropShadow.
            // WPF cannot bind a Brush to a Color DependencyProperty.
            ["AccentShadowColor"] = "#005FB8",
            // Text
            ["TextPrimary"]   = "#1A1A1A",
            ["TextSecondary"] = "#5D5D5D",
            ["TextMuted"]     = "#8A8A8A",
            // Borders
            ["Border"]        = "#D9D9D9",
            ["BorderHover"]   = "#B8B8B8",
            ["SubtleBorder"]  = "#EBEBEB",
            ["GridLine"]      = "#F0F0F0",
            ["TrackBg"]       = "#D4D4D4",
            ["ScrollBarThumb"] = "#B0B0B0",
            // Semantic
            ["Success"]       = "#0F7B3F",
            ["SuccessHover"]  = "#0A6333",
            ["Danger"]        = "#C42B1C",
            ["DangerHover"]   = "#A61B10",
            ["Warning"]       = "#D48C00",
            ["DangerLight"]   = "#FDE7E9",
            ["DangerGhostBorder"] = "#F0C6CA",
            // On-accent text (text on Accent/Success/Danger button) — dark on light bg
            ["OnAccent"]      = "#FFFFFF",
            ["OnSuccess"]     = "#FFFFFF",
            ["OnDanger"]      = "#FFFFFF",
            // Ghost button — slightly elevated surface for visibility
            ["GhostBg"]       = "#FFFFFF",
            ["GhostBorder"]   = "#D0D0D0",
            // Section card — subtle tinted background for sidebar sub-cards
            ["SectionBg"]     = "#F8F9FB",
            ["SectionAccent"] = "#005FB8",
            // Glow color for button hover
            ["GlowAccent"]    = "#005FB8",
            ["SuccessShadow"] = "#0F7B3F",
            ["DangerShadow"]  = "#C42B1C",
            // Total bar
            ["TotalBg"]       = "#1B1B1B",
            ["TotalText"]     = "#FFFFFF",
            ["TotalTextMuted"] = "#A0A0A0",
            // Quick-add / chips
            ["ChipBg"]        = "#EFF4FB",
            // DataGrid headers — Fluent light
            ["HeaderBg"]      = "#F9F9F9",
            ["HeaderBorder"]  = "#E8E8E8",
            ["HeaderText"]    = "#424242",
            ["HeaderPress"]   = "#E0E0E0",
            // Menu
            ["MenuItemPressed"] = "#E8EDF5",
            // Shadow
            ["ShadowColor"]   = "#1A1A1A",
            // Badges
            ["BadgeDefaultBg"]  = "#EFF4FB",
            ["BadgeDefaultFg"]  = "#005FB8",
            ["BadgeSuccessBg"]  = "#E6F5EC",
            ["BadgeSuccessFg"]  = "#0F7B3F",
            ["BadgeWarningBg"]  = "#FFF4CE",
            ["BadgeWarningFg"]  = "#D48C00",
            ["BadgeDangerBg"]   = "#FDE7E9",
            ["BadgeDangerFg"]   = "#C42B1C",
            // Install toggle
            ["InstallGreen"]  = "#0F7B3F",
            ["InstallRed"]    = "#C42B1C",
            ["InstallGray"]   = "#8A8A8A",
        };

        // ─────────────────────────────────────────────────────────
        // Dark theme colors — Windows 11 Fluent Design (Dark)
        //
        // Elevation system (Windows 11 Dark):
        //   L0  AppBg      #202020  — deepest page background
        //   L0.5 RowAlt    #252525  — alternating rows
        //   L1  Surface    #2D2D2D  — cards, panels, table bodies
        //   L2  HeaderBg   #383838  — elevated panels, column headers
        // ─────────────────────────────────────────────────────────
        private static readonly Dictionary<string, string> DarkColors = new()
        {
            // Backgrounds
            ["AppBg"]        = "#202020",
            ["Surface"]       = "#2D2D2D",
            ["QuickBg"]       = "#2D2D2D",
            ["RowAlt"]        = "#252525",
            ["RowHover"]      = "#383838",
            ["RowAltHover"]   = "#383838",
            ["RowAltSelected"] = "#2A4A6E",
            // Row-hover/select animation targets (Color) for dark theme
            ["RowHoverColor"]    = "#383838",
            ["AccentLightColor"] = "#1A3A54",
            // Accent (Windows 11 Dark Blue)
            ["Accent"]        = "#60CDFF",
            ["AccentHover"]   = "#4DB8E8",
            ["AccentPress"]   = "#3AA0D0",
            ["AccentLight"]   = "#1A3A54",
            // AccentShadowColor — mirror of Accent for DropShadowEffect on dark theme.
            // Same hue as Accent brush — glow is brighter cyan on dark background.
            ["AccentShadowColor"] = "#60CDFF",
            // Text
            ["TextPrimary"]   = "#FFFFFF",
            ["TextSecondary"] = "#C5C5C5",
            ["TextMuted"]     = "#8A8A8A",
            // Borders
            ["Border"]        = "#4A4A4A",
            ["BorderHover"]   = "#5F5F5F",
            ["SubtleBorder"]  = "#3D3D3D",
            ["GridLine"]      = "#383838",
            ["TrackBg"]       = "#4A4A4A",
            ["ScrollBarThumb"] = "#8A8A8A",
            ["HeaderBorder"]  = "#555555",
            // Semantic
            ["Success"]       = "#4CC97D",
            ["SuccessHover"]  = "#3DB86E",
            ["Danger"]        = "#FF6B6B",
            ["DangerHover"]   = "#E85555",
            ["Warning"]       = "#FFB347",
            ["DangerLight"]   = "#3A2028",
            ["DangerGhostBorder"] = "#5A2838",
            // On-accent text — dark on bright Accent/Success/Danger in dark mode
            ["OnAccent"]      = "#0A1A2A",
            ["OnSuccess"]     = "#0A1F12",
            ["OnDanger"]      = "#2A0A0A",
            // Ghost button — slightly elevated surface for visibility
            ["GhostBg"]       = "#383838",
            ["GhostBorder"]   = "#5A5A5A",
            // Section card — subtle tinted background for sidebar sub-cards
            ["SectionBg"]     = "#353535",
            ["SectionAccent"] = "#60CDFF",
            // Glow color for button hover
            ["GlowAccent"]    = "#60CDFF",
            ["SuccessShadow"] = "#4CC98A",
            ["DangerShadow"]  = "#FF6B6B",
            // Total bar
            ["TotalBg"]       = "#1A1A1A",
            ["TotalText"]     = "#FFFFFF",
            ["TotalTextMuted"] = "#A0A0A0",
            // Quick-add / chips
            ["ChipBg"]        = "#1A3A54",
            // DataGrid headers — Fluent dark
            ["HeaderBg"]      = "#383838",
            ["HeaderBorder"]  = "#4A4A4A",
            ["HeaderText"]    = "#C5C5C5",
            ["HeaderPress"]   = "#202020",
            // Menu
            ["MenuItemPressed"] = "#2A4A6E",
            // Shadow
            ["ShadowColor"]   = "#060606",
            // Badges
            ["BadgeDefaultBg"]  = "#1A3A54",
            ["BadgeDefaultFg"]  = "#60CDFF",
            ["BadgeSuccessBg"]  = "#1A3828",
            ["BadgeSuccessFg"]  = "#5BC98A",
            ["BadgeWarningBg"]  = "#3A2A18",
            ["BadgeWarningFg"]  = "#FFB347",
            ["BadgeDangerBg"]   = "#3A2028",
            ["BadgeDangerFg"]   = "#FF6B6B",
            // Install toggle
            ["InstallGreen"]  = "#4CC97D",
            ["InstallRed"]    = "#FF6B6B",
            ["InstallGray"]   = "#8A8A8A",
        };
    }
}
