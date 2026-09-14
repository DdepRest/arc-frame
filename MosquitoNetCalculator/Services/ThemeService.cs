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
        /// <summary>Только для тестов контраста: читаемый снимок светлой палитры.</summary>
        internal static System.Collections.Generic.Dictionary<string, string> LightColorsForTests() =>
            new(LightColors);

        /// <summary>Только для тестов контраста: читаемый снимок тёмной палитры.</summary>
        internal static System.Collections.Generic.Dictionary<string, string> DarkColorsForTests() =>
            new(DarkColors);

        private static readonly Dictionary<string, string> LightColors = new()
        {
            // Backgrounds
            ["AppBg"]        = "#F5F5F7",
            ["SidebarBg"]     = "#FFFFFF",
            ["Surface"]       = "#FFFFFF",
            // Скрим подложки оверлеев (см. Brushes.xaml): в светлой теме мягче,
            // чтобы панель отделялась без «чёрного провала».
            ["Scrim"]        = "#80000000",
            ["QuickBg"]       = "#FCFCFD",
            ["RowHover"]      = "#F0F0F5",
            ["RowAlt"]        = "#FAFAFD",
            ["RowAltHover"]   = "#F0F0F5",
            ["RowAltSelected"] = "#E8F0FA",
            // Row-hover/select animation targets (Color)
            ["RowHoverColor"]    = "#F0F0F5",
            ["AccentLightColor"] = "#E8F0FA",
            // Accent — professional blue. v3.51 contrast audit: #3878C8 давал
            // 4.48:1 с белым текстом (чуть ниже AA 4.5); #3574C3 = 4.73:1.
            ["Accent"]        = "#3574C3",
            // v3.53 contrast audit: #4A90E0 давал 3.30:1 с белым текстом —
            // активная кнопка на hover становилась нечитаемой. hover/press
            // теперь ШАГ ВНИЗ от базового (как в тёмной теме — шаг вверх):
            // #2E63A8 = 6.06:1, #24508F = 8.02:1. Базовый оттенок не менялся.
            ["AccentHover"]   = "#2E63A8",
            ["AccentPress"]   = "#24508F",
            ["AccentLight"]   = "#EBF3FC",
            ["AccentShadowColor"] = "#3878C8",
            // Text
            ["TextPrimary"]   = "#1A1A24",
            ["TextSecondary"] = "#585868",
            // UX-07: #90909C was ≈2.9:1 on white (below WCAG AA 4.5:1 for the
            // 8-11px labels it's used on). #6E6E7A gives ≈5.0:1 on white.
            ["TextMuted"]     = "#6E6E7A",
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
            // v3.51 contrast audit: #D48C00 на белой карточке = 2.78:1 —
            // ниже non-text AA (3:1) для полосы «Исправление». #BF8000 = 3.33:1.
            ["Warning"]       = "#BF8000",
            ["DangerLight"]   = "#FDE7E9",
            ["DangerGhostBorder"] = "#F0C6CA",
            // On-accent text
            ["OnAccent"]      = "#FFFFFF",
            ["OnAccentPrimary"] = "#FFFFFF",
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
            ["HeaderHover"]   = "#F0F0F6",
            ["HeaderPress"]   = "#E8E8EC",
            // Menu
            ["MenuItemPressed"] = "#E8EDF5",
            // Shadow
            ["ShadowColor"]   = "#1A1A24",
            // Badges — Fg затемнён до 4.5:1+ на своём Bg (UX-07/AA; v3.51
            // contrast audit: #3878C8 на #EBF3FC давал 4.00:1).
            ["BadgeDefaultBg"]  = "#EBF3FC",
            ["BadgeDefaultFg"]  = "#2B639F",
            ["BadgeSuccessBg"]  = "#E6F5EC",
            ["BadgeSuccessFg"]  = "#0F7B3F",
            ["BadgeWarningBg"]  = "#FFF4CE",
            // v3.51 contrast audit: #D48C00 на #FFF4CE = 2.53:1 — ниже AA;
            // #8F5F00 = 5.02:1 (тот же янтарный тон, читаемее).
            ["BadgeWarningFg"]  = "#8F5F00",
            ["BadgeDangerBg"]   = "#FDE7E9",
            ["BadgeDangerFg"]   = "#C42B1C",
            ["BadgeVisionBg"]   = "#F0ECFA",
            // v3.53 contrast audit: #7A5AF8 на этом фоне давал 3.89:1 (ниже AA).
            // #6338E8 = 5.54:1 — тот же фиолетовый, читаемый.
            ["BadgeVisionFg"]   = "#6338E8",
            // Install toggle
            ["InstallGreen"]  = "#0F7B3F",
            ["InstallRed"]    = "#C42B1C",
            ["InstallGray"]   = "#8A8A9A",
        };

        // ─────────────────────────────────────────────────────────
        // Dark theme — v3.50 prototype palette
        // (docs/prototype.html: bg #131417, panel #1E2025, accent #4EA1F7)
        //
        // Elevation system (4 visible levels):
        //   L0  AppBg      #131417  — deepest page background
        //   L0.5 RowAlt    #17181C  — alternating row background
        //   L1  Surface    #1E2025  — cards, panels, tables
        //   L2  QuickBg    #23262C  — elevated interactive cards
        //   L3  HeaderBg   #2A2D33  — column headers, prominent panels
        // ─────────────────────────────────────────────────────────
        private static readonly Dictionary<string, string> DarkColors = new()
        {
            // Backgrounds — 4-level elevation (prototype: --bg/--panel/--panel2/--field)
            ["AppBg"]        = "#131417",
            ["SidebarBg"]    = "#17181C",
            ["Surface"]       = "#1E2025",
            ["QuickBg"]       = "#23262C",
            // Скрим глубже, чем в светлой теме: тёмная подложка уже тёмная,
            // панель должна читаться как отдельный слой.
            ["Scrim"]        = "#8C000000",
            ["RowAlt"]        = "#17181C",
            ["RowHover"]      = "#22252B",
            ["RowAltHover"]   = "#22252B",
            ["RowAltSelected"] = "#26384C",
            // Row-hover/select animation targets (Color)
            ["RowHoverColor"]    = "#22252B",
            ["AccentLightColor"] = "#26384C",
            // Accent — prototype blue #4EA1F7 (--accent), darker steps for hover/press
            ["Accent"]        = "#4EA1F7",
            ["AccentHover"]   = "#6BB4F9",
            ["AccentPress"]   = "#2B7FD4",
            ["AccentLight"]   = "#26384C",
            ["AccentShadowColor"] = "#4EA1F7",
            // Text — prototype: --text #E8EAED, --muted #9AA0A8
            ["TextPrimary"]   = "#E8EAED",
            ["TextSecondary"] = "#C3C8CF",
            // Prototype --dim #6D727B is ≈3.5:1 on Surface — below AA for 8-11px
            // labels; #8B919A keeps the grayish hue at ≈5.4:1 (UX-07 rule).
            ["TextMuted"]     = "#8B919A",
            // Borders — prototype: --border #2C2F36, --border2 #383C44
            ["Border"]        = "#2C2F36",
            ["BorderHover"]   = "#454A53",
            ["SubtleBorder"]  = "#26292F",
            ["GridLine"]      = "#26292F",
            ["TrackBg"]       = "#33363D",
            ["ScrollBarThumb"] = "#606873",
            ["HeaderBorder"]  = "#33363C",
            // Semantic — prototype: --green #3ECF8E, --red #E5484D, --amber #F5A524
            ["Success"]       = "#3ECF8E",
            ["SuccessHover"]  = "#5EDDA4",
            // v3.53 contrast audit: прототипный #E5484D давал 3.91:1 с белым
            // текстом на опасной кнопке (ниже AA). #D63C42 = 4.58:1 и при этом
            // остаётся графикой ≥3:1 на Surface (3.56) — красный не «сереет».
            ["Danger"]        = "#D63C42",
            ["DangerHover"]   = "#FF6B6F",
            ["Warning"]       = "#F5A524",
            ["DangerLight"]   = "#2E1B1E",
            ["DangerGhostBorder"] = "#5A2629",
            // On-accent text.
            // v3.53 contrast audit: белый на прототипном акценте #4EA1F7 давал
            // 2.70:1 — все 19 мест с OnAccent (галочки, активные чипы, тумблеры
            // вкладок) сидели на акцентной заливке. В тёмной теме это тёмный
            // текст на ярком акценте, ровно как OnAccentPrimary (#06121F = 6.97:1).
            ["OnAccent"]      = "#06121F",
            // v3.50.1: prototype .btn.blue color #06121F — dark text on accent fill
            ["OnAccentPrimary"] = "#06121F",
            ["OnSuccess"]     = "#04180D",
            ["OnDanger"]      = "#FFFFFF",
            // Ghost button — prototype .btn.dark #2A2D33 with border #3A3E46
            ["GhostBg"]       = "#2A2D33",
            ["GhostBorder"]   = "#3A3E46",
            // Section card — sidebar sub-cards
            ["SectionBg"]     = "#22252B",
            ["SectionAccent"] = "#4EA1F7",
            // Glow / shadow
            ["GlowAccent"]    = "#4EA1F7",
            ["SuccessShadow"] = "#3ECF8E",
            ["DangerShadow"]  = "#E5484D",
            // Total bar — deeper than AppBg like prototype statusbar (--bg2 #17181C)
            ["TotalBg"]       = "#17181C",
            ["TotalText"]     = "#E8EAED",
            ["TotalTextMuted"] = "#8B919A",
            // Quick-add / chips — visible accent (accent at ~12% on surface)
            ["ChipBg"]        = "#26384C",
            // DataGrid headers — elevated
            ["HeaderBg"]      = "#2A2D33",
            ["HeaderBorder"]  = "#33363C",
            ["HeaderText"]    = "#CFD3D9",
            ["HeaderHover"]   = "#2F333A",
            ["HeaderPress"]   = "#22252B",
            // Menu
            ["MenuItemPressed"] = "#26384C",
            // Shadow
            ["ShadowColor"]   = "#000000",
            // Badges — Fg высветлен до 4.5:1+ на своём Bg (v3.51 contrast
            // audit: #4EA1F7 на #26384C давал 4.43:1).
            ["BadgeDefaultBg"]  = "#26384C",
            ["BadgeDefaultFg"]  = "#65AEF9",
            ["BadgeSuccessBg"]  = "#12291D",
            ["BadgeSuccessFg"]  = "#3ECF8E",
            ["BadgeWarningBg"]  = "#2E2415",
            ["BadgeWarningFg"]  = "#F5A524",
            ["BadgeDangerBg"]   = "#2E1B1E",
            // v3.53 contrast audit: #E5484D на этом фоне давал 4.15:1 (ниже AA).
            // #F06A6E = 5.41:1 — высветление того же тона.
            ["BadgeDangerFg"]   = "#F06A6E",
            ["BadgeVisionBg"]   = "#251E38",
            ["BadgeVisionFg"]   = "#A78BFA",
            // Install toggle
            ["InstallGreen"]  = "#3ECF8E",
            ["InstallRed"]    = "#E5484D",
            ["InstallGray"]   = "#8A8A9A",
        };
    }
}
