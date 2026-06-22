# Anwis режим размера → контекстное меню (спецификация)

## 1. Цель

Убрать постоянно занимаемое место в рабочей зоне для выбора режима Anwis
(`ББ 60 / ББ 70 / ПП / Проём / Габарит`). Заменить ряд пилюль в QuickAdd и
отдельную колонку «Режим» в DataGrid на:

- **выбор через ПКМ-контекстное меню** в обоих контекстах;
- **минимальный визуальный индикатор** текущего режима (0 px места в QuickAdd,
  маленькая пилюля в ячейке «Наименование» DataGrid);
- **прозрачное поведение ширины/высоты** при смене режима: raw-размеры
  пользователя сохраняются, пересчёт ведётся по правилам нового режима.

Сохранение удобства и функциональности — обязательное условие.

## 2. Затрагиваемые контексты (два)

| Контекст | Сегодня | После |
|---|---|---|
| **QuickAdd** (композиция новой строки) | Горизонтальный ряд `AnwisModeRow` с 5 RadioButton-пилюлями + заголовок «Режим размера Anwis ⓘ»; виден при `Тип = Anwis`. | Ряд пилюль удалён. Режим виден в `ToolTip` на `CmbQuickType` (0 px). Смена — ПКМ по `CmbQuickType` → `ContextMenu` с 5 `MenuItem` (radio-чек у текущего). |
| **OrderItemsControl** (правка в заказе) | Отдельная колонка «Режим» с пилюлей-Button на строку (ЛКМ → контекстное меню). | Колонка «Режим» удалена. В ячейке «Наименование» — горизонтальная `StackPanel`: `TextBlock «Anwis»` + `Border … [пилюля] Режим`. ПКМ по пилюле → `ContextMenu`. |
| **FactoryTextService** (печать) | Строки вида `Anwis [ББ 60]` + секции `Anwis, размер проёма (Режим: Брусбокс 60)`. | Только чистое `Anwis`. Режим в печать НЕ попадает. |
| **Persistence в QuickAdd** | `SelectedAnwisMode = Брусбокс60` сбрасывается при каждом `CmbQuickType_SelectionChanged` (на любой Type, включая возврат). | Режим сохраняется между несколькими «Добавить» **внутри одного заказа**. Сбрасывается только на «Новый заказ» / «Открыть заказ» / «Очистить». |

## 3. Дизайн решения

### 3.1. QuickAdd

#### 3.1.1. UI

- **Удалить** Border `AnwisModeRow` целиком (XAML): 5 RadioButton-пилюль,
  заголовок «Режим размера Anwis ⓘ», `AnwisInfoIcon`.
- **Удалить** связанный code-behind:
  - поле `_themeChangedHandler` + подписку на `ThemeService.ThemeChanged`
    (если больше нигде не нужно);
  - метод `UpdateAnwisHintText` и весь построитель `Tooltip`;
  - `AnwisModePills.Children.Clear()` цикл (если только ради пилюль);
  - `AnwisQuickMode_Click` обработчики (если больше нигде не используются).

#### 3.1.2. Индикатор текущего режима (0 px)

- При `CmbQuickType.SelectedItem == "Anwis"`: программно
  `ToolTipService.SetToolTip(CmbQuickType, "Режим Anwis: <FullLabel>\n\nПКМ по этому полю — сменить режим.")`.
- При смене Тип с Anwis на любой другой — `ToolTipService.SetToolTip(CmbQuickType, null)`.
- При смене режима (внутри меню) — обновить ToolTip.
- При смене темы — подписка `ThemeService.ThemeChanged` обновляет
  `ToolTip` (текст+кисти, через делегат-поле как в текущем
  QuickAddControl паттерне).

#### 3.1.3. Триггер контекстного меню

- Обернуть `CmbQuickType` в `Grid` (или оставить как есть) и повесить
  обработчик `PreviewMouseRightButtonDown` на ту же сетку/бордер:
  ```text
  e.Handled = True  // не пускать в ComboBox, сохранить фокус на текстовых полях
  если SelectedItem == "Anwis" — построить и открыть ContextMenu
  иначе — пропустить (дать ComboBox стандартное поведение ПКМ)
  ```
- ContextMenu строится программно (как уже делает
  `MainWindow.AnwisModePill_Click`): 5 `MenuItem` с
  `IsCheckable=True`, `IsChecked = (mode == SelectedAnwisMode)`. Стиль —
  `(Style)FindResource(typeof(ContextMenu))`, чтобы кисти следовали теме
  через `DynamicResource`.

#### 3.1.4. Поведение при смене режима

- SelectedAnwisMode = новый режим → пересобрать ToolTip (см. 3.1.2).
- Ширина/Высота в полях ввода НЕ пересчитываются (это справедливо ТОЛЬКО
  для уже добавленных OrderItem; в QuickAdd поля — сырой ввод и должны
  сохраниться для случая, когда пользователь меняет режим и затем нажимает
  «Добавить». Расчёт при добавлении сделает сам `CalculationViewModel.AddItem`
  через `AnwisSize.ОтВвода(W, H, mode)` — там уже всё учтено).

### 3.2. OrderItemsControl (правка существующих строк)

#### 3.2.1. Удалить отдельную колонку «Режим»

- Удалить DataGridTemplateColumn «Режим» Width=76 целиком из
  `OrderItemsControl.xaml`.
- Удалить `AnwisModePill_Click` обработчик в коде-бихайнде
  (`OrderItemsControl.xaml.cs:43` → вызов `MainWindow.AnwisModePill_Click`).

#### 3.2.2. Слить в ячейку «Наименование»

- Превратить колонку «Наименование» из `DataGridTextColumn` в
  `DataGridTemplateColumn` с `CellTemplate`:
  ```xml
  <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
    <TextBlock Text="{Binding Name}" ... />     <!-- "Anwis" -->
    <Border Visibility="{Binding IsAnwis, Converter=BoolToVis}"
            Background="..." CornerRadius="5" Padding="6,2"
            Cursor="Hand"
            PreviewMouseRightButtonDown="AnwisModePill_PreviewRightClick">
      <TextBlock Text="{Binding AnwisSizeShortLabel}" ... />
    </Border>
  </StackPanel>
  ```
- Визуально ячейка для Anwis строки: **`Anwis [ББ 60]`**, для не-Anwis —
  только «Наименование», как сейчас.
- `OrderItem.Name` остаётся строго `"Anwis"` → печать (см. 3.3) и
  FactoryTextService независимы от визуального склеивания.

#### 3.2.3. Триггер

- `PreviewMouseRightButtonDown` на Border-пилюле → строит и открывает
  ContextMenu программно. **ПКМ, как в QuickAdd** (по выбору пользователя).
- Контекстное меню идентично QuickAdd-овскому (5 radio-элементов,
  текущий отмечен, кисти — `FindResource(typeof(ContextMenu))`).

#### 3.2.4. Поведение при смене режима

- Использует существующий **публичный setter** `OrderItem.AnwisSizeMode`
  (НЕ `SetAnwisModeQuiet`): reverse→apply, raw-размеры сохраняются,
  `ШиринаВвод/ВысотаВвод` пересчитываются динамически через
  `AnwisSize.ОтХранимого(...).СРежимом(mode)`. Это уже работает —
  ничего не меняем в `OrderItem`.

### 3.3. Печать — режим НЕ печатается

В `FactoryTextService.cs`:

- **Строка ~72** (`DisplayName`): вернуть к `item.Name` (без
  `Anwis [{ShortLabels[...]}]`), если раньше приклеивали суффикс.
  Сейчас в файле: `? $"Anwis [{AnwisSizeService.ShortLabels[item.AnwisSizeMode]}]"`.
  После: `item.Name` (т.е. всегда «Anwis»).
- **Строка ~154** (GroupBy Anwis): для группировки использовать
  `item.Name` (без режима) — тогда все Anwis строки идут в одну секцию,
  без разделения по режиму.
- **Строка ~168 / `GetSectionHeader`**: убрать вызов
  `AnwisSizeService.GetSectionHeader(mode)` (он добавляет
  `(Режим: Брусбокс 60)` в заголовок). Заменить на просто «Anwis:».

Печать становится чище: пользователь видит одно «Anwis» с итогом
площади и суммы; режим остаётся в БД и влияет на расчёт КП, но в
документ не прорывается.

> Тесты `FactoryTextServiceTests.cs` уже используют
> `AnwisSizeMode = ...` в фикстурах — нужно проверить, что удаление
> суффикса не сломает существующие ассерты, и обновить их при
> необходимости (изменять ассерт, не поведение).

### 3.4. Persistence в QuickAdd

- **Удалить** строку `SelectedAnwisMode = Models.AnwisSizeMode.Брусбокс60;`
  из обработчика `CmbQuickType_SelectionChanged` (она сбрасывает режим при
  возврате на «Anwis» — лишнее).
- **Сохранять** `SelectedAnwisMode` между добавлениями в одном заказе
  (поведение менять не надо — поле приватное и переживает добавления).
- **Экспортировать `void ResetAnwisMode()` на QuickAddControl**:
  ```text
  SelectedAnwisMode = Брусбокс60;
  ToolTip CmbQuickType = "Режим Anwis: Брусбокс 60. ПКМ для смены.";
  если открыто ContextMenu — IsOpen = false;
  ```
- **В `MainWindow.xaml.cs`** вызвать `ResetAnwisMode()` из:
  - `StartNewOrder()` / «Новый заказ»;
  - `OpenSelectedOrder()` / «Открыть заказ»;
  - «Очистить» если есть отдельный путь.

## 4. Файлы изменений

| Файл | Действие |
|---|---|
| `MosquitoNetCalculator/Controls/QuickAddControl.xaml` | Удалить `AnwisModeRow` (Border, RadioButton'ы, header, ⓘ-иконку). Wrapping-контейнер вокруг `CmbQuickType` для ПКМ. |
| `MosquitoNetCalculator/Controls/QuickAddControl.xaml.cs` | Удалить `UpdateAnwisHintText`, `_themeChangedHandler` (если больше не нужен), `AnwisQuickMode_Click` обработчики. Добавить: `PreviewMouseRightButtonDown` на обёртке; `BuildAnwisContextMenu()`; `UpdateModeTooltip()`; `ResetAnwisMode()`. |
| `MosquitoNetCalculator/Controls/OrderItemsControl.xaml` | Удалить колонку «Режим». Переделать «Наименование» в TemplateColumn с inline-пилюлей для Anwis. |
| `MosquitoNetCalculator/Controls/OrderItemsControl.xaml.cs` | Удалить `AnwisModePill_Click` (forward-вызов). Добавить `AnwisModePill_PreviewRightClick` (Border-based). |
| `MosquitoNetCalculator/MainWindow.xaml.cs` | В `OpenSelectedOrder`, `StartNewOrder`, любом «очистить» — вызвать `QuickAdd.ResetAnwisMode()`. Возможно, переиспользовать билдер ContextMenu из QuickAdd-логики (вынести в общий helper или продублировать — оба варианта принимаемы). |
| `MosquitoNetCalculator/Services/FactoryTextService.cs` | Strip Anwis-суффикса из `DisplayName`; group-by `Name` без режима; убрать `GetSectionHeader`. |
| `MosquitoNetCalculator.Tests/Services/FactoryTextServiceTests.cs` | Обновить ассерты под новое (чистое) форматирование. ББ 60 → «Anwis» без `[ББ 60]`. |
| `MosquitoNetCalculator/Services/UpdateLog.cs` | Changelog entry: «Режим размера Anwis: убран ряд пилюль, выбор через ПКМ-меню по Тип-dropdown / пилюле в строке. Режим больше не печатается в FactoryText ("Anwis [ББ 60]" → "Anwis").» |

## 5. Edge cases

1. **Потеря фокуса при ПКМ по ComboBox.** `PreviewMouseRightButtonDown` +
   `e.Handled = true` останавливает туннелирование; фокус на текстовых полях
   Ширина/Высота сохраняется. Подтверждено через WPF tunneling phase.
2. **Меню открыто, пользователь переключает заказ.** `IsOpen = false` в
   `ResetAnwisMode()` (или при закрытии окна через `MainWindow.Closing` —
   если заказы переключаются без закрытия).
3. **`Type != "Anwis"` и ПКМ по Тип-dropdown.** Подменю не открывается;
   стандартное WPF-контекстное меню Combobox остаётся доступным (или не
   показывается вовсе — нативный ПКМ ComboBox не имеет дефолт-меню,
   `e.Handled = false` сохраняет пустое поведение).
4. **Смена темы при открытом меню.** `Style = (Style)FindResource(typeof(ContextMenu))`
   + фон/рамки через `DynamicResource` в `ContextMenuStyles.xaml` →
   перерисовывается автоматически. Никаких выделенных кистей не храним.
5. **Revert режима.** Пользователь выбрал ББ 70, увидел числа 998×970, не
   понравилось, вернулся на ББ 60: `OrderItem.AnwisSizeMode` setter делает
   reverse→apply, raw-размеры восстанавливаются к 1000×970 → 1002×970.
   Контрактно идентично сегодняшнему поведению колонки «Режим» в
   OrderItemsControl.
6. **QuickAdd: пользователь меняет режим ПОСЛЕ ввода Ширина/Высота.**
   Поля остаются (raw ввод), ToolTip обновляется. При «Добавить»
   `CalculationViewModel.AddItem` применит `AnwisSize.ОтВвода(W, H, mode)`
   и сохранит режим в OrderItem.
7. **Сохранение/загрузка заказов.** `OrderData.AnwisSizeMode` уже
   сохраняется; ничего не меняется — при открытии заказа все Anwis-строки
   восстанавливают свои режимы, а QuickAdd сбрасывается на default через
   `ResetAnwisMode()`.

## 6. Acceptance criteria

- [ ] В QuickAdd **нет** Border `AnwisModeRow`/`AnwisModePills`/ⓘ-иконки —
      только строка полей ввода.
- [ ] При `Тип = Anwis` → `CmbQuickType.ToolTip` показывает
      «Режим Anwis: … — ПКМ для смены».
- [ ] **ПКМ** по `CmbQuickType` (при `Тип = Anwis`) открывает
      `ContextMenu` с 5 radio-пунктами, текущий отмечен.
- [ ] В `OrderItemsControl` нет колонки «Режим». Ячейка «Наименование»
      для Anwis-строк рендерит «Anwis [ББ 60]» (или эквивалент).
- [ ] **ПКМ** по пилюле в ячейке «Наименование» открывает то же меню.
- [ ] **ПКМ** по тексту «Anwis» (без пилюли) НЕ открывает меню — только
      по пилюле.
- [ ] При выборе нового режима в меню: для существующего OrderItem
      Ширина/Высота пересчитываются по правилам нового режима (raw
      сохраняются); Total обновляется.
- [ ] `FactoryTextService` НЕ содержит `Anwis [ББ 60]` нигде. Печать —
      только чистое «Anwis».
- [ ] «Новый заказ» / «Открыть заказ» сбрасывает `SelectedAnwisMode`
      в QuickAdd на `Брусбокс60`.
- [ ] Множественные «Добавить» в рамках одного заказа сохраняют
      выбранный режим.
- [ ] Сборка чистая (0 errors / 0 warnings).
- [ ] Все 415 существующих тестов проходят; новые/изменённые тесты
      (FactoryTextServiceTests) обновлены под чистое форматирование.

## 7. Testing

- **Unit (обязательно):**
  - `QuickAddControl` — добавить smoke-тест: в `AnwisSizeServiceTests` уже
    есть coverage на `GetExplanation`. Логика индикатора — больше UI, чем
    сервис; можно покрыть визуально вручную или через `ManualChecklist`.
  - `FactoryTextServiceTests` — обновить ассерты: ожидаемая строка для
    Anwis выходных — `"Anwis"` без суффикса.
- **Manual (обязательно):**
  - Сценарий «ПКМ по Тип» → выбрать ББ 70 → добавить 3 строки
    поочерёдно → все получают ББ 70.
  - «Новый заказ» → QuickAdd ToolTip снова «Брусбокс 60».
  - Открыть заказ с Anwis-ББ 70 строкой → в гриде pill показывает ББ 70.
  - ПКМ по pill → переключить на Проём → Ширина `1000+20=1020`, Высота те же
    значения, Total пересчитан.
  - «Сформировать КП» → в печати строка Anwis без суффикса.
  - Сменить тему при открытом меню → рамки/фон меню обновляются.

## 8. Notes для реализации

1. **Existing infrastructure reuse:**
   - `MainWindow.AnwisModePill_Click` (line 535+) уже строит программный
     ContextMenu с 5 `MenuItem` и ApplyMode closure. Этот же helper либо
     переиспользуем (через `internal` или вынос в `AnwisContextMenuBuilder`),
     либо дублируем в `QuickAddControl` — оба варианта проходят code review.
   - Стиль меню: `(Style)FindResource(typeof(ContextMenu))` уже
     рендерится через `Themes/ContextMenuStyles.xaml` — повторно
     использовать, тема-следование бесплатно.
   - `OrderItem.AnwisSizeMode` public setter уже делает reverse→apply для
     рантайм-смены в OrderItemsControl — НЕ трогать, использовать как есть.

2. **ToolTip + theme:** для обновления ToolTip на смене темы использовать
   тот же паттерн с приватным полем-делегатом
   (`_themeChangedHandler`), который уже введён в `QuickAddControl`
   после предыдущей итерации (Round 2 fix). Если поле становится
   не нужно (весь ToolTip удалён) — удалить поле и подписку.

3. **PrintPost-Filter:** перед изменением FactoryTextService прогнать
   `grep -rn "ShortLabels\[item.AnwisSizeMode\]"` по проекту — найти
   любые другие места, которые могут печатать/экспортировать `Anwis [...]`.

4. **`UpdateLog.cs`:** добавить entry в `Updates` (сразу после версии
   3.29.3, например от 2026-06-21, версия «3.30.0»):
   «Режим Anwis — выбор через ПКМ-меню в QuickAdd и в строках заказа.
   Режим больше не печатается в документе КП (вместо "Anwis [ББ 60]" —
   "Anwis"). Колонка «Режим» в заказах удалена; текущий режим показан
   inline в названии.»

5. **Версия:** поднять `Version` в `MosquitoNetCalculator.csproj` и
   `installer.iss` на `3.30.0` (или согласовать с пользователем).

## 9. Детальный TestPlan: FactoryTextServiceTests (обязательные правки)

Карта всех 17 тестов из `MosquitoNetCalculator.Tests/Services/FactoryTextServiceTests.cs`,
которые затронет чистка Anwis-суффикса. Нумерация соответствует порядку
тестов в файле сверху вниз.

### 9.1. Семантика до и после

| Зона `FactoryTextService` | Сейчас | После |
|---|---|---|
| `BuildSelectableItems.DisplayName` для Anwis | `Anwis [ББ 60]` (через `ShortLabels[mode]`) | Просто `item.Name` → `Anwis` |
| `BuildSelectableItems.Detail` для Anwis | `1002×1170 — 1 шт.` (per-row, не зависит от суффикса) | **Без изменений** — числовые размеры per-row |
| `Generate` секция для Anwis | Разделение по режиму: `Anwis, размер проёма (Режим: Брусбокс 60):`, `Anwis, габаритный размер:`, и т.д. | Один общий заголовок `Anwis:` (группировка только по `Name`) |
| `Generate` per-row `Ш: X × В: Y` для Anwis | `Ш: 982 × В: 1150 — 1 шт.` (через `item.Размеры.ШиринаЗавод`) | **Без изменений** — каждая строка показывает корректные размеры в свой секции `Anwis:` |

### 9.2. Тесты, которые остаются зелёными без правок

| # | Тест (имя в файле) | Почему не правка |
|---|---|---|
| 1 | `Generate_NonAnwisItem_ContainsShVLabels` | Не Anwis (Отлив). |
| 2 | `Generate_AnwisItem_Brusbox60_ContainsShVLabels` | Ассертит только `Ш:`/`В:` — per-row dims зависят от `item.Размеры`, которое вычисляется из `_width/_height/_anwisSizeMode`. Эта математика не меняется. |
| 3 | `Generate_AnwisItem_Profiplast_ContainsShVLabels` | То же — `Ш:`/`В:` для Профипласт. |
| 4 | `Generate_RazmerProema_ShowsOriginalDimensions` | То же — `Ш:`/`В:` для РазмерПроёма. |
| 5 | `Generate_AnbisItem_Gabaritny_ContainsShVLabels` | То же — `Ш:`/`В:` для Габаритный. |
| 9 | `Generate_NonAnwisItem_GroupsByName` | Не Anwis — группировка по имени уже работает. |
| 10 | `Generate_IncludesAddress` | Не Anwis. |
| 11 | `Generate_IncludesAdditionalKp` | Не Anwis. |
| 14 | `BuildSelectableItems_NonAnwisItem_ShowsName` | Не Anwis (Отлив). |
| 15 | `BuildSelectableItems_ManualPiece_ShowsQuantityOnly` | Работа (ручная позиция). |
| 16 | `BuildSelectableItems_NonProduction_IsNotSelected` (Theory: Брус / Пояс / Работа / ПСУЛ / Доставка / Откос материал / Уплотнение / Короб) | Все 8 кейсов — не Anwis. |
| 17 | `BuildSelectableItems_Production_IsSelected` (Theory: Anwis / На навесах / Оконная на метал. крепл. / Отлив / Козырёк) | Ассертит только `IsSelected=true`; `DisplayName` не проверяется. **Опционально** добавить саб-ассерт для кейса `Anwis` на `DisplayName == "Anwis"` (новая проверка, не правка старой). |

### 9.3. Тесты с обязательными правками (5 штук)

#### #6 — `Generate_AnbisSectionHeader_ContainsModeLabel`

```csharp
// БЫЛО:
var text = FactoryTextService.Generate("", selectable);
Assert.Contains("Anwis, размер проёма (Режим: Брусбокс 60):", text);

// СТАЛО:
var text = FactoryTextService.Generate("", selectable);
Assert.Contains("Anwis:", text);            // заголовок чистый — без (Режим: …)
Assert.DoesNotContain("(Режим:", text);      // явный негативный ассерт на отсутствие
Assert.DoesNotContain("[ББ", text);          // и суффикса в именах строк тоже
```

Рекомендация по неймингу: переименовать в `Generate_AnwisSectionHeader_IsAnwisOnly`
для семантической ясности.

#### #7 — `Generate_AnwisItemsWithDifferentModes_CreateSeparateSections`

```csharp
// БЫЛО:
var text = FactoryTextService.Generate("", selectable);
Assert.Contains("Брусбокс 60", text);   // две отдельные секции по режимам
Assert.Contains("Профипласт", text);

// СТАЛО:
var text = FactoryTextService.Generate("", selectable);
// Все Anwis-строки группируются в одну секцию по Name
int headerCount = text.Split('\n').Count(l => l.Trim() == "Anwis:");
Assert.Equal(1, headerCount);
// Per-row размеры остаются — каждая строка со своим режимом через «заводские» −20 мм
Assert.Contains("Ш: 982", text);  // ББ 60 row copy: 1002→982
Assert.Contains("Ш: 980", text);  // ПП row copy: 1000→980
```

Переименовать в `Generate_AnwisItemsDifferentModes_MergeIntoOneSection`.

#### #8 — `Generate_AnwisSameMode_GroupsTogether`

```csharp
// БЫЛО:
var text = FactoryTextService.Generate("", selectable);
int headerCount = text.Split('\n').Count(l => l.Contains("Брусбокс 60"));
Assert.Equal(1, headerCount);

// СТАЛО:
var text = FactoryTextService.Generate("", selectable);
int headerCount = text.Split('\n').Count(l => l.Trim() == "Anwis:");
Assert.Equal(1, headerCount);
// Per-row оба ББ 60 присутствуют
Assert.Contains("Ш: 982", text);
Assert.Contains("Ш: 982", text.Split("Ш: 982")[1]);  // обе строки ББ 60
```

Переименовать в `Generate_AnwisItemsSameMode_GroupIntoOneSection` или оставить имя.

#### #12 — `BuildSelectableItems_AnwisDisplayName_IsCompact`

```csharp
// БЫЛО:
Assert.Equal("Anwis [ББ 60]", selectable[0].DisplayName);

// СТАЛО:
Assert.Equal("Anwis", selectable[0].DisplayName);
```

Опционально — переименовать в `BuildSelectableItems_AnwisDisplayName_IsClean`
(«compact» теперь уже про суффикс — после правки постфикса нет, имя теста
становится неточным).

#### #13 — `BuildSelectableItems_AnwisDetail_ShowsOriginalSizes`

```csharp
// БЫЛО:
Assert.Equal("Anwis [ББ 70]", selectable[0].DisplayName);
Assert.Contains("998",  selectable[0].Detail);  // ★ stored calc width
Assert.Contains("1170", selectable[0].Detail);  // ★ stored calc height
Assert.Contains("2 шт.", selectable[0].Detail);

// СТАЛО:
Assert.Equal("Anwis", selectable[0].DisplayName);   // постфикс убран
// Detail НЕ меняется — числа зависят от AnwisSize, не от имени
Assert.Contains("998",  selectable[0].Detail);
Assert.Contains("1170", selectable[0].Detail);
Assert.Contains("2 шт.", selectable[0].Detail);
```

Имя теста `BuildSelectableItems_AnwisDetail_ShowsOriginalSizes` остаётся
корректным — он по-прежнему проверяет `Detail` (числовые размеры), просто
из-за того, что ассерт `DisplayName` рядом, нужно его синхронизировать.

### 9.4. Реальный вывод `Generate()` после правок (sanity check)

Чтобы ревьюер мог глазами сравнить «до/после», два эталонных примера.

**Пример A — один Anwis row, ББ 60 (исходные данные теста #2):**

```text
Адрес: <address>

Anwis:
Ш: 982 × В: 1150 — 1 шт.
```

**Пример B — два Anwis row, разные режимы (исходные данные теста #7):**

```text
Anwis:
Ш: 982 × В: 1150 — 1 шт.
Ш: 980 × В: 1180 — 1 шт.
```

До правок `Пример B` имел две секции с заголовками
`Anwis, размер проёма (Режим: Брусбокс 60):` и
`Anwis, размер проёма (Режим: Профипласт):`. После — одна общая `Anwis:`.
Per-row `Ш: X × В: Y` остаётся корректным для каждой строки.

### 9.5. Non-change zones (не трогаем, тесты остаются зелёными)

- `BuildSelectableItems`: ветка `IsManualPiece` → detail `"N шт."` — не Anwis.
- `BuildSelectableItems`: ветка `IsAnwis` → detail `"origW × origH — N шт."` —
  числовые per-row значения, вычисляются из `_width/_height`, не зависят от имени.
- `Generate`: Address header `"Адрес: …"`.
- `Generate`: `"К КП № …"` для AdditionalKp.
- `Generate`: Aнwis-specific per-row `Ш: X × В: Y` через `item.Размеры.ШиринаЗавод/ВысотаЗавод`.
- `BuildSelectableItems`: production vs non-production selection list (theory 16/17).

### 9.6. Manual acceptance (после правок и реализации)

Кроме автотестов, после применения патча убедиться вручную:

- [ ] Открыть существующий заказ с двумя Anwis-строками в разных режимах
      → «Сформировать КП» → проверить, что в превью одна секция `Anwis:`.
- [ ] Обе `Ш: … × В: …` строки присутствуют в секции.
- [ ] Заголовок `Anwis:` НЕ содержит `(Режим: …)`.
- [ ] Скопированный в буфер текст, вставленный в блокнот, не содержит
      `[ББ 60]` / `[ББ 70]` / `[ПП]` / `[Проём]` / `[Габарит]`.
- [ ] В диалоге `SendToFactoryWindow` список чекбоксов — для Anwis строк
      `name` поля = просто «Anwis» (без постфикса в скобках).
- [ ] Регресс-чек не-Anwis товаров (Отлив, Работа, …) — форматирование
      в КП не изменилось.
