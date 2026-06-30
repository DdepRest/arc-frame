# CURRENT_STATE.md

## Что сейчас выглядит рабочим

- Все основные функции расчёта работают стабильно.
- Печать КП, отправка на завод, сохранение заказов — функционируют.
- Автообновление через GitHub Releases настроено и работает (перешли с Velopack Flow на собственный механизм через watchdog .bat).
- Тёмная тема стабильна, переключается без потери данных.
- Undo/Redo работает для позиций расчёта и Доп.КП.
- Юнит-тесты покрывают ключевые сценарии (расчёты, экспорт/импорт, версия, обновления).
- Текущая версия: **3.40.4** (публикуется).
- Последние изменения: UpdateService DI для тестирования, zero-byte download fix, XAML-анимация UpdateDownloadBar, UI-polish (CornerRadius), новые интеграционные и unit-тесты, исправления документации.
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

- `MosquitoNetCalculator/MosquitoNetCalculator.csproj` — версия 3.40.3.
- `releases.json` — история релизов.
- `MosquitoNetCalculator/Resources/update-log.json` — история для UI.
- `docs/arc/*.md` — вся проектная документация.
- `docs/arc/documentation-matrix.json` — машиночитаемый источник матрицы.
- `what-to-update.ps1` — git diff -> docs к обновлению.
- `validate-docs.ps1` — 8 проверок консистентности.
- `generate-update-log.ps1` — CHANGELOG -> update-log.
- `render-matrix.ps1` — JSON -> DOCUMENTATION_MATRIX.md.

## Last verified

2026-06-30 (v3.40.4 публикуется: «Отлив» opt-in + Антикошка финальный + декомпозиция Phase 1+2. 742/742 tests pass.)
