# План рефакторинга крупнейших файлов — A.R.C. Frame

## 1. Executive summary

**Цель:** разбить три самых крупных файла кода (по итогам анализа 2026-08-10), устранив God-file'ы в UI-оркестрации и сервисах, сохранив бизнес-логику неизменной.

**Почему именно эти три:** анализ топ-7 файлов по размеру показал, что XAML-файлы (`AiAssistantControl.xaml` 1074 строки, `MainWindow.xaml` 690, `SlopePanelControl.xaml` 720) рефакторить **нецелесообразно** — это разметка без бизнес-логики, разбиение даёт только косметический эффект при росте риска поломки именованных элементов. `MainWindow.xaml.cs` (783) уже прошёл фазу 1 рефакторинга и оставшийся код — клей/проводка, дальнейшее разбиение маловыгодно. Три файла ниже — **крупные, содержат реальную логику и имеют очевидные точки разбиения**.

**Базовые метрики (фиксация 2026-08-10, v3.47.4):**

| Файл | Строк | Байт | Проблема |
|---|---|---|---|
| `MosquitoNetCalculator/Services/AiAssistantService.cs` | 1 242 | 66 KB | Сеть + стриминг + ретраи + промпты + тест ключа |
| `MosquitoNetCalculator/Controls/PrintPreviewControl.xaml.cs` | 997 | 45 KB | Печать + принтеры + пагинация + настройки + затемнение |
| `MosquitoNetCalculator/Controls/SlopePanelControl.xaml.cs` | 969 | 53 KB | UI откосов + расчёт + сводка материалов + VM-свойства внутри контрола |

**Принципы:**

1. **Бизнес-логика не трогается** — формулы Anwis, цены, монтаж, итоги, печать КП, автообновление, сериализация.
2. **Partial-классы остаются** — новые компоненты выделяются в отдельные классы, а не в новые partial-файлы.
3. **Тесты до/после** — каждая фаза начинается с фиксации baseline и заканчивается `dotnet test`.
4. **Документация обновляется параллельно** — `CURRENT_STATE.md`, `CHANGELOG.md`, `SYMBOL_INDEX.md` (через `gensymbols.ps1`), `DOCUMENTATION_MATRIX.md`, `MODULES.md`.
5. **AI-зона — с осторожностью:** `AiAssistantService` — критичный модуль, сейчас в статусе «В РАЗРАБОТКЕ»; публичный API сохраняется, изменения только структурные (вынос приватных методов).

---

## 2. Baseline и контрольные точки

### 2.1. Фиксация baseline

Перед началом любой фазы:

```powershell
dotnet build MosquitoNetCalculator.sln -c Debug
dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --no-build
```

**Текущий baseline (2026-08-10):** 1527/1527 tests pass, v3.47.4.

### 2.2. Контрольные точки после каждой фазы

- `dotnet build` — 0 errors, 0 warnings.
- `dotnet test` — все тесты проходят (baseline + новые).
- `validate-docs.ps1` — без критических расхождений.
- `code-reviewer-luna` — ревью изменённых файлов.

---

## 3. Фаза A — `PrintPreviewControl.xaml.cs` (997 строк): выделить состояния и коллекторы

### 3.1. Цель

Уменьшить `PrintPreviewControl.xaml.cs` до ~450 строк, вынеся чистые (pure) и самодостаточные части в отдельные классы. Файл не был покрыт фазами 1–6 системного рефакторинга — это самый «первый» кандидат: бизнес-логика документа уже живёт в `FlowDocumentBuilder`/`FixedDocumentBuilder`/`PrintQueueManager`, в code-behind осталась UI-оркестрация.

### 3.2. Выделяемые компоненты

| Компонент | Файл | Отвечает за | Строк к выносу |
|---|---|---|---|
| `PrintSettingsCollector` | `Services/PrintSettingsCollector.cs` | `CollectSettings`/`CollectSettingsInto`/`RestoreSettings`/`GetSettings` — перенос состояния UI в `PrintSettings` и обратно | ~80 |
| `PaginationState` | `Services/PrintPagination.cs` | `PageMode_Changed`, `UpdatePageModeVisibility`, `RangeField_ValueChanged`, `OnPageNumberChanged`, `UpdateDimmingOverlay`, `UpdateRangeHint`, `GetPageCountForRange`, `PrevPage_Click`, `NextPage_Click` | ~200 |
| `PrinterListProvider` | `Services/PrinterListProvider.cs` | `PopulatePrinterList`, `DisposePrinterQueues`, `PrinterCombo_SelectionChanged` — перечисление очередей печати (использовать существующий `PrintQueueManager`) | ~80 |
| `DebounceTimers` (опционально) | внутри контрола | Вынос двух `DispatcherTimer` (text-change + resize) в маленький helper | ~30 |

### 3.3. Пошаговые действия

1. **Создать `PrintSettingsCollector`**
   - Переместить `CollectSettingsInto(PrintSettings target)` и логику чтения полей UI.
   - `RestoreSettings` остаётся в контроле (нужен доступ к элементам), но делегирует построение значений коллектору.
   - Чистый класс → легко тестируется без WPF.

2. **Создать `PrintPagination`**
   - Перенести всю математику страниц/диапазонов: `GetPageCountForRange`, `UpdateRangeHint`, расчёт видимости `UpdatePageModeVisibility`.
   - Контрол вызывает методы с аргументами (totalPages, rangePages), класс не знает о визуальном дереве.
   - `UpdateDimmingOverlay` — оставить в контроле (работает с XAML-элементами), но вычисления вынести.

3. **Создать `PrinterListProvider`**
   - Обёртка над `PrintQueueManager`/`LocalPrintServer` для перечисления очередей.
   - Учесть v3.47.4 fix: единый экземпляр `PrintQueue` из `Local + Connections`, никакого повторного разрешения по имени.

4. **Обновить `PrintPreviewControl.xaml.cs`**
   - Оставить: `Initialize`, обработчики событий, работу с визуальными элементами, `DeepCloneDocument`.
   - Все вычисления — в новых классах.

### 3.4. Тесты

- +8 тестов на `PrintSettingsCollector` (round-trip: UI-значения → PrintSettings → обратно; edge: пустые поля, диапазоны).
- +8 тестов на `PrintPagination` (количество страниц, диапазоны, prev/next границы, page-mode видимость).
- +4 теста на `PrinterListProvider` (мок очередей; при отсутствии — пустой список).
- Существующие STA-тесты печати (`PrintServiceTests`, `ManualChecklistTests`) должны продолжать проходить.

### 3.5. Ожидаемый эффект

- `PrintPreviewControl.xaml.cs` — ~450–500 строк (−50%).
- Пагинация и настройки тестируемы изолированно без WPF.
- Никаких изменений печатного документа и логики выбора принтера.

---

## 4. Фаза B — `AiAssistantService.cs` (1242 строки): разделить ответственности

### 4.1. Цель

Разбить самый большой .cs-файл проекта. Точки разбиения очевидны и безопасны: ~200 строк — чистый текст системных промптов (не бизнес-логика), каталог моделей — независимый сетевой блок, тест ключа — самостоятельная операция.

⚠️ **Ограничение:** AI-раздел сейчас в режиме preview/lockdown («В РАЗРАБОТКЕ»). Меняются **только** приватные методы и внутренняя структура. Публичный API (`FetchAvailableModelsAsync`, `SendMessageAsync`, `SendStreamingAsync`, `TestApiKeyAsync`, `GetProviderForModel`, `HttpClient` static) — **без изменений**. Никаких правок промптов и логики ретраев/фолбэка.

### 4.2. Выделяемые компоненты

| Компонент | Файл | Отвечает за | Строк к выносу |
|---|---|---|---|
| `AiPromptBuilder` | `Services/AiPromptBuilder.cs` | `BuildSystemPrompt`, `AppendRecentUpdates`, `FormatUpdateHistory` — весь текст системного промпта | ~200 |
| `AiModelCatalogClient` | `Services/AiModelCatalogClient.cs` | `FetchOpenRouterModelsAsync`, `FetchNvidiaModelsAsync`, `IsZeroPrice`, `ReconcileSavedModels`, `SetAvailableModels` — загрузка и синхронизация каталога моделей | ~150 |
| `AiKeyValidator` | `Services/AiKeyValidator.cs` | `TestApiKeyAsync`, `GetApiKey`, `GetApiUrl`, `ProviderName`, `GetProviderForModel` — проверка ключа и провайдер-хелперы | ~100 |
| `AiStreamingClient` (опционально) | `Services/AiStreamingClient.cs` | `TrySendModelAsync`, `ResolveFallbackModels`, `EnsureNvidiaFallback`, `BuildMessages`, `FormatModelName` — низкоуровневый HTTP-слой | ~200 |

### 4.3. Пошаговые действия

1. **Создать `AiPromptBuilder`** (самый безопасный шаг — чистый текст)
   - Переместить `BuildSystemPrompt`, `AppendRecentUpdates`, `FormatUpdateHistory` как static-методы.
   - Содержимое промптов не меняется ни на символ — только переезд.
   - `AiAssistantService` делегирует: `AiPromptBuilder.BuildSystemPrompt(...)`.

2. **Создать `AiModelCatalogClient`**
   - Перенести методы загрузки каталогов OpenRouter/NVIDIA и синхронизацию с сохранённым выбором.
   - `FetchAvailableModelsAsync` остаётся в сервисе как фасад, делегирует клиенту.

3. **Создать `AiKeyValidator`**
   - Перенести `TestApiKeyAsync` и провайдер-хелперы.
   - `AiApiKeyTestResult` record — оставить в `AiAssistantService` (публичный контракт) или перенести с using-обновлением; решение — по месту.

4. **(Опционально) Создать `AiStreamingClient`**
   - Только если после шагов 1–3 файл всё ещё > 700 строк.
   - Стриминг и ретраи — критичная логика; вынос только механическим переносом без изменения поведения.

5. **Обновить `AiAssistantService.cs`**
   - Оставить: публичный API, `SendMessageAsync`/`SendStreamingAsync`-оркестрацию, `HttpClient`, ошибки/статусы.
   - Ожидаемый размер: ~400–500 строк.

### 4.4. Тесты

- +10 тестов на `AiPromptBuilder` (промпт содержит ключевые секции: откосы, обновления, правила уточнения; не падает на пустом контексте).
- +6 тестов на `AiModelCatalogClient` (парсинг каталога OpenRouter/NVIDIA, zero-price фильтр).
- +6 тестов на `AiKeyValidator` (провайдер по modelId, URL, обработка пустого ключа).
- Существующие AI-тесты (`AiAssistantViewModelTests`, `AiGoldenCaseTests`, `AiCommandParserPlanModeTests`, `AiTelemetryServiceTests`) — без изменений, должны проходить.
- Release-flow AI-регрессии: `dotnet test --filter "FullyQualifiedName~AiGoldenCase|FullyQualifiedName~AiPlan|FullyQualifiedName~AiTelemetry"`.

### 4.5. Ожидаемый эффект

- `AiAssistantService.cs` — ~450 строк (−65%).
- Тексты промптов тестируемы и изолированы от сетевого кода.
- Публичный API и поведение AI — без изменений.

---

## 5. Фаза C — `SlopePanelControl.xaml.cs` (969 строк): вынести расчёт и сводку

### 5.1. Цель

Файл смешивает три сущности: **VM-свойства** (`HasNote`, `HasEconomyTooltip`, `OnPropertyChanged` — контрол сам реализует INPC, код-смэлл), **расчёт** (`UpdateCalculation` ~200 строк, `MakeWithoutEconomy`, `ComputeTotalSavings`) и **сводку материалов** (`_BuildMaterialSummary`, `BuildMaterialSummaryRows`, `GetRussianPlural`). Всё это можно вынести без риска: расчётная математика уже частично в `SlopeCalculatorService`/`SlopeEconomyCalculator`.

### 5.2. Выделяемые компоненты

| Компонент | Файл | Отвечает за | Строк к выносу |
|---|---|---|---|
| `SlopePanelViewModel` | `ViewModels/SlopePanelViewModel.cs` | INPC-свойства (`HasNote`, `HasEconomyTooltip`, заметки, экономия) — вынести VM из контрола | ~60 |
| `MaterialSummaryBuilder` | `Services/MaterialSummaryBuilder.cs` | `BuildMaterialSummaryRows`, `_BuildMaterialSummary`, `GetRussianPlural`, `ComputeTotalSavings` — построение сводки материалов | ~130 |
| `SlopeCalculationUpdater` (опционально) | `Services/SlopeCalculationUpdater.cs` | `UpdateCalculation` (входные поля → `SlopeCalculation` + пересчёт), `MakeWithoutEconomy` | ~200 |

### 5.3. Пошаговые действия

1. **Создать `SlopePanelViewModel`**
   - Перенести INPC-свойства и их логику из начала класса.
   - Контрол держит экземпляр VM и биндится к нему (или делегирует сеттеры).

2. **Создать `MaterialSummaryBuilder`** (чистый static helper — самый безопасный шаг)
   - `BuildMaterialSummaryRows(SlopeCalculation)` и `GetRussianPlural` уже `internal static` — переносятся механически.
   - `ComputeTotalSavings` — уже `internal static`, переносится как есть.

3. **(Опционально) Создать `SlopeCalculationUpdater`**
   - Только если после шагов 1–2 файл всё ещё > 500 строк.
   - Перенести тело `UpdateCalculation` (чтение полей → `SlopeCalculatorService` → запись в `SlopeCalculation`).
   - Требует аккуратного доступа к UI-полям — делегировать через параметры.

4. **Обновить `SlopePanelControl.xaml.cs`**
   - Оставить: обработчики событий, доступ к UI-элементам, `LoadForEdit`/`PrefillDimensions`/`Reset`, экономию.
   - Ожидаемый размер: ~450–500 строк.

### 5.4. Тесты

- +6 тестов на `SlopePanelViewModel` (INPC-события, дефолты).
- +8 тестов на `MaterialSummaryBuilder` (строки сводки, русская плюрализация, экономия с/без материала).
- Существующие `SlopeCalculatorServiceTests` (1107 строк) — без изменений, должны проходить.
- STA-тесты откосов (`ManualChecklistTests`) — должны проходить.

### 5.5. Ожидаемый эффект

- `SlopePanelControl.xaml.cs` — ~500 строк (−50%).
- VM-логика отделена от контрола (устранён код-смэлл INPC в контроле).
- Сводка материалов тестируема без WPF.

---

## 6. Риски и mitigation

| Риск | Вероятность | Влияние | Mitigation |
|---|---|---|---|
| Регрессия в печати КП (фаза A) | Средняя | Высокое | STA-тесты печати, round-trip `PrintSettingsCollector`, неизменный печатный документ |
| Поломка AI-поведения (фаза B) | Низкая | Высокое | Только механический перенос приватных методов; публичный API не трогается; полный AI-прогон тестов |
| Сломанный расчёт откосов (фаза C) | Средняя | Высокое | Существующие `SlopeCalculatorServiceTests`, перенос только чистых static-методов |
| Рост числа файлов усложняет навигацию | Средняя | Среднее | Обновить `SYMBOL_INDEX.md`, `MODULES.md`, `DOCUMENTATION_MATRIX.md` |

---

## 7. Success criteria

1. Все 3 фазы завершены (A → B → C, в любом безопасном порядке; C не зависит от A/B).
2. `dotnet test` — 100% pass (baseline 1527 + новые тесты).
3. Ни одна бизнес-формула не изменилась.
4. `PrintPreviewControl.xaml.cs` ≤ 500 строк.
5. `AiAssistantService.cs` ≤ 500 строк (публичный API без изменений).
6. `SlopePanelControl.xaml.cs` ≤ 550 строк.
7. `validate-docs.ps1` проходит без критических ошибок.
8. `SYMBOL_INDEX.md` перегенерирован (`gensymbols.ps1`), `MODULES.md` и `DOCUMENTATION_MATRIX.md` обновлены.

---

## 8. Порядок выполнения

```text
Фаза A (PrintPreviewControl) → Фаза B (AiAssistantService) → Фаза C (SlopePanelControl)
```

**Зависимости:** фаз нет — файлы не пересекаются; можно выполнять параллельно разными агентами (по одному на файл). Рекомендуется порядок A → B → C по убыванию безопасности/выгоды.

---

## Source files

- `MosquitoNetCalculator/Controls/PrintPreviewControl.xaml.cs`
- `MosquitoNetCalculator/Services/AiAssistantService.cs`
- `MosquitoNetCalculator/Controls/SlopePanelControl.xaml.cs`

## Last verified

2026-08-10 — план создан по итогам анализа топ-7 крупнейших файлов (v3.47.4, 1527/1527 tests pass).
