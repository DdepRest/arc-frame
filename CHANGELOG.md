# Changelog

## Unreleased — готовится к следующему релизу

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
