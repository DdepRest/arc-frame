# A.R.C. Agent System

Единая папка системы агентов проекта. Всё, что нужно AI-агенту для работы в репозитории, лежит здесь — не разбросано по корню.

## Структура

```
agents/
├── README.md                     ← этот файл (точка входа)
├── docs/                         ← проектная память и правила
│   ├── MULTI_AGENT_ARC_CALC_CONTROL.md  ← source of truth (routing + CONTROL §1–13)
│   ├── CHEATSHEET.md                    ← быстрый вход, Fast-Track vs Full-Track (читай первым!)
│   ├── AGENT_GIT_AUTH.md                ← авторизация пушей агентов (gh credential helper)
│   ├── INTENTS.md                       ← mapping намерений → файлы
│   ├── SYMBOL_INDEX.md                  ← индекс классов (авто-генерация)
│   ├── DOCUMENTATION_MATRIX.md          ← «файл → документы» (из JSON)
│   ├── CURRENT_STATE.md                 ← текущее состояние проекта (актуальный срез, макс 10 версий)
│   ├── GOTCHAS.md                       ← 52 ловушки с интерактивным реестром-оглавлением
│   ├── history/                         ← архивы старых релизов (CURRENT_STATE_ARCHIVE.md)
│   └── ...                              ← CALCULATION_LOGIC, MODULES, DECISIONS и др.
└── scripts/                       ← скрипты системы
    ├── arc-finish.ps1             ← единая автоматическая финализация задачи (Last verified + SYMBOL_INDEX + validate + check)
    ├── find-task.ps1              ← умный поиск целевых файлов, классов, ловушек и разделов формул
    ├── validate-docs.ps1          ← 12 проверок консистентности docs (exit 0/1)
    ├── arc-check.ps1              ← pre-commit проверка (validate + SYMBOL_INDEX + docs-sync)
    ├── rotate-current-state.ps1   ← авто-ротация истории версий в CURRENT_STATE (лимит 10)
    ├── what-to-update.ps1         ← какие docs обновить при изменении файлов
    ├── gensymbols.ps1             ← генерация SYMBOL_INDEX.md из .cs
    ├── render-matrix.ps1          ← DOCUMENTATION_MATRIX.md из JSON
    ├── sync-version.ps1           ← версия из csproj → «## Last verified» всех docs
    ├── sync-last-verified.ps1     ← дата «Last verified» для изменённых docs (-Check — проверка)
    └── install-git-hooks.ps1      ← pre-commit → даты в docs обновляются на коммите
```

## Как использовать (для агента)

### 🚀 Быстрые ярлыки в корне репозитория (Zero Friction):
- `.\find.bat <запрос>` — мгновенный поиск файлов, классов, ловушек и формул.
- `.\finish.bat` — финализация задачи в 1 команду (синхронизация + валидация).
- `.\check.bat` — pre-commit проверка репозитория.

### 🚀 Fast-Track (Простая задача ≤ 15 строк кода):
1. **Читай только** `agents/docs/CHEATSHEET.md` и реестр в `agents/docs/GOTCHAS.md`.
2. Правка кода → целевой тест (`dotnet test --filter ...`) → запись в `CHANGELOG.md`.
3. Финализация: `.\finish.bat`. Не читай 20 файлов документации!

### 🏛️ Full-Track (Архитектурная / расчётная задача):
1. Локализация: `.\find.bat "намерение/символ"` (находит нужные файлы, классы и ловушки).
2. Читай `agents/docs/CHEATSHEET.md` и `agents/docs/MULTI_AGENT_ARC_CALC_CONTROL.md`.
3. Маршрут по задаче — Quick Index в `CALCULATION_LOGIC.md`, `AUTO_UPDATE.md` или `RELEASE_PROCESS.md`.
4. Работай по циклу: Intake → Context → Plan → Execute → Verify → Document → Report.
5. **Финализация в 1 клик:** `.\finish.bat` (сам актуализирует даты `Last verified`, обновит `SYMBOL_INDEX.md`, проверит ротацию и запустит проверки).
6. **Самоподдержание (CONTROL#13):** если ты разбил/переименовал файл, добавил класс, изменил бизнес-логику или саму систему — обнови соответствующие файлы в этом цикле.

## Запуск скриптов

```powershell
.\finish.bat   # автоматическая финализация всей задачи (1 клик)
.\check.bat    # pre-commit safety net
.\find.bat     # умный поиск файлов и классов
powershell -ExecutionPolicy Bypass -File agents/scripts/validate-docs.ps1
powershell -ExecutionPolicy Bypass -File agents/scripts/rotate-current-state.ps1  # ротация версий
```

## Не заходить

- `archive/` (корень) — **корзина мусора** (удалена в процессе чистки репо). Агентам не создавать её и не искать в ней код.
- `migration/` (корень) — статус незавершённой миграции (если есть).

## Почему отдельная папка

- **Один каталог вместо разброса:** система агентов целиком собрана в `agents/`.
- **Внутренние ссылки — короткие имена:** файлы переносимы, ссылки не ломаются.
- **`AGENTS.md` в корне** — тонкий wrapper-указатель (конвенция агентских инструментов требует его в корне).

## Last verified
2026-10-19 (v3.55.1) — добавлен Fast-Track коридор (≤15 строк), скрипт rotate-current-state.ps1, реестр 52 ловушек в GOTCHAS.md, сжатие CURRENT_STATE.md (CONTROL#13).
2026-10-04 (v3.54.0) — добавлен `AGENT_GIT_AUTH.md` (авторизация пушей через `gh`); строки routing в `CHEATSHEET.md`/`INTENTS.md`; скрипты не менялись.
2026-09-21 (v3.53.0) — в scripts/ добавлены sync-last-verified.ps1 и install-git-hooks.ps1.
2026-08-10 — миграция `agents/docs/` + корневые скрипты → `agents/`; `validate-docs.ps1` — 0 issues.
