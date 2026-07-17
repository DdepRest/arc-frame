# Changelog

## 3.46.0 — 2026-07-16

### Новое — форматирование примечаний в КП

- **Лёгкий markup для поля «Примечания»:** теперь текст примечаний можно оформлять прямо в сайдбаре.
  - `**жирный**` — выделение жирным.
  - `*курсив*` — выделение курсивом.
  - `[color=#RRGGBB]цветной текст[/color]` — цветной текст (поддерживаются hex и named colors).
  - `- список` — маркированный список.
- **Панель форматирования в сайдбаре:** кнопки «Ж», «К», цветные кнопки (красный/зелёный/синий/оранжевый) и «Список» оборачивают выделенный текст или текущую строку в соответствующие теги.
- **Live-превью:** под полем ввода примечаний добавлена область предпросмотра, которая сразу показывает, как форматирование будет выглядеть в печатном КП.
- **Единый рендерер:** `Services/NotesRenderer.cs` конвертирует распарсенные примечания в WPF `Inline` одинаково для печатного КП (`FlowDocumentBuilder`) и для live-превью (`SidebarControl`).
- **PDF-экспорт:** `PdfExportService` рендерит форматированные примечания с сохранением жирного, курсива и цвета.
- **Тесты:** +18 тестов — `NotesFormatterTests.cs` (12) и `NotesRendererTests.cs` (6 STA). Все существующие тесты продолжают проходить.

### Техническое

- `Services/NotesFormatter.cs` (NEW) — парсер лёгкого markup: `**bold**`, `*italic*`, `[color=...]...[/color]`, `- list item`.
- `Services/NotesRenderer.cs` (NEW) — WPF-рендерер `NoteLine`/`NoteSegment` → `Inline`.
- `Services/FlowDocumentBuilder.cs` — `BuildNotesSection` теперь использует `NotesFormatter` + `NotesRenderer`.
- `Services/PdfExportService.cs` — блок примечаний в PDF теперь рендерит форматирование и разделяет строки через `EmptyLine()`.
- `Controls/SidebarControl.xaml/.cs` — добавлена панель инструментов форматирования и live-превью `FlowDocumentScrollViewer`.

## 3.46.1 — 2026-07-17

### UI — переключатель +/- в системе монтажа

- **ToggleSwitch +/-** в контекстном меню монтажа: крупный зелёный/красный переключатель
  `SignToggleCheckBox` (48×26 px) с тактильной обратной связью при нажатии, заменяет
  мелкий 20×20 CheckBox.
- **Только значки в таблице:** в колонке «Монтаж» теперь отображаются только **V**
  (включён), **X** (без монтажа), **В** (в конструкцию), **—** (не предусмотрен).
  Значки без фона и обводки, с мягким hover-эффектом (Opacity 0.65).
- **Hover сохраняет цвет состояния:** при наведении меняется только прозрачность,
  цвет значка (зелёный/красный/серый) остаётся неизменным.
- **Убранные иконки:** старые цветные Fluent-иконки режимов монтажа удалены,
  кнопка стала простой текстовой без лишнего визуального шума.
- Новое свойство `InstallationButtonLabel` в `OrderItem.Installation.cs`
  с уведомлениями об изменении (`OnPropertyChanged`).

### Техническое

- `Models/OrderItem.Installation.cs` — свойство `InstallationButtonLabel` возвращает
  однобуквенные метки: `"V"`, `"X"`, `"В"`, `"—"`.
- `Themes/InputStyles.CheckBox.xaml` — стиль `SignToggleCheckBox`: ToggleSwitch
  48×26 px, Success/Danger цвета, IsPressed анимация.
- `Controls/OrderItemsControl.xaml` — кнопка 34×26 px без фона/обводки,
  `Content="{Binding InstallationButtonLabel}"`.
- Удалены неиспользуемые `IntEqualsConverter.cs`, `BoolToCursorConverter.cs`
  и их регистрации в `App.xaml`.
- **Тесты:** 1227/1227 pass (без изменений — только UI).

────────────────────────────────────────

## 3.45.0 — 2026-07-15

### Техническое — завершение Фазы 6 рефакторинга (`MainWindow.Orders.cs`)

Из `MainWindow.Orders.cs` (527 → 226 строк, −57 %) выделены три чистых компонента. Бизнес-логика не тронута, public/internal API сохранены как тонкие прокси.

- **`Services/OrderGridPresenter.cs` (NEW, static internal, ~140 строк)** — pure-helper для DataGrid заказов: `RefreshOrdersGrid(grid, orders)` (autosize колонок к содержимому, restore SortDescriptions, apply sort indicators), `ApplySortIndicators(grid)` (▲/▼ arrows), `GetColumnSortKey(col)` (SortMemberPath → Binding.Path), `IsHeaderClick(hit)` (visual tree walker для double-click filter). Паттерн совпадает с `DataGridColumnAutoSizer` (static internal helpers).
- **`Services/OrderImportExportService.cs` (NEW, instance, ~210 строк)** — оркестратор file-IO поверх `OrderStorageService` + `OrdersHistoryViewModel`. Методы: `ExportAllOrders`, `ExportSingleOrder` (BuildSingleOrderFileName pure helper), `ImportOrders` (multi-select dialog), `CopyOrder` (DeepCloneOrder + identity mutation + Save + return new contract number). Все сигнатуры `Window? owner = null` (matches `DialogService.ShowConfirm` pattern) — позволяет headless-test early-return paths без MTA-thread Window ctor.
- **`Controls/ChangeOrderStatusWindow.xaml + .cs` (NEW, ~150 строк)** — хромосом-диалог изменения статуса заказа в Phase 4 паттерне (`MessageDialogWindow`). Заменяет ~110 строк legacy inline-XAML в `MainWindow.ChangeSelectedOrderStatus`. ComboBox заполнен через `{x:Static models:OrderStatuses.All}`, IsEditable-gated SelectAll, DragMove title bar, анимированный ✕ button (Danger overlay fade 150 мс).
- **`MainWindow.Orders.cs`** — теперь тонкий orchestrator: lazy-init `OrderImportExportService`, `RefreshOrdersList` делегирует grid-логику в `OrderGridPresenter`, `BtnExportOrders_Click`/`BtnImportOrders_Click`/`ExportSelectedOrder`/`CopySelectedOrder` делегируют в service, `ChangeSelectedOrderStatus` показывает `ChangeOrderStatusWindow`. **`OpenSelectedOrder` оставлен инлайн** (комплексный multi-VM flow: ClientInfo + Sidebar + CalcVM + UndoRedo — extracting в service потребовал бы state-handle context object хуже текущего delegation). Удалён мёртвый `using System.Windows.Media;` (VisualTreeHelper переехал в presenter).
- **Nullable-tolerance contract:** `DeepCloneOrder(OrderData? source)`, `BuildSingleOrderFileName(OrderData? order)`, `CopyOrder(OrderData? source)`, `ExportSingleOrder(OrderData? order)`, `ExportAllOrders(IReadOnlyList<OrderData>? orders)`. Если caller передаёт null — defensive early-return с toast/informative message; никаких NRE на null order.

### Техническое — тесты (+27, итого 1179/1179)

- **`Services/OrderGridPresenterTests.cs` (NEW, 12 STA тестов):** GetColumnSortKey (4: SortMemberPath / Binding.Path / null / null column), IsHeaderClick (4: column header / row header / generic hit / null), ApplySortIndicators (5: ascending/descending arrows, idempotent re-render, no-sort clears arrow, no-matching-key doesn't pick foreign arrow), **RefreshOrdersGrid (4: null grid guard, null orders guard, sets ItemsSource, restores SortDescriptions after ItemsSource swap — capture-карта regression inline RefreshOrdersList)**.
- **`Services/OrderImportExportServiceTests.cs` (NEW, 12 pure тестов):** DeepCloneOrder (4: independent ref, null source, preserves all fields incl. IsAnticat, no-mutate source), BuildSingleOrderFileName (8: empty/null/whitespace address fallback, slash→space, uppercase + collapse spaces, strip invalid chars `<>|*"`, 60-char trim, null order, regression round-trip).
- **`Services/OrderImportExportServiceValidationTests.cs` (NEW, 4 [Collection("FileSystem")] тесты):** early-return path coverage без UI-host dependencies — ExportAllOrders_NullOrders/EmptyList, ExportSingleOrder_NullOrder, CopyOrder_NullSource.

### Техническое — backward-compat

- Поведение, типы, public API не изменились. Все существующие 1143 → 1179 тестов (1143 baseline + 27 Phase 6) проходят. Производственные формулы, JSON-контракт `OrderData`, печатное КП, автообновление, сериализация заказов — без изменений.
- `OpenSelectedOrder` оставлен в partial-классном `MainWindow.Orders.cs` (как было до рефакторинга) — 10+ VM-полей (ClientInfo, CalcVM, Sidebar, UndoRedo, ...). Решение зафиксировано в `REFACTORING_PLAN.md §8` + этот CHANGELOG.
- Генерация `update-log.json` автоматическая через `generate-update-log.ps1` (Phase A.R.C. v3).

### Исправления после физического QA

- **Натуральная сортировка по колонке «№ КП»:** стандартная WPF-сортировка строк вела себя лексикографически — например, «2-9» оказывалась выше «2-21». Добавлено cached свойство `OrderData.ContractNumberSortKey` (`[JsonIgnore]`), которое pre-pads каждое число в номере КП до 10 цифр (`«2-1»` → `«0000000002-0000000001»`), после чего обычный string-compare даёт правильный числовой порядок. В `OrdersHistoryControl.xaml` для колонки «№ КП» установлен `SortMemberPath="ContractNumberSortKey"` (отображение по-прежнему через `ContractNumber`). Sort-индикаторы (▲/▼) продолжают работать через `OrderGridPresenter.ApplySortIndicators`. Добавлен 11 юнит-тестов в `OrderDataSortKeyTests.cs` (Theory-based: `2-9 < 2-10`, `2-9 < 2-21`, `10-1 > 2-1`, memoization, padding invariant).

- **«Изменить статус» теперь открывает диалог:** динамически генерируемые sub-items в контекстном меню «Изменить статус» не работали надёжно из-за WPF ContextMenu caching на virtualized rows. Теперь пункт меню «Изменить статус...» сразу открывает уже существующее модальное окно `ChangeOrderStatusWindow` (Phase 4). Удалены мёртвые методы `RefreshStatusSubMenu` и `ChangeSelectedOrderStatusInline`, а также их вызов; единственный flow — `MainWindow.ChangeSelectedOrderStatus()` → `ChangeOrderStatusWindow.ShowDialog()`.

────────────────────────────────────────

## 3.44.1 — 2026-07-14

### Технические исправления

Исправления стабильности и внутренние улучшения.

---

## 3.44.0 — 2026-07-14

### Автопросчёт откосов — новая система

Новая панель откосов автоматически рассчитывает все материалы по размерам окна:

- Автоматически рассчитывает по ширине, высоте, глубине и количеству откосов: сэндвич (м²), пена (баллоны), герметик/скотч (общие на весь заказ), Старт/F-планка (полосы 3 м), пеноплекс (листы), работа (м.п.).
- **Экономия материалов:** при нескольких откосах программа объединяет расход герметика/скотча и оптимизирует раскрой Старт/F-планки по всем окнам, что снижает итоговую сумму.
- **Режим экономии переключаемый:** галочка «Применить экономию» позволяет сравнить сумму с экономией и без неё прямо в панели.
- **Ручные правки:** количество и цена любого материала можно изменить вручную — авто-пересчёт не перезапишет ручные значения (чип «ручн.»).
- **Детали экономии:** отдельное окно с расшифровкой, сколько и на каком материале сэкономлено, с tooltip'ами для каждой строки.
  - Новая кнопка «Детали экономии» в панели откосов открывает окно с расшифровкой экономии по каждому материалу.
  - Показывает общую экономию по всем откосам и экономию на 1 откос.
  - Для каждой строки (герметик, скотч, Старт, F-планка) отображается количество с экономией и без, а также итоговая сумма.
  - При наведении на строку появляется tooltip с подробным расчётом: сколько тюбиков/полос нужно без экономии, сколько с экономией и сколько сэкономлено.
  - Корректно работает при смешанных размерах откосов — экономия считается по всем окнам суммарно, а не по первому размеру.
- **BETA-плашка:** в панели откосов показывается предупреждение, что функция в стадии тестирования; плашку можно закрыть и больше не видеть.
  - Жёлтая плашка BETA с предупреждением: функция в стадии тестирования, рекомендуется перепроверить расчёт вручную.
  - Плашка закрываемая: кнопка × сохраняет состояние «скрыто» в настройках пользователя.
- **Ламинат в откосах:** материал и работа добавляются кнопкой «Порог (Ламинат)».
  - В панели откосов добавлен материал «Ламинат» (500 ₽/шт.) и работа «Работа за ламинат» (500 ₽/шт.).
  - В таблице материалов откоса добавлена строка «Ламинат» с редактируемым количеством и ценой.
  - Под строкой «Работа» добавлена строка «Работа за ламинат».
  - В футере панели откосов добавлена кнопка «Порог (Ламинат)» — увеличивает количество ламината и работы за него на 1.
  - Ламинат входит в общую сумму откоса как материал (`TotalMaterials`), работа за него — как труд (`TotalLabor`).
  - В печатном КП не выводится отдельной строкой; сумма входит в итоговые суммы откоса.

### Нативный движок печати КП

Новый нативный движок печати КП полностью заменяет старую браузерную эмуляцию:

- **Никаких задержек при открытии предпросмотра:** больше не нужно ждать загрузки браузерного движка — окно появляется мгновенно.
- **Идеальное совпадение экрана и бумаги:** всё, что вы видите в предпросмотре, выходит на принтере один в один — без расхождений и неожиданных переносов.
- **Встроенный экспорт в PDF:** сохранение коммерческого предложения в PDF работает напрямую, без зависимостей от сторонних программ.
- **Удобная панель масштабирования:** новый тулбар `[ − / N% / + / 100% ]` для точного зума документа.
- **Заметный счётчик копий:** поле «Кол-во копий» в Fluent-стиле — цифры видно сразу при открытии.
- **Единый дизайн:** окно печати получило такую же шапку, как в основной программе; убран лишний визуальный шум старого браузера.
- **Полная работа с принтерами Windows:** вы видите весь список установленных принтеров и можете выбрать принтер по умолчанию прямо из окна печати.

### Новое

- **Новый товар «Материал»:
  - Добавлен в выпадающий список «Тип» в QuickAdd и в каталог цен (`prices.json` / `PriceService.DefaultPrices`).
  - Без цвета, цена и количество задаются вручную.
  - Ширина/высота не функциональны — поля отключены в QuickAdd и не редактируются в DataGrid.
  - Количество опционально: при `Quantity = 1` в таблице отображается только сумма; при количестве > 1 показываются и количество, и сумма.
  - Без указания суммы товар не может быть добавлен в список просчёта — поле «Цена» подсвечивается красной обводкой как обязательное.
  - Расчёт: `Total = Price × Quantity`, единица измерения — «шт.».

### Исправления

- **Сброс цен к значениям по умолчанию не работал.**
  - **Корневая причина:** `PricesViewModel.ResetPrices()` удалял `prices.json` из `AppDomain.CurrentDomain.BaseDirectory` (папка установки), тогда как `PriceService` читает/пишет `%AppData%\MosquitoNetCalculator\prices.json` (архитектурное требование: цены переживают авто-обновление, которое заменяет install-папку). Delete в BaseDirectory молча не находил файл, после чего `LoadPrices()` читал старые цены из AppData — UI выглядел неизменённым. Точно такой же класс багов уже был в `WatchdogService` в v3.42.1.
  - Fix: `ResetPrices()` теперь использует `PriceService.PricesPath` (AppData). Удалён избыточный `SavePrices()` после `ResetPrices()` (`PriceService.LoadPrices()` уже сохраняет defaults при отсутствии файла).
  - Убраны `IsCancel`/`IsDefault` bindings в `MessageDialogWindow.xaml` — WPF автоматически выставлял `DialogResult` для этих кнопок до выполнения кастомного `Click`-обработчика, и `SelectedResult` мог не записаться. Enter/Escape теперь обрабатываются вручную в `Window_KeyDown`.
  - Добавлен toast «Цены сброшены к значениям по умолчанию».
  - +3 регрессионных теста в новом файле `MosquitoNetCalculator.Tests/ViewModels/PricesViewModelResetTests.cs` (sentinel reset, BaseDirectory guard, recreated file).
  - Затронутые файлы: `ViewModels/PricesViewModel.cs`, `Controls/PricesControl.xaml.cs`, `Controls/MessageDialogWindow.xaml`, `Controls/MessageDialogWindow.xaml.cs`.

- **Распределение общих материалов откоса (герметик/скотч) — устранена двойная оплата.**
  - `SlopeCalculatorService.RecalculateSealantAndTape` теперь считает общее количество герметика/скотча по всему заказу и распределяет стоимость пропорционально `WindowCount` между строками «Откос» через новое свойство `SlopeCalculation.DistributedSharedSum`.
  - `OrderItem.Calculations.cs` для «Откос» теперь использует `DistributedSharedSum` вместо `Sealant.Sum + Tape.Sum`, исключая умножение общей стоимости на количество строк.
  - Защитная инициализация `DistributedSharedSum` в `SlopeCalculatorService._ApplyDefaults` для одиночного откоса/изолированного использования.
  - Тест `SlopeDataChildMaterialQuantityChange_CascadeRefreshesOrderItemTotal` обновлён — вызывает `RecalculateSealantAndTape` перед проверкой каскада.

- **Экономия Старт/F-планка в откосах — устранено двойное учитывание и смешение участников.**
  - **Bug:** когда галочка «Применить экономию» была снята, `StartProfile`/`FProfile` количества выставлялись как общие для всех окон заказа, но `OrderItem.Total` умножал per-window материалы на `Quantity` (=WindowCount) → стоимость профилей учитывалась дважды.
  - **Fix:** при `IsProfileEconomyApplied=false` `RecalculateSealantAndTape` теперь оставляет per-window (3-сторонние) количества профилей; `OrderItem.Total` корректно умножает их на количество окон.
  - **Mixed economy:** глобальная оптимизация профилей теперь применяется только к откосам с `IsProfileEconomyApplied=true`; общая стоимость Старт/F-планка распределяется только между участниками экономии, а герметик/скотч — по-прежнему между всеми активными откосами.
  - `SlopePanelControl` синхронизирован: при снятии экономии количества профилей возвращаются к per-window значениям.
  - +3 regression-теста в `SlopeCalculatorServiceTests.cs`: `ProfileEconomyDisabled_StartProfileIsPerWindow`, `ProfileEconomyDisabled_OrderItemTotal_NoDoubleCount`, `MixedEconomy_OnlyAppliesToOptedInSlopes`.

- **Печатное КП — устранены утечки событий и UI-регрессии.**
  - `PrintPreviewControl.OnUnloaded` теперь корректно отписывается от `PageFromControl`/`PageToControl`/`SinglePageControl`/`CopiesControl` и удаляет `_pageNumberDescriptor` handler, предотвращая утечку памяти при повторных открытиях.
  - `MainWindow.xaml.cs`: исправлена отписка от `ThemeService.ThemeChanged` — теперь отписывается `OnThemeChanged` вместо несовпадающего `ApplyMicaTitleBar`.
  - `MainWindow.EditSlopeItem`: поиск парной строки «Работа за откос» теперь устойчив к удалению/сортировке строк (ищет по имени + порядковому номеру, а не строго по `idx + 1`).
  - `PrintService.FlowDocument.cs` и `PrintService.Pdf.cs`: колонка «Площ./Дл.» для «Материал» теперь пустая при `Quantity <= 1`, как в DataGrid.
  - `SlopeCalculation.IsQuantityOverridden`: уведомляет только об изменении самого флага; агрегаты `TotalMaterials`/`TotalLabor`/`GrandTotal` обновляются через `OnChildMaterialChanged`.
  - `UpdateService.GetAvailableUpdate`: добавлен guard против `CurrentVersion == 0.0.0.0`, чтобы не предлагать обновление при неопределённой текущей версии.
  - `PrintService.cs`: фон `FlowDocument` восстанавливается в `finally` после печати.
  - `MainWindow.OnPrintPreviewClosed`: настройки печати сохраняются перед отпиской от `Closed`.

- **Orphan-calc DistributedSharedSum сбрасывается корректно.**
  - Когда после вызова `RecalculateSealantAndTape` не остаётся активных строк «Откос» (все отключены или переименованы), `DistributedSharedSum` обнуляется для всех связанных `SlopeCalculation`.
  - Snapshot isolation invariant: `OrderItem.Clone()` вызывает `DeepCloneSlopeData()` → каждая коллекция (live и undo/redo history) имеет свой собственный `SlopeCalculation`-инстанс.

- **Оптимизация раскроя `OptimizeStrips` для кусков > 3000 мм.**
  - Новый двухфазный алгоритм: куски ≥ 3000 мм разбиваются на полные полосы + остатки, остатки собираются в общий пул и упаковываются по алгоритму Best Fit Decreasing.
  - Улучшает использование материала, например `[3500, 3500]` теперь требует 3 полос вместо 4.


## 3.44.2 — 2026-07-12

### Исправления

- **Bugfix: orphan-calc DistributedSharedSum сбрасывается корректно.**
  - `SlopeCalculatorService.RecalculateSealantAndTape` — broadened else-branch: когда после вызова НЕТ ни одной активной строки «Откос» (`slopeItems.Count == 0` или `totalWindowCount == 0`), DSS обнуляется для **всех SlopeData, на которые сейчас ссылаются items в коллекции** (не только из `allSlopeData` с фильтром `Name == "Откос"`, но и из полного `items.Where(SlopeData != null)`). Это покрывает два кейса, в которых старая логика зависала с устаревшим значением от defensive init (485) или с произвольным stale-значением:
    1. Все строки «Откос» с `IsActive=false` — orphan после отключения чекбокса.
    2. Orphan после `slopeItem.Name = "Отлив"` (rename из «Откос» в не-slope) — раньше calc выпадал из `allSlopeData` и не сбрасывался; теперь reset идёт по ref-ссылкам из любых items коллекции.
  - **Безопасно для `Работа за откос`:** он использует `TotalLabor`, а не `DSS` — reset happily в 0, никакого визуального эффекта.
  - **Snapshot isolation invariant:** `OrderItem.Clone()` вызывает `DeepCloneSlopeData()` → каждая коллекция (live и undo/redo history) имеет свой собственный `SlopeCalculation`-инстанс; broadened reset не «просачивается» между коллекциями через разделяемые ссылки.

### Тесты

- **3 новых edge-case теста** в `SlopeCalculatorServiceTests.cs` фиксируют контракт для последовательностей вызовов с мутациями между ними:
  - `RecalculateSealantAndTape_SharedSlopeBetweenOtkosAndRabota_RemoveOtkos_ResetsOrphanCalcViaBroadenedReset` — DSS=485 → rename в «Отлив» → DSS=0 → restore → DSS=485 (renamed test catches the broadened reset contract).
  - `RecalculateSealantAndTape_Idempotent_ProfileEconomyAndIsActiveSettings` — 4 одинаковых вызова с включённой экономией Start/F-планка, `DSS`/`Total` идентичны, sum-of-distributions = true shared cost (включая Start+F-Profile).
  - `RecalculateSealantAndTape_IsActiveToggleBetweenCalls_FollowsParticipation` — three-way split → toggle inactive → DSS=0 для выключенной, redistribute → toggle active → three-way resume.
- **Тесты:** 906/906 pass (903 предыдущих + 3 новых).

---

## 3.43.2.12 — 2026-07-12

### Исправления

- **Print-fixes: «Коричневый» зажёвывается в Цвет-колонке печатного КП.** Юзер сообщил, что в КП видит «оричневы» вместо «Коричневый» — обе крайние символы отсекаются (первая «К» и последняя «й»). Тот же эффект нашли для «Золотой дуб» (11 char — самый длинный color name в каталоге, цвета для Отлив/Козырёк/Короб).
  - **Корневая причина:** `MakeNonWrappingCell` auto-shrink safety-net останавливался на floor `0.75 × fontSize`. Для bodyFontSize=12 DIP это 9 DIP, что давало ширину «Коричневый» ≈ 70 DIP при `usableWidths[2] = 0.08 × 670 − 14 = 39.6 DIP` → overflow ≈ 30 DIP → при горизонтальном centering обе крайние символы клипаются.
  - **Первоначальный fix (отклонён):** floor 0.75 → 0.55. Текст влез, но стал нечитаемо мелким (~5–7.5 DIP). Юзер: «Стало ничего не видно, слишком мелко».
  - **Окончательный fix:** widen Цвет 0.08 → 0.12 (budget взят у № 0.05→0.04, Цена 0.10→0.09, Ш 0.08→0.075, Монтаж 0.075→0.07, Чертёж 0.10→0.09). Теперь `usableWidths[2] = 0.12 × 670 − 14 = 66.4 DIP`, и «Коричневый»/«Золотой дуб» shrink только до ~11.4 DIP — читаемо. Floor оставлен 0.75 (= 9 DIP) как safety-net для future ultra-long color names — читаемость в приоритете.
  - **Почему не lower-floor:** lower-floor лечит симптом (overflow), но создаёт новый дефект — нечитаемый мелкий текст. Widen column лечит причину: длинные color names получают достаточно места.
  - **Не задевает остальные колонки:** № 0.05→0.04 остаётся достаточным для 2-digit индексов (usable 12.8 DIP, body «10» ~12 DIP). Цена 0.10→0.09 остаётся достаточным для «1 800,00» (usable 46.3 DIP, body ~42 DIP). Ш/Монтаж/Чертёж немного уменьшены, но их контент (числа, галочка, чертёж) остаётся читаемым.

### Техническое

- `Services/PrintService.FlowDocument.cs::BuildItemsTable`:
  - `widths[]`: Цвет 0.08 → 0.12, № 0.05 → 0.04, Цена 0.10 → 0.09, Ш 0.08 → 0.075, Монтаж 0.075 → 0.07, Чертёж 0.10 → 0.09. Сумма долей = 1.00.
  - `DrawingColumnWidthDip`: 0.10 → 0.09 (aligns с новой шириной Чертёж для BuildTotalSection margin).
  - `CreateDrawingImageElement` displayWidth: 44 → 42 (fits в Чертёж 0.09).
  - Comment-block (line ~218) пересчитан — точные числа для новых usable widths.
- `Services/PrintService.FlowDocument.cs::MakeNonWrappingCell`:
  - Floor restored 0.55 → 0.75 (was incorrectly left at 0.55 after first attempt). XML-doc + inline-комментарий обновлены — описывают историю бага, первоначальный lower-floor fix и окончательный widen-column fix.
- `Tests/PrintServiceTests.cs` +2 STA regression tests обновлены:
  - `BuildFlowDocument_Korichnevy_NoHorizontalClip` — «Anwis Коричневый» 1000×1000.
  - `BuildFlowDocument_ZolotoyDub_NoHorizontalClip` — «Отлив Золотой дуб» 1000×800.
  - Verifier: `measuredWidth <= usableWidths[2] (66.4 DIP) + 0.5 DIP tolerance` AND `fontSize >= 11.0 DIP` (readability guard против повторного aggressive shrink).
- Затронутые файлы: 2 (Services + Tests). Производительность: 0 effect.
- **Backward-compat:** orders с длинными color names теперь рендерятся в КП читаемо — «Коричневый»/«Золотой дуб» на ~11.4 DIP. Никаких API/JSON изменений. Производственная data не задета.

---

## 3.43.2.11 — 2026-07-11

### Исправления

- **Флип sign convention в «Монтаж включён» (корректировка после пользовательского фидбэка):** v3.43.2.10 зашипнул convention «+ вычитает, − добавляет» (по аналогии с deduction в modes 1/2), но интуитивно ожидаемая convention — «+ добавляет, − вычитает». Формула и tooltip-ы развёрнуты, чтобы поведение совпадало с привычным чтением чисел.
  - **Положительное значение** (напр. 500) — **добавляется** к Total (надбавка).
  - **Отрицательное значение** (напр. `-200`) — **вычитается** из Total (как deduction в режимах 1/2).
  - **Ноль** (по умолчанию) — Total остаётся без изменений (старое поведение).
  - **Новая формула:** `TotalWithDeduction = Math.Round(Math.Max(0, Total + InstallationAdjustment × Quantity), 2)`.
  - Примеры (с теми же числами, но противоположным знаком эффекта):
    - Base = 1800 ₽, Кол-во = 1, Adjustment = 500 → `TotalWithDeduction = 2300 ₽` (добавили 500).
    - Base = 1800 ₽, Кол-во = 1, Adjustment = `−200` → `TotalWithDeduction = 1600 ₽` (вычли 200).
    - Base = 5400 ₽ (1.5 м² × 1200 × 3 шт.), Кол-во = 3, Adjustment = `−300` → `TotalWithDeduction = 4500 ₽` (per-piece × Quantity scale).

### Техническое

- `Models/OrderItem.Installation.cs` — формула `TotalWithDeduction` для mode 0: `Total − Adjustment × Q` → `Total + Adjustment × Q` (тот же `Math.Round(Math.Max(0, …), 2)`, противоположный знак). `InstallationToolTip` для mode 0: положительное теперь «+X добавляется», отрицательное теперь «−X вычитается». XML-doc-суммары и inline-комментарии обновлены под новую convention.
- `MainWindow.Items.cs` — `RefreshDeductionField` tooltip: «Сумма корректировки: положительное значение добавляется к сумме, отрицательное — вычитается». Inline-комментарии обновлены.
- **Без изменений:** `Models/OrderItem.Dto.cs` (default 0 = no-op), `Models/OrderItem.cs::Clone()/ToOrderItemData()` (пробрасывают значение как есть), `ViewModels/CalculationViewModel.cs` (для новых позиций инициализация 0), modes 1/2 «Без монтажа» и «В конструкцию» (клампят в собственных setters; «надбавка» через эти режимы по-прежнему невозможна).
- **Backward-compat:** orders.json с `InstallationAdjustment`, сохранённые между v3.43.2.10 и v3.43.2.11 (если такие успели появиться), будут интерпретированы с инвертированной логикой. Поскольку v3.43.2.10 не публиковался широко и пользователь явно запросил flip ДО публикации — миграция не требуется. Production-orders.json с `InstallationAdjustment=0` (default) не подвержены влиянию — Total passthrough для mode 0 сохранён.
- **Тесты:** в `OrderItemTests.cs` обновлено 6 тестов под новую convention:
  - `…_PositiveAdjustment_Subtracts` → `…_PositiveAdjustment_Adds` (expected: 1300 → 2300).
  - `…_NegativeAdjustment_Adds` → `…_NegativeAdjustment_Subtracts` (expected: 2000 → 1600).
  - `…_AdjustmentScaledByQuantity` оставлен с флипнутым value (6300 → 4500; inline-коммент обновлён).
  - `…_ClampsToZero_WhenAdjustmentExceeds` — флипнут от большого положительного к большому отрицательному (500 → −1500); положительное после флипа НЕ вызывает clamp (100 + 1500 = 1600 > 0), поэтому нужна именно большая отрицательная сумма для проверки той же `Math.Max(0, …)`-ветки.
  - `…_AdjustmentPositive_ShowsMinus` → `…_AdjustmentPositive_ShowsPlus` (expected: «−500» → «+500»).
  - `…_AdjustmentNegative_ShowsPlus` → `…_AdjustmentNegative_ShowsMinus` (expected: «+300» → «−300»).

---

## 3.43.2.10 — 2026-07-11

### Новое

- **Режим «Монтаж включён» теперь поддерживает редактируемую сумму корректировки (±).** В контекстном меню кнопки монтажа (ранее доступное только для режимов «Без монтажа» ✕ и «В конструкцию» В) поле «Сумма:» теперь доступно и в режиме «Монтаж включён» ✓. По умолчанию 0.
  - **Положительное значение** (напр. 500) — вычитается из Total (как deduction в режимах 1/2).
  - **Отрицательное значение** (напр. `-200`) — добавляется к Total (формула `Total − Adjustment × Quantity` сама инвертирует знак).
  - **Ноль** (по умолчанию) — Total остаётся без изменений (старое поведение).
  - Примеры:
    - Base = 1800 ₽, Кол-во = 1, Adjustment = 500 → `TotalWithDeduction = 1300 ₽` (вычли 500).
    - Base = 1800 ₽, Кол-во = 1, Adjustment = `−200` → `TotalWithDeduction = 2000 ₽` (добавили 200).
    - Base = 5400 ₽ (1.5 м² × 1200 × 3 шт.), Кол-во = 3, Adjustment = `−300` → `TotalWithDeduction = 6300 ₽` (per-piece × Quantity scale).

### Техническое

- `Models/OrderItem.Installation.cs` — новое поле `_installationAdjustment = 0` + свойство `InstallationAdjustment` (БЕЗ `Math.Max(0, …)` clamp — принимает знаковые значения). `CurrentInstallationAmount` для mode 0 возвращает adjustment, `SetCurrentInstallationAmount` убрал `if (value < 0) value = 0`. `TotalWithDeduction` для mode 0: `Math.Round(Math.Max(0, Total − InstallationAdjustment × Quantity), 2)` (формула единая — отрицательная adjustment даёт сложение). `InstallationToolTip` для mode 0 отображает «±X руб./шт. × Кол-во» с явным объяснением знакового конвенции в зависимости от знака adjustment.
- `Models/OrderItem.Dto.cs` — поле `InstallationAdjustment { get; set; } = 0` → backward-compat: orders.json без этого поля загружаются как «без изменений» (no-op).
- `Models/OrderItem.cs` — `Clone()` и `ToOrderItemData()` пробрасывают `InstallationAdjustment`.
- `ViewModels/CalculationViewModel.cs` — `AddItem` инициализирует `InstallationAdjustment = 0` для новых позиций; `LoadFromOrderData` маппит `od.InstallationAdjustment` (DTO default 0 = старая заказ без поля).
- `MainWindow.Items.cs::BtnToggleInstallation_Click` — `RefreshDeductionField` теперь ВСЕГДА включает `txtDeduction` (раньше только для modes 1/2); tooltip объясняет знаковую конвенцию (+) vs (−). `CommitDeductionIfPending` убрал `val >= 0` — отрицательный ввод теперь коммитится для mode 0.
- **Регрессия:** modes 1/2 «Без монтажа» и «В конструкцию» по-прежнему клампят InstallationDeduction/Surcharge к 0 в собственных setters — «добавить» через эти режимы невозможно, только «вычесть». Тесты `SetCurrentInstallationAmount_Mode1/2_StillClampsNegatives` фиксируют это.
- **Backward-compat:** orders.json сохранённые до этого релиза загружаются чисто: `InstallationAdjustment = 0` → для mode 0 effect отсутствует (Total passthrough). Существующие строки в режимах 1/2 продолжают работать как раньше.
- **Тесты:** +18 юнит-тестов в `OrderItemTests.cs` покрывают все аспекты: default/positive/negative values, PropertyChanged firing, CurrentInstallationAmount mode 0, SetCurrentInstallationAmount mode 0, TotalWithDeduction формула для mode 0 (все 3 варианта знака + per-piece Quantity scaling + clamp-to-zero edge case), tooltip текст для mode 0 (нулевой/положительный/отрицательный adjustment), independence между режимами при переключении, Clone preserves, regression для modes 1/2.

---

## 3.43.2.9 — 2026-07-10

### Новое

- **Монтаж-toggle для товара «Оконная на метал. крепл.»** — расширение существующего функционала «антимонтаж / в конструкцию» (ранее только Anwis / На навесах / Дверная сетка). В левом меню «Тип» теперь можно добавить «Оконную на метал. крепл.» и кликнуть по переключателю монтажа в колонке таблицы — те же три режима: «Монтаж включён» ✓ / «Без монтажа» ✕ (−500 ₽/шт. × Кол-во) / «В конструкцию» (−500 ₽/шт. × Кол-во). Дефолтный вычет — 500 ₽ (через `DefaultInstallationDeductionFallback`), как у Anwis; если в будущем нужно будет задать специфичную сумму — добавить запись в `DefaultInstallationDeductions` в `OrderItem.Installation.cs` (паттерн «Дверная сетка» = 600).

### Техническое

- `Models/OrderItem.cs` — `InstallationApplicableProducts` HashSet: добавлено «Оконная на метал. крепл.». Архитектура уже позволяла расширять список — `IsInstallationApplicable`, `InstallationLabel`, `KpInstallationDisplay`, `InstallationToolTip`, `BtnToggleInstallation_Click` уже гейтятся через эту HashSet. Никаких UI-изменений не потребовалось — кнопка-тултип/меню появляется автоматически.
- **Backward-compat (важно):** заказы, сохранённые до v3.43.2.9 с строками «Оконная на метал. крепл.», при открытии в новой версии будут показывать ✓/✕/В в колонке монтажа вместо серого «—». Данные не повреждаются — `InstallationMode=0` (дефолт), `InstallationDeduction=500` (уже был в DTO). Только визуальное изменение: раньше монтаж был «не предусмотрен» (UI-серая), теперь «включён» (зелёная ✓). Никаких действий от пользователя не требуется.
- **Тесты:** добавлен `IsInstallationApplicable_OkonnayaNaMetallKrepl_True` в `OrderItemTests.cs` (одиночный locked-кейс); `Check3_InstallationToggle_AppliesTo_MountedProducts` в `ManualChecklistTests.cs` (переименован с `_Only_To_Anwis_And_Navesi`) теперь проверяет все четыре applicable-продукта — Anwis / На навесах / Дверная сетка / Оконная на метал. крепл. — с инверс-проверкой Отлив/ПСУЛ.
- Расчётная логика (m²), пенель выбора цвета, Антикошка — без изменений (Оконная на метал. крепл. уже была в `AreaBasedProducts` и `AnticatApplicableProducts`).

---

## 3.43.2.8 — 2026-07-09

### Юмор — Easter-egg «PRO подписка» в меню Откосы со СТРОГИМ циклом

- **Шуточное модальное окно `EasterProUpsellWindow`** показывается на КАЖДЫЙ клик по «Откосы» в левом slide-out menu, пока пользователь явно не «Оплатит»:
  - Текст: «🪄 Автопросчёт откосов доступен по подписке PRO — оформить за **350 ₽/мес**?»
  - Две кнопки внизу: «Оплатить» (PrimaryButton, `IsDefault=True`) и «Отклонить» (GhostButton, `IsCancel=True` — native ESC-handling).
  - На «Оплатить» — модалка переключается на второе состояние: «😄 Это шутка! Калькулятор откосов всегда был бесплатным — пользуйтесь на здоровье.» + кнопка OK (`IsDefault=True`).
  - На «ОК» в шутке — флаг `SlopesProUpsellUnlocked=true` выставляется, slope открывается. Диалог больше НИКОГДА не показывается.
  - **На «Отклонить» / X / ESC** — панель откосов НЕ открывается, флаг НЕ выставляется (per user requirement: «последующие разы тоже выпадает эта меню, пока не нажмётся Оплатить»).
- **СТРОГИЙ цикл (strict loop):**
  - `unlocked=true`  → диалог НЕ показывается, slope открывается сразу.
  - `unlocked=false` → диалог показывается; Pay → mark → open; Decline/X/ESC → mark=false → на следующем клике диалог снова.
- **Mark-after-Pay discipline** (v3.43.2.9): флаг выставляется ТОЛЬКО после явного «Оплатить» → OK, а не «mark-before-show» (раньше был mark-before-show — joke-collision опасность).
- **Toggle-close escape:** если панель уже открыта и пользователь кликает «Откосы» чтобы закрыть — диалог НЕ показывается (закрываем молча).
- **Левый-nav label:** `Откосы` → `Авто · Откосы` (13 chars @ FontSize=12 Segoe UI, умещается в ~108px раскрытого nav menu; «Авто» сигнализирует автопросчёт).
- **Срабатывает ТОЛЬКО в `NavSlope_Click` (на menu-click)** — НЕ на Ctrl+5 (Print) и НЕ из двойного клика по строке «Откос» в OrderItemsControl.

### Техническое — state machine + лёгкое удаление

- **`SlopesProUpsellSeen` → `SlopesProUpsellUnlocked`** (semantic rename): раньше имя «Seen» означало «диалог был показан» (mark-before-show). Теперь «Unlocked» означает «пользователь явно Оплатил» (mark-after-Pay). Backward-compat: старый ключ «SlopesProUpsellSeen» в settings.json пользователей после апгрейда просто игнорируется System.Text.Json → пользователь увидит шутку ещё раз, что является корректным поведением (они ещё не разблокировали).
- `EasterMenuService.ShowSlopesProUpsellIfNotUnlocked(Window?) → bool`:
  - `unlocked=true` → short-circuit return true (no dialog).
  - Show dialog → `if (DialogResult==true) MarkSlopesProUpsellUnlocked(); return true; else return false;`.
- `MainWindow.NavSlope_Click` — toggle-close short-circuit + try/catch graceful fallback (info-toast и открыть slope при I/O-error) + early-return на `!unlocked`. Избыточный `alreadySeen` pre-check устранён — теперь вся логика в `EasterMenuService`.
- `EasterProUpsellWindow.xaml` — `BtnDecline` получил `IsCancel="True"` для native ESC-mapping; цена обновлена до `350 ₽/мес`.
- **Удаление шутки — чеклист 6 шагов** (полный текст в XML-doc на `EasterMenuService` + этот пункт CHANGELOG):
  1. Удалить `Services/EasterMenuService.cs`
  2. Удалить `Controls/EasterProUpsellWindow.xaml` + `.xaml.cs`
  3. В `Services/AppSettingsService.cs` удалить поле `SlopesProUpsellUnlocked` + пару `IsSlopesProUpsellUnlocked/MarkSlopesProUpsellUnlocked`
  4. Убрать вызов `EasterMenuService.ShowSlopesProUpsellIfNotUnlocked(this)` из `MainWindow.xaml.cs::NavSlope_Click` (одна строка + коммент)
  5. Вернуть `Text="Откосы"` в `MainWindow.xaml` (NavLabelSlope)
  6. (опц.) удалить этот пункт CHANGELOG.md
- **Grep для будущего аудита:** `EasterMenuService` / `EasterProUpsellWindow` / `SlopesProUpsellUnlocked` / `"Авто · Откосы"`.
- **Backward-compat:** ключ `SlopesProUpsellUnlocked` отсутствует в settings.json у пользователей после удаления аксессоров — `System.Text.Json` его игнорирует, settings остаются валидными.
- **Тесты:** без новых тестов — существующий 822/822 pass покрытие достаточно (easter egg non-essential, основной workflow не зависит).

## 3.43.2 — 2026-07-06

### Исправления

- **Print-fixes: обрезка текста в таблице КП (Проблема 1).**
  - `FormatIntWithNbsp` — порог ≥10 000: числа короче 5 знаков теперь без NBSP-разделителя, не удлиняют строку в узких колонках «Ш»/«В». Ранее даже 4-значное `1002` форматировалось как `1 002` с NBSP.
  - `widths[]` перераспределены: № +0.01, Наим −0.005, Монтаж +0.005, Площ −0.01, Сумма −0.005. Сумма долей = 1.00 (было 0.995).
  - `MakeNonWrappingCell` — добавлен параметр `availableWidthDip` + `FormattedText`-измерение реальной ширины текста + авто-shrink `FontSize` до 75% от исходного, если текст не влезает в колонку. Предотвращает наложение на соседние ячейки.
  - Все 22 вызова `MakeCenteredCell`/`MakeRightAlignedCell`/`MakeNonWrappingCell` обновлены — прокинут `usableWidths[i]`.

- **Print-fixes: низкое качество предпросмотра чертежей (Проблема 2).**
  - `CreateDrawingImageElement` (`PrintService.Drawings.cs`): `EdgeMode.Aliased` → `Unspecified` — сглаживание линий на экране (раньше жёстко `Aliased` для печати 600 DPI давал видимую лесенку в превью).
  - `PrintPreviewControl.Print_Click`: перед `SendToQueue` → `SetDrawingsEdgeMode(Aliased: true)`, после (ошибка/catch/finally) → restore `Unspecified`. Новые методы `SetDrawingsEdgeMode` + `SetEdgeModeInBlock` (рекурсивный walker по `Table`→`Cell`, `Section`, `BlockUIContainer`→`Image`).
  - `PrintPreviewControl.xaml` — `FlowDocumentPageViewer`: `TextOptions.TextFormattingMode="Display"` + `TextRenderingMode="ClearType"` + `UseLayoutRounding=True`.

### Техническое

- **Тесты:**
  - `PrintServiceTests.cs`: +4 теста `FormatIntWithNbsp_*` (порог 10k, NBSP для больших/без NBSP для малых).
  - `PrintServiceTests.cs`: обновлён `BuildFlowDocument_AnwisBrusbox60_ShowsCalcAdjustedSizes` (NBSP → plain для Width=1002).
  - `ManualChecklistTests.cs`: `ExtractFlowDocumentText` теперь обрабатывает `BlockUIContainer` (все ячейки таблицы с v3.44.6 используют `BlockUIContainer` → TextBlock, старый экстрактор их не видел).
  - 774/775 tests pass (1 pre-existing `AppSettingsServiceTests`).
- Затронутые файлы: `PrintService.FlowDocument.cs`, `PrintService.Drawings.cs`, `PrintPreviewControl.xaml`, `PrintPreviewControl.xaml.cs`, `PrintServiceTests.cs`, `ManualChecklistTests.cs`.

- **Print-fixes: центрирование иконки чертежа в ячейке «Чертёж» (Проблема 3).**
  - `PrintService.Drawings.cs`: новый метод `WrapForCentering(UIElement)` — обёртывает контент в `Grid` с `Stretch`/`Stretch`, гарантируя центрирование внутри `BlockUIContainer` (раньше Image прижимался к верхнему краю).
  - `PrintService.FlowDocument.cs` — `imageCell.Padding`: `Thickness(0)` → `Thickness(4, 5, 4, 5)` (согласован с остальными ячейками).
  - `displayWidth`: 36 → 30 DIP (компенсация за +8 DIP горизонтального padding).
  - 789/789 tests pass.

- **Regression guard: размеры Anwis в печатном КП (расследование, 2026-07-06).**
  - Пользователь сообщил, что печать показывает размеры «без +20» (заводские вместо расчётных). Анализ всего печатного конвейера (`PrintService.FlowDocument`, `Drawings`, `Pdf`, `MainWindow.ShowPrintOverlay`) показал: утечки заводских размеров нет. DataGrid показывает сырые размеры (`ШиринаВвод`), а КП — расчётные (`Width`) — это осознанное проектное решение (расчётные синхронны с площадью/ценой).
  - **6 новых тестов** в `PrintServiceTests.cs` фиксируют контракт для всех режимов Anwis + двух не-Anwis товаров:
    - `BuildFlowDocument_AnwisBrusbox60_Raw500x1000_ShowsCalc502x970_NotRawNorFactory` — ББ60: raw 500×1000 → calc 502×970, NOT raw/factory.
    - `BuildFlowDocument_AnwisBrusbox70_Raw500x1000_ShowsCalc498x970_NotRawNorFactory` — ББ70.
    - `BuildFlowDocument_AnwisRazmerProyoma_Raw600x1200_ShowsCalc620x1220_NotRaw` — РазмерПроёма.
    - `BuildFlowDocument_AnwisGabarityj_Raw500x1000_ShowsCalc500x1000_NotFactory` — Габаритный.
    - `BuildFlowDocument_NaNavesah_ShowsStoredDimensions` — не-Anwis «На навесах».
    - `BuildFlowDocument_OkonnayaNaMetallKrepl_ShowsStoredDimensions` — не-Anwis «Оконная на метал. крепл.».

- **Flaky test fixes (3 теста, 2026-07-06):**
  - `SaveContractPrefix_TrimsWhitespace` (expected "3", got "1") — пять классов в коллекции `[Collection("FileSystem")]` делили `static AppSettingsService.SettingsPath`. Без явного `CollectionDefinition` с `DisableParallelization` xUnit мог перемежать ctor/dispose разных классов. **Fix:** добавлен `[CollectionDefinition("FileSystem", DisableParallelization = true)]` в `AppSettingsServiceTests.cs`.
  - `PrintPreviewWindow_OpensWithoutNRE_DuringInitialXamlParse` (host crash `Environment.FailFast`) — тест создавал голый `new Application()` без ресурсов, но `PrintPreviewControl.xaml` использует десятки `{DynamicResource}` (Surface, Accent, PrimaryButton, GhostButton, ...). Отсутствие Style-ключей вызывало `XamlParseException` → WPF `FailFast`. **Fix:** STA-тест заменён на детерминированный source-code scan — Regex-проверка `if (!IsInitialized) return;` guard'а в `UpdateDimmingOverlay()`.
  - `RunUpdateFlowAsync_ConfirmedDialog_FiresUpdateDetected_AndStopsOnDownloadFailure` — `UpdateServiceIntegrationTests` модифицирует статический `AppSettingsService.SettingsPath` (редирект на temp-файл), но класс не состоял в коллекции `"FileSystem"`. xUnit запускал его параллельно с другими тестами коллекции (AppSettingsServiceTests, PriceServiceTests, ...), вызывая гонку: `SavePendingUpdateVersion` писала в чужой temp-файл, а `LoadPendingUpdateVersion` читала из другого. **Fix:** добавлен `[Collection("FileSystem")]` в `UpdateServiceIntegrationTests.cs` — теперь тесты класса сериализованы со всей FileSystem-коллекцией.

---

## 3.43.1 — 2026-07-03

### Исправления

- **Crashfix: `NullReferenceException` в `PrintPreviewWindow.UpdateDimmingOverlay()` при первом открытии окна предпросмотра КП.** XAML парсился по порядку: `AllPagesCheck.IsChecked="True"` обрабатывался во время `InitializeComponent` раньше, чем создавались `DimmingOverlay` / `RangeHint` (правая колонка, `Grid.Column="2"`). Добавлен guard `if (!IsInitialized) return;` — идиоматический WPF-чек (становится `true` только после завершения `InitializeComponent`; `IsLoaded` тут не подходит — срабатывает ещё позже).
- **Из тела печатного КП убрано слово «Договор».** Заголовок теперь компактнее: `КОММЕРЧЕСКОЕ ПРЕДЛОЖЕНИЕ / № X-XXX от DD.MM.YYYY` (раньше было `Договор № X-XXX от DD.MM.YYYY`). Колонтитулы `PrintPaginator` и `PrintService.Pdf.cs` остались без изменений — это было лишь избыточное слово в основной строке КП.

### UI/UX — окно предпросмотра КП

- **Кастомный title bar в стиле основной программы** (`PrintPreviewWindow.xaml`): `WindowStyle=None` + `<WindowChrome WindowChrome.CaptionHeight="40">` + переиспользованный `TitleBarControl` (новая `bool ShowSettings` DependencyProperty — скрывает кнопку ⚙, т.к. в окне предпросмотра нет настроек). Win32 `WndProc` ловит `WM_GETMINMAXINFO` (через `MONITORINFO`/`RECT`) — maximized-окно больше не уходит под панель задач на multi-monitor setups.
- **NumericUpDown «Кол-во копий»** (`Controls/NumericUpDownControl.xaml`) обёрнут в Fluent-`Border` с явными `Surface`/`Border`/`CornerRadius=6` и явным стилем `ValueTextBox` (`TextPrimary` foreground) — цифры видны сразу при первом открытии, а не как в полупрозрачной ячейке.
- **Новый Fluent RadioButton** (`Themes/InputStyles.RadioButton.xaml`): implicit `Style TargetType=RadioButton` — внешний круг 18×18 + внутренняя Accent-точка 8×8, плавный fade-in при `IsChecked=True`. Подключён глобально через merge ResourceDictionary в `App.xaml`. Покрывает RadioButton «Цветная»/«Ч/б» печать.
- **Range-блок прячется через DataTrigger** при выборе «Все страницы» (`PrintPreviewWindow.xaml`): внешний `Border` с `Style Trigger Binding="{Binding IsChecked, ElementName=AllPagesCheck}"` → `Visibility=Collapsed` (раньше гасился `IsEnabled` — визуально «серое на сером»). Убраны избыточные	label-ы «с»/«по»/«или»/«Страница» — поля говорят сами за себя через `ToolTip`.
- **Кастомный Zoom-тулбар** `[ − / N% / + / 100% ]` над документом (`PrintPreviewWindow.xaml` + `.cs`): три Fluent-кнопки + текущий процент; `<FlowDocumentReader>` заменён на `<FlowDocumentPageViewer>` (нет встроенного тулбара + есть публичный `Zoom`-DP). Стандартный зум `FlowDocumentReader` без UI — не работал у пользователя.
- **Затронутые файлы:** `PrintPreviewWindow.xaml/.cs`, `Controls/TitleBarControl.xaml.cs`, `Controls/NumericUpDownControl.xaml`, `Themes/InputStyles.RadioButton.xaml` (новый), `App.xaml`, `Services/PrintService.FlowDocument.cs`.
- **Тесты:** 788/789 pass (1 pre-existing flake `AppLifecycleTests.PrintPreviewWindow_OpensWithoutNRE_DuringInitialXamlParse` — race на AppDomain-wide `Application.Current` при параллельном xUnit, проходит в одиночку, не regression от этих правок).

---

## 3.43.0 — 2026-07-03

### Новое

- **Новый товар «Дверная сетка»:**
  - Цена 3000 ₽/м² (Белый). Доступна во вкладке «Цены», выпадающем списке «Тип» в QuickAdd и в диалоге «На завод».
  - Опция «Антикошка» (+2000 ₽/м²) — как для Anwis/На навесах/Оконной на метал. крепл. (новый товар расширил `AnticatApplicableProducts`).
  - Монтаж доступен, вычет по умолчанию **600 ₽/шт.** (вместо стандартных 500 ₽). В `OrderItem.Installation.cs` добавлены `GetDefaultInstallationDeduction(name)` / `GetDefaultInstallationSurcharge(name)` — теперь каждый товар может иметь свой дефолтный вычет, словарём `DefaultInstallationDeductions`.
  - В диалоге «На завод» Дверная сетка **выбрана по умолчанию** (производственный товар) — не входит в `FactoryTextService.notForProduction`.
  - Чертёж в КП — прямоугольник с петлями (как «На навесах»), текст «двер.сетка».
  - `prices.json`: +1 запись; `DefaultPrices` в `PriceService.cs`: +1 запись (14 товаров, 27 записей).
  - Покрыто: `OrderItemTests` (Anticat-логика для нового товара), `FactoryTextServiceTests` (auto-выбор в «На завод»), `ManualChecklistTests` (Δ в counts), `UpdateServiceTests` (stub shape), `UpdateServiceIntegrationTests` (runAfterConfirm/Cancel/NoUpdate); 748/748 → 771/771 tests pass после relocation.

### Техническое

- **Рефакторинг блока «Обновления» — append-only архитектурный инвариант:**
  - Бейдж «Новейшая» теперь привязан к свойству `UpdateItem.IsLatest` (`[JsonIgnore]`, in-memory only) вместо позиции в коллекции через `AlternationIndex`. Убран `AlternationCount="999"` из `UpdatesTabControl.xaml`, использован `DataTrigger Binding="{Binding IsLatest}"`.
  - `UpdateLog.AllNewestFirst()` явно сбрасывает `IsLatest=false` для всех записей, затем ставит `true` ровно одной (с максимальной версией). Между вызовами состояние корректно сбрасывается (тест на cache leakage).
  - `UpdateLog.ValidateLogInvariant()` — новая статическая проверка: дубликаты `Version` (`StringComparer.Ordinal`), непарсируемые версии, пустые строки. Возвращает `List<string>` (пустой = OK).
  - `MainWindowViewModel.AddNewUpdate(UpdateItem)` — runtime-добавление новой записи с атомарной сменой `IsLatest` (сначала новая, потом clear старых, потом `Insert(0, …)`) без flicker-окна.
  - **Главное архитектурное изменение:** `Resources/update-log.json` теперь можно дописывать **строго в конец** (append-only) — старые записи остаются байт-в-байт неизменными. Зафиксировано `AppendOnly_NewEntryAppendedToEnd_PreservesOldRecords` (манипулирует embedded JSON через `JsonNode` и проверяет, что JSON-текст старых записей сохраняется).
  - 759/759 tests pass.

- **Auto-update → MainWindow.AddNewUpdate + no-flicker contract:**
  - `UpdateService` оповещает UI о новых релизах через статический event `UpdateService.UpdateDetected` (Action<UpdateItem>). Fire'ится из обоих орккестраторов: `CheckInBackgroundAsync` (фоновая проверка) и `RunUpdateFlowAsync` (стартаповая/ручная, gated на `confirmed == true`).
  - **Подробная архитектура + диаграмма + тесты** в `docs/arc/CALCULATION_LOGIC.md`, раздел «Как данные Auto-update попадают на UI (AddNewUpdate flow)».
  - `UpdateService.CreateReleaseStub(string version)` — helper, обе fire-точки идут через него (DRY). Stub — осознанный placeholder: запущенный бинарник НЕ может отобразить полный changelog будущей версии (он вшит в её embedded `update-log.json`); полный changelog подтянется автоматически после установки и перезапуска.
  - `UpdateService.FireUpdateDetected` — per-subscriber isolation через `GetInvocationList()` + try/catch each. Один «плохой» subscriber не короткозамыкает доставку у остальных. Метод `internal` для unit-тестов.
  - `MainWindow.OnUpdateDetected` (добавлен в `MainWindow.Progress.cs` partial рядом с `OnUpdateProgressChanged`) делает sync `Dispatcher.Invoke(() => ViewModel.AddNewUpdate(item))`. Sync — намеренно: `AddNewUpdate` синхронный 3-step, WPF рендерит после полного вызова → нет промежуточного «0 карточек с IsLatest=true».
  - Подписка/отписка в `MainWindow` ctor и `Closed` (нет утечки памяти через static-event + per-window подписчик).
  - **No-flicker контракт зафиксирован тестами** (9 новых):
    - `AddNewUpdate_ZeroIsLatestFrame_neverObserved` — PropertyChanged-snapshotter на existing items + candidate, `count(IsLatest=true) >= 1` в каждой записанной точке.
    - `AddNewUpdate_OldItemsFire_OnlyIsLatest_PropertyChanged` — только старая «latest» карточка делает PropertyChanged, и только для `IsLatest`.
    - `AddNewUpdate_OldItems_ReferenceIdentity_Unchanged` — `ObservableCollection.Insert(0,…)` сохраняет ref-identity → WPF reuses ContentPresenter.
    - `UpdateDetected_HandlerSwallows_ExceptionsFromSubscribers` — throwing subscriber не short-circuits остальных.
    - `UpdateDetected_CreateReleaseStub_HasExpectedShape` — stub contract locked через `private const string` anchors + clinically-positive date guards.
    - `FetchManifestAsync_NewerReleaseAvailable_DoesNotFireEvent` — regression guard: только оркестраторы fire'ят.
    - `RunUpdateFlowAsync_ConfirmedDialog_FiresUpdateDetected_AndStopsOnDownloadFailure` — e2e smoke test с замоканным `DialogService.ShowUpdateAvailable` через новый `internal static Func<…>? ShowUpdateAvailableOverride` seam + опциональный `HttpClient?` DI в `RunUpdateFlowAsync`.
- **Миграция старых `prices.json`:** `ApplyMigrations` Migration 4 автоматически добавит запись «Дверная сетка» в пользовательские файлы при следующем запуске — без необходимости ручного вмешательства.
- Расчётная логика, печать КП, формулы товаров — без изменений (новый товар использует существующий m²-расчёт).

## 3.42.1 — 2026-07-02

### Исправления

- **WatchdogService: Access Denied при обновлении в Program Files:**
  - Файлы обновления (`arc-update-watchdog.bat`, `arc-update.zip`, `.exe.bak`) теперь создаются в `%AppData%\MosquitoNetCalculator\` вместо `AppContext.BaseDirectory` (Program Files).
  - Устранена ошибка `Access to the path ... is denied` при попытке записи .bat в защищённую папку.
  - Watchdog .bat получает путь к `.exe` через параметр `exeBaseDirectory`; ZIP и бэкап читает из своей папки (`%~dp0`), а новую версию копирует в `BaseDirectory`.

## 3.42.0 — 2026-07-02

### Улучшения

- **Slide-out левая панель навигации:**
  - Левая панель навигации теперь работает в режиме slide-out overlay: свёрнутое состояние — 52px с иконками, при наведении плавно расширяется до 160px с появлением текстовых подписей.
  - Анимация ширины и fade подписей, 500мс grace timer на MouseLeave.
  - Бейджи (счётчик заказов, точка обновления) видны в свёрнутом состоянии.
  - NavPanel — overlay (ZIndex=10) поверх контента, правые оверлеи перекрывают панель (ZIndex=15).
- **SidebarControl: убрано сворачивание карточек:**
  - Карточки ЗАКАЗЧИК, ДОГОВОР, ПРИМЕЧАНИЯ теперь всегда развёрнуты (без ▼/► chevron).
  - Удалены `ToggleSection` и обработчики клика по заголовку.
- **ПСУЛ и Уплотнение — упрощённый ввод:**
  - Теперь можно указать только количество (без ширины и высоты).
  - ПСУЛ: при вводе только кол-ва — цена 100 ₽/шт., расчёт = Кол-во × 100.
  - Уплотнение: при вводе только кол-ва — расчёт = Кол-во × Цена.
  - При указании размеров — расчёт по периметру, как раньше.
- **Антикошка: toggle вынесен в отдельную строку:**
  - Кнопка «Антикошка» перенесена из StackPanel поля «Тип» в отдельную строку на всю ширину секции «Товар».
  - Устранено смещение кнопки «Добавить» при появлении/скрытии toggle.
- Расчётная логика НЕ затронута.

## 3.41.0 — 2026-07-01
### РЈР»СѓС‡С€РµРЅРёСЏ

- **РџРѕР»РЅС‹Р№ UX-СЂРµС„Р°РєС‚РѕСЂРёРЅРі РёРЅС‚РµСЂС„РµР№СЃР° (v3.41.x):**
  - **ActionBar** вЂ” РєРЅРѕРїРєРё СЂРµРѕСЂРіР°РЅРёР·РѕРІР°РЅС‹ РІ 3 РІРёР·СѓР°Р»СЊРЅС‹С… РєР»Р°СЃС‚РµСЂР° СЃ СЂР°Р·РґРµР»РёС‚РµР»РµРј: Primary [РџРµС‡Р°С‚СЊ РљРџ][РЎРѕС…СЂР°РЅРёС‚СЊ] | Secondary [РќРѕРІС‹Р№ Р·Р°РєР°Р·][РќР° Р·Р°РІРѕРґ] | Tertiary [РћС‡РёСЃС‚РёС‚СЊ РІСЃС‘]. DirtyIndicator Рё RunCurrentOrderInfo РїРµСЂРµРЅРµСЃРµРЅС‹ РІ СЃС‚Р°С‚СѓСЃ-Р±Р°СЂ.
  - **TitleBar** вЂ” РґРѕР±Р°РІР»РµРЅР° РёРєРѕРЅРєР° вљ™ СЃ РІС‹РїР°РґР°СЋС‰РёРј РјРµРЅСЋ РЅР°СЃС‚СЂРѕРµРє (С‚РµРјР°, С‚РѕС‡РєР° СѓСЃС‚Р°РЅРѕРІРєРё, РїСЂРѕРІРµСЂРєР° РѕР±РЅРѕРІР»РµРЅРёР№) Рё РєСЂР°СЃРЅР°СЏ С‚РѕС‡РєР°-РёРЅРґРёРєР°С‚РѕСЂ РґРѕСЃС‚СѓРїРЅРѕРіРѕ РѕР±РЅРѕРІР»РµРЅРёСЏ.
  - **QuickAdd** вЂ” РґРѕР±Р°РІР»РµРЅС‹ РіСЂСѓРїРїРѕРІС‹Рµ РјРµС‚РєРё РїРѕР»РµР№ (РўРѕРІР°СЂ, Р Р°Р·РјРµСЂС‹, РљРѕР»-РІРѕ Рё С†РµРЅР°), РєР»Р°РІРёР°С‚СѓСЂРЅС‹Рµ С…РёРЅС‚С‹, РјРµС‚РєР° В«Р РµР¶РёРј Р·Р°РјРµСЂР°В» РїРµСЂРµРґ Anwis segmented control, СѓР»СѓС‡С€РµРЅ PreviewChip.
  - **РўР°Р±С‹** вЂ” Р±РµР№РґР¶Рё: СЃС‡С‘С‚С‡РёРє Р·Р°РєР°Р·РѕРІ РЅР° РІРєР»Р°РґРєРµ В«Р—Р°РєР°Р·С‹В», РєСЂР°СЃРЅР°СЏ С‚РѕС‡РєР° РЅР° В«РћР±РЅРѕРІР»РµРЅРёСЏВ» РїСЂРё РЅРѕРІРѕР№ РІРµСЂСЃРёРё. Р“РѕСЂСЏС‡РёРµ РєР»Р°РІРёС€Рё Ctrl+1..4 РґР»СЏ РїРµСЂРµРєР»СЋС‡РµРЅРёСЏ РІРєР»Р°РґРѕРє.
  - **Sidebar** вЂ” РєР°СЂС‚РѕС‡РєРё СЃС‚Р°Р»Рё СЃС…Р»РѕРїС‹РІР°РµРјС‹РјРё (РєР»РёРє РїРѕ Р·Р°РіРѕР»РѕРІРєСѓ СЃРІРѕСЂР°С‡РёРІР°РµС‚/СЂР°Р·РІРѕСЂР°С‡РёРІР°РµС‚ СЃ Р°РЅРёРјР°С†РёРµР№ fade, в–ј/в–є chevron). РЈРЅРёС„РёС†РёСЂРѕРІР°РЅС‹ РѕС‚СЃС‚СѓРїС‹ РїРѕР»РµР№ Рё РєР°СЂС‚РѕС‡РµРє.
  - **РЎС‚Р°С‚СѓСЃ-Р±Р°СЂ** вЂ” РєРѕРјРїР°РєС‚РЅР°СЏ СЃС‚СЂРѕРєР° СЃС‚Р°С‚СѓСЃР° РїРѕРґ TotalCard СЃ РёРЅС„Рѕ Рѕ С‚РµРєСѓС‰РµРј Р·Р°РєР°Р·Рµ Рё РёРЅРґРёРєР°С‚РѕСЂРѕРј РЅРµСЃРѕС…СЂР°РЅС‘РЅРЅС‹С… РёР·РјРµРЅРµРЅРёР№.
- **В«РћС‚Р»РёРІВ» Р±РѕР»СЊС€Рµ РЅРµ РІС‹Р±РёСЂР°РµС‚СЃСЏ Р°РІС‚РѕРјР°С‚РёС‡РµСЃРєРё РІ РґРёР°Р»РѕРіРµ В«РќР° Р·Р°РІРѕРґВ».** РћС‚Р»РёРІ вЂ” СЌС‚Рѕ РіРѕС‚РѕРІС‹Р№ РїРѕРґРѕРєРѕРЅРЅРёРє/РѕС‚Р»РёРІ (РЅРµ СЃРµС‚РєР°), Рё С‚РµРїРµСЂСЊ РїРѕР»СЊР·РѕРІР°С‚РµР»СЊ СЏРІРЅРѕ РІРєР»СЋС‡Р°РµС‚ РµРіРѕ РіР°Р»РѕС‡РєРѕР№ РїРµСЂРµРґ РѕС‚РїСЂР°РІРєРѕР№ РЅР° Р·Р°РІРѕРґ. РР·РјРµРЅС‘РЅ `notForProduction` set РІ `FactoryTextService.BuildSelectableItems` (РґРѕР±Р°РІР»РµРЅРѕ В«РћС‚Р»РёРІВ») Рё РїРѕРґС‚РІРµСЂР¶РґР°СЋС‰РёРµ С‚РµСЃС‚С‹ РІ `FactoryTextServiceTests` / `ManualChecklistTests`. Р”РѕРєСѓРјРµРЅС‚Р°С†РёСЏ РІ `TESTING_CHECKLIST.md` (В§12.2) СЃРёРЅС…СЂРѕРЅРёР·РёСЂРѕРІР°РЅР°. Р Р°РЅРµРµ В«РћС‚Р»РёРІВ» РїРѕРїР°РґР°Р» РІ РєР°С‚РµРіРѕСЂРёСЋ В«РџСЂРѕРёР·РІРѕРґСЃС‚РІРµРЅРЅС‹Рµ С‚РѕРІР°СЂС‹В» СЂСЏРґРѕРј СЃ Anwis/РљРѕР·С‹СЂСЊРєРѕРј.
- **Polishing UX-С„РёРєСЃС‹ Рє v3.41.x Deep UX Refactor (3 С‚РѕС‡РµС‡РЅС‹С… РїСЂР°РІРєРё + 1 refinement v2):**
  - **[v2] NavOrdersBadge refinement (РїРѕСЃР»Рµ feedback code-reviewer):** РїРµСЂРІР°СЏ РёС‚РµСЂР°С†РёСЏ (16в†’18, FontSize 8в†’10) РЅРµ СЂРµС€РёР»Р° В«РїР»РѕС…Рѕ РѕС‚РѕР±СЂР°Р¶Р°РµС‚СЃСЏВ» вЂ” РїРѕР»СЊР·РѕРІР°С‚РµР»СЊ РїРѕРґС‚РІРµСЂРґРёР» РїРѕРІС‚РѕСЂРµРЅРёРµ. РљРѕСЂРЅРµРІР°СЏ РїСЂРёС‡РёРЅР° С‡РµСЂРµР· thinker-Р°РЅР°Р»РёР·: Р·РЅР°С‡РѕРє Height=18 + Margin Top=-3 РїРµСЂРµРєСЂС‹РІР°Р» РІРµСЂС…РЅСЋСЋ С‡Р°СЃС‚СЊ РёРєРѕРЅРєРё Orders (FontSize=20, С†РµРЅС‚СЂРёСЂРѕРІР°РЅ РІ 40Г—40), РІРёР·СѓР°Р»СЊРЅРѕ Р·РЅР°С‡РѕРє Рё РёРєРѕРЅРєР° РєРѕРЅРєСѓСЂРёСЂРѕРІР°Р»Рё Р·Р° РІРЅРёРјР°РЅРёРµ. Р’С‚РѕСЂР°СЏ РёС‚РµСЂР°С†РёСЏ (#polishing-v2) СЂРµС€Р°РµС‚ С‡РµСЂРµР·:
    - **DropShadowEffect** РЅР° Р·РЅР°С‡РєРµ (`BlurRadius=6 ShadowDepth=0 Opacity=0.45`, `Color={DynamicResource AccentShadowColor}`) вЂ” soft glow РѕС‚РґРµР»СЏРµС‚ Р·РЅР°С‡РѕРє РѕС‚ РёРєРѕРЅРєРё, РІРёР·СѓР°Р»СЊРЅРѕ В«floating over buttonВ».
    - **Р“РµРѕРјРµС‚СЂРёСЏ:** 18Г—18 в†’ **20Г—20**, `FontSize 10в†’11`, `FontWeight Boldв†’SemiBold`, РґРѕР±Р°РІР»РµРЅС‹ `LineStackingStrategy=BlockLineHeight` + `LineHeight=14`.
    - **Margin СЃРєРѕСЂСЂРµРєС‚РёСЂРѕРІР°РЅ** СЃ `0,-3,-9,0` РЅР° **`0,-5,-6,0`** вЂ” РїСЂР°РІС‹Р№ РєСЂР°Р№ Р·РЅР°С‡РєР° С‚РµРїРµСЂСЊ x=46+6=52 СЂРѕРІРЅРѕ РїРѕ РіСЂР°РЅРёС†Рµ nav-column 52px, РЅРµ РІС‹Р»РµР·Р°РµС‚ РІ content area.
    - **РўР°Р№РјРµСЂ** `_navBadgeTimer.Interval` СЃРЅРёР¶РµРЅ СЃ `4СЃ` РґРѕ **`1.5СЃ`** вЂ” fallback РґР»СЏ out-of-band РѕРїРµСЂР°С†РёР№ (Save/Delete/Import СѓР¶Рµ РІС‹Р·С‹РІР°СЋС‚ `RefreshNavBadges()` РЅР°РїСЂСЏРјСѓСЋ).
  - **WPF restriction discovered + fixed:** `Color={DynamicResource Accent}` РЅР° `DropShadowEffect` РќР• СЂР°Р±РѕС‚Р°РµС‚ (Color вЂ” struct, РЅРµР»СЊР·СЏ implicit convert РёР· SolidColorBrush). РўСЂРµР±РѕРІР°Р»СЃСЏ РїР°СЂР°Р»Р»РµР»СЊРЅС‹Р№ `<Color>` resource.
    - **`Themes/Brushes.xaml`:** РґРѕР±Р°РІР»РµРЅ `<Color x:Key="AccentShadowColor">#005FB8</Color>` вЂ” РїР°СЂР° Рє РєРёСЃС‚Рё `Accent` РґР»СЏ Р°РЅРёРјР°С†РёРѕРЅРЅС‹С… target'РѕРІ (РїР°С‚С‚РµСЂРЅ СѓР¶Рµ РёСЃРїРѕР»СЊР·РѕРІР°Р»СЃСЏ РґР»СЏ `RowHoverColor`/`AccentLightColor`).
    - **`Services/ThemeService.cs`:** `AccentShadowColor` РґРѕР±Р°РІР»РµРЅ РІ `LightColors` (`#005FB8`) Рё `DarkColors` (`#60CDFF`) вЂ” СЃС‚Р°РЅРґР°СЂС‚РЅС‹Р№ `ApplyTheme()` С‚РµРїРµСЂСЊ РєРѕСЂСЂРµРєС‚РЅРѕ РѕР±РЅРѕРІР»СЏРµС‚ С†РІРµС‚ С‚РµРЅРё РЅР° СЃРјРµРЅРµ С‚РµРјС‹.
  - **Build/tests:** `dotnet build -c Release` вЂ” **0 errors**, 5 warnings MSB3026 (testhost DLL-lock, pre-existing). Tests: 709/711 pass; 2 pre-existing fails `UpdateLogTests.AllNewestFirst_FirstItemIsNewest` + `UpdateLogTests.GetChangesSince_LatestVersion_ReturnsEmpty` вЂ” hardcoded "3.40.3" РІ С‚РµСЃС‚Р°С… vs actual "3.40.4" РІ embedded log (drift РїРѕСЃР»Рµ v3.40.4 release, РЅРµ СЃРІСЏР·Р°РЅРѕ СЃ СЌС‚РёРјРё С„РёРєСЃР°РјРё вЂ” Р±СѓРґРµС‚ РёСЃРїСЂР°РІР»РµРЅРѕ РІ Р±Р»РёР¶Р°Р№С€РµРј update-log sync).
- **OrdersCountBadge РІ С…РµРґРµСЂРµ РѕРІРµСЂР»РµСЏ В«Р—Р°РєР°Р·С‹В» (#polishing-v3):** СѓС‚РѕС‡РЅРµРЅРёРµ РѕС‚ РІР»Р°РґРµР»СЊС†Р° вЂ” В«Р·РЅР°С‡РѕРє РѕР±С‰РµРіРѕ РєРѕР»-РІР° Р·Р°РєР°Р·РѕРІВ» РѕС‚РЅРѕСЃРёС‚СЃСЏ РќР• Рє NavOrdersBadge РЅР° РёРєРѕРЅРєРµ Orders (СЌС‚РѕС‚ С„РёРєСЃРёР»Рё РІ v1/v2), Р° Рє С‚РµРєСЃС‚Сѓ **В«вЂў N Р·Р°РєР°Р·РѕРІВ» РІ С…РµРґРµСЂРµ РѕС‚РєСЂС‹С‚РѕРіРѕ РѕРІРµСЂР»РµСЏ Р—Р°РєР°Р·С‹**. РўРµРєСѓС‰РёР№ `TextBlock` `OrdersCountText` (FontSize=11, TextMuted) РїРѕС‡С‚Рё РЅРµРІРёРґРёРј РЅР° СЃРµСЂРѕРј `HeaderBg` вЂ” В«РїР»РѕС…Рѕ РѕС‚РѕР±СЂР°Р¶Р°РµС‚СЃСЏВ» РёР·-Р·Р° РЅРёР·РєРѕРіРѕ РєРѕРЅС‚СЂР°СЃС‚Р°.
  - **`MainWindow.xaml`:** `TextBlock` РѕР±С‘СЂРЅСѓС‚ РІ `Border OrdersCountBadge` (chip-СЃС‚РёР»СЊ, РёРґРµРЅС‚РёС‡РЅРѕ `TxtOrdersCount` РІ `OrdersHistoryControl`) вЂ” `Background=ChipBg` (#EFF4FB / #1A3A54 dark), `CornerRadius=10`, `Padding=10,4`, `Margin=10,0,0,0`, `TextBlock` РІРЅСѓС‚СЂРё СЃ `Foreground=Accent` (#005FB8 / #60CDFF dark), `FontWeight=SemiBold`. РўРµРїРµСЂСЊ Р·РЅР°С‡РѕРє РІРёР·СѓР°Р»СЊРЅРѕ РІС‹РґРµР»СЏРµС‚СЃСЏ РєР°Рє tag.
  - **`MainWindow.xaml.cs RefreshNavBadges`:** РѕР±РЅРѕРІР»С‘РЅ Р±Р»РѕРє РґР»СЏ РѕРґРЅРѕРІСЂРµРјРµРЅРЅРѕРіРѕ СѓРїСЂР°РІР»РµРЅРёСЏ `OrdersCountBadge.Visibility` (Visible РїСЂРё count>0, РёРЅР°С‡Рµ Collapsed вЂ” РїСѓСЃС‚РѕР№ chip СЂСЏРґРѕРј СЃ В«Р—Р°РєР°Р·С‹В» СЌС‚Рѕ visual noise) Рё `OrdersCountText.Text` (В«вЂў N _suffix_В» СЃ РїСЂР°РІРёР»СЊРЅС‹Рј СЃРєР»РѕРЅРµРЅРёРµРј). Р›РѕРіРёРєР° СЃРєР»РѕРЅРµРЅРёСЏ СЃРѕС…СЂР°РЅРµРЅР° (11вЂ“14 вЂ” В«Р·Р°РєР°Р·РѕРІВ», 1 вЂ” В«Р·Р°РєР°Р·В», 2вЂ“4 вЂ” В«Р·Р°РєР°Р·Р°В», РёРЅР°С‡Рµ вЂ” В«Р·Р°РєР°Р·РѕРІВ»).
  - **Р Р°СЃС‡С‘С‚РЅР°СЏ Р»РѕРіРёРєР° РќР• Р·Р°С‚СЂРѕРЅСѓС‚Р°**, JSON Р±РµР· РёР·РјРµРЅРµРЅРёР№, РїРµС‡Р°С‚СЊ РљРџ Р±РµР· РёР·РјРµРЅРµРЅРёР№, UpdateService Р±РµР· РёР·РјРµРЅРµРЅРёР№.
  - **Build/tests:** `dotnet build -c Release` вЂ” 0 errors, 4 warnings MSB3026 (pre-existing). Tests: **740/742 pass**; 2 pre-existing UpdateLog failures (`AllNewestFirst_FirstItemIsNewest`, `GetChangesSince_LatestVersion_ReturnsEmpty`) вЂ” hardcoded "3.40.3" РІ С‚РµСЃС‚Р°С… vs actual "3.40.4" РІ embedded log, РЅРµ СЃРІСЏР·Р°РЅРѕ СЃ СЌС‚РёРјРё С„РёРєСЃР°РјРё.
  - **NavOrdersBadge (MainWindow.xaml):** Р±РµР№РґР¶ С‡РёСЃР»Р° Р·Р°РєР°Р·РѕРІ РЅР° РёРєРѕРЅРєРµ РЅР°РІРёРіР°С†РёРё СѓРІРµР»РёС‡РµРЅ СЃ 16Г—16 РґРѕ 18Г—18, `FontSize` 8в†’10, `CornerRadius` 8в†’9 вЂ” С†РёС„СЂР° (РІРєР»СЋС‡Р°СЏ В«99+В») С‚РµРїРµСЂСЊ С‡РёС‚Р°РµРјР°. Р¦РІРµС‚Р° `Accent` + `OnAccent` СЃРѕС…СЂР°РЅРµРЅС‹ (Р±СЂРµРЅРґРѕРІС‹Р№ СЃРёРЅРёР№, Р±РµР· СЂРµР±СЂРµРЅРґРёРЅРіР°); `Padding` (4,0в†’4,1) Рё `Margin` (-2/-8 в†’ -3/-9) СЃРєРѕСЂСЂРµРєС‚РёСЂРѕРІР°РЅС‹ РґР»СЏ С†РµРЅС‚СЂРёСЂРѕРІР°РЅРёСЏ РІРЅСѓС‚СЂРё РєРЅРѕРїРєРё РЅР°РІРёРіР°С†РёРё.
  - **Р СѓСЃСЃРєРѕРµ СЃРєР»РѕРЅРµРЅРёРµ В«Р·Р°РєР°Р·РѕРІВ» РІ С…РµРґРµСЂРµ РѕРІРµСЂР»РµСЏ (MainWindow.xaml.cs, `RefreshNavBadges`):** СЃС‚Р°СЂС‹Р№ `switch (orderCount)` СѓС‡РёС‚С‹РІР°Р» С‚РѕР»СЊРєРѕ РїРѕСЃР»РµРґРЅСЋСЋ С†РёС„СЂСѓ Р±РµР· РёСЃРєР»СЋС‡РµРЅРёСЏ 11вЂ“14. РўРµРїРµСЂСЊ Р°Р»РіРѕСЂРёС‚Рј РЅР° РїРѕСЃР»РµРґРЅРёС… **РґРІСѓС…** С†РёС„СЂР°С…: `m100 в€€ [11..14]` в†’ РІСЃРµРіРґР° В«Р·Р°РєР°Р·РѕРІВ»; РёРЅР°С‡Рµ РїРѕ `m10` (1 в†’ Р·Р°РєР°Р·, 2..4 в†’ Р·Р°РєР°Р·Р°, РѕСЃС‚Р°Р»СЊРЅРѕРµ в†’ Р·Р°РєР°Р·РѕРІ). Р‘РµР· СЌС‚РѕРіРѕ В«11 Р·Р°РєР°Р·РѕРІВ» РїРѕРєР°Р·С‹РІР°Р»РѕСЃСЊ РєР°Рє В«11 Р·Р°РєР°Р·Р°В», В«21 Р·Р°РєР°Р·В» РєР°Рє В«21 Р·Р°РєР°Р·В», В«111 Р·Р°РєР°Р·В» РєР°Рє В«111 Р·Р°РєР°Р·В», В«122 Р·Р°РєР°Р·Р°В» вЂ” С‚РµРїРµСЂСЊ СЃРѕРѕС‚РІРµС‚СЃС‚РІРµРЅРЅРѕ В«Р·Р°РєР°Р·РѕРІВ», В«Р·Р°РєР°Р·В», В«Р·Р°РєР°Р·РѕРІВ», В«Р·Р°РєР°Р·Р°В». РџСЂРѕРІРµСЂРµРЅРѕ РґР»СЏ РґРёР°РїР°Р·РѕРЅР° 1вЂ“9999.
  - **РљРЅРѕРїРєР° В«Р”РѕР±Р°РІРёС‚СЊВ» РІ QuickAdd (QuickAddControl.xaml):** `VerticalAlignment` Bottomв†’Top + `Margin="0,17,0,0"` (`Height=32` СЃРѕС…СЂР°РЅРµРЅР°). РљРѕСЂРµРЅСЊ Р±Р°РіР°: РІРЅСѓС‚СЂРё `StackPanel` РїРѕР»СЏ В«РўРёРїВ» СЃРїСЂСЏС‚Р°РЅ РґРёРЅР°РјРёС‡РµСЃРєРёР№ `ToggleButton РђРЅС‚РёРєРѕС€РєР°`; РїСЂРё РµРіРѕ РїРѕСЏРІР»РµРЅРёРё `StackPanel` СЂР°СЃС‚С‘С‚ РЅР° ~30 px в†’ `Grid.Row 1 Height="Auto"` СЂР°СЃС€РёСЂСЏРµС‚ РІСЃСЋ СЃС‚СЂРѕРєСѓ в†’ РєРЅРѕРїРєР° (Bottom-aligned) В«СЃРїРѕР»Р·Р°Р»Р°В» РІРЅРёР· РѕС‚РЅРѕСЃРёС‚РµР»СЊРЅРѕ РІРµСЂС…Р°/С†РµРЅС‚СЂР° СЃРѕСЃРµРґРЅРёС… `TextBox`/`ComboBox`. Top + СЏРІРЅС‹Р№ `margin 17 px` (= РІС‹СЃРѕС‚Р° `FieldLabel`) РїСЂРёР¶РёРјР°РµС‚ РІРµСЂС… РєРЅРѕРїРєРё Рє РЅРёР·Сѓ РјРµС‚РєРё в†’ bottoms РєРЅРѕРїРєРё Рё СЃРѕСЃРµРґРЅРёС… РєРѕРЅС‚СЂРѕР»РѕРІ РІСЃРµРіРґР° СЃРѕРІРїР°РґР°СЋС‚, РЅРµР·Р°РІРёСЃРёРјРѕ РѕС‚ РґРёРЅР°РјРёРєРё СЃС‚СЂРѕРєРё.
  - **Р Р°СЃС‡С‘С‚РЅР°СЏ Р»РѕРіРёРєР° РќР• Р·Р°С‚СЂРѕРЅСѓС‚Р°:** С„РѕСЂРјСѓР»С‹ Anwis/РїР»РѕС‰Р°РґСЊ/РїРµСЂРёРјРµС‚СЂ/РјРѕРЅС‚Р°Р¶, JSON-СЃС…РµРјР° `OrderItemData`, РїРµС‡Р°С‚СЊ РљРџ, UpdateService вЂ” Р±РµР· РёР·РјРµРЅРµРЅРёР№.
  - **Build/tests:** `dotnet build MosquitoNetCalculator.sln -c Release` вЂ” 0 errors (warnings MSB3026 вЂ” testhost DLL-lock, РЅРµ СЃРІСЏР·Р°РЅРѕ). Tests: 710/711 pass РїРѕСЃР»Рµ С„РёРєСЃР°; 1 РїСЂРµРґСЃСѓС‰РµСЃС‚РІСѓСЋС‰РёР№ fail `AppLifecycleTests.Print_Template_Has_No_Slash_Dogovor` (С‚РµСЃС‚ РёС‰РµС‚ РїСѓС‚СЊ Рє `PrintService.HtmlBuilder.cs` вЂ” Р»РѕРјР°Р»СЃСЏ РµС‰С‘ РґРѕ v3.41.x, РѕС‚РјРµС‡РµРЅ РІ release-notes РґР»СЏ СЃР»РµРґСѓСЋС‰РµРіРѕ minor).
- **Font-family fallback РґР»СЏ РёРєРѕРЅРѕРє РЅР° Windows 10 + С„РёРєСЃ В«РєСЂСЏРєР°Р·СЏР±СЂВ» РІ NavOrdersBadge РЅР° Windows 11:**
  - **`MainWindow.xaml`:** `NavButton` style Рё `OverlayCloseButton` style вЂ” `FontFamily` РёР·РјРµРЅС‘РЅ СЃ `Segoe Fluent Icons` РЅР° `Segoe Fluent Icons, Segoe MDL2 Assets` (fallback-С€СЂРёС„С‚ РёР· Windows 10). Inline `FontFamily` Сѓ `NavIconCalc`/`NavIconOrders`/`NavIconPrices`/`NavIconUpdates` СѓР±СЂР°РЅС‹ вЂ” С‚РµРїРµСЂСЊ РЅР°СЃР»РµРґСѓСЋС‚ РѕС‚ СЃС‚РёР»СЏ РєРЅРѕРїРєРё.
  - **`NavOrdersBadgeText` (MainWindow.xaml):** РґРѕР±Р°РІР»РµРЅ СЏРІРЅС‹Р№ `FontFamily="Segoe UI"` вЂ” СЂР°РЅРµРµ РЅР°СЃР»РµРґРѕРІР°Р» РёРєРѕРЅРѕС‡РЅС‹Р№ С€СЂРёС„С‚ РѕС‚ `NavButton`, С‡С‚Рѕ РЅР° Windows 11 (РіРґРµ `Segoe Fluent Icons` СѓСЃС‚Р°РЅРѕРІР»РµРЅ) РїСЂРёРІРѕРґРёР»Рѕ Рє Р°СЂС‚РµС„Р°РєС‚Р°Рј СЂРµРЅРґРµСЂРёРЅРіР° С†РёС„СЂ (В«РєСЂСЏРєР°Р·СЏР±СЂРёВ»). РќР° Windows 10 Р±РµР№РґР¶ СЂР°Р±РѕС‚Р°Р», РїРѕС‚РѕРјСѓ С‡С‚Рѕ РѕС‚СЃСѓС‚СЃС‚РІСѓСЋС‰РёР№ С€СЂРёС„С‚ fallback'РёР»СЃСЏ РЅР° СЃРёСЃС‚РµРјРЅС‹Р№, РЅРѕ СЃР°РјРё Р·РЅР°С‡РєРё РЅРµ РѕС‚РѕР±СЂР°Р¶Р°Р»РёСЃСЊ.
  - **`TitleBarControl.xaml`, `ImportDialogWindow.xaml`, `SendToFactoryWindow.xaml`:** РІСЃРµ inline `FontFamily="Segoe Fluent Icons"` Р·Р°РјРµРЅРµРЅС‹ РЅР° `FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets"` РґР»СЏ РєРѕРЅСЃРёСЃС‚РµРЅС‚РЅРѕСЃС‚Рё.
  - **Build/tests:** `dotnet build MosquitoNetCalculator.sln -c Release` вЂ” 0 errors. Tests: **742/742 pass** (2 РїСЂРµРґСЃСѓС‰РµСЃС‚РІСѓСЋС‰РёС… fail `UpdateLogTests` СЃ hardcoded "3.40.3" РёСЃРїСЂР°РІР»РµРЅС‹ РЅР° "3.40.4").

### РќРѕРІРѕРµ

- **РџРѕР»РѕС‚РЅРѕ В«РђРЅС‚РёРєРѕС€РєР°В»** вЂ” РґР»СЏ С‚СЂС‘С… С‚РёРїРѕРІ СЃРµС‚РѕРє (Anwis, РќР° РЅР°РІРµСЃР°С…, РћРєРѕРЅРЅР°СЏ РЅР° РјРµС‚Р°Р»Р» РєСЂРµРїР».) РґРѕР±Р°РІР»РµРЅР° РѕРїС†РёСЏ В«РђРЅС‚РёРєРѕС€РєР° (+2000 СЂСѓР±/РјВІ)В» РІ РїР°РЅРµР»СЊ QuickAdd.
  - Р§РµРєР±РѕРєСЃ РІРёРґРµРЅ С‚РѕР»СЊРєРѕ РґР»СЏ РїСЂРёРјРµРЅРёРјС‹С… С‚РѕРІР°СЂРѕРІ.
  - РљР°С‚Р°Р»РѕРіРѕРІР°СЏ С†РµРЅР° Р°РІС‚РѕРјР°С‚РёС‡РµСЃРєРё СѓРІРµР»РёС‡РёРІР°РµС‚СЃСЏ РЅР° 2000 в‚Ѕ/РјВІ РїСЂРё РІРєР»СЋС‡С‘РЅРЅРѕР№ РіР°Р»РѕС‡РєРµ.
  - Р’ С‚Р°Р±Р»РёС†Рµ СЂР°СЃС‡С‘С‚Р°, РїРµС‡Р°С‚РЅРѕРј РљРџ Рё С‚РµРєСЃС‚Рµ В«РќР° Р·Р°РІРѕРґВ» РЅР°Р·РІР°РЅРёРµ С‚РѕРІР°СЂР° РѕС‚РѕР±СЂР°Р¶Р°РµС‚СЃСЏ СЃ СЃСѓС„С„РёРєСЃРѕРј В«(РђРЅС‚РёРєРѕС€РєР°)В».
  - Р¤Р»Р°Рі `IsAnticat` СЃРѕС…СЂР°РЅСЏРµС‚СЃСЏ РІ JSON-Р·Р°РєР°Р·Рµ Рё РїРµСЂРµР¶РёРІР°РµС‚ undo/redo С‡РµСЂРµР· `Clone()`.

### РЈР»СѓС‡С€РµРЅРёСЏ

- **Р РµРґРёР·Р°Р№РЅ РєРЅРѕРїРєРё В«РђРЅС‚РёРєРѕС€РєР°В»:** ToggleButton РІ QuickAdd РїРµСЂРµРІРµРґС‘РЅ РЅР° СЃС‚РёР»СЊ `GhostButton` (РїР°С‚С‚РµСЂРЅ РёР· `Themes/ButtonStyles.xaml`) вЂ” РЅРµР°РєС‚РёРІРЅРѕРµ СЃРѕСЃС‚РѕСЏРЅРёРµ С‚РµРїРµСЂСЊ РёРјРµРµС‚ СЏРІРЅС‹Р№ С„РѕРЅ `Surface` Рё СЂР°РјРєСѓ `Border`, С‡С‚Рѕ СѓСЃС‚СЂР°РЅСЏРµС‚ СЃР»РёСЏРЅРёРµ СЃ С„РѕРЅРѕРј `QuickBg`. РђРєС‚РёРІРЅРѕРµ СЃРѕСЃС‚РѕСЏРЅРёРµ Рё С…РѕРІРµСЂ РѕСЃС‚Р°СЋС‚СЃСЏ Р±РµР· РёР·РјРµРЅРµРЅРёР№.

### РўРµС…РЅРёС‡РµСЃРєРѕРµ

- **Р”РµРєРѕРјРїРѕР·РёС†РёСЏ РјРѕРЅРѕР»РёС‚РЅС‹С… С„Р°Р№Р»РѕРІ (Phase 1 + Phase 2):**
  - `MainWindow.xaml.cs` СЂР°Р·Р±РёС‚ РЅР° 6 partial-С„Р°Р№Р»РѕРІ: `MainWindow.Animations.cs`, `MainWindow.Progress.cs`, `MainWindow.GridColumns.cs`, `MainWindow.Pricing.cs`, `MainWindow.Totals.cs`, `MainWindow.Contracts.cs`
  - `PrintService.cs` СЂР°Р·Р±РёС‚ РЅР° 3 partial-С„Р°Р№Р»Р°: `PrintService.Template.cs`, `PrintService.HtmlBuilder.cs`, `PrintService.SvgDrawer.cs`
  - `QuickAddControl.xaml.cs` СЂР°Р·Р±РёС‚ РЅР° 4 partial-С„Р°Р№Р»Р°: `QuickAddControl.AddItem.cs`, `QuickAddControl.Preview.cs`, `QuickAddControl.Search.cs`, `QuickAddControl.AnwisMode.cs`
  - РџСѓР±Р»РёС‡РЅС‹Р№ API РЅРµ РёР·РјРµРЅС‘РЅ; РІСЃРµ 711 С‚РµСЃС‚РѕРІ РїСЂРѕС…РѕРґСЏС‚.
  - `AppLifecycleTests.OnThemeChanged_Does_Not_Manually_Rebind_TitleBar_Background` Р°РґР°РїС‚РёСЂРѕРІР°РЅ РїРѕРґ partial-РєР»Р°СЃСЃС‹ (РїРѕРёСЃРє РїРѕ РІСЃРµРј `MainWindow*.cs`).
  - 742/742 С‚РµСЃС‚РѕРІ РїСЂРѕС…РѕРґСЏС‚.

### РўРµС…РЅРёС‡РµСЃРєРѕРµ

- **release.yml**: РїРµСЂРµСЂР°Р±РѕС‚Р°РЅ С€Р°Рі Update releases.json вЂ” С‚РµРїРµСЂСЊ РѕРЅ РѕР±РЅРѕРІР»СЏРµС‚ url/size/sha256/date РІ СЃСѓС‰РµСЃС‚РІСѓСЋС‰РµР№ Р·Р°РїРёСЃРё РІРµСЂСЃРёРё (РЅРµ РїРµСЂРµР·Р°РїРёСЃС‹РІР°РµС‚ type/title/changes РїР»РµР№СЃС…РѕР»РґРµСЂРѕРј). РР·РјРµРЅС‘РЅ РїРѕСЂСЏРґРѕРє С€Р°РіРѕРІ: GitHub Release СЃРѕР·РґР°С‘С‚СЃСЏ Р”Рћ РєРѕРјРјРёС‚Р° releases.json РІ main (СЃРѕР±Р»СЋРґРµРЅРёРµ safety rule РёР· RELEASE_PROCESS.md: РїРѕР»СЊР·РѕРІР°С‚РµР»Рё РЅРµ РґРѕР»Р¶РЅС‹ РІРёРґРµС‚СЊ РѕР±РЅРѕРІР»РµРЅРёРµ, РєРѕС‚РѕСЂРѕРµ РЅРµР»СЊР·СЏ СЃРєР°С‡Р°С‚СЊ).

## 3.40.3 вЂ” 2026-06-29

### РўРµС…РЅРёС‡РµСЃРєРѕРµ

- **`MainWindow.OnUpdateProgressChanged` РёР·РІР»РµС‡С‘РЅ РІ РѕС‚РґРµР»СЊРЅС‹Р№ С‚РµСЃС‚РёСЂСѓРµРјС‹Р№ С…РµР»РїРµСЂ `Helpers/ProgressBarUpdateAnimator.cs`:** С‚РµР»Рѕ РјРµС‚РѕРґР° СЃС‚Р°Р»Рѕ РѕРґРЅРѕСЃС‚СЂРѕС‡РЅС‹Рј РґРµР»РµРіР°С‚РѕРј `_progressAnimator?.Animate(...)`. РҐРµР»РїРµСЂ РёРЅРєР°РїСЃСѓР»РёСЂСѓРµС‚ РІСЃСЋ Р»РѕРіРёРєСѓ `TryFindResource` + Storyboard fade (fade-in 200 РјСЃ / fade-out 250 РјСЃ) + belt-and-suspenders `try/catch` СЃ СЏРІРЅС‹Рј re-throw РґР»СЏ `OutOfMemoryException` / `StackOverflowException`. РћС‚РґРµР»С‘РЅ РѕС‚ `UpdateService` С‡РµСЂРµР· `Func<double>` (С‚РµРєСѓС‰РёР№ РїСЂРѕРіСЂРµСЃСЃ) Рё `Func<bool>` (РёРґС‘С‚ Р»Рё СЃРєР°С‡РёРІР°РЅРёРµ) вЂ” СЌС‚Рѕ РїРѕР·РІРѕР»РёР»Рѕ РїРѕРєСЂС‹С‚СЊ РєРѕРЅС‚СЂР°РєС‚ В«UI РїСЂРѕРіСЂРµСЃСЃ-Р±Р°СЂ РЅРµ СЂРѕРЅСЏРµС‚ auto-update flowВ» СЋРЅРёС‚-С‚РµСЃС‚Р°РјРё РІРїРµСЂРІС‹Рµ Р·Р° РІСЃСЋ РёСЃС‚РѕСЂРёСЋ РїСЂРѕРµРєС‚Р°.
- **`UpdateService.IsCurrentVersionBrokenForAutoUpdate(Version?)`** (internal): РїСЂРѕРІРµСЂРєР° half-open interval `[3.40.0, 3.40.2)` С‡РµСЂРµР· РїСЂСЏРјСѓСЋ int-СЃСЂР°РІРЅРєСѓ `Major`/`Minor`/`Build` (РёР·Р±РµРіР°РµС‚ footgun `Version(3,40,1,0) > Version(3,40,1)` РёР·-Р·Р° `Revision 0 > -1`). Р Р°РЅРµРµ С‚Р° Р¶Рµ РїСЂРѕРІРµСЂРєР° РґСѓР±Р»РёСЂРѕРІР°Р»Р°СЃСЊ inline-Р»РёС‚РµСЂР°Р»Р°РјРё РІ `MainWindow_Loaded`.
- **30 РЅРѕРІС‹С… СЋРЅРёС‚-С‚РµСЃС‚РѕРІ** (711/711, Р±С‹Р»Рѕ 681):
  - `UpdateServiceTests.cs`: 14 inline-РєРµР№СЃРѕРІ РЅР° boundary РґРёР°РїР°Р·РѕРЅР° (3-part Рё 4-part РІРµСЂСЃРёРё, before/after/different major) + null-arg + smoke test РЅР° `CurrentVersion`.
  - `ProgressBarUpdateAnimatorTests.cs`: 10 С‚РµСЃС‚РѕРІ вЂ” no-throw РєРѕРЅС‚СЂР°РєС‚ РґР»СЏ РЅРµ-fatal РёСЃРєР»СЋС‡РµРЅРёР№ (`InvalidOperationException`, `InvalidCastException`, `ArgumentException`, `FormatException` С‡РµСЂРµР· `[Theory]`), СЏРІРЅС‹Р№ `Assert.Throws` РґР»СЏ OOM/SOF, fallback Visibility/Opacity path РїСЂРё РѕС‚СЃСѓС‚СЃС‚РІРёРё Storyboard, no-op early-return, РєРѕРЅСЃС‚СЂСѓРєС‚РѕСЂСЃРєРёРµ null-РїСЂРѕРІРµСЂРєРё.
- **xUnit `[Collection("STA")]` СЃ `DisableParallelization`:** РёР·РѕР»РёСЂСѓРµС‚ STA-С‚РµСЃС‚С‹ РѕС‚ РїР°СЂР°Р»Р»РµР»СЊРЅРѕРіРѕ РёСЃРїРѕР»РЅРµРЅРёСЏ, С‡С‚РѕР±С‹ РёР·Р±РµР¶Р°С‚СЊ РіРѕРЅРєРё РЅР° AppDomain-wide `Application.Current`. РЎР°РјРё С‚РµСЃС‚С‹ РќР• СЃРѕР·РґР°СЋС‚ `Application` вЂ” `FrameworkElement.TryFindResource` РєРѕСЂСЂРµРєС‚РЅРѕ РѕР±СЂР°Р±Р°С‚С‹РІР°РµС‚ `Application.Current == null`, С‡С‚Рѕ РґР°С‘С‚ С€С‚Р°С‚РЅС‹Р№ fallback path РґР»СЏ С‚РµСЃС‚РёСЂРѕРІР°РЅРёСЏ Рё СѓСЃС‚СЂР°РЅСЏРµС‚ РєРѕРЅС„Р»РёРєС‚ СЃ `AppLifecycleTests`.

### Р—Р°РјРµС‚РєРё

- РџРѕР»РЅРѕСЃС‚СЊСЋ РѕР±СЂР°С‚РЅРѕ-СЃРѕРІРјРµСЃС‚РёРјРѕ СЃ v3.40.0 в†’ v3.40.2 (РЅРёРєР°РєРёС… СЂРµРіСЂРµСЃСЃРёР№). Production-РїРѕРІРµРґРµРЅРёРµ `OnUpdateProgressChanged` РёРґРµРЅС‚РёС‡РЅРѕ v3.40.2.
- v3.40.3 вЂ” РїРµСЂРІС‹Р№ СЂРµР»РёР· РїСЂРѕРµРєС‚Р°, РіРґРµ РєРѕРЅС‚СЂР°РєС‚ В«UI РѕС€РёР±РєР° РЅРµ СЂРѕРЅСЏРµС‚ auto-updateВ» РёРјРµРµС‚ **testable regression coverage** (Р° РЅРµ С‚РѕР»СЊРєРѕ РІ РєРѕРјРјРµРЅС‚Р°СЂРёСЏС…).
- РџРѕР»СЊР·РѕРІР°С‚РµР»СЏРј v3.40.0 / v3.40.1 РїРµСЂРµС…РѕРґ РЅР° v3.40.3 Р±РµР·РѕРїР°СЃРµРЅ, РЅРѕ РЅРµ РѕР±СЏР·Р°С‚РµР»РµРЅ вЂ” РІРёРґРёРјРѕРµ РїРѕРІРµРґРµРЅРёРµ РЅРµ РјРµРЅСЏР»РѕСЃСЊ. Р РµРєРѕРјРµРЅРґСѓРµС‚СЃСЏ РІСЃРµРј, РєС‚Рѕ С…РѕС‡РµС‚ unit-test РіР°СЂР°РЅС‚РёРё РѕС‚ СЂРµРіСЂРµСЃСЃРёР№ СЌС‚РѕРіРѕ С‚РёРїР°.

---

## 3.40.2 вЂ” 2026-06-29

### РСЃРїСЂР°РІР»РµРЅРёСЏ

- **Belt-and-suspenders try/catch РІ `MainWindow.OnUpdateProgressChanged`:** С‚РµР»Рѕ РјРµС‚РѕРґР° РѕР±С‘СЂРЅСѓС‚Рѕ РІ `try { ... } catch (Exception ex) { ... }`. РџСЂРё Р›Р®Р‘РћРњ РёСЃРєР»СЋС‡РµРЅРёРё РІРЅСѓС‚СЂРё (РІРєР»СЋС‡Р°СЏ Р±СѓРґСѓС‰РёРµ BAML-mismatch, XAML-refactor, third-party change) РјРµС‚РѕРґ Р»РѕРіРёСЂСѓРµС‚ РІ Debug, СѓСЃС‚Р°РЅР°РІР»РёРІР°РµС‚ Visibility РЅР°РїСЂСЏРјСѓСЋ Р±РµР· Р°РЅРёРјР°С†РёРё, Рё **РЅРµ РїСЂРѕР±СЂР°СЃС‹РІР°РµС‚ РёСЃРєР»СЋС‡РµРЅРёРµ РІС‹С€Рµ**. Р­С‚Рѕ Р·Р°С‰РёС‚Р° РѕС‚ РїРѕРІС‚РѕСЂРµРЅРёСЏ v3.40.0-style Р±Р°РіР°, РєРѕРіРґР° РѕС€РёР±РєР° РІ UI РїСЂРѕРіСЂРµСЃСЃ-Р±Р°СЂР° СѓР±РёРІР°Р»Р° Dispatcher.Invoke в†’ MessageBox в†’ СЂР°Р·СЂС‹РІ auto-update-flow в†’ РЅРµРІРѕР·РјРѕР¶РЅРѕСЃС‚СЊ РїРѕР»СѓС‡РёС‚СЊ РЅРёРєР°РєРѕР№ СЃР»РµРґСѓСЋС‰РёР№ СЂРµР»РёР·.

### РџСЂРѕС‡РµРµ

- **РЎС‚Р°СЂС‚РѕРІС‹Р№ Р±Р°РЅРЅРµСЂ РґР»СЏ РёР·РІРµСЃС‚РЅС‹С… СЃР»РѕРјР°РЅРЅС‹С… РІРµСЂСЃРёР№:** РІ `MainWindow.MainWindow_Loaded` РґРѕР±Р°РІР»РµРЅР° РїСЂРѕРІРµСЂРєР° `CurrentVersion >= 3.40.0 && CurrentVersion < 3.40.2`. Р•СЃР»Рё СѓСЃР»РѕРІРёРµ РІС‹РїРѕР»РЅРµРЅРѕ, РїСЂРё Р·Р°РїСѓСЃРєРµ РїРѕРєР°Р·С‹РІР°РµС‚СЃСЏ toast-РїСЂРµРґСѓРїСЂРµР¶РґРµРЅРёРµ СЃРѕ СЃСЃС‹Р»РєРѕР№ РЅР° GitHub Releases (РґР»РёС‚РµР»СЊРЅРѕСЃС‚СЊ 15 СЃРµРє, С‚РёРї Warning). Р­С‚Рѕ no-op РґР»СЏ РЅРѕСЂРјР°Р»СЊРЅС‹С… РІРµСЂСЃРёР№ вЂ” РЅРѕ РµСЃР»Рё РІ Р±СѓРґСѓС‰РµРј СЃР»СѓС‡РёС‚СЃСЏ Р°РЅР°Р»РѕРіРёС‡РЅС‹Р№ Р±Р°Рі, РґР°РЅРЅС‹Р№ РјРµС…Р°РЅРёР·Рј Р°РІС‚РѕРјР°С‚РёС‡РµСЃРєРё РїСЂРµРґСѓРїСЂРµРґРёС‚ РїРѕР»СЊР·РѕРІР°С‚РµР»СЏ СЃ РёРЅСЃС‚СЂСѓРєС†РёРµР№.

### Р—Р°РјРµС‚РєРё

- v3.40.0 Р±РёРЅР°СЂРЅРёРєРё, СѓСЃС‚Р°РЅРѕРІР»РµРЅРЅС‹Рµ РєРѕРЅРµС‡РЅС‹РјРё РїРѕР»СЊР·РѕРІР°С‚РµР»СЏРјРё, **РґРѕ СЃРёС… РїРѕСЂ РЅРµ РјРѕРіСѓС‚ РѕР±РЅРѕРІРёС‚СЊСЃСЏ Р°РІС‚РѕРјР°С‚РёС‡РµСЃРєРё** вЂ” Р±Р°Рі СЂРѕРЅСЏР» auto-update flow, Рё РїР°С‚С‡ РґР»СЏ РЅРёС… РґРѕСЃС‚СѓРїРµРЅ С‚РѕР»СЊРєРѕ С‡РµСЂРµР· СЂСѓС‡РЅСѓСЋ Р·Р°РјРµРЅСѓ EXE РЅР° v3.40.1 РёР»Рё v3.40.2 (`https://github.com/DdepRest/arc-frame/releases/download/v3.40.2/ARC-Frame-3.40.2-full.zip`).

---

## 3.40.1 вЂ” 2026-06-29

### РСЃРїСЂР°РІР»РµРЅРёСЏ

- **Р СѓС‡РЅР°СЏ РїСЂРѕРІРµСЂРєР° РѕР±РЅРѕРІР»РµРЅРёР№ РЅРµ РїР°РґР°Р»Р° СЃ `ResourceReferenceKeyNotFoundException`:** Storyboards `UpdateBarFadeIn` / `UpdateBarFadeOut` РґР»СЏ РїСЂРѕРіСЂРµСЃСЃ-Р±Р°СЂР° РѕРїСЂРµРґРµР»РµРЅС‹ РІ `Grid.Resources` (РІРЅСѓС‚СЂРё РєРѕСЂРЅРµРІРѕРіРѕ `<Grid>`), Р° `MainWindow.OnUpdateProgressChanged` РґС‘СЂРіР°Р» `this.FindResource(...)`. WPF-РјРµС‚РѕРґ `FindResource` С…РѕРґРёС‚ Р’Р’Р•Р РҐ РїРѕ Р»РѕРіРёС‡РµСЃРєРѕРјСѓ РґРµСЂРµРІСѓ вЂ” СЂРµСЃСѓСЂСЃС‹ РїРѕС‚РѕРјРєРѕРІ РЅРµРІРёРґРёРјС‹. Storyboards РїРµСЂРµРЅРµСЃРµРЅС‹ РІ `<Window.Resources>` вЂ” С‚РµРїРµСЂСЊ `FindResource("UpdateBarFadeIn")` РЅР°С…РѕРґРёС‚ РёС….
- **Defense-in-depth РІ `OnUpdateProgressChanged`:** Р·Р°РјРµРЅС‘РЅ `FindResource(...).Clone()` (Р±СЂРѕСЃР°РµС‚ `InvalidCastException` РµСЃР»Рё СЂРµСЃСѓСЂСЃ РЅРµ РЅР°Р№РґРµРЅ) РЅР° `TryFindResource(...) is Storyboard` вЂ” РµСЃР»Рё СЂРµСЃСѓСЂСЃ РєРѕРіРґР°-С‚Рѕ В«РїРѕС‚РµСЂСЏРµС‚СЃСЏВ» (XAML-СѓРґР°Р»РµРЅРёРµ, merge-РєРѕРЅС„Р»РёРєС‚), С‚РµРїРµСЂСЊ РІРёРґРёРј debug-Р»РѕРі Рё Р±Р°СЂ РїРѕРєР°Р·С‹РІР°РµС‚СЃСЏ/СЃРєСЂС‹РІР°РµС‚СЃСЏ Р±РµР· Р°РЅРёРјР°С†РёРё, РІРјРµСЃС‚Рѕ РєСЂР°С€Р°.

### Р—Р°РјРµС‚РєРё

- Р‘Р°Рі СЃСѓС‰РµСЃС‚РІРѕРІР°Р» СЃ v3.38.0 (РєРѕРіРґР° Р±С‹Р» РґРѕР±Р°РІР»РµРЅ XAML-Р°РЅРёРјР°С†РёСЏ UpdateDownloadBar); v3.40.0 РµРіРѕ С‚РѕР»СЊРєРѕ В«Р°РєС‚РёРІРёСЂРѕРІР°Р»В», РїРѕС‚РѕРјСѓ С‡С‚Рѕ РїСѓР±Р»РёС‡РЅС‹Р№ РјРµС‚РѕРґ СЂСѓС‡РЅРѕР№ РїСЂРѕРІРµСЂРєРё `CheckAndApplyAsync` С‚РµРїРµСЂСЊ РєРѕСЂСЂРµРєС‚РЅРѕ РїСЂРѕРєРёРґС‹РІР°Р» `isAutomatic` РІ РґРёР°Р»РѕРі вЂ” РїРѕР»СЊР·РѕРІР°С‚РµР»Рё СѓРІРёРґРµР»Рё РїСѓС‚СЊ РґРѕ РєСЂР°С€Р°.
- Pubished as patch release (3.40.0 в†’ 3.40.1), РЅРµ minor вЂ” РїРѕРІРµРґРµРЅРёРµ РІ РѕСЃС‚Р°Р»СЊРЅРѕРј РЅРµ РјРµРЅСЏР»РѕСЃСЊ.

---

## 3.40.0 вЂ” 2026-06-29

### РЈР»СѓС‡С€РµРЅРёСЏ

- **WinAPI idle detection:** `UpdateCheckScheduler` С‚РµРїРµСЂСЊ РёСЃРїРѕР»СЊР·СѓРµС‚ WinAPI `GetLastInputInfo` РґР»СЏ РѕРїСЂРµРґРµР»РµРЅРёСЏ СЃРёСЃС‚РµРјРЅРѕРіРѕ РїСЂРѕСЃС‚РѕСЏ РІРјРµСЃС‚Рѕ UI-СЃРѕР±С‹С‚РёР№ (`PreviewMouseMove`/`PreviewKeyDown`). Р­С‚Рѕ РґР°С‘С‚ Р±РѕР»РµРµ С‚РѕС‡РЅРѕРµ РёР·РјРµСЂРµРЅРёРµ СЂРµР°Р»СЊРЅРѕР№ РЅРµР°РєС‚РёРІРЅРѕСЃС‚Рё РїРѕР»СЊР·РѕРІР°С‚РµР»СЏ (РЅРµ С‚РѕР»СЊРєРѕ РІ РѕРєРЅРµ РїСЂРёР»РѕР¶РµРЅРёСЏ, РЅРѕ Рё СЃРёСЃС‚РµРјРЅРѕ) Рё СѓСЃС‚СЂР°РЅСЏРµС‚ РЅРµРѕР±С…РѕРґРёРјРѕСЃС‚СЊ РІ `NotifyActivity()`.
- **Anti-recommend text РІ РґРёР°Р»РѕРіРµ РѕР±РЅРѕРІР»РµРЅРёСЏ:** РџСЂРё Р°РІС‚РѕРјР°С‚РёС‡РµСЃРєРѕРј РѕР±РЅР°СЂСѓР¶РµРЅРёРё РѕР±РЅРѕРІР»РµРЅРёСЏ (startup-check) РєРЅРѕРїРєР° В«РћС‚РјРµРЅР°В» РїРµСЂРµРёРјРµРЅРѕРІР°РЅР° РІ В«РћС‚Р»РѕР¶РёС‚СЊВ» СЃ С‚РµРєСЃС‚РѕРј В«РќРµ СЂРµРєРѕРјРµРЅРґСѓРµС‚СЃСЏ РѕС‚РєР»Р°РґС‹РІР°С‚СЊ РѕР±РЅРѕРІР»РµРЅРёРµ РЅР°РґРѕР»РіРѕВ».
- **Minimized window guard:** Р¤РѕРЅРѕРІР°СЏ РїСЂРѕРІРµСЂРєР° `CheckInBackgroundAsync` С‚РµРїРµСЂСЊ РїСЂРѕРїСѓСЃРєР°РµС‚ РїРѕРєР°Р· toast-СѓРІРµРґРѕРјР»РµРЅРёСЏ, РµСЃР»Рё РіР»Р°РІРЅРѕРµ РѕРєРЅРѕ СЃРІС‘СЂРЅСѓС‚Рѕ вЂ” СѓРІРµРґРѕРјР»РµРЅРёРµ РїРѕСЏРІРёС‚СЃСЏ РїРѕСЃР»Рµ РІРѕСЃСЃС‚Р°РЅРѕРІР»РµРЅРёСЏ РѕРєРЅР°.

### РўРµС…РЅРёС‡РµСЃРєРѕРµ

- `UpdateCheckScheduler`: СѓРґР°Р»С‘РЅ `NotifyActivity()` Рё `_lastActivityTime`; РґРѕР±Р°РІР»РµРЅ `GetSystemIdleTime` callback (С‚РёРї `Func<TimeSpan>`). РўРµСЃС‚С‹ РїРµСЂРµРїРёСЃР°РЅС‹ РЅР° С„РµР№РєРѕРІС‹Р№ `FakeIdle` РІРјРµСЃС‚Рѕ `NotifyActivity`.
- `UpdateService`: РґРѕР±Р°РІР»РµРЅ P/Invoke `GetLastInputInfo` Рё РїСѓР±Р»РёС‡РЅС‹Р№ `GetIdleTime()`; СѓР±СЂР°РЅР° РІСЃСЏ Р»РѕРіРёРєР° `IsCellEditing`, `CanShowUpdateDialog`, retry-С‚Р°Р№РјРµСЂРѕРІ Рё `OnWindowStateChanged` вЂ” СЃР»РѕР¶РЅРѕСЃС‚СЊ РЅРµ РЅСѓР¶РЅР° РїСЂРё toast-based UX.
- `MainWindow.xaml.cs`: СѓРґР°Р»РµРЅС‹ `PreviewMouseMove` Рё `PreviewKeyDown` РІС‹Р·РѕРІС‹ `_updateCheckScheduler?.NotifyActivity()`.
- `DialogService.ShowUpdateAvailable`: РґРѕР±Р°РІР»РµРЅ РїР°СЂР°РјРµС‚СЂ `isAutomatic` СЃ anti-recommend UI.
- **16 С‚РµСЃС‚РѕРІ** РІ `UpdateCheckSchedulerTests.cs` РѕР±РЅРѕРІР»РµРЅС‹/РїРµСЂРµРїРёСЃР°РЅС‹ РїРѕРґ РЅРѕРІСѓСЋ РјРѕРґРµР»СЊ idle-РґРµС‚РµРєС†РёРё.

---

## 3.39.0 вЂ” 2026-06-29

### РќРѕРІРёРЅРєР°

- **Background auto-update checks** (Variant A РёР· СЃРїРµРєРё `update-notification-rework-spec.md`):
  - Periodic checks РєР°Р¶РґС‹Рµ 30 РјРёРЅСѓС‚ РѕС‚ РїРѕСЃР»РµРґРЅРµР№ РїСЂРѕРІРµСЂРєРё.
  - Idle checks РїРѕСЃР»Рµ 10 РјРёРЅСѓС‚ РїСЂРѕСЃС‚РѕСЏ РїРѕР»СЊР·РѕРІР°С‚РµР»СЏ.
  - Anti-spam throttle: РјРёРЅРёРјСѓРј 2 РјРёРЅСѓС‚С‹ РјРµР¶РґСѓ РґРІСѓРјСЏ СЂРµР°Р»СЊРЅС‹РјРё РїСЂРѕРІРµСЂРєР°РјРё.
  - Р РµР°Р»РёР·РѕРІР°РЅРѕ РІ РЅРѕРІРѕРј `Services/UpdateCheckScheduler.cs` СЃ pure-Р»РѕРіРёРєРѕР№ `ShouldCheckAt(...)` РґР»СЏ unit-С‚РµСЃС‚РѕРІ.
- **Persistent update notification toast** (`ToastService.ShowUpdateNotification`):
  - РџРѕРєР°Р·С‹РІР°РµС‚СЃСЏ РїСЂРё С„РѕРЅРѕРІРѕРј РѕР±РЅР°СЂСѓР¶РµРЅРёРё РѕР±РЅРѕРІР»РµРЅРёСЏ РІР·Р°РјРµРЅ РјРѕРґР°Р»СЊРЅРѕРіРѕ РґРёР°Р»РѕРіР°.
  - Action-РєРЅРѕРїРєРё `РћР±РЅРѕРІРёС‚СЊ` / `РџРѕР·Р¶Рµ`; РЅРµ РёСЃС‡РµР·Р°РµС‚ СЃР°РјР° РїРѕРєР° РїРѕР»СЊР·РѕРІР°С‚РµР»СЊ РЅРµ РІС‹Р±РµСЂРµС‚.
  - Click-РѕР±СЂР°Р±РѕС‚С‡РёРє РєРѕСЂСЂРµРєС‚РЅРѕ СЂР°Р±РѕС‚Р°РµС‚ СЃ `ToastCanvas.IsHitTestVisible="False"`: СЏРІРЅРѕ СЃС‚Р°РІРёС‚ `IsHitTestVisible = true` РЅР° toast Border (РёРЅР°С‡Рµ РєРЅРѕРїРєРё РјРѕР»С‡Р° РЅРµ РїРѕР»СѓС‡Р°СЋС‚ Click РёР·-Р·Р° Inheritable DP).
- `UpdateService.CheckInBackgroundAsync` вЂ” С„РѕРЅРѕРІР°СЏ РїСЂРѕРІРµСЂРєР°, РѕС‚Р»РёС‡Р°РµС‚СЃСЏ РѕС‚ СЃС‚Р°СЂС‚Р°Рї-С‡РµРєР° С‚РµРј, С‡С‚Рѕ РѕС‚РєСЂС‹РІР°РµС‚ persistent toast РІРјРµСЃС‚Рѕ РјРѕРґР°Р»СЊРЅРѕРіРѕ РґРёР°Р»РѕРіР° (СЃС‚Р°СЂС‚Р°Рї вЂ” РјРѕРґР°Р» РїРѕ РїСЂРµР¶РЅРµРјСѓ).
- Activity-tracking РІ `MainWindow.xaml.cs`: `PreviewMouseMove` Рё `PreviewKeyDown` СЃР±СЂР°СЃС‹РІР°СЋС‚ idle-С‚Р°Р№РјРµСЂ scheduler'Р°.

### РўРµС…РЅРёС‡РµСЃРєРѕРµ

- `UpdateService`: РґРѕР±Р°РІР»РµРЅРѕ РїРѕР»Рµ `_lastNotifiedVersion` (anti-spam РІ СЃРµСЃСЃРёРё РґР»СЏ notification toast), СЃР±СЂР°СЃС‹РІР°РµС‚СЃСЏ РЅР° СѓСЃРїРµС€РЅРѕР№ СѓСЃС‚Р°РЅРѕРІРєРµ Рё РїСЂРё СЂСѓС‡РЅРѕРј В«РћР±РЅРѕРІРёС‚СЊВ» РІ РїР»Р°С€РєРµ.
- `MainWindow.xaml.cs`: scheduler СЃС‚Р°СЂС‚СѓРµС‚ РІ `Loaded`, РѕСЃС‚Р°РЅР°РІР»РёРІР°РµС‚СЃСЏ РІ `Closed`.
- **20+ РЅРѕРІС‹С… СЋРЅРёС‚-С‚РµСЃС‚РѕРІ** РґР»СЏ `UpdateCheckScheduler.ShouldCheckAt` (`UpdateCheckSchedulerTests.cs`): edge-cases `>=`/`<=` gate, throttle/idle/periodic state-machine, activity-reset, `OnCheckDue` SafeInvoke contract.

---

## 3.38.0 вЂ” 2026-06-28

### РЈР»СѓС‡С€РµРЅРёСЏ

- **UI-polish:** `CornerRadius` СѓРІРµР»РёС‡РµРЅ СЃ 4 РґРѕ 7 РІ `AdditionalKpsControl.xaml`; РґРѕР±Р°РІР»РµРЅ `CornerRadius` РІ `DataGridStyles.xaml` РґР»СЏ СЏС‡РµРµРє Рё СЃС‚СЂРѕРє вЂ” Р±РѕР»РµРµ РјСЏРіРєРёР№ Рё РµРґРёРЅРѕРѕР±СЂР°Р·РЅС‹Р№ РІРЅРµС€РЅРёР№ РІРёРґ.
- **XAML-Р°РЅРёРјР°С†РёСЏ UpdateDownloadBar:** fade-in (200 РјСЃ, CubicEase EaseOut) Рё fade-out (250 РјСЃ, CubicEase EaseIn) РІС‹РЅРµСЃРµРЅС‹ РІ `Storyboard` СЂРµСЃСѓСЂСЃС‹ `MainWindow.xaml` (Р±Р°Рі #8). `From` РґР»СЏ fade-out Р·Р°РґР°С‘С‚СЃСЏ РґРёРЅР°РјРёС‡РµСЃРєРё РёР· С‚РµРєСѓС‰РµРіРѕ `Opacity` вЂ” Р±РµР· РІРёР·СѓР°Р»СЊРЅС‹С… СЃРєР°С‡РєРѕРІ РїСЂРё РїСЂРµСЂС‹РІР°РЅРёРё.

### РСЃРїСЂР°РІР»РµРЅРёСЏ

- **Zero-byte download fix:** `DownloadWithProgressAsync` С‚РµРїРµСЂСЊ РєРѕСЂСЂРµРєС‚РЅРѕ СЃРѕРѕР±С‰Р°РµС‚ 100% РїСЂРѕРіСЂРµСЃСЃ РїСЂРё РѕС‚СЃСѓС‚СЃС‚РІРёРё `Content-Length` РёР»Рё РЅСѓР»РµРІРѕРј СЂР°Р·РјРµСЂРµ РѕС‚РІРµС‚Р° вЂ” СЂР°РЅСЊС€Рµ РїРѕР»РѕСЃРєР° РѕСЃС‚Р°РІР°Р»Р°СЃСЊ РЅР° 0%.

### РўРµС…РЅРёС‡РµСЃРєРѕРµ

- **UpdateService С‚РµСЃС‚РёСЂСѓРµРјРѕСЃС‚СЊ:** `FetchManifestAsync` Рё `DownloadWithProgressAsync` С‚РµРїРµСЂСЊ РїСЂРёРЅРёРјР°СЋС‚ РѕРїС†РёРѕРЅР°Р»СЊРЅС‹Р№ `HttpClient?` вЂ” РІРѕР·РјРѕР¶РЅРѕСЃС‚СЊ РёРЅСЉРµРєС†РёРё РјРѕРєРѕРІ РґР»СЏ РёРЅС‚РµРіСЂР°С†РёРѕРЅРЅС‹С… С‚РµСЃС‚РѕРІ. РџР°С‚С‚РµСЂРЅ `ownsClient` РіР°СЂР°РЅС‚РёСЂСѓРµС‚, С‡С‚Рѕ РІРЅРµС€РЅРёР№ `HttpClient` РЅРµ Р±СѓРґРµС‚ `Dispose`'РЅСѓС‚. Р”РѕР±Р°РІР»РµРЅРѕ 7 РёРЅС‚РµРіСЂР°С†РёРѕРЅРЅС‹С… С‚РµСЃС‚РѕРІ (`UpdateServiceIntegrationTests.cs`).
- **РўРµСЃС‚С‹:** 3 РЅРѕРІС‹С… С‚РµСЃС‚Р° РІ `PrintServiceTests.cs` вЂ” РїСЂРѕРІРµСЂРєР° РѕС‚РѕР±СЂР°Р¶РµРЅРёСЏ СЂР°СЃС‡С‘С‚РЅС‹С… СЂР°Р·РјРµСЂРѕРІ Anwis Р‘Р‘ 60 РІ РљРџ, HTML-СЌРєСЂР°РЅРёСЂРѕРІР°РЅРёРµ СЃРїРµС†СЃРёРјРІРѕР»РѕРІ (`&`, `"`, `'`), Рё РєРѕРЅРІРµСЂС‚Р°С†РёСЏ РїРµСЂРµРІРѕРґРѕРІ СЃС‚СЂРѕРє РІ `<br/>` РІ РїСЂРёРјРµС‡Р°РЅРёСЏС….

---

## 3.37.2 вЂ” 2026-06-27

### РСЃРїСЂР°РІР»РµРЅРёСЏ

- **SelectAll race fix:** РѕР±СЂР°Р±РѕС‚С‡РёРє `SelectAll_OnFocus` С‚РµРїРµСЂСЊ СЃРёРЅС…СЂРѕРЅРЅС‹Р№ (`tb.SelectAll()` Р±РµР· `BeginInvoke`) вЂ” РїСЂРё РєР»РёРєРµ РІ СЏС‡РµР№РєСѓ РЁРёСЂРёРЅС‹/Р’С‹СЃРѕС‚С‹ С‚РµРєСЃС‚ РІС‹РґРµР»СЏРµС‚СЃСЏ РґРѕ РїРµСЂРІРѕРіРѕ РЅР°Р¶Р°С‚РёСЏ РєР»Р°РІРёС€Рё, РІРІРѕРґ Р·Р°РјРµРЅСЏРµС‚ Р·РЅР°С‡РµРЅРёРµ, Р° РЅРµ РґРѕРїРёСЃС‹РІР°РµС‚. Р”РµС‚Р°Р»СЊ СЃРј. `docs/arc/GOTCHAS.md#14`.
- **Mid-typing formula clamp fix:** РЁРёСЂРёРЅР° Рё Р’С‹СЃРѕС‚Р° РїРµСЂРµРєР»СЋС‡РµРЅС‹ СЃ `UpdateSourceTrigger=PropertyChanged` РЅР° `LostFocus` вЂ” С„РѕСЂРјСѓР»Р° Anwis (РЅР°РїСЂ. `max(0, rawв€’30)`) Р±РѕР»СЊС€Рµ РЅРµ РїРµСЂРµС…РІР°С‚С‹РІР°РµС‚ Р·РЅР°С‡РµРЅРёРµ РЅР° РєР°Р¶РґРѕРј РЅР°Р¶Р°С‚РёРё, РЅР°Р±РѕСЂ РёРґС‘С‚ СЃРІРѕР±РѕРґРЅРѕ. Р”РµС‚Р°Р»СЊ СЃРј. `docs/arc/GOTCHAS.md#15`.
- **DeleteRowButton padding fix:** `Padding="4,0"` в†’ `Padding="5"` вЂ” РєРЅРѕРїРєР° СѓРґР°Р»РµРЅРёСЏ СЃС‚СЂРѕРєРё С‚РµРїРµСЂСЊ 20Г—20px, РєСЂР°СЃРЅС‹Р№ hover-С„РѕРЅ СЃ `CornerRadius=5` РІС‹РіР»СЏРґРёС‚ РїСЂРѕРїРѕСЂС†РёРѕРЅР°Р»СЊРЅРѕ, Р° РЅРµ СЃРїР»СЋСЃРЅСѓС‚Рѕ.

### РўРµС…РЅРёС‡РµСЃРєРѕРµ

- **UpdateLog sort-in-code:** `AllNewestFirst()` Рё `GetChangesSince()` С‚РµРїРµСЂСЊ СЃРѕСЂС‚РёСЂСѓСЋС‚ Р·Р°РїРёСЃРё РїРѕ РґР°С‚Рµ/РІРµСЂСЃРёРё РІ РєРѕРґРµ (`.OrderByDescending`/`.OrderBy`) вЂ” РїРѕСЂСЏРґРѕРє Р·Р°РїРёСЃРµР№ РІ JSON-С„Р°Р№Р»Рµ Р±РѕР»СЊС€Рµ РЅРµ РёРјРµРµС‚ Р·РЅР°С‡РµРЅРёСЏ. РЈСЃС‚СЂР°РЅСЏРµС‚ РїРѕРІС‚РѕСЂСЏСЋС‰РёР№СЃСЏ Р±Р°Рі СЃ СЂСѓС‡РЅС‹Рј prepend'РѕРј Р·Р°РїРёСЃРµР№ РїСЂРё СЂРµР»РёР·Рµ.
- **`ParseVersion` РґРёР°РіРЅРѕСЃС‚РёРєР°:** РґРѕР±Р°РІР»РµРЅ `Debug.WriteLine` РїСЂРё Р±РёС‚РѕР№ СЃС‚СЂРѕРєРµ РІРµСЂСЃРёРё РІ JSON вЂ” РѕРїРµС‡Р°С‚РєР° РІРёРґРЅР° РІ РѕС‚Р»Р°РґС‡РёРєРµ СЃСЂР°Р·Сѓ, Р° РЅРµ С‡РµСЂРµР· РјРѕР»С‡Р°Р»РёРІС‹Р№ fallback.
- 2 СЂРµРіСЂРµСЃСЃРёРѕРЅРЅС‹С… С‚РµСЃС‚Р° РІ `DataGridBindingsTests.cs` РїРµСЂРµРїСЂРѕС„РёР»РёСЂРѕРІР°РЅС‹: `PropertyChanged` в†’ `LostFocus` РґР»СЏ РЁРёСЂРёРЅС‹/Р’С‹СЃРѕС‚С‹.

---

## 3.37.1 вЂ” 2026-06-27

### РСЃРїСЂР°РІР»РµРЅРёСЏ

- **РњРѕРЅС‚Р°Р¶ СЃ Quantity > 1:** РІ СЂРµР¶РёРјР°С… В«Р‘РµР· РјРѕРЅС‚Р°Р¶Р°В» Рё В«Р’ РєРѕРЅСЃС‚СЂСѓРєС†РёСЋВ» deduction С‚РµРїРµСЂСЊ СѓРјРЅРѕР¶Р°РµС‚СЃСЏ РЅР° `Quantity` (per piece), Р° РЅРµ СЃРїРёСЃС‹РІР°РµС‚СЃСЏ РѕРґРёРЅ СЂР°Р·. РќР°РїСЂРёРјРµСЂ: Anwis 1000Г—1000 Г— 3 С€С‚. Г— СЂРµР¶РёРј В«Р’ РєРѕРЅСЃС‚СЂСѓРєС†РёСЋВ» СЃ РІС‹С‡РµС‚РѕРј 500 в‚Ѕ в†’ РёС‚РѕРіРѕРІС‹Р№ РІС‹С‡РµС‚ 1500 в‚Ѕ (Р° РЅРµ 500 в‚Ѕ). Backward-compat РґР»СЏ Q=1. Р”РµС‚Р°Р»СЊ СЃРј. `docs/arc/GOTCHAS.md#12` Рё `CALCULATION_TEST_CASES.md` Case 16.
- **РЈС‚РѕС‡РЅС‘РЅ tooltip РјРѕРЅС‚Р°Р¶Р°** РґР»СЏ СЂРµР¶РёРјРѕРІ 1/2: С‚РµРїРµСЂСЊ СЏРІРЅРѕ РїРѕРєР°Р·С‹РІР°РµС‚ `СЂСѓР±./С€С‚. Г— РљРѕР»-РІРѕ`, С‡С‚РѕР±С‹ РїРѕР»СЊР·РѕРІР°С‚РµР»СЊ РІРёРґРµР», С‡С‚Рѕ РІРІРµРґС‘РЅРЅР°СЏ СЃСѓРјРјР° РїСЂРёРјРµРЅСЏРµС‚СЃСЏ Р·Р° РєР°Р¶РґСѓСЋ С€С‚СѓРєСѓ.
- **РђРІС‚РѕС€РёСЂРёРЅР° РєРѕР»РѕРЅРєРё В«Р¦РµРЅР°В» РІ DataGrid:** РєРѕР»РѕРЅРєР° В«Р¦РµРЅР°В» РІ С‚Р°Р±Р»РёС†Рµ В«Р Р°СЃС‡С‘С‚В» (Рё В«Р¦РµРЅР°, СЂСѓР±.В» РІ tab В«Р¦РµРЅС‹В») РЅРµ СѓСЃРїРµРІР°Р»Р° СЂР°СЃС€РёСЂСЏС‚СЊСЃСЏ РїСЂРё РЅР°Р±РѕСЂРµ вЂ” `Width="Auto"` СЂРµРєРѕРјРїСЊСЋС‚РёР» С€РёСЂРёРЅСѓ РїРѕ СЃС‚Р°СЂРѕРјСѓ Р·РЅР°С‡РµРЅРёСЋ `Price` (binding РёРјРµР» `UpdateSourceTrigger="LostFocus"`). РўРµРїРµСЂСЊ `Price` РѕР±РЅРѕРІР»СЏРµС‚СЃСЏ РїРѕ `PropertyChanged`, Рё РєРѕР»РѕРЅРєР° РїРѕРґСЃС‚СЂР°РёРІР°РµС‚СЃСЏ РїРѕРґ РЅР°Р±РѕСЂ РІ СЂРµР°Р»СЊРЅРѕРј РІСЂРµРјРµРЅРё. Р”РµС‚Р°Р»СЊ СЃРј. `docs/arc/GOTCHAS.md#13`.

### РўРµС…РЅРёС‡РµСЃРєРѕРµ

- `InstallationToolTip` РґРѕРїРѕР»РЅРµРЅ СЃСѓС„С„РёРєСЃРѕРј `СЂСѓР±./С€С‚. Г— РљРѕР»-РІРѕ` РґР»СЏ modes 1/2.
- `UpdateSourceTrigger` РІ `OrderItemsControl.xaml` (Р¦РµРЅР°) Рё `PricesControl.xaml` (Р¦РµРЅР°, СЂСѓР±.) РїРµСЂРµРєР»СЋС‡С‘РЅ СЃ `LostFocus` РЅР° `PropertyChanged`.
- Р”РѕР±Р°РІР»РµРЅРѕ 8 РЅРѕРІС‹С… СЋРЅРёС‚-С‚РµСЃС‚РѕРІ РЅР° `TotalWithDeduction Г— Quantity` (РІРєР»СЋС‡Р°СЏ linear-scaling С‚РµРѕСЂРёСЋ Qв€€{1,2,5}, clamp-to-0 РґР»СЏ РІС‹СЃРѕРєРѕРіРѕ Q, regression РґР»СЏ В«Р’ РєРѕРЅСЃС‚СЂСѓРєС†РёСЋВ» СЃ Q=3).
- Р”РѕР±Р°РІР»РµРЅ `DataGridBindingsTests.cs` (5 regression-С‚РµСЃС‚РѕРІ С‡РµСЂРµР· grep XAML-binding-triggers) вЂ” guardrails РЅР° РїСЂР°РІРёР»СЊРЅС‹Р№ `UpdateSourceTrigger` РґР»СЏ РІСЃРµС… СЂРµРґР°РєС‚РёСЂСѓРµРјС‹С… РєРѕР»РѕРЅРѕРє СЃ `Width="Auto"`.

---

## 3.37.0 вЂ” 2026-06-27

### Р”Р»СЏ РїРѕР»СЊР·РѕРІР°С‚РµР»РµР№

- **РћР±РЅРѕРІР»РµРЅРёРµ СѓРІРµРґРѕРјР»РµРЅРёР№ вЂ” РїРѕР»РЅС‹Р№ rework:** РґРёР°Р»РѕРі В«Р”РѕСЃС‚СѓРїРЅРѕ РѕР±РЅРѕРІР»РµРЅРёРµВ» С‚РµРїРµСЂСЊ РїРѕРєР°Р·С‹РІР°РµС‚ СЃРїРёСЃРѕРє РёР·РјРµРЅРµРЅРёР№ (changelog) СЃ РјРѕРјРµРЅС‚Р° СѓСЃС‚Р°РЅРѕРІР»РµРЅРЅРѕР№ РІРµСЂСЃРёРё. TitleBar РїРѕР»СѓС‡РёР» РїР»Р°РІРЅРѕ РїРѕСЏРІР»СЏСЋС‰СѓСЋСЃСЏ/РёСЃС‡РµР·Р°СЋС‰СѓСЋ РїРѕР»РѕСЃРєСѓ РїСЂРѕРіСЂРµСЃСЃР° СЃРєР°С‡РёРІР°РЅРёСЏ (fade-in 200 РјСЃ / fade-out 250 РјСЃ). РЈР±СЂР°РЅ СЃС‚Р°СЂС‹Р№ РїСЂРѕРіСЂРµСЃСЃ-РїР°РЅРµР»СЊ РёР· ActionBar.
- **Toast-С„РёР»СЊС‚СЂР°С†РёСЏ:** РїСЂРё Р°РІС‚РѕРјР°С‚РёС‡РµСЃРєРѕР№ РїСЂРѕРІРµСЂРєРµ РѕР±РЅРѕРІР»РµРЅРёР№ РїСЂРё СЃС‚Р°СЂС‚Рµ РїСЂРѕРіСЂР°РјРјС‹ РЅРµ РїРѕРєР°Р·С‹РІР°СЋС‚СЃСЏ С‚РѕСЃС‚С‹ В«РћР±РЅРѕРІР»РµРЅРёР№ РЅРµС‚В» вЂ” С‚РѕР»СЊРєРѕ РїСЂРё СЂСѓС‡РЅРѕР№ РїСЂРѕРІРµСЂРєРµ С‡РµСЂРµР· РјРµРЅСЋ.

### РўРµС…РЅРёС‡РµСЃРєРѕРµ

- **UpdateService:** Р»РѕРіРёРєР° РїСЂРѕРІРµСЂРєРё РІРµСЂСЃРёРё РёР·РІР»РµС‡РµРЅР° РІ С‡РёСЃС‚С‹Р№ `internal static GetAvailableUpdate` вЂ” С‚РµСЃС‚РёСЂСѓРµРјР°СЏ Р±РµР· UI-Р·Р°РІРёСЃРёРјРѕСЃС‚РµР№. Р”РѕР±Р°РІР»РµРЅ С„Р»Р°Рі `isAutomatic` РґР»СЏ СЂР°Р·Р»РёС‡РµРЅРёСЏ Р°РІС‚Рѕ/СЂСѓС‡РЅРѕР№ РїСЂРѕРІРµСЂРєРё.
- **8 РЅРѕРІС‹С… СЋРЅРёС‚-С‚РµСЃС‚РѕРІ** РґР»СЏ `UpdateService` (6 РЅР° `GetAvailableUpdate` + 2 РЅР° `HasPendingUpdate`).
- **A.R.C. v4** вЂ” SYMBOL_INDEX.md (60 РєР»Р°СЃСЃРѕРІ, 16 РјРѕРґСѓР»РµР№), INTENTS.md (routing С„СЂР°Р· РЅР° С„Р°Р№Р»С‹), `gensymbols.ps1`, `arc-check.ps1`.

---

## 3.36.2 вЂ” 2026-06-25

### РЈР»СѓС‡С€РµРЅРёСЏ

- **Р‘СЂСѓСЃ, РџРѕСЏСЃ, Р”РѕСЃС‚Р°РІРєР° вЂ” С‚РѕР»СЊРєРѕ СЃСѓРјРјР°:** РґР»СЏ СЌС‚РёС… С‚РѕРІР°СЂРѕРІ СЃРєСЂС‹С‚С‹ РєРѕР»РѕРЅРєРё В«РљРѕР»-РІРѕВ», В«РџР»РѕС‰./Р”Р».В» РІ С‚Р°Р±Р»РёС†Рµ СЂР°СЃС‡С‘С‚Р° Рё РѕС‚РєР»СЋС‡РµРЅС‹ РїРѕР»СЏ В«РљРѕР»-РІРѕВ», В«РЁРёСЂРёРЅР°В», В«Р’С‹СЃРѕС‚Р°В» РІ РїР°РЅРµР»Рё Р±С‹СЃС‚СЂРѕРіРѕ РІРІРѕРґР°. РџСЂРµРІСЊСЋ РїРѕРєР°Р·С‹РІР°РµС‚ С‚РѕР»СЊРєРѕ С†РµРЅСѓ.

### РСЃРїСЂР°РІР»РµРЅРёСЏ

- РСЃРїСЂР°РІР»РµРЅР° РєРѕРґРёСЂРѕРІРєР° UTF-8 BOM РІ PowerShell-СЃРєСЂРёРїС‚Р°С… (`validate-docs.ps1`, `what-to-update.ps1`, `render-matrix.ps1`, `generate-update-log.ps1`) вЂ” РєРёСЂРёР»Р»РёС†Р° РІ regex РїР°СЂСЃРёР»Р°СЃСЊ РЅРµРєРѕСЂСЂРµРєС‚РЅРѕ.
- Р’РѕСЃСЃС‚Р°РЅРѕРІР»РµРЅ `update-log.json` РёР· git (Р±С‹Р» РѕР±СЂРµР·Р°РЅ РґРѕ 3 Р·Р°РїРёСЃРµР№); РґРѕР±Р°РІР»РµРЅР° Р·Р°РїРёСЃСЊ 3.36.1.
- РСЃРїСЂР°РІР»РµРЅРѕ 6 РѕС€РёР±РѕРє РІ `UpdateLogTests` Рё `ManualChecklistTests`.

---

## 3.36.1 вЂ” 2026-06-24

### РўРµС…РЅРёС‡РµСЃРєРѕРµ

- **A.R.C. upgrade v3** вЂ” РїРѕР»РЅР°СЏ Р°РІС‚РѕРјР°С‚РёР·Р°С†РёСЏ РґРѕРєСѓРјРµРЅС‚РёСЂРѕРІР°РЅРёСЏ (0 СЂСѓС‡РЅС‹С… РѕРїРµСЂР°С†РёР№ РґР»СЏ Р°РіРµРЅС‚Р°):
  - `docs/arc/documentation-matrix.json` вЂ” РјР°С€РёРЅРѕС‡РёС‚Р°РµРјС‹Р№ РёСЃС‚РѕС‡РЅРёРє РјР°С‚СЂРёС†С‹ В«С„Р°Р№Р» в†’ РґРѕРєСѓРјРµРЅС‚С‹В».
  - `what-to-update.ps1` вЂ” СЃРєСЂРёРїС‚: РїСЂРёРЅРёРјР°РµС‚ `git diff --name-only`, РІС‹РІРѕРґРёС‚ СЃРїРёСЃРѕРє docs/arc С„Р°Р№Р»РѕРІ Рє РѕР±РЅРѕРІР»РµРЅРёСЋ. Р¤Р°Р·Р° Document СЃС‚Р°Р»Р° 100% РјРµС…Р°РЅРёС‡РµСЃРєРѕР№.
  - `generate-update-log.ps1` вЂ” Р°РІС‚РѕРіРµРЅРµСЂР°С†РёСЏ `update-log.json` РёР· `CHANGELOG.md`. РЈР±РёСЂР°РµС‚ СЂСѓС‡РЅСѓСЋ СЃРёРЅС…СЂРѕРЅРёР·Р°С†РёСЋ РїСЂРё СЂРµР»РёР·Рµ.
  - `render-matrix.ps1` вЂ” РіРµРЅРµСЂР°С†РёСЏ `DOCUMENTATION_MATRIX.md` РёР· JSON.
  - `validate-docs.ps1` СЂР°СЃС€РёСЂРµРЅ РґРѕ 8 РїСЂРѕРІРµСЂРѕРє: git-based Last verified (check 7), staleness detection (check 8).
  - `PROMPTS.md` вЂ” РґРѕР±Р°РІР»РµРЅ Prompt 7 (self-check РїРѕСЃР»Рµ РёР·РјРµРЅРµРЅРёР№: git diff в†’ what-to-update в†’ validate-docs).
  - `AGENTS.md` вЂ” РґРѕР±Р°РІР»РµРЅ inline routing (С‚Р°Р±Р»РёС†Р° РІ СЃР°РјРѕРј wrapper'Рµ вЂ” Р°РіРµРЅС‚ РјРѕР¶РµС‚ РЅРµ С‡РёС‚Р°С‚СЊ CHEATSHEET РґР»СЏ routing).
  - `CHEATSHEET.md` вЂ” РґРѕР±Р°РІР»РµРЅРѕ РїСЂР°РІРёР»Рѕ #17 (what-to-update в†’ validate-docs) Рё СЃРµРєС†РёСЏ В«РРЅСЃС‚СЂСѓРјРµРЅС‚С‹ Р°РІС‚РѕРјР°С‚РёР·Р°С†РёРёВ».

- **A.R.C. upgrade v2** вЂ” CHEATSHEET.md, DOCUMENTATION_MATRIX.md, PROMPTS.md, РіСЂР°РЅСѓР»СЏСЂРЅС‹Р№ routing, validate-docs.ps1.

---

## 3.36.0 вЂ” 2026-06-24

### Р”Р»СЏ РїРѕР»СЊР·РѕРІР°С‚РµР»РµР№

- **РљРѕРїРёСЂРѕРІР°РЅРёРµ Р·Р°РєР°Р·РѕРІ:** РґРѕР±Р°РІР»РµРЅ РїСѓРЅРєС‚ В«РљРѕРїРёСЂРѕРІР°С‚СЊВ» РІ РєРѕРЅС‚РµРєСЃС‚РЅРѕРј РјРµРЅСЋ СЃРїРёСЃРєР° В«Р—Р°РєР°Р·С‹В». РЎРѕР·РґР°С‘С‚ РїРѕР»РЅСѓСЋ РєРѕРїРёСЋ Р·Р°РєР°Р·Р° СЃ РЅРѕРІС‹Рј РЅРѕРјРµСЂРѕРј (РЅР°РїСЂРёРјРµСЂ, В«2-8В» в†’ В«2-8.1В»), СЃС‚Р°С‚СѓСЃРѕРј В«РќРѕРІС‹Р№В» Рё Р°РєС‚СѓР°Р»СЊРЅРѕР№ РґР°С‚РѕР№.

### РСЃРїСЂР°РІР»РµРЅРёСЏ

- РСЃРїСЂР°РІР»РµРЅ XML-РєРѕРјРјРµРЅС‚Р°СЂРёР№ `InstallationSurcharge` РІ `OrderItem.Installation.cs`: `Default 0 в‚Ѕ` в†’ `Default 500 в‚Ѕ` (СЃРѕРѕС‚РІРµС‚СЃС‚РІСѓРµС‚ РєРѕРґСѓ).
- РСЃРїСЂР°РІР»РµРЅ pre-existing РґСЂРёС„С‚ РІРµСЂСЃРёРё РІ С‚РµСЃС‚Рµ `UpdateLogTests.AllNewestFirst_FirstItemIsNewest` (РѕР¶РёРґР°Р» 3.34.5, С‚РµРїРµСЂСЊ 3.35.0).

### РўРµС…РЅРёС‡РµСЃРєРѕРµ

- **GenerateCopyContractNumber** РїРµСЂРµРЅРµСЃС‘РЅ РёР· `MainWindow.Orders.cs` РІ `OrderStorageService.cs`.
- Р”РѕР±Р°РІР»РµРЅРѕ **11 С‚РµСЃС‚РѕРІ**.
- РРЅРёС†РёР°Р»РёР·РёСЂРѕРІР°РЅР° СЃРёСЃС‚РµРјР° A.R.C.
- **Multi-agent control: portability migration.**

---

## 3.35.0 вЂ” 2026-06-23

### РСЃРїСЂР°РІР»РµРЅРёСЏ

- РџРѕР»РЅС‹Р№ С„РёРєСЃ СѓС‚РµС‡РєРё С„РѕСЂРјСѓР» Anwis РЅР° РЅРµ-Anwis С‚РѕРІР°СЂС‹.

---

*РџРѕР»РЅР°СЏ РёСЃС‚РѕСЂРёСЏ СЂРµР»РёР·РѕРІ РґРѕСЃС‚СѓРїРЅР° РІ `releases.json` Рё `MosquitoNetCalculator/Resources/update-log.json`.*

