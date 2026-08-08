# INTENTS.md — mapping намерений на файлы

> How AI agents use this: On the Intake phase, after understanding the user's request, check this table to jump directly to relevant files instead of full codebase exploration.

## UI / интерфейс

| Пользователь хочет | Смотреть файлы |
|---|---|
| Скрыть/показать колонку в таблице | `OrderItemsControl.xaml`, `OrderItem.Calculations.cs` (display свойства), `MainWindow.xaml.cs` (editing gates) |
| Изменить панель быстрого ввода (QuickAdd) | `QuickAddControl.xaml`, `QuickAddControl.xaml.cs`, `OrderItem.cs` (продуктовые set'ы) |
| Изменить таблицу заказов (DataGrid) | `OrderItemsControl.xaml`, `OrderItemsControl.xaml.cs`, `MainWindow.DataGrid.cs` |
| Изменить боковую панель (клиент, договор) | `SidebarControl.xaml`, `SidebarControl.xaml.cs`, `ClientInfo.cs` |
| Изменить итоговую карточку | `TotalCardControl.xaml`, `TotalCardControl.xaml.cs`, `CalculationViewModel.cs` |
| Изменить панель действий | `ActionBarControl.xaml`, `ActionBarControl.xaml.cs` |
| Изменить диалог «На завод» | `SendToFactoryWindow.xaml`, `SendToFactoryWindow.xaml.cs`, `FactoryTextService.cs` |
| Изменить вкладку «Заказы» | `OrdersHistoryControl.xaml`, `OrdersHistoryControl.xaml.cs`, `OrdersHistoryViewModel.cs` |
| Изменить тему/цвета | `Themes/Brushes.xaml`, `ThemeService.cs`, `DECISIONS.md#10`, `GOTCHAS.md#7` |
| Изменить стили кнопок/карточек/таблиц | `Themes/*Styles.xaml`, `GOTCHAS.md#7` |

## Товары / добавление

| Пользователь хочет | Смотреть файлы |
|---|---|
| Добавить новый товар | `prices.json`, `PriceService.cs` (DefaultPrices + ApplyMigrations), `OrderItem.cs` (product sets), `QuickAddControl.xaml.cs` |
| Изменить поведение товара при добавлении | `QuickAddControl.xaml.cs` (CmbQuickType_SelectionChanged, QuickAddItem), `OrderItem.cs` (ManualPieceProducts, AmountOnlyProducts, etc.) |
| Изменить формат отображения товара в таблице | `OrderItem.Calculations.cs` (display свойства), `OrderItemsControl.xaml` (binding'и) |
| Изменить формулу расчёта товара | `OrderItem.Calculations.cs` (Recalculate), `CALCULATION_LOGIC.md` ⚠️ |
| Изменить цену товара | `prices.json`, `PriceService.cs` |
| Отключить колонку для определённых товаров | `OrderItem.cs` (product set'ы: ManualPieceProducts, AmountOnlyProducts, NoColorProducts, WidthOnlyProducts) |

## Расчёты, размеры, Anwis

| Пользователь хочет | Смотреть файлы |
|---|---|
| Изменить формулу Anwis | `AnwisSize.cs`, `AnwisSizeMode.cs`, `AnwisSizeService.cs`, `CALCULATION_LOGIC.md` ⚠️ |
| Изменить монтаж / вычеты | `OrderItem.Installation.cs`, `CALCULATION_LOGIC.md#монтаж`, `GOTCHAS.md#11` ⚠️ |
| Изменить расчёт итогов | `CalculationViewModel.cs` (CalculateTotal), `OrderItem.Calculations.cs` (Recalculate) |
| Проблема с размерами (странные значения) | `GOTCHAS.md#1` (утечка Anwis), `OrderItem.cs` (ШиринаВвод/ВысотаВвод/AnwisSizeMode setter'ы) |

## Печать / КП

| Пользователь хочет | Смотреть файлы |
|---|---|
| Изменить шаблон КП | `Services/FlowDocumentBuilder.cs`, `Services/DrawingService.cs`, `Services/PrintService.cs` (facade), `GOTCHAS.md#6` |
| Добавить поле в КП | `Services/FlowDocumentBuilder.cs`, `Models/OrderItem.cs` (свойства) |
| Проблема с HTML/FlowDocument в КП (кракозябры, наложение) | `GOTCHAS.md#6`, `Services/FlowDocumentBuilder.cs`, `Services/DrawingService.cs` |

## Завод

| Пользователь хочет | Смотреть файлы |
|---|---|
| Изменить текст «На завод» | `FactoryTextService.cs`, `SendToFactoryWindow.xaml` |
| Изменить группировку товаров | `FactoryTextService.cs` (Generate), `CALCULATION_LOGIC.md#завод` |

## Сохранение / загрузка

| Пользователь хочет | Смотреть файлы |
|---|---|
| Изменить формат сохранения заказов | `OrderItem.Dto.cs`, `OrderStorageService.cs`, `GOTCHAS.md#3`, `GOTCHAS.md#9` |
| Изменить путь хранения данных | `AppSettingsService.cs`, `GOTCHAS.md#9` |
| Проблема с сохранением/загрузкой | `GOTCHAS.md#2,#3,#9`, `OrderStorageService.cs`, `OrderItem.Dto.cs` |

## Релиз / автообновление

| Пользователь хочет | Смотреть файлы |
|---|---|
| Сделать релиз (bump версии) | `RELEASE_PROCESS.md`, `MosquitoNetCalculator.csproj` |
| Настроить автообновление | `UpdateService.cs`, `releases.json`, `AUTO_UPDATE.md` |
| Проблема с автообновлением | `AUTO_UPDATE.md`, `releases.json`, `UpdateService.cs`, `Generate-update-log.ps1` |
| Обновить releases.json | `update-releases-json.ps1`, `releases.json`, `RELEASE_PROCESS.md` |

## Цены

| Пользователь хочет | Смотреть файлы |
|---|---|
| Изменить стартовый каталог цен | `PriceService.cs` (DefaultPrices), `prices.json` |
| Мигрировать цены | `PriceService.cs` (ApplyMigrations), `GOTCHAS.md#4` |
| Изменить вкладку «Цены» | `PricesControl.xaml`, `PricesControl.xaml.cs`, `PricesViewModel.cs` |

## Тесты

| Пользователь хочет | Смотреть файлы |
|---|---|
| Найти тест для OrderItem | `MosquitoNetCalculator.Tests/Models/OrderItemTests.cs` |
| Найти тест для расчётов | `MosquitoNetCalculator.Tests/ViewModels/CalculationViewModelTests.cs`, `ManualChecklistTests.cs` |
| Проверить эталонный расчёт | `CALCULATION_TEST_CASES.md` |

## A.R.C. / автодокументирование

| Пользователь хочет | Смотреть файлы |
|---|---|
| Сгенерировать индекс символов | Run: `powershell -File gensymbols.ps1` |
| Проверить документацию | Run: `powershell -File validate-docs.ps1` |
| Узнать что обновить в docs | Run: `what-to-update.ps1 $(git diff --name-only)` |
| Перегенерировать DOCUMENTATION_MATRIX.md | Run: `powershell -File render-matrix.ps1` |
| Добавить новый файл в матрицу | Edit: `documentation-matrix.json`, then run `render-matrix.ps1` |
| Проверить синхронизацию перед коммитом | Run: `powershell -File arc-check.ps1` |
| Разбил большой файл на части | Run: `gensymbols.ps1`; update INTENTS.md, MODULES.md, DOCUMENTATION_MATRIX.md, CURRENT_STATE.md → CONTROL#13 |
| Переименовал/переместил файл или класс | Run: `gensymbols.ps1`; update INTENTS.md, MODULES.md, DOCUMENTATION_MATRIX.md → CONTROL#13 |
| Добавил новый класс/сервис/модуль | Run: `gensymbols.ps1`; update INTENTS.md (если есть intent), MODULES.md → CONTROL#13 |
| Изменил сигнатуры методов/свойств | Run: `gensymbols.ps1` (SYMBOL_INDEX.md) → CONTROL#13 |
| Изменил бизнес-логику/формулы/монтаж | `CALCULATION_LOGIC.md`, `GOTCHAS.md`, `CALCULATION_TEST_CASES.md` ⚠️ critical domain → CONTROL#3 |
| Изменил процессы (релиз/автообновление) | `RELEASE_PROCESS.md`, `AUTO_UPDATE.md` → CONTROL#13 |
| Изменил саму систему документации (правила/routing) | `MULTI_AGENT_ARC_CALC_CONTROL.md`, `CHEATSHEET.md`, `AGENTS.md`, `CURRENT_STATE.md` → CONTROL#13 |
| Не уверен, обновлять ли docs | Run: `what-to-update.ps1 $(git diff --name-only)`; если система неактуальна — обязан обновить → CONTROL#13 |

---

## Usage in A.R.C. workflow

```
Intake phase: user describes intent
  -> check this file for matching pattern
  -> jump to relevant files directly
  -> skip full codebase exploration
  -> saves ~80% of context-gathering tokens
```

## Source files

- `docs/arc/INTENTS.md` — this file

## Last verified

2026-08-03 (v3.47.3) — добавлены mapping'и структурных изменений (разбивка/переименование/новые классы/сигнатуры/бизнес-логика/процессы/сама система документации) согласно CONTROL#13.

2026-07-12 — документ перепроверен в рамках Фазы 3 рефакторинга; routing-таблица и mapping намерений на файлы актуальны. Учтена декомпозиция `PrintService` на `FlowDocumentBuilder`/`DrawingService`/`FixedDocumentBuilder`/`PrintQueueManager`/`PdfExportService` и bugfix экономии Старт/F-планка в откосах.

2026-06-27 (создан)
