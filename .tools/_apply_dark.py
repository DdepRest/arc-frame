import io, os, re, sys

root = os.getcwd()
ts = os.path.join(root, 'MosquitoNetCalculator', 'Services', 'ThemeService.cs')
src = io.open(ts, encoding='utf-8-sig', newline='').read()

start_anchor = '        // Dark theme — «Угольный / Антрацит / Электрик»'
start = src.index(start_anchor)
end_anchor = '\r\n        };\r\n'
end = src.index(end_anchor, start) + len(end_anchor)

block = '''        // Dark theme — «Угольный / Антрацит / Электрик»
        // Based on user reference palette:
        //   Угольный   #151517 — AppBg (canvas root, neutral graphite charcoal)
        //   Антрацит   #1F1F23 — Surface (cards, panels, data grid)
        //   Сланец     #292930 — QuickBg / SectionBg / GhostBg (панель ПОВЕРХ карточки)
        //   Шапка      #2E2E36 — HeaderBg (самая верхняя ступень)
        //   Электрик   #3272D8 — Accent (заливка под белым текстом)
        //   Циан       #6FA8FF — AccentText (акцентный ТЕКСТ/глифы, 6.8:1 на Surface)
        //   Сигнальный #1C8648 — Success (save action, fresh emerald)
        //   Янтарный   #D9A62E — Warning
        //   Белый      #F2F3F6 — TextPrimary
        //   Серебристый#B0B2BB / #94969F — TextSecondary, TextMuted
        //
        // КОНТРАКТ ВЫСОТ: светлее = выше. AppBg < Surface < QuickBg/SectionBg/GhostBg
        // < HeaderBg. Раньше вложенные панели были ТЕМНЕЕ карточки (1.05:1), и блоки
        // читались как дыры, а не как блоки; в тёмной теме теней нет (ShadowColor
        // #000000 на #151517), поэтому высоту несёт только светлотa поверхности и
        // читаемая рамка. Стережёт Design/ThemeSurfacesTests.
        private static readonly Dictionary<string, string> DarkColors = new()
        {
            // Backgrounds — Нейтральный Угольный & Антрацит (без синего уклона)
            ["AppBg"]             = "#151517",
            ["SidebarBg"]         = "#101012",
            ["Surface"]           = "#1F1F23",
            ["QuickBg"]           = "#292930",
            // Скрим подложки оверлеев
            ["Scrim"]             = "#99000000",
            ["RowAlt"]            = "#26262C",
            ["RowHover"]          = "#2E2E36",
            ["RowAltHover"]       = "#30303A",
            ["RowAltSelected"]    = "#2B3448",
            // Row-hover/select animation targets (Color)
            ["RowHoverColor"]     = "#2E2E36",
            ["AccentLightColor"]  = "#2B3448",
            // Accent — Электрик (#3272D8) остаётся ЗАЛИВКОЙ: белый текст на нём
            // 4.63:1. AccentText — тот же акцент для текста и глифов: #3272D8 на
            // тёмной панели даёт 3.34:1 (ниже AA 4.5), а поднимать сам Accent
            // нельзя — он держит белый текст. Светлая AccentText == Accent.
            ["Accent"]            = "#3272D8",
            ["AccentText"]        = "#6FA8FF",
            ["AccentHover"]       = "#2C6BD2",
            ["AccentPress"]       = "#2662BA",
            ["AccentLight"]       = "#2B3448",
            ["AccentShadowColor"] = "#3272D8",
            // Typography — Белый (#F2F3F6) & Серебристый (#B0B2BB / #94969F)
            ["TextPrimary"]       = "#F2F3F6",
            ["TextSecondary"]     = "#B0B2BB",  // 7.8:1 на Surface
            ["TextMuted"]         = "#94969F",  // 5.6:1 на Surface
            // Сланцевые границы — не декоративные: ими читаются поля ввода,
            // карточки и разделители таблицы (было 1.33:1 = «границ нет»).
            ["Border"]            = "#4A4A55",  // 1.88:1 к Surface
            ["BorderHover"]       = "#63636F",  // 2.77:1 к Surface
            ["SubtleBorder"]      = "#32323A",
            ["GridLine"]          = "#3B3B44",  // 1.41:1 к RowAlt — линия видна
            ["TrackBg"]           = "#2C2C33",
            ["ScrollBarThumb"]    = "#57575F",  // 2.29:1 к Surface
            ["HeaderBorder"]      = "#4A4A55",
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
            // Ghost button — та же ступень, что и вложенные панели
            ["GhostBg"]           = "#292930",
            ["GhostBorder"]       = "#4A4A55",
            // Section card
            ["SectionBg"]         = "#292930",
            ["SectionAccent"]     = "#3272D8",
            // Glow / shadow
            ["GlowAccent"]        = "#3272D8",
            ["SuccessShadow"]     = "#1C8648",
            ["DangerShadow"]      = "#D63C42",
            // Total bar — самая нижняя (утопленная) ступень с белой суммой
            ["TotalBg"]           = "#101012",
            ["TotalText"]         = "#FFFFFF",
            ["TotalTextMuted"]    = "#94969F",
            ["TotalSheen"]        = "#33FFFFFF",
            // Quick-add / chips
            ["ChipBg"]            = "#2C2C33",
            // DataGrid headers — Сланцевые шапки
            ["HeaderBg"]          = "#2E2E36",
            ["HeaderBorder"]      = "#4A4A55",
            ["HeaderText"]        = "#C6C6CE",
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
            ["InstallGray"]       = "#94969F",
        };
'''.replace('\n', '\r\n')

src = src[:start] + block + src[end:]

# Light: add AccentText next to AccentLight (same value as Accent).
light_anchor = '            ["AccentLight"]   = "#EBF3FC";\r\n'
if light_anchor not in src:
    # no trailing semicolon in dictionary literal
    light_anchor = '            ["AccentLight"]   = "#EBF3FC",\r\n'
assert light_anchor in src, 'light accent anchor not found'
light_add = (
    '            // AccentText — акцент для ТЕКСТА/глифов. В светлой теме совпадает\r\n'
    '            // с Accent (вид не меняется ни на пиксель), в тёмной — светлее.\r\n'
    '            ["AccentText"]    = "#3574C3";\r\n'
) if light_anchor.endswith(';\r\n') else (
    '            // AccentText — акцент для ТЕКСТА/глифов. В светлой теме совпадает\r\n'
    '            // с Accent (вид не меняется ни на пиксель), в тёмной — светлее.\r\n'
    '            ["AccentText"]    = "#3574C3",\r\n'
)
src = src.replace(light_anchor, light_anchor + light_add, 1)

io.open(ts, 'w', encoding='utf-8-sig', newline='').write(src)
print('ThemeService.cs updated')

# Brushes.xaml default (light) token
bx = os.path.join(root, 'MosquitoNetCalculator', 'Themes', 'Brushes.xaml')
b = io.open(bx, encoding='utf-8-sig', newline='').read()
if 'x:Key="AccentText"' not in b:
    a = '    <SolidColorBrush x:Key="AccentLight"   Color="#EBF3FD"/>\r\n'
    assert a i
