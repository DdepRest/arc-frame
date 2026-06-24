# Multi-Agent Control Plan — A.R.C. / расчёты товаров, размеров, Anwis, КП, завод

Этот документ адаптирует AI-самоконтроль под проект, который делают разные агенты.

Проект уже имеет существующую структуру:

```text
docs/arc/
  MULTI_AGENT_ARC_CALC_CONTROL.md   ← этот файл (source of truth)
  CHEATSHEET.md                     ← быстрый вход (читай первым!)
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

Главная задача: сделать так, чтобы любой агент, входящий в проект, не ломал расчёты, размеры, Anwis-логику, КП, заводские размеры, релизный процесс и автообновление — и тратил минимум токенов на понимание проекта.

---

## 1. Источник истины для всех агентов

Единственный канонический master-файл для всех агентов:

```text
docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md
```

Этот файл является source of truth для поведения AI-агентов в проекте.

Файлы вне `docs/arc/`:

```text
AGENT.md
AGENTS.md
CLAUDE.md
GEMINI.md
~/.claude/skills/MULTI_AGENT_ARC_CALC_CONTROL.md
```

являются только совместимыми thin wrappers / redirect-файлами. Они не должны дублировать правила проекта. Если wrapper-файл противоречит этому файлу — приоритет у этого файла.

Проектная память и фактическое состояние проекта находятся в `docs/arc/`.

---

## 2. Обязательный вход любого агента (token-optimised)

### Первый шаг: CHEATSHEET (всегда, ~40 строк)

Перед любой нетривиальной задачей агент обязан прочитать:

```text
docs/arc/CHEATSHEET.md
```

Это даёт критические правила и routing-таблицу за 15 секунд.

### Второй шаг: полные файлы по routing-таблице

Затем агент читает `docs/arc/CURRENT_STATE.md` и, в зависимости от задачи, дополнительные файлы:

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
| Всё остальное | Только `CURRENT_STATE.md` |
| **Тривиальная задача (≤10 строк, не critical domain)** | Только `CHEATSHEET.md`, затем проверь `GOTCHAS.md` по ключевым словам |

Если агент физически не имеет доступа к `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md` — остановиться и сообщить владельцу. Запрещено использовать `AGENT.md` как source of truth.

### Третий шаг: DOCUMENTATION_MATRIX

На фазе **Document** цикла работы агент обязан свериться с:

```text
docs/arc/DOCUMENTATION_MATRIX.md
```

Этот файл говорит, какие `docs/arc/*.md` обновить при изменении конкретного исходника. Делает документирование механическим.

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

1. чтения релевантных `docs/arc`;
2. плана изменений;
3. проверки тестами/сборкой;
4. обновления документации (свериться с `DOCUMENTATION_MATRIX.md`);
5. обновления `CHANGELOG.md`;
6. явного указания рисков.

---

## 4. Правило подтверждения владельцем

Файл `docs/arc/CALCULATION_TEST_CASES.md` может содержать статусы:

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

Правило: **код важнее комментария**. XML-комментарии могут устареть и ввести AI в заблуждение. Перед правкой монтажа обязательно читать `docs/arc/GOTCHAS.md#11`.

---

## 7. Release / Auto-update safety

`releases.json` — это рубильник автообновления. Файл читается напрямую с `raw.githubusercontent.com`.

**Безопасный порядок:**

1. Собрать ZIP.
2. Посчитать SHA-256.
3. Создать GitHub Release и загрузить ZIP-asset.
4. **Только после этого** опубликовать/запушить обновлённый `releases.json` в `main`.

Запрещено пушить `releases.json` раньше GitHub Release — пользователи увидят обновление, которое нельзя скачать.

---

## 8. Универсальный цикл работы агента

```text
Intake → Context → Plan → Execute → Verify → Document → Report
```

| Фаза | Действие |
|------|----------|
| **Intake** | Понять задачу, критичность и затронутые области |
| **Context** | Прочитать `CHEATSHEET.md` → `CURRENT_STATE.md` → routing-таблица → релевантные `docs/arc` |
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

## Проверки

## Риски / TODO

## Следующий шаг
```

---

## 9. Инструменты автоматизации

В проекте есть 4 скрипта, которые делают документирование механическим:

| Скрипт | Назначение |
|--------|-----------|
| `what-to-update.ps1 $(git diff --name-only)` | Принимает список изменённых файлов → выводит, какие `docs/arc/*.md` обновить. Читает `documentation-matrix.json`. |
| `validate-docs.ps1` | 8 автоматических проверок: версия, ссылки MODULES, CHEATSHEET cross-refs, MATRIX cross-refs, CONTROL cross-refs, полнота docs/arc, git-based Last verified, staleness. |
| `generate-update-log.ps1` | Генерирует `update-log.json` из `CHANGELOG.md` (при релизе). |
| `render-matrix.ps1` | Генерирует `DOCUMENTATION_MATRIX.md` из `documentation-matrix.json`. |

**Обязательный финальный ритуал после любых изменений:**

```powershell
# 1. Узнать что обновить
what-to-update.ps1 $(git diff --name-only)

# 2. Обновить перечисленные docs/arc/*.md

# 3. Проверить консистентность
powershell -ExecutionPolicy Bypass -File validate-docs.ps1
```

Агент должен выполнять этот ритуал на фазе **Document** цикла работы.

Источник матрицы — `docs/arc/documentation-matrix.json`. При добавлении нового файла в проект добавить запись в JSON и запустить `render-matrix.ps1`.

---

## 10. Prompt-шаблоны

Готовые prompt-шаблоны для типовых сценариев вынесены в:

```text
docs/arc/PROMPTS.md
```

Не дублируй их здесь. Используй по необходимости.

---

## 11. Prompt для нового агента (краткий)

```
Ты — агент проекта A.R.C. Frame. Следуй каноническому master-файлу:

docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md

Перед работой прочитай:
- docs/arc/CHEATSHEET.md          (критические правила + routing)
- docs/arc/CURRENT_STATE.md       (текущее состояние)

Затем следуй routing-таблице в CHEATSHEET.md.

На фазе Document используй docs/arc/DOCUMENTATION_MATRIX.md.

Цикл: Intake → Context → Plan → Execute → Verify → Document → Report.

В конце — финальный отчёт: что сделано, какие файлы изменены, затронута ли расчётная логика, документация обновлена, проверки, риски, следующий шаг.
```

---

## Source files

- `docs/arc/CHEATSHEET.md` — быстрый вход
- `docs/arc/DOCUMENTATION_MATRIX.md` — карта «файл → документы»
- `docs/arc/PROMPTS.md` — prompt-шаблоны
- `docs/arc/CURRENT_STATE.md` — текущее состояние
- `docs/arc/MODULES.md` — карта модулей
- `docs/arc/DECISIONS.md` — принятые решения
- `docs/arc/GOTCHAS.md` — опасные места
- `validate-docs.ps1` — валидация документации

## Last verified

2026-06-24 (переработан: гранулярный routing, CHEATSHEET, DOCUMENTATION_MATRIX, PROMPTS вынесены)
