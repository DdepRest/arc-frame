# UX Refactor Plan — A.R.C. Frame v3.41

## Цель

Устранить накопленный UI-долг (Franken-UI): структурировать меню, перегруппировать элементы,
переработать UX с учётом когнитивной нагрузки, сохранив всю бизнес-логику неизменной.

## Принципы

- **Код > план.** Решения, принятые в плане — отправная точка, но код и тесты — источник правды.
- **НЕ трогаем:** формулы расчёта, сохранение/загрузку JSON, печать КП, автообновление, горячие клавиши.
- **Меняем только:** XAML + code-behind привязок (no бизнес-логика).
- **После каждой фазы:** `dotnet build` + `dotnet test` → только потом следующая фаза.

---

## Фаза 0: Аудит и подготовка (READ-ONLY)

### Что делаем
1. Читаем все изменяемые XAML-файлы и их code-behind:
   - `ActionBarControl.xaml` + `.xaml.cs`
   - `TitleBarControl.xaml` + `.xaml.cs`
   - `MainWindow.xaml` + `.xaml.cs` (все partial-файлы)
   - `SidebarControl.xaml` + `.xaml.cs`
2. Находим все references на `BtnSettings_Click`, `MenuThemeLight_Click`, `MenuThemeDark_Click`,
   `MenuChangeLocation_Click`, `MenuCheckUpdates_Click`, `UpdateBadge`, `RunCurrentOrderInfo`,
   `DirtyIndicator` в code-behind.
3. Находим все references на `SettingsMenu`, `BtnSettings` в XAML и C#.
4. Убеждаемся, что понимаем текущие привязки и события.

### Проверка
- Файлы прочитаны, references собраны, зависимости замаплены.

---

## Фаза 1: ActionBar — реорганизация

### Что делаем

#### 1a. Три визуальных кластера
Текущая структура (все в одной строке):
```
[Печать КП] [Сохранить] [Новый заказ] [На завод] | Текст заказа | DirtyIndicator | [Настройки] [Очистить всё]
```

Целевая структура:
```
[Печать КП] [Сохранить]  |  [Новый заказ] [На завод]  |  [Очистить всё]
  Primary     Success        Ghost        Ghost          DangerGhost
```

Изменения:
- Убрать `RunCurrentOrderInfo` (TextBlock с "Новый заказ") — уедет в TitleBar в Фазе 3.
- Убрать `DirtyIndicator` — уедет в StatusBar/под ActionBar (в Фазе 1b или 3).
- Убрать `BtnSettings` + `SettingsMenu` + `UpdateBadge` — уедет в TitleBar в Фазе 2.
- Группировка: `Margin="0,0,16,0"` между кластерами, разделитель `|` (Rectangle 1px × 20px).

#### 1b. DirtyIndicator
- Перенести из ActionBar в новую строку под ActionBar (или в StatusBar TotalCard).
- Сохранить функциональность (связан с `HasUnsavedChanges`).

#### 1c. XAML изменения
- `ActionBarControl.xaml`:
  - Удалить `RunCurrentOrderInfo` TextBlock.
  - Удалить `DirtyIndicator` Border.
  - Удалить `BtnSettings` + `SettingsMenu` + `UpdateBadge` + `SettingsMenu` ContextMenu полностью.
  - Добавить визуальные разделители между кластерами кнопок.
- `ActionBarControl.xaml.cs`:
  - Удалить обработчики: `BtnSettings_Click`, `MenuThemeLight_Click`, `MenuThemeDark_Click`,
    `MenuChangeLocation_Click`, `MenuCheckUpdates_Click`.
  - Удалить все references на `UpdateBadge`, `SettingsMenu`, `RunCurrentOrderInfo`, `DirtyIndicator`.
  - Эти обработчики будут перенесены в TitleBar/MainWindow.

### Проверка
- `dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj` — 0 ошибок.
- `dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj` — все тесты проходят (742/742).

---

## Фаза 2: TitleBar — настройки и индикатор обновлений

### Что делаем

#### 2a. Иконка ⚙ с выпадающим меню
- Добавить кнопку с иконкой шестерёнки (Segoe Fluent Icons `&#xE713;`) в TitleBar,
  слева от кнопок [─][□][X].
- Стиль: как у существующих кнопок TitleBar (прозрачный фон, hover = AccentLight).
- ContextMenu:
  - «☀ Светлая тема» (checkable, привязан к текущей теме)
  - «🌙 Тёмная тема» (checkable)
  - Разделитель
  - «📍 Сменить точку установки…»
  - Разделитель
  - «🔄 Проверить обновления»

#### 2b. Red dot badge
- `Ellipse` (9×9, красный) на иконке шестерёнки.
- Видимость: `Collapsed` по умолчанию, `Visible` когда `UpdateService.HasPendingUpdate`.
- ToolTip: «Доступна новая версия!»

#### 2c. Перенос обработчиков
- Из `ActionBarControl.xaml.cs` в `TitleBarControl.xaml.cs` (или `MainWindow.xaml.cs`):
  - `MenuThemeLight_Click` — переключение на светлую тему.
  - `MenuThemeDark_Click` — переключение на тёмную тему.
  - `MenuChangeLocation_Click` — диалог смены точки установки.
  - `MenuCheckUpdates_Click` — ручная проверка обновлений.
- Обновить `UpdateBadge` привязку в code-behind.

### Проверка
- `dotnet build` — 0 ошибок.
- `dotnet test` — все тесты проходят.

---

## Фаза 3: MainWindow — обновление привязок

### Что делаем

#### 3a. Информация о заказе в TitleBar
- `RunCurrentOrderInfo` (TextBlock «Новый заказ» / «Заказ №0254») перенести в TitleBar.
- В TitleBar: `A.R.C. Frame — Новый заказ` или `A.R.C. Frame — Заказ №0254`.
- Обновить `MainWindow.TitleDirty.cs` — логика обновления заголовка.

#### 3b. Перенос DirtyIndicator
- Вариант A: в StatusBar под DataGrid (5-я строка в Grid).
- Вариант B: в TotalCard (рядом с ИТОГО).
- Вариант C: оставить в ActionBar, но под кнопками (2-я строка).
- **Выбрать вариант C** (наименьшие изменения) — DirtyIndicator в отдельной строке ActionBar.

#### 3c. Привязка UpdateBadge
- MainWindow должен обновлять `UpdateBadge` в TitleBar при изменении `HasPendingUpdate`.
- Метод `UpdateUpdateBadge()` уже существует в MainWindow — обновить target с ActionBar на TitleBar.

### Проверка
- `dotnet build` — 0 ошибок.
- `dotnet test` — все тесты проходят.

---

## Фаза 4: Sidebar — компактный layout

### Что делаем

#### 4a. Унификация отступов
- Все карточки Sidebar: одинаковые `Padding="12"`, `Margin="0,0,0,6"`.
- `SectionLabel` — унифицировать размер (FontSize="10").

#### 4b. Уплотнение полей
- Уменьшить `Margin="0,0,0,10"` между полями до `Margin="0,0,0,7"`.
- Высота TextBox: унифицировать `Height="30"`.
- Убрать лишние вложенные StackPanel (упростить вложенность).

#### 4c. Улучшение читаемости
- Добавить визуальный разделитель (1px Border) между карточками.
- Активное поле (focus) — accent border.

### Проверка
- `dotnet build` — 0 ошибок.
- `dotnet test` — все тесты проходят.

---

## Фаза 5: Валидация и полировка

### Что делаем

#### 5a. Сборка
- `dotnet build MosquitoNetCalculator/MosquitoNetCalculator.csproj` — 0 errors, 0 warnings.
- `dotnet build MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj` — 0 errors.

#### 5b. Тесты
- `dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj` — 742/742 pass.

#### 5c. Code Review
- Спавним `code-reviewer-deepseek` на все изменённые файлы.

#### 5d. Документация
- Обновить `CURRENT_STATE.md` — зафиксировать UX refactor.
- Обновить `CHANGELOG.md` через skill `arc-frame-changelog`.

#### 5e. Проверка A.R.C.
- `powershell -ExecutionPolicy Bypass -File validate-docs.ps1`.
- `powershell -ExecutionPolicy Bypass -File arc-check.ps1`.

---

## Файлы, которые будут изменены

| Файл | Фаза | Тип изменений |
|------|------|---------------|
| `ActionBarControl.xaml` | 1 | Реорганизация кнопок, удаление Settings/DirtyIndicator/RunCurrentOrderInfo |
| `ActionBarControl.xaml.cs` | 1 | Удаление обработчиков, перенесённых в TitleBar/MainWindow |
| `TitleBarControl.xaml` | 2, 3 | Добавление ⚙ + ContextMenu + red dot + инфо о заказе |
| `TitleBarControl.xaml.cs` | 2 | Обработчики меню настроек + UpdateBadge |
| `MainWindow.xaml` | 3 | Обновление привязок, перенос DirtyIndicator |
| `MainWindow.xaml.cs` | 3 | Обновление UpdateUpdateBadge, перенос обработчиков |
| `MainWindow.TitleDirty.cs` | 3 | Обновление логики заголовка |
| `SidebarControl.xaml` | 4 | Унификация отступов, уплотнение |

## Файлы, которые НЕ трогаем

- `OrderItem*.cs` — модель данных.
- `CalculationViewModel.cs` — бизнес-логика расчётов.
- `PriceService.cs`, `PrintService.cs`, `UpdateService.cs`, `OrderStorageService.cs` — сервисы.
- `QuickAddControl.xaml` — панель добавления (уже хорошо структурирована).
- `OrderItemsControl.xaml` — таблица позиций (уже хорошо структурирована).
- `TotalCardControl.xaml` — итоговая сумма.
- `OrdersHistoryControl.xaml`, `PricesControl.xaml`, `UpdatesTabControl.xaml` — вкладки.
- `Themes/*.xaml` — стили (меняем только если нужно для новых элементов).

---

## Last verified

2026-07-15 — план актуализирован и синхронизирован с текущим состоянием проекта.
