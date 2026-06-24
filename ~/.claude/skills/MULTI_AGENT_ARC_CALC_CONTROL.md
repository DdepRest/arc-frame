# External Bootstrap Wrapper — A.R.C. Calculation Project

> **This file is NOT the canonical source of truth.**
>
> Этот файл — внешний bootstrap loader для Claude-инструментов. Он не содержит правил проекта и обязан загрузить настоящий canonical master-файл из репозитория.

Канонический multi-agent control file находится в текущем репозитории:

```text
docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md
```

---

## Что делать

**До любой нетривиальной задачи:**

1. Найди корень текущего репозитория (`git rev-parse --show-toplevel` или эквивалент).
2. Прочитай:

```text
docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md
```

3. Следуй маршруту, который он задаёт.
4. Затем прочитай:

```text
docs/arc/CURRENT_STATE.md
```

---

## Если репозиторный master-файл недоступен

Если `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md` отсутствует или нечитаем:

- **Остановись и сообщи владельцу**, что невозможно загрузить канонический master-файл.
- **Не подставляй этот external skill как source of truth.**
- **Не придумывай правила** на основе содержимого этого файла.

---

## Ограничения

- Этот external skill — **не** source of truth.
- Запрещено дублировать правила проекта в этом файле.
- Если этот файл противоречит `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md`, приоритет всегда у **репозиторного master-файла**.
- Изменения правил проекта редактируются в `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md`, не здесь.

---

*Этот файл — тонкий loader, не контейнер правил. Все правила проекта — в `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md`.*
