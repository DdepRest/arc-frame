# CURRENT_STATE.md

## Что сейчас выглядит рабочим

- Все основные функции расчёта работают стабильно.
- Печать КП, отправка на завод, сохранение заказов — функционируют.
- Автообновление через GitHub Releases настроено и работает (перешли с Velopack Flow на собственный механизм через watchdog .bat).
- Тёмная тема стабильна, переключается без потери данных.
- Undo/Redo работает для позиций расчёта и Доп.КП.
- Юнит-тесты покрывают ключевые сценарии (расчёты, экспорт/импорт, версия, обновления).
- Текущая версия: **3.35.0**.
- Система A.R.C. инициализирована и прошла аудит документации.
- **A.R.C. технически принят** — документация прошла аудит по коду, эталонные расчётные кейсы созданы, релизный процесс описан, терминология по размерам разведена однозначно.
- **Multi-agent portability migration завершён** — канонический master-файл перенесён внутрь репозитория в `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md`; `~/.claude/skills/MULTI_AGENT_ARC_CALC_CONTROL.md` теперь external bootstrap loader. Циклическая переадресация устранена.

## Статус A.R.C.

✅ A.R.C. создан.
✅ Структура соответствует multi-agent архитектуре.
✅ Аудит прошёл — утверждения в `CALCULATION_LOGIC.md`, `GOTCHAS.md`, `RELEASE_PROCESS.md`, `AUTO_UPDATE.md` проверены по исходному коду и тестам.
✅ Созданы эталонные расчётные кейсы в `CALCULATION_TEST_CASES.md` (с явными статусами).
✅ Термины по размерам (введённые / расчётные / заводские / в КП) разведены однозначно.
✅ Правило безопасного порядка публикации `releases.json` зафиксировано в `RELEASE_PROCESS.md` и `AUTO_UPDATE.md`.
✅ Multi-agent master-файл перенесён в репозиторий (`docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md`) — source of truth версионируется, доступен любым агентам.
✅ Wrappers (`AGENT.md`, `AGENTS.md`, `CLAUDE.md`, `GEMINI.md`) — тонкие redirect-файлы, правила не дублируют.
⚠️ **Ожидает**: владелец должен вручную подтвердить бизнес-правильность расчётных кейсов в `CALCULATION_TEST_CASES.md`.

## Архитектура multi-agent control

```text
docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md
    = canonical source of truth (внутри репозитория, версионируется)

~/.claude/skills/MULTI_AGENT_ARC_CALC_CONTROL.md
    = external bootstrap loader только для Claude-среды

AGENT.md / AGENTS.md / CLAUDE.md / GEMINI.md
    = thin compatibility wrappers

docs/arc/CURRENT_STATE.md
docs/arc/CALCULATION_LOGIC.md
docs/arc/CALCULATION_TEST_CASES.md
docs/arc/GOTCHAS.md
docs/arc/RELEASE_PROCESS.md
docs/arc/AUTO_UPDATE.md
    = проектная память
```

Любой агент обязан сначала прочитать `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md`, затем следовать маршруту, который он задаёт. Изменения правил редактируются только в repo-local master-файле.

## Последние изменения (что было сделано недавно)

- **v3.35.0** — полный фикс утечки формул Anwis на не-Anwis товары. Исправлены 4 точки утечки + добавлены юнит-тесты.
- **v3.34.5** — фикс отображения версии в заголовке (многослойный fallback с логированием).
- **v3.34.4** — рефакторинг: разбиение больших файлов на partial-классы.
- **v3.34.3** — сегментированный контроль режима Anwis в QuickAdd.
- **A.R.C. portability migration** — master-файл multi-agent control перенесён из `~/.claude/skills/` в `docs/arc/`. Устранена циклическая переадресация, source of truth теперь версионируется в репозитории.

## Что выглядит незавершённым / требует внимания

- `README.md` в корне практически пустой (`# arc-frame`) — стоит обновить для GitHub.
- `releases.json` и `update-log.json` дублируют частично одну информацию — releases.json для автообновления, update-log.json для UI. Нужно синхронизировать при каждом релизе.
- Нет автоматической проверки калькуляции при релизе — только юнит-тесты.
- Эталонные расчётные кейсы (`CALCULATION_TEST_CASES.md`) созданы из baseline кода, но ещё не подтверждены владельцем вручную.

## Открытые вопросы

- Нужно ли добавить новые товары или изменить цены? (Только по явному запросу владельца.)
- Нужно ли улучшить механизм автообновления? (Сейчас работает через .bat-файл — достаточно надёжно, но не идеально.)
- Нужен ли CI/CD для автоматической сборки и публикации? (Сейчас ручной процесс через build.bat + gh release upload.)
- Нужно ли подтвердить владельцем эталонные расчётные кейсы из `CALCULATION_TEST_CASES.md`?

## Что стоит проверить владельцу

1. **Подтвердить эталонные расчётные кейсы** в `docs/arc/CALCULATION_TEST_CASES.md` — отметить галочками подтверждённые примеры.
2. Проверить, что расчёт Anwis в режиме ББ 60 работает корректно на нескольких реальных примерах.
3. Проверить, что не-Anwis товары (Отлив, Козырёк, Откос материал, Работа) не показывают лишних коррекций размеров.
4. Проверить, что КП печатается корректно и сумма прописью верна.
5. Проверить, что автообновление видит новую версию (после следующего релиза).

## Рекомендуемые следующие шаги

1. **Владелец: подтвердить эталонные расчётные кейсы** в `CALCULATION_TEST_CASES.md` (поставить статус «Подтверждено владельцем»).
2. После подтверждения кейсов — зафиксировать A.R.C. в git одним коммитом: `git add AGENT.md AGENTS.md CLAUDE.md GEMINI.md CHANGELOG.md docs/arc && git commit -m "docs: accept A.R.C. project memory (master moved into repo)"` (не выполнять без явного разрешения).
3. **(Отдельная мини-задача)** Исправить устаревший XML-комментарий в `OrderItem.Installation.cs` — `InstallationSurcharge` фактически равен 500, но комментарий говорит «0 ₽» (см. `GOTCHAS.md`, грабля «Устаревший комментарий по InstallationSurcharge»). Бизнес-логику не менять.
4. **Обновить README.md** для GitHub — добавить описание, скриншоты, инструкцию по установке (низкий приоритет).
5. **Настроить CI/CD** (GitHub Actions) для автоматической сборки и публикации релизов (низкий приоритет).

## Source files

- `MosquitoNetCalculator/MosquitoNetCalculator.csproj` — версия 3.35.0.
- `releases.json` — история релизов.
- `MosquitoNetCalculator/Resources/update-log.json` — история для UI.
- `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md` — canonical master после migration.
- `~/.claude/skills/MULTI_AGENT_ARC_CALC_CONTROL.md` — external bootstrap loader.

## Last verified

2026-06-24 (multi-agent portability migration завершена).
