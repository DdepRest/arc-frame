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

            // Кисти темы и подписчики ThemeChanged — принадлежность UI-потока.
            // С фоновой нити (тест-раннер, будущий системный watcher) первый же
            // прочитанный ресурс становился чужим DispatcherObject'ом, и дальше
            // ЛЮБОЕ обращение UI-потока к нему падало в VerifyAccess (ловушка
            // «кисть украдена»: цвет не применялся, тосты/таблицы падали в
            // StyleHelper.SealIfSealable). Маршалируем вместе с событием, чтобы
            // подписчики (Mica заголовка и т.п.) тоже работали на UI-потоке.
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess()
                && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
            {
                dispatcher.Invoke(() =>
                {
                    ApplyTheme();
                    ThemeChanged?.Invoke();
                });
                return;
            }

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

            // Инвариант потока: ресурсы темы — DispatcherObject'ы, и владельцем
            // становится тот, кто прочитал их ПЕРВЫМ. Прямой вызов с фоновой
            // нити (в тестах — не-STA кейс, читающий ThemeService; в проде —
            // любой будущий фоновый триггер) отдавал все кисти чужому потоку:
            // последующие UI-операции валились в VerifyAccess, причём падал не
            // виновник, а следующий тест (см. ManualChecklistTests). Мёртвый
            // dispatcher — применять некуда: тихо выходим, НЕ трогая ресурсы.
            if (!app.Dispatcher.CheckAccess())
            {
                if (app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished) return;
                app.Dispatcher.Invoke(() => ApplyTheme(transitionDuration));
                return;
            }

            // ── Tell Windows the process-level dark-mode preference has changed.
            App.NotifyThemeChanged(IsDarkTheme);

            var colors = IsDarkTheme ? DarkColors : LightColors;
            var duration = transitionDuration ?? TransitionDuration;

            // Смена темы — тоже движение: при системной настройке «отключить
            // анимации» она применяется мгновенно (иначе Accessibility-обещание
            // соблюдалось бы везде, кроме самого заметного перехода).
            bool animate = duration > TimeSpan.Zero && !Helpers.Motion.ReducedMotion;

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
                // interpolates from the correct starting point. БЕЗ анимации
                // (animate=false) кисть должна создаваться СРАЗУ в целевом
                // цвете: ниже её цвет никто не двигает — v3.53.0-a11y ловушка
                // (gate !ReducedMotion) оставляла кисть в старой теме навсегда
                // на устройствах с выключенными анимациями Windows.
                var newBrush = (animate && oldColor.HasValue)
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
            ["SurfaceVariant"] = "#F0F0F4",
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
            // акцент текста/глифов: в светлой теме == Accent (вид не меняется)
            ["AccentText"]        = "#3574C3",
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
            ["WindowBorderBrush"] = "#B8B8C4",
            // Синхронно с тёмной: разделитель должен читаться и на белом.
            ["GridLine"]      = "#E4E6EC",
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
            // Total bar — organic surface in light theme, deep in dark
            ["TotalBg"]       = "#FFFFFF",
            ["TotalText"]     = "#1A1A24",
            ["TotalTextMuted"] = "#6E6E7A",
            ["TotalSheen"]     = "#14000000",
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
            ["InstallBlue"]   = "#0E86C4",
            ["InstallGray"]   = "#8A8A9A",
        };

        // ─────────────────────────────────────────────────────────
        // Dark theme — «Угольный / Антрацит / Электрик»
        // Based on user reference palette:
        //   Угольный   #19191B — AppBg (canvas root, neutral graphite charcoal)
        //   Антрацит   #242427 — Surface (cards, panels, data grid)
        //   Сланец     #202023 — QuickBg / SectionBg (УГЛУБЛЕНИЯ внутри карточки)
        //   Шапка      #1E1E21 — HeaderBg (шапка таблицы, тоже углубление)
        //   Электрик   #3272D8 — Accent (заливка под белым текстом)
        //   Циан       #6FA8FF — AccentText (акцентный ТЕКСТ/глифы, 6.4:1 на Surface)
        //   Сигнальный #1C8648 — Success (save action, fresh emerald)
        //   Янтарный   #D9A62E — Warning
        //   Белый      #F0F1F4 — TextPrimary, TotalText
        //   Серебристый#ADAFB7 / #8F9199 — TextSecondary, TextMuted
        //
        // КОНТРАКТ ВЫСОТ: вложенные панели — УГЛУБЛЕНИЯ, они ТЕМНЕЕ карточки
        // (QuickBg/SectionBg/HeaderBg). Делать их светлее нельзя: большие панели
        // превращаются в серые плиты, и «угольная» тема становится серой.
        // Читаемость дают кромка (Border) и разделители (GridLine): теней в тёмной
        // теме нет — ShadowColor #000000 на #19191B.
        // Стережёт Design/ThemeSurfacesTests.
        // ─────────────────────────────────────────────────────────
        private static readonly Dictionary<string, string> DarkColors = new()
        {
            // Backgrounds — Нейтральный Угольный & Антрацит (без синего уклона)
            ["AppBg"]             = "#19191B",
            ["SidebarBg"]         = "#131315",
            ["Surface"]           = "#242427",
            ["SurfaceVariant"]   = "#2A2A2E",
            ["QuickBg"]           = "#1F1F22",
            // Скрим подложки оверлеев
            ["Scrim"]             = "#99000000",
            ["RowAlt"]            = "#202023",
            ["RowHover"]          = "#2D2D31",
            ["RowAltHover"]       = "#2D2D31",
            ["RowAltSelected"]    = "#2E3A4F",
            // Row-hover/select animation targets (Color)
            ["RowHoverColor"]     = "#2D2D31",
            ["AccentLightColor"]  = "#2E3A4F",
            // Accent — Электрик (#3272D8) остаётся заливкой: белый текст 4.63:1
            ["Accent"]            = "#3272D8",
            // акцент текста/глифов: #3272D8 на тёмной панели = 3.34:1, ниже AA
            ["AccentText"]        = "#6FA8FF",
            ["AccentHover"]       = "#2C6BD2",
            ["AccentPress"]       = "#2662BA",
            ["AccentLight"]       = "#2E3A4F",
            ["AccentShadowColor"] = "#3272D8",
            // Typography — Белый (#F0F1F4) & Серебристый (#ADAFB7 / #8F9199)
            ["TextPrimary"]       = "#F0F1F4",
            ["TextSecondary"]     = "#ADAFB7",  // 7.1:1 на Surface
            ["TextMuted"]         = "#8F9199",  // 4.9:1 на Surface
            // Сланцевые границы — читаемая кромка полей/карточек/таблицы (было 1.3:1)
            ["Border"]            = "#46464F",
            ["BorderHover"]       = "#5A5A64",
            ["SubtleBorder"]      = "#303035",
            ["WindowBorderBrush"] = "#46464F",
            ["GridLine"]          = "#3A3A42",
            ["TrackBg"]           = "#2B2B30",
            ["ScrollBarThumb"]    = "#53535C",
            ["HeaderBorder"]      = "#3A3A42",
            // Semantic — Сигнальный (#1C8648), Ruby (#D63C42), Янтарный (#D9A62E)
            ["Success"]           = "#1C8648",
            // v3.56: hover ШАГ ВНИЗ от базового, как у Accent: #229853 давал
            // белый текст 3.69:1 (ниже AA). #17703D = 6.1:1.
            ["SuccessHover"]      = "#17703D",
            ["Danger"]            = "#D63C42",
            // v3.56: #E05358 давал белый текст 3.78:1 (ниже AA).
            // #C3353B = 5.4:1 — тот же красный, читаемый под курсором.
            ["DangerHover"]       = "#C3353B",
            ["Warning"]           = "#D9A62E",
            ["DangerLight"]       = "#351B20",
            ["DangerGhostBorder"] = "#6E2B33",
            // Action fills & buttons (чистый белый текст на заливках)
            ["OnAccent"]          = "#FFFFFF",
            ["OnAccentPrimary"]   = "#FFFFFF",
            ["OnSuccess"]         = "#FFFFFF",
            ["OnDanger"]          = "#FFFFFF",
            // Ghost button
            ["GhostBg"]           = "#242427",
            ["GhostBorder"]       = "#46464F",
            // Section card
            ["SectionBg"]         = "#202023",
            ["SectionAccent"]     = "#3272D8",
            // Glow / shadow
            ["GlowAccent"]        = "#3272D8",
            ["SuccessShadow"]     = "#1C8648",
            ["DangerShadow"]      = "#D63C42",
            // Total bar — Угольный статус-бар с белой суммой
            ["TotalBg"]           = "#131315",
            ["TotalText"]         = "#FFFFFF",
            ["TotalTextMuted"]    = "#8F9199",
            ["TotalSheen"]        = "#33FFFFFF",
            // Quick-add / chips
            ["ChipBg"]            = "#303036",
            // DataGrid headers — Сланцевые шапки
            ["HeaderBg"]          = "#1E1E21",
            ["HeaderBorder"]      = "#3A3A42",
            ["HeaderText"]        = "#BEBEC6",
            ["HeaderHover"]       = "#36363F",
            ["HeaderPress"]       = "#26262C",
            // Menu
            ["MenuItemPressed"]   = "#2E2E36",
            // Shadow
            ["ShadowColor"]       = "#000000",
            // Status Badges
            ["BadgeDefaultBg"]    = "#1E2838",
            ["BadgeDefaultFg"]    = "#4EA8DE",
            ["BadgeSuccessBg"]    = "#172E20",
            ["BadgeSuccessFg"]    = "#38D690",
            ["BadgeWarningBg"]    = "#332814",
            ["BadgeWarningFg"]    = "#FBBF24",
            ["BadgeDangerBg"]     = "#351C20",
            ["BadgeDangerFg"]     = "#F87171",
            ["BadgeVisionBg"]     = "#281D3E",
            ["BadgeVisionFg"]     = "#C084FC",
            // Install toggle (vibrant accessible tints for table labels «Вкл» / «Без» / «—»)
            ["InstallGreen"]      = "#38D690",
            ["InstallRed"]        = "#F87171",
            ["InstallBlue"]       = "#4FC1F0",
            ["InstallGray"]       = "#8F9199",
        };
    }
}
