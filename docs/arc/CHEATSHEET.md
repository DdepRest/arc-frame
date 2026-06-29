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
11. releases.json пушить ПОСЛЕ GitHub Release + ZIP                         → RELEASE_PROCESS
12. При добавлении товара → DefaultPrices + ApplyMigrations                 → GOTCHAS#4
13. Обновить CHANGELOG.md + CURRENT_STATE.md после изменений                → CONTROL#8
14. Статус «Подтверждено владельцем» — только после явного подтверждения    → CONTROL#4
15. Single-file publish: Assembly.GetName().Version = null                   → GOTCHAS#8
16. Перед изменениями сверься с DOCUMENTATION_MATRIX.md                     → DOCUMENTATION_MATRIX
17. После изменений: what-to-update.ps1 → обнови docs → validate-docs.ps1   → скрипты
```

## Быстрый routing

Только что вошёл? Используй **INTENTS.md** для полного mapping'а намерений.
Нужен конкретный символ/класс? Используй **SYMBOL_INDEX.md** (60 классов, 16 модулей).

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

## Инструменты автоматизации

```
gensymbols.ps1                                # Генерация SYMBOL_INDEX.md (индекс классов)
what-to-update.ps1 $(git diff --name-only)   # Что обновить в docs?
validate-docs.ps1                             # 8 проверок консистентности
generate-update-log.ps1                       # CHANGELOG.md → update-log.json
render-matrix.ps1                             # JSON → DOCUMENTATION_MATRIX.md
arc-check.ps1                                 # Проверка docs перед коммитом
```

## Source files

- `docs/arc/MULTI_AGENT_ARC_CALC_CONTROL.md` — полные правила
- `docs/arc/DOCUMENTATION_MATRIX.md` — карта «файл → документы» (генерируется из JSON)
- `docs/arc/documentation-matrix.json` — машиночитаемый источник матрицы
- `docs/arc/CURRENT_STATE.md` — текущее состояние проекта
- `docs/arc/SYMBOL_INDEX.md` — индекс классов/методов/свойств (60 классов, 16 модулей)
- `docs/arc/INTENTS.md` — mapping намерений на файлы

## Last verified

2026-06-29 (v3.39.0 — background auto-update checks via UpdateCheckScheduler: periodic 30 мин + idle 10 мин + persistent notification toast с кнопками [Обновить]/[Позже])
