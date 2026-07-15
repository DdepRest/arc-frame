# Спецификация: Выдвижное левое меню навигации + Фиксированный сайдбар

## 1. Обзор

Нужно сделать так, чтобы левое меню навигации (сейчас 52px с иконками) выезжало при наведении курсора и показывало текстовые подписи рядом с иконками. Поведение аналогично правым оверлеям (Заказы, Цены, Обновления) — выезд поверх основного контента. Также убрать возможность сворачивания внутренних карточек сайдбара (ЗАКАЗЧИК, ДОГОВОР, ПРИМЕЧАНИЯ, ДОП. КП) — они всегда должны быть развёрнуты.

---

## 2. Текущее состояние

### 2.1. Левая панель навигации (`MainWindow.xaml`)
- Ширина: фиксированная 52px (`Grid.ColumnDefinitions` — `<ColumnDefinition Width="52"/>`)
- Содержит 4 кнопки-иконки: Расчёт (Ctrl+1), Заказы (Ctrl+2), Цены (Ctrl+3), Обновления (Ctrl+4)
- Каждая кнопка — `Style="{StaticResource NavButton}"`, 40×40px, иконка из шрифта Segoe Fluent Icons
- На кнопке «Заказы» — бейдж с количеством заказов (`NavOrdersBadge`)
- На кнопке «Обновления» — красная точка-индикатор (`NavUpdatesDot`)
- Активная кнопка подсвечивается `ActivePill` (3px Accent-полоска слева)
- Нет текстовых подписей — только иконки

### 2.2. Правые оверлеи (reference)
- OrdersOverlay, PricesOverlay, UpdatesOverlay — `Grid` с `Visibility="Collapsed"`
- При открытии: `ShowOverlay()` анимирует `TranslateTransform.X` от `panelWidth` до `0`
- Backdrop: полупрозрачный `#80000000` с fade-in
- Закрытие: `CloseAllOverlays()` — slide-out вправо + fade-out backdrop
- Анимация: 280ms slide-in (EaseOut), 220ms slide-out (EaseIn)

### 2.3. Сайдбар (`SidebarControl.xaml`)
- Ширина: 250px (фиксированная в `MainWindow.xaml` — `<ColumnDefinition Width="250"/>`)
- Содержит 4 карточки: ЗАКАЗЧИК, ДОГОВОР, ПРИМЕЧАНИЯ, ДОПОЛНИТЕЛЬНОЕ КП
- Каждая карточка collapsible — клик по заголовку сворачивает/разворачивает содержимое
- Анимация сворачивания: fade 180–200ms с chevron ▼/►
- Внутри: поля ввода (TextBox, DatePicker, ComboBox)

---

## 3. Требования

### 3.1. Левая панель навигации — выдвижная

#### 3.1.1. Свёрнутое состояние (по умолчанию)
- Ширина: 52px (как сейчас)
- Видны только иконки кнопок (Расчёт, Заказы, Цены, Обновления)
- Бейджи и индикаторы (`NavOrdersBadge`, `NavUpdatesDot`) — на своих местах
- `ActivePill` работает как сейчас

#### 3.1.2. Развёрнутое состояние (при hover)
- Триггер: наведение курсора на левую панель навигации
- Ширина: ~160px
- Каждая кнопка превращается в горизонтальный ряд: [ActivePill] [Иконка 20px] [Текстовая подпись]
- Подписи:
  - `&#xE1D0;` → «Расчёт»
  - `&#xE71D;` → «Заказы»
  - `&#xE8C7;` → «Цены»
  - `&#xE81C;` → «Обновления»
- Бейджи (`NavOrdersBadge`, `NavUpdatesDot`) остаются на иконке, как сейчас
- Текст подписи: `FontSize="12"`, `Foreground` = `TextMuted` (неактивно) / `Accent` (активно) / `TextPrimary` (hover)
- Активная кнопка: `FontWeight="SemiBold"`, `Foreground="Accent"`

#### 3.1.3. Анимация
- **Раскрытие**: `DoubleAnimation` ширины панели 52px → 160px, длительность 250ms, `CubicEase EaseOut`
- **Сворачивание**: 52px ← 160px, длительность 200ms, `CubicEase EaseIn`
- **Параллельно**: fade-in текста (Opacity 0→1, 150ms) при раскрытии; fade-out при сворачивании
- Анимация должна быть плавной, без дерганий

#### 3.1.4. Overlay-режим
- Панель навигации выезжает **поверх** основного контента (CalculationView + SidebarControl)
- Контент не сдвигается — панель накладывается на левую часть окна
- Z-order: NavigationPanel > SidebarControl (250px) > Content
- При развёрнутой панели она перекрывает левый край сайдбара на ~160px, но при hover на сайдбар панель сворачивается

#### 3.1.5. Grace period
- После ухода мыши с панели — задержка 500ms перед сворачиванием
- Если курсор вернулся в течение 500ms — панель остаётся развёрнутой
- Если фокус ввода находится внутри панели навигации (например, пользователь Tab-ом попал на кнопку) — панель не сворачивается

#### 3.1.6. Нет pin-режима
- Только hover-активация
- Ни при каких условиях панель не остаётся развёрнутой постоянно
- Состояние не сохраняется между сессиями

#### 3.1.7. Кликабельность в свёрнутом состоянии
- Кнопки в свёрнутом виде работают как сейчас — клик переключает вкладку / открывает overlay
- Клик НЕ триггерит раскрытие (только hover)

#### 3.1.8. Раскрытие НЕ меняет активную вкладку
- Наведение на кнопку «Заказы» не открывает OrdersOverlay — только показывает подпись
- Клик по кнопке (в любом состоянии) — выполняет текущую логику

### 3.2. Сайдбар — убрать collapsible-карточки

#### 3.2.1. Удалить возможность сворачивания
- Убрать `PreviewMouseLeftButtonDown` с заголовков карточек
- Убрать `ToggleSection()` метод в `SidebarControl.xaml.cs`
- Убрать chevron (`ChevronClient`, `ChevronContract`, `ChevronNotes`) из XAML
- Убрать анимации fade для `ContentClient`, `ContentContract`, `ContentNotes`

#### 3.2.2. Карточки всегда развёрнуты
- `ContentClient.Visibility = Visible` (постоянно)
- `ContentContract.Visibility = Visible` (постоянно)
- `ContentNotes.Visibility = Visible` (постоянно)
- `ContentAdditionalKp` — тоже всегда видим (если был collapsible)

#### 3.2.3. Заголовки карточек остаются
- Заголовки («ЗАКАЗЧИК», «ДОГОВОР», «ПРИМЕЧАНИЯ», «ДОПОЛНИТЕЛЬНОЕ КП») — остаются как визуальные разделители
- Только убирается интерактивность (клик не сворачивает)
- Chevron (▼/►) убирается

#### 3.2.4. Сохранить Opacity-анимацию при загрузке
- `Opacity="0"` на карточках + `AnimateCardsOnLoad()` в `MainWindow.xaml.cs` — оставить как есть
- Это entrance-анимация при старте приложения, не связана с collapsible behavior

### 3.3. Горячие клавиши
- Ctrl+1..4 — сохраняют текущее поведение (переключение вкладок)
- Tab-навигация внутри развёрнутой панели навигации — поддерживается
- Escape — если панель развёрнута, сворачивает её (дополнительно)

---

## 4. Техническая реализация

### 4.1. Изменения в `MainWindow.xaml`

#### 4.1.1. Навигационная панель
- Обёрнуть текущую `Border` навигации (Grid.Column="0") в дополнительный `Grid` или `Border` с фиксированной шириной 52px
- Добавить `RenderTransform` (`TranslateTransform` или изменение `Width`) для анимации
- Изменить `NavButton` Style: в развёрнутом виде кнопка должна быть шириной ~140px (внутри 160px панели)
- Добавить `TextBlock` для подписи рядом с иконкой (initially `Opacity="0"` или `Visibility="Collapsed"`)
- Структура кнопки в развёрнутом виде:
  ```
  [3px ActivePill] [20px Icon] [Padding 8px] [Label Text] [Badge]
  ```

#### 4.1.2. Overlay-контейнер
- Панель навигации должна быть вынесена за пределы основного `Grid` (или использовать `Panel.ZIndex`)
- Текущий layout:
  ```
  Grid (Main)
    Column 0: Nav (52px) | Column 1: Content (Sidebar + Calculation)
  ```
- Новый layout:
  ```
  Grid (Main)
    Layer 0: Content (Sidebar 250px + Calculation)
    Layer 1: Navigation Panel (52px → 160px, overlay, higher Z-index)
  ```

### 4.2. Изменения в `MainWindow.xaml.cs`

#### 4.2.1. Hover-логика
- Подписаться на `MouseEnter` / `MouseLeave` на панели навигации
- `MouseEnter` → `ExpandNavPanel()` (анимация width + fade-in labels)
- `MouseLeave` → запустить `DispatcherTimer` на 500ms → `CollapseNavPanel()`
- Если `MouseEnter` повторно в течение 500ms — отменить таймер
- Проверка `IsKeyboardFocusWithin` — если фокус внутри, не сворачивать

#### 4.2.2. Анимация
```csharp
private void ExpandNavPanel()
{
    _navCollapseTimer?.Stop();
    var widthAnim = new DoubleAnimation(160, TimeSpan.FromMilliseconds(250))
    {
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
    };
    NavBorder.BeginAnimation(WidthProperty, widthAnim);
    // Параллельно: fade-in текстовых подписей
}

private void CollapseNavPanel()
{
    if (NavBorder.IsKeyboardFocusWithin) return;
    var widthAnim = new DoubleAnimation(52, TimeSpan.FromMilliseconds(200))
    {
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
    };
    NavBorder.BeginAnimation(WidthProperty, widthAnim);
    // Параллельно: fade-out текстовых подписей
}
```

#### 4.2.3. Обновление `SetActiveNavButton`
- Должен корректно обновлять текстовые подписи (цвет `Accent` для активной, `TextMuted` для неактивной)

### 4.3. Изменения в `SidebarControl.xaml`

- Убрать `PreviewMouseLeftButtonDown` с заголовков (`CardClient_HeaderClick`, etc.)
- Убрать `ChevronClient`, `ChevronContract`, `ChevronNotes` TextBlock'и
- Убрать `x:Name="ContentClient"` и т.д. с анимациями (или оставить имена, но без анимаций)
- Убедиться, что `ContentClient`, `ContentContract`, `ContentNotes` всегда `Visible`

### 4.4. Изменения в `SidebarControl.xaml.cs`

- Удалить метод `ToggleSection`
- Удалить обработчики `CardClient_HeaderClick`, `CardContract_HeaderClick`, `CardNotes_HeaderClick`

### 4.5. Стили

#### 4.5.1. `NavButton` Style (обновление)
- Добавить `TextBlock` для подписи в `ControlTemplate`
- Подпись изначально `Opacity="0"`, `Width="0"` или `Visibility="Collapsed"`
- При развёртывании панели — fade-in + width expansion

---

## 5. Edge Cases

| Сценарий | Ожидаемое поведение |
|----------|---------------------|
| Пользователь быстро провёл мышью через панель | Grace period 500ms — панель не дергается, остаётся развёрнутой, если курсор вернулся |
| Пользователь нажал Tab и фокус на кнопке навигации | Панель не сворачивается, пока фокус внутри |
| Пользователь открыл OrdersOverlay | Навигация остаётся видимой (как сейчас), overlay закрывает контент справа |
| Пользователь навёл на сайдбар (250px), минуя навигацию | Навигация сворачивается (если курсор покинул область навигации), сайдбар работает как обычно |
| Окно меньше минимальной ширины | Панель всё равно выезжает поверх, но контент под ней сильнее перекрывается |
| Resize окна во время развёрнутой панели | Панель остаётся развёрнутой, адаптируется к новой высоте |
| Мышь ушла за пределы окна | Срабатывает MouseLeave, grace period 500ms, затем сворачивание |

---

## 6. UI/UX Детали

### 6.1. Подписи
- Шрифт: `Segoe UI`, `FontSize="12"`, `FontWeight="Regular"` (неактивно) / `"SemiBold"` (активно)
- Цвет: `TextMuted` → `TextPrimary` (hover) → `Accent` (active)
- Выравнивание: по левому краю, `VerticalAlignment="Center"`

### 6.2. Иконки в развёрнутом виде
- Размер иконки: `FontSize="20"` (как сейчас)
- Расположение: слева от текста, `Margin="0,0,8,0"`

### 6.3. Бейджи
- `NavOrdersBadge`: позиция — на иконке (top-right), как сейчас; в развёрнутом виде остаётся на иконке
- `NavUpdatesDot`: позиция — на иконке, как сейчас

### 6.4. ActivePill
- Должна корректно анимироваться при смене активной кнопки в обоих состояниях (свёрнутом и развёрнутом)

### 6.5. Плавность
- 60fps: используем `RenderTransform` и `Opacity` (GPU-accelerated)
- Избегать анимации `Margin`, `Padding`, `Left`/`Top` — это вызывает layout pass
- Анимировать только `Width` (панели), `Opacity` (текстовых подписей), `TranslateTransform.X` (если нужно)

---

## 7. Тестирование

### 7.1. Ручные проверки
1. При наведении на левую панель — плавное раскрытие до 160px, появление подписей
2. При уходе мыши — задержка 500ms, затем плавное сворачивание
3. Быстрое наведение/уход — панель не дергается
4. Клик по кнопкам в обоих состояниях работает
5. Бейджи и индикаторы видны в обоих состояниях
6. ActivePill корректно отображается
7. Tab-навигация — фокус держит панель открытой
8. OrdersOverlay открывается поверх развёрнутой/свёрнутой панели
9. Сайдбар: карточки не сворачиваются по клику, все поля доступны
10. Resize окна — панель адаптируется

### 7.2. Юнит-тесты (если применимо)
- Нет новой бизнес-логики — только UI-анимации и XAML
- Regression: существующие тесты `DataGridBindingsTests`, `QuickAddControlTests` и т.д. должны проходить

---

## 8. Файлы к изменению

| Файл | Изменения |
|------|-----------|
| `MainWindow.xaml` | Перестроить layout: навигация в overlay-контейнере; обновить NavButton Style с подписями; добавить анимации |
| `MainWindow.xaml.cs` | Hover-логика (`MouseEnter`/`MouseLeave`/`DispatcherTimer`); `ExpandNavPanel`/`CollapseNavPanel`; обновить `SetActiveNavButton` |
| `SidebarControl.xaml` | Убрать collapsible behavior: chevrons, header click handlers, ToggleSection |
| `SidebarControl.xaml.cs` | Удалить `ToggleSection`, `Card*_HeaderClick` handlers |
| `Themes/TabStyles.xaml` или `MainWindow.xaml` (Resources) | Обновить `NavButton` Style с поддержкой подписей |

---

## 9. Не в.scope

- Изменение ширины сайдбара (250px)
- Изменение содержимого карточек сайдбара
- Изменение правых оверлеев (Orders, Prices, Updates)
- Изменение ActionBar, TitleBar, QuickAdd, TotalCard
- Добавление новых функций (pin, drag-resize, и т.д.)
- Изменение расчётной логики, сохранения, печати

---

## 10. Согласованные решения (из интервью)

| Вопрос | Решение |
|--------|---------|
| Что выезжает | Только панель навигации (иконки); сайдбар с карточками не выезжает |
| Триггер | Hover (наведение мыши) |
| Режим | Overlay (поверх контента, аналогично правым панелям) |
| Ширина развёрнутой панели | ~160px |
| Бейджи | Остаются на иконке, как сейчас |
| Pin-режим | Нет, только hover |
| Grace period | 500ms после ухода мыши; не сворачиваться при фокусе ввода |
| Карточки сайдбара | Убрать возможность сворачивания, всегда развёрнуты |
| Скролл при клике на иконку | Не меняется (неактуально для навигации) |

---

*Создано: 2026-07-01*  
*Версия спецификации: 1.0*
