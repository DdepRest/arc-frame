# План системного рефакторинга — A.R.C. Frame

## 1. Executive summary

**Цель:** устранить накопленный архитектурный долг в виде God-classes и высокого coupling, сохранив расчётную логику, печать КП, автообновление и сериализацию заказов неизменными.

**Базовые метрики (фиксация 2026-07-12):**

| Файл | Строк | `using` | Fan-in | Проблема |
|---|---|---|---|---|
| `MosquitoNetCalculator/MainWindow.xaml.cs` | 1 052 | 21 | 25 | God-class главного окна |
| `MosquitoNetCalculator/Services/UpdateService.cs` | 910 | 17 | 11 | Сеть + криптография + UI + версии |
| `MosquitoNetCalculator/Services/DialogService.cs` | 641 | 10 | — | UI диалогов в коде |
| `MosquitoNetCalculator/Services/PrintService.cs` | 632 | 12 | — | Настройки, рендеринг, очередь печати |
| `MosquitoNetCalculator/Models/OrderItem.cs` | 651 | 6 | 21 | Домен + каталог + Anwis + откосы |
| `MosquitoNetCalculator/MainWindow.Orders.cs` | 527 | — | — | UI + сериализация + импорт/экспорт |

**Принципы:**

1. **Бизнес-логика не трогается** — формулы Anwis, цены, монтаж, итоги, печать КП, автообновление, сериализация.
2. **Partial-классы остаются** — новые компоненты выделяются в отдельные классы/сервисы, а не в новые partial-файлы.
3. **Тесты до/после** — каждая фаза начинается с фиксации baseline и заканчивается `dotnet test`.
4. **Документация обновляется параллельно** — `CURRENT_STATE.md`, `CHANGELOG.md`, `SYMBOL_INDEX.md`, `DOCUMENTATION_MATRIX.md`.

---

## 2. Baseline и контрольные точки

### 2.1. Фиксация baseline

Перед началом любой фазы:

```powershell
dotnet build MosquitoNetCalculator.sln -c Release
dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --no-build
```

**Текущий baseline (2026-07-12):** 906/906 tests pass, v3.44.2.

### 2.2. Контрольные точки после каждой фазы

- `dotnet build` — 0 errors.
- `dotnet test` — все тесты проходят (baseline + новые).
- `validate-docs.ps1` — без критических расхождений.
- `code-reviewer-kimi` — ревью изменённых файлов.

---

## 3. Фаза 1 — `MainWindow.xaml.cs`: выделить координаторы

### 3.1. Цель

Уменьшить размер `MainWindow.xaml.cs` с 1 052 строк до ~400 строк, вынеся самостоятельные подсистемы в отдельные сервисы/координаторы.

### 3.2. Выделяемые компоненты

| Компонент | Файл | Отвечает за |
|---|---|---|
| `NavigationService` | `Services/NavigationService.cs` | Управление активной кнопкой навигации, `SetActiveNavButton`, expand/collapse nav panel, hotkeys |
| `OverlayManager` | `Services/OverlayManager.cs` | Открытие/закрытие оверлеев, анимации, `CloseAllOverlays`, `HideOverlayInstant`, ZIndex |
| `SlopeOverlayCoordinator` | `Services/SlopeOverlayCoordinator.cs` | `ShowSlopeOverlay`, `EditSlopeItem`, загрузка цен откосов, поиск парной строки «Работа за откос» |
| `SlopesProUpsellGate` | `Services/SlopesProUpsellGate.cs` | Easter-egg логика диалога PRO (или удаление по чеклисту CHANGELOG) |

### 3.3. Пошаговые действия

1. **Создать `NavigationService`**
   - Интерфейс `INavigationService` с методами `SetActive(string navId)`, `Expand()`, `Collapse()`.
   - Реализация хранит ссылки на кнопки навигации и панель.
   - Перенести логику из `MainWindow.xaml.cs` методов `Nav*_Click`, `SetActiveNavButton`, `NavPanel_MouseEnter/Leave`.

2. **Создать `OverlayManager`**
   - Методы `ShowOverlay(FrameworkElement overlay)`, `CloseOverlay(FrameworkElement overlay)`, `CloseAll()`, `HideAllInstant()`.
   - Хранит словарь `overlay → Storyboard` для анимаций.
   - Перенести `ShowOverlay`, `CloseOverlay`, `CloseAllOverlays`, `HideOverlayInstant`.

3. **Создать `SlopeOverlayCoordinator`**
   - Методы `ShowSlopeOverlay(OrderItem? item = null)`, `EditSlopeItem(OrderItem item)`.
   - Инкапсулирует загрузку цен откосов из `PriceService`, создание/обновление `SlopeCalculation`, поиск парной строки «Работа за откос».
   - Устранить дублирование между `ShowSlopeOverlay` и `EditSlopeItem`.

4. **(Опционально) удалить Easter-egg `EasterProUpsellWindow`**
   - Если владелец согласен — удалить `EasterMenuService`, `EasterProUpsellWindow`, `SlopesProUpsellUnlocked` из настроек.
   - Иначе вынести логику в `SlopesProUpsellGate`.

5. **Обновить `MainWindow.xaml.cs`**
   - Оставить только делегирование событий XAML, инициализацию сервисов и высокоуровневую координацию.
   - Все вызовы навигации/оверлеев/откосов заменить на вызовы новых сервисов.

### 3.4. Тесты

- +10 юнит-тестов на `NavigationService` (установка активной кнопки, expand/collapse).
- +8 тестов на `OverlayManager` (show/close/close all, анимации).
- +6 тестов на `SlopeOverlayCoordinator` (загрузка цен, поиск парной строки, создание slope).

### 3.5. Ожидаемый эффект

- `MainWindow.xaml.cs` — ~400 строк.
- Coupling: `MainWindow` не знает о деталях анимаций и цен откосов.
- Тестируемость: координаторы можно тестировать изолированно.

---

## 4. Фаза 2 — `UpdateService.cs`: разделить ответственности

### 4.1. Цель

Разбить монолитный статический `UpdateService` на инжектируемые компоненты.

### 4.2. Выделяемые компоненты

| Компонент | Файл | Отвечает за |
|---|---|---|
| `IUpdateManifestClient` / `UpdateManifestClient` | `Services/UpdateManifestClient.cs` | Загрузка и парсинг `releases.json` |
| `IVersionResolver` / `VersionResolver` | `Services/VersionResolver.cs` | Парсинг версий, сравнение, `StripVersionSuffix`, `IsCurrentVersionBrokenForAutoUpdate` |
| `IUpdateDownloader` / `UpdateDownloader` | `Services/UpdateDownloader.cs` | Скачивание ZIP с прогрессом и retry-логикой |
| `IUpdateVerifier` / `UpdateVerifier` | `Services/UpdateVerifier.cs` | SHA-256 проверка архива |
| `IUpdatePresenter` / `UpdatePresenter` | `Services/UpdatePresenter.cs` | Показ диалогов, toasts, запуск watchdog, перезапуск приложения |
| `IIdleDetector` / `IdleDetector` | `Services/IdleDetector.cs` | WinAPI idle detection |

### 4.3. Пошаговые действия

1. **Создать интерфейсы и реализации**
   - Вынести каждую ответственность в отдельный класс.
   - Сохранить публичный API `UpdateService` для backward-compat.

2. **Перевести `UpdateService` с static на instance + DI**
   - Добавить конструктор `UpdateService(IUpdateManifestClient, IVersionResolver, ...)`.
   - Сохранить статические методы-прокси для существующих вызовов (mark as obsolete).

3. **Перенести WinAPI idle detection**
   - В `IdleDetector` с `GetLastInputInfo` P/Invoke.

4. **Перенести SHA-256 проверку**
   - В `UpdateVerifier`.

5. **Перенести диалоги/toasts/watchdog**
   - В `UpdatePresenter`.

### 4.4. Тесты

- +12 тестов на `VersionResolver` (парсинг, сравнение, suffix stripping).
- +8 тестов на `UpdateVerifier` (SHA-256 совпадает/не совпадает, повреждённый файл).
- +6 тестов на `UpdateDownloader` (прогресс, retry, zero-byte, ошибка HTTP).
- +4 теста на `IdleDetector` (mock `GetLastInputInfo`).
- Существующие интеграционные тесты `UpdateServiceIntegrationTests` должны продолжать проходить.

### 4.5. Ожидаемый эффект

- `UpdateService.cs` — ~200 строк (фасад).
- Каждый класс имеет одну причину для изменения (SRP).
- Возможность мокировать зависимости в тестах.

---

## 5. Фаза 3 — `PrintService.cs`: выделить рендеринг и очередь

### 5.1. Цель

Разделить настройки печати, рендеринг документа и работу с очередью печати.

### 5.2. Выделяемые компоненты

| Компонент | Файл | Отвечает за |
|---|---|---|
| `IPrintQueueResolver` / `PrintQueueResolver` | `Services/PrintQueueResolver.cs` | Поиск принтера, `LocalPrintServer`, `PrintQueue` |
| `IFixedDocumentBuilder` / `FixedDocumentBuilder` | `Services/FixedDocumentBuilder.cs` | Сборка `FixedDocument` из `FlowDocument` |
| `PrintSettings` / `PrintResult` | `Models/PrintSettings.cs`, `Models/PrintResult.cs` | Настройки печати и результат операции |
| `PrintOrchestrator` | `Services/PrintOrchestrator.cs` | Высокоуровневый pipeline: настройки → рендеринг → очередь |

### 5.3. Пошаговые действия

1. **Вынести `PrintSettings`/`PrintResult`**
   - Перенести в `Models/`.

2. **Создать `PrintQueueResolver`**
   - Методы `GetDefaultQueue()`, `GetQueueByName(string)`.

3. **Создать `FixedDocumentBuilder`**
   - Метод `BuildFixedDocument(FlowDocument, PrintTicket)`.

4. **Создать `PrintOrchestrator`**
   - Pipeline: получить настройки → построить документ → отправить в очередь.

5. **Оставить `PrintService` как фасад**
   - Сохранить публичные методы для backward-compat.

### 5.4. Тесты

- +6 тестов на `PrintQueueResolver`.
- +4 теста на `FixedDocumentBuilder`.
- +4 теста на `PrintOrchestrator`.
- Существующие STA-тесты печати должны продолжать проходить.

---

## 6. Фаза 4 — `DialogService.cs`: UI в XAML-шаблоны

### 6.1. Цель

Уйти от построения UI диалогов в коде, вынести XAML в отдельные шаблоны.

### 6.2. Выделяемые компоненты

| Компонент | Файл | Отвечает за |
|---|---|---|
| `DialogBuilder<T>` | `Services/DialogBuilder.cs` | Fluent-builder для диалогов |
| `ConfirmDialogWindow` | `Controls/ConfirmDialogWindow.xaml` | Универсальный диалог подтверждения |
| `InputDialogWindow` | `Controls/InputDialogWindow.xaml` | Диалог ввода строки/числа |
| `MessageDialogWindow` | `Controls/MessageDialogWindow.xaml` | Информационный диалог |

### 6.3. Пошаговые действия

1. **Создать XAML-шаблоны**
   - Универсальные диалоги с параметрами (title, message, buttons).

2. **Создать `DialogBuilder<T>`**
   - Fluent API: `.Title(...)`, `.Message(...)`, `.WithButton(...)`, `.ShowDialog()`.

3. **Постепенно мигрировать существующие диалоги**
   - `ShowConfirmation` → `ConfirmDialogWindow`.
   - `ShowInput` → `InputDialogWindow`.
   - `ShowMessage` → `MessageDialogWindow`.

4. **Удалить дублирующий код**
   - Унифицировать стили, margins, иконки.

### 6.4. Тесты

- +6 тестов на `DialogBuilder`.
- +4 STA-теста на каждый новый диалог.

---

## 7. Фаза 5 — `OrderItem.cs`: разделить домен

### 7.1. Цель

Вынести каталог продуктов, Anwis-логику и откосы из доменной модели.

### 7.2. Выделяемые компоненты

| Компонент | Файл | Отвечает за |
|---|---|---|
| `ProductCatalog` | `Models/ProductCatalog.cs` | HashSet'ы `AreaBasedProducts`, `NoColorProducts`, `InstallationApplicableProducts`, `AnticatApplicableProducts`, `OptionalQuantityProducts`, `ManualPieceProducts` |
| `AnwisSizeCalculator` | `Services/AnwisSizeCalculator.cs` | Формулы Anwis: `ОтВвода`, `ApplyCalcWidth`, `ApplyCalcHeight`, `ReverseCalcWidth`, `ReverseCalcHeight` |
| `SlopeCalculationExtensions` | `Models/SlopeCalculationExtensions.cs` | Методы-расширения для `SlopeCalculation` |

### 7.3. Пошаговые действия

1. **Создать `ProductCatalog`**
   - Перенести все HashSet'ы категорий товаров.
   - Статический singleton или инжектируемый сервис.

2. **Создать `AnwisSizeCalculator`**
   - Вынести pure-функции расчёта Anwis-размеров.
   - `OrderItem` делегирует вычисления этому сервису.

3. **Создать `SlopeCalculationExtensions`**
   - Вспомогательные методы для работы с `SlopeCalculation`.

4. **Обновить `OrderItem`**
   - Убрать статические HashSet'ы.
   - Использовать `ProductCatalog`.
   - Делегировать Anwis-вычисления `AnwisSizeCalculator`.

### 7.4. Тесты

- +10 тестов на `ProductCatalog` (категории, принадлежность).
- +12 тестов на `AnwisSizeCalculator` (все режимы ББ60/ББ70/РазмерПроёма/Габаритный).
- Существующие `OrderItemTests` должны продолжать проходить.

### 7.5. Ожидаемый эффект

- `OrderItem.cs` — ~350 строк.
- Доменная модель не зависит от каталога продуктов.
- Anwis-логика тестируема изолированно.

---

## 8. Фаза 6 — `MainWindow.Orders.cs`: выделить сервисы заказов

### 8.1. Цель

Убрать из `MainWindow` сериализацию, импорт/экспорт и диалоги заказов.

### 8.2. Выделяемые компоненты

| Компонент | Файл | Отвечает за |
|---|---|---|
| `IOrderImportExportService` / `OrderImportExportService` | `Services/OrderImportExportService.cs` | Импорт/экспорт заказов, JSON-serialization |
| `IOrderDialogService` / `OrderDialogService` | `Services/OrderDialogService.cs` | Диалоги «Сохранить как», «Открыть», «Новый заказ» |
| `IOrderGridPresenter` / `OrderGridPresenter` | `Services/OrderGridPresenter.cs` | Работа с DataGrid: выделение строк, скролл, фокус |

### 8.3. Пошаговые действия

1. **Создать `OrderImportExportService`**
   - Перенести `SaveOrder`, `LoadOrder`, `ExportOrder`, `ImportOrder`.

2. **Создать `OrderDialogService`**
   - Перенести диалоги сохранения/открытия.

3. **Создать `OrderGridPresenter`**
   - Перенести методы работы с `DataGrid` (выделение, скролл, фокус).

4. **Обновить `MainWindow.Orders.cs`**
   - Оставить только event-handlers, делегирующие работу сервисам.

### 8.4. Тесты

- +8 тестов на `OrderImportExportService`.
- +4 теста на `OrderDialogService`.
- +4 теста на `OrderGridPresenter`.

---

## 9. Фаза 7 — Валидация и документация

### 9.1. Сборка и тесты

```powershell
dotnet build MosquitoNetCalculator.sln -c Release
dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj
```

**Ожидаемый результат:** 0 errors, все тесты проходят (baseline + новые).

### 9.2. Обновление документации

| Документ | Что обновить |
|---|---|
| `docs/arc/CURRENT_STATE.md` | Статус рефакторинга, новые сервисы |
| `docs/arc/CHANGELOG.md` | Запись «Технический рефакторинг» |
| `docs/arc/SYMBOL_INDEX.md` | Новые классы/интерфейсы |
| `docs/arc/DOCUMENTATION_MATRIX.md` | Новые файлы → документы |
| `docs/arc/MODULES.md` | Обновить карту модулей |
| `docs/arc/DECISIONS.md` | Архитектурное решение о DI-сервисах |

### 9.3. Автоматизация

```powershell
# Генерация SYMBOL_INDEX
powershell -ExecutionPolicy Bypass -File gensymbols.ps1

# Проверка, что обновить
what-to-update.ps1 $(git diff --name-only)

# Валидация
powershell -ExecutionPolicy Bypass -File validate-docs.ps1
```

---

## 10. Риски и mitigation

| Риск | Вероятность | Влияние | Mitigation |
|---|---|---|---|
| Регрессия в навигации/оверлеях | Средняя | Высокое | Исчерпывающие тесты `NavigationService`, `OverlayManager` |
| Сломанное автообновление | Низкая | Критическое | Сохранить публичный API `UpdateService`, интеграционные тесты |
| Проблемы с печатью КП | Средняя | Высокое | STA-регрессии на `PrintService`, `FixedDocumentBuilder` |
| Нарушение сериализации заказов | Низкая | Критическое | Не менять DTO, тесты round-trip JSON |
| Рост числа файлов усложняет навигацию | Средняя | Среднее | Обновить `SYMBOL_INDEX.md`, `MODULES.md` |

---

## 11. Success criteria

1. Все 7 фаз завершены.
2. `dotnet test` — 100% pass (baseline + новые тесты).
3. Ни одна бизнес-формула не изменилась.
4. `MainWindow.xaml.cs` ≤ 400 строк.
5. `UpdateService.cs` ≤ 250 строк (фасад).
6. `OrderItem.cs` ≤ 400 строк.
7. `AGENTS.md` и `MULTI_AGENT_ARC_CALC_CONTROL.md` содержат ссылку на этот план.
8. `validate-docs.ps1` проходит без критических ошибок.

---

## 12. Порядок выполнения

```text
Фаза 1 → Фаза 2 → Фаза 3 → Фаза 4 → Фаза 5 → Фаза 6 → Фаза 7
```

**Зависимости:**
- Фаза 6 зависит от Фазы 4 (диалоги).
- Фаза 5 может идти параллельно с Фазами 1–4 (разные файлы).

---

## Source files

- `MosquitoNetCalculator/MainWindow.xaml.cs`
- `MosquitoNetCalculator/MainWindow.Orders.cs`
- `MosquitoNetCalculator/Services/UpdateService.cs`
- `MosquitoNetCalculator/Services/PrintService.cs`
- `MosquitoNetCalculator/Services/DialogService.cs`
- `MosquitoNetCalculator/Models/OrderItem.cs`

## Last verified

2026-07-13 — **Фаза 5 завершена.** `OrderItem.cs` 651→~520 строк (−20%). Выделены 3 компонента: `ProductCatalog` (категории товаров), `AnwisSizeCalculator` (pure-функции Anwis), `SlopeCalculationExtensions` (методы-расширения для `SlopeCalculation`). +21 тест. **1133/1133 tests pass.** Бизнес-логика и public API не затронуты.

2026-07-12 — **Фаза 4 завершена.** `DialogService.cs` 641→~250 строк (−61%). Созданы XAML-шаблоны диалогов и `DialogBuilder<T>`. +10 тестов. **1071/1071 tests pass.** Бизнес-логика и public API не затронуты.

2026-07-12 — **Фаза 3 завершена.** `PrintService.cs` 632→81 строк (−87%). 6 компонентов: `DrawingService`, `FlowDocumentBuilder`, `FixedDocumentBuilder`, `PrintQueueManager`, `PdfExportService`, плюс модели `PageMode`/`PrintSettings`/`PrintResult`. +~40 тестов. **1038/1038 tests pass.** Бизнес-логика не затронута.

2026-07-12 — **Фаза 2 завершена.** `UpdateService.cs` 910→608 строк (−33%). 5 компонентов: `VersionResolver`, `IdleDetector`, `UpdateVerifier`, `UpdateManifestClient`, `UpdateDownloader`. +46 тестов. **978/978 tests pass.** Бизнес-логика не затронута.

**Фаза 1 также завершена.** `MainWindow.xaml.cs` 1051→760 строк (−28%). 4 сервиса: `NavigationService`, `OverlayManager`, `SlopeOverlayCoordinator`, `SlopesProUpsellGate`. +26 тестов.
