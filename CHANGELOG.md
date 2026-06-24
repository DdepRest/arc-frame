# Changelog

## Unreleased

*(пусто — все изменения вошли в релиз 3.36.0)*

---

## 3.36.0 — 2026-06-24

### Для пользователей

- **Копирование заказов:** добавлен пункт «Копировать» в контекстном меню списка «Заказы». Создаёт полную копию заказа с новым номером (например, «2-8» → «2-8.1»), статусом «Новый» и актуальной датой.

### Исправления

- Исправлен XML-комментарий `InstallationSurcharge` в `OrderItem.Installation.cs`: `Default 0 ₽` → `Default 500 ₽` (соответствует коду).
- Исправлен pre-existing дрифт версии в тесте `UpdateLogTests.AllNewestFirst_FirstItemIsNewest` (ожидал 3.34.5, теперь 3.35.0).

### Техническое

- **GenerateCopyContractNumber** перенесён из `MainWindow.Orders.cs` в `OrderStorageService.cs` — логика нумерации копий теперь в одном месте с `GetNextOrderNumber` и `GenerateContractNumber`.
- Добавлено **11 тестов**: 10 unit-тестов `GenerateCopyContractNumber` (базовый, инкремент, стриппинг суффикса, пустой номер, без дефиса, множественные точки, пробелы, три копии подряд, игнорирование dash-номеров) + 1 интеграционный тест `CopyOrder_PreservesAllData_AndMutatesIdentityFields` (deep-clone сохраняет Items/ClientInfo/AdditionalKps/Notes/ContractDate).
- Инициализирована система A.R.C. (Agent Reference & Control) — документация для AI-навигации, расчётов, релизов и автообновления.
- **Multi-agent control: portability migration.** Канонический master-файл перенесён из `~/.claude/skills/MULTI_AGENT_ARC_CALC_CONTROL.md` в `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md`. Теперь source of truth живёт внутри репозитория, версионируется в git и доступен любым агентам (Claude, Gemini, Copilot, Aider, CI). Файл `~/.claude/skills/MULTI_AGENT_ARC_CALC_CONTROL.md` стал тонким external bootstrap loader. Устранена циклическая переадресация `AGENT.md → MULTI → AGENT.md`. Wrappers (`AGENT.md`, `AGENTS.md`, `CLAUDE.md`, `GEMINI.md`) обновлены и более не дублируют правила.

---

## 3.35.0 — 2026-06-23

### Исправления

- Полный фикс утечки формул Anwis на не-Anwis товары.
- Исправлено: Откос материал, Работа, Пояс больше не показывают высоту 30 мм.
- Исправлено: редактирование ширины для Откос материал больше не добавляет +2 мм.
- Исправлено: смена режима AnwisSizeMode для не-Anwis товаров больше не портит размеры.
- Добавлены юнит-тесты на все 4 точки утечки.

---

*Полная история релизов доступна в `releases.json` и `MosquitoNetCalculator/Resources/update-log.json`.*
