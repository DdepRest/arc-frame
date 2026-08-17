# Спецификация: Декомпозиция монолитных файлов + документирование для @AGENTS.md

> Статус: **Спецификация** (не начато выполнение)  
> Собрано по результатам 3 раундов интервью + анализа кодовой базы (711 тестов, 60+ классов, 16 модулей).  
> Автор спека: AI-ассистент на основе ответов владельца проекта.

---

## 1. Цель и контекст

### 1.1 Проблема

Проект A.R.C. Frame (WPF .NET 8, MosquitoNetCalculator) вырос до 60+ классов, но ряд ключевых файлов остался монолитным — в них смешано 5–10 ответственностей. Это замедляет навигацию AI-агентов и человека, повышает риск конфликтов при параллельной работе и затрудняет документирование в `docs/arc/`.

### 1.2 Критерий «слишком большой»

Применяется **мягкий SRP-критерий**:
- Файл >300 строк кода (без using/комментариев) **ИЛИ**
- Файл содержит >1 независимой ответственности (например, DialogService делает Confirm + UpdateAvailable + SaveDiscardCancel) **ИЛИ**
- Файл трудно описать одной строкой в MODULES.md

Разрешение на критерий дано владельцем: «оставь на твоё усмотрение, что AI было максимально легко читать».

### 1.3 Цель

Разбить монолиты на части, сохранив **100% поведение** (публичные API не меняются), адаптировать тесты под новую структуру, обновить `docs/arc/*` так, чтобы каждый файл имел однозначное описание в MODULES.md/INTENTS.md/SYMBOL_INDEX.md.

---

## 2. Текущее состояние монолитов

### 2.1 Уже декомпозировано (хороший пример)

| Класс | Файлы | Размер суммарный |
|---|---|---|
| `MainWindow` | `.xaml.cs`, `.DataGrid.cs`, `.Orders.cs`, `.Items.cs`, `.WindowChrome.cs`, `.TitleDirty.cs` | ~100 KB |
| `OrderItem` | `.cs`, `.Calculations.cs`, `.Installation.cs`, `.Dto.cs` | ~55 KB |

Эти partial-классы работают хорошо и служат шаблоном для остальных.

### 2.2 Монолиты, требующие декомпозиции

#### Tier 1 — приоритет максимальный (сервисы + MainWindow core)

| Файл | Размер | Строки кода (≈) | Ответственности внутри |
|---|---|---|---|
| `Services/UpdateService.cs` | 34.6 KB | ~750 | Version resolution, WinAPI idle detection, broken-version banner, background check, startup check, manual flow, manifest fetch, download+progress, SHA verify |
| `Services/DialogService.cs` | 26.9 KB | ~580 | Base dialog builder, Confirm dialog, UpdateAvailable dialog (с changelog), SaveDiscardCancel dialog, FluentCloseButton helper |
| `Services/PrintService.cs` | 26.7 KB | ~570 | HTML template filling, SVG чертежи, print preview window, EscapeHtml |
| `MainWindow.xaml.cs` | 25.8 KB | ~550 | Constructor, event wiring, Loaded handler, scheduler init, banner toast, card animations, theme handler, progress bar hook, empty state, grid column sizing, price loading, total debounce, contract number, title bar |
| `Controls/QuickAddControl.xaml.cs` | 24.8 KB | ~520 | Type/color/size selection, Anwis mode UI, price lookup, validation, auto-fill, keyboard shortcuts |

#### Tier 2 — приоритет высокий (тесты + вспомогательные сервисы)

| Файл | Размер | Строки кода (≈) | Ответственности внутри |
|---|---|---|---|
| `Tests/Models/OrderItemTests.cs` | 51.0 KB | ~1100 | Property tests, calculation tests, Anwis mode tests, installation tests, clone tests, edge cases |
| `Tests/App/ManualChecklistTests.cs` | 42.4 KB | ~900 | Integration checklist: calculations, UI bindings, save/load, Anwis, printing, factory text |
| `Services/ToastService.cs` | 19.1 KB | ~410 | Basic toast, update notification banner (persistent), toast repositioning, animation storyboards |

#### Tier 3 — приоритет средний (остальное)

| Файл | Размер | Примечание |
|---|---|---|
| `MainWindow.Orders.cs` | 21.4 KB | ~450 строк. Можно разбить на ImportExport, StatusManagement, Sorting |
| `Controls/OrderItemsControl.xaml` | 22.9 KB | XAML-only, но очень длинный. Можно вынести DataGrid columns в отдельный ResourceDictionary |
| `WelcomeWindow.xaml` | 21.1 KB | XAML с вложенной разметкой. Низкий приоритет — редко меняется |

---

## 3. Стратегия декомпозиции

### 3.1 Подход по типу файла

| Тип файла | Стратегия | Обоснование |
|---|---|---|
| **UI code-behind** (`MainWindow.*`, `Controls/*.xaml.cs`) | **Partial-классы** (продолжить существующий паттерн) | WPF code-behind привязан к XAML-дереву; partial позволяет держать файл рядом с родителем и понятно именовать |
| **Сервисы** (`Services/*.cs`) | **Partial-классы** для статических сервисов, **вспомогательные классы** для сложной логики | DialogService/UpdateService — static API, который нельзя ломать. Partial сохраняет публичную поверхность, но разделяет реализацию |
| **Тесты** (`Tests/**/*.cs`) | **Partial-классы** по предметной области | xUnit поддерживает partial test-классы. Разбиение по `OrderItem.PropertyTests.cs`, `.CalculationTests.cs` и т.д. делает поиск теста мгновенным |

### 3.2 Именование

- **Язык**: английский (как в проекте).
- **Суффикс**: `.[Responsibility].cs` для partial-файлов.
- **Пример**: `DialogService.Update.cs`, `OrderItemTests.Calculations.cs`.
- **Исключение**: если выделяется полноценный новый класс (не partial), имя без точки: `UpdateManifestFetcher.cs`.

---

## 4. Детальный план по фазам

### Phase 1: MainWindow.xaml.cs — выделение оставшихся ответственностей

**Состояние**: Уже есть 6 partial-файлов, но `MainWindow.xaml.cs` всё ещё содержит ~550 строк.

#### Что оставить в `MainWindow.xaml.cs`

| Элемент | Строки | Почему остаётся |
|---|---|---|
| `using` directives | 1–15 | Требуются всем partial-файлам; дублировать нельзя |
| Class declaration + fields/properties (`ViewModel`, `PrintService`, `_columnRecalcPending`, `_initialLoadDone`, `_suppressContractNumberUpdate`, `_appVersion`, `_cachedTitleLocation`, `_updateTotalDebounceTimer`, `_updateCheckScheduler`) | 18–52 | Общее состояние, используемое несколькими partial-файлами |
| Constructor `MainWindow()` | 54–192 | Точка входа; содержит wiring всех событий, инициализацию контролов, `LoadPrices()`, `StartNewOrder()`, `RefreshComboBoxColumns()` |
| `MainWindow_Loaded` | 194–243 | Startup-логика: `RefreshOrdersList`, `RecalculatePriceGridColumnWidths`, `_initialLoadDone = true`, запуск `_updateCheckScheduler`, баннер сломанных версий, dispatch анимации |
| `UpdateEmptyState()` | 332–337 | Вызывается из конструктора (строка 181) и из `.Items.cs` (`OpenSelectedOrder`) — core UI state |
| `PreviewKeyDown` handler | 165–170 | Глобальные shortcuts (Ctrl+Z / Ctrl+Y) |
| `StateChanged` handler | 187–191 | Max/restore icon toggle |
| `SizeChanged` handler | 183–186 | Toast reposition |
| `Closed` handler | 184–188 | Cleanup: отписка от `ProgressChanged`, остановка `_updateCheckScheduler` |

#### Новые partial-файлы — точные границы

**`MainWindow.Animations.cs`**
- Переносим из `MainWindow.xaml.cs`:
  - **Строки 245–297**: `AnimateCardsOnLoad()` — entrance cascade для 8 карточек с `DoubleAnimation` + `CubicEase`
  - **Строки 299–316**: `OnThemeChanged()` — `InvalidateVisual` для title bar при смене темы
- Итого: ~73 строки (без using — using shared из `.xaml.cs`)
- Зависимости: `Sidebar`, `ActionBarControl`, `QuickAddControl`, `OrderItemsControl`, `TotalCardControl` (все из XAML)

**`MainWindow.Progress.cs`**
- Переносим из `MainWindow.xaml.cs`:
  - **Строки 318–321**: Поле `private ProgressBarUpdateAnimator? _progressAnimator;`
  - **Строки 323–330**: `OnUpdateProgressChanged(object? sender, EventArgs e)` — делегат к `_progressAnimator?.Animate(...)`
- Итого: ~14 строк
- Зависимости: `UpdateService.ProgressChanged` event (подписка в конструкторе строка 68)

**`MainWindow.GridColumns.cs`**
- Переносим из `MainWindow.xaml.cs`:
  - **Строки 339–349**: `RecalculateOrderGridColumnWidths()` — debounce-обёртка с `_columnRecalcPending`
  - **Строки 351–383**: `RecalculateOrderGridColumnWidthsCore()` — 12 вызовов `DataGridColumnAutoSizer.SetColumnMinWidth` для таблицы заказов
- Итого: ~46 строк
- Зависимости: `_columnRecalcPending` (field в `.xaml.cs`), `OrderItemsControl.Grid`, `OrderItems` collection

**`MainWindow.Pricing.cs`**
- Переносим из `MainWindow.xaml.cs`:
  - **Строки 385–397**: `LoadPrices()` — делегат к `PricesVM.LoadPrices()` + `Items.Refresh()` + `RecalculatePriceGridColumnWidths()`
  - **Строки 399–404**: `RefreshComboBoxColumns()` — делегат к `ViewModel.RefreshComboBoxColumns()` + `QuickAddControl.CmbType.ItemsSource`
  - **Строки 406–414**: `RecalculatePriceGridColumnWidths()` — 3 вызова `DataGridColumnAutoSizer` для таблицы цен
- Итого: ~31 строка
- Зависимости: `PricesControl.PriceDataGrid`, `Prices` collection, `QuickAddControl.CmbType`

**`MainWindow.Totals.cs`**
- Переносим из `MainWindow.xaml.cs`:
  - **Строки 416–435**: `UpdateTotal()` — debounce-логика через `_updateTotalDebounceTimer` (50 мс)
  - **Строки 437–479**: `ExecuteUpdateTotal()` — вызов `CalcVM.CalculateTotal`, обновление `TotalCardControl.TotalRun` / `TotalSub` / `AmountWords`
- Итого: ~65 строк
- Зависимости: `_updateTotalDebounceTimer` (field в `.xaml.cs`), `TotalCardControl`, `ClientInfo.AdditionalKpsTotal`

**`MainWindow.Contracts.cs`** (консолидация из `.xaml.cs` + `.Items.cs`)
- Переносим из `MainWindow.xaml.cs`:
  - *Нет прямых методов* — но `MainWindow_Loaded` (строка 194–243) вызывает `SyncContractPrefix` и `UpdateBaseTitle`, которые работают с контрактом
- Переносим из `MainWindow.Items.cs`:
  - **Строки 41–51**: `UpdateContractNumber()` — генерация номера через `OrdersVM.GenerateContractNumber`
  - **Строки 53–60**: `UpdateCurrentOrderInfo()` — обновление `ActionBarControl.OrderInfoRun`
  - **Строки 279–286**: `SyncContractPrefix(string newPrefix)` — установка префикса + перегенерация номера
  - **Строки 270–272**: `SuppressContractNumberUpdates()` — фабрика `SuppressContractNumberScope`
  - **Строки 288–294**: `SuppressContractNumberScope` class — `IDisposable` scope для `_suppressContractNumberUpdate`
- Итого: ~42 строки
- Зависимости: `Sidebar.TxtPrefix`, `ActionBarControl.OrderInfoRun`, `_suppressContractNumberUpdate` (field в `.xaml.cs`)

> **Примечание**: После переноса из `MainWindow.Items.cs` методов `UpdateContractNumber`, `UpdateCurrentOrderInfo`, `SyncContractPrefix`, `SuppressContractNumberUpdates`, `SuppressContractNumberScope` — в `MainWindow.Items.cs` остаются:
> - `StartNewOrder` (строки 12–39)
> - `BtnSaveOrder_Click` (строки 62–65)
> - `BtnDeleteRow_Click` (строки 67–78)
> - `AnwisModePillLeftClick` / `RightClick` (строки 80–116)
> - `BtnToggleInstallation_Click` (строки 118–259)
>
> `MainWindow.Items.cs` уменьшается с ~260 строк до ~180 строк.

### Phase 2: Сервисы — DialogService, UpdateService, ToastService

#### 2A. DialogService.cs (26.9 KB → 4 partial-файла)

| Новый файл | Что переносится | Публичный API |
|---|---|---|
| `DialogService.Base.cs` | `BuildDialogBase`, `GetBrush`, `CreateFluentCloseButton` | `CreateFluentCloseButton` (public) |
| `DialogService.Confirm.cs` | `ShowConfirm` + внутренняя разметка | `ShowConfirm(...)` (public, без изменений) |
| `DialogService.Update.cs` | `ShowUpdateAvailable` (обе перегрузки) + changelog UI + badge UI + anti-recommend text | `ShowUpdateAvailable(...)` (public, без изменений) |
| `DialogService.SaveDiscard.cs` | `ShowSaveDiscardCancel` + 3 кнопки | `ShowSaveDiscardCancel(...)` (public, без изменений) |

> **Правило**: Все методы остаются `public static` на классе `DialogService`. Тело каждого метода остаётся в своём partial-файле. Нет breaking changes.

#### 2B. UpdateService.cs (34.6 KB → 5 partial-файлов + 1 новый helper)

| Новый файл | Что переносится | Публичный API |
|---|---|---|
| `UpdateService.Version.cs` | `CurrentVersion`, `TryResolveCurrentVersion`, `ResolveVersion`, `ParseSafe`, `StripVersionSuffix` | `CurrentVersion` (internal readonly) |
| `UpdateService.Idle.cs` | `GetIdleTime`, `LASTINPUTINFO`, P/Invoke | `GetIdleTime()` (public static) |
| `UpdateService.BrokenVersion.cs` | `IsCurrentVersionBrokenForAutoUpdate`, константы `BrokenVersion*Build` | `IsCurrentVersionBrokenForAutoUpdate(...)` (internal) |
| `UpdateService.Background.cs` | `_lastNotifiedVersion`, `CheckInBackgroundAsync` | `CheckInBackgroundAsync()` (public static) |
| `UpdateService.Flow.cs` | `CheckOnStartupAsync`, `CheckAndApplyAsync`, `RunUpdateFlowAsync` | `CheckOnStartupAsync()`, `CheckAndApplyAsync(...)` (public) |
| `UpdateService.Manifest.cs` | `GetAvailableUpdate`, `FetchManifestAsync` | `GetAvailableUpdate(...)` (internal), `FetchManifestAsync(...)` (internal) |
| `UpdateService.Download.cs` | `DownloadWithProgressAsync`, `ComputeSha256`, `TryDelete` | `DownloadWithProgressAsync(...)` (internal) |

> **Примечание**: `UpdateService.csproj` (основной файл) остаётся точкой входа — содержит поля `IsChecking`, `IsDownloading`, `DownloadProgress`, события `CheckingChanged`/`ProgressChanged`, `HasPendingUpdate()`.

#### 2C. ToastService.cs (19.1 KB → 2 partial-файла)

| Новый файл | Что переносится | Публичный API |
|---|---|---|
| `ToastService.Core.cs` | `Initialize`, `ShowToast`, `RepositionToasts`, внутренние storyboard/animation helpers | `ShowToast(...)`, `RepositionToasts()` (public static) |
| `ToastService.UpdateNotification.cs` | `ShowUpdateNotification`, persistent banner UI, кнопки «Обновить»/«Позже» | `ShowUpdateNotification(...)` (public static) |

#### 2D. PrintService.cs (26.7 KB → 4 partial-файла)

**Решение**: Partial-классы (Option A) — нулевой риск для тестов, zero breaking changes, повторяет паттерн Phase 1.

| Новый файл | Точные строки (из текущего PrintService.cs, 418 строк всего) | Что переносится | Публичный API |
|---|---|---|---|
| `PrintService.Template.cs` | **33–57** | `LoadTemplate()` — embedded resource → disk fallback → exception | Остаётся `private` |
| `PrintService.HtmlBuilder.cs` | **59–219** | `FillTemplate(...)` (59–200) + `EscapeHtml(...)` (202–219) — вся HTML-шаблонизация: таблица, client block, regex replace, additional KP block, notes block | Остаётся `private` / `private static` |
| `PrintService.SvgDrawer.cs` | **221–417** | `GetDrawingSvg(...)` — 13+ product-specific SVG string generators (Отлив, Anwis, На навесах, Козырёк, Короб, ПСУЛ, Откос материал, Работа, Брус, Пояс, Доставка, Уплотнение, fallback) | Остаётся `private static` |

**Что остаётся в `PrintService.cs`**:
- **1–31**: `using`, namespace, `public partial class PrintService` declaration
- **17–31**: `GenerateKpHtml(...)` — public entry point, фильтрация valid items, вызов `LoadTemplate` + `FillTemplate`
- **418**: closing brace

> **Visibility change**: строка с `public class PrintService` → `public partial class PrintService`.
>
> **Risk**: Zero. `PrintServiceTests.cs` тестирует только public `GenerateKpHtml` — его сигнатура не меняется. `.csproj` автоглоубит новые файлы.
>
> **Примечание**: `PrintPreviewWindow.xaml.cs` вызывает `new PrintService().GenerateKpHtml(...)` — сигнатура не меняется.

#### 2E. QuickAddControl.xaml.cs (24.8 KB → 5 partial-файлов)

**Решение**: Partial-классы (Option A) — единственный безопасный вариант для WPF code-behind. Все `x:Name`-ссылки и XAML-wired event handlers остаются доступны across partial files. Public API не меняется.

| Новый файл | Точные строки (из текущего QuickAddControl.xaml.cs, 580 строк всего) | Что переносится | Примечание |
|---|---|---|---|
| `QuickAddControl.AddItem.cs` | **85–140** + **142–152** + **167–171** + **173** + **175–220** | `CmbQuickType_SelectionChanged` (85–140), `CmbQuickColor_SelectionChanged` (142–152), `TxtQuickPrice_LostFocus` (167–171), `BtnQuickAdd_Click` (173), `QuickAddItem` (175–220) | Вся логика «подготовка полей → добавление». Метод `TryGetMainWindow` остаётся в main, но используется здесь. |
| `QuickAddControl.Preview.cs` | **222–269** + **376** | `UpdateQuickPreview` (222–269), `RefreshPreview` (376) | Preview-логика: расчёт площади, цены, Anwis-коррекция. |
| `QuickAddControl.Search.cs` | **271–374** | `TxtQuickSearch_TextChanged` (271–274), `TxtQuickSearch_KeyDown` (276–311), `SearchSuggestions_SelectionChanged` (313–315), `SearchSuggestions_PreviewMouseDown` (317–360), `SelectAll_OnFocus` (362–366), `TxtQuickSearch_LostFocus` (368–374) | Полный поисковый UI: TextBox → Popup → ListBox → выбор типа. |
| `QuickAddControl.AnwisMode.cs` | **382–578** | `CmbQuickType_PreviewMouseRightButtonDown` (382–404), `AnwisModePill_Click` (406–432), `ToggleAnwisModePanel` (434–493), `UpdateAnwisModePills` (495–537), `HoverSegment` (539–556), `ResetAnwisMode` (558–567), `UpdateAnwisModeToolTip` (569–578) | Всё, что касается segmented control режимов Anwis: pills, hover, animation, ToolTip. |

**Что остаётся в `QuickAddControl.xaml.cs`**:
- **1–34**: `using`, namespace, `public partial class QuickAddControl`, свойства/поля (`CardQuickAddBorder`, `CmbType`, `SelectedAnwisMode` и т.д.)
- **35–71**: Constructor — инициализация, wiring событий, ToolTips, hover effects для pills
- **73–83**: `TryGetMainWindow(...)` — helper, используемый почти всеми partial-файлами
- **154–165**: `QuickField_TextChanged` (154), `QuickField_KeyDown` (156–159), `QuickField_GotFocus` (161–165) — мелкие event handlers, wired в constructor

> **Risk**: Низкий. XAML designer не сломается, потому что `x:Class` и constructor остаются в main. Нет unit-тестов на code-behind, поэтому test-risk = 0.
>
> **Зависимости между partial-файлами**:
> - `AddItem.cs` вызывает `UpdateQuickPreview()` (определён в `Preview.cs`) — OK, same partial class.
> - `AddItem.cs` вызывает `ToggleAnwisModePanel()` / `UpdateAnwisModeToolTip()` (в `AnwisMode.cs`) — OK.
> - Constructor (main) регистрирует `MouseLeave += (_, _) => UpdateAnwisModePills()` — вызов метода из `AnwisMode.cs` — OK.

### Phase 3: Тесты

#### 3A. OrderItemTests.cs (51 KB → 4 partial-файла)

| Новый файл | Что переносится |
|---|---|
| `OrderItemTests.Properties.cs` | Тесты свойств: Name, Color, Width, Height, Quantity, Price, IsActive, RowNumber |
| `OrderItemTests.Calculations.cs` | Тесты CalculatedValue, Total, Unit, Display-свойств, Recalculate для всех типов товаров |
| `OrderItemTests.Anwis.cs` | Тесты AnwisSizeMode, ШиринаВвод/ВысотаВвод, SetAnwisModeQuiet, размеры |
| `OrderItemTests.Installation.cs` | Тесты InstallationMode, вычеты, surcharge, Display-свойств монтажа |

> **Правило**: Класс `OrderItemTests` остаётся единым для xUnit. Каждый partial содержит регион тестов.

#### 3B. ManualChecklistTests.cs (42.4 KB → 3 partial-файла)

| Новый файл | Что переносится |
|---|---|
| `ManualChecklistTests.Calculations.cs` | Чеклист расчётов: площадь, периметр, штуки, итоги, Anwis-коррекция |
| `ManualChecklistTests.Persistence.cs` | Чеклист сохранения/загрузки: JSON roundtrip, derived-поля, migration |
| `ManualChecklistTests.Integration.cs` | Чеклист UI+integration: печать, заводской текст, цены, статусы |

### Phase 4: Документирование для @AGENTS.md

#### 4A. Обновление существующих arc-документов

| Документ | Что обновить |
|---|---|
| `MODULES.md` | Добавить все новые partial-файлы в таблицы с описанием ответственности. Удалить/переименовать устаревшие записи. |
| `INTENTS.md` | Добавить routing для новых файлов (например, «изменить диалог обновления» → `DialogService.Update.cs`). |
| `SYMBOL_INDEX.md` | Перегенерировать через `gensymbols.ps1` после всех изменений. |
| `DOCUMENTATION_MATRIX.md` | Обновить `documentation-matrix.json`, запустить `render-matrix.ps1`. Добавить записи для каждого нового partial-файла. |
| `CURRENT_STATE.md` | Зафиксировать текущую версию структуры (после Phase 4). |

#### 4B. Новый документ: `docs/arc/REFACTORING_GUIDE.md`

Содержание:
- **Конвенция именования partial-файлов**: `[ClassName].[Responsibility].cs`
- **Где искать логику**: mapping «задача → файл» для всех новых partial-файлов
- **Правило добавления нового partial**: куда положить, как назвать, как обновить docs
- **Пример**: «Если нужно изменить диалог обновления → иди в `DialogService.Update.cs`, не в `DialogService.cs`»

#### 4C. Чек-лист документирования после каждой фазы

```powershell
# После ЛЮБОЙ фазы:
what-to-update.ps1 $(git diff --name-only)
# → обновить перечисленные docs
validate-docs.ps1
# → всё должно быть зелёным
```

---

## 5. Definition of Done (по всем фазам)

Владелец выбрал комбинированный DoD — **все три критерия обязательны**:

1. **Функциональный**:
   - `dotnet build MosquitoNetCalculator.sln` — без ошибок
   - `dotnet test MosquitoNetCalculator.sln` — **все 711+ тестов проходят**
   - Публичные API не изменили сигнатур (binary compatible)

2. **AI-навигационный**:
   - `file-picker` на запрос «изменить диалог обновления» возвращает `DialogService.Update.cs` в top-3
   - `file-picker` на запрос «починить анимацию карточек» возвращает `MainWindow.Animations.cs` в top-3
   - Каждый новый файл описан одной строкой в MODULES.md

3. **Качественный**:
   - `code-reviewer-kimi` одобрил каждую фазу
   - `validate-docs.ps1` проходит без ошибок
   - CHANGELOG.md обновлён

---

## 6. Риски и смягчение

| Риск | Вероятность | Влияние | Смягчение |
|---|---|---|---|
| Partial-классы запутывают поиск символов (gensymbols) | Низкая | Среднее | SYMBOL_INDEX.md генерируется автоматически; partial файлы группируются по классу |
| Тесты сломаются из-за `internal` доступа из другого partial | Средняя | Высокое | `InternalsVisibleTo` уже настроен в .csproj; partial в том же проекте — доступ полный |
| Документация устареет во время рефакторинга | Средняя | Среднее | Обновлять docs строго в конце каждой фазы, не откладывать на «потом» |
| XAML-файлы с `x:Class` ломаются при перемещении partial | Низкая | Высокое | Не трогать `x:Class` директивы; partial добавляются, не удаляются |
| Регрессия в расчётах из-за переноса `Recalculate` | Низкая | Критическое | Phase 1 не трогает `OrderItem.Calculations.cs` — только MainWindow-логику; Phase 2 не трогает models |

---

## 7. Файлы, которые НЕ трогаем

| Файл | Почему |
|---|---|
| `Models/*.cs` | Уже декомпозированы, содержат критичную расчётную логику — риск не оправдан |
| `ViewModels/*.cs` | Разумного размера (<20 KB каждый), ответственность чёткая |
| `Themes/*.xaml` | XAML ресурсы, декомпозиция не даст выигрыша |
| `Services/PriceService.cs` | 12 KB, ответственность одна — цены |
| `Services/OrderStorageService.cs` | 16 KB, ответственность одна — хранение |
| `.github/workflows/*` | Не относится к задаче |

---

## 8. Пример финальной структуры (фрагмент)

```text
MosquitoNetCalculator/
  MainWindow.xaml.cs              # constructor, events, wiring
  MainWindow.DataGrid.cs          # hover, checkbox click
  MainWindow.Orders.cs            # order history (thin wrapper)
  MainWindow.Items.cs             # item CRUD, installation, Anwis pills
  MainWindow.WindowChrome.cs      # drag, WndProc
  MainWindow.TitleDirty.cs        # title + dirty state
  MainWindow.Animations.cs        # ← NEW: card entrance animation
  MainWindow.Progress.cs          # ← NEW: download progress bar
  MainWindow.GridColumns.cs       # ← NEW: auto-size DataGrid columns
  MainWindow.Pricing.cs           # ← NEW: price loading + column sizing
  MainWindow.Totals.cs            # ← NEW: total calculation + debounce
  MainWindow.Contracts.cs         # ← NEW: contract number generation
  Services/
    DialogService.cs              # entry point (thin wrapper)
    DialogService.Base.cs         # ← NEW: BuildDialogBase, GetBrush
    DialogService.Confirm.cs      # ← NEW: ShowConfirm
    DialogService.Update.cs       # ← NEW: ShowUpdateAvailable
    DialogService.SaveDiscard.cs  # ← NEW: ShowSaveDiscardCancel
    UpdateService.cs              # state + events (thin wrapper)
    UpdateService.Version.cs      # ← NEW: version resolution
    UpdateService.Idle.cs         # ← NEW: WinAPI idle detection
    UpdateService.BrokenVersion.cs# ← NEW: broken-version banner logic
    UpdateService.Background.cs   # ← NEW: background scheduler checks
    UpdateService.Flow.cs         # ← NEW: startup + manual flow
    UpdateService.Manifest.cs     # ← NEW: manifest fetch + parsing
    UpdateService.Download.cs     # ← NEW: download + SHA verify
    ToastService.Core.cs          # ← NEW: ShowToast, RepositionToasts
    ToastService.UpdateNotification.cs # ← NEW: persistent banner
    PrintService.cs               # entry point (defer to Phase 2.5)
    PrintService.Html.cs          # ← NEW: HTML template (deferred)
    PrintService.Svg.cs           # ← NEW: SVG drawings (deferred)
    PrintService.Preview.cs       # ← NEW: preview window (deferred)
  Controls/
    QuickAddControl.xaml.cs            # constructor, fields, TryGetMainWindow, small field handlers
    QuickAddControl.AddItem.cs         # ← NEW: type/color selection, price lost-focus, BtnQuickAdd_Click, QuickAddItem
    QuickAddControl.Preview.cs         # ← NEW: UpdateQuickPreview, RefreshPreview
    QuickAddControl.Search.cs          # ← NEW: search TextBox, ListBox, Popup handlers
    QuickAddControl.AnwisMode.cs       # ← NEW: pills, hover, animation, ToolTip
  Tests/
    Models/
      OrderItemTests.cs           # entry point (empty or thin)
      OrderItemTests.Properties.cs    # ← NEW
      OrderItemTests.Calculations.cs  # ← NEW
      OrderItemTests.Anwis.cs         # ← NEW
      OrderItemTests.Installation.cs  # ← NEW
    App/
      ManualChecklistTests.cs         # entry point (thin)
      ManualChecklistTests.Calculations.cs  # ← NEW
      ManualChecklistTests.Persistence.cs   # ← NEW
      ManualChecklistTests.Integration.cs   # ← NEW
```

---

## 9. Согласованные параметры (из интервью)

| Параметр | Значение |
|---|---|
| **Приоритет** | Все монолиты сверху вниз (Tier 1 → Tier 2 → Tier 3) |
| **Стиль рефакторинга** | Самый AI-читаемый вариант (partial-классы для UI, partial/вспомогательные для сервисов) |
| **API stability** | Публичные сигнатуры не меняются; внутренности — на усмотрение |
| **Язык имён** | Английский |
| **Phasing** | Поэтапно: Phase 1 (MainWindow) → Phase 2 (сервисы) → Phase 3 (тесты) → Phase 4 (доки) |
| **DoD** | Тесты зелёные + компиляция + docs + AI-навигация + code-reviewer |
| **Тесты** | Можно адаптировать (using/namespace), но логика тестов не меняется |
| **Документирование** | Обновить MODULES.md, INTENTS.md, SYMBOL_INDEX.md, DOCUMENTATION_MATRIX.md + новый REFACTORING_GUIDE.md |

---

## 10. Следующий шаг

1. Владелец ревьюит и аппрувит этот spec.
2. Начинаем **Phase 1**: `MainWindow.xaml.cs` → 6 новых partial-файлов.
3. После каждой фазы: build → test → code-reviewer → docs → commit.
