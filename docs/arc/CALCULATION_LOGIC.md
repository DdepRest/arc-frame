# CALCULATION_LOGIC.md

## Где находится расчёт стоимости

Главные файлы:

1. **`MosquitoNetCalculator/Models/OrderItem.Calculations.cs`** — расчёт `CalculatedValue` и `Total` для одной строки.
2. **`MosquitoNetCalculator/Models/AnwisSize.cs`** — коррекция размеров Anwis (3 слоя: Ввод → Расчёт → Завод).
3. **`MosquitoNetCalculator/ViewModels/CalculationViewModel.cs`** — агрегация итогов по всем позициям.
4. **`MosquitoNetCalculator/Services/PriceService.cs`** — загрузка и поиск цен.

---

## Какие данные входят в расчёт

Для каждой строки заказа (`OrderItem`):

| Поле | Описание |
|------|----------|
| `Name` | Название товара (Anwis, Отлив, ПСУЛ, Работа и т.д.) |
| `Color` | Цвет товара |
| `Width` | Ширина в мм (хранимое/расчётное значение) |
| `Height` | Высота в мм (хранимое/расчётное значение) |
| `Quantity` | Количество (минимум 1) |
| `Price` | Цена за единицу (за м², м.п. или шт.) |
| `IsActive` | Включена ли позиция в итог |
| `InstallationMode` | 0=включён, 1=без монтажа, 2=в конструкцию |
| `AnwisSizeMode` | Режим Anwis (только для Anwis) |

---

## Единицы измерения по товарам

```csharp
"ПСУЛ"          → "м.п."   // периметр: (W+H)*2 / 1000
"Уплотнение"    → "м.п."   // периметр: (W+H)*2 / 1000
"Откос материал"→ "шт."    // 1 шт.
"Работа"        → "шт."    // 1 шт.
"Брус"          → "шт."    // 1 шт.
"Пояс"          → "шт."    // 1 шт.
"Доставка"      → "шт."    // 1 шт.
остальные       → "м²"     // площадь: W*H / 1_000_000
                              // (Anwis, Отлив, Козырёк, Короб, На навесах,
                              //  Оконная на метал. крепл., Дверная сетка)
```

---

## Формулы расчёта CalculatedValue (за 1 штуку)

```csharp
// ПСУЛ и Уплотнение — периметр в метрах
CalculatedValue = Math.Round((Width + Height) * 2 / 1000.0, 3)

// Откос материал, Работа, Брус, Пояс, Доставка — фиксированно 1 шт.
CalculatedValue = 1

// Всё остальное (Anwis, Отлив, Козырёк, Короб, На навесах, Дверная сетка, Оконная на метал. крепл.) — площадь в м²
CalculatedValue = Math.Round(Width * Height / 1_000_000.0, 3)
```

---

## Формула итоговой стоимости строки

```csharp
Total = Math.Round(CalculatedValue * Price * Quantity, 2)
```

**Пример:** Anwis 1000×1000 мм, цена 1800 руб/м², кол-во 2
- CalculatedValue = 1000 * 1000 / 1_000_000 = 1.0 м²
- Total = 1.0 * 1800 * 2 = 3600.00 руб.

---

## 4 типа размеров для Anwis

Для Anwis существует 4 представления размеров. Важно не путать их:

| Тип | Название в коде | Описание |
|-----|-----------------|----------|
| 1. **Введённые (raw)** | `ШиринаВвод` / `ВысотаВвод` | То, что пользователь набрал в поле ввода. Для Anwis получается обратным пересчётом из хранимых значений. |
| 2. **Хранимые / Расчётные** | `OrderItem.Width` / `Height` | Размеры после применения коррекции режима. Используются для расчёта площади, цены и **КП**. |
| 3. **Заводские** | `Размеры.ШиринаЗавод` / `ВысотаЗавод` | Расчётные размеры минус 20 мм. Уходят в текст «На завод». |
| 4. **Отображаемые в таблице** | `OrderItem.Width` / `Height` | Те же хранимые значения, что видны в DataGrid. |

### Формулы коррекции режимов (Ввод → Расчёт)

| Режим | Ширина ввод → расчёт | Высота ввод → расчёт |
|-------|---------------------|---------------------|
| ББ 60 | W + 2 мм | H − 30 мм |
| ББ 70 | W − 2 мм | H − 30 мм |
| Профипласт | без изменений | без изменений |
| Размер проёма | W + 20 мм | H + 20 мм |
| Габаритный | без изменений | без изменений |

### Заводские размеры (Расчёт → Завод)

От расчётных размеров отнимается 20 мм по ширине и высоте **для всех режимов**:

```
Завод_W = Расчёт_W − 20
Завод_H = Расчёт_H − 20
```

Если расчётный размер меньше 20 мм, заводской размер clamps to 0.

### Для не-Anwis товаров

Все 4 типа равны (identity): Ввод = Расчёт = Завод = КП.

**Критически важно:** формулы Anwis НЕ должны применяться к не-Anwis товарам. Это было источником бага в v3.35.0.

---

## Какие размеры используются где

| Назначение | Какой размер используется | Пример (Anwis ББ 60, ввод 1000×1000) |
|-----------|--------------------------|--------------------------------------|
| **Отображение пользователю в поле ввода** | `ШиринаВвод` / `ВысотаВвод` | 1000 × 1000 |
| **Отображение в таблице расчёта** | `OrderItem.Width` / `Height` | 1002 × 970 |
| **Расчёт площади и цены** | `OrderItem.Width` / `Height` | 1002 × 970 |
| **КП (коммерческое предложение)** | `OrderItem.Width` / `Height` | 1002 × 970 |
| **Отправка на завод** | `Размеры.ШиринаЗавод` / `ВысотаЗавод` | 982 × 950 |

> **Подтверждено владельцем (2026-06-24):** В КП должны быть **хранимые (расчётные)** размеры (`Width`/`Height`), а не введённые пользователем. Это текущее поведение кода — оставить.

---

## Итоговая сумма заказа (TotalInfo)

```csharp
// В CalculationViewModel.CalculateTotal(double additionalKpTotal):

var validItems = OrderItems.Where(i => !string.IsNullOrEmpty(i.Name) && i.IsActive && i.Total > 0).ToList();

double itemsTotal = validItems.Sum(i => i.TotalWithDeduction);
double total = itemsTotal + additionalKpTotal;

// Подытоги:
double totalArea   = validItems.Where(i => i.Unit == "м²").Sum(i => i.CalculatedValue * i.Quantity);
double totalLinear = validItems.Where(i => i.Unit == "м.п.").Sum(i => i.CalculatedValue * i.Quantity);
int    totalPieces = validItems.Where(i => i.Unit == "шт.").Sum(i => i.Quantity);
```

---

## Монтаж (вычеты из стоимости)

Применимые товары:

| Товар | Режим по умолчанию | Базовая ставка |
|-------|-------------------|----------------|
| Anwis | Монтаж включён | 500 ₽/шт. |
| На навесах | Монтаж включён | 500 ₽/шт. |
| Дверная сетка | Монтаж включён | 600 ₽/шт. |
| Оконная на метал. крепл. | Монтаж включён | 500 ₽/шт. |
| **Отлив** | **Без монтажа** | **500 ₽/м.п.** |
| **Козырёк** | **Без монтажа** | **750 ₽/м.п.** |

Для Anwis, На навесах, Дверной сетки и Оконной на метал. крепл. вычет/надбавка указывается **за штуку**.
Для **Отлива** и **Козырька** ставка указывается **за метр погонный** (периметр изделия).

### Формула

```csharp
// Для штучных товаров:
TotalWithDeduction = Total + amount × Quantity

// Для Отлива/Козырька (периметр в метрах):
linearMeters = (Width + Height) × 2 / 1000
TotalWithDeduction = Total + amount × linearMeters × Quantity
```

Значения по умолчанию задаются через `OrderItem.GetDefaultInstallationDeduction(name)`,
`GetDefaultInstallationSurcharge(name)` и `GetDefaultInstallationAdjustment(name)`.
Положительное значение добавляет к сумме, отрицательное — вычитает (signed convention, v3.46.1).

| Режим | Эффект |
|-------|--------|
| 0 (включён) | Без изменений |
| 1 (без монтажа) | Total − InstallationDeduction × Quantity (по умолч. 500 руб./шт.) |
| 2 (в конструкцию) | Total − InstallationSurcharge × Quantity (по умолч. 500 руб./шт.) |

```csharp
TotalWithDeduction = _installationMode switch
{
    1 => Math.Round(Math.Max(0, Total - InstallationDeduction * Quantity), 2),
    2 => Math.Round(Math.Max(0, Total - InstallationSurcharge * Quantity), 2),
    _ => Total
};
```

**Семантика per-piece:** вычет указывается как `руб./шт.` в UI и применяется один раз за каждую
единицу товара в строке. Например: Anwis ×3 шт., режим «В конструкцию», surcharge 500 →
итоговый вычет 1500 ₽ (а не 500 ₽). Quantity-1 → backward-compatible: для одной штуки
формула даёт тот же результат, что и до фикса. Текущий `Quantity` зажимается снизу на 1,
поэтому манипуляций с Quantity=0 для вычета не возникает. JSON-схема (`InstallationDeduction`,
`InstallationSurcharge`) не меняется — это всё ещё per-unit поля.

> Если deduction × Quantity > Total — результат зажимается на 0 (никогда не отрицательный).

---

## Антикошка (надбавка +2000 ₽/м²)

Для четырёх площадных полотен доступна опция «Антикошка» — усиленная сетка с фиксированной надбавкой к базовой цене каталога.

### Применимость

| Товар | Базовая цена (пример) | Цена с антикошкой |
|-------|----------------------|-------------------|
| Anwis | 1800 ₽/м² | 3800 ₽/м² |
| На навесах | 2900 ₽/м² | 4900 ₽/м² |
| Оконная на металл крепл. | 3200 ₽/м² | 5200 ₽/м² |
| Дверная сетка | 3000 ₽/м² | 5000 ₽/м² |

### Механика

```csharp
// Константа надбавки
OrderItem.AnticatSurcharge = 2000.0;

// В QuickAdd при включённой опции цена в поле «Цена» увеличивается:
Price = CatalogPrice + AnticatSurcharge   // например, 1800 + 2000 = 3800

// OrderItem.Price хранит уже увеличенную цену.
// Расчёт итога не меняется:
Total = CalculatedValue * Price * Quantity
```

- Надбавка **фиксированная** — не редактируется пользователем.
- Флаг `IsAnticat` хранится в `OrderItem` и сериализуется в `OrderItemData` (JSON).
- Переключатель доступен **только в панели QuickAdd** при выборе применимого типа сетки. После добавления позиции в таблицу флаг нельзя изменить.
- При отображении в DataGrid, КП и тексте «На завод» к названию товара добавляется суффикс `(Антикошка)` через свойство `OrderItem.DisplayName`.
- `SetDefaultPrice` фиксирует цену с надбавкой как «каталоговую», поэтому ручное изменение цены в таблице корректно детектируется через `IsPriceOverridden`.

---

## Откуда берутся цены

1. **Стартовый каталог** — зашит в `PriceService.DefaultPrices` (27 записей, 14 товаров).
2. **Пользовательские цены** — сохраняются в `%AppData%\MosquitoNetCalculator\prices.json`.
3. При первом запуске копируются дефолтные цены в AppData.
4. Пользователь может редактировать цены во вкладке "Цены".

---

## Как расчёт связан с КП

`PrintService.GenerateKpHtml` берёт `OrderItem.Width` и `OrderItem.Height` (хранимые/расчётные размеры) и подставляет их в HTML-шаблон.

Подробнее:
- `PrintService.FillTemplate` строки 48–49: `item.Width:F0`, `item.Height:F0`.
- Для Anwis в КП попадают расчётные размеры после применения режима (например, 1002×970 для ББ 60 при вводе 1000×1000).
- Для не-Anwis в КП попадают хранимые размеры без изменений.
- Расчёт `CalculatedValue` и `TotalWithDeduction` использует те же хранимые размеры.

> **Проверить владельцу (подтверждено 2026-06-24):** В КП показываются расчётные размеры (1002×970) — это правильно. Клиент видит реальные размеры сетки.

---

## Как расчёт связан с отправкой на завод

`FactoryTextService.Generate` использует `item.Размеры.ШиринаЗавод` / `ВысотаЗавод` для Anwis и обычные `Width`/`Height` для остальных.

### Auto-selection: какие товары попадают в партию по умолчанию

`FactoryTextService.BuildSelectableItems` строит список чекбоксов для диалога «На завод».
По умолчанию галочка **включена** для товаров, которые фабрика реально изготавливает из сетки/профиля:

| Категория | Товары | По умолчанию |
|-----------|-------|--------------|
| Сетки (производство) | Anwis, На навесах, Оконная на метал. крепл., Козырёк, Дверная сетка | ✅ включён |
| Готовые элементы (НЕ производство) | **Отлив**, Короб, Уплотнение, Откос материал | ❌ выключен |
| Услуги / штучные | ПСУЛ, Работа, Доставка, Брус, Пояс | ❌ выключен |

Контракт зафиксирован в коде: `FactoryTextService.notForProduction` HashSet.
**Отлив перенесён из «производственных» в «готовый элемент» (Unreleased, план. v3.41.x)** — это готовый оконный слив/подоконник,
а не сетчатое полотно, поэтому пользователь явно включает его галочкой перед отправкой партии.
Покрыто `FactoryTextServiceTests.BuildSelectableItems_NonProduction_IsNotSelected("Отлив")` и
`ManualChecklistTests.Check12_BuildSelectableItems_Production_On_NonProduction_Off`.

---

## Как данные Auto-update попадают на UI (AddNewUpdate flow)

Когда фоновая или стартаповая проверка находит новый релиз, пользователь видит новый блок в шапке «Обновления» (бейдж «Доступно обновление vX.Y.Z») — **без перезапуска**. Ключевое свойство flow: старая карточка «Новейшая» НЕ «мигает» при появлении новой.

### Главные файлы потока

| Файл | Роль |
|---|---|
| `MosquitoNetCalculator/Services/UpdateService.cs` | `FireUpdateDetected` (per-subscriber isolation через `GetInvocationList()`) + `CreateReleaseStub(version)` helper |
| `MosquitoNetCalculator/MainWindow.Progress.cs` (partial) | `OnUpdateDetected` — sync `Dispatcher.Invoke(() => ViewModel.AddNewUpdate(item))` |
| `MosquitoNetCalculator/ViewModels/MainWindowViewModel.cs` | `AddNewUpdate(newItem)` — атомарный 3-step: set new → clear old → `Insert(0, …)` |
| `MosquitoNetCalculator/Controls/UpdatesTabControl.xaml` | DataTrigger `Binding={Binding IsLatest}` для бейджа «Новейшая» |
| `MosquitoNetCalculator/Services/UpdateCheckScheduler.cs` | Триггер каждые 30 мин / после 10 мин idle |

### Архитектурная диаграмма

```
┌──────────────────────────────────────────────────────────────────────┐
│  UpdateCheckScheduler (timer)                                        │
│      │  every 30 min  OR  after 10 min idle                          │
│      ▼                                                               │
│  UpdateService.CheckInBackgroundAsync  (background thread)           │
│      │  FetchManifestAsync(HttpClient)            → releases.json     │
│      │  GetAvailableUpdate(manifest, CurrentVersion)                 │
│      ▼                                                               │
│  if (release.Version > CurrentVersion)                               │
│      │  FireUpdateDetected(CreateReleaseStub(release.Version))       │
│      │      ↳ безопасный per-subscriber iteration + try/catch each   │
│      │      ↳ stub UpdateItem: Version, Type="Доступно",             │
│      │                       Title="Доступно обновление v…",          │
│      │                       Changes=["stub"]  ← см. «stub» ниже      │
│      ▼                                                               │
│  ToastService.ShowUpdateNotification (persistent, кнопки «Обновить»/«Позже») │
│      ▲                                                               │
│      │  то же самое происходит из RunUpdateFlowAsync →               │
│      │  DialogService.ShowUpdateAvailable (confirmed=true) → fire   │
└──────────────────────────────────────────────────────────────────────┘
                              │ UpdateDetected event
                              ▼
┌──────────────────────────────────────────────────────────────────────┐
│  MainWindow.OnUpdateDetected  (MainWindow.Progress.cs, partial)      │
│      │  Dispatcher.Invoke (sync, НЕ BeginInvoke)                     │
│      │   ↳ sync нужен: AddNewUpdate — синхронный 3-step,             │
│      │     WPF рендер происходит ПОСЛЕ полного вызова →              │
│      │     нет промежуточного состояния «0 карточек IsLatest».        │
│      ▼                                                               │
│  ViewModel.AddNewUpdate(newItem):                                    │
│      │  1) newItem.IsLatest = true                                   │
│      │  2) для всех existing в Updates: IsLatest = false             │
│      │  3) Updates.Insert(0, newItem)                                │
│      ▼                                                               │
│  Updates[0]   = newItem    (IsLatest=true)  ← бейдж «Новейшая»        │
│  Updates[1..N] = old items  (ref-identity preserved,                 │
│                              only the old-latest fires IsLatest=false)│
└──────────────────────────────────────────────────────────────────────┘
```

### Что гарантирует архитектура

1. **Один «Новейшая» в любой момент времени.** Новый stub всегда
   `IsLatest=true`; `AddNewUpdate` явно сбрасывает старые.
   Тест: `AddNewUpdate_ExactlyOneItemIsLatest`.

2. **Никакого «мигания» старой карточки.** Синхронный `Dispatcher.Invoke`
   + синхронный `AddNewUpdate` = все `PropertyChanged` накапливаются до
   WPF-рендера. Ни один момент времени не показывает «все бейджи сняты».
   Тест: `AddNewUpdate_ZeroIsLatestFrame_neverObserved` —
   `PropertyChanged`-snapshotter на каждый элемент, `count IsLatest >= 1`
   во ВСЕХ записанных моментах.

3. **Минимальная PropertyChanged нагрузка на старые карточки.** Только
   карточка, которая раньше имела `IsLatest=true` (ровно одна), fire'ит
   PropertyChanged — и только с `PropertyName="IsLatest"`. Никаких других
   полей → WPF не пересчитывает binding'и на старых.
   Тест: `AddNewUpdate_OldItemsFire_OnlyIsLatest_PropertyChanged`.

4. **Reference identity сохранена.** `ObservableCollection.Insert(0,…)` —
   это index re-shuffle, не re-mount. WPF `ItemsControl` переиспользует тот
   же `ContentPresenter` для каждой старой карточки (=> ноль layout-overhead).
   Тест: `AddNewUpdate_OldItems_ReferenceIdentity_Unchanged`.

5. **Подписка чистая, нет утечки памяти.** `MainWindow` подписывается
   `UpdateDetected += OnUpdateDetected` в ctor и отписывается в `Closed`.
   Static-event + per-window подписчик не утекает, потому что обе стороны
   живут синхронно.

6. **Stub placeholder — осознанный compromise.** Запущенный бинарник НЕ
   может отобразить полный changelog будущей версии (он вшит в её
   embedded `update-log.json`). Stub говорит «обновите, чтобы увидеть
   подробности». Полный changelog подтягивается автоматически ПОСЛЕ
   установки и перезапуска через
   `MainWindowViewModel.ctor` → `UpdateLog.AllNewestFirst()`.

7. **Per-subscriber isolation в `FireUpdateDetected`.** Один «плохой»
   подписчик, бросающий исключение, не убивает доставку для остальных
   (используется `GetInvocationList()` + try/catch each).
   Тест: `UpdateDetected_HandlerSwallows_ExceptionsFromSubscribers`.

### Тестовое покрытие Auto-update → UI flow

| Тест | Что локает |
|---|---|
| `AddNewUpdate_ZeroIsLatestFrame_neverObserved` | ≥1 карточка с `IsLatest=true` во ВСЕХ моментах наблюдения |
| `AddNewUpdate_OldItemsFire_OnlyIsLatest_PropertyChanged` | только old-latest карточка fire'ит PropertyChanged, только для `IsLatest` |
| `AddNewUpdate_OldItems_ReferenceIdentity_Unchanged` | `ObservableCollection.Insert(0,…)` сохраняет ref-identity старых |
| `AddNewUpdate_NullInput_NoOp` | null guard на `newItem` |
| `AddNewUpdate_CalledTwice_SecondCall_DemotesFirst` | property-based logic composes через серию релизов |
| `UpdateDetected_FireUpdateDetected_…` | handlers receive probe через canonical fire path |
| `UpdateDetected_HandlerSwallows_ExceptionsFromSubscribers` | throwing subscriber не short-circuits остальных |
| `UpdateDetected_CreateReleaseStub_HasExpectedShape` | stub contract locked: anchors via `private const string` для grep-during-refactor |
| `FetchManifestAsync_NewerReleaseAvailable_DoesNotFireEvent` | regression: только оркестраторы fire'ят, НЕ сам `FetchManifest` |

### Когда срабатывает

- **Фоновая проверка** (каждые 30 мин или после 10 мин idle, тихо,
  persistent toast в шапке) — `CheckInBackgroundAsync`.
- **Стартаповая проверка** (после `Loaded`, один раз за сессию, модальный
  диалог «Доступно обновление» при подтверждении) — `CheckOnStartupAsync`
  → `RunUpdateFlowAsync`.
- **Ручная проверка** (через шестерёнку → «Проверить обновления»,
  dialog как стартап) — `CheckAndApplyAsync(isAutomatic: false)`.

Все три пути сходятся в одном `FireUpdateDetected(CreateReleaseStub(...))`.

---

## API-контракт `DistributedSharedSum` (общие материалы откоса)

> **Версия:** v3.43.5 (2026-07-06)  
> **Затронутые файлы:** `SlopeCalculatorService.cs`, `OrderItem.Calculations.cs`, `SlopeCalculation.cs`

### Проблема

В заказе может быть несколько строк «Откос» (и/или одна строка с `Quantity > 1`).
Герметик и скотч — **общие материалы** на весь заказ: один тюбик герметика
хватает на 4 окна, один моток скотча — на 3 окна. Если просто умножить
стоимость герметика/скотча на `Quantity` каждой строки, общая сумма по заказу
завысится (общий материал будет учтён несколько раз).

### Решение

Стоимость герметика и скотча рассчитывается **глобально** по всем откосам
заказа, а затем **распределяется пропорционально `WindowCount`** между строками
«Откос» через свойство `SlopeCalculation.DistributedSharedSum`.

### Кто вычисляет

```csharp
SlopeCalculatorService.RecalculateSealantAndTape(IEnumerable<OrderItem> items)
```

**Когда вызывать:**
- После добавления/удаления/изменения строк «Откос» в заказе.
- После изменения `WindowCount` в существующем откосе.
- Перед финальным пересчётом итогов и печатью КП.

**Контракт:**
1. Метод учитывает **только** строки с `Name == "Откос"` и ненулевым
   `SlopeData`.
2. Строки «Работа за откос» игнорируются — они не влияют на расход
   герметика/скотча.
3. Если несколько `OrderItem` ссылаются на один и тот же `SlopeCalculation`
   (shared instance), он учитывается **один раз** (`Distinct()` по ссылке).
4. Общее количество окон (`totalWindowCount`) — сумма `WindowCount` всех
   уникальных `SlopeCalculation`.
5. Количество герметика: `Math.Ceiling(totalWindowCount / 4.0)` тюбиков.  
   Количество скотча: `Math.Ceiling(totalWindowCount / 3.0)` мотков.
6. Общая стоимость (`Sealant.Sum + Tape.Sum`) распределяется между откосами
   пропорционально `WindowCount`. Последний откос забирает остаток, чтобы
   сумма по заказу совпадала с точностью до копейки.

### Кто использует

```csharp
// OrderItem.Recalculate() для Name == "Откос"
double perWindowSum = SlopeData.Sandwich.Sum + SlopeData.Foam.Sum
                      + SlopeData.StartProfile.Sum + SlopeData.FProfile.Sum
                      + SlopeData.Penoplex.Sum;
double sharedSum = SlopeData.DistributedSharedSum;
_total = Math.Round(perWindowSum * Quantity + sharedSum, 2);
```

`DistributedSharedSum` — это **доля** общих материалов, отнесённая к данному
`OrderItem`. Она прибавляется к per-window сумме, умноженной на `Quantity`.

> **Важно:** `DistributedSharedSum` — вычисляемое in-memory значение. Оно
> **не сериализуется** в `SlopeCalculationData` и не сохраняется в JSON.
> После загрузки заказа из файла необходимо заново вызвать
> `RecalculateSealantAndTape`, чтобы восстановить корректное распределение.

### Защитная инициализация

В `_ApplyDefaults` (вызывается из `Calculate` и `UpdateInPlace`) добавлена
защитная инициализация для изолированного использования:

```csharp
if (windowCount > 0 && windowCount == totalWindowCount)
{
    calc.DistributedSharedSum = calc.Sealant.Sum + calc.Tape.Sum;
}
```

Это гарантирует, что для **одиночного откоса** (unit-тесты, первичный расчёт,
заказ с одной строкой «Откос») `DistributedSharedSum` сразу равен полной сумме
герметика/скотча, и `OrderItem.Total` корректен даже без явного вызова
`RecalculateSealantAndTape`.

В **многопозиционном заказе** значение перезаписывается в
`RecalculateSealantAndTape`.

### Пример распределения

Три откоса: `WindowCount = [1, 1, 1]` (итого 3 окна).

```
Sealant.Quantity = ceil(3 / 4) = 1 тюбик
Tape.Quantity    = ceil(3 / 3) = 1 моток
Sealant.Sum      = 1 × 350 = 350 ₽
Tape.Sum         = 1 × 135 = 135 ₽
totalSharedSum   = 485 ₽
```

Распределение:

| Откос | WindowCount | DistributedSharedSum |
|-------|-------------|----------------------|
| 1     | 1           | round(485 × 1/3) = 161.67 ₽ |
| 2     | 1           | round(485 × 1/3) = 161.67 ₽ |
| 3     | 1           | 485 − 161.67 − 161.67 = 161.66 ₽ (остаток) |

Сумма по всем откосам: `161.67 + 161.67 + 161.66 = 485.00 ₽` — совпадает с
глобальной стоимостью общих материалов.

### Что будет, если нарушить контракт

- **Не вызвать `RecalculateSealantAndTape` для многопозиционного заказа:**
  `DistributedSharedSum` останется равным защитной инициализации (или 0 при
  `totalWindowCount > windowCount`). Итоговая сумма по строкам «Откос» будет
  занижена — общие материалы не попадут в Total.

- **Вызвать `RecalculateSealantAndTape` только для части строк:**
  распределение будет считать только переданные строки, что приведёт к
  некорректному `totalWindowCount` и неверной доле.

- **Передать строки «Работа за откос» вместо «Откос»:**
  метод отфильтрует их (`Where(i => i.Name == "Откос")`), и распределение не
  произойдёт.

### Тестовое покрытие контракта

| Тест | Что проверяет |
|---|---|
| `RecalculateSealantAndTape_ThreeWindows_DistributesSharedCost` | Корректное распределение между 3 окнами |
| `RecalculateSealantAndTape_UnevenWindowCount_LastTakesRemainder` | Остаток копейки забирает последний откос |
| `RecalculateSealantAndTape_SharedSlopeData_DoesNotDoubleCount` | Shared `SlopeData` учитывается один раз |
| `RecalculateSealantAndTape_LaborItemsIgnored` | «Работа за откос» не влияет на распределение |
| `RecalculateSealantAndTape_OrderItemTotal_UsesDistributedSharedSum` | `OrderItem.Total` использует `DistributedSharedSum` |
| `RecalculateSealantAndTape_SingleItem_GetsFullSharedSum` | Одиночный откос получает полную сумму |
| `RecalculateSealantAndTape_EmptyInput_DoesNotThrow` | Пустая коллекция не вызывает исключение |
| `RecalculateSealantAndTape_PreservesUserOverrides` | Ручные правки `Quantity` сохраняются |

---

## Экономия Старт/F-планка в откосах

> **Версия:** v3.44.3 (2026-07-12)  
> **Затронутые файлы:** `SlopeCalculatorService.cs`, `SlopePanelControl.xaml.cs`, `SlopeCalculatorServiceTests.cs`

### Проблема

В панели откосов есть чекбокс **«Экономия профилей»** (`IsProfileEconomyApplied`). Когда экономия включена, программа оптимизирует суммарное количество Старт-профиля и F-профиля по всем окнам заказа (например, три окна 1000×1000 мм дают 3 куска длиной 1000 мм, которые можно вырезать из одной 6000-мм полосы). Когда экономия выключена, каждое окно должно учитывать свой профиль отдельно.

Баг заключался в том, что при выключенной экономии `RecalculateSealantAndTape` всё равно выставляла глобальные (оптимизированные) количества профилей, а `OrderItem.Total` умножал per-window материалы на `Quantity` (=WindowCount). В результате профили учитывались дважды.

### Решение

`SlopeCalculatorService.RecalculateSealantAndTape` теперь разделяет откосы на участников и не-участников экономии:

1. **Экономия выключена (`IsProfileEconomyApplied = false`)**:  
   `StartProfile.Quantity` и `FProfile.Quantity` остаются per-window (3-сторонние значения). `OrderItem.Total` корректно умножает per-window сумму на `Quantity`.

2. **Экономия включена (`IsProfileEconomyApplied = true`)**:  
   Глобальная оптимизация применяется только к откосам, у которых чекбокс включён. Общая стоимость профилей распределяется только между участниками экономии.

3. **Герметик/скотч**:  
   Остаются общими для всех активных откосов независимо от флага экономии.

4. **Смешанная экономия**:  
   Откос без экономии не платит за чужие профили; откос с экономией оптимизируется и делит профильную стоимость только с себе подобными.

### Пример

Два откоса одного размера, по 1 окну каждый (числа приблизительные, зависят от `OptimizeStrips`):

| Режим | StartProfile.Quantity (на откос / окно) | FProfile.Quantity (на откос / окно) | DistributedSharedSum |
|-------|--------------------------------|---------------------------|----------------------|
| Экономия OFF | `Sw` (per-window) | `Fw` (per-window) | только герметик+скотч |
| Экономия ON (оба) | `Sg` (глобальное суммарное) | `Fg` (глобальное суммарное) | герметик+скотч+профили |
| Смешанная (1-й ON, 2-й OFF) | `Sg` у 1-го, `Sw` у 2-го | `Fg` у 1-го, `Fw` у 2-го | профили только у 1-го |

**Легенда:** `Sw` / `Fw` — per-window количество Старт/F-профиля на одно окно; `Sg` / `Fg` — глобальное суммарное количество Старт/F-профиля после оптимизации по всем участникам экономии.

> **Примечание:** точные количества зависят от размеров и алгоритма `OptimizeStrips`. Главное — разница в логике распределения стоимости, а не конкретные числа.
>
> **Reference pricing:** при включённой экономии цены Старт/F-профиля для расчёта общей стоимости профилей берутся из первого участника экономии (`economySlopes.First()`). Если участники имеют разные ручные цены на профили, это может влиять на распределение.

### Тестовое покрытие

| Тест | Что проверяет |
|---|---|
| `ProfileEconomyDisabled_StartProfileIsPerWindow` | При выключенной экономии профили per-window |
| `ProfileEconomyDisabled_OrderItemTotal_NoDoubleCount` | Нет двойного учёта при выключенной экономии |
| `MixedEconomy_OnlyAppliesToOptedInSlopes` | Смешанная экономия: только участники платят за профили |

---

## Ламинат в откосах

> **Версия:** v3.44.0 (2026-07-11)  
> **Затронутые файлы:** `SlopeCalculation.cs`, `SlopeCalculatorService.cs`, `SlopePanelControl.xaml/.xaml.cs`, `OrderItem.Calculations.cs`, `OrderItem.cs`

### Описание

В панели откосов добавлена возможность учитывать **ламинатину** — дополнительный материал и работу за его установку.

- **Материал «Ламинат»**: цена по умолчанию **500 ₽/шт.**
- **Работа «Работа за ламинатину»**: цена по умолчанию **500 ₽/шт.**
- Количество задаётся пользователем вручную (через поле в таблице материалов) или по нажатию кнопки **«Добавить ламинатину»** в футере панели.
- Ламинат участвует в общей сумме откоса как материал, работа за него — как труд.
- В печатном КП не выводится отдельной строкой; сумма входит в `TotalMaterials` / `TotalLabor` откоса.

### Модель

В `SlopeCalculation` добавлены два дополнительных `SlopeMaterial`:

```csharp
public SlopeMaterial Laminatina { get; } = new();       // Ламинат (шт × ₽)
public SlopeMaterial LaminatinaLabor { get; } = new();    // Работа за ламинатину (шт × ₽)
```

Агрегаты обновлены:

```csharp
public double TotalMaterials =>
    Sandwich.Sum + Foam.Sum + Sealant.Sum + Tape.Sum +
    StartProfile.Sum + FProfile.Sum + Penoplex.Sum + Laminatina.Sum;

public double TotalLabor => Labor.Sum + LaminatinaLabor.Sum;
```

### Инициализация

В `SlopeCalculatorService._ApplyDefaults`:

```csharp
// Ламинат: по умолчанию 0 шт, цена 500 ₽.
if (!calc.Laminatina.IsQuantityOverridden)
{
    calc.Laminatina.Quantity = 0;
    calc.Laminatina.Price = laminatinaPrice; // 500
}
calc.Laminatina.Unit = "шт."; calc.Laminatina.Name = "Ламинат";

// Работа за ламинатину: по умолчанию 0 шт, цена 500 ₽.
if (!calc.LaminatinaLabor.IsQuantityOverridden)
{
    calc.LaminatinaLabor.Quantity = 0;
    calc.LaminatinaLabor.Price = laminatinaLaborPrice; // 500
}
calc.LaminatinaLabor.Unit = "шт."; calc.LaminatinaLabor.Name = "Работа за ламинатину";
```

### UI

- В таблице материалов откоса добавлена строка **«Ламинат»** с редактируемым количеством и ценой.
- Под строкой «Работа» добавлена строка **«Работа за ламинат»**.
- В футере панели добавлена кнопка **«Порог (Ламинат)»**, которая увеличивает количество ламината и работы за него на 1 и выставляет `IsQuantityOverridden = true`, чтобы авто-пересчёт не сбросил значение при изменении размеров.

### Влияние на итоги

В `OrderItem.Calculations.cs` ламинатина включена в per-window сумму материалов:

```csharp
double perWindowSum = SlopeData.Sandwich.Sum + SlopeData.Foam.Sum
                       + SlopeData.StartProfile.Sum + SlopeData.FProfile.Sum
                       + SlopeData.Penoplex.Sum + SlopeData.Laminatina.Sum;
```

### Тестовое покрытие

| Тест | Что проверяет |
|---|---|
| `Calculate_Laminatina_InitializedToZeroWithDefaultPrice` | Ламинат и работа инициализируются с Quantity=0, Price=500 |
| `Laminatina_Added_IncreasesTotalMaterialsAndTotalLabor` | Увеличение количества ламинатины растет в TotalMaterials и TotalLabor |
| `Laminatina_IsIncludedInTotalMaterials` | Ламинат входит в TotalMaterials |
| `UpdateInPlace_PreservesLaminatinaOverride` | Ручное количество сохраняется при UpdateInPlace |
| `LaminatinaLabor_DefaultPrice_500` | Цена работы за ламинатину по умолчанию 500 ₽ |

---

## Места, которые НЕЛЬЗЯ менять без явного разрешения владельца

1. Формулы `CalculatedValue` в `OrderItem.Calculations.cs`.
2. Формулы `ApplyCalcWidth/Height` и `ReverseCalcWidth/Height` в `AnwisSize.cs`.
3. Значения `InstallationDeduction` / `InstallationSurcharge` по умолчанию.
4. `DefaultPrices` в `PriceService.cs`.
5. Логика `TotalWithDeduction`.
6. Округление (`Math.Round(..., 2)` для денег, `Math.Round(..., 3)` для площади/периметра).

---

## Как вручную проверить, что расчёт работает правильно

1. Добавить Anwis 1000×1000 мм, цена 1800, кол-во 1.
   - Ожидается: CalculatedValue = 1.000 м², Total = 1800.00 руб.
2. Добавить Anwis 1000×2000 мм, режим ББ 60.
   - Хранимая ширина должна быть 1002, хранимая высота 1970.
   - CalculatedValue = 1002 * 1970 / 1_000_000 = 1.974 м².
3. Добавить ПСУЛ 1000×1000 мм.
   - CalculatedValue = (1000+1000)*2/1000 = 4.000 м.п.
4. Добавить Откос материал.
   - CalculatedValue = 1 шт., ширина и высота не влияют на стоимость.
5. Проверить КП — сумма в КП должна совпадать с итогом в программе.
6. Проверить "На завод" — размеры Anwis должны быть меньше на 20 мм.

## Source files

- `MosquitoNetCalculator/Models/OrderItem.Calculations.cs`
- `MosquitoNetCalculator/Models/AnwisSize.cs`
- `MosquitoNetCalculator/Models/OrderItem.Installation.cs`
- `MosquitoNetCalculator/ViewModels/CalculationViewModel.cs`
- `MosquitoNetCalculator/Services/PriceService.cs`
- `MosquitoNetCalculator/Services/PrintService.cs`
- `MosquitoNetCalculator/Services/FactoryTextService.cs`
- `MosquitoNetCalculator/Models/OrderItem.cs` (Width/Height setter'ы, ШиринаВвод/ВысотаВвод)

## Last verified

2026-07-12 (1038/1038 tests pass) — документ перепроверен в рамках завершения Фазы 3 рефакторинга; разделы «API-контракт `DistributedSharedSum`» и «Ламинат в откосах» актуальны.

2026-07-06 (878/878 tests pass) — добавлена секция «API-контракт `DistributedSharedSum` (общие материалы откоса)»: описание контракта `SlopeCalculatorService.RecalculateSealantAndTape`, защитная инициализация в `_ApplyDefaults`, формула распределения пропорционально `WindowCount`, последний откос забирает остаток, тестовое покрытие в `SlopeCalculatorServiceTests.cs`.

2026-07-03 (768/768 tests pass) — Auto-update → MainWindow.AddNewUpdate: новая архитектурная секция «Как данные Auto-update попадают на UI» в этом документе, `UpdateService.UpdateDetected` event, `MainWindow.OnUpdateDetected` (sync `Dispatcher.Invoke`), `MainWindowViewModel.AddNewUpdate` контракт locked тестом `AddNewUpdate_ZeroIsLatestFrame_neverObserved` (≥1 карточка с `IsLatest=true` во всех наблюдаемых моментах).
2026-07-03 (759/759 tests pass) — рефакторинг блока «Обновления»: бейдж «Новейшая» property-based (`UpdateItem.IsLatest`, `[JsonIgnore]`); `UpdateLog.AllNewestFirst()` сбрасывает `IsLatest` для всех и ставит ровно одной; `ValidateLogInvariant()` для дублей версий; **append-only архитектурный инвариант** для `Resources/update-log.json` (дописывается строго в конец, старые записи байт-в-байт неизменны).
2026-07-03 (добавлен товар «Дверная сетка» — 3000 ₽/м², Антикошка, монтаж 600 ₽/шт., 748/748 tests pass)
