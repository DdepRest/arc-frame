# Changelog

## Unreleased — готовится к следующему релизу

## 3.40.3 — 2026-06-29

### Техническое

- **`MainWindow.OnUpdateProgressChanged` извлечён в отдельный тестируемый хелпер `Helpers/ProgressBarUpdateAnimator.cs`:** тело метода стало однострочным делегатом `_progressAnimator?.Animate(...)`. Хелпер инкапсулирует всю логику `TryFindResource` + Storyboard fade (fade-in 200 мс / fade-out 250 мс) + belt-and-suspenders `try/catch` с явным re-throw для `OutOfMemoryException` / `StackOverflowException`. Отделён от `UpdateService` через `Func<double>` (текущий прогресс) и `Func<bool>` (идёт ли скачивание) — это позволило покрыть контракт «UI прогресс-бар не роняет auto-update flow» юнит-тестами впервые за всю историю проекта.
- **`UpdateService.IsCurrentVersionBrokenForAutoUpdate(Version?)`** (internal): проверка half-open interval `[3.40.0, 3.40.2)` через прямую int-сравнку `Major`/`Minor`/`Build` (избегает footgun `Version(3,40,1,0) > Version(3,40,1)` из-за `Revision 0 > -1`). Ранее та же проверка дублировалась inline-литералами в `MainWindow_Loaded`.
- **30 новых юнит-тестов** (711/711, было 681):
  - `UpdateServiceTests.cs`: 14 inline-кейсов на boundary диапазона (3-part и 4-part версии, before/after/different major) + null-arg + smoke test на `CurrentVersion`.
  - `ProgressBarUpdateAnimatorTests.cs`: 10 тестов — no-throw контракт для не-fatal исключений (`InvalidOperationException`, `InvalidCastException`, `ArgumentException`, `FormatException` через `[Theory]`), явный `Assert.Throws` для OOM/SOF, fallback Visibility/Opacity path при отсутствии Storyboard, no-op early-return, конструкторские null-проверки.
- **xUnit `[Collection("STA")]` с `DisableParallelization`:** изолирует STA-тесты от параллельного исполнения, чтобы избежать гонки на AppDomain-wide `Application.Current`. Сами тесты НЕ создают `Application` — `FrameworkElement.TryFindResource` корректно обрабатывает `Application.Current == null`, что даёт штатный fallback path для тестирования и устраняет конфликт с `AppLifecycleTests`.

### Заметки

- Полностью обратно-совместимо с v3.40.0 → v3.40.2 (никаких регрессий). Production-поведение `OnUpdateProgressChanged` идентично v3.40.2.
- v3.40.3 — первый релиз проекта, где контракт «UI ошибка не роняет auto-update» имеет **testable regression coverage** (а не только в комментариях).
- Пользователям v3.40.0 / v3.40.1 переход на v3.40.3 безопасен, но не обязателен — видимое поведение не менялось. Рекомендуется всем, кто хочет unit-test гарантии от регрессий этого типа.

---

## 3.40.2 — 2026-06-29

### Исправления

- **Belt-and-suspenders try/catch в `MainWindow.OnUpdateProgressChanged`:** тело метода обёрнуто в `try { ... } catch (Exception ex) { ... }`. При ЛЮБОМ исключении внутри (включая будущие BAML-mismatch, XAML-refactor, third-party change) метод логирует в Debug, устанавливает Visibility напрямую без анимации, и **не пробрасывает исключение выше**. Это защита от повторения v3.40.0-style бага, когда ошибка в UI прогресс-бара убивала Dispatcher.Invoke → MessageBox → разрыв auto-update-flow → невозможность получить никакой следующий релиз.

### Прочее

- **Стартовый баннер для известных сломанных версий:** в `MainWindow.MainWindow_Loaded` добавлена проверка `CurrentVersion >= 3.40.0 && CurrentVersion < 3.40.2`. Если условие выполнено, при запуске показывается toast-предупреждение со ссылкой на GitHub Releases (длительность 15 сек, тип Warning). Это no-op для нормальных версий — но если в будущем случится аналогичный баг, данный механизм автоматически предупредит пользователя с инструкцией.

### Заметки

- v3.40.0 бинарники, установленные конечными пользователями, **до сих пор не могут обновиться автоматически** — баг ронял auto-update flow, и патч для них доступен только через ручную замену EXE на v3.40.1 или v3.40.2 (`https://github.com/DdepRest/arc-frame/releases/download/v3.40.2/ARC-Frame-3.40.2-full.zip`).

---

## 3.40.1 — 2026-06-29

### Исправления

- **Ручная проверка обновлений не падала с `ResourceReferenceKeyNotFoundException`:** Storyboards `UpdateBarFadeIn` / `UpdateBarFadeOut` для прогресс-бара определены в `Grid.Resources` (внутри корневого `<Grid>`), а `MainWindow.OnUpdateProgressChanged` дёргал `this.FindResource(...)`. WPF-метод `FindResource` ходит ВВЕРХ по логическому дереву — ресурсы потомков невидимы. Storyboards перенесены в `<Window.Resources>` — теперь `FindResource("UpdateBarFadeIn")` находит их.
- **Defense-in-depth в `OnUpdateProgressChanged`:** заменён `FindResource(...).Clone()` (бросает `InvalidCastException` если ресурс не найден) на `TryFindResource(...) is Storyboard` — если ресурс когда-то «потеряется» (XAML-удаление, merge-конфликт), теперь видим debug-лог и бар показывается/скрывается без анимации, вместо краша.

### Заметки

- Баг существовал с v3.38.0 (когда был добавлен XAML-анимация UpdateDownloadBar); v3.40.0 его только «активировал», потому что публичный метод ручной проверки `CheckAndApplyAsync` теперь корректно прокидывал `isAutomatic` в диалог — пользователи увидели путь до краша.
- Pubished as patch release (3.40.0 → 3.40.1), не minor — поведение в остальном не менялось.

---

## 3.40.0 — 2026-06-29

### Улучшения

- **WinAPI idle detection:** `UpdateCheckScheduler` теперь использует WinAPI `GetLastInputInfo` для определения системного простоя вместо UI-событий (`PreviewMouseMove`/`PreviewKeyDown`). Это даёт более точное измерение реальной неактивности пользователя (не только в окне приложения, но и системно) и устраняет необходимость в `NotifyActivity()`.
- **Anti-recommend text в диалоге обновления:** При автоматическом обнаружении обновления (startup-check) кнопка «Отмена» переименована в «Отложить» с текстом «Не рекомендуется откладывать обновление надолго».
- **Minimized window guard:** Фоновая проверка `CheckInBackgroundAsync` теперь пропускает показ toast-уведомления, если главное окно свёрнуто — уведомление появится после восстановления окна.

### Техническое

- `UpdateCheckScheduler`: удалён `NotifyActivity()` и `_lastActivityTime`; добавлен `GetSystemIdleTime` callback (тип `Func<TimeSpan>`). Тесты переписаны на фейковый `FakeIdle` вместо `NotifyActivity`.
- `UpdateService`: добавлен P/Invoke `GetLastInputInfo` и публичный `GetIdleTime()`; убрана вся логика `IsCellEditing`, `CanShowUpdateDialog`, retry-таймеров и `OnWindowStateChanged` — сложность не нужна при toast-based UX.
- `MainWindow.xaml.cs`: удалены `PreviewMouseMove` и `PreviewKeyDown` вызовы `_updateCheckScheduler?.NotifyActivity()`.
- `DialogService.ShowUpdateAvailable`: добавлен параметр `isAutomatic` с anti-recommend UI.
- **16 тестов** в `UpdateCheckSchedulerTests.cs` обновлены/переписаны под новую модель idle-детекции.

---

## 3.39.0 — 2026-06-29

### Новинка

- **Background auto-update checks** (Variant A из спеки `update-notification-rework-spec.md`):
  - Periodic checks каждые 30 минут от последней проверки.
  - Idle checks после 10 минут простоя пользователя.
  - Anti-spam throttle: минимум 2 минуты между двумя реальными проверками.
  - Реализовано в новом `Services/UpdateCheckScheduler.cs` с pure-логикой `ShouldCheckAt(...)` для unit-тестов.
- **Persistent update notification toast** (`ToastService.ShowUpdateNotification`):
  - Показывается при фоновом обнаружении обновления взамен модального диалога.
  - Action-кнопки `Обновить` / `Позже`; не исчезает сама пока пользователь не выберет.
  - Click-обработчик корректно работает с `ToastCanvas.IsHitTestVisible="False"`: явно ставит `IsHitTestVisible = true` на toast Border (иначе кнопки молча не получают Click из-за Inheritable DP).
- `UpdateService.CheckInBackgroundAsync` — фоновая проверка, отличается от стартап-чека тем, что открывает persistent toast вместо модального диалога (стартап — модал по прежнему).
- Activity-tracking в `MainWindow.xaml.cs`: `PreviewMouseMove` и `PreviewKeyDown` сбрасывают idle-таймер scheduler'а.

### Техническое

- `UpdateService`: добавлено поле `_lastNotifiedVersion` (anti-spam в сессии для notification toast), сбрасывается на успешной установке и при ручном «Обновить» в плашке.
- `MainWindow.xaml.cs`: scheduler стартует в `Loaded`, останавливается в `Closed`.
- **20+ новых юнит-тестов** для `UpdateCheckScheduler.ShouldCheckAt` (`UpdateCheckSchedulerTests.cs`): edge-cases `>=`/`<=` gate, throttle/idle/periodic state-machine, activity-reset, `OnCheckDue` SafeInvoke contract.

---

## 3.38.0 — 2026-06-28

### Улучшения

- **UI-polish:** `CornerRadius` увеличен с 4 до 7 в `AdditionalKpsControl.xaml`; добавлен `CornerRadius` в `DataGridStyles.xaml` для ячеек и строк — более мягкий и единообразный внешний вид.
- **XAML-анимация UpdateDownloadBar:** fade-in (200 мс, CubicEase EaseOut) и fade-out (250 мс, CubicEase EaseIn) вынесены в `Storyboard` ресурсы `MainWindow.xaml` (баг #8). `From` для fade-out задаётся динамически из текущего `Opacity` — без визуальных скачков при прерывании.

### Исправления

- **Zero-byte download fix:** `DownloadWithProgressAsync` теперь корректно сообщает 100% прогресс при отсутствии `Content-Length` или нулевом размере ответа — раньше полоска оставалась на 0%.

### Техническое

- **UpdateService тестируемость:** `FetchManifestAsync` и `DownloadWithProgressAsync` теперь принимают опциональный `HttpClient?` — возможность инъекции моков для интеграционных тестов. Паттерн `ownsClient` гарантирует, что внешний `HttpClient` не будет `Dispose`'нут. Добавлено 7 интеграционных тестов (`UpdateServiceIntegrationTests.cs`).
- **Тесты:** 3 новых теста в `PrintServiceTests.cs` — проверка отображения расчётных размеров Anwis ББ 60 в КП, HTML-экранирование спецсимволов (`&`, `"`, `'`), и конвертация переводов строк в `<br/>` в примечаниях.

---

## 3.37.2 — 2026-06-27

### Исправления

- **SelectAll race fix:** обработчик `SelectAll_OnFocus` теперь синхронный (`tb.SelectAll()` без `BeginInvoke`) — при клике в ячейку Ширины/Высоты текст выделяется до первого нажатия клавиши, ввод заменяет значение, а не дописывает. Деталь см. `docs/arc/GOTCHAS.md#14`.
- **Mid-typing formula clamp fix:** Ширина и Высота переключены с `UpdateSourceTrigger=PropertyChanged` на `LostFocus` — формула Anwis (напр. `max(0, raw−30)`) больше не перехватывает значение на каждом нажатии, набор идёт свободно. Деталь см. `docs/arc/GOTCHAS.md#15`.
- **DeleteRowButton padding fix:** `Padding="4,0"` → `Padding="5"` — кнопка удаления строки теперь 20×20px, красный hover-фон с `CornerRadius=5` выглядит пропорционально, а не сплюснуто.

### Техническое

- **UpdateLog sort-in-code:** `AllNewestFirst()` и `GetChangesSince()` теперь сортируют записи по дате/версии в коде (`.OrderByDescending`/`.OrderBy`) — порядок записей в JSON-файле больше не имеет значения. Устраняет повторяющийся баг с ручным prepend'ом записей при релизе.
- **`ParseVersion` диагностика:** добавлен `Debug.WriteLine` при битой строке версии в JSON — опечатка видна в отладчике сразу, а не через молчаливый fallback.
- 2 регрессионных теста в `DataGridBindingsTests.cs` перепрофилированы: `PropertyChanged` → `LostFocus` для Ширины/Высоты.

---

## 3.37.1 — 2026-06-27

### Исправления

- **Монтаж с Quantity > 1:** в режимах «Без монтажа» и «В конструкцию» deduction теперь умножается на `Quantity` (per piece), а не списывается один раз. Например: Anwis 1000×1000 × 3 шт. × режим «В конструкцию» с вычетом 500 ₽ → итоговый вычет 1500 ₽ (а не 500 ₽). Backward-compat для Q=1. Деталь см. `docs/arc/GOTCHAS.md#12` и `CALCULATION_TEST_CASES.md` Case 16.
- **Уточнён tooltip монтажа** для режимов 1/2: теперь явно показывает `руб./шт. × Кол-во`, чтобы пользователь видел, что введённая сумма применяется за каждую штуку.
- **Автоширина колонки «Цена» в DataGrid:** колонка «Цена» в таблице «Расчёт» (и «Цена, руб.» в tab «Цены») не успевала расширяться при наборе — `Width="Auto"` рекомпьютил ширину по старому значению `Price` (binding имел `UpdateSourceTrigger="LostFocus"`). Теперь `Price` обновляется по `PropertyChanged`, и колонка подстраивается под набор в реальном времени. Деталь см. `docs/arc/GOTCHAS.md#13`.

### Техническое

- `InstallationToolTip` дополнен суффиксом `руб./шт. × Кол-во` для modes 1/2.
- `UpdateSourceTrigger` в `OrderItemsControl.xaml` (Цена) и `PricesControl.xaml` (Цена, руб.) переключён с `LostFocus` на `PropertyChanged`.
- Добавлено 8 новых юнит-тестов на `TotalWithDeduction × Quantity` (включая linear-scaling теорию Q∈{1,2,5}, clamp-to-0 для высокого Q, regression для «В конструкцию» с Q=3).
- Добавлен `DataGridBindingsTests.cs` (5 regression-тестов через grep XAML-binding-triggers) — guardrails на правильный `UpdateSourceTrigger` для всех редактируемых колонок с `Width="Auto"`.

---

## 3.37.0 — 2026-06-27

### Для пользователей

- **Обновление уведомлений — полный rework:** диалог «Доступно обновление» теперь показывает список изменений (changelog) с момента установленной версии. TitleBar получил плавно появляющуюся/исчезающую полоску прогресса скачивания (fade-in 200 мс / fade-out 250 мс). Убран старый прогресс-панель из ActionBar.
- **Toast-фильтрация:** при автоматической проверке обновлений при старте программы не показываются тосты «Обновлений нет» — только при ручной проверке через меню.

### Техническое

- **UpdateService:** логика проверки версии извлечена в чистый `internal static GetAvailableUpdate` — тестируемая без UI-зависимостей. Добавлен флаг `isAutomatic` для различения авто/ручной проверки.
- **8 новых юнит-тестов** для `UpdateService` (6 на `GetAvailableUpdate` + 2 на `HasPendingUpdate`).
- **A.R.C. v4** — SYMBOL_INDEX.md (60 классов, 16 модулей), INTENTS.md (routing фраз на файлы), `gensymbols.ps1`, `arc-check.ps1`.

---

## 3.36.2 — 2026-06-25

### Улучшения

- **Брус, Пояс, Доставка — только сумма:** для этих товаров скрыты колонки «Кол-во», «Площ./Дл.» в таблице расчёта и отключены поля «Кол-во», «Ширина», «Высота» в панели быстрого ввода. Превью показывает только цену.

### Исправления

- Исправлена кодировка UTF-8 BOM в PowerShell-скриптах (`validate-docs.ps1`, `what-to-update.ps1`, `render-matrix.ps1`, `generate-update-log.ps1`) — кириллица в regex парсилась некорректно.
- Восстановлен `update-log.json` из git (был обрезан до 3 записей); добавлена запись 3.36.1.
- Исправлено 6 ошибок в `UpdateLogTests` и `ManualChecklistTests`.

---

## 3.36.1 — 2026-06-24

### Техническое

- **A.R.C. upgrade v3** — полная автоматизация документирования (0 ручных операций для агента):
  - `docs/arc/documentation-matrix.json` — машиночитаемый источник матрицы «файл → документы».
  - `what-to-update.ps1` — скрипт: принимает `git diff --name-only`, выводит список docs/arc файлов к обновлению. Фаза Document стала 100% механической.
  - `generate-update-log.ps1` — автогенерация `update-log.json` из `CHANGELOG.md`. Убирает ручную синхронизацию при релизе.
  - `render-matrix.ps1` — генерация `DOCUMENTATION_MATRIX.md` из JSON.
  - `validate-docs.ps1` расширен до 8 проверок: git-based Last verified (check 7), staleness detection (check 8).
  - `PROMPTS.md` — добавлен Prompt 7 (self-check после изменений: git diff → what-to-update → validate-docs).
  - `AGENTS.md` — добавлен inline routing (таблица в самом wrapper'е — агент может не читать CHEATSHEET для routing).
  - `CHEATSHEET.md` — добавлено правило #17 (what-to-update → validate-docs) и секция «Инструменты автоматизации».

- **A.R.C. upgrade v2** — CHEATSHEET.md, DOCUMENTATION_MATRIX.md, PROMPTS.md, гранулярный routing, validate-docs.ps1.

---

## 3.36.0 — 2026-06-24

### Для пользователей

- **Копирование заказов:** добавлен пункт «Копировать» в контекстном меню списка «Заказы». Создаёт полную копию заказа с новым номером (например, «2-8» → «2-8.1»), статусом «Новый» и актуальной датой.

### Исправления

- Исправлен XML-комментарий `InstallationSurcharge` в `OrderItem.Installation.cs`: `Default 0 ₽` → `Default 500 ₽` (соответствует коду).
- Исправлен pre-existing дрифт версии в тесте `UpdateLogTests.AllNewestFirst_FirstItemIsNewest` (ожидал 3.34.5, теперь 3.35.0).

### Техническое

- **GenerateCopyContractNumber** перенесён из `MainWindow.Orders.cs` в `OrderStorageService.cs`.
- Добавлено **11 тестов**.
- Инициализирована система A.R.C.
- **Multi-agent control: portability migration.**

---

## 3.35.0 — 2026-06-23

### Исправления

- Полный фикс утечки формул Anwis на не-Anwis товары.

---

*Полная история релизов доступна в `releases.json` и `MosquitoNetCalculator/Resources/update-log.json`.*
