# CURRENT_STATE.md

## Что сейчас выглядит рабочим

- Все основные функции расчёта работают стабильно.
- Печать КП, отправка на завод, сохранение заказов — функционируют.
- Автообновление через GitHub Releases настроено и работает (перешли с Velopack Flow на собственный механизм через watchdog .bat).
- Тёмная тема стабильна, переключается без потери данных.
- Undo/Redo работает для позиций расчёта и Доп.КП.
- Юнит-тесты покрывают ключевые сценарии (расчёты, экспорт/импорт, версия, обновления).
- Текущая версия: **3.36.2** (опубликован GitHub Release, автообновление настроено).
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

- `MosquitoNetCalculator/MosquitoNetCalculator.csproj` — версия 3.36.0.
- `releases.json` — история релизов.
- `MosquitoNetCalculator/Resources/update-log.json` — история для UI.
- `docs/arc/*.md` — вся проектная документация.
- `docs/arc/documentation-matrix.json` — машиночитаемый источник матрицы.
- `what-to-update.ps1` — git diff -> docs к обновлению.
- `validate-docs.ps1` — 8 проверок консистентности.
- `generate-update-log.ps1` — CHANGELOG -> update-log.
- `render-matrix.ps1` — JSON -> DOCUMENTATION_MATRIX.md.

## Last verified

2026-06-25 (release 3.36.2 — AmountOnlyProducts, UTF-8 BOM fix, 603/603 tests pass)
