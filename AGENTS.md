# Agent Wrapper — AGENTS.md

This repository uses a canonical multi-agent control file:

```text
docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md
```

## Quick start (token-optimised)

Before any non-trivial task:

1. Read `docs/arc/CHEATSHEET.md` — critical rules + routing table (40 lines, 15 seconds).
2. Read `docs/arc/CURRENT_STATE.md` — current project state.
3. Follow the routing table below for additional files.
4. After changes: run `what-to-update.ps1 $(git diff --name-only)` → update listed docs → run `validate-docs.ps1`.

### Inline routing (same as CHEATSHEET, for zero-extra-read access)

```
Задача про →            Читай (кроме CHEATSHEET + CURRENT_STATE)
──────────────────────────────────────────────────────────────────
Расчёты, формулы, Anwis  CALCULATION_LOGIC + TEST_CASES + GOTCHAS
Релиз, автообновление   RELEASE_PROCESS + AUTO_UPDATE (читать ОБЯЗАТЕЛЬНО обе для каждого релиза)
UI, темы, стили          GOTCHAS#7 + DECISIONS#10
Печать КП                GOTCHAS#6 + CALCULATION_LOGIC#КП
Сохранение/загрузка      GOTCHAS#2,#3,#9 + DECISIONS#3
Цены                     GOTCHAS#4 + CALCULATION_LOGIC#цены
Тесты                    CALCULATION_TEST_CASES
Навигация по коду        SYMBOL_INDEX.md (60 классов, 16 модулей)
Понимание намерений       INTENTS.md (mapping фраз на файлы)
Всё остальное            (ничего)
Тривиально (≤10 строк)   grep GOTCHAS по имени изменённого файла
```

## Key reference files

```text
docs/arc/CHEATSHEET.md              — быстрый вход, критические правила
docs/arc/DOCUMENTATION_MATRIX.md   — карта «поменял файл → обнови документы»
docs/arc/PROMPTS.md                — готовые prompt-шаблоны
docs/arc/MODULES.md                — карта модулей проекта
docs/arc/DECISIONS.md              — принятые архитектурные решения
docs/arc/GOTCHAS.md                — опасные места, исторические баги
docs/arc/CALCULATION_LOGIC.md      — логика расчётов
docs/arc/CALCULATION_TEST_CASES.md — эталонные расчётные кейсы
docs/arc/RELEASE_PROCESS.md        — процесс релиза
docs/arc/AUTO_UPDATE.md            — автообновление
```

## Automation tools

```powershell
# Сгенерировать SYMBOL_INDEX.md (после добавления/удаления классов)
powershell -ExecutionPolicy Bypass -File gensymbols.ps1

# Что обновить в документации?
what-to-update.ps1 $(git diff --name-only)

# Валидация консистентности (8 проверок)
powershell -ExecutionPolicy Bypass -File validate-docs.ps1

# Сгенерировать update-log.json из CHANGELOG.md (при релизе)
powershell -ExecutionPolicy Bypass -File generate-update-log.ps1

# Перегенерировать DOCUMENTATION_MATRIX.md из JSON
powershell -ExecutionPolicy Bypass -File render-matrix.ps1

# Проверить синхронизацию docs перед коммитом
powershell -ExecutionPolicy Bypass -File arc-check.ps1
```

---

## Последние изменения (v3.43.1 — 2026-07-03)

- **Crashfix: `NullReferenceException` в `PrintPreviewWindow.UpdateDimmingOverlay()` при первом открытии окна предпросмотра КП.** XAML парсился сверху вниз: `AllPagesCheck.IsChecked="True"` срабатывал во время `InitializeComponent` раньше, чем создавались `DimmingOverlay`/`RangeHint`. Guard `if (!IsInitialized) return;` решает одной строкой без рассыпанных null-checks в каждом месте.
- **Печатное КП — UX-пакет:**
  - **Кастомный title bar** в стиле основной программы: `WindowStyle=None` + `WindowChrome` + переиспользованный `TitleBarControl` (новая `bool ShowSettings` DependencyProperty — скрывает кнопку ⚙). Win32 `WndProc` ловит `WM_GETMINMAXINFO` → maximized-окно больше не уходит под панель задач.
  - **NumericUpDown «Кол-во копий»** в Fluent-обёртке (Surface фон, Border, `CornerRadius=6`) — цифры видны сразу при первом открытии.
  - **Новый Fluent RadioButton** (`Themes/InputStyles.RadioButton.xaml`) — implicit Style, подключён глобально через `App.xaml`.
  - **Range-блок прячется через DataTrigger** при «Все страницы» (раньше IsEnabled — «серое на сером»).
  - **Кастомный Zoom-тулбар** `[ − / N% / + / 100% ]`; `<FlowDocumentReader>` заменён на `<FlowDocumentPageViewer>` (нет встроенного тулбара + публичный `Zoom`). Стандартный зум FlowDocumentReader без UI — не работал.
- **Из тела КП убрано слово «Договор»** — теперь просто «КОММЕРЧЕСКОЕ ПРЕДЛОЖЕНИЕ / № X-XXX от DD.MM.YYYY».

Подробности — в [`CHANGELOG.md`](./CHANGELOG.md), состояние — в [`docs/arc/CURRENT_STATE.md`](./docs/arc/CURRENT_STATE.md).

**Предыдущие релизы для контекста:**
- v3.43.0 (2026-07-03) — Дверная сетка (3000 ₽/м² + Антикошка + 600 ₽ вычет) + Append-only Updates block + AddNewUpdate no-flicker контракт.
- v3.42.1 (2026-07-02) — hotfix автообновления в Program Files.
- v3.42.0 (2026-07-02) — slide-out панель навигации, ПСУЛ/Уплотнение — ввод только кол-вом.

---\n\n## Ограничения — 3000 ₽/м², Белый; Антикошка (+2000 ₽/м²); монтаж 600 ₽/шт.; в «На завод» выбирается автоматически.
- **Блок «Обновления» — append-only архитектурный инвариант** — `update-log.json` теперь можно дописывать в конец без перестановки старых записей; новая версия больше не «мигает», старая карточка «Новейшая» плавно сменяется.
- **Auto-update → MainWindow.AddNewUpdate, no-flicker контракт** — при появлении обновления карточка добавляется без промежуточного состояния «без бейджа».

Подробная архитектура, ASCII-диаграмма и таблица тестов — в [`docs/arc/CALCULATION_LOGIC.md`](./docs/arc/CALCULATION_LOGIC.md) (раздел «Как данные Auto-update попадают на UI (AddNewUpdate flow)»). Состояние проекта — в [`docs/arc/CURRENT_STATE.md`](./docs/arc/CURRENT_STATE.md), полный список изменений — в [`CHANGELOG.md`](./CHANGELOG.md).

**Предыдущие релизы для контекста:**
- v3.42.1 (2026-07-02) — hotfix автообновления в Program Files.
- v3.42.0 (2026-07-02) — slide-out панель навигации, ПСУЛ/Уплотнение — ввод только кол-вом.

---

## Ограничения

- This file is a **thin compatibility wrapper**, not the source of truth.
- Do not duplicate project rules here.
- If this file conflicts with `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md`, the repository-local canonical file wins.
- To change project rules, edit `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md`, not this file.

---

Если `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md` недоступен — остановись и сообщи владельцу. Не придумывай правила.

---

## Application Icon

Файл иконки приложения: **`A.R.C.Icon.png`** (корень репозитория).

Копии, используемые при сборке:
- `MosquitoNetCalculator/Resources/app_icon.png` — PNG-версия
- `MosquitoNetCalculator/Resources/app_icon.ico` — ICO-версия (сконвертирована из PNG)

### Обновление иконки

1. Заменить `A.R.C.Icon.png` в корне репозитория.
2. Скопировать в `MosquitoNetCalculator/Resources/app_icon.png`.
3. Сконвертировать в ICO: `python -c "from PIL import Image; img=Image.open('MosquitoNetCalculator/Resources/app_icon.png'); img.save('MosquitoNetCalculator/Resources/app_icon.ico', format='ICO', sizes=[(16,16),(32,32),(48,48),(256,256)])"`

---

## План системного рефакторинга

Проект находится в активной фазе устранения архитектурного долга. Детальный план по фазам, компонентам, тестам и критериям успеха зафиксирован в:

```text
docs/arc/REFACTORING_PLAN.md
```

Ключевые направления:
- **Фаза 1:** `MainWindow.xaml.cs` → `NavigationService`, `OverlayManager`, `SlopeOverlayCoordinator`.
- **Фаза 2:** `UpdateService.cs` → `UpdateManifestClient`, `VersionResolver`, `UpdateDownloader`, `UpdateVerifier`, `UpdatePresenter`.
- **Фаза 3:** `PrintService.cs` → `PrintQueueResolver`, `FixedDocumentBuilder`, `PrintOrchestrator`.
- **Фаза 4:** `DialogService.cs` → XAML-шаблоны + `DialogBuilder`.
- **Фаза 5:** `OrderItem.cs` → `ProductCatalog`, `AnwisSizeCalculator`, `SlopeCalculationExtensions`.
- **Фаза 6:** `MainWindow.Orders.cs` → `OrderImportExportService`, `OrderDialogService`, `OrderGridPresenter`.
- **Фаза 7:** Валидация, тесты, обновление документации.

Перед началом любой фазы — зафиксировать baseline (`dotnet build` + `dotnet test`). После каждой фазы — `code-reviewer-kimi` + `validate-docs.ps1`.

---

## Last verified

2026-07-13 — **Phase 5 refactoring complete:** `OrderItem.cs` 651→~520 строк (−20%). 3 new components: `ProductCatalog` (product categories), `AnwisSizeCalculator` (pure Anwis size functions), `SlopeCalculationExtensions` (DeepClone extension). +21 tests. **1133/1133 tests pass.** Бизнес-логика не затронута, все public/internal API сохранены как тонкие прокси.

2026-07-12 — **Phase 2 refactoring complete:** `UpdateService.cs` 910→608 строк (−33%). 5 new components: `VersionResolver`, `IdleDetector`, `UpdateVerifier`, `UpdateManifestClient`, `UpdateDownloader`. +46 tests. **978/978 tests pass.** Бизнес-логика не затронута, все public/internal API сохранены как тонкие прокси.

2026-07-12 — **Phase 1 refactoring complete:** `MainWindow.xaml.cs` 1051→760 строк (−28%). 4 new services: `NavigationService`, `OverlayManager`, `SlopeOverlayCoordinator`, `SlopesProUpsellGate`. +26 tests. **932/932 tests pass.** Бизнес-логика не затронута, все `internal` API сохранены.
