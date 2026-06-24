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
Релиз, автообновление   RELEASE_PROCESS + AUTO_UPDATE
UI, темы, стили          GOTCHAS#7 + DECISIONS#10
Печать КП                GOTCHAS#6 + CALCULATION_LOGIC#КП
Сохранение/загрузка      GOTCHAS#2,#3,#9 + DECISIONS#3
Цены                     GOTCHAS#4 + CALCULATION_LOGIC#цены
Тесты                    CALCULATION_TEST_CASES
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
# Что обновить в документации?
what-to-update.ps1 $(git diff --name-only)

# Валидация консистентности (8 проверок)
powershell -ExecutionPolicy Bypass -File validate-docs.ps1

# Сгенерировать update-log.json из CHANGELOG.md (при релизе)
powershell -ExecutionPolicy Bypass -File generate-update-log.ps1

# Перегенерировать DOCUMENTATION_MATRIX.md из JSON
powershell -ExecutionPolicy Bypass -File render-matrix.ps1
```

---

## Ограничения

- This file is a **thin compatibility wrapper**, not the source of truth.
- Do not duplicate project rules here.
- If this file conflicts with `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md`, the repository-local canonical file wins.
- To change project rules, edit `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md`, not this file.

---

Если `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md` недоступен — остановись и сообщи владельцу. Не придумывай правила.
