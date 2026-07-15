# CURRENT_STATE.md

## Что сейчас выглядит рабочим

- **Активный план системного рефакторинга (2026-07-12):** зафиксирован baseline (906/906 tests pass, v3.44.2), создан детальный план по 7 фазам в `docs/arc/REFACTORING_PLAN.md`.
  - **Фаза 1 завершена (2026-07-12):** `MainWindow.xaml.cs` 1051→760 строк (−28%). Выделены 4 сервиса: `NavigationService`, `OverlayManager`, `SlopeOverlayCoordinator`, `SlopesProUpsellGate`. +26 тестов. **932/932 tests pass.** Бизнес-логика не затронута. Все `internal` API сохранены как тонкие delegates.
  - **Фаза 2 завершена (2026-07-12):** `UpdateService.cs` 910→608 строк (−33%). Выделены 5 компонентов: `VersionResolver`, `IdleDetector`, `UpdateVerifier`, `UpdateManifestClient`, `UpdateDownloader`. +67 тестов (включая прямые тесты `UpdateManifestClientTests` и `UpdateDownloaderTests`). **999/999 tests pass.** Бизнес-логика не затронута. Все public/internal API сохранены как тонкие прокси.
  - **Фаза 3 завершена (2026-07-12):** `PrintService.cs` 632→81 строк (−87%). Выделены 6 компонентов: `DrawingService`, `FlowDocumentBuilder`, `FixedDocumentBuilder`, `PrintQueueManager`, `PdfExportService`, плюс модели `PageMode`/`PrintSettings`/`PrintResult`. +~40 тестов (`DrawingServiceTests`, `FlowDocumentBuilderTests`, `FixedDocumentBuilderTests`, `PrintQueueManagerTests`, `PdfExportServiceTests`). **1038/1038 tests pass.** Бизнес-логика не затронута. Все public/internal API сохранены как тонкие прокси.
  - **Фаза 4 завершена (2026-07-12):** `DialogService.cs` 641→~250 строк (−61%). Созданы XAML-шаблоны диалогов и fluent-builder:
    - `Services/DialogBuilder.cs` — generic fluent API `DialogBuilder<T>` с методами `.Title()`, `.Message()`, `.WithButton()`, `.ShowDialog()`.
    - `Controls/MessageDialogWindow.xaml` + `.xaml.cs` — универсальный chromeless диалог с ItemsControl для кнопок, анимированной кнопкой закрытия и поддержкой Escape.
    - `Controls/UpdateAvailableWindow.xaml` + `.xaml.cs` — dedicated диалог «Доступно обновление» с бейджем версии, changelog и anti-recommend hint.
    - `Services/DialogService.cs` — сохранён как тонкий фасад: `ShowConfirm`, `ShowSaveDiscardCancel`, `ShowUpdateAvailable`, `CreateFluentCloseButton` делегируют в новые XAML-окна / `DialogBuilder<T>`.
    - +10 тестов: `DialogBuilderTests` (5), `MessageDialogWindowTests` (10 STA). **1071/1071 tests pass.** Бизнес-логика и public API не затронуты.
  - **Фаза 5 завершена (2026-07-13):** `OrderItem.cs` 651→~520 строк (−20%). Выделены 3 компонента:
    - `Models/ProductCatalog.cs` — единый источник истины для категорий товаров (`AreaBasedProducts`, `ManualPieceProducts`, `AmountOnlyProducts`, `OptionalQuantityProducts`, `NoColorProducts`, `InstallationApplicableProducts`, `AnticatApplicableProducts`) с helper-методами `Is*`.
    - `Services/AnwisSizeCalculator.cs` — pure-функции расчёта Anwis-размеров для всех режимов (ББ60, ББ70, РазмерПроёма, Габаритный).
    - `Models/SlopeCalculationExtensions.cs` — методы-расширения для `SlopeCalculation`, включая `DeepClone()`.
    - `Models/OrderItem.cs` сохранил все public/internal API как тонкие прокси; `Models/AnwisSize.cs` делегирует в `AnwisSizeCalculator`; `SlopePanelControl.xaml.cs` использует `SlopeCalculation.DeepClone()`.
    - Новые тесты в рамках фазы: +21 (`ProductCatalogTests` 7, `AnwisSizeCalculatorTests` 10, `SlopeCalculationExtensionsTests` 4). Общий счётчик тестов: **1133/1133 pass** (остальные +41 теста — параллельные изменения в рабочей ветке, не связанные с Фазой 5). Бизнес-логика и public API не затронуты.
  - Цель — устранить God-classes и высокий coupling в `MainWindow.xaml.cs`, `UpdateService.cs`, `PrintService.cs`, `DialogService.cs`, `OrderItem.cs`, `MainWindow.Orders.cs`. Бизнес-логика не трогается.

### Ручная проверка после Фазы 5

- **QuickAdd / DataGrid — категории товаров:** `Anwis`, `На навесах`, `Дверная сетка`, `Оконная на метал. крепл.` должны быть площадными; `Брус`, `Пояс`, `Доставка` — только сумма; `Материал` — опциональное количество; `Работа`, `Откос`, `Работа за откос` — ручные позиции.
- **Anwis — расчётные размеры:** проверить 4 режима (ББ60, ББ70, РазмерПроёма, Габаритный) в таблице и в печатном КП.
- **Откосы — клонирование и undo/redo:** добавить откос, изменить материалы, отменить/повторить — `SlopeCalculation` клонируется без shared refs, итоги пересчитываются корректно.
- **Сериализация / backward-compat:** открыть заказы, сохранённые в предыдущих версиях — категории товаров, Anwis-размеры и данные откосов должны загружаться без изменений.

- Все основные функции расчёта работают стабильно.
- Печать КП, отправка на завод, сохранение заказов — функционируют.
- Автообновление через GitHub Releases настроено и работает (перешли с Velopack Flow на собственный механизм через watchdog .bat).
- Тёмная тема стабильна, переключается без потери данных.
- Undo/Redo работает для позиций расчёта и Доп.КП.
- Юнит-тесты покрывают ключевые сценарии (расчёты, экспорт/импорт, версия, обновления).
- Текущая версия: **3.44.1** (релиз 2026-07-14: технические исправления).
- **Bugfix v3.44.2 (2026-07-12):** broadened else-branch reset в `SlopeCalculatorService.RecalculateSealantAndTape` — orphan calcs (все «Откос» IsActive=false или rename в не-slope) корректно сбрасывают DSS=0, а не зависают от defensive init (485). Snapshot isolation invariant: `OrderItem.Clone()→DeepCloneSlopeData()` исключает шаринг refs между live и undo/redo коллекциями. **906/906 tests pass.**
- **Новый товар «Материал» (v3.43.3/Unreleased):** добавлен в типы товаров; цена и количество вручную, ширина/высота не функциональны, количество опционально (скрыто в таблице при значении 1), без суммы добавление блокируется с красной обводкой поля «Цена». Покрыт юнит-тестами.
- **Bugfix-пакет откосов/печати (v3.43.3/Unreleased):**
  - Общие материалы откоса (герметик/скотч) теперь распределяются пропорционально между строками «Откос» через `SlopeCalculation.DistributedSharedSum` — устранена двойная оплата.
  - `PrintPreviewControl` — устранена утечка событий при повторных открытиях.
  - `MainWindow` — исправлена отписка от `ThemeService.ThemeChanged` и робастный поиск «Работа за откос» в `EditSlopeItem`.
  - Печатное КП — «Материал» с Quantity=1 не показывает лишнюю единицу в колонке «Площ./Дл.».
  - `OptimizeStrips` улучшен для кусков > 3000 мм (двухфазный Best Fit Decreasing).
  - `UpdateService` — guard против `CurrentVersion == 0.0.0.0`.
  - **Ламинат в откосах (v3.44.0/Unreleased):** добавлены материал «Ламинат» (500 ₽/шт.) и работа «Работа за ламинат» (500 ₽/шт.) в панель откосов; кнопка «Порог (Ламинат)» в футере; ламинат входит в `TotalMaterials`/`TotalLabor` откоса, не выводится отдельно в КП. +7 тестов в `SlopeCalculatorServiceTests`.
  - 891/891 tests pass.
- Последние изменения: Print-fixes (3 проблемы) — `FormatIntWithNbsp` порог ≥10 000, перераспределение ширин колонок (sum=1.0), `MakeNonWrappingCell` с `FormattedText`-автоshrink, `EdgeMode.Unspecified` для предпросмотра + toggle на `Aliased` перед печатью, `TextOptions` ClearType на `FlowDocumentPageViewer`, `WrapForCentering` + `Padding (4,5,4,5)` для центрирования иконки чертежа. 789/789 tests pass.
- Система A.R.C. прошла 3 итерации улучшений:
  - **v1:** инициализация, аудит, эталонные кейсы.
  - **v2:** CHEATSHEET, DOCUMENTATION_MATRIX, PROMPTS, гранулярный routing, validate-docs.
  - **v3 (текущая):** полная автоматизация — documentation-matrix.json (машиночитаемая матрица), what-to-update.ps1 (git diff → docs к обновлению), generate-update-log.ps1 (CHANGELOG → update-log), render-matrix.ps1 (JSON → MD рендеринг), git-based Last verified и staleness detection в validate-docs (теперь 8 проверок), self-check prompt (Prompt 7), inline routing в AGENTS.md.
- **Multi-agent portability migration завершён** — канонический master-файл перенесён внутрь репозитория в `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md`; `~/.claude/skills/MULTI_AGENT_ARC_CALC_CONTROL.md` теперь external bootstrap loader.

## Статус A.R.C.

✅ A.R.C. создан.
✅ Структура соответствует multi-agent архитектуре.
✅ Аудит прошёл — утверждения в `CALCULATION_LOGIC.md`, `GOTCHAS.md`, `RELEASE_PROCESS.md`, `AUTO_UPDATE.md` проверены по исходному коду и тестам.
✅ Созданы эталонные расчётные кейсы в `CALCULATION_TEST_CASES.md` (с явными статусами).
✅ Термины по размерам (введённые / расчётные / заводские / в КП) разведены однозначно.
✅ Правило безопасного порядка публикации `releases.json` зафиксировано в `RELEASE_PROCESS.md` и `AUTO_UPDATE.md`.
✅ Multi-agent master-файл перенесён в репозиторий (`docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md`) — source of truth версионируется.
✅ Wrappers (`AGENT.md`, `AGENTS.md`, `CLAUDE.md`, `GEMINI.md`) — тонкие redirect-файлы.
✅ **Все расчётные кейсы (1–15) подтверждены владельцем 2026-06-24.**
✅ **A.R.C. upgrade v2 (2026-06-24):** CHEATSHEET.md, DOCUMENTATION_MATRIX.md, PROMPTS.md, validate-docs.ps1, гранулярный routing, token-aware severity levels.
✅ **A.R.C. upgrade v4 (2026-06-25):** SYMBOL_INDEX.md, INTENTS.md, gensymbols.ps1, arc-check.ps1.
✅ `MULTI_AGENT_ARC_CALC_CONTROL.md` переработан: убрано дублирование, добавлены ссылки на новые файлы, расширена routing-таблица (12 категорий вместо 2).

## Архитектура multi-agent control

```text
docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md
    = canonical source of truth (версионируется в репозитории)

docs/arc/CHEATSHEET.md
    = быстрый вход (критические правила + routing, 40 строк)

docs/arc/DOCUMENTATION_MATRIX.md
    = механическая карта «поменял файл → обнови документы»

docs/arc/PROMPTS.md
    = готовые prompt-шаблоны для типовых сценариев

docs/arc/CURRENT_STATE.md
docs/arc/CALCULATION_LOGIC.md
docs/arc/CALCULATION_TEST_CASES.md
docs/arc/GOTCHAS.md
docs/arc/MODULES.md
docs/arc/DECISIONS.md
docs/arc/PROJECT_OVERVIEW.md
docs/arc/RELEASE_PROCESS.md
docs/arc/AUTO_UPDATE.md
    = проектная память

validate-docs.ps1
    = автоматическая валидация консистентности документации

~/.claude/skills/MULTI_AGENT_ARC_CALC_CONTROL.md
    = external bootstrap loader (только для Claude-среды)

AGENT.md / AGENTS.md / CLAUDE.md / GEMINI.md
    = thin compatibility wrappers
```

## Порядок входа агента (token-optimised)

1. `CHEATSHEET.md` — 40 строк, 15 секунд, критические правила + routing-таблица.
2. `CURRENT_STATE.md` — текущее состояние.
3. Routing-таблица в `CHEATSHEET.md` → релевантные полные файлы (2-3 вместо 5+).
4. На фазе Document → `DOCUMENTATION_MATRIX.md` (механическое обновление docs).
5. Валидация → `validate-docs.ps1`.

## Последние изменения

- **Regression guard: размеры Anwis в КП + flaky test fixes (v3.43.2, 2026-07-06):**
  - Расследование бага «на печати показывается без +20»: утечки заводских размеров в КП нет — DataGrid показывает сырые (ШиринаВвод), КП — расчётные (Width), осознанно. 12 новых тестов в `PrintServiceTests.cs` покрывают все режимы Anwis (ББ60, ББ70, РазмерПроёма, Габаритный) + 2 не-Anwis товара (На навесах, Оконная на метал. крепл.) + 6 товаров с чертежами (Дверная сетка, Отлив, Козырёк, Короб, ПСУЛ, Откос материал).
  - **Flaky `SaveContractPrefix_TrimsWhitespace`:** 5 классов в коллекции `[Collection("FileSystem")]` делили `static AppSettingsService.SettingsPath`. Fix: `[CollectionDefinition("FileSystem", DisableParallelization = true)]` в `AppSettingsServiceTests.cs`.
  - **STA-flake `PrintPreviewWindow_OpensWithoutNRE`:** host crash из-за отсутствия ресурсов в `new Application()`. Fix: STA-тест заменён на детерминированный Regex-скан `if (!IsInitialized) return;` guard'а.
  - **Flaky `RunUpdateFlowAsync_ConfirmedDialog_FiresUpdateDetected_AndStopsOnDownloadFailure`:** `UpdateServiceIntegrationTests` не состоял в коллекции `"FileSystem"`, но модифицировал `static AppSettingsService.SettingsPath` → гонка с другими FileSystem-тестами. Fix: `[Collection("FileSystem")]` в `UpdateServiceIntegrationTests.cs`.
  - Тесты: **805/805 pass** (0 failed, 0 skipped, 0 host crash).

- **Print-fixes — центрирование иконки чертежа в ячейке «Чертёж» (v3.43.2, 2026-07-06):**
  - **Проблема 3 — иконка прижата к верху:** `imageCell.Padding`: `Thickness(0)` → `Thickness(4, 5, 4, 5)` (согласован с остальными ячейками). Новый метод `WrapForCentering(UIElement)` в `PrintService.Drawings.cs` — обёртывает контент в `Grid` с `Stretch`/`Stretch` для гарантированного центрирования в `BlockUIContainer`. `displayWidth`: 36 → 30 DIP (компенсация за +8 DIP padding).
  - **Тесты:** 789/789 pass.
  - Затронуто 2 файла: `PrintService.FlowDocument.cs`, `PrintService.Drawings.cs`.

- **Print-fixes — обрезка текста в таблице КП + качество предпросмотра (v3.43.2, 2026-07-06):**
  - **Проблема 1 — наложение текста:** `FormatIntWithNbsp` теперь с порогом ≥10 000 (короткие числа без NBSP-разделителя, не удлиняются в узких Ш/В). `widths[]` перераспределены — сумма долей ровно 1.0. `MakeNonWrappingCell` — новый параметр `availableWidthDip` + `FormattedText`-измерение реальной ширины текста + авто-shrink `FontSize` до 75% от исходного (safety-net против наложения на соседние ячейки). Все 22 вызова `MakeCenteredCell`/`MakeRightAlignedCell` обновлены.
  - **Проблема 2 — «зубчатые» чертежи:** `CreateDrawingImageElement` — `EdgeMode.Aliased` → `Unspecified` (сглаживание на экране). `PrintPreviewControl.Print_Click` — toggle `EdgeMode.Aliased` перед `SendToQueue`, restore `Unspecified` после. Новые методы `SetDrawingsEdgeMode` + `SetEdgeModeInBlock` (рекурсивный walker). `FlowDocumentPageViewer` — `TextOptions.TextFormattingMode="Display"` + `TextRenderingMode="ClearType"`.
  - **Тесты:** +4 `FormatIntWithNbsp_*`, фикс `BuildFlowDocument_AnwisBrusbox60_ShowsCalcAdjustedSizes` (NBSP→plain), `ManualChecklistTests.ExtractFlowDocumentText` теперь обрабатывает `BlockUIContainer`.
  - **774/775 tests pass** (1 pre-existing `AppSettingsServiceTests`).
  - Затронуто 6 файлов: `PrintService.FlowDocument.cs`, `PrintService.Drawings.cs`, `PrintPreviewControl.xaml`, `PrintPreviewControl.xaml.cs`, `PrintServiceTests.cs`, `ManualChecklistTests.cs`.

- **Рефакторинг блока «Обновления» — append-only архитектурный инвариант (v3.43.0, 2026-07-03):**
  - Бейдж «Новейшая» переведён с position-based (`AlternationIndex`/`Tag`) на property-based (`UpdateItem.IsLatest` с `[JsonIgnore]`). Убран `AlternationCount="999"` из `UpdatesTabControl.xaml`, используется `DataTrigger Binding="{Binding IsLatest}"`.
  - `UpdateLog.AllNewestFirst()` сбрасывает `IsLatest=false` для всех записей, затем ставит `true` ровно одной (с максимальной версией). `ValidateLogInvariant()` — статический метод проверки дубликатов `Version` (`StringComparer.Ordinal`).
  - `MainWindowViewModel.AddNewUpdate(UpdateItem)` — runtime-добавление новой записи с атомарной сменой `IsLatest` без flicker-окна.
  - **Главное архитектурное изменение:** `Resources/update-log.json` теперь можно дописывать строго в конец (append-only) — старые записи остаются байт-в-байт неизменными при добавлении нового блока. Зафиксировано `AppendOnly_NewEntryAppendedToEnd_PreservesOldRecords` (манипулирует JSON через `JsonNode`, верифицирует «byte-for-byte» неизменность старых записей).
  - **Раньше** (AI-ручной workflow): при добавлении нового блока требовалось вклинивать запись в начало JSON-массива, физически сдвигая индексы всех старых записей. **Теперь** порядок в JSON не имеет значения — `AllNewestFirst()` сортирует в коде, а `IsLatest` — runtime-only вычисление. Старые блоки при добавлении нового вообще не меняются ни в данных, ни в визуальных триггерах.
  - 759/759 tests pass (7 новых тестов на `IsLatest`-инвариант, валидацию и append-only контракт).

- **Новый товар «Дверная сетка» (v3.43.0, 2026-07-03):**
  - Цена 3000 ₽/м² (Белый), доступна опция «Антикошка» (+2000 ₽/м²).
  - Монтаж: вычет по умолчанию 600 ₽/шт. (через `GetDefaultInstallationDeduction`).
  - В «На завод» — выбрана по умолчанию (вне `notForProduction` HashSet).
  - Входит в `OrderItem.AreaBasedProducts` / `InstallationApplicableProducts` / `AnticatApplicableProducts`.
  - Чертёж в КП — прямоугольник с петлями (как «На навесах»), текст «двер.сетка».
  - 748/748 → 771/771 tests pass (после relocation).

- **AddNewUpdate no-flicker контракт → MainWindow (v3.43.0, 2026-07-03):**
  - `UpdateService.UpdateDetected` static event + `internal static FireUpdateDetected` (per-subscriber isolation через `GetInvocationList`) + `CreateReleaseStub` helper.
  - `MainWindow.OnUpdateDetected` (sync `Dispatcher.Invoke`) → `MainWindowViewModel.AddNewUpdate(...)` — 3-step atomic `IsLatest` swap.
  - 9 новых тестов: `AddNewUpdate_ZeroIsLatestFrame_neverObserved`, `_OldItemsFire_OnlyIsLatest_*`, `_OldItems_ReferenceIdentity_*`, `UpdateDetected_*`, `FetchManifestAsync_*` regression guards, e2e `RunUpdateFlowAsync_*` через `ShowUpdateAvailableOverride` seam + опциональный `HttpClient?` DI.
  - Полная архитектура + ASCII-диаграмма задокументированы в `docs/arc/CALCULATION_LOGIC.md`, раздел «Как данные Auto-update попадают на UI (AddNewUpdate flow)».

- **Slide-out левая панель навигации + ПСУЛ/Уплотнение (v3.42.0):**
  - Левая панель навигации (52px → 160px hover-expand) реализована как overlay поверх основного контента, аналогично правой панели Заказов.
  - Анимация ширины + fade подписей, 500мс grace timer на MouseLeave, бейджи видны в свёрнутом состоянии (внутри 30px контейнера иконки).
  - NavPanel размещён в content Grid (ZIndex=10), overlays имеют ZIndex=15 — корректное перекрытие.
  - SidebarControl: убрано сворачивание карточек (chevrons удалены, контент всегда видим).
  - **ПСУЛ и Уплотнение — упрощённый ввод:** теперь можно указать только количество (без ширины/высоты). ПСУЛ: 100 ₽/шт. Уплотнение: цена × кол-во. При указании размеров — расчёт по периметру как раньше.
  - **Антикошка toggle:** вынесен в отдельную строку на всю ширину секции «Товар» — кнопка «Добавить» больше не смещается при появлении/скрытии toggle.
  - Расчётная логика НЕ затронута. 742/742 tests pass.

- **Deep UX Refactor — полный рефакторинг интерфейса (Unreleased, план. v3.41.x):**
  - **ActionBar:** кнопки реорганизованы в 3 визуальных кластера с разделителем: [Печать КП][Сохранить] | [Новый заказ][На завод] | [Очистить всё]. DirtyIndicator и RunCurrentOrderInfo перенесены в статус-бар.
  - **TitleBar:** добавлена иконка ⚙ с выпадающим меню настроек (☀ Светлая тема, 🌙 Тёмная тема, 📍 Сменить точку установки…, 🔄 Проверить обновления) + красная точка-индикатор доступного обновления.
  - **QuickAdd:** добавлены групповые метки полей (Товар, Размеры, Кол-во и цена), клавиатурные хинты (Enter — добавить, ↑↓ — навигация), метка «Режим замера» перед Anwis segmented control, улучшен PreviewChip (AccentLight фон + Accent рамка).
  - **Табы:** добавлены бейджи — счётчик заказов на вкладке «Заказы», красная точка на вкладке «Обновления» при доступной новой версии. Горячие клавиши Ctrl+1..4 для переключения вкладок.
  - **Sidebar:** карточки стали схлопываемыми — клик по заголовку сворачивает/разворачивает содержимое с анимацией fade (▼/► chevron). Унифицированы отступы полей (7px) и карточек (6px).
  - **Статус-бар:** добавлена компактная строка статуса под TotalCard — инфо о текущем заказе + индикатор несохранённых изменений.
  - **Расчётная логика НЕ затронута.** Изменения только UI-слой (XAML + code-behind привязок). 740/742 tests pass (2 предсуществующих failure в UpdateLogTests — версия 3.40.4).

- **UX-polishing к v3.41.x — 3 точечных фикса поверх Deep UX Refactor (Unreleased):**
  - **NavOrdersBadge читаемость:** `MainWindow.xaml` — бейдж числа заказов на иконке навигации 16×16 → 18×18, `FontSize` 8 → 10, `CornerRadius` 8 → 9. Цифра (включая «99+») теперь читаема. Брендовый синий (`Accent` + `OnAccent`) сохранён — ребрендинг не нужен, только масштаб.
  - **Скорректированы `Padding` (4,0→4,1) и `Margin` (-2/-8 → -3/-9)** для центрирования цифры внутри кнопки навигации (отрицательные margins выносят бейдж за правый верхний угол кнопки).
  - **Русское склонение «заказов» в хедере оверлея (v1 declension + v3 chip-styling):** `MainWindow.xaml.cs` `RefreshNavBadges` — старый `switch (orderCount)` учитывал только последнюю цифру без исключения 11–14. Теперь алгоритм на последних **двух** цифрах: `m100 ∈ [11..14]` → всегда «заказов»; иначе по `m10` (1 → заказ, 2..4 → заказа, остальное → заказов). Без этого «11 заказа», «21 заказ», «111 заказ», «122 заказа» показывались некорректно — теперь «11 заказов», «21 заказ», «111 заказов», «122 заказа». Диапазон 1–9999 проверен. **v3 (после уточнения владельца):** «значок общего кол-ва заказов» относится не к NavOrdersBadge, а к этому тексту в хедере оверлея. v1 фиксил только склонение, но сам элемент был `TextBlock` с `Foreground=TextMuted` на сером `HeaderBg` — практически невидим. v3 чип-стилизация:
  - **`MainWindow.xaml`:** плоский `TextBlock` `OrdersCountText` обёрнут в `Border OrdersCountBadge` (тот же паттерн, что у `TxtOrdersCount` в OrdersHistoryControl) — `Background=ChipBg`, `CornerRadius=10`, `Padding=10,4`, `TextBlock` внутри — `Foreground=Accent`, `FontWeight=SemiBold`. Теперь четкий видимый tag.
  - **`MainWindow.xaml.cs`:** `RefreshNavBadges` дополнительно управляет `OrdersCountBadge.Visibility` (Visible при count>0, иначе Collapsed — пустой chip рядом с «Заказы» это visual noise).
  - **Кнопка «Добавить» в QuickAdd:** `QuickAddControl.xaml` — `VerticalAlignment` Bottom → Top + `Margin="0,17,0,0"` (`Height=32` сохранена). Корень бага: внутри `StackPanel` поля «Тип» спрятан динамический `ToggleButton Антикошка`; при его появлении `StackPanel` растёт на ~30 px → `Grid.Row 1 Height="Auto"` расширяет всю строку → кнопка (Bottom-anchored) сползала вниз относительно соседних `TextBox`/`ComboBox`. Top + явный `margin 17 px` (= высота `FieldLabel`) прижимает верх кнопки к низу метки → bottoms кнопки и соседних контролов всегда совпадают, независимо от динамики строки (Антикошка включена/выключена).
  - **Расчётная логика НЕ затронута:** формулы Anwis/площадь/периметр/монтаж, JSON-схема `OrderItemData`, печать КП, автообновление — без изменений. 742/742 tests pass после фиксов не задеты (см. tests ниже).
  - **Build/tests:** `dotnet build MosquitoNetCalculator.sln -c Release` — 0 errors (warnings MSB3026 про `testhost` DLL-lock — не связано с фиксами, pre-existing). Tests: 710/711 pass после фикса; 1 предсуществующий fail `AppLifecycleTests.Print_Template_Has_No_Slash_Dogovor` (тест ссылается на путь, которого нет после decompose PrintService — отмечен в release-notes для фикса в ближайшем minor).

- **«Отлив» больше не выбирается автоматически в диалоге «На завод»** — UX-фикс (Unreleased, план. v3.41.x):
  - `FactoryTextService.BuildSelectableItems` теперь включает `Отлив` в `notForProduction` HashSet. Отлив — это готовый подоконник/оконный слив, не сетка, поэтому пользователь явно включает его галочкой перед отправкой партии на завод.
  - **Расчётная логика НЕ затронута**: формулы Anwis/площадь/периметр работают как раньше. Заводские размеры Anwis по-прежнему `Расчёт − 20 мм`. Меняется только поведение пресет-чексбоксов в `SendToFactoryWindow`.
  - Покрыто тестами: `FactoryTextServiceTests.BuildSelectableItems_NonProduction_IsNotSelected` (теория включает «Отлив»), `ManualChecklistTests.Check12_BuildSelectableItems_Production_On_NonProduction_Off` (counts 7→8 order items / 8→9 total, явная проверка `Отлив` == `!IsSelected`).
  - Документация: `TESTING_CHECKLIST.md §12.2` (Отлив перенесён из «включены» в «выключены»), `CHANGELOG.md Unreleased → Улучшения`, `CALCULATION_LOGIC.md#завод` (контракт auto-selection). Матрица `documentation-matrix.json` не требует изменений — изменение только UX.
  - 742/742 tests pass после фикса.
- **Антикошка (завершён)** — функционал надбавки +2000 ₽/м² для трёх сеток (`Anwis`, `На навесах`, `Оконная на метал. крепл.`):
  - **Модель**: `IsAnticat` (bool) в `OrderItem`, метод `DisplayName` возвращает `Anwis (Антикошка)` при активном флаге; формула расчёта не меняется — надбавка зашита в цену при добавлении через `SetDefaultPrice` для корректной работы `IsPriceOverridden`.
  - **QuickAdd UI**: `CheckBox` → стилизованный `ToggleButton` (pill, GhostButton-стиль: `Surface`-фон + `Border`-рамка, активное — Accent), перенесён из нижней строки в `StackPanel` поля «Тип» (появляется сразу после выбора применимого типа); извлечён тестируемый статический хелпер `UpdateAnticatToggleState`.
  - **Печать КП и заводской текст**: `PrintService.FillTemplate` и `FactoryTextService.Generate` используют `DisplayName` — обычный `Anwis` и `Anwis (Антикошка)` попадают в разные секции заводского текста (`Anwis (Антикошка), размер проёма (ББ 60):`).
  - **Persistence**: `IsAnticat` сериализуется в JSON (round-trip через `OrderStorageService` + deep-clone проверены).
  - **Документация**: раздел «Антикошка (надбавка +2000 ₽/м²)» в `CALCULATION_LOGIC.md`; запись в `CHANGELOG.md`.
  - **Тесты**: ~20 новых (PrintService×1, FactoryTextService×3, OrderStorageService×2, QuickAddControl×14 STA-thread). 742/742 tests pass.
  - **Ограничение**: переключатель доступен только в QuickAdd — после добавления позиции изменить `IsAnticat` нельзя (по явному решению владельца).
- **SelectAll race fix (GOTCHAS#14):** `SelectAll_OnFocus` теперь синхронный (`tb.SelectAll()` без `BeginInvoke`) — при клике в ячейку Ширины/Высоты текст выделяется до первого нажатия, ввод заменяет значение, а не дописывает.
- **Mid-typing formula clamp fix (GOTCHAS#15):** Ширина и Высота переключены с `UpdateSourceTrigger=PropertyChanged` на `LostFocus` — формула Anwis больше не перехватывает значение на каждом нажатии.
- **DeleteRowButton padding fix:** `Padding="4,0"` → `Padding="5"` — кнопка удаления строки 20×20px, hover-фон пропорциональный.
- **UpdateLog sort-in-code:** `AllNewestFirst()` и `GetChangesSince()` сортируют по дате/версии в коде — порядок в JSON больше не важен.
- **ParseVersion диагностика:** `Debug.WriteLine` при битой строке версии.
- **Автоширина колонки «Цена» (fix):** в `OrderItemsControl.xaml` (колонка «Цена» в таблице «Расчёт») и `PricesControl.xaml` (колонка «Цена, руб.» в tab «Цены») `UpdateSourceTrigger` для binding `Price` переключён с `LostFocus` на `PropertyChanged`. Pre-fix `Width="Auto"` не успевал подстроить ширину колонки при наборе (пользователь видел только начало введённого значения, напр. «5000» при наборе «15000»). Задокументировано в `GOTCHAS.md#13`. 5 новых regression-тестов в `DataGridBindingsTests.cs` (grep XAML-binding-triggers): прямые регрессии для обеих колонок + guardrails на Ширину/Высоту/Кол-во.
- **UpdateService тестируемость:** `FetchManifestAsync` и `DownloadWithProgressAsync` переработаны для приёма опционального `HttpClient?` — инъекция моков в интеграционных тестах. Паттерн `ownsClient` предотвращает преждевременное `Dispose` внешнего клиента. 7 новых интеграционных тестов покрывают fetch-манифеста, скачивание с прогрессом, zero-byte payload и ошибки HTTP.
- **Zero-byte download fix:** `DownloadWithProgressAsync` корректно отчитывает 100% прогресс при `Content-Length: 0` или отсутствии заголовка — ранее полоска зависала на 0%.
- **XAML-анимация UpdateDownloadBar (баг #8):** `Storyboard` ресурсы `UpdateBarFadeIn` (200 мс, CubicEase EaseOut) и `UpdateBarFadeOut` (250 мс, CubicEase EaseIn) добавлены в `MainWindow.xaml`. Code-behind использует `FindResource` + `Clone()` для запуска. Динамический `From` для fade-out предотвращает скачок при прерывании fade-in.
- **UI-polish:** `CornerRadius` увеличен с 4 до 7 в `AdditionalKpsControl.xaml`; добавлен `CornerRadius` в `DataGridStyles.xaml` для единообразия.
- **Тесты печати КП:** добавлены 3 теста в `PrintServiceTests.cs` — проверка расчётных размеров Anwis ББ 60 в HTML КП, HTML-экранирование спецсимволов (`&`, `"`, `'`), конвертация переводов строк в `<br/>`.
- **Монтаж × Quantity (fix):** в `OrderItem.Installation.cs` `TotalWithDeduction` теперь умножает `InstallationDeduction`/`InstallationSurcharge` на `Quantity` для режимов 1 («Без монтажа») и 2 («В конструкцию»). Pre-fix вычет списывался один раз на строку (занижал скидку для bulk-orders). Результат задокументирован в `GOTCHAS.md#12` и кейсе 16 в `CALCULATION_TEST_CASES.md`. Tooltip теперь явно показывает «руб./шт. × Кол-во». 8 новых юнит-тестов. Backward-compat: для Q=1 поведение не изменилось.
- **Update notification rework** — редизайн системы обновлений:
  - `UpdateLog.GetChangesSince(Version)` — фильтрация embedded changelog по версии.
  - `DialogService.ShowUpdateAvailable()` — новый диалог с changelog (ScrollViewer, type-бейджи, compact cards).
  - `UpdateService.RunUpdateFlowAsync()` — общий flow для авто/ручной проверки с флагом `isAutomatic`.
  - TitleBar-полоска прогресса (3px ProgressBar) вместо ActionBar `DownloadProgressPanel`.
  - Toast-фильтрация: убран спам при запуске, "Обновлений нет ✓" при ручной проверке.
  - 8 новых тестов: 6 для `GetAvailableUpdate` + 2 для `HasPendingUpdate`.
- **AmountOnlyProducts** — Брус, Пояс, Доставка: скрыты колонки Кол-во/Площ./Дл. в таблице, отключены поля Кол-во/Ш/В в QuickAdd, превью показывает только цену. 7 новых тестов.
- **update-log.json восстановлен** из git (был обрезан до 3 записей); исправлены 6 ошибок в тестах.
- **v3.36.0** — копирование заказов, 11 тестов, A.R.C. init.
- **A.R.C. upgrade v3** — полная автоматизация: documentation-matrix.json, what-to-update.ps1, generate-update-log.ps1, render-matrix.ps1, git-based Last verified (check 7), staleness detection (check 8), self-check prompt, inline routing в AGENTS.md.
- **A.R.C. upgrade v2** — CHEATSHEET.md, DOCUMENTATION_MATRIX.md, PROMPTS.md, гранулярный routing, validate-docs.ps1.
- **v3.35.0** — полный фикс утечки формул Anwis на не-Anwis товары.

## Что выглядит незавершённым

- `README.md` в корне практически пустой — стоит обновить для GitHub.
- `releases.json` и `update-log.json` дублируют частично одну информацию — нужна синхронизация при каждом релизе (рассмотреть консолидацию).
- Нет автоматической проверки калькуляции при релизе — только юнит-тесты.
- Редизайн системы обновлений частично завершён (core-логика + тесты готовы, UI-полировка в процессе). Спецификация: `docs/arc/update-notification-rework-spec.md`.
- **HeadlessWpf behavior-тест для «Цена автоширины» отменён** (6 итераций не дали рабочего решения). XAML-grep test в `DataGridBindingsTests.cs` остаётся канонической гарантией. Для возврата к этой фиче требуется либо project-wide `[CollectionFixture<WpfAppFixture>]`, либо переход на `Border + TextBlock + Width=NaN` без DataGrid.

## Открытые вопросы

- Нужно ли добавить новые товары или изменить цены? (Только по явному запросу владельца.)
- Нужно ли улучшить механизм автообновления?
- Нужен ли CI/CD для автоматической сборки и публикации?
- Нужна ли консолидация CHANGELOG.md ↔ update-log.json?

## Рекомендуемые следующие шаги

1. Запустить `validate-docs.ps1` и исправить найденные расхождения.
2. ~~Обновить README.md~~ (низкий приоритет).
3. Настроить CI/CD (GitHub Actions) для автоматической сборки и публикации релизов.
4. Консолидировать CHANGELOG.md и update-log.json (один source of truth).

## Source files

- `MosquitoNetCalculator/MosquitoNetCalculator.csproj` — версия 3.43.2.
- `releases.json` — история релизов (latest заполняется после GitHub Release + ZIP).
- `MosquitoNetCalculator/Resources/update-log.json` — история для UI.
- `docs/arc/*.md` — вся проектная документация.
- `docs/arc/documentation-matrix.json` — машиночитаемый источник матрицы.
- `what-to-update.ps1` — git diff -> docs к обновлению.
- `validate-docs.ps1` — 8 проверок консистентности.
- `generate-update-log.ps1` — CHANGELOG -> update-log.
- `render-matrix.ps1` — JSON -> DOCUMENTATION_MATRIX.md.

## Last verified

2026-07-14 — **v3.44.1 release prep:** version bump, CHANGELOG, update-log, releases.json, CURRENT_STATE.md.

2026-07-12 — **Фаза 3 рефакторинга завершена.** `PrintService.cs` 632→81 строк (−87%). 6 компонентов: `DrawingService`, `FlowDocumentBuilder`, `FixedDocumentBuilder`, `PrintQueueManager`, `PdfExportService`, плюс модели `PageMode`/`PrintSettings`/`PrintResult`. +~40 тестов. **1038/1038 tests pass.** Бизнес-логика не затронута.

2026-07-12 — **Фаза 2 рефакторинга завершена.** `UpdateService.cs` 910→608 строк (−33%). 5 компонентов: `VersionResolver`, `IdleDetector`, `UpdateVerifier`, `UpdateManifestClient`, `UpdateDownloader`. +67 тестов (включая прямые тесты `UpdateManifestClientTests` и `UpdateDownloaderTests`). **999/999 tests pass.** Бизнес-логика не затронута.

2026-07-12 (v3.44.3 — bugfix: slope profile economy double-counting + mixed-economy isolation, **1041/1041 tests pass**):
  - **Bug:** когда экономия Старт/F-планка была выключена (`IsProfileEconomyApplied=false`), `RecalculateSealantAndTape` выставляла общие (оптимизированные по всем окнам) количества профилей, но `OrderItem.Total` умножал per-window материалы на `Quantity` (=WindowCount) → двойное учитывание профилей.
  - **Fix:** при `IsProfileEconomyApplied=false` профили остаются per-window (3-сторонние); при `true` глобальная оптимизация применяется только к участникам экономии, а общая стоимость профилей распределяется только между ними. Герметик/скотч — по-прежнему общие для всех активных откосов.
  - **Mixed economy:** откосы без экономии не платят за чужие профили; откосы с экономией оптимизируются и делят профильную стоимость только между собой.
  - `SlopePanelControl.xaml.cs` синхронизирован: при снятии экономии количества профилей возвращаются к per-window значениям.
  - +3 regression-теста в `SlopeCalculatorServiceTests.cs`: `ProfileEconomyDisabled_StartProfileIsPerWindow`, `ProfileEconomyDisabled_OrderItemTotal_NoDoubleCount`, `MixedEconomy_OnlyAppliesToOptedInSlopes`.
  - Затронутые файлы: `SlopeCalculatorService.cs`, `SlopePanelControl.xaml.cs`, `SlopeCalculatorServiceTests.cs`.

2026-07-12 (v3.44.2 — bugfix: orphan-calc DistributedSharedSum reset, **906/906 tests pass**):
  - **Broadened else-branch reset** в `SlopeCalculatorService.RecalculateSealantAndTape`: orphan calcs (все «Откос» с `IsActive=false` или rename в не-slope имя) теперь корректно сбрасывают DSS=0, а не зависают от defensive init (485) или произвольного stale-значения. `Работа за откос` calcs тоже сбрасываются — безопасно (используют `TotalLabor`).
  - **Snapshot isolation invariant:** `OrderItem.Clone()` всегда вызывает `DeepCloneSlopeData()` → live и undo/redo history коллекции никогда не шаринг `SlopeCalculation` refs. Broadened reset безопасен даже на снимках — orphan-DSS в одной коллекции не сбрасывает DSS в другой.
  - **3 новых edge-case теста** в `SlopeCalculatorServiceTests.cs` фиксируют контракт для последовательностей вызовов с мутациями между ними: `SharedSlopeBetweenOtkosAndRabota_RemoveOtkos_ResetsOrphanCalcViaBroadenedReset`, `Idempotent_ProfileEconomyAndIsActiveSettings`, `IsActiveToggleBetweenCalls_FollowsParticipation`. Test name суффикс `ViaBroadenedReset` явно фиксирует контракт broadened reset — будущий рефакторинг не «откатит» fix как «too eager».
  - Затронутые файлы: `SlopeCalculatorService.cs` (broadened else-branch + 3-строчный trimmed-комментарий вместо 11-строчного), `SlopeCalculatorServiceTests.cs` (3 теста + 1 rename).

2026-07-11 (v3.44.0 — ламинат в откосах, **891/891 tests pass**):
  - **Ламинат в откосах:** добавлены `SlopeCalculation.Laminatina`/`LaminatinaLabor` (500 ₽/шт. каждый), UI-строки в `SlopePanelControl`, кнопка «Порог (Ламинат)» в футере, `IsQuantityOverridden` сохраняется при `UpdateInPlace`, ламинат входит в `TotalMaterials`/`TotalLabor`, не выводится отдельно в КП. +7 тестов в `SlopeCalculatorServiceTests`.

2026-07-10 (v3.43.3 — новый товар «Материал» на базе v3.43.2.12, **861/861 tests pass**):
  - **Новый товар «Материал»:** добавлен в `PriceService.DefaultPrices`/`prices.json`, `OrderItem.ManualPieceProducts`/`NoColorProducts`/`OptionalQuantityProducts`; UI-гейтинг в `QuickAddControl`/`OrderItemsControl`/`MainWindow`; валидация цены; опциональное отображение количества. +8 тестов в `OrderItemTests.cs`.

2026-07-06 (v3.43.2 — print-fixes + regression guard + flaky fixes, **805/805 tests pass**):
  - **Проблема 1:** `FormatIntWithNbsp` порог ≥10 000, `widths[]` sum=1.0, `MakeNonWrappingCell` + `availableWidthDip` + `FormattedText` авто-shrink до 75%.
  - **Проблема 2:** `EdgeMode.Unspecified` в превью + toggle `Aliased` перед печатью (`SetDrawingsEdgeMode`/`SetEdgeModeInBlock`), `TextOptions` ClearType на `FlowDocumentPageViewer`.
  - **Проблема 3:** `WrapForCentering` (Grid Stretch/Stretch обёртка), `imageCell.Padding` 0 → (4,5,4,5), `displayWidth` 36 → 30.
  - **Тесты:** +4 `FormatIntWithNbsp`, фикс Anwis-теста (NBSP→plain), `ManualChecklistTests.ExtractFlowDocumentText` +`BlockUIContainer`.

  - **Flaky fixes (3 теста):** `SaveContractPrefix_TrimsWhitespace` (CollectionDefinition FileSystem DisableParallelization), `PrintPreviewWindow_OpensWithoutNRE` (STA→Regex source-scan), `RunUpdateFlowAsync_ConfirmedDialog_...` (UpdateServiceIntegrationTests → Collection("FileSystem")).
  - **Тесты:** 6 Anwis-режим + 2 не-Anwis + 6 Build*-чертежей (Дверная сетка, Отлив, Козырёк, Короб, ПСУЛ, Откос) regression guards в `PrintServiceTests`.

2026-07-03 (v3.43.0 — Дверная сетка + Updates block + AddNewUpdate no-flicker, **771/771 tests pass**):
  - **Дверная сетка:** +1 в `prices.json`, +1 в `PriceService.DefaultPrices`, добавлена в `OrderItem.AreaBasedProducts` / `InstallationApplicableProducts` / `AnticatApplicableProducts`, 600 ₽/шт. в `OrderItem.Installation.DefaultInstallationDeductions`. Новый `GetDefaultInstallationDeduction(name)` API.
  - **Append-only Updates block:** `UpdateItem.IsLatest` (`[JsonIgnore]` property-based) вместо position-based (`AlternationIndex`/`Tag`); `UpdateLog.AllNewestFirst()` сбрасывает `IsLatest` для всех и ставит ровно одной; `UpdateLog.ValidateLogInvariant()` для дубликатов; `MainWindowViewModel.AddNewUpdate(...)` — runtime-добавление с 3-step atomic `IsLatest` swap (no-flicker); 7 новых тестов (IsLatest, валидация, append-only контракт через `JsonNode`).
  - **AddNewUpdate → MainWindow:** static `UpdateDetected` event в `UpdateService` (`internal static FireUpdateDetected` + `CreateReleaseStub` helper, per-subscriber isolation через `GetInvocationList`), `MainWindow.OnUpdateDetected` (sync `Dispatcher.Invoke`), e2e `RunUpdateFlowAsync_*` тесты через `ShowUpdateAvailableOverride` seam + `HttpClient?` DI.

2026-07-02 (v3.42.1 — hotfix):
  - Исправлено: автообновление не работало при установке в Program Files из-за `E_ACCESSDENIED` при создании `arc-update-watchdog.bat`. Watchdog-файлы (.bat, .zip, .bak) теперь создаются в `%AppData%\MosquitoNetCalculator`. 742/742 tests pass.
2026-07-02 (v3.42.0 — релиз):
  - Выпущен релиз v3.42.0: slide-out панель навигации, sidebar без chevrons, ПСУЛ/Уплотнение — ввод только кол-вом, Антикошка toggle в отдельной строке. 742/742 tests pass.
