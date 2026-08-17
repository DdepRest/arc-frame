# CURRENT_STATE.md

## Что сейчас выглядит рабочим

- **Активный план системного рефакторинга (2026-07-12):** зафиксирован baseline, создан детальный план по 7 фазам в `REFACTORING_PLAN.md`.
  - **Фаза 1 завершена (2026-07-12):** `MainWindow.xaml.cs` 1051→760 строк (−28%). Выделены 4 сервиса: `NavigationService`, `OverlayManager`, `SlopeOverlayCoordinator`, `SlopesProUpsellGate`. +26 тестов. **932/932 tests pass.** Бизнес-логика не затронута.
  - **Фаза 2 завершена (2026-07-12):** `UpdateService.cs` 910→608 строк (−33%). Выделены 5 компонентов: `VersionResolver`, `IdleDetector`, `UpdateVerifier`, `UpdateManifestClient`, `UpdateDownloader`. +67 тестов. **999/999 tests pass.** Бизнес-логика не затронута.
  - **Фаза 3 завершена (2026-07-12):** `PrintService.cs` 632→81 строк (−87%). Выделены 6 компонентов: `DrawingService`, `FlowDocumentBuilder`, `FixedDocumentBuilder`, `PrintQueueManager`, `PdfExportService`, плюс модели `PageMode`/`PrintSettings`/`PrintResult`. +~40 тестов. **1038/1038 tests pass.** Бизнес-логика не затронута.
  - **Фаза 4 завершена (2026-07-12):** `DialogService.cs` 641→~250 строк (−61%). Созданы XAML-шаблоны диалогов и fluent-builder. +10 тестов. **1071/1071 tests pass.** Бизнес-логика и public API не затронуты.
  - **Фаза 5 завершена (2026-07-13):** `OrderItem.cs` 651→~520 строк (−20%). Выделены 3 компонента: `ProductCatalog`, `AnwisSizeCalculator`, `SlopeCalculationExtensions`. +21 тест. **1133/1133 tests pass.** Бизнес-логика и public API не затронуты.
  - **Фаза 6 завершена (2026-07-15):** `MainWindow.Orders.cs` 527→226 строк (−57%). Выделены 3 компонента: `OrderGridPresenter`, `OrderImportExportService`, `ChangeOrderStatusWindow`. +27 тестов. **1179/1179 tests pass** (baseline; +16 тестов в post-QA bugfixes довели итог до 1195/1195). Бизнес-логика, JSON-контракт `OrderData`, печатное КП, автообновление — без изменений.
  - Цель — устранить God-classes и высокий coupling в `MainWindow.xaml.cs`, `UpdateService.cs`, `PrintService.cs`, `DialogService.cs`, `OrderItem.cs`, `MainWindow.Orders.cs`. Бизнес-логика не трогается.

- Все основные функции расчёта работают стабильно.
- Печать КП, отправка на завод, сохранение заказов — функционируют.
- **Сетевые принтеры:** список выбора и отправка используют один и тот же экземпляр `PrintQueue`, перечисленный через `Local + Connections`; выбранная очередь больше не пересоздаётся по строковому имени перед печатью. Это сохраняет UNC/серверный контекст локального сетевого принтера и исключает подмену на физический USB-принтер. Для явно выбранной очереди fallback на default запрещён; default используется только при отсутствии выбора, неизвестное имя возвращает `null`.
- Автообновление через GitHub Releases настроено и работает (собственный механизм через watchdog .bat).
- Тёмная тема стабильна, переключается без потери данных.
- Undo/Redo работает для позиций расчёта и Доп.КП.
- Юнит-тесты покрывают ключевые сценарии (расчёты, экспорт/импорт, версия, обновления).
- **Текущая версия: 3.47.4** (релиз 2026-08-08: исправлена печать на сетевой принтер и переключатель +/- в режиме «Монтаж включён»).
- **Unreleased AI/navigation UX:** левая навигация раскрыта по умолчанию и сворачивается только явной кнопкой; встроенные AI-ключи не показываются в UI; каталоги моделей OpenRouter и NVIDIA загружаются параллельно, обновляются при открытом окне и синхронизируют добавленные/удалённые модели с сохранённым выбором.
- **AI-раздел временно закрыт для действий:** пункт «AI Ассистент» открывает существующую панель только для просмотра с блокирующей плашкой «В РАЗРАБОТКЕ»; ввод, отправка, очистка и плановые действия перехватываются overlay. Пункт «AI Ассистент — API ключ…» остаётся в меню, но отключён и не открывает `AiApiKeyDialog`; расчётная логика не затронута.
- **AI UI без технических меток:** при открытии встроенной или пристыкованной AI-панели скрыты название текущей модели, провайдер, badge фактически использованной модели и строка телеметрии запроса; внутренние свойства/маршрутизация не менялись.
- **AI streaming UX:** сетевые SSE-ответы читаются в фоне, чанки буферизуются и публикуются в UI не чаще 20 раз/с, чтобы очередь WPF не переполнялась; пустой пузырь сразу показывает анимированный статус «Думает…» до первого фрагмента; внутренние метки модели/провайдера и телеметрии не показываются в UI; кнопка AI и заголовок панели отмечены `BETA`.
- **AI clarification form:** когда AI отвечает уточнением вместо выполнения команды («Сделай сетку» → «Уточните: тип, размеры…»), в пузыре ответа появляется интерактивная карточка «Заполните параметры» (`AiClarificationForm`, runtime-only, не сериализуется) — выпадающие списки типа/цвета/режима Anwis/монтажа, поля размеров и количества; «Добавить в расчёт» строит AddItem-команду без повторного запроса к LLM (`AiAssistantViewModel.SubmitClarificationForm`). Список «Тип» фильтруется по запросу: «сделай сетку» → только сеточные изделия (`FilterProductsForRequest`).
- **AI streaming reliability:** каждая модель ретраится до 3 раз на транзиентных ошибках (429/5xx/сеть/пустой поток) с паузой, в цепочку фолбэка всегда добавляется бесплатная NVIDIA-модель, даже если выбраны только OpenRouter; финальная ошибка нейтральна к провайдеру (`MaxAttemptsPerModel`, `RetryDelayMs`, `EnsureNvidiaFallback`).
- **AI Agent Mode (Unreleased):** AI переведён с прямых мутаций заказа на безопасный конвейер «план → проверка → предпросмотр → подтверждение → атомарное выполнение → Undo». Новые модули: `AiActionPlan`/`AiActionStep` (статусы, `PlanId`/`MessageId`/`RequestId`), `AiPlanBuilder`/`AiPlanValidator`/`AiPlanExecutor` (атомарный пакет с rollback), `AiLocalCommandRouter` (slash-команды без LLM: `/товары /цены /итоги /статус /последняя /отменить /повторить /очистить /объясни`), `AiOrderContextBuilder` (богатый контекст заказа), `AiExplanationContextBuilder` (`/объясни` по фактическим итогам), `AiTelemetryService` (метрики запросов), plan-mode JSON в `AiCommandParser`, карточка плана с «Выполнить/Отмена» и «Отменить действие» в `AiAssistantControl.xaml`, guarded undo в `MainWindow.AI.cs` (один снимок на пакет, защита от отката поверх ручных правок).
- **Исправлен краш AI Agent Mode:** `DialogOutlineButton`/`DialogPrimaryButton` перенесены из локальных ресурсов `AiApiKeyDialog` в общий `Themes/ButtonStyles.xaml` — карточка плана в `AiAssistantControl` больше не падает с `XamlParseException: StaticResourceHolder`. Добавлен регрессионный тест разрешимости StaticResource-ключей.
- **Админ-панель (Unreleased):** пункт левой навигации **в самом низу списка** (отделён тонкой линией от основной навигации — явно показывает, что это тех-раздел, а не основной режим работы); виден всем офисам, вход по ВШИТОМУ паролю `AppSettingsService.EmbeddedAdminPassword` — одинаковый во всех офисах, ничего настраивать не нужно. Контейнер секций (вкладки): **«Обновления»** (статусы версий) и **«Статистика»** (кол-во заказов в программе каждого офиса) — новые секции добавляются как TabItem + свой список строк без изменения канала. UX: статусные **иконки (✓/⚠/❓) + цвет** в бейдже (`OfficeStatusRow.StatusGlyph`); **клик на устаревший офис** копирует в буфер напоминание («Здравствуйте! Поставьте vX.Y.Z — {download URL}») и показывает Success-тост; **большие сводные карточки** на вкладках с цифрой «3/5» и «36» (заказов); **пустые состояния** с пиктограммами если отчётов ещё нет; **авто-рефреш раз в 2 ч**, пока панель открыта (DispatcherTimer, останавливается при закрытии оверлея), в шапке видно «обновлено HH:mm · авто каждые 2 ч» (кнопка «Обновить» — мгновенно по требованию). **Живые данные весь день БЕЗ админки — редкие считки:** отчёт отправляется при запуске программы (`MainWindow_Loaded`) и каждые 2 часа планировщиком (`OfficeReportScheduler`, контракт как у UpdateCheckScheduler); ручная «Проверить обновления» тоже шлёт отчёт (`TitleBarControl`). Фоновая 30-мин проверка обновлений отчёт НЕ отправляет — автоматическая считка не чаще «запуск + раз в 2 ч». При открытой панели она обновляется по тому же редкому таймеру, без мигания UI. **Несколько устройств в одном офисе:** каждая программа имеет стабильный `deviceId` (GUID в settings.json, `AppSettingsService.LoadOrCreateDeviceId`) и тихо обновляет СВОЙ файл `office-{prefix}-{deviceId}.json` в секретном GitHub Gist (`OfficeReportService`, PATCH /gists/{id} с Bearer-токеном): версия, время, `deviceName` (имя ПК), `orderCount` (файлы заказов в %AppData%) — ПК офиса не перетирают друг друга, легаси-файлы `office-{prefix}.json` читаются как одно устройство. Панель читает весь gist, сравнивает с последним релизом (releases.json) и показывает статусы: Актуальна / Устарела / Нет данных (порог свежести 72ч, `OfficeStatusCalculator`, статус офиса агрегируется по свежим устройствам: есть хоть одно устаревшее → «Устарела»). В карточке офиса — чип «N устройств» + чипы устройств `имя ПК · vX.Y.Z ✓/⚠/❓` (тултип: статус + последний отчёт), сводка «X из N устройств актуальны»; статистика суммирует `orderCount` по всем устройствам офиса (`OfficeStatsCalculator`, чистая логика, покрыта тестами). **Один ПК = одно устройство:** отчёты дедуплицируются по имени машины (`OfficeDeviceGrouping.DistinctDevices`) — если на компьютере запущены две копии программы (обычная + dev) или при гонке первой генерации ID, панель не считает его дважды; легаси-записи `office-{prefix}.json` отбрасываются, когда у офиса есть именованные устройства. Генерация `deviceId` кросс-процессно безопасна (атомарный файл `device-id` рядом с settings.json). **Кнопка «Очистить дубли»** в шапке панели удаляет из gist лишние файлы устройств (PATCH с `content: null`): остаются только новейшие файлы каждого ПК, легаси-файлы при наличии именованных удаляются — gist приводится к тому, что показывает панель (`OfficeReportService.CleanupDuplicatesAsync`, чистый расчёт `ComputeDuplicateFilesToDelete`, покрыт тестами). Для напоминания об обновлении панель хранит версию и download URL из манифеста (`LatestManifestResult`); клик по устаревшему офису адресует конкретное устаревшее устройство. Токен встраивается при СБОРКЕ, а не в git: csproj-target `GenerateOfficeReportToken` берёт его из env `OFFICE_REPORT_TOKEN` или локального gitignored-файла `MosquitoNetCalculator/.office-report-token` (репозиторий публичный — токен не должен попадать в историю); переопределение через settings.json для отладки/тестов. Gist: https://gist.github.com/DdepRest/6d6ff7389efc44f7aa6e57361a55ee24. Новые/изменённые файлы: `Models/OfficeReport.cs` (+`deviceId`/`deviceName`), `Models/OfficeDeviceRow.cs`, `Models/OfficeStatusRow.cs` (+`DeviceCount`/`Devices`), `Models/OfficeStatsRow.cs` (+`DeviceCount`), `Services/OfficeReportService.cs`, `Services/OfficeReportScheduler.cs`, `Services/OfficeStatusCalculator.cs`, `Services/OfficeStatsCalculator.cs`, `Services/OfficeDeviceGrouping.cs` (дедупликация по имени машины), `Services/AppSettingsService.cs` (+`LoadOrCreateDeviceId`, кросс-процессный файл `device-id`), `Controls/AdminPanelControl.*`, `Controls/AdminPasswordWindow.*`, `Converters/OfficeStatusConverters.cs`, `MainWindow.xaml(.cs)` (навигация: админ-панель в самом низу, разделитель; `StartAsync`/`StopAutoRefresh` панели; стартовая отправка отчёта), `Controls/TitleBarControl.xaml.cs` (отчёт при ручной проверке обновлений).
- **Текущий статус тестов: 1602/1602 pass** (+8 к 1594: очистка дублей — расчёт 4, сетевой PATCH 3, парсинг с именами файлов 1).
- **Знак суммы монтажа исправлен:** в popup «Сумма» можно выбрать «−» при нулевой сумме и затем ввести значение; явно введённое `-500` также сохраняется как отрицательная корректировка. Формулы и модель расчёта не менялись.

> Полная история изменений по версиям — в [`CHANGELOG.md`](../../CHANGELOG.md).

## Статус A.R.C.

✅ A.R.C. создан.
✅ Структура соответствует multi-agent архитектуре.
✅ Аудит прошёл — утверждения в `CALCULATION_LOGIC.md`, `GOTCHAS.md`, `RELEASE_PROCESS.md`, `AUTO_UPDATE.md` проверены по исходному коду и тестам.
✅ Созданы эталонные расчётные кейсы в `CALCULATION_TEST_CASES.md` (с явными статусами).
✅ Термины по размерам (введённые / расчётные / заводские / в КП) разведены однозначно.
✅ Правило безопасного порядка публикации `releases.json` зафиксировано в `RELEASE_PROCESS.md` и `AUTO_UPDATE.md`.
✅ Multi-agent master-файл перенесён в репозиторий (`MULTI_AGENT_ARC_CALC_CONTROL.md`) — source of truth версионируется.
✅ Wrappers (`AGENT.md`, `AGENTS.md`, `CLAUDE.md`, `GEMINI.md`) — тонкие redirect-файлы.
✅ **Все расчётные кейсы (1–15) подтверждены владельцем 2026-06-24.**
✅ **A.R.C. upgrade v2 (2026-06-24):** CHEATSHEET.md, DOCUMENTATION_MATRIX.md, PROMPTS.md, validate-docs.ps1, гранулярный routing, token-aware severity levels.
✅ **A.R.C. upgrade v4 (2026-06-25):** SYMBOL_INDEX.md, INTENTS.md, gensymbols.ps1, arc-check.ps1.
✅ **A.R.C. upgrade v5 (2026-08-03):** добавлена обязанность самоподдержания (CONTROL §13): агент может и должен обновлять AGENTS.md/agents/docs при неактуальности; ситуации А–Ж (разбивка/переименование/новые классы/сигнатуры/бизнес-логика/процессы/сама система) → какие файлы обновлять; метрики экономии в шаблоне отчёта; routing-строка «Структурные изменения» в CHEATSHEET; мягкая проверка устаревания версии в validate-docs.ps1 (#10). ТЗ: `ТЗ_самоподдержание_AGENTS_2026-08-03.md`.

✅ `MULTI_AGENT_ARC_CALC_CONTROL.md` переработан: убрано дублирование, добавлены ссылки на новые файлы, расширена routing-таблица (12 категорий вместо 2).

## Архитектура multi-agent control

```text
MULTI_AGENT_ARC_CALC_CONTROL.md
    = canonical source of truth (версионируется в репозитории)

CHEATSHEET.md
    = быстрый вход (критические правила + routing, 40 строк)

DOCUMENTATION_MATRIX.md
    = механическая карта «поменял файл → обнови документы»

PROMPTS.md
    = готовые prompt-шаблоны для типовых сценариев

CURRENT_STATE.md
CALCULATION_LOGIC.md
CALCULATION_TEST_CASES.md
GOTCHAS.md
MODULES.md
DECISIONS.md
PROJECT_OVERVIEW.md
RELEASE_PROCESS.md
AUTO_UPDATE.md
    = проектная память

agents/scripts/validate-docs.ps1
    = автоматическая валидация консистентности документации

.claude/skills/MULTI_AGENT_ARC_CALC_CONTROL.md
    = external bootstrap loader (только для Claude-среды)

AGENT.md / AGENTS.md / CLAUDE.md / GEMINI.md
    = thin compatibility wrappers
```

## Порядок входа агента (token-optimised)

1. `CHEATSHEET.md` — 40 строк, 15 секунд, критические правила + routing-таблица.
2. `CURRENT_STATE.md` — текущее состояние.
3. Routing-таблица в `CHEATSHEET.md` → релевантные полные файлы (2-3 вместо 5+).
4. На фазе Document → `DOCUMENTATION_MATRIX.md` (механическое обновление docs).
5. Валидация → `agents/scripts/validate-docs.ps1`.

## Что выглядит незавершённым

- ✅ `README.md` в корне обновлён: добавлено описание проекта, инструкции по сборке, тестированию и плейсхолдеры для скриншотов.
- ✅ `PROJECT_OVERVIEW.md` синхронизирован с текущим стеком: убрана устаревшая зависимость `Microsoft.Web.WebView2`, оставлена `QuestPDF` для PDF-экспорта.
- `releases.json` и `update-log.json` дублируют частично одну информацию — нужна синхронизация при каждом релизе (рассмотреть консолидацию).
- Нет автоматической проверки калькуляции при релизе — только юнит-тесты.
- Редизайн системы обновлений частично завершён (core-логика + тесты готовы, UI-полировка в процессе). Спецификация: `update-notification-rework-spec.md`.
- **HeadlessWpf behavior-тест для «Цена автоширины» отменён** (6 итераций не дали рабочего решения). XAML-grep test в `DataGridBindingsTests.cs` остаётся канонической гарантией. Для возврата к этой фиче требуется либо project-wide `[CollectionFixture<WpfAppFixture>]`, либо переход на `Border + TextBlock + Width=NaN` без DataGrid.

## Открытые вопросы

- Нужно ли добавить новые товары или изменить цены? (Только по явному запросу владельца.)
- Нужно ли улучшить механизм автообновления?
- ✅ CI/CD настроен через GitHub Actions (`.github/workflows/ci.yml` и `.github/workflows/release.yml`): build + test на push/PR, автоматическая публикация релиза и обновление `releases.json` по тегу `vX.Y.Z`.
- Нужна ли консолидация CHANGELOG.md ↔ update-log.json?

## Рекомендуемые следующие шаги

1. Запустить `agents/scripts/validate-docs.ps1` и исправить найденные расхождения.
2. Обновить `README.md` (низкий приоритет).
3. Настроить CI/CD (GitHub Actions) для автоматической сборки и публикации релизов.
4. Консолидировать CHANGELOG.md и update-log.json (один source of truth).

## Source files

- `MosquitoNetCalculator/MosquitoNetCalculator.csproj` — версия 3.47.3.
- `releases.json` — история релизов (latest заполняется после GitHub Release + ZIP).
- `MosquitoNetCalculator/Resources/update-log.json` — история для UI.
- `agents/docs/*.md` — вся проектная документация.
- `agents/scripts/gensymbols.ps1` — сканирует .cs → SYMBOL_INDEX.md.
- `agents/scripts/what-to-update.ps1` — git diff → docs к обновлению.
- `agents/scripts/validate-docs.ps1` — 11 проверок консистентности.
- `tools/release/generate-update-log.ps1` — CHANGELOG → update-log.
- `agents/scripts/render-matrix.ps1` — JSON → DOCUMENTATION_MATRIX.md.
- `agents/scripts/arc-check.ps1` — pre-commit проверка docs.
- `agents/scripts/sync-version.ps1` — версия из csproj → `Last verified`.

## Last verified

2026-08-17 — **Миграция системы в `agents/`:** `docs/arc/*` → `agents/docs/`, скрипты → `agents/scripts/`. AGENTS.md — чистый wrapper на `agents/README.md`. Внутренние ссылки — короткие имена. Все 11 проверок валидатора — 0 issues.

2026-08-10 — **A.R.C. улучшения II (v3.47.4):** добавлена жёсткая проверка #11 в validate-docs.ps1 (битые ссылки `CONTROL#N` резолвятся в реальные секции CONTROL); новая секция «AI Agent Mode» в INTENTS.md (mapping намерений на AI-файлы); новый скрипт `sync-version.ps1` — версия берётся из csproj и автоматически синхронизируется в `## Last verified` всех agents/docs, убирая ручную правку ~9 файлов при релизе; правило разбиения больших файлов (CONTROL §13 п.7, CHEATSHEET #21) — файл > 400–500 строк разбивается в том же цикле, а не «на потом». Синхронизированы версии 14 agents/docs с v3.47.4.

2026-08-08 — **v3.47.4:** исправлена печать на сетевой принтер из локальной сети; добавлен переключатель +/- в режиме «Монтаж включён». Полный набор тестов: **1527/1527 pass**.

2026-08-06 — Исправлено разрешение подключённых сетевых принтеров: список выбора и отправка теперь используют один и тот же экземпляр `PrintQueue` из `Local + Connections`, без повторного разрешения по имени; это исключает подмену выбранной сетевой очереди физическим USB-принтером. Для явно выбранного устройства fallback на default запрещён. AI-раздел переведён в preview-only режим до завершения разработки: блокирующая UI-плашка «В РАЗРАБОТКЕ», API-key menu item disabled/no-op. Дополнительно скрыты модель/провайдер/телеметрия из AI-панелей. Также стабилизирован flaky-тест прогресса автообновления синхронным `IProgress<int>`; production-код не менялся. Расчётная логика не затронута; целевые проверки печати проходят, `UpdateDownloaderTests` — **13/13**. Дополнительно синхронизированы даты «Last verified» в `agents/docs/*.md` с git-историей (CONTROL#13) — 7 файлов; код не менялся.

2026-08-08 — Исправлен ввод знака в popup монтажа: «−» больше не сбрасывается при нулевой сумме, а явный отрицательный ввод принимается; добавлена фильтрация нечисловых/бесконечных значений. Формулы `TotalWithDeduction` и расчётная модель не изменялись. Полный набор: **1515/1515 pass**.

2026-08-04 — **AI Agent Mode:** реализован безопасный конвейер выполнения AI-действий через план с подтверждением, атомарным применением и отменой; slash-команды без LLM; богатый контекст заказа; `/объясни`; защита от двойного выполнения; golden-тесты. Сборка и тесты: **1497/1497 pass** (+70 новых тестов). Также исправлен баг `gensymbols.ps1` (символ `g` в формате даты интерпретировался как спецификатор эры).

2026-08-04 — **Unreleased AI UX:** добавлена интерактивная карточка уточнения параметров в AI-чате — когда AI просит уточнить тип/размеры/цвет/режим/монтаж, пользователь заполняет форму галочками и выпадающими списками, «Добавить в расчёт» создаёт AddItem без повторного запроса к LLM (`AiClarificationForm`, `SubmitClarificationForm`, XAML-карточка в `AiAssistantControl.xaml`); список «Тип» фильтруется по запросу («сетки» → только сеточные изделия). Надёжность: ретраи до 3 раз на транзиентные ошибки + гарантированный фолбэк на бесплатную NVIDIA-модель. Сборка и тесты: **1413/1413 pass**.

2026-08-03 — **Unreleased AI UX:** исправлено зависание интерфейса при потоковом ответе AI буферизацией чанков и фоновым чтением сети; добавлены анимированный статус «Думает…» до первого токена, корректный приоритет task-ranked модели в автовыборе, исправлена обрезка кнопок настроек, добавлены метки фактически использованной модели/провайдера, `BETA` на AI-навигации и улучшены подсказки автовыбора. Сборка и тесты: **1388/1388 pass**.

2026-08-03 — **v3.47.3 (самоподдержание A.R.C.):** добавлены CONTROL §13 (обязанность самоподдержания, ситуации А–Ж), правила 18–20 в CHEATSHEET, routing «Структурные изменения», мягкая проверка #10 в validate-docs.ps1 (устаревшая версия в Last verified). Зафиксирован baseline: валидатор до правок давал 1 issue (версия 3.47.2 ≠ csproj 3.47.3) + 20 warnings (устаревшие даты/версии в 10+ файлах). Тест-драйв SYMBOL_INDEX: класс найден за 1 grep-шаг. ТЗ согласовано владельцем.

2026-07-22 — **v3.47.3 (URGENT + refactor):** В старых заказах после system V X B в Отлив/Козырёк, X или B показывали «0 ₽». Root cause: pre-v3.47.0 saved JSON содержал DTO defaults (-500/-500/0); v3.47.0 per-linear-meter formula умножала (-500) × linearMeters × Q, превышая Total и зажимая в 0. Fix: расширил `CalculationViewModel.LoadFromOrderData` строгим `isLegacyLoad` детектором + исключил Отлив/Козырёк из v3.46.1 sign-flip migration. Refactor: извлек `ProductCatalog.PerLinearMeterProducts` HashSet + `IsPerLinearMeter(string?)` helper как single source of truth — убрал 4 дублирования name-string чеков в `CalculationViewModel.cs`; broadened heuristic до `Math.Abs(Math.Abs(x)-500) < 0.01` для поддержки обоих conventions (±500). Файлы: `ViewModels/CalculationViewModel.cs`, `Models/ProductCatalog.cs`, `Models/OrderItem.Installation.cs`. **Тесты: 1253/1253 pass** (+4 регрессионных тестов в `CalculationViewModelTests.cs`). Зафиксировано в `GOTCHAS.md#16` как правило для будущих per-linear-meter добавлений.

2026-07-20 — **v3.47.1:** Portable ZIP для ручного обновления (create-manual-update.ps1 + README_ОБНОВЛЕНИЕ.txt). Те же фичи: монтаж для Отлива/Козырька (500/750 ₽/м.п., по умолчанию выключен), дробное количество в заказе, убран цветной фон SignToggleCheckBox в тёмной теме. Тесты: 1234/1234 pass.
- **AGENTS.md:** добавлены секции «Wrapper contract» и «Last verified».
- **PdfExportService.BuildAdditionalKpPdf:** динамическая ширина ConstantItem через `ComputeAmountColumnWidth` + `MeasureTextWidthPt` (`Graphics.PageUnit=Point` для корректного DPI-преобразования).
- **PdfExportService.AddClientRowPdf + FlowDocumentBuilder.AddClientGridRow:** значения в клиентском блоке КП теперь `SemiBold` (жирные), не только лейблы.
- **Тесты:** +7 юнит-тестов на хелперы измерения ширины колонки (`PdfExportServiceTests.MeasureTextWidthPt_*` / `ComputeAmountColumnWidth_*`).
