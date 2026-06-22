# Anwis — пилюли выбора режима в QuickAdd (спецификация)

## 1. Цель

Сделать выбор режима Anwis (ББ 60 / ББ 70 / ПП / Проём / Габарит) **заметным и очевидным**
при добавлении позиции, не занимая полезное пространство постоянно.

**Проблемы текущей реализации (v3.34.x):**
- Пользователи не знают про ПКМ на выпадающем списке «Тип»
- Тултип «Текущий режим: ББ 60 (ПКМ для изменения)» слишком незаметен
- Пользователи забывают сменить режим между добавлениями
- Хочется видеть все доступные режимы сразу, без дополнительных кликов

**Что НЕ трогаем:**
- Логику расчётов (AnwisSize, AnwisSizeMode, AnwisSizeService) — всё считается правильно
- Столбцы/колонки в DataGrid таблицы позиций — остаются без изменений
- Сохранение/загрузку заказов

---

## 2. QuickAdd: ряд пилюль выбора режима

### 2.1. Внешний вид

Под основной строкой полей QuickAdd (Тип | Цвет | Ширина | Высота | Кол-во | Цена | [Добавить])
появляется **горизонтальный ряд из 5 кнопок-пилюль**:

```
[ ББ 60 ] [ ББ 70 ] [ ПП ] [ Проём ] [ Габарит ]
```

- **Отступ от основной строки:** 8px (Margin="0,8,0,0")
- **Высота ряда:** ~30-32px (высота кнопки + padding)
- **Ширина кнопок:** auto по содержимому, min 48px
- **Горизонтальный зазор между кнопками:** 4px
- **Выравнивание:** по левому краю (HorizontalAlignment="Left")
- **Стиль:** Fluent-дизайн, скруглённые углы (CornerRadius="14"), как существующие пилюли в DataGrid

### 2.2. Состояния кнопок

| Состояние | Фон | Текст | Курсор |
|-----------|-----|-------|--------|
| Неактивная (не выбрана) | `ChipBg` (из ресурсов) | `TextSecondary` | Hand |
| Активная (выбрана) | `Accent` | `OnAccent` | Hand |
| Hover (не выбрана) | `AccentLight` | `TextPrimary` | Hand |
| Hover (выбрана) | `AccentHover` | `OnAccent` | Hand |

### 2.3. Видимость и анимация

#### 2.3.1. Логика видимости

- **Anwis выбран в «Тип»** → ряд пилюль появляется
- **Любой другой товар** → ряд скрыт, основная строка не сдвигается
- **Ничего не выбрано** → ряд скрыт

Переключение видимости происходит в `CmbQuickType_SelectionChanged` (уже есть хук).

#### 2.3.2. Анимация появления ( fade-in + slide-down )

При выборе Anwis ряд пилюль появляется с комбинированной анимацией:
- **Opacity:** 0 → 1, длительность 250ms
- **TranslateTransform.Y:** -8px → 0px, длительность 300ms (лёгкое выплывание сверху)
- **Easing:** `CubicEase { EasingMode = EasingMode.EaseOut }` — как во всех анимациях проекта

Перед запуском анимации:
1. `Visibility = Visible`
2. `Opacity = 0`
3. `RenderTransform = new TranslateTransform(0, -8)`

Код анимации (code-behind, в `CmbQuickType_SelectionChanged`):
```csharp
// Появление
PanelAnwisModes.Visibility = Visibility.Visible;
PanelAnwisModes.Opacity = 0;
PanelAnwisModes.RenderTransform = new TranslateTransform(0, -8);

var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(250))
{
    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
};
PanelAnwisModes.BeginAnimation(OpacityProperty, fadeIn);

var slideDown = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300))
{
    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
};
var transform = (TranslateTransform)PanelAnwisModes.RenderTransform;
transform.BeginAnimation(TranslateTransform.YProperty, slideDown);
```

#### 2.3.3. Анимация исчезновения ( fade-out + slide-up )

При переключении на не-Anwis товар:
- **Opacity:** текущая → 0, длительность 200ms
- **TranslateTransform.Y:** 0 → 8px, длительность 200ms
- После завершения анимации → `Visibility = Collapsed`

Код анимации (code-behind):
```csharp
// Исчезновение
var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200))
{
    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
};
fadeOut.Completed += (_, _) =>
{
    PanelAnwisModes.Visibility = Visibility.Collapsed;
};
PanelAnwisModes.BeginAnimation(OpacityProperty, fadeOut);

var slideUp = new DoubleAnimation(8, TimeSpan.FromMilliseconds(200))
{
    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
};
var transform = (TranslateTransform)PanelAnwisModes.RenderTransform;
transform.BeginAnimation(TranslateTransform.YProperty, slideUp);
```

#### 2.3.4. Прерывание анимации при быстром переключении

При переходе Anwis → не-Anwis → Anwis за < 500ms возможна ситуация когда анимация
исчезновения ещё не завершилась, а уже нужно показывать панель снова.

Решение: перед запуском анимации появления **сбросить** предыдущие анимации:
```csharp
PanelAnwisModes.BeginAnimation(OpacityProperty, null);
var transform = PanelAnwisModes.RenderTransform as TranslateTransform;
if (transform != null)
    transform.BeginAnimation(TranslateTransform.YProperty, null);
```

#### 2.3.5. Без анимации

Следующие сценарии НЕ анимируются — панель скрывается мгновенно:
- `StartNewOrder()` — панель скрыта с самого начала (товар не выбран)
- `OpenSelectedOrder()` — то же самое
- `ResetAnwisMode()` — только сброс активной пилюли, без анимации панели

#### 2.3.6. XAML-разметка

```xml
<StackPanel x:Name="PanelAnwisModes"
            Orientation="Horizontal"
            Margin="0,8,0,0"
            Visibility="Collapsed"
            Opacity="0">
    <StackPanel.RenderTransform>
        <TranslateTransform X="0" Y="0"/>
    </StackPanel.RenderTransform>
    <!-- 5 кнопок-пилюль -->
</StackPanel>
```

Используем `StackPanel` (а не `Border`) чтобы анимация применялась
непосредственно к контейнеру пилюль — проще и без лишней вложенности.

### 2.4. Поведение

- Клик по пилюле → мгновенно меняет `SelectedAnwisMode` в `QuickAddControl`
- Preview (чип с формулой) обновляется автоматически (уже вызывает `UpdateQuickPreview()`)
- Текущий ToolTip на «Тип» **обновляется** чтобы показывать «Режим: ББ 60» (уже есть `UpdateAnwisModeToolTip()`)
- ПКМ на «Тип» **сохраняется** как альтернативный способ для опытных пользователей

### 2.5. Сброс режима

- Режим **сохраняется** между добавлениями позиций в рамках одного заказа (текущее поведение)
- При `StartNewOrder()` → сброс на `Брусбокс60` (уже есть вызов `ResetAnwisMode()`)
- При `OpenSelectedOrder()` → сброс на `Брусбокс60` (уже есть вызов `ResetAnwisMode()`)

### 2.6. Клавиатурная навигация (опционально, фаза 2)

- Tab-последовательность: ... → Цена → [Tab] → пилюли → [Tab] → Добавить
- Стрелки влево/вправо внутри ряда пилюль
- Enter на пилюле = выбор режима
- **Фаза 1: только мышь.** Клавиатура — по запросу.

---

## 3. DataGrid: пилюля режима — левый клик

### 3.1. Текущее состояние

Пилюля в ячейке «Наименование» (в `OrderItemsControl.xaml`):
```xml
<Border ... PreviewMouseRightButtonDown="AnwisModePill_PreviewRightClick">
```
Реагирует только на **правый клик** (ПКМ).

### 3.2. Изменение

- Добавить обработчик **левого клика** (`PreviewMouseLeftButtonDown` или `MouseLeftButtonDown`)
- Левый клик → открывает **то же меню** выбора режима, что и ПКМ (`AnwisContextMenuBuilder.Build`)
- ПКМ **сохранить** (работают оба клика)
- Поведение кнопки (hover-анимация, стиль) не меняется

### 3.3. Важно

- Колонки DataGrid **не трогаем** — никаких новых столбцов, никаких ComboBox'ов
- Только добавляем реакцию на левый клик к существующей пилюле

---

## 4. Файлы, которые нужно изменить

### 4.1. `QuickAddControl.xaml`
- Добавить `StackPanel` / `ItemsControl` с 5 кнопками-пилюлями под основной строкой
- `x:Name="AnwisModePills"` или `x:Name="PanelAnwisModes"`
- Изначально `Visibility="Collapsed"`

### 4.2. `QuickAddControl.xaml.cs`
- В `CmbQuickType_SelectionChanged`: показать/скрыть панель пилюль через `AnwisSizeService.IsApplicable()`
- Обработчики кликов для каждой пилюли (или один общий с привязкой через Tag/DataContext)
- Вызов `UpdateAnwisModeToolTip()` и `UpdateQuickPreview()` при смене режима
- `ResetAnwisMode()`: обновить визуальное состояние пилюль (активная/неактивная)

### 4.3. `OrderItemsControl.xaml`
- Добавить `PreviewMouseLeftButtonDown="AnwisModePill_PreviewLeftClick"` к пилюле в DataTemplate

### 4.4. `OrderItemsControl.xaml.cs`
- Добавить метод `AnwisModePill_PreviewLeftClick` (форвардинг на MainWindow)
- Либо использовать существующий `AnwisModePill_PreviewRightClick` и добавить левый клик

### 4.5. `MainWindow.xaml.cs`
- Добавить метод `AnwisModePillLeftClick` (или переиспользовать `AnwisModePillRightClick`)
- Логика идентична текущему `AnwisModePillRightClick`

---

## 5. Что НЕ меняется

- `AnwisSizeMode.cs` — enum без изменений
- `AnwisSize.cs` — структура без изменений
- `AnwisSizeService.cs` — сервис без изменений
- `AnwisContextMenuBuilder.cs` — без изменений
- `CalculationViewModel.cs` — без изменений
- `OrderItem.cs` — без изменений
- `FactoryTextService.cs` — без изменений
- Колонки DataGrid (`OrderItemsControl.xaml`) — без изменений
- Стили тем (`Themes/*.xaml`) — без изменений (используем существующие ресурсы)

---

## 6. Крайние случаи

### 6.1. Быстрое переключение Тип → не-Anwis → Anwis
- При выборе не-Anwis товара пилюли скрываются
- При повторном выборе Anwis пилюли появляются с последним выбранным режимом

### 6.2. Добавление позиции без смены режима
- Пользователь может кликнуть «Добавить» не трогая пилюли — используется текущий `SelectedAnwisMode`
- Это текущее поведение, без изменений

### 6.3. Изменение режима у уже добавленной позиции
- Только через пилюлю в DataGrid (левый/правый клик)
- QuickAdd на это не влияет

### 6.4. Поиск через строку поиска
- При выборе Anwis через поиск (`SearchSuggestions`) пилюли должны показаться
- Проверить, что `CmbQuickType_SelectionChanged` срабатывает при выборе из поиска

---

## 7. Приёмка (критерии готовности)

- [ ] При выборе «Anwis» в «Тип» — ряд из 5 пилюль появляется с анимацией fade-in + slide-down
- [ ] При выборе любого другого товара — пилюли исчезают с анимацией fade-out + slide-up
- [ ] Быстрое переключение Anwis → другой → Anwis не ломает анимацию (пилюли корректно появляются)
- [ ] При `StartNewOrder()` и `OpenSelectedOrder()` пилюли скрыты, анимация не проигрывается
- [ ] Активная пилюля подсвечена цветом `Accent`
- [ ] Клик по пилюле меняет режим и обновляет preview
- [ ] ПКМ на «Тип» всё ещё работает
- [ ] ToolTip на «Тип» показывает текущий режим
- [ ] Левый клик по пилюле в DataGrid открывает меню выбора режима
- [ ] Правый клик по пилюле в DataGrid всё ещё работает
- [ ] Режим сохраняется между добавлениями позиций
- [ ] StartNewOrder / OpenSelectedOrder сбрасывают режим на ББ 60
- [ ] Все существующие тесты проходят
- [ ] Расчёты не изменились
