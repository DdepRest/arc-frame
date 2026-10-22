# CURRENT_STATE_ARCHIVE.md — Архив проектной памяти A.R.C.

Этот файл хранит подробные технические хроники и историю рефакторингов прошлых версий программы (v3.40.0 — v3.54.3), перенесённые из активного [`CURRENT_STATE.md`](../CURRENT_STATE.md) по правилу **Rolling Window Policy** (лимит 5–10 версий в активном файле).

Агентам при решении текущих задач читать этот архив **НЕ ТРЕБУЕТСЯ**. Актуальное состояние системы описано в [`CURRENT_STATE.md`](../CURRENT_STATE.md).

---

## Исторические записи: v3.54.0 — v3.54.3

- **Аудит deadlock-класса «фон ↔ UI» закрыт обеими половинами + фикс мёртвого VC++-тоста (v3.54.1):** (1) фон→UI — все блокирующие `Dispatcher.Invoke` безопасны (`OnUpdateDetected` — по инварианту события fire всегда на UI-потоке → инлайн; `FixedDocumentBuilder` — dispatcher вызывающей нити; AI-стриминг `wait:true` — все `SendMessageAsync` через await); (2) UI→фон — в продакшене ноль блокирующих идиом (`.Wait()`/`GetAwaiter().GetResult()`/`SynchronizationContext.Send`/`WaitOne`/`Task.WaitAll`/`Thread.Join` отсутствуют; оба `.Result` — post-await чтения завершённых тасков; три `Thread.Sleep` ждут внешний мир — AV/файлы/другой процесс, цикл не замыкается). Реальный баг всплыл и исправлен: `NotifyIfMissingOnPortable` из `Task.Run` звал `ShowToast` напрямую → VerifyAccess бросал внутрь catch сервиса → тост «Скачать VC++» никогда не показывался портабельным пользователям; фикс choke-point — `ToastService.DeferToUiThread` (фоновый вызов → `BeginInvoke` на dispatcher владельца, UI-вызов инлайн; naive-фикс блокирующим Invoke создал бы класс deadlock v3.54.1). Харденинги: XML-инвариант `UpdateService.UpdateDetected` («fire всегда на UI-потоке; новый фоновый fire-site запрещён — marshal'ить сам fire») и shutdown-guard в `InvokeOnUi` AI-стриминга (`HasShutdownStarted` + exception filter). Страж — `DependencyCheckerServiceTests.NotifyIfMissingOnPortable_FromBackgroundThread_QueuesToastOnUiThread`. Разбор и правило — GOTCHAS §45. Сборка 0/0, полный прогон 2411/2411.
- **Автопин тестов журнала обновлений (v3.54.1):** ритуальные лок-версии `UpdateLogTests` удалены — оба теста (`AllNewestFirst_FirstItemIsNewest`, `GetChangesSince_LatestVersion_ReturnsEmpty`) сверяются с `UpdateService.CurrentVersion` (internal, резолвится из `<Version>` csproj через `VersionResolver`), как и требует запрет на brittle-пины в RELEASE_PROCESS. При bump ритуал «не забудь пин» больше не существует: тест падает только если запись новой версии не добавлена в `update-log.json` или противоречит csproj (сообщение об ошибке говорит, что именно сделать). История ловушки: 3.53.2 ушла в тег с пином на 3.53.1.
- **Пеноплекс (откос): 450 → 500 ₽/лист (v3.54.0):** дефолт обновлён в `DefaultPrices` (PriceService.cs) + Migration 7 в `ApplyMigrations` (поднимает ТОЛЬКО точный legacy-дефолт 450, пользовательские цены не трогает — правило Migration 6), fallback-дефолты `SlopeCalculatorService.Calculate/UpdateInPlace`, `SlopeOverlayCoordinator.LoadSlopePrices`, `SlopePanelControl.Prices`, эталонный `prices.json` в корне. Тест: `LoadPrices_Migration7_BumpsPenoplexDefault450To500`. Целевые прогоны 392/392 (PriceService/Slope/OrderItem/SlopePanel), полный — за CI.

---

## Исторические записи: v3.53.0 — v3.53.2

- **Релиз 3.53.1 (2026-09-27):** GitHub Release + ZIP (93,77 МБ), releases.json в main обновлён CI. (а) Композитный тип «Новинка + Исправление»: `UpdateItem.Types` (парсинг по «+», JSON-контракт прежний), `HasType` для фильтров; (б) Шаблоны — первым пунктом записи в ActionBar; (в) Кнопка «Шаблоны» в ActionBar. Стражи: `UpdateItemCompositeTypeTests` (12), UIA `.tools/check-composite-badge.ps1`.
- **CI-уроки v3.53.1:** (1) шаг Test обёрнут в `nick-fields/retry` (3 попытки); (2) из ci.yml убран `-r win-x64` (SDK 10 incompatibility); (3) releases.json в main кладётся без новой записи (latest остаётся старым до релиза тега); (4) releases.json и update-log.json для релиза правятся синхронно; (5) WpfTestHelper idle-pump `Dispatcher.PushFrame` в воркере против STA-зависания.
- **Фикс тем (v3.53.0-hotfix):** на устройствах с выключенными анимациями Windows (`ClientAreaAnimation=false`) тема не применялась (гейт `!Motion.ReducedMotion`). Исправлено: без анимации кисть создаётся сразу в целевом цвете; страж `ThemeApplyTests.ApplyTheme_SetsBrushesToTargetColors_WithoutAnimation`.
- **Кнопка «Шаблоны» вместо «На завод» (v3.53.1):** «На завод» (текст для WhatsApp) удалён полностью. Внедрены шаблоны изделий: Каталог `OrderTemplateService`, модальное окно `TemplatesWindow`, векторные пиктограммы Path, атомарное добавление через Undo.
- **v3.53.0 — премиальная дизайн-система:** (1) Дизайн-токены четырёх шкал — Spacing, Radius, Typography, Motion; (2) Шрифт Inter вшит в сборку; (3) Стражи дизайн-системы (ратчет бюджетов, запрет hex в разметке, WCAG контраст); (4) Пустые состояния сведены к `EmptyStateStyles.xaml`; (5) Радиусы сведены к шкале; (6) PerMonitorV2 манифест и `Window.Shared` стиль; (7) Плотность таблицы позиций приведена к 12px (MinRowHeight 26); (8) Шрифт попапов изолирован.

---

## Исторические записи: v3.48.0 — v3.52.0

- **Производительность (v3.52.0):** устранены живые DropShadowEffect во вкладках, стиль `Card` переведён на hairline-рамку. AI-чат получил кап 100 сообщений с подгрузкой по 50.
- **История обновлений (v3.51.0):** поиск по версиям, фильтрация по чипам, виртуализация VirtualizingStackPanel, бейдж «Ваша версия».
- **v3.50.0 — Паритет с прототипом `docs/prototype.html`:** тёмная палитра, группы навигации, split-кнопка «Печать КП», компактный QuickAdd F2, статус-пилюли.
- **Админ-панель (v3.48.6):** атомарная привязка устройств через GitHub Gist PATCH, автоочистка отвязанных устройств, разделение на partial-классы.
- **Сумма монтажа (v3.48.3):** интеграция денег монтажа в ячейку грида и печать КП/PDF без создания лишних колонок.
- **v3.48.2 (монтаж по длине):** расчет монтажа козырьков/отливов по большей стороне `Max(Width, Height)/1000`.
- **v3.48.0 (Импост):** автоматический импост сеток при W>=500 или H>=1500 мм (+200 ₽/м.п. по расчётной ширине).

---

## Исторические записи: v3.40.0 — v3.47.4

- **v3.47.4:** сетевая печать через `PrintQueue` из `Local + Connections`, знак монтажа в popup.
- **v3.47.3:** запуск системы A.R.C. v5, обязанность самоподдержания (CONTROL §13).
- **v3.47.0 — v3.47.2:** монтаж для отлива и козырька (500/750 ₽/м.п.), sign-flip миграция legacy-заказов.
- **v3.44.0 — v3.46.0:** выравнивание колонок DataGrid, печать «В ПРОИЗВОДСТВО», XPS-сериализация штампа.
- **v3.40.0 — v3.43.0:** базовые формулы Anwis, DTO-декуплинг, хранение данных в %AppData%.
