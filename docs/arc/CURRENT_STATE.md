# CURRENT_STATE.md

## Что сейчас выглядит рабочим

- **Активный план системного рефакторинга (2026-07-12):** зафиксирован baseline, создан детальный план по 7 фазам в `docs/arc/REFACTORING_PLAN.md`.
  - **Фаза 1 завершена (2026-07-12):** `MainWindow.xaml.cs` 1051→760 строк (−28%). Выделены 4 сервиса: `NavigationService`, `OverlayManager`, `SlopeOverlayCoordinator`, `SlopesProUpsellGate`. +26 тестов. **932/932 tests pass.** Бизнес-логика не затронута.
  - **Фаза 2 завершена (2026-07-12):** `UpdateService.cs` 910→608 строк (−33%). Выделены 5 компонентов: `VersionResolver`, `IdleDetector`, `UpdateVerifier`, `UpdateManifestClient`, `UpdateDownloader`. +67 тестов. **999/999 tests pass.** Бизнес-логика не затронута.
  - **Фаза 3 завершена (2026-07-12):** `PrintService.cs` 632→81 строк (−87%). Выделены 6 компонентов: `DrawingService`, `FlowDocumentBuilder`, `FixedDocumentBuilder`, `PrintQueueManager`, `PdfExportService`, плюс модели `PageMode`/`PrintSettings`/`PrintResult`. +~40 тестов. **1038/1038 tests pass.** Бизнес-логика не затронута.
  - **Фаза 4 завершена (2026-07-12):** `DialogService.cs` 641→~250 строк (−61%). Созданы XAML-шаблоны диалогов и fluent-builder. +10 тестов. **1071/1071 tests pass.** Бизнес-логика и public API не затронуты.
  - **Фаза 5 завершена (2026-07-13):** `OrderItem.cs` 651→~520 строк (−20%). Выделены 3 компонента: `ProductCatalog`, `AnwisSizeCalculator`, `SlopeCalculationExtensions`. +21 тест. **1133/1133 tests pass.** Бизнес-логика и public API не затронуты.
  - **Фаза 6 завершена (2026-07-15):** `MainWindow.Orders.cs` 527→226 строк (−57%). Выделены 3 компонента: `OrderGridPresenter`, `OrderImportExportService`, `ChangeOrderStatusWindow`. +27 тестов. **1179/1179 tests pass** (baseline; +16 тестов в post-QA bugfixes довели итог до 1195/1195). Бизнес-логика, JSON-контракт `OrderData`, печатное КП, автообновление — без изменений.
  - Цель — устранить God-classes и высокий coupling в `MainWindow.xaml.cs`, `UpdateService.cs`, `PrintService.cs`, `DialogService.cs`, `OrderItem.cs`, `MainWindow.Orders.cs`. Бизнес-логика не трогается.

- Все основные функции расчёта работают стабильно.
- Печать КП, отправка на завод, сохранение заказов — функционируют.
- Автообновление через GitHub Releases настроено и работает (собственный механизм через watchdog .bat).
- Тёмная тема стабильна, переключается без потери данных.
- Undo/Redo работает для позиций расчёта и Доп.КП.
- Юнит-тесты покрывают ключевые сценарии (расчёты, экспорт/импорт, версия, обновления).
- **Текущая версия: 3.46.1** (релиз 2026-07-17: переключатель +/- для монтажа, улучшенные примечания в КП).
- **Текущий статус тестов: 1227/1227 pass.**

> Полная история изменений по версиям — в [`CHANGELOG.md`](../../CHANGELOG.md).

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

## Что выглядит незавершённым

- ✅ `README.md` в корне обновлён: добавлено описание проекта, инструкции по сборке, тестированию и плейсхолдеры для скриншотов.
- ✅ `docs/arc/PROJECT_OVERVIEW.md` синхронизирован с текущим стеком: убрана устаревшая зависимость `Microsoft.Web.WebView2`, оставлена `QuestPDF` для PDF-экспорта.
- `releases.json` и `update-log.json` дублируют частично одну информацию — нужна синхронизация при каждом релизе (рассмотреть консолидацию).
- Нет автоматической проверки калькуляции при релизе — только юнит-тесты.
- Редизайн системы обновлений частично завершён (core-логика + тесты готовы, UI-полировка в процессе). Спецификация: `docs/arc/update-notification-rework-spec.md`.
- **HeadlessWpf behavior-тест для «Цена автоширины» отменён** (6 итераций не дали рабочего решения). XAML-grep test в `DataGridBindingsTests.cs` остаётся канонической гарантией. Для возврата к этой фиче требуется либо project-wide `[CollectionFixture<WpfAppFixture>]`, либо переход на `Border + TextBlock + Width=NaN` без DataGrid.

## Открытые вопросы

- Нужно ли добавить новые товары или изменить цены? (Только по явному запросу владельца.)
- Нужно ли улучшить механизм автообновления?
- ✅ CI/CD настроен через GitHub Actions (`.github/workflows/ci.yml` и `.github/workflows/release.yml`): build + test на push/PR, автоматическая публикация релиза и обновление `releases.json` по тегу `vX.Y.Z`.
- Нужна ли консолидация CHANGELOG.md ↔ update-log.json?

## Рекомендуемые следующие шаги

1. Запустить `validate-docs.ps1` и исправить найденные расхождения.
2. Обновить `README.md` (низкий приоритет).
3. Настроить CI/CD (GitHub Actions) для автоматической сборки и публикации релизов.
4. Консолидировать CHANGELOG.md и update-log.json (один source of truth).

## Source files

- `MosquitoNetCalculator/MosquitoNetCalculator.csproj` — версия 3.45.0.
- `releases.json` — история релизов (latest заполняется после GitHub Release + ZIP).
- `MosquitoNetCalculator/Resources/update-log.json` — история для UI.
- `docs/arc/*.md` — вся проектная документация.
- `docs/arc/documentation-matrix.json` — машиночитаемый источник матрицы.
- `what-to-update.ps1` — git diff -> docs к обновлению.
- `validate-docs.ps1` — 8 проверок консистентности.
- `generate-update-log.ps1` — CHANGELOG -> update-log.
- `render-matrix.ps1` — JSON -> DOCUMENTATION_MATRIX.md.

## Last verified

2026-07-17 — **v3.46.1:** Синхронизация doc-версии с реальным состоянием проекта (ранее sticky на v3.45.0). Тесты: 1195 → 1227/1227 pass. Изменения серии v3.46.0:
- **AGENTS.md:** добавлены секции «Wrapper contract» и «Last verified».
- **PdfExportService.BuildAdditionalKpPdf:** динамическая ширина ConstantItem через `ComputeAmountColumnWidth` + `MeasureTextWidthPt` (`Graphics.PageUnit=Point` для корректного DPI-преобразования).
- **PdfExportService.AddClientRowPdf + FlowDocumentBuilder.AddClientGridRow:** значения в клиентском блоке КП теперь `SemiBold` (жирные), не только лейблы.
- **Тесты:** +7 юнит-тестов на хелперы измерения ширины колонки (`PdfExportServiceTests.MeasureTextWidthPt_*` / `ComputeAmountColumnWidth_*`).
