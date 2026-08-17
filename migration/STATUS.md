# Миграция A.R.C. (2026-08) — статус и как завершить

> Единая точка возврата к незавершённой миграции агент-системы из старого
> расположения в единую папку `agents/`. Открой этот файл, когда вернёшься
> к задаче: пройди чек-лист внизу.

## Что за миграция

Агент-система A.R.C. переезжает из разброса (доки в `docs/arc/`, скрипты в
корне) в **единую папку `agents/`** (README + `docs/` + `scripts/`).
Корневые `AGENTS.md` / `CLAUDE.md` — тонкие wrapper-указатели, содержимое
живёт только в `agents/`.

## Что уже сделано (в рабочем дереве, НЕ закоммичено)

| Было | Стало |
|---|---|
| `docs/arc/*` (22 файла) | удалены → новые в `agents/docs/` (21 файл) |
| Корневые `validate-docs.ps1`, `gensymbols.ps1`, `render-matrix.ps1`, `what-to-update.ps1`, `arc-check.ps1` | удалены → переехали в `agents/scripts/` |
| `AGENTS.md`, `CLAUDE.md`, `README.md`, `~/.claude/skills/MULTI_AGENT_ARC_CALC_CONTROL.md` | обновлены (wrapper-контракт, ссылки) |
| — | новое: `agents/` (untracked), `docs/plans/` (untracked), `migration/` (этот файл) |

Старая система полностью сохранена в git-истории: `git show HEAD:docs/arc/CHEATSHEET.md` и т.п.

## Что осталось — чек-лист завершения

1. ~~**Устаревшие ссылки на `docs/arc/`**~~ ✅ **ПРОВЕРЕНО (2026-08-17):** единственные
   вхождения (`CURRENT_STATE.md:136`, `CHANGELOG.md:37`) — исторические записи
   о самой миграции («`docs/arc/*` → `agents/docs/`»), а не битые ссылки.
   Оставлены как есть. Битая ссылка на `arc/` была в схеме структуры README.md — исправлена.
2. ~~**Ссылки на скрипты**~~ ✅ **СДЕЛАНО (2026-08-17):** все выполнимые команды в доках
   переведены на `agents/scripts/<имя>.ps1`: INTENTS, PROMPTS, REFACTORING_PLAN,
   GOTCHAS, ux-refactor-plan, CURRENT_STATE (структура/шаги), README.md
   (список скриптов + команды + схема структуры: `docs/arc/` → `agents/`).
   Шаблон `render-matrix.ps1` обновлён, DOCUMENTATION_MATRIX.md перегенерирован.
   Осознанно оставлено: CHEATSHEET-правила (короткие имена — конвенция,
   полные пути есть в секции «Инструменты автоматизации»), SYMBOL_INDEX.md
   (генерируется), исторические записи в CHANGELOG/CURRENT_STATE
   (`docs/arc/*` — описание самой миграции, а не битая ссылка).
3. **Судьба корневых спеков** — `native-print-spec.md` (90 КБ),
   `native-print-collated-spec.md` (88 КБ), `monolith-decomposition-spec.md`,
   `bug-analysis-report.md`, `ai-coder-task-*.md`, `ТЗ_*.md` и др. (18 .md в корне):
   решить — перенос в `docs/plans/`, в `agents/docs/` или в `archive/`.
4. **Прогнать валидацию** перед коммитом:
   ```powershell
   powershell -ExecutionPolicy Bypass -File agents/scripts/validate-docs.ps1
   powershell -ExecutionPolicy Bypass -File agents/scripts/arc-check.ps1
   ```
5. **Закоммитить миграцию отдельным коммитом**: `agents/` + удаления
   (`docs/arc/*`, корневые скрипты) + обновлённые wrapper'ы + `docs/plans/`.
   ⚠️ В рабочем дереве есть и **не связанные с миграцией** изменения
   (Office-отчётность, AdminPanel, тесты Office-*): их коммитить отдельно.
6. **`agents/scripts/sync-version.ps1`** — после релиза (Last verified).

## Сопутствующие задачи (не миграция, но рядом)

- `archive/` — корзина мусора (разбор — владельцем, см. CHEATSHEET #22).
- `docs/plans/REFACTORING_PLAN_BIG_FILES.md` — готовый план разбивки
  `AiAssistantService.cs`, `PrintPreviewControl.xaml.cs`, `SlopePanelControl.xaml.cs`
  (фазы A→B→C; дождаться стабилизации AI-дизайна перед фазой B).

## Наведение порядка в файлах (2026-08-17)

Выполнено (коммиты `190ad15`, `caa733c` + рабочий этап):

- ✅ Фаза 1: джанк-файлы удалены из git; миграция закоммичена.
- ✅ Фаза 2: спеки → `docs/specs/`, отчёты → `docs/reports/`, release-notes →
  `docs/release-notes/`, план AI → `docs/plans/` (ссылки в CHEATSHEET обновлены),
  TESTING_CHECKLIST → `docs/`, README_installer → `docs/README_installer.md`.
- ✅ Фаза 3: релизные скрипты → `tools/release/`, dev-скрипты → `tools/dev/`;
  ссылки в agents/docs и отчётах обновлены. Точки входа остались в корне.
- ✅ Фаза 4: `A.R.C.Icon.png` (не использовалась) → `archive/` + удалена из git;
  пустая `docs/images/` удалена.
- ⏭ Фаза 5: CHANGELOG НЕ разбивался — история начинается с 3.35.0 (2026-06-23),
  старых записей нет. Нарезка спеков и рефакторинг .cs — по мере работы (план готов).
- ✅ Фаза 6: `arc-check.ps1` — проверки [4] гигиена корня и [5] большие файлы;
  CHEATSHEET #23 (структура корня).

✅ **Решено (2026-08-17):** историческая папка `~/.claude/skills/...` удалена из git
(файл побайтово идентичен `.claude/skills/...` — SHA256 совпадает, уникальных
переводов/контента в `~` не было); каноничный источник —
`.claude/skills/MULTI_AGENT_ARC_CALC_CONTROL.md`.

## Last verified

2026-08-17 — файл создан по итогам анализа рабочего дерева (git status, grep по ссылкам).
