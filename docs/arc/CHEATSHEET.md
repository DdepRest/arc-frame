# CHEATSHEET — критические правила (читай перед любой задачей)

Правила без объяснений. Подробности — по ссылкам.

```
 1. Цикл работы:    Intake→Context→Plan→Execute→Verify→Document→Report    → CONTROL#8
 2. НЕ менять формулы без плана + тестов + разрешения владельца             → CALCULATION_LOGIC
 3. IsAnwis всегда проверять перед Anwis-формулами                          → GOTCHAS#1
 4. НЕ сохранять derived-поля (CalculatedValue, Total) в JSON               → GOTCHAS#3
 5. 4 типа размеров не смешивать: Ввод / Расчёт / Завод (−20) / КП          → CALCULATION_LOGIC#4
 6. SetAnwisModeQuiet при загрузке/клоне, НЕ публичный setter               → GOTCHAS#2
 7. Код > комментарий. XML-комментарий может врать                          → GOTCHAS#11
 8. Версия — только в .csproj (<Version>X.Y.Z</Version>)                    → RELEASE_PROCESS
 9. Все данные в %AppData%, не рядом с .exe                                 → GOTCHAS#9
10. EscapeHtml для всех строковых полей в КП                                → GOTCHAS#6
11. releases.json пушить ПОСЛЕ GitHub Release + ZIP (полный pipeline: RELEASE_PROCESS • CDN-диагностика: AUTO_UPDATE) → RELEASE_PROCESS
12. При добавлении товара → DefaultPrices + ApplyMigrations                 → GOTCHAS#4
13. Обновить CHANGELOG.md + CURRENT_STATE.md после изменений                → CONTROL#8
14. Статус «Подтверждено владельцем» — только после явного подтверждения    → CONTROL#4
15. Single-file publish: Assembly.GetName().Version = null                   → GOTCHAS#8
16. Перед изменениями сверься с DOCUMENTATION_MATRIX.md                     → DOCUMENTATION_MATRIX
17. После изменений: what-to-update.ps1 → обнови docs → validate-docs.ps1   → скрипты
17a. AI Agent Mode: full status table (Этап 0–12) — CURRENT_STATE.md → секция «AI Agent Mode — Progress».
     План: [`../../ai-agent-mode-plan.md`](../../ai-agent-mode-plan.md).
17b. Release-flow AI-регрессии: `dotnet test --filter "FullyQualifiedName~AiGoldenCase|FullyQualifiedName~AiPlan|FullyQualifiedName~AiTelemetry"`.
     `what-to-update.ps1 -RunAiTests` гоняет его автоматически; если в diff есть AI-файлы, скрипт предупреждает в любом случае.
18. Структурные изменения (разбивка/переименование/новые классы) → gensymbols.ps1 + INTENTS/MODULES/matrix → CONTROL#13
19. AGENTS.md/docs держать в актуальности — обязанность агента, не опция       → CONTROL#13
20. В отчёте указывай метрики экономии (шагов/токенов), честно если ноль       → CONTROL#8
```

## Быстрый routing

Только что вошёл? Используй **INTENTS.md** для полного mapping'а намерений.
Нужен конкретный символ/класс? Используй **SYMBOL_INDEX.md** (60 классов, 16 модулей).
Нужен статус AI Agent Mode? Используй **CURRENT_STATE.md → «AI Agent Mode — Progress»** (Этап 0–12, полная таблица).

```
Задача про →            Читай (кроме CHEATSHEET)
──────────────────────────────────────────────────────────
Расчёты, формулы, Anwis  CURRENT_STATE + CALCULATION_LOGIC + TEST_CASES + GOTCHAS
Релиз, автообновление   CURRENT_STATE + RELEASE_PROCESS + AUTO_UPDATE
UI, темы, стили          CURRENT_STATE + GOTCHAS#7 + DECISIONS#10
Печать КП                CURRENT_STATE + GOTCHAS#6 + CALCULATION_LOGIC#КП
Сохранение/загрузка      CURRENT_STATE + GOTCHAS#3,#9,#2 + DECISIONS#3
Цены                     CURRENT_STATE + GOTCHAS#4 + CALCULATION_LOGIC#цены
Тесты                    CURRENT_STATE + CALCULATION_TEST_CASES
Структурные изменения    CONTROL#13 + gensymbols.ps1 + INTENTS/MODULES/matrix
AI Agent Mode            CURRENT_STATE → «AI Agent Mode — Progress» + ai-agent-mode-plan.md
AI-регрессии (релиз)     dotnet test --filter AiGoldenCase|AiPlan|AiTelemetry
                         (или what-to-update.ps1 -RunAiTests)
Навигация по коду        SYMBOL_INDEX.md (index классов/методов/свойств)
Понимание намерений       INTENTS.md (mapping фраз на файлы)
Всё остальное            CURRENT_STATE
Тривиально (≤10 строк)   Только CHEATSHEET, затем grep GOTCHAS.md по имени изменённого файла
```

## Термины

| Термин | Определение |
|--------|------------|
| Ввод (raw) | Что пользователь набрал (`ШиринаВвод`/`ВысотаВвод`) |
| Расчёт / хранение | После Anwis-коррекции (`Width`/`Height`) — для цены и КП |
| Завод | Расчёт − 20 мм (`ШиринаЗавод`/`ВысотаЗавод`) |
| КП | = Расчётные размеры (`Width`/`Height`) |
| A.R.C. | Agent Reference & Control — система AI-документации проекта |
| AI Agent Mode | План развития AI [`ai-agent-mode-plan.md`](../../ai-agent-mode-plan.md); статус — CURRENT_STATE.md → «AI Agent Mode — Progress» |
| plan-mode | Контракт ответа модели: `{mode: plan\|answer\|clarification\|explanation, reply, requires_confirmation, steps[]}` |

## Инструменты автоматизации

```
gensymbols.ps1                                # Генерация SYMBOL_INDEX.md (индекс классов)
what-to-update.ps1 $(git diff --name-only)   # Что обновить в docs? + рекомендованный AI-фильтр
                                             # С флагом -RunAiTests реально запускает AI-регрессии
validate-docs.ps1                             # 10 проверок консистентности (вкл. self-maintenance, мягкая)
generate-update-log.ps1                       # CHANGELOG.md → update-log.json
render-matrix.ps1                             # JSON → DOCUMENTATION_MATRIX.md
arc-check.ps1                                 # Проверка docs перед коммитом
```

## Source files

- `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md` — полные правила
- `docs/arc/DOCUMENTATION_MATRIX.md` — карта «файл → документы» (генерируется из JSON)
- `docs/arc/documentation-matrix.json` — машиночитаемый источник матрицы
- `docs/arc/CURRENT_STATE.md` — текущее состояние проекта (включая «AI Agent Mode — Progress» Этап 0–12)
- `docs/arc/SYMBOL_INDEX.md` — индекс классов/методов/свойств (60 классов, 16 модулей)
- `docs/arc/INTENTS.md` — mapping намерений на файлы
- `ai-agent-mode-plan.md` — полный план AI Agent Mode (13 этапов: Этап 0–12)

## Last verified

2026-08-05 — добавлены правила **17a** (AI Agent Mode — таблица Этап 0–12 в CURRENT_STATE.md) и **17b** (release-flow AI-регрессии: `dotnet test --filter AiGoldenCase|AiPlan|AiTelemetry`, автоматически через `what-to-update.ps1 -RunAiTests`); новая routing-строка «AI Agent Mode»; ссылка на план в Source files.

2026-08-03 (v3.47.3) — добавлены правила 18–20 (самоподдержание: структурные изменения → gensymbols + INTENTS/MODULES; обязанность держать docs актуальными; метрики в отчёте) + routing-строка «Структурные изменения».

2026-07-02 (v3.42.0 — slide-out навигация, sidebar без chevrons, ПСУЛ/Уплотнение упрощённый ввод, Антикошка toggle в отдельной строке, WebView2 fix + DependencyChecker)
