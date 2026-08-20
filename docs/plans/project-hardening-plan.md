# План устранения слабостей проекта — A.R.C. Frame (9 пунктов)

> Составлен по анализу проекта 2026-08-19 (v3.47.4, 1746/1746 tests pass).
> Назначение: поэтапное устранение 9 выявленных слабостей без изменения расчётной логики.
> Смежные планы, которые НЕ дублируются здесь:
> - `REFACTORING_PLAN_BIG_FILES.md` — уже содержит «Фазу B» по разбиению `AiAssistantService.cs`.
> - `ai-agent-mode-plan.md` — контракт plan-mode и целевой поток AI.

---

## 1. Карта «слабость → этап»

| # | Слабость (из анализа) | Этап | Приоритет |
|---|---|---|---|
| 6 | Golden-кейсы не покрывают слой перехвата | **Этап 0** | P0 |
| 4 | Предохранитель «не выдумывай» не централизован | **Этап 1** | P0 |
| 5 | Класс «молчаливый дефолт» закрыт не до конца (цена, цвет) | **Этап 1** | P0 |
| 2 | Три источника правды (цены/каталог/цвета/ключевые слова) | **Этап 2** | P1 |
| 3 | Системный промпт зашит в C# | **Этап 2** | P1 |
| 1 | AI-подсистема — новые God-классы | **Этап 3** | P1 |
| 7 | Релизная дисциплина (7+ Unreleased, версия 3.47.4) | **Этап 4** | P2 |
| 8 | Безопасность (пароль, Gist-токен, ключ, телеприватность) | **Этап 5** | P2 |
| 9 | Мелкая рассинхронизация доков (GOTCHAS, AI-план) | **Этап 6** | P3 |

**Порядок выполнения (жёсткий):**
```text
Этап 0 → 1 → 2 → 3 → 4 → 5 → 6
```
- 0/1 создают регрессионный каркас ДО рефакторинга (иначе пункты 2–3 рискованны).
- 2 должен идти ДО 3: разбивать спутанный код бессмысленно; сначала один источник фактов.
- 4/5/6 независимы по коду и могут идти параллельно после 3 (рекомендуется последовательно, чтобы не смешивать релизный коммит с рефакторингом).

---

## 2. Baseline и контрольные точки

Перед каждой фазой и после неё:

```powershell
dotnet build MosquitoNetCalculator.sln -c Release
dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj -c Release
# AI-регрессии (CHEATSHEET #17b)
dotnet test --no-build -c Release --filter "FullyQualifiedName~AiGoldenCase|FullyQualifiedName~AiPlan|FullyQualifiedName~AiTelemetry"
powershell -ExecutionPolicy Bypass -File agents/scripts/validate-docs.ps1
```

**Текущий baseline (2026-08-19):** 1746/1746 tests pass, v3.47.4, validate-docs 0 issues.

**Ритуал документации после каждой фазы** (CONTROL §9): `what-to-update.ps1 $(git diff --name-only)` → обновить перечисленные `agents/docs/*.md` → `validate-docs.ps1` → `gensymbols.ps1` при новых классах.

---

## 3. Этап 0 — Регрессионный каркас на слое перехвата (пункт 6)

### Цель
Запереть текущее поведение `FinalizeStreamingMessage` / `ShouldAskForMissingParams` тестами, чтобы последующие правки (этапы 1–3) ничего не сломали. Сейчас `golden-cases.json` (15 кейсов) проверяет только parser/plan, а баги Anwis-профиля и монтажа жили именно на слое перехвата.

### Файлы
- `MosquitoNetCalculator.Tests/AI/golden-cases.json` — добавить кейсы.
- `MosquitoNetCalculator.Tests/Services/AiGoldenCaseTests.cs` — расширить ассерты при необходимости.
- `MosquitoNetCalculator.Tests/ViewModels/AiAssistantViewModelTests.cs` — прямые offline-тесты `FinalizeStreamingMessage`.
- `MosquitoNetCalculator.Tests/Models/AiClarificationFormTests.cs` — уже частично покрыт монтаж (дополнить пропущенное).

### Шаги
1. Зафиксировать baseline (сборка + полный `dotnet test`).
2. Добавить golden-кейсы (id + user_text + model_response):
   - `add-otliv-missing-installation` — «отлив бел 170 900» + `add_item` без `installation_mode` → ожидание: уточнение/карточка, НЕ выполнение.
   - `add-otliv-with-installation` — «отлив бел 170 900 с монтажом» + `installation_mode:0` → add_item, mode 0.
   - `add-anwis-mode-without-installation` — «Anwis белый 739×1116 ПП» + `add_item` без `installation_mode` → уточнение по монтажу.
   - `add-korob-no-install-toggle` — «короб белый 200×1500» → add_item выполняется (нет монтажного переключателя).
   - `update-items-without-installation` (пункт 5) — если решено спрашивать, кейс на update_items.
3. В `AiGoldenCaseTests` добавить поле/ветку `expected_clarification` (или переиспользовать `expected_mode == "clarification"`), чтобы golden-слой проверял исход «выполнить vs уточнить», а не только параметры плана.
4. Добавить/дополнить `AiAssistantViewModelTests.FinalizeStreamingMessage_*`:
   - монтаж отсутствует → карточка + сообщение «Не указан монтаж…» (есть);
   - монтаж указан → карточка НЕ показывается, план ждёт подтверждения;
   - размеры отсутствуют → «Не хватает параметров…» (есть);
   - приоритет сообщений: Anwis-режим > размеры > монтаж.
5. При желании — recorded-responses harness: файл `Tests/AI/recorded-responses.json` с реальными ответами моделей (Markdown-обёртка, битые JSON, пустые ответы) и тест, прогоняющий их через `AiCommandParser.TryParse`. Online-проверки остаются ручными (как в плане AI Agent Mode §14).

### Критерии готовности
- golden-кейсы покрывают все 4 ветки монтажа/перехвата;
- добавленные offline-тесты зелёные;
- полный прогон без регрессий.

---

## 4. Этап 1 — Централизация предохранителя + аудит «молчаливых дефолтов» (пункты 4, 5)

### Цель
Сделать политику «не выдумывай» единой точкой для ВСЕХ путей создания `AiCommand`, и явно решить, какие дефолты безопасны, а какие обязаны спрашивать.

### 4.1 Централизация (пункт 4)

**Текущее состояние:** `ShouldAskForMissingParams` вызывается только в `AiAssistantViewModel.FinalizeStreamingMessage` (LLM-путь). Команды создаются 4 путями:
1. `AiCommandParser.TryParse` → plan-mode (`TryParsePlanResponse`);
2. `AiCommandParser.TryParse` → legacy `action`;
3. `AiClarificationForm.TryBuildCommand` → `SubmitClarificationForm`;
4. `AiLocalCommandRouter` → `HandleLocalRoute` (например `/очистить`).

**Файлы:** `Services/AiPlanValidator.cs`, `Models/AiClarificationForm.cs`, `ViewModels/AiAssistantViewModel.cs`, `Services/AiLocalCommandRouter.cs`.

**Шаги:**
1. Добавить в `AiPlanValidator.Validate` (или новый статический `AiPlanSafetyPolicy.ValidateNoInventions`) проверку `ShouldAskForMissingParams`-семантики: Anwis без режима, размерный товар с 0-размерами, монтажный товар без монтажа.
   - Важно: `AiPlanValidator` работает на плане/командах + `userRequest`; передать `SourceUserText` плана в проверку (он уже есть в `AiActionPlan.SourceUserText`).
2. `AiPlanValidator.Validate` возвращает новый статус/флаг `NeedsClarification` (или список полей) — чтобы UI мог показать карточку.
3. `FinalizeStreamingMessage` перестаёт дублировать логику и просто читает `NeedsClarification` из валидатора.
4. Прогнать пути 3 и 4 через ту же проверку: `SubmitClarificationForm` и `AiLocalCommandRouter` уже строят план → перед показом подтверждения вызывать `AiPlanValidator.Validate`.
5. Оставить `AiClarificationForm.ShouldAskForMissingParams` как публичный предикат (его использует валидатор), но убрать дублирование из VM.

### 4.2 Аудит «молчаливых дефолтов» (пункт 5)

Составить таблицу решений и зафиксировать в `CALCULATION_LOGIC.md` (или новом `AI_DEFAULTS_POLICY.md`). Предварительный каркас:

| Параметр | Текущий дефолт | Решение | Действие |
|---|---|---|---|
| Количество | `1` | Безопасно (§3.1) | Оставить |
| Тип товара | из запроса | Спрашивать при неоднозначности (§3.2) | Уже есть |
| Anwis-режим | — | Спрашивать (защищено) | Оставить |
| Монтаж | программный дефолт | Спрашивать (защищено, 2026-08-19) | Оставить |
| Глубина откоса | — | Спрашивать (CalcSlope валидация) | Оставить |
| **Цена** | `AiCommandParser.GetDefaultPrice` (хардкод) | Безопасно, НО источник должен быть канонический `PriceService`, а не хардкод | Перенести на этапе 2 |
| **Цвет** | пустой/первый цвет палитры | Один цвет → безопасно; несколько → **решить** | См. ниже |

**Цвет (единственный спорный пункт):**
- Вариант A (консервативный): если у товара >1 цвета и цвет не назван — показывать карточку (как монтаж). Дороже по UX, но соответствует «только полная информация».
- Вариант B: дефолт на первый цвет палитры + явная пометка в предпросмотре «Цвет: Белый (по умолчанию)».
- **Решение владельца обязательно.** До решения оставить текущее поведение, зафиксировав его тестом (не «молча», а «задокументировано»).

**UpdateItems (монтаж/цена/цвет существующих позиций):** проверить, что `UpdateInstallationMode`/`UpdatePrice`/`UpdateColor` не применяются, если пользователь их не назвал (зеркально AddItem). Если есть риск — добавить проверку в валидатор + golden-кейс.

### Критерии готовности
- Все 4 пути создания команд проходят `AiPlanValidator` + политику «не выдумывай»;
- таблица дефолтов зафиксирована в docs с явным решением владельца по цвету;
- golden-кейсы на update_items (если решено).

---

## 5. Этап 2 — Единый источник фактов + промпт из ресурса (пункты 2, 3)

### Цель
Убрать тройное дублирование каталога/цен/цветов/ключевых слов и вынести системный промпт из C#.

### 5.1 AiFactsProvider (пункт 2)

**Файлы (новые):**
- `Services/AiFactsProvider.cs` — статический источник фактов для AI: цены, цвета по товару, ключевые слова (Anwis/монтаж/цвет/количество), labels, категории.
- (опционально узкие) `Services/AiKeywordLexicon.cs` — все regex/словари ключевых слов в одном месте.

**Шаги:**
1. Определить канонические источники:
   - **Цены** → `PriceService.DefaultPrices` (единственный источник). `AiFactsProvider.GetPrice(name, color)` делегирует туда; `AiCommandParser.GetDefaultPrice` становится прокси или удаляется.
   - **Каталог/категории** → `ProductCatalog` (уже единственный).
   - **Цвета по товару** → `AiClarificationForm.ColorMap` переезжает в `AiFactsProvider`; `ProductCatalog` не знает о цветах — оставить в провайдере, но в одном месте.
   - **Ключевые слова** → объединить `AiCommandParser.ParseAnwisModeString` + `AiClarificationForm.DetectAnwisMode`; `ParseInstallationModeField` + `DetectInstallationMode`; `DetectColor` → один `AiKeywordLexicon`.
2. Перевести потребителей: `AiCommandParser`, `AiPlanValidator.KnownColors`, `AiClarificationForm` (ColorMap/DetectColor/DetectAnwisMode/DetectInstallationMode), `AiLocalCommandRouter`, `AiPlanBuilder` (preview labels).
3. Удалить хардкод-таблицу цен из `AiCommandParser.GetDefaultPrice` (оставить тонкий адаптер, если API публичный и нужен для тестов).
4. Тест консистентности: цены в промпте == `PriceService.DefaultPrices`; цвета формы == `AiFactsProvider`.

### 5.2 Промпт из ресурса (пункт 3)

**Файлы:**
- `Resources/ai-system-prompt.md` — статическая часть промпта (правила, примеры, режимы Anwis).
- `Services/AiPromptBuilder.cs` — собирает финальный промпт: статика из ресурса + динамика (orderContext) + каталог/цены/примеры из `AiFactsProvider`.

**Шаги:**
1. Создать embedded resource (`ai-system-prompt.md`, `EmbeddedResource` в csproj).
2. `AiPromptBuilder.Build(orderContext)` читает ресурс и подставляет:
   - таблицу каталога/цен из `AiFactsProvider` (не хардкод);
   - `orderContext` (как сейчас).
3. Перенести `BuildSystemPrompt`/`AppendRecentUpdates`/`FormatUpdateHistory` в `AiPromptBuilder` (это пересекается с `REFACTORING_PLAN_BIG_FILES.md` Фаза B — см. этап 3; можно объединить).
4. Тесты: промпт содержит актуальные цены (сверка с `PriceService`), не падает на пустом контексте, содержит обязательные секции.

### Критерии готовности
- цены/цвета/ключевые слова существуют ровно в одном месте;
- системный промпт читается из ресурса и собирается из фактов;
- консистентность промпта проверяется тестом;
- публичный AI-API не изменился.

---

## 6. Этап 3 — Разбиение God-классов AI (пункт 1)

### Цель
Разнести новую AI-подсистему по ответственностям. Часть уже спланирована — ссылаемся, не дублируем.

**Уже есть:** `REFACTORING_PLAN_BIG_FILES.md` **Фаза B** (`AiAssistantService.cs` → `AiPromptBuilder` + `AiModelCatalogClient` + `AiKeyValidator` + `AiStreamingClient`). После этапа 2 `AiPromptBuilder` должен собирать промпт из фактов/ресурса — выполнять Фазу B **вместе** с этапом 2 (одна правка промпта, не две).

**Недостающее (добавить):**

| Файл | Строк | Точки разбиения |
|---|---|---|
| `ViewModels/AiAssistantViewModel.cs` | 1065 | streaming-оркестрация (буфер/таймер/Dispatcher) vs план-пайплайн (ConfirmPlan/OnPlanExecuted/Undo) vs интеграция карточки vs обработка slash-маршрутов vs history persistence |
| `Models/AiClarificationForm.cs` | 659 | чистый парсинг (regex/детекторы) vs VM-состояние INPC + `TryBuildCommand` |
| `MainWindow.AI.cs` | 665 | уже partial; ревизия на вынос `ExecuteAiCommandCore` в `AiPlanExecutor`-пространство (если не сделано) |

**Предлагаемые компоненты:**
- `ViewModels/AiAssistantViewModel.Streaming.cs` (partial) или `Services/AiStreamingCoordinator.cs` — буфер чанков, `DispatcherTimer`, `FlushPendingText`.
- `ViewModels/AiAssistantViewModel.Plans.cs` (partial) — `ConfirmPlan`/`CancelPlan`/`OnPlanExecuted`/undo-блокировка.
- `Services/AiClarificationParser.cs` — `DetectColor`/`DetectAnwisMode`/`DetectInstallationMode`/regex → после этапа 2 это `AiKeywordLexicon`, форма остаётся тонкой.

**Шаги:**
1. Выполнить `REFACTORING_PLAN_BIG_FILES.md` Фазу B (в связке с этапом 2).
2. Разбить `AiAssistantViewModel` на partial-части по SRP (критерий: каждая < 400–500 строк, разные ответственности).
3. После этапа 2 вынести детекторы из `AiClarificationForm` в `AiKeywordLexicon`; форма держит только состояние + `TryBuildCommand`/`BuildSummaryText`.
4. Обновить `REFACTORING_PLAN_BIG_FILES.md` — добавить «Фазу D» (VM + форма) и актуализировать «Last verified».
5. `gensymbols.ps1` + `what-to-update.ps1` + `validate-docs.ps1`.

### Критерии готовности
- `AiAssistantService.cs` ≤ 500, `AiAssistantViewModel.cs` ≤ ~500 суммарно (по частям), `AiClarificationForm.cs` ≤ ~350;
- публичный API неизменен; полный прогон + AI-регрессии зелёные.

---

## 7. Этап 4 — Релизная дисциплина (пункт 7)

### Цель
Выпустить накопленный «Unreleased» объём и разобраться с дублированием манифестов.

**Файлы:** `CHANGELOG.md`, `.csproj` (`<Version>`), `releases.json`, `Resources/update-log.json`, `agents/scripts/sync-version.ps1`, `RELEASE_PROCESS.md`.

**Шаги:**
1. Согласовать с владельцем скоуп релиза: **один v3.48.0** (рекомендуется) или серия мелких.
2. Свернуть 7+ секций «## Unreleased» → одна «## v3.48.0» сгруппированно (фичи / фиксы / тех).
3. Поднять `<Version>` в csproj → `agents/scripts/sync-version.ps1` (обновит «Last verified» всех docs).
4. Собрать релиз по `RELEASE_PROCESS.md` (ZIP + GitHub Release + `releases.json` + `update-log.json` в правильном порядке).
5. Решить вопрос дублирования `releases.json`/`update-log.json` (отмечено в `CURRENT_STATE`): либо консолидация, либо явный контракт «кто из чего читает». Зафиксировать в `RELEASE_PROCESS.md`.

### Критерии готовности
- нет «## Unreleased»-хвоста; версия поднята и синхронизирована;
- релиз опубликован, автообновление видит новую версию (проверка по `AUTO_UPDATE.md`).

---

## 8. Этап 5 — Безопасность (пункт 8)

### Цель
Убрать секреты из публичного репозитория и подтвердить приватность телеметрии. **Без спешки: пункты, требующие решения владельца.**

**Шаги (по подпунктам):**

1. **Вшитый админ-пароль** (`AppSettingsService.EmbeddedAdminPassword = "2000200014az"`):
   - Согласовать вариант: (a) соль+хэш в settings, (b) per-office пароль, (c) вынос в env/build-time (как `OFFICE_REPORT_TOKEN`).
   - Убрать plaintext из git-истории (ротация, а не просто удаление из HEAD).
2. **Office-отчёты в публичный Gist** (`OfficeReportService` + build-time `OFFICE_REPORT_TOKEN`):
   - Зафиксировать threat model (что реально утекает: версия/имя ПК/кол-во заказов — уже агрегировано, но в публичном gist).
   - Решение владельца: приватный gist / собственный endpoint / оставить, но задокументировать.
3. **Телеприватность** (`AiTelemetryService`):
   - Аудит: убедиться, что не пишутся полные тексты запросов, содержимое заказов, ключи (план §12 требует). Добавить регрессионный тест, если нет.
4. **Встроенный OpenRouter-ключ** (403 «limit exceeded»):
   - Это настройка владельца; действие — ротация/удаление + документирование в `CURRENT_STATE`.

### Критерии готовности
- секреты не лежат в git; threat model и решения задокументированы; тест приватности телеметрии зелёный.

---

## 9. Этап 6 — Гигиена документации (пункт 9)

**Файлы:** `agents/docs/GOTCHAS.md`, `docs/plans/ai-agent-mode-plan.md`.

**Шаги:**
1. `GOTCHAS.md`: восстановить сквозную нумерацию (#1–#17 по порядку), убрать дублирующийся «## Last verified» и вынести «Source files»/«Last verified» в конец файла.
2. `ai-agent-mode-plan.md`: заменить статус «РЕАЛИЗОВАНО (2026-08-04)» на актуальный (OCR/vision/монтаж-фикс уже реализованы, но статус-трекинг устарел); добавить ссылку на этот план.
3. Если применимо — добавить в `validate-docs.ps1` мягкую проверку нумерации GOTCHAS (опционально, только если дёшево).

### Критерии готовности
- `validate-docs.ps1` — 0 issues; нумерация GOTCHAS сквозная; AI-план актуален.

---

## 10. Риски и mitigation

| Риск | Вероятность | Влияние | Mitigation |
|---|---|---|---|
| Рефакторинг AI сломает поведение | Средняя | Высокое | Этапы 0–1 ДО рефакторинга (регрессионный каркас); публичный API не трогается |
| Расхождение цен в промпте/парсере при консолидации | Низкая | Высокое | `AiFactsProvider` = единственный источник; консистентность проверяется тестом |
| Релиз сломает автообновление | Низкая | Критическое | `releases.json` после GitHub Release + ZIP (CONTROL §7), проверка по `AUTO_UPDATE.md` |
| Секреты останутся в истории git | Высокая (если просто удалить) | Высокое | Ротация + (при необходимости) history rewrite владельцем |
| Разбиение XAML/контролов | Средняя | Высокое | XAML-файлы НЕ трогаем (обосновано в `REFACTORING_PLAN_BIG_FILES.md` §1) |

---

## 11. Success criteria (итоговые)

1. Этапы 0–6 выполнены, `dotnet test` 100% pass.
2. Политика «не выдумывай» — одна точка, покрыта golden-кейсами.
3. Каталог/цены/цвета/ключевые слова — один источник (`AiFactsProvider`).
4. Системный промпт — embedded resource, собирается из фактов.
5. `AiAssistantService`/`AiAssistantViewModel`/`AiClarificationForm` разбиты по SRP.
6. «Unreleased» свернут в релиз, версия поднята, автообновление работает.
7. Секреты не в git, приватность телеметрии подтверждена тестом.
8. `validate-docs.ps1` — 0 issues; `gensymbols.ps1`/`SYMBOL_INDEX.md` актуальны.

---

## Прогресс выполнения

| Этап | Статус | Примечание |
|---|---|---|
| 0 — Регрессионный каркас | ✅ done (2026-08-19) | +5 golden-кейсов, +2 VM-теста; **1753/1753 pass** |
| 1 — Централизация предохранителя | ✅ done (2026-08-20) | Services/AiPlanSafetyPolicy + NeedsClarification/MissingField на плане; 18 новых тестов; **1783/1783 pass** |
| 2 — Единый источник фактов | ✅ done (2026-08-20) | Services/AiFactsProvider + AiKeywordLexicon + AiPromptBuilder (embedded Resources/ai-system-prompt.md); 99 новых тестов; **1882/1882 pass** |
| 3 — Разбиение God-классов | ✅ done (2026-08-20) | AiModelCatalogClient + AiKeyValidator вынесены из AiAssistantService (−661 строк, −37%); VM разбит на Streaming + Plans partials; AiClarificationPrefill отделён от формы |
| 4 — Релизная дисциплина | ⬜ blocked | требует решения владельца по скоупу v3.48.0 (один или серия мелких) |
| 5 — Безопасность | ⬜ blocked | threat model подготовлен в плане; admin-пароль/Gist/OpenRouter-ключ требуют явного «Подтверждено владельцем» |
| 6 — Гигиена доков | ✅ done (2026-08-20) | GOTCHAS.md сквозная нумерация 1–17; дубль Last verified убран; validate-docs.ps1 #12 — мягкая проверка нумерации; ai-agent-mode-plan.md cross-link на этот план |

## Last verified

2026-08-20 — Этапы 0, 1, 2, 3 и 6 выполнены (1882/1882 tests pass, validate-docs 0 issues).
Этапы 4–5 ожидают решений владельца (см. «Open questions»).

2026-08-20 — Этапы 0, 1, 2, 3 и 6 выполнены (1882/1882 tests pass, validate-docs 0 issues).
Этапы 4–5 ожидают решений владельца (см. «Open questions»).
