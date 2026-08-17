# Multi-Agent Control Plan — A.R.C. / расчёты товаров, размеров, Anwis, КП, завод

Этот документ адаптирует AI-самоконтроль под проект, который делают разные агенты.

Проект уже имеет существующую структуру:

```text
agents/docs/
  MULTI_AGENT_ARC_CALC_CONTROL.md   ← этот файл (source of truth)
  CHEATSHEET.md                     ← быстрый вход (читай первым!)
  INTENTS.md                        ← mapping намерений на файлы (A.R.C. v4)
  SYMBOL_INDEX.md                   ← индекс классов/методов/свойств (A.R.C. v4, авто-генерация)
  DOCUMENTATION_MATRIX.md          ← карта «файл → документы»
  PROMPTS.md                       ← готовые prompt-шаблоны
  CURRENT_STATE.md                 ← текущее состояние проекта
  CALCULATION_LOGIC.md             ← логика расчётов
  CALCULATION_TEST_CASES.md        ← эталонные кейсы
  GOTCHAS.md                       ← опасные места / баги
  MODULES.md                       ← карта модулей
  DECISIONS.md                     ← принятые архитектурные решения
  PROJECT_OVERVIEW.md              ← обзор проекта
  RELEASE_PROCESS.md               ← процесс релиза
  AUTO_UPDATE.md                   ← автообновление
```

Скрипты системы — в `agents/scripts/` (validate-docs.ps1, arc-check.ps1, what-to-update.ps1, gensymbols.ps1, render-matrix.ps1, sync-version.ps1).

Главная задача: сделать так, чтобы любой агент, входящий в проект, не ломал расчёты, размеры, Anwis-логику, КП, заводские размеры, релизный процесс и автообновление — и тратил минимум токенов на понимание проекта.

---

## 1. Источник истины для всех агентов

Единственный канонический master-файл для всех агентов:

```text
MULTI_AGENT_ARC_CALC_CONTROL.md
```

Этот файл является source of truth для поведения AI-агентов в проекте.

Файлы вне `agents/docs/`:

```text
AGENT.md
AGENTS.md
CLAUDE.md
GEMINI.md
~/.claude/skills/MULTI_AGENT_ARC_CALC_CONTROL.md
```

являются только совместимыми thin wrappers / redirect-файлами. Они не должны дублировать правила проекта. Если wrapper-файл противоречит этому файлу — приоритет у этого файла.

Проектная память и фактическое состояние проекта находятся в `agents/docs/`.

---

## 2. Обязательный вход любого агента (token-optimised)

### Первый шаг: CHEATSHEET (всегда, ~40 строк)

Перед любой нетривиальной задачей агент обязан прочитать:

```text
CHEATSHEET.md
```

Это даёт критические правила и routing-таблицу за 15 секунд.

### Второй шаг: полные файлы по routing-таблице

Затем агент читает `CURRENT_STATE.md` и, в зависимости от задачи, дополнительные файлы:

| Задача касается... | Читать обязательно (кроме CHEATSHEET + CURRENT_STATE) |
|---|---|
| Расчётов, формул, Anwis, размеров | `CALCULATION_LOGIC.md`, `CALCULATION_TEST_CASES.md`, `GOTCHAS.md` |
| КП, печати | `CALCULATION_LOGIC.md#КП`, `GOTCHAS.md#6` |
| Завода, FactoryText | `CALCULATION_LOGIC.md#завод`, `GOTCHAS.md` |
| Монтажа | `CALCULATION_LOGIC.md#монтаж`, `GOTCHAS.md#11` |
| Итогов, TotalInfo | `CALCULATION_LOGIC.md#итоги` |
| Цен | `CALCULATION_LOGIC.md#цены`, `GOTCHAS.md#4`, `DECISIONS.md#9` |
| Релиза, версии, публикации | `RELEASE_PROCESS.md`, `AUTO_UPDATE.md` |
| Автообновления | `AUTO_UPDATE.md`, `GOTCHAS.md#5,#8`, `RELEASE_PROCESS.md` |
| UI, тем, стилей | `GOTCHAS.md#7`, `DECISIONS.md#10` |
| Сохранения/загрузки данных | `GOTCHAS.md#2,#3,#9`, `DECISIONS.md#3` |
| Тестов | `CALCULATION_TEST_CASES.md` |
| Новый агент (первый вход) | + `MODULES.md`, `DECISIONS.md`, `PROJECT_OVERVIEW.md` |
| Структурные изменения (разбивка/переименование/новые файлы или классы) | `CONTROL#13`, `SYMBOL_INDEX.md`, `INTENTS.md`, `DOCUMENTATION_MATRIX.md` |
| Всё остальное | Только `CURRENT_STATE.md` |
| **Тривиальная задача (≤10 строк, не critical domain)** | Только `CHEATSHEET.md`, затем проверь `GOTCHAS.md` по ключевым словам |

Если агент физически не имеет доступа к `MULTI_AGENT_ARC_CALC_CONTROL.md` — остановиться и сообщить владельцу. Запрещено использовать `AGENT.md` как source of truth.

### Третий шаг: SYMBOL_INDEX для поиска символов (A.R.C. v4)

Если агент ищет конкретный класс, метод или свойство — проверить:

```text
SYMBOL_INDEX.md
```

Этот файл содержит индекс 60+ классов с их свойствами, методами и файлами. Генерируется автоматически через `gensymbols.ps1`. Вместо чтения полных исходников, агент grep'ает этот индекс → находит файл → code_searcher для точной строки → экономит ~90% токенов на поиск символов.

### Четвёртый шаг: INTENTS для быстрого routing (A.R.C. v4)

Если задача неочевидна или агент хочет проверить, какие файлы релевантны запросу — проверить:

```text
INTENTS.md
```

Этот файл mapping'ит пользовательские фразы («скрыть колонку», «добавить товар») на конкретные файлы в проекте. 50+ mapping записей в 7 категориях. Вместо исследования кодовой базы агент может сразу перейти к релевантным файлам.

### Пятый шаг: DOCUMENTATION_MATRIX

На фазе **Document** цикла работы агент обязан свериться с:

```text
DOCUMENTATION_MATRIX.md
```

Этот файл говорит, какие `agents/docs/*.md` обновить при изменении конкретного исходника. Делает документирование механическим.

---

## 3. Critical domain

Критичными областями являются:

- расчёт цены;
- расчёт итогов;
- размеры;
- Anwis-формула;
- коммерческое предложение / КП;
- заводские размеры;
- монтаж;
- автообновление;
- release process;
- `releases.json`.

Агент не имеет права менять эти области без:

1. чтения релевантных `agents/docs`;
2. плана изменений;
3. проверки тестами/сборкой;
4. обновления документации (свериться с `DOCUMENTATION_MATRIX.md`);
5. обновления `CHANGELOG.md`;
6. явного указания рисков.

---

## 4. Правило подтверждения владельцем

Файл `CALCULATION_TEST_CASES.md` может содержать статусы:

- `baseline из кода`
- `подтверждено владельцем`
- `Требует подтверждения владельца`

Запрещено ставить статус `подтверждено владельцем`, если владелец явно не подтвердил кейс. Если агент видит непроверенный кейс — оставить `Требует подтверждения владельца`.

---

## 5. Правило четырёх типов размеров

Агент обязан различать 4 типа размеров:

1. **Введённые размеры** (`ШиринаВвод`/`ВысотаВвод`) — то, что ввёл пользователь.
2. **Расчётные размеры** (`Width`/`Height`) — размеры после Anwis-формулы.
3. **Заводские размеры** (`ШиринаЗавод`/`ВысотаЗавод`) — расчётные размеры минус 20 мм.
4. **Размеры в КП** — хранимые `Width`/`Height`, то есть расчётные размеры.

Нельзя смешивать эти размеры. КП берёт `Width`/`Height`. Завод получает `ШиринаЗавод`/`ВысотаЗавод` = расчётные −20.

---

## 6. Известная грабля по монтажу

В `OrderItem.Installation.cs` есть свойство `InstallationSurcharge`. Исторический XML-комментарий утверждал `Default 0 ₽`, но фактическая бизнес-логика кода даёт `500 ₽` для обоих режимов вычета (`_installationDeduction = 500`, `_installationSurcharge = 500`).

Правило: **код важнее комментария**. XML-комментарии могут устареть и ввести AI в заблуждение. Перед правкой монтажа обязательно читать `GOTCHAS.md#11`.

---

## 7. Release / Auto-update safety (КРИТИЧНО)

`releases.json` — рубильник автообновления (публикация в `main` необратимо триггерит обновление у всех пользователей).

**Канонические дома:**

- `RELEASE_PROCESS.md` (раздел «Канонический Pipeline релиза») — полный release pipeline, ⚠️ правило безопасности, git push sequence, `git checkout --theirs releases.json` при конфликте, **Запрещено**-список.
- `AUTO_UPDATE.md` — диагностика «не видит обновление» (CDN-кэш, raw vs api endpoint).

---

## 8. Универсальный цикл работы агента

```text
Intake → Context → Plan → Execute → Verify → Document → Report
```

| Фаза | Действие |
|------|----------|
| **Intake** | Понять задачу, критичность и затронутые области |
| **Context** | Прочитать `CHEATSHEET.md` → `CURRENT_STATE.md` → routing-таблица → релевантные `agents/docs` |
| **Plan** | Краткий план изменений |
| **Execute** | Менять только нужные файлы |
| **Verify** | Запустить сборку/тесты: `dotnet build`, `dotnet test`, или проектные аналоги |
| **Document** | Обновить `CHANGELOG.md`, `CURRENT_STATE.md` и файлы из `DOCUMENTATION_MATRIX.md` |
| **Report** | Финальный отчёт по шаблону (см. ниже) |

### Шаблон финального отчёта

```md
## Сделано

## Изменённые файлы

## Расчётная логика затронута?
- да/нет

## Документация обновлена

## Метрики (feedback loop)
- шагов/токенов, сэкономленных системой (например: «класс найден по SYMBOL_INDEX за 1 чтение»)
- если система ничего не сэкономила — честно написать, что нет

## Проверки

## Риски / TODO

## Следующий шаг
```

---

## 9. Инструменты автоматизации

В проекте есть 6 скриптов, которые делают документирование механическим:

| Скрипт | Назначение |
|--------|-----------|
| `agents/scripts/gensymbols.ps1` | Сканирует .cs файлы → генерирует `SYMBOL_INDEX.md`. **Запускать после добавления/удаления классов.** |
| `agents/scripts/what-to-update.ps1 $(git diff --name-only)` | Принимает список изменённых файлов → выводит, какие `agents/docs/*.md` обновить. Читает `documentation-matrix.json`. |
| `agents/scripts/validate-docs.ps1` | 11 автоматических проверок: версия, MODULES, CHEATSHEET cross-refs, MATRIX cross-refs, CONTROL cross-refs, полнота, git-based, staleness, releases.json, **self-maintenance (устаревание версии, мягкая, #10)**, **CONTROL#N link resolution (жёсткая, #11)**. |
| `agents/scripts/sync-version.ps1` | Синхронизация версии из csproj во все `agents/docs/*.md` (секция `## Last verified`). Запускать после релиза. |
| `generate-update-log.ps1` (корень) | Генерирует `update-log.json` из `CHANGELOG.md` (при релизе). |
| `agents/scripts/render-matrix.ps1` | Генерирует `DOCUMENTATION_MATRIX.md` из `documentation-matrix.json`. |
| `agents/scripts/arc-check.ps1` | Проверяет, что все agents/docs актуальны. Используется как pre-commit safety net. |

**Обязательный финальный ритуал после любых изменений** (включая изменения самих agents/docs — это тоже «изменения», CONTROL#13):

```powershell
# 1. Узнать что обновить
agents/scripts/what-to-update.ps1 $(git diff --name-only)

# 2. Обновить перечисленные agents/docs/*.md

# 3. Проверить консистентность
powershell -ExecutionPolicy Bypass -File agents/scripts/validate-docs.ps1
```

Агент должен выполнять этот ритуал на фазе **Document** цикла работы.

Источник матрицы — `documentation-matrix.json`. При добавлении нового файла в проект добавить запись в JSON и запустить `agents/scripts/render-matrix.ps1`.

---

## 10. Prompt-шаблоны

Готовые prompt-шаблоны для типовых сценариев вынесены в:

```text
PROMPTS.md
```

Не дублируй их здесь. Используй по необходимости.

---

## 11. Prompt для нового агента (краткий)

```
Ты — агент проекта A.R.C. Frame. Следуй каноническому master-файлу:

MULTI_AGENT_ARC_CALC_CONTROL.md

Перед работой прочитай:
- CHEATSHEET.md          (критические правила + routing)
- CURRENT_STATE.md       (текущее состояние)

Затем следуй routing-таблице в CHEATSHEET.md.

На фазе Document используй DOCUMENTATION_MATRIX.md.

Систему agents/docs держи в актуальности: структурные изменения кода → SYMBOL_INDEX/INTENTS/MODULES (CONTROL#13).

Цикл: Intake → Context → Plan → Execute → Verify → Document → Report.

В конце — финальный отчёт: что сделано, какие файлы изменены, затронута ли расчётная логика, документация обновлена, проверки, риски, следующий шаг.
```

---

## 12. План системного рефакторинга

Проект находится в активной фазе устранения архитектурного долга (God-classes, high coupling). Детальный план по фазам, компонентам, тестам и критериям успеха зафиксирован в:

```text
REFACTORING_PLAN.md
```

### Ключевые направления

| Фаза | Цель | Выделяемые компоненты |
|------|------|------------------------|
| 1 | `MainWindow.xaml.cs` — God-class | `NavigationService`, `OverlayManager`, `SlopeOverlayCoordinator` |
| 2 | `UpdateService.cs` — смешение ответственностей | `UpdateManifestClient`, `VersionResolver`, `UpdateDownloader`, `UpdateVerifier`, `UpdatePresenter` |
| 3 | `PrintService.cs` — рендеринг + очередь | `PrintQueueResolver`, `FixedDocumentBuilder`, `PrintOrchestrator` |
| 4 | `DialogService.cs` — UI в коде | XAML-шаблоны, `DialogBuilder` |
| 5 | `OrderItem.cs` — домен + каталог | `ProductCatalog`, `AnwisSizeCalculator`, `SlopeCalculationExtensions` |
| 6 | `MainWindow.Orders.cs` — UI + сериализация | `OrderImportExportService`, `OrderDialogService`, `OrderGridPresenter` |
| 7 | Валидация и документация | `dotnet test`, `validate-docs.ps1`, `gensymbols.ps1` |

### Правила рефакторинга для агентов

- **Baseline до фазы:** `dotnet build MosquitoNetCalculator.sln -c Release` + `dotnet test`.
- **Бизнес-логика не трогается** — формулы Anwis, цены, монтаж, итоги, печать КП, автообновление, сериализация.
- **После каждой фазы:** `code-reviewer-kimi` + `validate-docs.ps1`.
- **Документы к обновлению:** `CURRENT_STATE.md`, `CHANGELOG.md`, `SYMBOL_INDEX.md`, `DOCUMENTATION_MATRIX.md`, `MODULES.md`, `DECISIONS.md`.

### Routing при задачах рефакторинга

Если задача касается рефакторинга — агент обязан:

1. Прочитать `REFACTORING_PLAN.md`.
2. Определить, к какой фазе относится изменение.
3. Зафиксировать baseline.
4. Выполнить фазу согласно плану.
5. Обновить документацию по `DOCUMENTATION_MATRIX.md`.

---

## 13. Обязанность самоподдержания системы (SELF-MAINTENANCE DUTY) ⚠️

Система A.R.C. жива, только пока её держат в актуальности. Каждый агент **обязан**
обновлять документацию, если его изменения кода или процесса сделали её неактуальной.
**Устаревшая документация хуже отсутствующей — она активно вводит в заблуждение.**

### Когда документация обязана быть обновлена (ситуации А–Ж)

| # | Ситуация | Что обновить |
|---|---|---|
| А | Разбил большой файл на части | `SYMBOL_INDEX.md` (через `gensymbols.ps1`), `INTENTS.md`, `MODULES.md`, `DOCUMENTATION_MATRIX.md`, `CURRENT_STATE.md` |
| Б | Переименовал/переместил файл или класс | `SYMBOL_INDEX.md` (через `gensymbols.ps1`), `INTENTS.md`, `MODULES.md`, `DOCUMENTATION_MATRIX.md` |
| В | Добавил новый класс/сервис/модуль | `SYMBOL_INDEX.md` (через `gensymbols.ps1`), `INTENTS.md` (если есть intent), `MODULES.md` |
| Г | Изменил сигнатуры методов/свойств (упомянутых в SYMBOL_INDEX) | `SYMBOL_INDEX.md` (через `gensymbols.ps1`) |
| Д | Изменил бизнес-логику | `CALCULATION_LOGIC.md`, `GOTCHAS.md`, `CALCULATION_TEST_CASES.md` + critical domain ритуал (§3) |
| Е | Изменил процессы (релиз, автообновление) | `RELEASE_PROCESS.md`, `AUTO_UPDATE.md` |
| Ж | Изменил саму систему документации (этот файл, routing, CHEATSHEET) | `CHEATSHEET.md`, `AGENTS.md`, `CURRENT_STATE.md` |

### Правила самоподдержания

1. **Право и обязанность:** ИИ может и должен править `AGENTS.md` и `agents/docs/*.md`,
   если данные неактуальны. Это не «самодеятельность», а часть задачи.
2. **Фиксируй в том же цикле:** документацию обновляй на фазе **Document** того же
   цикла, где сделал изменения кода, — не «потом» и не «в следующей задаче».
3. **Код важнее комментария** (§6), но **документация следует за кодом**: если docs
   противоречат коду — обнови docs (при сомнении — спроси владельца).
4. **Мягкий контроль:** `validate-docs.ps1` предупреждает (warning), но не блокирует.
   Если проверка указала на рассинхрон — исправь до финального отчёта.
5. **Метрики (feedback loop):** в финальном отчёте указывай, сколько шагов/токенов
   сэкономила система (например, «класс найден по SYMBOL_INDEX за 1 чтение»).
   Это позволяет владельцу верифицировать пользу A.R.C. (причина №1).
6. **Проверка после правок docs:** `what-to-update.ps1` → обновить перечисленные →
   `validate-docs.ps1` (ритуал §9).
7. **Разбиение больших файлов — не «когда-нибудь», а прямо сейчас.** Если ты
   (агент) пишешь код и видишь, что файл разрастается (правило-ориентир:
   **> 400–500 строк**, или несколько несвязанных ответственностей в одном
   файле) — **разбей его на части сам**, в том же цикле, а не оставляй
   «рефакторинг на потом». Большой файл = дорогое чтение для следующих агентов
   (токены на контекст, риск пропустить важное). Разбивка — это не
   «дополнительная работа», а способ облегчить задачу **себе же** в будущем
   (и владельцу). После разбивки — ритуал ситуации А (gensymbols + INTENTS +
   MODULES + matrix). Правило-ориентир не жёсткий лимит: файлы < 400 строк
   тоже стоит разбить, если ответственности разные (например, частичная
   декомпозиция `MainWindow.*.cs` — это норма).

---

## Source files

- `CHEATSHEET.md` — быстрый вход
- `DOCUMENTATION_MATRIX.md` — карта «файл → документы»
- `PROMPTS.md` — prompt-шаблоны
- `CURRENT_STATE.md` — текущее состояние
- `MODULES.md` — карта модулей
- `DECISIONS.md` — принятые решения
- `GOTCHAS.md` — опасные места
- `validate-docs.ps1` — валидация документации

## Last verified
2026-08-10 (v3.47.4) — auto-synced from csproj (sync-version.ps1, CONTROL#13).


2026-08-03 (v3.47.3) — добавлен §13 «Обязанность самоподдержания системы» (ситуации А–Ж, метрики в шаблоне отчёта, routing для структурных изменений); правило подтверждено владельцем по ТЗ `ТЗ_самоподдержание_AGENTS_2026-08-03.md`.

2026-07-17 — документ просмотрен и синхронизирован с текущим состоянием проекта (v3.46.1): переключатель +/- для монтажа (SignToggleCheckBox), только значки V/X/В в таблице, динамическая ширина колонки в PdfExportService, жирные значения в клиентском блоке КП, форматирование примечаний (жирный/курсив/цвет/списки). **1227/1227 tests pass.**
