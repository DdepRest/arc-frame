# A.R.C. Agent System

Единая папка системы агентов проекта. Всё, что нужно AI-агенту для работы в репозитории, лежит здесь — не разбросано по корню.

## Структура

```
agents/
├── README.md                     ← этот файл (точка входа)
├── docs/                         ← проектная память и правила (21 файл)
│   ├── MULTI_AGENT_ARC_CALC_CONTROL.md  ← source of truth (routing + CONTROL §1–13)
│   ├── CHEATSHEET.md                    ← быстрый вход, критические правила (читай первым!)
│   ├── INTENTS.md                       ← mapping намерений → файлы
│   ├── SYMBOL_INDEX.md                  ← индекс классов (авто-генерация)
│   ├── DOCUMENTATION_MATRIX.md          ← «файл → документы» (из JSON)
│   ├── CURRENT_STATE.md                 ← текущее состояние проекта
│   ├── PROMPTS.md                       ← готовые шаблоны промптов
│   └── ...                              ← CALCULATION_LOGIC, GOTCHAS, MODULES и др.
└── scripts/                       ← скрипты системы
    ├── validate-docs.ps1          ← 11 проверок консистентности docs (exit 0/1)
    ├── arc-check.ps1              ← pre-commit проверка (validate + SYMBOL_INDEX + docs-sync)
    ├── what-to-update.ps1         ← какие docs обновить при изменении файлов
    ├── gensymbols.ps1             ← генерация SYMBOL_INDEX.md из .cs
    ├── render-matrix.ps1          ← DOCUMENTATION_MATRIX.md из JSON
    └── sync-version.ps1           ← версия из csproj → «## Last verified» всех docs
```

## Как использовать (для агента)

1. **Всегда читай** `agents/docs/CHEATSHEET.md` и `agents/docs/MULTI_AGENT_ARC_CALC_CONTROL.md` перед нетривиальной задачей.
2. Маршрут по задаче — routing-таблица в CHEATSHEET.md.
3. Поиск символов — `SYMBOL_INDEX.md` (grep, а не чтение исходников).
4. Работай по циклу: Intake → Context → Plan → Execute → Verify → Document → Report.
5. После изменений — ритуал: `what-to-update.ps1` → обнови docs → `validate-docs.ps1`.
6. **Самоподдержание (CONTROL#13):** если ты разбил/переименовал файл, добавил класс, изменил бизнес-логику или саму систему — обнови соответствующие файлы здесь же, в этом цикле. Устаревшая документация хуже отсутствующей.

## Запуск скриптов

```powershell
powershell -ExecutionPolicy Bypass -File agents/scripts/validate-docs.ps1
powershell -ExecutionPolicy Bypass -File agents/scripts/arc-check.ps1
agents/scripts/sync-version.ps1        # после релиза
```

## Не заходить

- `archive/` (корень) — **корзина мусора** (`nul`, `-1`, `Probe.cs`, логи, случайные
  папки). Агентам туда НЕ ходить: там нет кода, спеков и скриптов. Разбор — владельцем.
  Правило: CHEATSHEET.md #22.
- `migration/` (корень) — статус незавершённой миграции (это НЕ мусор):
  читать `migration/STATUS.md`, если задача связана с переездом системы.

## Почему отдельная папка

- **Один каталог вместо разброса:** раньше docs лежали в `agents/docs/`, скрипты — в корне. Теперь система агентов целиком в `agents/`.
- **Внутренние ссылки — короткие имена** (`CHEATSHEET.md`, не `agents/docs/CHEATSHEET.md`): файлы переносимы, ссылки не ломаются при открытии из любой папки.
- **`AGENTS.md` в корне** — тонкий wrapper-указатель на этот файл (конвенция агентских инструментов требует его в корне).

## Last verified

2026-08-10 — миграция `agents/docs/` + корневые скрипты → `agents/`; `validate-docs.ps1` — 0 issues.
