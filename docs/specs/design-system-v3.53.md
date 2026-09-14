# Дизайн-система A.R.C. Frame — контракт токенов (v3.53.0)

Этот документ — источник истины по дизайн-токенам: что где лежит, какие шкалы
разрешены, какие правила нельзя нарушать и чем это проверяется автоматически.
Стили и разметка без него расползаются: до v3.53.0 в проекте жило 496 литералов
размера шрифта, 226 радиусов и 36 хардкод-цветов мимо палитры.

## 1. Слои и файлы

| Слой | Файл | Что там |
|---|---|---|
| Цвет | `Themes/Brushes.xaml` + `Services/ThemeService.cs` (палитры light/dark) | единственное место, где допустимы hex-литералы |
| Отступы | `Themes/Tokens.Spacing.xaml` | `Space.*`, готовые `Gap.*` / `Pad.*` |
| Радиусы | `Themes/Tokens.Radius.xaml` | `Radius.*` (шкала + семантика) |
| Типографика | `Themes/Tokens.Typography.xaml` | `Font.*`, `Type.*`, `Weight.*` |
| Движение | `Themes/Tokens.Motion.xaml` | `Motion.*` (длительности), `Ease.*` |
| Стили компонентов | остальные `Themes/*.xaml` | потребляют токены, своих чисел не вводят |

Порядок мержа в `App.xaml` обязателен: `Brushes` → `FocusVisualStyles` →
`Tokens.*` → стили. Тот же порядок продублирован в тестовом bootstrap
(`MosquitoNetCalculator.Tests/Helpers/TestAppThemes.cs`) — тесты обязаны видеть
ту же картину, что приложение.

## 2. Шкалы

**Отступы.** 4 · 8 · 12 · 16 · 24 · 32 · 48 (`Space.Xs … Space.3Xl`).
Готовые отступы для типовых случаев: `Gap.Block` (между блоками), `Gap.BlockSm`,
`Gap.Inline` (иконка → подпись, кнопка → кнопка), `Gap.InlineSm`, `Gap.Left`,
`Pad.Card`, `Pad.Row`, `Pad.Header`, `Pad.Pill`, `Pad.Section`.
Значения 5/6/7/10/14/18/22 больше не вводятся: именно они давали «почти
одинаковые» отступы. Единственное исключение ниже шкалы — `Space.Hair` (2px),
только для иконочных пар внутри чипа.

**Радиусы.** 4 · 8 · 12 · 16 · Pill (+ семантика `Radius.Card`, `Radius.Control`,
`Radius.Element`, `Radius.CardHeader`, `Radius.Sheet`, `Radius.StripeInner`).

> **Правило полосы.** Цветная полоса слева на карточке — левая граница
> внутреннего `Border`, поэтому её радиус = радиус карточки − 1px её собственной
> рамки (`Radius.StripeInner` = `11,0,0,11` при `Radius.Card` = 12). Инвариант
> закреплён тестом `DesignTokenGuardTests.StripingRadius_IsCardRadiusMinusOnePixelFrame`.

**Типографика.** 11 · 12 · 13 · 14 · 16 · 18 · 20 · 24 · 32 · 42
(`Type.Caption … Type.Hero`), базовый размер интерфейса — 13 (`Type.BodyMd`).
Пол 11px для любого реального текста; 9px (`Type.Monogram`) — только монограммы
бейджей (BETA/OR/NV/USER). Дробные размеры (11.5 / 10.5 / 12.5) запрещены: при
100% DPI дают субпиксельный гребень.
Шрифт — вшитый **Inter** (SIL OFL 1.1, файлы `Resources/Fonts/*.ttf`, лицензия
рядом — `OFL.txt`). Следствие рантайм-подмены (см. §4): `Font.*` потребляется
только через `DynamicResource`.

**Движение.** 120 · 180 · 240 · 320 мс (`Motion.Fast/Base/Slow/Emphasized`),
кривые `Ease.Standard` (появление), `Ease.Move` (сдвиг), `Ease.Exit`
(исчезновение). Правила: ни одна анимация не длиннее 320 мс; никаких
`DropShadowEffect`/`BlurEffect` в триггерах и шаблонах (размытие считается на
CPU каждый кадр — выпилено в v3.52.0 и защищено тестом); уважать системную
настройку «отключить анимации».

## 3. Правила

1. Вне цветового слоя нет hex-литералов.
2. Размеры, радиусы, отступы и длительности берутся из токенов. Новый компонент
   пишется на токенах сразу — «бюджета» у нового файла нет.
3. Токены неизменяемы в рантайме (кроме `Font.Text`) — потребляются через
   `{StaticResource}`; для `Font.*` — только `{DynamicResource}`.
4. Ссылка на несуществующий токен — ошибка конфигурации: `DynamicResource` в WPF
   молчит и оставляет свойство дефолтным, поэтому существование ключей проверяется
   статически.
5. Дизайн-долг сокращается «ратчетом»: см. §5.

## 4. Почему `Font.Text` задаётся кодом

Абсолютный pack-URI со `#` внутри (`…/Resources/Fonts/#Inter`) WPF разбирает так,
что `#Inter` становится URI-фрагментом, а не именем семейства, и шрифт молча
подменяется системным. Работает только форма «базовый URI папки + относительное
имя» — а её в XAML не выразить. Поэтому:

* `Themes/Tokens.Typography.xaml` объявляет **фолбэк** (`Inter, Segoe UI, Tahoma`);
* `Services/AppFontService.cs` подменяет токен на вшитый Inter при старте
  (`App.OnStartup`) и в тестовом bootstrap'е;
* весь `Font.*` берётся через `DynamicResource`.

Разбор с замерами и воспроизведением — `agents/docs/GOTCHAS.md` §20.
Факт загрузки Inter (а не фолбэка) стережёт
`TypographyTests.InterFont_IsActuallyBundled_NotSilentFallback`.

## 5. Ратчет дизайн-долга

`MosquitoNetCalculator.Tests/Design/design-token-budget.json` хранит текущее
число литералов на файл (типографика, радиусы) и суммарные лимиты (hex вне
цветового слоя, длительности анимаций, размеры ниже 11px / дробные).

Тест `Design/DesignTokenGuardTests.cs` падает, если:

* в файле литералов стало **больше** бюджета;
* бюджет «просел» больше чем на 3 (его забыли понизить после миграции);
* литералы появились в файле, которого в бюджете нет (новый файл сразу на токенах).

Порядок работы: мигрировал литералы на токены → понизил цифры в том же коммите →
`dotnet test --filter FullyQualifiedName~Tests.Design`.

## 5б. Контраст и доступность

Контраст считается машинно по WCAG 2.1 в **обеих** темах:
`Design/ContrastTests.cs` прогоняет 26 пар «передний план на фоне» из реальных
словарей `ThemeService`, а не из разметки. Долг (пара ниже минимума) обязан быть
перечислен в `Design/design-contrast-debt.json` — иначе тест падает; починил
пару → строку убери, иначе тест упадёт на «долг закрыт, но не снят». Аудит
v3.53.0 нашёл 5 реальных нарушений, все они **исправлены**, поэтому счётчик
долга пуст:

| Пара | Тема | Было | Стало |
|---|---|---|---|
| `OnAccent` на `Accent` | dark | 2.70:1 | 6.97:1 (тёмный текст на ярком акценте) |
| `OnDanger` на `Danger` | dark | 3.91:1 | 4.58:1 (`Danger` → `#D63C42`) |
| `BadgeDangerFg` на `BadgeDangerBg` | dark | 4.15:1 | 5.41:1 |
| `OnAccentPrimary` на `AccentHover` | light | 3.30:1 | 6.06:1 (hover/press — шаг вниз) |
| `BadgeVisionFg` на `BadgeVisionBg` | light | 3.89:1 | 5.54:1 |

Доступность стережёт `Design/AccessibilityTests.cs`: у иконочных кнопок и
управления окном есть `AutomationProperties.Name` (экранный диктор больше не
читает «кнопка»), покрытие растёт ратчетом `Floors` — минимум на файл можно
только поднимать. Общий стиль `DialogCloseButton` несёт имя один раз на все
~10 диалогов.

## 6. Проверка

| Что проверяется | Тест |
|---|---|
| Шкалы объявлены, пол 11px, уникальные ступени | `TypographyTests.TypeScale_*` |
| Inter реально грузится, есть 4 веса + курсив | `TypographyTests.InterFont_*` |
| Лицензия OFL и подключение файлов в csproj | `TypographyTests.BundledFontFiles_*` |
| Стили типографики идут из токенов | `TypographyTests.TextStyles_*` |
| `Font.*` только через `DynamicResource` | `DesignTokenGuardTests.FontTokens_*` |
| Ссылки на токены существуют | `DesignTokenGuardTests.EveryTokenReferencedInXaml_IsDefined` |
| Порядок мержа токенов в App.xaml и bootstrap | `DesignTokenGuardTests.TokenDictionaries_AreMerged*` |
| Инвариант радиуса полосы | `DesignTokenGuardTests.StripingRadius_*` |
| Бюджеты литералов | `DesignTokenGuardTests.*_DoNotGrow`, `HardcodedHexColors_*` |
| Контраст WCAG 2.1 в двух темах + реестр долга | `ContrastTests.Contrast_MeetsMinimumOrIsListedAsDebt` |
| UIA-имена иконочных кнопок и окна | `AccessibilityTests.*` |
| `TextFormattingMode`/`UseLayoutRounding` и DPI | `WindowChromeAndDpiTests.*` |

## 7. Порядок миграции (для продолжающих)

1. Стили-каскад (`FontStyles`, `MiscStyles`, `ButtonStyles`, `InputStyles.*`,
   `DataGridStyles`, `TabStyles`, `ScrollViewerStyles`) — правки здесь дают
   максимум эффекта: одна строка чинит все экраны.
2. Главные экраны (`MainWindow`, `ActionBarControl`, `SidebarControl`,
   `OrderItemsControl`, `TotalCardControl`, `TitleBarControl`).
3. Панели и диалоги второго плана.
4. После каждой группы — понизить бюджет и прогнать полный `dotnet test`
   плюс smoke-базлайны (`.tools/uiverify.ps1 dark|light`), потому что
   смена отступов и размеров меняет картинку.
