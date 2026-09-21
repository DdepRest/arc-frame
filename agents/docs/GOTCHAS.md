# GOTCHAS.md

## Опасные места

### 1. Утечка формул Anwis на не-Anwis товары (КРИТИЧНО)

**Где:** `OrderItem.cs` — свойства `ШиринаВвод`, `ВысотаВвод`, `Размеры`, `AnwisSizeMode`.

**Что может случиться:** Если забыть проверку `IsAnwis` в setter'ах, формулы коррекции Anwis применятся к Отливу, Козырьку, Откосу материалу и т.д.

**Пример бага (исправлен в v3.35.0):**
- Откос материал показывал высоту 30 мм (ReverseCalcHeight ББ60 утекала через `Размеры`).
- Редактирование ширины Откоса добавляло +2 мм (ШиринаВвод setter не проверял IsAnwis).

**Правило:** Всегда проверять `IsAnwis` перед применением Anwis-формул:
```csharp
_width = IsAnwis
    ? AnwisSize.ОтВвода(raw, ...).ШиринаРасчёт
    : raw;  // identity для не-Anwis
```

---

### 2. AnwisSizeMode setter при загрузке/клонировании (КРИТИЧНО)

**Где:** `OrderItem.cs` — свойство `AnwisSizeMode` и метод `SetAnwisModeQuiet`.

**Что может случиться:** При загрузке заказа или клонировании публичный setter `AnwisSizeMode` пытается сделать "reverse → apply" через старый режим (по умолчанию ББ 60), что портит уже правильные хранимые размеры.

**Решение:** Всегда использовать `SetAnwisModeQuiet()` при загрузке/клонировании/инициализации.

---

### 3. Сохранение CalculatedValue и Total в JSON (КРИТИЧНО)

**Где:** `OrderItem.Dto.cs`, `OrderStorageService.cs`.

**Что было:** Раньше `CalculatedValue` и `Total` сохранялись в JSON. Это приводило к рассинхронизации при изменении цен или формул.

**Решение (v3.22.0):** Эти поля убраны из DTO и всегда пересчитываются при загрузке.

**Правило:** Никогда не сохранять derived-поля (CalculatedValue, Total) в JSON.

---

### 4. Цены и миграции (КРИТИЧНО)

**Где:** `PriceService.cs` — `ApplyMigrations`.

**Что может случиться:**
- `RemoveAll` вместо `prices = prices.Where(...).ToList()` — иначе локальная переменная перепривязывается и изменения теряются.
- Добавление новых товаров в `DefaultPrices` без `ApplyMigrations` — старые пользователи не увидят новый товар.

**Правило:** При добавлении нового товара добавить его и в `DefaultPrices`, и в `ApplyMigrations` (Migration 4).

---

### 5. Автообновление: имена файлов, версии и UX-flow (КРИТИЧНО)

**Где:** `UpdateService.cs`, `WatchdogService.cs`, `releases.json`, `DialogService.cs`.

**Что может сломаться:**
- Имя ZIP в `releases.json` не совпадает с фактическим именем файла в GitHub Release.
- Версия в `.csproj` не совпадает с версией в `releases.json`.
- Изменение имени `ExeFileName` в `WatchdogService` без обновления `build.bat`.
- `GetAvailableUpdate` возвращает `Releases[0]` без проверки, что он соответствует `manifest.Latest` — предполагается newest-first ordering (см. `UpdateServiceTests.GetAvailableUpdate_LatestGreaterThanCurrent_ReturnsRelease`).

**Правило:**
- Версия = единственный источник правды в `.csproj` (`<Version>X.Y.Z</Version>`).
- `releases.json` должен быть синхронизирован вручную (или через скрипт).
- Имя asset'а: `ARC-Frame-X.Y.Z-full.zip`.
- `update-log.json` должен содержать записи для всех версий, иначе changelog в диалоге будет пуст.

**Изменения update notification rework:**
- `CheckOnStartupAsync` теперь показывает **диалог** автоматически (вместо toast).
- `RunUpdateFlowAsync` — общий метод с флагом `isAutomatic` для различения авто/ручной проверки.
- Ошибки при `isAutomatic=true` показываются через `ToastService.Error`, не `MessageBox` (не блокируют UI).
- TitleBar-полоска прогресса заменила `DownloadProgressPanel` в ActionBar.
- **HttpClient DI (unreleased):** `FetchManifestAsync` и `DownloadWithProgressAsync` принимают `HttpClient? httpClient = null`. Паттерн `ownsClient` (`var ownsClient = httpClient == null;`) предотвращает `Dispose` инжектированного клиента. Production-настройки (timeout, User-Agent) применяются только к самоуправляемому клиенту.
- **Zero-byte download fix (unreleased):** если `Content-Length` отсутствует или равен 0, прогресс теперь корректно отчитывается как 100% по завершении. Ранее полоска зависала на 0%, потому что `while ((read = await stream.ReadAsync(buffer)) > 0)` не выполнялся ни разу, а финальный `Report(100)` не вызывался.

---

### 6. Печать КП: HTML-инъекция

**Где:** `PrintService.cs` — `FillTemplate`.

**Что может случиться:** Если клиент введёт `<`, `>`, `&` в имя/адрес/примечания, HTML сломается.

**Решение:** Все строковые поля проходят через `EscapeHtml` перед вставкой в шаблон.

**Правило:** При добавлении нового поля в КП обязательно обернуть в `EscapeHtml()`.

---

### 7. Тема и замороженные кисти

**Где:** `ThemeService.cs`.

**Что было:** Frozen brush внутри Style/ControlTemplate нельзя анимировать — краш при переключении темы.

**Решение:** Кисти пересоздаются, а не модифицируются. Frozen кисти заменяются на новые.

---

### 8. Single-file publish и версия

**Где:** `UpdateService.cs` — `TryResolveCurrentVersion`.

**Что было:** `Assembly.GetName().Version` возвращает `null` в single-file publish.

**Решение (v3.34.5):** Многослойный fallback:
1. `AssemblyInformationalVersionAttribute` (лучший источник).
2. `AssemblyFileVersionAttribute` (fallback).
3. `Assembly.GetName().Version` (legacy fallback).

---

### 9. Пути к данным (%AppData% vs BaseDirectory)

**Где:** `AppSettingsService.cs`, `PriceService.cs`, `OrderStorageService.cs`.

**Что было:** Данные хранились рядом с .exe и терялись при обновлении.

**Решение (v3.28+):** Все данные в `%AppData%\MosquitoNetCalculator\`.

**Правило:** Никогда не хранить пользовательские данные в `AppDomain.CurrentDomain.BaseDirectory`.

---

### 10. Undo/Redo и события

**Где:** `UndoRedoService.cs`, `CalculationViewModel.cs`.

**Что может случиться:** При Undo/Redo старые OrderItem остаются подписанными на события и вызывают лишние пересчёты.

**Решение:** `UnsubscribeAll` перед `RestoreFromSnapshot` и `LoadFromOrderData`.

---

### 11. InstallationSurcharge: расхождение комментария и кода

**Где:** `OrderItem.Installation.cs` — свойство `InstallationSurcharge`.

**Что может случиться:** XML-комментарий говорит "Default 0 ₽", но фактическое значение в коде `_installationSurcharge = 500`. AI или программист может прочитать комментарий и принять неверное решение.

**Фактическое поведение:**
- `_installationDeduction = 500` (режим 1 — без монтажа).
- `_installationSurcharge = 500` (режим 2 — в конструкцию).
- Тесты подтверждают 500 для обоих режимов.

**Статус:** Комментарий исправлен — теперь соответствует коду (`Default 500 ₽`). Эта грабля оставлена в документации как пример: XML-комментарии могут устареть и ввести AI в заблуждение. Код и тесты — источник правды.

---

### 12. Монтаж с Quantity > 1: deduction умножается на Q (per piece, не flat fee) (КРИТИЧНО)

**Где:** `OrderItem.Installation.cs` — свойство `TotalWithDeduction`.

**Что было:** При выборе «Без монтажа» или «В конструкцию» в строке с `Quantity > 1`
программа вычитала установленный вычет **только один раз** (`Total − 500`), как будто
пользователь отказался от одного монтажа. На самом деле он отказался от N монтажей —
по одному на каждую штуку. Это занижало скидку для bulk-строк.

**Пример бага (исправлен):**
- Anwis ББ 60 Профипласт 1000×1000 мм × 3 шт. × 1800 руб/м², режим «В конструкцию» (вычет 500).
- Pre-fix: `Total − 500 = 5400 − 500 = 4900` ₽ (вычли один раз).
- Post-fix: `Total − 500×3 = 5400 − 1500 = 3900` ₽ (вычли за каждую штуку).

**Решение:**
```csharp
public double TotalWithDeduction
{
    get
    {
        if (!IsInstallationApplicable) return Total;
        return _installationMode switch
        {
            1 => Math.Round(Math.Max(0, Total - InstallationDeduction * Quantity), 2),
            2 => Math.Round(Math.Max(0, Total - InstallationSurcharge * Quantity), 2),
            _ => Total
        };
    }
}
```

**Правило:** В режимах 1/2 deduction указывается в UI как **per-piece** (`руб./шт.`) и
применяется один раз на каждую единицу товара. `TotalWithDeduction = Total − PerPieceFee × Quantity`.
Backward-compat для Q=1: результат идентичен pre-fix формуле.
JSON-схема не меняется — `InstallationDeduction`/`InstallationSurcharge` остаются
per-unit полями в DTO, старые сохранённые заказы продолжают работать.

**UI-сигнал:** `InstallationToolTip` для режимов 1/2 явно показывает `руб./шт. × Кол-во`,
чтобы пользователь видел, что введённая сумма применяется за каждую штуку, а не один раз.

**Тесты:** `OrderItemTests.TotalWithDeduction_Mode{1,2}_Quantity3_SubtractsPerPiece*`,
`TotalWithDeduction_Mode2_QuantityScaling_IsLinearWithQ` (теория Q∈{1,2,5}).

**Кейс:** `CALCULATION_TEST_CASES.md#Case 16` (16/16b/16c).

---

### 13. Автоширина колонки «Цена» в DataGrid: «LostFocus» обрезает видимый текст при наборе (СРЕДНИЙ/ВЫСОКИЙ)

**Где:** `MosquitoNetCalculator/Controls/OrderItemsControl.xaml` (line 243, Цена column),
`MosquitoNetCalculator/Controls/PricesControl.xaml` (line 32, «Цена, руб.» column).

**Что было:** Колонка с `Width="Auto"` и `UpdateSourceTrigger="LostFocus"` рекомпьютит ширину
по старому отформатированному `Price` (напр. «5 000,00»), потому что `Price` обновляется только
при потере фокуса. Пользователь печатает 15000 — видит только 5000, потому что редактирующий
TextBox обрезан до ширины старого display-значения. Проблема не воспроизводилась на колонках
«Ширина», «Высота», «Кол-во» — там используется `UpdateSourceTrigger="PropertyChanged"`, и
`Width="Auto"` успевает подстроиться под набор.

**Пример бага:**
- «Расчёт» — добавить товар, кликнуть в ячейку «Цена», набрать 15000 — ячейка показывает только
  5000 при наборе (после потери фокуса значение корректно становится 15000, но пользователь не
  может видеть, что набирает).
- «Цены» — то же самое в колонке «Цена, руб.».

**Решение:** `UpdateSourceTrigger` переключён с `LostFocus` на `PropertyChanged` в обеих
колонках. После нажатия клавиши `Price` обновляется мгновенно → `Width="Auto"` пересчитывает
ширину под новый размер текста → редактирующий TextBox больше не обрезается.

**Известный трейд-офф (НЕ подтвердился эмпирически):** исходная формулировка грабли
утверждала, что при наборе «15000,» `MoneyFormatService.TryParse` возвращает `false`, `Price`
временно становится 0 → flash «0,00». Эмпирическая проверка на .NET проекта показала обратное:
`double.TryParse("15000,", NumberStyles.Any, RuNumberFormat)` возвращает `(true, 15000.0)`.
Современный .NET принимает trailing-запятую как нулевую дробную часть для целых. Реальный UX при
наборе «15000» в ячейке «Цена»: `1 → 15 → 150 → 1500 → 15000 → 15000` — без вспышки «0,00».

**Актуальный контракт trailing-запятой ЗАФИКСИРОВАН** в `MoneyFormatServiceTests`:
- `TryParse_TrailingComma_DocumentsActualContract_FromGOTCHAS13` — последовательность 15000 → 15000, → 15000,5 → 15000,50 → 15000.5.
- `TryParse_TrailingComma_OnWholeNumber_AbsorbsAsInteger` — 3 успешных кейса (15000,, 1,, 15000, ).
- `TryParse_MalformedCommaVariants_ReturnFalse_AndZero` — 5 инвалидных кейсов (15000,, , 15000.5,, 15000,5,, 15000,.5).

Если будущий рефакторинг `MoneyFormatService.TryParse` снимет trailing-junk tolerance
ИЛИ изменит `NumberStyles` / `RuNumberFormat`, эти тесты сломаются и потребуют явного обновления,
тем самым сохраняя видимость актуального контракта в коде.

**Правило:** для редактируемых колонок DataGrid, использующих `Width="Auto"`, ВСЕГДА
применять `UpdateSourceTrigger="PropertyChanged"`. `LostFocus` корректно работает только для
read-only display.

**Locale-инвариант:** `MoneyFormatService` жёстко использует `ru-RU` (decimal sep = ",",
group sep = " ", decimal digits = 2). Если кто-то сменит локаль на en-US (decimal sep = "."),
trade-off trailing-запятой исчезнет (потому что "15000," в en-US разбирается как 15000), но
НЕ обновит этот gotcha → рассогласование с фактическим поведением.

**Тесты:** `MosquitoNetCalculator.Tests.DataGridBindingsTests`:
- `Расчёт_Цена_UsesPropertyChanged_So_AutoWidthTracksTyping` — прямая регрессия бага.
- `Цены_Цена_UsesPropertyChanged_So_AutoWidthTracksTyping` — то же для tab «Цены».
- `Расчёт_Ширина_/Высота_/Колво_StillUsesPropertyChanged` — guardrails на соседние колонки
  (чтобы фикс не сломал паттерн).
- + `MoneyFormatServiceTests.TryParse_TrailingComma_*` — фиксация trailing-comma контракта.

---

### 14. DataGridTextColumn SelectAll race: «Dispatcher.BeginInvoke» проигрывает первому нажатию клавиши (СРЕДНИЙ)

**Где:** `MosquitoNetCalculator/Controls/OrderItemsControl.xaml.cs` — метод `SelectAll_OnFocus`;
XAML — все редактируемые колонки `Ширина`, `Высота`, `Кол-во`, `Цена` в `OrderItemsControl.xaml`.

**Симптом бага:**
1. Пользователь добавил Anvis ББ 60 с малым raw (например H=100 мм) → после формулы `stored = max(0, 100−30) = 70 мм`, или `30` при H=60.
2. Пользователь кликает в ячейку «Ширина» / «Высота», видит курсор в позиции клика (WPF по умолчанию
   не делает select-all на вход в edit-режим).
3. Набирает `1200`.
4. Получает `701200`, `301200`, `31200` и т.д. — **текст дописан, не заменён**.

**Корень:** `SelectAll_OnFocus` был реализован с отложенным `Dispatcher.BeginInvoke`:
```csharp
if (sender is TextBox tb)
    tb.Dispatcher.BeginInvoke(() => tb.SelectAll());   // ← deferred
```
`BeginInvoke` помещает SelectAll в очередь dispatcher'а после обработки текущего сообщения.
Между моментом, когда клик установил позицию курсора, и моментом, когда `SelectAll` запустился,
пользователь успевает нажать клавишу → символ вставляется в позицию курсора, не заменяя
выделение (потому что выделения ещё нет).

**Решение:** убрать `BeginInvoke` — делать SelectAll синхронно в GotFocus:
```csharp
if (sender is TextBox tb)
    tb.SelectAll();   // ← synchronous, runs in same dispatch frame as GotFocus
```
GotFocus прибывает после `PreviewMouseLeftButtonDown` (который уже позиционировал курсор)
в том же диспетчерском цикле, поэтому синхронный `SelectAll` переопределяет положение курсора
и выделяет весь текст. Любое последующее нажатие → символ заменяет выделение → результат: `1200` ✓.

**Тонкости:**
- Touch (тап вместо клика): ведёт себя идентично — `PreviewMouseLeftButtonDown` + `GotFocus` идут
  той же дорогой, `SelectAll` срабатывает до первого ввода.
- Клавиатурная навигация (Tab/F2 → ячейка): TextBox получает фокус без клика → GotFocus arrives
  до любого ввода → SelectAll выделяет всё → первый keystroke заменяет.

**Не нужно:** изощрённый шаблон с `PreviewMouseLeftButtonDown` + `e.Handled = true; Focus(); SelectAll()`
— для задачи «click into cell → typing replaces» это overkill. Синхронный SelectAll в GotFocus
достаточен для всех путей ввода в проекте.

**Регрессионный тест:** см. `MosquitoNetCalculator.Tests.DataGridBindingsTests`
(паттерн «SelectAll_OnFocus_Синхронный» — следит, что обработчик не вернёт `BeginInvoke`
в будущих рефакторингах).

**Правило для будущих колонок:** если редактируемая ячейка показывает derived/stored значение,
которое пользователь хочет полностью заменить, **ВСЕГДА** использовать синхронный SelectAll
в `GotFocus`. Никогда не оборачивать в `Dispatcher.BeginInvoke` / `Dispatcher.InvokeAsync`.

---

---

### 15. PropertyChanged на Ширине/Высоте: формула ББ60 перехватывает значение на каждом нажатии (СРЕДНИЙ)

**Где:** `MosquitoNetCalculator/Controls/OrderItemsControl.xaml` — колонки Ширина и Высота.

**Симптом:**
1. У Anwis в режиме ББ60 высота = 30 мм (формула `max(0, raw−30)` при raw=60).
2. Пользователь кликает в ячейку «Высота» — видит «30».
3. Набирает «1» → `PropertyChanged` срабатывает → `ВысотаВвод` setter: `ОтВвода(W, 1, ББ60)` → `ApplyCalcHeight(1, ББ60) = max(0, 1−30) = 0` → stored=0 → reverse=30. Ячейка обновляется до «30».
4. Набирает «2» → дописывается к «30» → «302». И так далее.
5. Итог: вместо «1200» получается «301200».

**Корень:** `UpdateSourceTrigger=PropertyChanged` заставляет формулу срабатывать на КАЖДОМ нажатии клавиши. Формула `max(0, raw−30)` создаёт разрыв на 30: любое raw<30 → stored=0 → reverse=30 → дисплей возвращается к 30. Следующий символ дописывается к тому, что показывает ячейка.

**Решение:** переключить Ширину и Высоту на `UpdateSourceTrigger=LostFocus`. Формула применяется один раз, когда пользователь покидает ячейку (клик в другую ячейку, Enter, Tab). Во время набора — никакого вмешательства формулы.

**Трейдофф:** `Width="Auto"` больше не отслеживает набор посимвольно для этих двух колонок (но для 3-4 значных размеров это некритично). Цена и Кол-во сохраняют `PropertyChanged` — для них формула не создаёт разрыва.

**Тесты:** `DataGridBindingsTests.Расчёт_Ширина_UsesLostFocus_ToPreventMidTypingClamp` + `Расчёт_Высота_UsesLostFocus_ToPreventMidTypingClamp`.

**Правило:** `PropertyChanged` допустим только для колонок, где setter НЕ создаёт разрыва в отображении (нет `max(0, x−N)` или аналогичной логики). Если setter может изменить отображаемое значение непредсказуемо для пользователя — используй `LostFocus`.

---

### 16. Per-linear-meter products: legacy JSON contains DTO defaults that break v3.47.0 formula (ВЫСОКИЙ)

**Где:** `CalculationViewModel.LoadFromOrderData` (~line 290), apply
после конструктора OrderItem. Связано с: `OrderItem.Installation.cs::TotalWithDeduction`,
`OrderItem.Dto.cs::InstallationDeduction / InstallationSurcharge / InstallationAdjustment`.

**Что может случиться:** При добавлении нового товара в
`InstallationApplicableProducts` (в т.ч. making it per-linear-meter) старые
заказы, сохранённые до этого момента, содержат только DTO defaults
(`mode=0, ded=-500, sur=-500, adj=0`) потому что для них товар был
**не** installation-aware. v3.47.0+ формула итого для per-linear-meter:
`Total + value × InstallationLinearMeters × Quantity` (₽/м.п.; с v3.48.2 метры = ДЛИНА изделия — большая сторона, не периметр)

С legacy defaults:
- `Max(0, 1075 + (-500)×3×1) = Max(0, −425) = 0` — режим X или B показывает 0.

**Пример бага (исправлен в v3.47.3):** Отлив 1000×500 в заказе,
сохранённом до v3.47.0, после upgrade → открыть заказ → выбрать X или B →
сумма показывает «0 ₽» вместо реальной.

**Решение:** В `LoadFromOrderData` — для каждого per-linear-meter товара
(`Отлив`, `Козырёк`) — расширить блок миграции строже:

```csharp
bool isLegacyLoad =
    item.InstallationMode == 0 &&
    Math.Abs(item.InstallationAdjustment) < 0.01 &&
    Math.Abs(item.InstallationDeduction + 500) < 0.01 &&
    Math.Abs(item.InstallationSurcharge + 500) < 0.01;
if (isLegacyLoad)
{
    item.InstallationMode = 1;
    item.InstallationDeduction = OrderItem.GetDefaultInstallationDeduction(item.Name);
    item.InstallationSurcharge = OrderItem.GetDefaultInstallationSurcharge(item.Name);
    item.InstallationAdjustment = OrderItem.GetDefaultInstallationAdjustment(item.Name);
}
```

Дополнительно — v3.46.1 sign-flip migration (в том же `LoadFromOrderData`,
~line 268) исключает per-linear-meter продукты:
```csharp
InstallationSurcharge = (od.InstallationSurcharge > 0 && od.InstallationMode != 0)
    && od.Name != "Отлив" && od.Name != "Козырёк"   // v3.47.0+: positive convention, no flip
    ? -od.InstallationSurcharge
    : od.InstallationSurcharge,
```

**Правило (важно при следующем расширении):** При добавлении нового good
на per-linear-meter ставку в v3.x+ **обязательно**:
1. Добавить его **в одном месте** — `ProductCatalog.PerLinearMeterProducts` (HashSet) +
   `ProductCatalog.InstallationApplicableProducts`. Это single source of truth — все остальные
   проверки (sign-flip exclusion, isLegacyLoad, IsInstallationDefaultNoInstallation,
   OrderItem.IsInstallationPerLinearMeter) автоматически подхватывают новое имя.
2. Определить per-linear-meter defaults в
   `OrderItem.GetDefaultInstallation{Deduction,Surcharge,Adjustment}`
   (плюс добавить записи в `DefaultInstallation{Deductions,Adjustments,Surcharges}`
   Dictionary'и для нового имени, иначе fallback).
3. Добавить регрессионные тесты на legacy JSON + v3.x+ JSON round-trip
   (Theory с параметрами ±500 для обеих convention, sign-flip exclusion case,
   ProductCatalog.IsPerLinearMeter consistency test).
4. Запустить `agents/scripts/what-to-update.ps1` и обновить `SYMBOL_INDEX.md` / `MODULES.md` /
   `DECISIONS.md` если product name появился в новых местах.

Без шагов 1–3 эта грабля повторится при следующем per-linear-meter product.

**Тесты:** `CalculationViewModelTests.LoadFromOrderData_Otliv_LegacyDefaults_*`,
`LoadFromOrderData_Kozyrek_LegacyDefaults_*`,
`LoadFromOrderData_LegacyOtlivKozyrek_TotalWithDeduction_NonZero_InAllThreeModes`,
`LoadFromOrderData_Otliv_NewV347Order_NotAffectedByMigration`,
`LoadFromOrderData_Otliv_CustomDeduction_NotOverridden`.

---

### 17. Поле «Сумма» в переключателе монтажа: знак нельзя терять на нулевом значении (СРЕДНИЙ)

**Где:** `MosquitoNetCalculator/MainWindow.Items.cs` — контекстное меню монтажа, обработчики `chkSign` и `CommitDeductionIfPending`.

**Симптом:** при нулевой сумме пользователь нажимает «−», но обновление поля тут же возвращает переключатель в «+», потому что математический ноль не имеет знака. Кроме того, явно введённое `-500` раньше игнорировалось проверкой `absVal >= 0`.

**Контракт UI:**
- «+» означает положительную корректировку (добавить к итогу);
- «−» означает отрицательную корректировку (вычесть из итога);
- при сумме `0` выбранный знак сохраняется, чтобы пользователь мог выбрать «−» до ввода числа;
- явно введённый минус имеет приоритет над состоянием переключателя;
- нечисловые и бесконечные значения не записываются в модель.

Модель и формула `TotalWithDeduction` не менялись: исправлено только преобразование ввода popup в signed amount. Регрессии покрыты `OrderItemTests.NormalizeInstallationAmount_PreservesSignedInput`, `NormalizeInstallationAmount_RejectsNonFiniteValues` и `ShouldRefreshInstallationSign_OnlyForMeaningfulAmount`.

---

### 18. Импост: производный флаг, сумма от ширины, дверная сетка исключена (СРЕДНИЙ)

**Где:** `MosquitoNetCalculator/Models/OrderItem.cs` (`HasImpost`, `ImpostLinearMeters`), `OrderItem.Calculations.cs` (`Recalculate`), `ProductCatalog.cs` (`ImpostApplicableProducts`).

**Симптом:** импост — **авто-надбавка** для оконных сеток (Anwis, На навесах, Оконная на метал. крепл.) при ширине >= 500 мм или высоте >= 1500 мм (по **РАСЧЁТНЫМ** размерам — тем же, что идут в площадь/цену/КП; владелец, 2026-08-24: «программа должна считать расчётный размер»).

**Критические правила:**
- `HasImpost` — **производное** свойство из `Name + Width + Height` (расчётные: для Anwis — после коррекции режима ББ60/ББ70/Проём; для остальных = введённым), **НЕ сериализуется** → убрать импост нельзя; при загрузке заказа пересчитывается сам.
- **Сумма импоста = `200 ₽/м.п. × (расчётная ширина/1000) × Кол-во`** — **всегда от ширины**, независимо от того, какой критерий сработал. Пример: Anwis ББ60, ввод 500×1500 (расчёт 502×1470) → надбавка 100,40 ₽; ввод 430×1510 (расчёт 432×1480) → импоста НЕТ (высота расчёта 1480 < 1500).
- **Смена режима сетки (AnwisSizeMode) обязана пересчитывать импост**: расчётная ширина меняется с режимом (ввод 600: ББ60 → 602, ББ70 → 598), сеттер `AnwisSizeMode` → `Recalculate()`. Регрессия: `OrderItemTests.AnwisSizeMode_Change_RecalculatesImpostSurcharge`.
- **Дверная сетка полностью исключена** из системы импоста (`ImpostApplicableProducts` не содержит её).
- Импост добавляется к `Total` в `Recalculate()`, поэтому затрагивает `TotalWithDeduction`, КП, итоги и текст «На завод».
- Критерий и сумма — единые статические хелперы `OrderItem.ImpostApplies` / `ImpostSurchargeFor`: их использует И строка, И превью быстрого добавления (`QuickAddControl.UpdateQuickPreview` считает расчётные размеры через `AnwisSize.ОтВвода(...)` с выбранным режимом и печатает «… + Импост N ₽ = …»). Превью и добавленная строка всегда дают одну сумму; не дублируй формулу.
- В `DisplayName` добавляется суффикс `(Импост)` → секции «На завод» разбиваются по квалификации (ожидаемо).

**Правило на будущее:** при добавлении нового товара-сетки решить, должен ли он участвовать в импосте, и добавить/не добавлять его в `ProductCatalog.ImpostApplicableProducts`. Регрессии покрыты `OrderItemTests.Impost*` / `HasImpost_*` / `Total_*Impost*`.

### 19. DynamicResource в Binding.Converter — краш только в рантайме (СРЕДНИЙ/ВЫСОКИЙ)

**Где:** любой XAML-шаблон с `{Binding Converter=...}`. Выявлено на заголовках колонок DataGrid
(`Themes/DataGridStyles.xaml`); рабочий фикс — `Converters/UppercaseHeaderTemplate.cs`
+ вызовы `UppercaseHeaderTemplate.Apply(grid)` в конструкторах
`OrderItemsControl` / `OrdersHistoryControl` / `PricesControl`.

**Что может случиться:** `{Binding Converter={DynamicResource SomeConverter}}` компилируется
без ошибок, но падает при построении шаблона ячейки/элемента:
`DynamicResourceExtension невозможно задать в свойстве Converter типа Binding`
(NotSupportedException из MarkupExtension). `Converter` — обычное CLR-свойство `Binding`,
НЕ DependencyProperty, поэтому DynamicResource там недопустим.

**Коварство:** компилятор XAML пропускает это — ошибка видна только в рантайме, обычно на
первом рендере грида, с непонятным стеком в `MeasureOverride`/`ApplyTemplate`. Тесты,
парсящие сырой XAML, тоже не ловят: пока шаблон реально не применён к элементу дерева,
ничего не падает.

**Решение (любой из вариантов):**
- `StaticResource` — если конвертер гарантированно загружен до словаря с шаблоном
  (см. порядок MergedDictionaries в App.xaml);
- назначение шаблона/конвертера из code-behind (`FrameworkElementFactory` +
  `new Binding { Converter = new ... }` — так сделан `UppercaseHeaderTemplate`);
- конвертер как ресурс в App.xaml + XAML-ссылка там, где возможен статический поиск.

**Правило:** В XAML `{Binding Converter={DynamicResource ...}}` запрещён. Если конвертер
должен приходить из темы — строй шаблон в коде или используй StaticResource с проверкой
порядка словарей.

**Тесты:** `MosquitoNetCalculator.Tests.Converters.UppercaseHeaderTemplateTests`
(STA-паттерн `RunOnStaThread`): фиксирует, что все колонки получают общий шаблон,
а строки `Header` остаются точными («Ширина»/«Высота» — их сравнивает `BeginningEdit`,
блокировка ячеек).

---

### 20. Вшитый шрифт: абсолютный pack-URI со «#» молча даёт системный фолбэк (СРЕДНИЙ)

**Где:** `Themes/Tokens.Typography.xaml` (токен `Font.Text`), `Services/AppFontService.cs`.
Выявлено при переходе на вшитый Inter (v3.53.0).

**Что может случиться:** шрифт лежит в сборке как `<Resource Include="Resources\Fonts\*.ttf" />`,
ресурс реально доступен (`Application.GetResourceStream` возвращает поток), но токен
`FontFamily` в «естественной» форме
`pack://application:,,,/MosquitoNetCalculator;component/Resources/Fonts/#Inter`
НЕ грузится: `Typeface.TryGetGlyphTypeface` отдаёт `Segoe UI`. Ошибки нет — просто
фолбэк на системный шрифт, то есть приложение выглядит почти так же, и подмена
остаётся незамеченной.

**Причина:** в абсолютном pack-URI символ `#` разбирается как URI-фрагмент, а не как
разделитель «папка → имя семейства» (то же значение играет роль fragment'а пакового URI).

**Замер (тест `TypographyTests`, воспроизводимо):**
```
  только pack-URI            → [не загрузился]
  pack-URI + фолбэки        → [Segoe UI]        // тихая подмена
  ctor(base, "./#Inter")   → [Inter]           // рабочая форма
```

**Решение (v3.53.0):** токен `Font.Text` в XAML объявляет только фолбэк
(`Inter, Segoe UI, Tahoma`), а вшитый Inter ставится кодом в `App.OnStartup` и в
тестовом `TestAppThemes`:
```csharp
new FontFamily(new Uri("pack://application:,,,/MosquitoNetCalculator;component/Resources/Fonts/"),
               "./#Inter");
```

**Правило:** значение подменяется в рантайме — значит, `Font.*` потребляется ТОЛЬКО
через `DynamicResource`; `StaticResource` зафиксирует фолбэк при разборе словаря.
Сторожит `DesignTokenGuardTests.FontTokens_AreConsumedViaDynamicResource`, а сам факт
загрузки Inter (а не фолбэка) — `TypographyTests.InterFont_IsActuallyBundled_NotSilentFallback`.

### 21. Одно `Application` на процесс + параллельные коллекции xUnit = «ресурс не найден» (СРЕДНИЙ)

**Где:** `MosquitoNetCalculator.Tests/Helpers/TestAppThemes.cs` (bootstrap тем для тестов).
Выявлено при добавлении новых тест-классов в v3.53.0.

**Что случилось:** прогнали 2345 тестов — 2345 pass. Добавили новый STA-класс
(`Design/MotionTests`), который тоже поднимает приложение с темами, — и полный прогон
стал падать несвязанным тестом: `ResourceReferenceKeyNotFoundException : Ресурс
"ToastBorder" не найден` в `ToastServiceTests` (тот поднимает только STA-поток,
без тем, и полагается на уже существующее приложение).

**Причина:** `Application` в WPF — один на процесс, а xUnit параллелит КЛАССЫ
разных коллекций. Два потока одновременно видели «приложения нет» — и один из них
успевал создать ПУСТОЕ (без словарей тем) приложение. Дальше первый же тест,
искавший ресурс темы, получал исключение; от порядка запуска зависело, падает ли
вся группа. Тот же класс проблемы, что «отравленные» статики WPF (GOTCHAS
о `AppLifecycleTests`), но причина — гонка, а не расхождение слотов.

**Решение (v3.53.0):** (1) весь `TestAppThemes.Ensure()` под процесс-глобальным
`lock` — второй вызов видит готовое приложение и переиспользует его; (2) всякий
новый тест-класс, который поднимает приложение или ищет ресурсы темы, обязан быть
в коллекции `[Collection("WPF_UI")]` (она `DisableParallelization`).

**Правило:** если тест читает `Application.Current` — он либо в `WPF_UI`, либо не
имеет права полагаться на чужой STA-поток.

---

### 22. Неявный стиль `Window` не достаёт до окон-наследников (ВЫСОКИЙ)

**Где:** `Themes/MiscStyles.xaml` (стиль `Window.Shared`) и 13 корневых `<Window>` в проекте.

**Что случилось:** стиль окна был неявным (`<Style TargetType="Window">` без `x:Key`) —
«чтобы один раз задать шрифт, режим рендера текста и округление для всех окон». Тест
проверял, что стиль ЛЕЖИТ В СЛОВАРЕ, и был зелёным. Замер на реальных окнах дал другое:
`AdminPasswordWindow`, `AiAssistantWindow`, `SlopeEconomyDetailsWindow` — `Style=null`,
`Segoe UI` 12px, `TextFormattingMode=Ideal`, `UseLayoutRounding=false`,
`SnapsToDevicePixels=false`. Обещание «12 вторичных окон рисуют текст одинаково» не
выполнялось ни для одного окна.

**Причина:** неявный стиль WPF сопоставляет по ТОЧНОМУ типу, а все окна приложения —
наследники (`MainWindow : Window`, диалоги тоже); стиль был зарегистрирован под
`typeof(Window)`. Проверка на голом `new Window()` даёт ложную уверенность — именно он
стиль получает. Второе следствие: шрифт текста наследуется ОТ ОКНА, поэтому без стиля
вшитый `Inter` до большинства `TextBlock` не доходил вообще (в разметке `Font.Text` не
упоминался ни разу, работали только `FieldLabel`/`SectionLabel`, у которых `FontFamily`
есть в самом стиле) — на одном экране соседствовали `Inter` 11 и `Segoe UI` 12–13.

**Решение (v3.53.0):** стиль именован (`x:Key="Window.Shared"`) и объявлен ЯВНО в
каждом окне (`Style="{DynamicResource Window.Shared}"`), неявный дубль удалён;
14 жёстких семей в разметке (`Segoe UI`, `Consolas`) переведены на токены
`Font.Text`/`Font.Mono`. Тест переписан на два уровня: скан разметки (все 13 корней
обязаны объявить стиль) + рантайм-проверка настоящих окон (`Inter`, размер из
`Type.Body`, `Display`, round/снаппинг).

**Правило:** для базового типа с производными элементами проверять ПРИМЕНЕНИЕ на
реальном объекте, а не наличие ресурса в словаре. Наличие стиля в словаре и его
действие — разные утверждения.

---

### 23. Анимация нулевой длины всё равно завершается — на этом стоит гейт reduced-motion (СРЕДНИЙ)

**Где:** `Helpers/Motion.cs`, системная настройка «отключить анимации»
(`SystemParameters.ClientAreaAnimation`).

**Что случилось:** гейт `Motion.Run(storyboard)` «уважал» настройку — возвращал `false`
и НЕ запускал анимацию. В продакшене он не вызывался ни разу (все 34 места брали из
`Motion` только длительности), то есть обещание жило в комментарии, а тест проверял
мёртвый хелпер. При этом наивная «починка» сломала бы интерфейс: очистка висит на
`Completed` — убрать тост (`ToastService.ScheduleToastRemoval`), свернуть оверлей
(`OverlayManager.CloseAll/CloseSingle`), спрятать кнопку (`AiAssistantControl`),
закрыть панель режимов (`QuickAddControl.AnwisMode`), свернуть бар обновления
(`ProgressBarUpdateAnimator`). Пропущенная анимация = тост не исчезает, оверлей не
закрывается, карточка остаётся с `Opacity=0` (пре-ролл в `MainWindow.Animations`).

**Факт (замер):** анимация нулевой длины доходит до конечного значения И вызывает
`Completed` — и для `BeginAnimation`, и для `Storyboard`. Замер закреплён тестом
`MotionTests.InstantAnimation_StillCompletes`: это инвариант, а не деталь реализации.

**Решение (v3.53.0):** «мгновенно» вместо «отменено»: токены
(`Motion.Fast/Base/Slow/Emphasized`) возвращают `TimeSpan.Zero`, а `Motion.Run`
проигрывает Storyboard со сжатым временем (`SetSpeedRatio` ×1000) — конечное состояние
применяется, `Completed` срабатывает. Смена темы тоже перестаёт анимироваться. Стражи:
`NoCode_StartsStoryboardsOutsideOfMotion` (единственная точка запуска) и условная
проверка «токены = 0, когда анимации выключены».

**Что НЕ покрыто (честно):** анимации, которые стартуют из XAML-триггеров
(hover/pressed в стилях контролов), — их запускает WPF, а не код; при выключенных
анимациях они остаются. Записано и в спецификации, а не умолчано.**Правило:** если на `Completed` висит не косметика, а состояние (свернуть, удалить,
снять подписку) — анимацию нельзя отменять, её нужно сжимать.

---

### 24. У Inter ступень 13px «вырождена»: высота чернил прыгает на +22% (СРЕДНИЙ)

**Где:** `Themes/Tokens.Typography.xaml` (шкала размеров), `Type.BodyMd` (удалён в v3.53.0),
всюду, где в разметке жили литералы `FontSize="13"`.

**Что случилось:** после вшивки Inter пользователи стали видеть текст «вытянутым по
высоте» — при том что на кнопках тулбара («Печать КП», «Заказчик», 12px) шрифт
выглядел правильно. Замер тем же WPF (RenderTargetBitmap + подсчёт «чернильного» бокса
по альфа-каналу, текст «Расчёт», режим Display):

```text
размер:        11px   12px   13px   14px   16px
Inter чернила:  8      9     11     11     12
Segoe UI:       8      9      9     10     11
```

**Причина:** у Inter на 13px высота глифов прыгает с 9px на 11px (+22%), пропуская
10px, — и по пропорциям глиф «тяжелеет» (ширина/высота падает с 4.56 до 3.91).
У Segoe UI такой ступеньки нет. «Вытянутость» была видна везде, где текст жил на
13px, — а это базовый размер интерфейса (бывший `Type.BodyMd` = 13, значение по
умолчанию окон) плюс 39 литералов `FontSize="13"` в разметке и 2 в коде.

**Решение (v3.53.0):** ступень 13px исключена из шкалы. `Type.BodyMd` удалён,
базой стал `Type.Body` = 12 (чернилами 12px Inter равен старому Segoe UI 12/13,
а рисунок букв остаётся Inter'овский). Все литералы 13 → 12; дробный 13.5 у
версии в «Обновлениях» → `Type.BodyLg` (14); единственный выживший 13.5 —
`Services/FlowDocumentBuilder` (печатный слой, пункты для QuestPDF, не интерфейс).
Подводный камень второго захода: первая миграция покрыла только атрибутную форму
`FontSize="13"`, а **сеттерная** `<Setter Property="FontSize" Value="13"/>` осталась
в 12 местах — DataGrid (та самая «гигантская» таблица позиций), все поля ввода
(TextBox/ComboBox/DatePicker/CheckBox/RadioButton), контекстное меню, вкладки —
то есть на всём, что не задаёт размер явно. Страж ловил только атрибутную форму.
Теперь стерж проверяет ОБЕ формы (`FontSize="13"` и `Value="13"`) по всем XAML
плюс `FontSize = 13` в C#; бюджеты ратчета понижены (обнулены) по 9 файлам.
Страж: `TypographyTests.TypeScale_ExcludesDegenerateThirteenPixelStep` + шкала
монотонна и без дробных ступеней.

**Правило:** при смене шрифта интерфейса перемеряй фактическую высоту чернил по всей
шкале размеров, а не доверяй номинальным px: у каждого шрифта свои «вырожденные»
ступени, и номинал их не показывает.

---

### 25. Плотность таблицы: строку держит элемент управления, а не текст (СРЕДНИЙ)

**Симптом (владелец):** «Проверь плотность таблицы позиций: высота строк и шапки
против 12px текста». В таблице позиций 9 строк занимали 354px по вертикали, и она
выглядела «тяжёлой» рядом с компактной сводкой ИТОГО.

**Причина — три независимые ошибки, которые складывались:**

| элемент | было | нужно контенту | «воздух» |
|---------|------|----------------|----------|
| шапка колонок | 36 | 12,6 (подпись **10,5px**) | 23,4 (65%) |
| строка данных | 32 | 14,4 (текст 12px) | 17,6 (55%) |
| строка с суммой монтажа | 39,2 | 26 (кнопка) + 13 (подпись) | — |

1. **Пола не было — было произвольное число.** `MinRowHeight` = 32 при строке
   текста 14,4px: 55% строки — пустота. При этом строку держал НЕ текст, а элементы
   управления: включаемый тумблер (20px) и кнопка «Вкл» в клетке «Монтаж» — она
   была 26px, поэтому строку нельзя было сжать ниже 27px, даже если текст
   уменьшить. Плотность в таблице данных задаётся самым высоким элементом в
   клетке, а не типографикой.
2. **Иерархия была перевёрнута.** `ColumnHeaderHeight` = 36 — шапка ВЫШЕ строк
   данных (32) и несёт при этом самую мелкую подпись (10,5px, ниже собственного
   пола шкалы 11px). Самая «жирная» полоса — при самом мелком тексте.
3. **Ритм ломался суммой монтажа.** v3.48.3 поставил сумму за метр подписью ПОД
   бейджем — высота клетки 26+13 = 39px против 21px у остальных: одна строка из
   пяти заметно выбивалась. В компактной шкале это стало бы 33 против 26 при
   разнице всего 7px — тем заметнее.

**Решение (v3.53.1):** всё в `Themes/DataGridStyles.xaml` + клетка «Монтаж»:
`MinRowHeight` и `ColumnHeaderHeight` → **26** (симметрия полос: содержимое 21px +
5px воздуха), подпись шапки → `Type.Caption` (11px — и на один дробный литерал
меньше), паддинг шапки 10,6 → 10,4, бар «Позиции заказа» 12,7 → 12,5, кнопка
«Вкл» 26 → 20px, а сумма монтажа переехала В СТРОКУ к бейджу
(`StackPanel Orientation="Horizontal"`). Замер после правки: строки 26, шапка 26,
9 строк + шапка + бар = 286px вместо 354px (−19%, +2,6 строки на том же экране).

**Правило:** высоту строки таблицы данных задаёт самый высокий элемент в клетке
(тумблер, кнопка, бейдж), а не размер шрифта. Меняя кнопки в клетках, перемеряй
плотность таблицы: строка молча вырастет. И шапка таблицы не должна быть выше её
строк — иначе самая заметная полоса несёт самый мелкий текст.

Страж — `Design/GridDensityTests` (открывает реальную таблицу вне экрана):
пустота вокруг строки текста ≤ 12px, шапка не выше строк, ритм не ломается суммой
монтажа.

---

### 26. Дробный и вне-шкальный размер шрифта «вытягивает» текст (СРЕДНИЙ)

**Симптом (владелец):** «Вот здесь тоже шрифт вытянутый по высоте» — меню печати
в попапе каретки «Печать КП» (три пункта).

**Причина (замирена, не догадка):** `PrintMenuItem` в `ActionBarControl.xaml`
задавал `FontSize="12.5"` — и это рисуется КАК 13, то есть на ступень выше
задуманного. При `TextFormattingMode=Display` и 100% DPI растеризатор квантует
размер в целую ступень, поэтому дробный размер никогда не даёт «чуть-чуть
больше» — он молча поднимает текст на следующую ступень. Замер
(RenderTargetBitmap, вшитый Inter, строка «Печать без предпросмотра»):

| задано | 10 · 10.5 · 11 | 11.5 · 12 | 12.5 · 13 | 13.5 · 14 | 15 | 16 |
|---|---|---|---|---|---|---|
| высота чернил | 10px | 12px | **14px** | 14px | 14px | 15px |
| ширина чернил | 145px | 159px | **177px** | 188px | 201px | 214px |

То есть автор писал 12.5 в надежде «между Body (12) и BodyLg (14)», а получал 13:
чернила на 17% выше и на 11% шире соседнего 12px-текста — ровно то, что владелец
назвал «вытянутым» и «огромным». Тот же механизм объясняет и прошлую жалобу на
13px (GOTCHAS §24): 13-я ступень даёт скачок чернил с 12 до 14px.

**Масштаб (замер по всему проекту):** 46 дробных вхождений (7.5 · 8.5 · 9.5 ·
10.5 · 11.5 · 12.5 · 13.5 · 14.5) и 64 вне-шкальных целых (8 · 9 · 10 · 15 · 17 ·
22 · 26) в интерфейсе — всего **110 в 29 файлах**. Вне-шкальное целое не даёт
мыла, но ломает иерархию: 10px подписей, 15px «заголовков» и 26px цифр жили рядом
с токенами 11/16/24 и спорили с ними.

**Решение (v3.53.1):** шкала стала обязательной, а не рекомендательной —
`12.5→12`, `11.5→12`, `10.5→11`, `9.5→11`, `10→11`, `14.5→14`, `15→16`, `17→16`,
`22→20`, `26→24`, монограммы бейджей `8→9` (Type.Monogram). Исключение одно и
осознанное — печатный слой `Services/FlowDocumentBuilder.cs`: там пункты QuestPDF,
дробные нужны для точной вёрстки листа и не проходят через экранный рендер.

Стражи: `DesignTokenGuardTests.FontSizes_LiveOnTheScale` (любой размер вне
`9/11/12/14/16/18/20/24/32/42` — сразу ошибка) плюс бюджет
`fractionalFontSizes = 0` (было 46).

**Правило:** размер шрифта в интерфейсе — только целой ступенью шкалы. Нужен
«размер между ступенями» — это повод пересмотреть шкалу для всего продукта, а не
писать половинку в одном месте: половинка не «немного меньше», а именно СЛЕДУЮЩАЯ
ступень, и рядом с обычным текстом видна как разнобой кеглей.

---

### 27. Радиусы мимо шкалы: «почти одинаковые» скругления и правило выбора токена (СРЕДНИЙ)

**Симптом.** Соседние элементы скругляются «чуть-чуть по-разному»: карточка
радиусом 10, её шапка — `10,10,0,0`, а соседняя такая же карточка — 12; бейдж 7px
рядом с бейджем 10px; тумблер 40x20 (r=10) и тумблер 48x26 (r=13) — один компонент
с двумя разными «полувысотами». Глаз читает это как неаккуратность, объяснить
словами трудно — потому что причины разные.

**Причина (замирена по файлам, не на глаз).** После первой миграции радиусов (226 → 113
в восьми плотных файлах) в разметке остались 113 литералов — и они делились на
четыре разных класса дефекта:

| Дефект | Факт |
|---|---|
| Карточки не на токене | 6 карточек-панелей жили на 10, хотя `Radius.Card` = 12: соседи по экрану скруглялись по-разному |
| Шапка ≠ корень | `10,10,0,0` у шапки против 12 у корня карточки — на стыке видна ступенька |
| Семейство без общего правила | бейджи-чипы `ChipBg` — 5 / 7 / 10; тумблеры — 10 / 13; прогресс-бар 34x5 — 2.5; скроллбар 6px — 3 |
| Окна разъехались | 11 модальных окон на 12, два (`AdminPasswordWindow`, `SendToFactoryWindow`) на 14 |

Отдельно: в C# радиусы задавались числами на месте (`new CornerRadius(2)` и
`(12)` в тостах, `(4)` в «Истории обновлений») — разметка уже жила на токенах, а код
оставался единственным местом, где шкала могла разойтись, и стражи его не видели.

**Решение (v3.53.1).** 113 литералов разметки и 5 в коде → 0. Токен выбирался по
правилу «ближайшее значение шкалы, при равном расстоянии — семантика, но не крупнее
родителя»:

| Литералы | Токен | Почему так |
|---|---|---|
| `0` | `Radius.None` (новый, 0) | Прямой угол — решение (плоский actionbar, `WindowChrome`), а не забытый литерал |
| `1.5` · `2.5` · `3` (полосы 3–6px: прогресс, скроллбар, индикаторы) | `Radius.CapsuleSm` (3) | На 5px полосе 2.5 и 3 дают одинаковое покрытие (радиус клампится по меньшей стороне) |
| `10` / `13` (треки тумблеров 40x20 и 48x26) · `8` / `11` (бегунки 16x16 и 22x22) · `9` (радио 18x18) · `7` (точка 6x6) | `Radius.Capsule`/`CapsuleLg` у треков, `Radius.Pill` у квадратов | Радиус капсулы = ровно половина высоты; у КВАДРАТА Pill пиксель-в-пиксель равен ей |
| `4` · `5` (пункты меню, вкладки, элементы строк) | `Radius.Sm` / `Radius.Element` | Попап 8 с паддингом 5 → вложенный пункт 4 (правило вложения) |
| `6` · `7` | `Radius.Control` — поля ввода, `Radius.Md` — кнопки/попапы/панели | Оба 8; разница только в семантике ключа |
| `8` | `Radius.Md` | Панель/попап шире поля ввода: значение сохраняем как есть |
| `10` (карточки) | `Radius.Card` (12) | Приводим к общей карточной дуге (+2px) |
| `10,10,0,0` · `14,14,0,0` | `Radius.CardHeader` | Шапка обязана повторять дугу корня |
| `0,0,10,10` | `Radius.CardFooter` (новый) | Парный к `CardHeader`: раньше токена для подвала не было вовсе |
| `12` / `14` (корни окон) | `Radius.Lg` (12) | Большинство окон уже на 12; два окна съезжают на −2px вместо одиннадцати на +4 |
| `6,0,0,6` · `0,6,6,0` · `0,7,7,0` | `Radius.StripeLeft` / `Radius.StripeRight` (новый) | Сегмент, склеенный с соседом: округлён только внешний край |
| `14,0,0,0` | `Radius.StripeLeft` | Полоса 4px у левой кромки окна |
| `5` · `7` · `10` (бейджи `ChipBg`, счётчики, статус) | `Radius.Capsule` (10) | Бейдж — капсула высотой 18–25px: 10 ≈ полувысота, остальные подтягиваем к тому же виду. Ставить сюда Pill НЕЛЬЗЯ — см. ниже |

**Находка — `Radius.Pill` на широком элементе даёт ЭЛЛИПС, а не капсулу.**
Комментарий к токену гласил «999 больше любой реальной высоты элемента» — как
будто WPF клампит радиус до полусферы. Замер (тот же WPF, RenderTargetBitmap,
96 dpi, сравнение пиксель-в-пиксель и по числу закрашенных пикселей):

| элемент | r = полувысота | r = 999 (Pill) | вердикт |
|---|---|---|---|
| 16x16, 22x22, 18x18, 24x24, 6x6 | круг | **отличий 0 байт** | Pill честен только на квадрате |
| 60x23 (бейдж) | капсула, 1317px | эллипс, +1264 отличия, −63px | Pill ломает бейдж |
| 40x20 (трек тумблера) | капсула, 737px | эллипс, +668 отличий, −69px | Pill ломает тумблер |
| 48x26 (крупный трек) | капсула, 1140px | эллипс, +932 отличия, −104px | Pill ломает тумблер |
| 6x40 (трек скроллбара) | капсула, 240px | сужающиеся концы, +404 отличия, −24px | Pill ломает скроллбар |
| 34x5: r=2.5 против r=3 | закрашено 170/170 одинаково | — | дробная ступень 2.5 не нужна, CapsuleSm (3) её заменяет |

Поэтому в шкале появились капсулы фиксированной высоты (`Radius.CapsuleSm` = 3,
`Radius.Capsule` = 10, `Radius.CapsuleLg` = 13), а `Pill` остался кругам — и это
записано прямо в комментарии токена, чтобы следующий агент не «упростил» капсулу
до Pill.

В коде — `Helpers/Radii` (`Card` / `Control` / `Element` / `Pill`): те же ключи
`Radius.*` через `TryFindResource` с запасным значением (радиус не зависит от темы,
поэтому тост может собраться до загрузки словарей).

**Проверка (не вакуумная).** Бюджет `cornerRadius` обнулён по всем 33 файлам;
`DesignTokenGuardTests.PartialRadiusTokens_MatchTheirCardEdges` держит инвариант
стыка, `DesignTokenGuardTests.CornerRadiusInCode_UsesTokens` — код (комментарии
из скана вырезаются: в пояснениях числа упоминаются как примеры). Форму держит
`Design/RadiusShapeTests`: Pill на квадрате пиксель-в-пиксель равен полувысоте и
НЕ равен капсуле на широком элементе (рендер вне экрана), а Pill в разметке
разрешён только тегам, у которых явные `Width` и `Height` равны — именно так
бейдж с `Padding` вместо размеров и ловится. Стражи проверены пробой:
`_ProbeRadius.xaml` с `Value="4"` и `_ProbeRadius.cs` с `new CornerRadius(9)` дали
падения с точным именем файла, `CardFooter` со значением `0,0,8,8` уронил инвариант
стыка, а собственный страж формы сначала уронил сам себя на определении ключа в
`Tokens.Radius.xaml` (ложное срабатывание убрано исключением словарей токенов).

**Правило.** Радиус — из токена, и первым делом по СМЫСЛУ элемента (карточка,
поле, бейдж, пункт меню), а не по ближайшему числу: значение токена вторично.
Бейдж — капсула, пункт меню — 4, поле ввода — 8, карточка — 12, и её шапка/подвал
повторяют её дугу. `Radius.Pill` — только круги (квадратные элементы), на широком
элементе он даёт эллипс. Нужен новый тип поверхности — сначала токен, потом
разметка.

---

### 28. Числа в таблице «зажёваны»: при правом выравнивании воздух даёт ЛЕВЫЙ отступ (СРЕДНИЙ)

**Симптом (владелец):** «Суммы зажёваны» — скриншот таблицы позиций, в кадре
числа в 5–6 знаков (суммы) стоят вплотную к числу соседней колонки.

**Замер по реальному кадру** (1200x760, чёрнильные боксы чисел, зазор между
соседними числами — самый узкий случай в таблице):

| Граница чисел | Было | Стало |
|---|---|---|
| ПЛОЩ./ДЛ. → ЦЕНА | **11px** | 28px |
| ЦЕНА → СУММА (самая длинная сумма) | **18px** | 26px |
| ЦЕНА → СУММА (короткая сумма) | 24px | 26px |
| Остальные колонки таблицы | 26–36px | не менялись |

**Причина.** Все числовые клетки были с правым выравниванием и СИММЕТРИЧНЫМ
`Margin="6,0"`. При правом выравнивании число прижато к правой границе своей
колонки, поэтому расстояние до числа соседней колонки равно сумме правого отступа
левой клетки и левого отступа правой — 12px, — и НЕ зависит от длины чисел. А вот
левая кромка самого числа съезжает влево тем сильнее, чем длиннее число: сумма в
5–6 знаков (60px против 50px у цены) съедала этот запас почти целиком. Колонка
«Площ./Дл.» вообще была без отступа (inline-стиль вместо общего `RightCell`), давая
6px у границы.

**Решение.** У правого выравнивания воздух даётся СЛЕВА: `Margin="14,0,6,0"`
вынесен в общий стиль `RightCell` и продублирован у клеток, которые задают отступ
явно; «Площ./Дл.» и «Высота» переведены на `RightCell` вместо своих inline-стилей.
Колонки при этом не съезжают: лишние 8px на число забирает звезда-колонка
«Наименование» (её MinWidth 80), а числовой блок остаётся на месте (правый край
суммы 1149px — как было).

**Проверка не вакуумная.** Страж
`GridDensityTests.NumericCells_KeepAirFromTheirLeftNeighbour` требует у всех клеток
с правым выравниванием левый отступ ≥ 12px и правый ≤ 8px и запрещает симметричную
форму `Margin="X,0"`. Проба «вернуть 6,0» дала падение с точным диагнозом
«симметричный Margin="6,0" — числа соседних колонок слипнутся».

**Правило.** Число в таблице — правое выравнивание, воздух 14px слева и 6px справа.
Симметричный отступ при правом выравнивании не работает: он отодвигает число от
СВОЕЙ границы и приближает к соседней. И проверять это нужно замером зазора между
чернилами соседних чисел, а не глазом: 6px отступа выглядят достаточно, пока в
колонке не появится длинное число.

---

### 29. Попапы живут в своём дереве: шрифт окна до них не доходит (ВЫСОКИЙ)

**Симптом (владелец):** «А вот это окно подверглось „редизайну“ с новым шрифтом?» —
скриншот контекстного меню карточек «Заказы» («Открыть заказ / Изменить статус… /
Экспорт заказа / Копировать / Удалить заказ»). Меню скруглённое, с новыми отступами
и hover — то есть редизайн применён, — а буквы системные.

**Замер по кадру владельца** (132x197, 1:1; чернильный бокс строки, порог по
яркости 40; шрифт тот же, что в приложении — 12px):

| Строка меню | Кадр владельца | Inter (вшитый) | Segoe UI |
|---|---|---|---|
| Открыть заказ | 78px | 87px | **78px** |
| Изменить статус... | 99px | 109px | **97px** |
| Экспорт заказа | 82px | 92px | **82px** |
| Копировать | 64px | 69px | **64px** |
| Удалить заказ | 73px | 85px | **75px** |

Совпадение с Segoe UI построчное (в пределах антиалиасинга), Inter шире на 10–15% —
меню рисовалось системным шрифтом.

**Причина.** Стиль `MenuItem` задавал `FontSize`, но не `FontFamily`, а умолчание
`Control.FontFamily` — это `SystemFonts.MessageFontFamily`, то есть Segoe UI. Стиль
`Window.Shared` тут не помогает: контекстное меню и подсказки живут в отдельном
Popup-дереве. Замер пробой (окно со стилем + кнопка внутри; окно показано):

| Элемент | FontFamily |
|---|---|
| Окно | `./#Inter` |
| `Popup.Child` с инлайновым содержимым (меню печати в ActionBar) | `./#Inter` |
| Кнопка-цель меню | `./#Inter` |
| `ContextMenu`, назначенное этой кнопке | **Segoe UI** |
| `└ MenuItem` внутри | **Segoe UI** |
| `ToolTip` с указанным `PlacementTarget` | **Segoe UI** |
| `└ TextBlock` внутри подсказки | **Segoe UI** |

То есть инлайновый `Popup` содержимое окна наследует (оно остаётся в логическом
дереве), а `ContextMenu`/`ToolTip` — нет, даже после `Show()`. Попап наследует
`DataContext` (поэтому биндинги в меню работают), а свойства оформления — нет;
это разные механизмы, и «биндинги работают» не означает «шрифт дошёл».

**Второй дефект той же природы.** У `ToolTip` не было стиля вообще: системный
светлый прямоугольник с Segoe UI поверх тёмного интерфейса (в тёмной теме —
заметное пятно).

**Решение.** `FontFamily="{DynamicResource Font.Text}"` добавлен и в стиль
`ContextMenu`, и в `MenuItem` (второй — не «на всякий случай»: пункты подменю
получают контейнеры в своём ItemsHost, и опираться только на наследование от корня
меню нельзя). Для подсказок в `Themes/MiscStyles.xaml` заведён неявный стиль
`ToolTip` на токенах: шрифт, размер `Type.Body`, поверхность `Surface`/`Border`,
радиус `Radius.Md`, паддинг `Pad.Row`, шаблон с `ContentPresenter`.

**Проверка не вакуумная.** `Design/PopupTypographyTests` — три уровня.
(1) Скан разметки: у каждого неявного стиля типов `ContextMenu`/`MenuItem`/`ToolTip`
обязан быть токен шрифта. (2) Рантайм: стили берутся из ресурсов по типу и
разрешение `FontFamily` проверяется у живых объектов. (3) Контрольная группа: пункт
СО СНЯТЫМ стилем обязан остаться на системном шрифте. Проба со снятием сеттеров
уронила обе первые проверки с диагнозом `FontFamily=Segoe UI` — ровно то, что на
кадре владельца. Отдельно: в контрольной группе стиль снимается явно (`Style = null`) —
без этого WPF подставит неявный стиль по типу, и контроль ничего не покажет
(пустой `new MenuItem()` в этом приложении уже получает Inter).

**Правило.** Стиль, чьё содержимое хостится в Popup — `ContextMenu`, `MenuItem`,
`ToolTip` и любой новый попап-тип, — обязан задавать шрифт сам. Проверять это
нужно рантайм-разрешением: «стиль лежит в словаре» проходит и когда сеттера в нём
нет (GOTCHAS §22, та же ловушка на окнах).

---

### 30. Смок-харнесс на занятом столе: клик и кадр обязаны доказать, что они «наши» (ВЫСОКИЙ)

**Симптом.** Прогон 2026-09-16 записал кадры 05 и 06 в чужом состоянии: на столе
работал второй экземпляр приложения, он держал фокус, клик по навигации не дошёл
(ушёл в чужое окно или в никуда), и харнесс молча снял ГЛАВНОЕ окно вместо
оверлея — записал его поверх базлайна. Тот же день, светлая тема: клик закрытия
промахнулся, кадры 04b и 09a записаны с открытым сайдбаром (235 426 и 204 898
пикселей скрима там, где в базлайне его нет). Ни одна из четырёх записей не
упала — тихий брак.

**Причина.** Кадры снимаются с ЭКРАНА (`CopyFromScreen`), а не из окна, поэтому
чужое окно поверх нашего попадает в базлайн как есть. Клик и клавиши
адресуются по координатам/фокусу, и Windows отдаёт их тому, кто сверху и
спереди — не факт, что нашему процессу. Ничего из этого харнесс не проверял.

**Решение (`.tools/uiverify.ps1`).**
(1) Страж «экран здесь наш»: сетка 5×4 точек по прямоугольнику окна (не «угол
и центр» — перекрытие бывает полосой), для каждой точки `WindowFromPoint` →
`GA_ROOT` → `GetWindowThreadProcessId` сравнивается с pid приложения. Не наша
точка → окно поднимается (`ShowWindow`/`BringWindowToTop`/`SetForegroundWindow`,
при отказе foreground-lock — `AttachThreadInput`, последний рубеж `HWND_TOPMOST`)
и проверка повторяется; не удалось — сцена ПАДАЕТ с диагнозом «какое окно
мешает» и кадр НЕ записывается. `Shot` проверяет область ДО захвата и ПОСЛЕ.
(2) `Click-At`/`Move-At` перед каждым кликом проверяют владельца точки: чужой
pid — отмена с диагнозом, клик не уходит в чужую программу.
(3) `Send-Keys-Safe` не отправляет клавиши, пока переднее окно не наше.
(4) Состояние сцены ждут по маркерам UIA (`Assert-SceneReady`), действие
повторяют до 3 раз; закрытие оверлея проверяется ПО ЭФФЕКТУ (`Close-Overlay`),
кадры «главное окно» требуют `Assert-NoOverlay`.
(5) Запись атомарная (tmp + `Move-Item` с 10 повторами — файл в shots может
мигом держать читатель) и с уборкой мусорных `*.tmp.png`.

**Самопроверка (`-SelfTest`) — страж не вакуумный.** Поверх окна кладётся
WinForms-форма самого харнесса (чужой pid) с магентой; подставной предикат
«в прямоугольнике всегда чужое» и точка внедрения «закрытие без эффекта».
Проверяется РЕЗУЛЬТАТ: при перекрытии `Shot` отказался писать (кадра нет),
клик по чужой точке отменён, незакрытый оверлей блокирует кадр главного окна,
настоящий путь закрытие делает, поднятый кадр без магенты (0/25). Без чужого
окна страж молчит — ложных срабатываний нет.

**Попутный дефект доступности.** Кнопка закрытия slide-over'а не имела UIA-имени
(только глиф) — Диктор читает пустоту, а UIA-Invoke был невозможен. Исправлено
(`AutomationProperties.Name` в `MainWindow.xaml`, страж в `AccessibilityTests`):
семантическое закрытие работает именно поэтому.

**Правило.** Любой новый экранный сценарий харнесса обязан идти через
`Assert-ScreenIsOurs` + маркеры состояния: «нажали и сняли через секунду» — это
не тест, а генератор тихого брака. Расхождение нового кадра с базлайном сначала
разбирайте по плиткам (где именно разошлось), а не перезаписывайте.

**«Что нового» появляется ПОЗЖЕ, чем находится главное окно.** Прогон
2026-09-18: семь кадров тёмной темы (01/02/03/04a/08a-d) разошлись с
базлайнами на 7,5% — поверх области быстрого добавления висело «Что
нового», которое показывается ОДИН раз на версию (то есть только в первом
прогоне после обновления) и попадает в UIA уже после одиночной проверки.
Страж «экран здесь наш» тут бессилен: окно принадлежит НАШЕМУ процессу,
поэтому и кадр считается своим. Теперь закрытие ждёт ФАКТИЧЕСКОГО
исчезновения окна (до 12 попыток: UIA-кнопка «Закрыть», затем ESC), а если
окно не ушло — прогон падает с диагнозом, а не пишет кадры с чужим окном.
Правило: стартовые окна проверяются не «один раз на старте», а ждут
устойчивого состояния перед первым кадром (улика — `01-main-dark` после
починки снова байт-в-байт равен прежнему базлайну).

**Вводное состояние кадра = часть детерминизма (2026-09-18).** Даже при
«экран здесь наш» и закрытом «Что новом» два прогона могли расходиться на
13 кадров: расхождение давало НЕ вид, а ИСТОРИЯ ВВОДА. Рамка фокуса
(FluentFocusVisual) и подсказка рисовались на том элементе, который получил
клавиатурный фокус раньше — а это зависит от того, показывалось ли окно
«Что нового» и чем его закрыли. Замеры: два прогона ПОДРЯД совпадали
пиксель-в-пиксель, а «обычный» против «первого после обновления» — нет
(до 14 000 px на кадр).

Разобранные тупики (чтобы не повторять): (а) инъекция движения мыши «в ту же
точку» — Windows не постит `WM_MOUSEMOVE` на перемещение без смещения, флаг
«последнее устройство ввода» не переключается; (б) `AutomationElement.SetFocus()`
на окно — пустое действие, когда фокус у чужого процесса (проверено логом:
фокус не менялся); (в) Win32 `SetFocus(hwnd)` — WPF понимает его как «окно
снова активно» и ВОЗВРАЩАЕТ каретку в элемент с логическим фокусом (расхождение
уехало из поля 03 в поле 06b); (г) каретка «мигает» — ложный след: SPI
`CaretBlinkTime` на машине вернул 0–2 мс (каретка статична), а различалось ПОЛЕ,
в котором она осталась.

Рабочий рецепт `Reset-InputState` (перед КАЖДЫМ кадром):
(1) настоящий клик через `mouse_event` по ИНЕРТНОЙ точке клиентской области
(инертность доказывается через UIA: ни одного живого контрола вверх 4 уровня;
клик по нефокусируемому фону переносит клавиатурный фокус на окно и гасит
рамку/подсказку); (2) UIA `SetFocus` на сам ЭЛЕМЕНТ ОКНА — окно focusable, но
без каретки и рамки (работает только после клика, поднявшего окно); (3) курсор
в фиксированную точку шапки; hover-сцены (`-KeepCursor`) снимаются с возвратом
курсора на место сцены, диалоги — без клика по чужой разметке.

Следствие: базлайны после внедрения изменяются ОДИН РАЗ — снимается накопленный
вводный шум прежних прогонов (рамки фокуса, «залипшие» подсказки, каретки).
После этого расхождение базлайна означает РЕАЛЬНОЕ изменение вида: «обычный» и
«первый после обновления» прогоны совпадают пиксель-в-пиксель (проверено на обеих
темах: 17/17 и 17/17).

---

### 31. Семья шрифта мимо токена: tofu без единого падения теста (ВЫСОКИЙ)

**Симптом.** Полный аудит «везде один шрифт» нашёл семьи, которые заданы
литералами в обход токенов: 80 мест в разметке с
`FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets"` (токен `Font.Icon`
существовал и использовался 4 раза), `Consolas, Courier New` в рендере маркдауна
чата (мимо каскада `Font.Mono` с Cascadia Mono) и три места в C# с собственными
`new FontFamily(...)`. Ничего из этого не ловил ни один тест: страж семей
**сам разрешал иконочные литералы** (`IconFamiliesMayStayLiteral`).

**Реальный дефект из этого ряда.** Глиф закрытия в `DialogService` был задан как
`new FontFamily("Segoe Fluent Icons")` — БЕЗ второго имени в списке. «Segoe Fluent
Icons» поставляется с Windows 11; продукт заявлен для Win10, где этой семьи нет,
и глиф рисуется пустым квадратом (tofu). WPF не падает и не пишет в лог — просто
квадрат. Список семей в WPF — это каскад фолбэков: первое существующее имя
побеждает, поэтому правильная строка —
«Segoe Fluent Icons, Segoe MDL2 Assets».

**Решение.** (1) Код берёт семьи из одной точки — `AppFontService.CreateIconFamily()`
и `CreateMonoFamily()`, строки-источники — константы `Font.IconFamilyString`/
`MonoFamilyString` рядом. (2) Разметка: все 80 литералов →
`{DynamicResource Font.Icon}` (DynamicResource, не StaticResource — по той же
причине, что и у `Font.Text`: токен подменяется в рантайме). (3) Страж семей
ужесточён: литерал `FontFamily="..."` в разметке — ошибка ВСЕГДА, без исключений
для иконок (опечатка в имени семьи иначе даст tofu, который не поймает ни один
тест; токен же проверяется на существование). (4) Новый страж `IconFontToken_KeepsWin10Fallback`
держит фолбэк MDL2 в токене. (5) Новый страж `FontFamiliesInCode_UseAppFontService_ExceptPrintLayer`:
в C# литералы семьи разрешены только в печатном слое (`FlowDocumentBuilder`,
`FixedDocumentBuilder`, `DrawingService` — SVG-схемы проёма, `PdfExportService`) —
бумага всегда белая, шрифт печати темой не переключается, Inter там не живёт.

**Проверка не вакуумная.** Вставка `new FontFamily("Segoe UI")` в UI-сервис
дала падение нового стража с точным именем файла и диагнозом; удаление — зелёный
прогон. Полный прогон после миграции: 2382/2382, verify-pixels чист — кадры
не изменились ни на пиксель (токен и литерал давали одну и ту же строку семей —
миграция меняет ownership, а не вид).

**Правило.** Новому глифу — `{DynamicResource Font.Icon}`, моно-тексту —
`{DynamicResource Font.Mono}`, тексту — `{DynamicResource Font.Text}`. Новая
семья в C# — только через `AppFontService`. Исключение одно и записано:
печатный слой.

---

### 32. Per-user самоустановка шрифта: три ловушки GDI и одна using (ВЫСОКИЙ)

**Задача.** После обновления шрифт должен появиться на устройстве сам —
без установщика и прав администратора. Решение — `FontSelfInstallService`:
при старте (фоном, после создания окна) проверяется наличие семьи Inter
через GDI; нет — комплект из вшитых ttf ставится per-user: файлы в
`%LocalAppData%\MosquitoNetCalculator\fonts\<версия>\` + `AddFontResource`
+ ключ `HKCU\...\Fonts`. Это страховка для печатного слоя и внешних
просмотрщиков — сам интерфейс рисует Inter напрямую из сборки через
`AppFontService`, поэтому отказы установки глотаются полностью.

**Ловушка 1: вшитые ttf не лежат рядом с exe.**
`<Resource Include="Resources\Fonts\*.ttf">` — это WPF Resource: файлы
компилируются в `.g.resources` сборки и НЕ копируются в вывод. Путь через
`AppContext.BaseDirectory` не работает. Извлекать надо ключ
`resources/fonts/inter-regular.ttf` (строчными!) из `.g.resources`
через `ResourceReader` — значение там `Stream`, иногда `byte[]`.

**Ловушка 2: GDI держит файлы открытыми.** `AddFontResource` загружает
шрифт в процесс и держит ttf открытым. Перезаписать зарегистрированный
файл в том же прогоне нельзя (IOException «being used by another
process»). Поэтому установка идёт в подпапку ВЕРСИИ комплекта:
при новой версии файлы пишутся в новую папку, ключи реестра
перезаписываются на новый путь, старая папка остаётся мусором (безвредно).

**Ловушка 3: using съедает ключ реестра.** Если «opener» ключа возвращает
ОДИН и тот же объект, а вызов обёрнут в `using` — после первого файла
ключ закрыт, второй файл получает `ObjectDisposedException: Cannot access
a closed registry key`. Opener обязан возвращать НОВЫЙ открытый ключ на
каждый вызов (или возвращать дубликат `OpenBaseKey`-стиля).

**Ловушка 4 (тестовая): тесты обязаны изолировать реестр.** Прогон тестов
с настоящим HKCU оставил в реальном профиле пять значений, указывающих на
удалённую тестовую папку. Тесты подменяют `FontsKeyOpener` на одноразовый
куст `Software\MosquitoNetCalculator\Tests\...` и удаляют его в Dispose;
`SettingsPath` тоже перенаправляется.

**Проверка.** 7 тестов: извлечение всех пяти ttf из сборки с проверкой
magic; формат имён реестра («Inter (TrueType) Bold (Inter-Bold.ttf)»);
семья есть → ничего не копирует; семья отсутствует → 5 файлов + версия в
settings + значения в подменном кусте; пустой комплект → false без
исключения; ключ строго HKCU; состав BundleFiles не разъехался с фактами.
Живой прогон приложения: Inter уже стоит → сервис молчит.

**Правило.** Любой новый вшитый шрифт для per-user установки — добавить в
`BundleFiles` и поднять `BundleVersion`; имена файлов — точное имя ресурса
в `Resources/Fonts/`. Установка всегда фоновая и best-effort: шрифт —
страховка, а не условие запуска.

---

### 33. MinWidth колонки DataGrid перезаписывается автосайзером молча (ВЫСОКИЙ)

**Симптом.** «Не полностью видно поле с адресом» — колонка со звездой
(`Width="*"`) и `MinWidth="170"` в XAML сжималась до ~90px, заголовок
под многоточием, перенос посреди слова. При этом XAML был «правильный».

**Причина.** `OrderGridPresenter.RefreshOrdersGrid` перед каждым
заполнением вызывает `DataGridColumnAutoSizer.SetColumnMinWidth` для
каждой колонки. Для колонки без `cellValues` минимум = ширина ЗАГОЛОВКА
+ паддинг (≈60px) — и это значение молча ПЕРЕЗАПИСЫВАЕТ XAML-минимум: WPF не мешает программно поставить MinWidth ниже заданного. Второй
удар: Auto-колонка телефона растягивалась под весь текст
(«+7 994 948 53 24» ≈ 190px), отнимая ширину у звезды; кап
ограничивал только MinWidth, но не фактическую auto-ширину.

**Правило.** Любой программный `MinWidth` колонки обязан уважать
XAML-минимум (берётся как пол) и капаться по содержимому: у колонок с
потенциально длинным текстом (телефон, адрес, статус) — явный cap в
вызове автосайзера. Если колонка звезда — её минимум не опускается.

**Остаток той же ошибки на остальных колонках (v3.53.1, живые кадры).**
Правило «минимум из разметки — пол» было применено только к адресу,
поэтому на узком окне владельца (~790 DIP) шапка «Заказов» рисовалась
как «№…» и «СУММА, Р…». Замер по кадру: колонка «№ КП» сжималась до
51px при подписи **50,8px** — т.е. не хватало 0,21px; «Сумма, руб.» —
ровно 95,0px под подпись 95,0px. Причина не «мало места», а НОЛЬ
запаса: замер `FormattedText` (подпись 11px SemiBold + паддинг 10+10)
ложится впритык, и автосайзер присваивал колонке именно его.
Теперь объявленный в разметке `MinWidth` — пол для ЛЮБОЙ колонки и
любого грида: `DataGridColumnAutoSizer` запоминает объявленное значение
при первом обращении к колонке и не даёт его опустить (запоминается
ПЕРВОЕ значение, а не текущее — иначе после длинного содержимого
минимум остался бы раздутым, храповик). Стражей два:
`SetColumnMinWidth_DoesNotDropBelowDeclaredMinWidth` (на старом коде
падает: 60 → 50,59) и `Заказы_ОбъявленныйMinWidthКолонок_ВмещаетПодписьШапки`
— он мерит каждую подпись ТЕМ ЖЕ инструментом, что и колонка
(`MeasureHeaderWidth`), и требует запас, чтобы страж и код не разъехались.
Живая проверка — кадры 06/06b харнесса до/после в обеих темах
(«№…» 22px → «№ КП» 31px; «СУММА, Р…» 67px → «СУММА, РУБ.» 74px).

**Сопутствующее — многострочные строки сливаются.** Когда строки
выросли в высоту (перенос адреса), выяснилось, что `GridLine` тёмной
темы `#26292F` на фоне строки `#1E2025` — контраст почти нулевой.
Разделители существовали, но были невидимы, и таблица выглядела
«кашей». Подняты до `#34383F` (тёмная) / `#E4E6EC` (светлая).
Правило: при повышении плотности/многострочности таблицы контраст
разделителей проверяется по реальным цветам темы, а не «линии есть».

---

### 34. Шкала отступов без стража: токены есть — принуждения нет (СРЕДНИЙ)

**Симптом.** Из четырёх шкал дизайн-системы три (размеры шрифта, радиусы,
движение) имели ратчет-бюджеты и стражей, а отступы — нет: токены `Gap.*`/`Pad.*`
существовали с запуска шкалы, но в разметке продолжали жить **694 литеральных
Margin/Padding** (36 файлов), из них **407 — значения вне шкалы** (5/6/7/9/10/14…
— те самые «почти одинаковые» отступы, против которых и вводилась шкала).
Никто не мешал добавить новый.

**Решение (v3.53.0, шаг 6).** (1) Ратчет-бюджет `margins` в
`design-token-budget.json` + страж `SpacingLiterals_DoNotGrow` (число может
только убывать; `Margin="0"` литералом не считается — обнуление не несёт
расстояния). (2) Шкала значений: `SpacingValues_LiveOnTheScale` требует
4·8·12·16·24·32·48 по каждому компоненту Thickness; вне-шкальные остатки —
через бюджет `totals.offScaleSpacings` (каждое видно в списке падения).
(3) Отрицательные отступы — отдельная категория `negativeSpacings` (осознанная
оптическая компенсация: глиф/рамка выходят за границу контейнера; их три —
выделение вкладки, кнопка на краю карточки, рамка диалога).
(4) Мигрированы точные совпадения токенов: 157 литералов → `Gap.*`/`Pad.*`
(добавлены недостающие 12px-направления `Gap.BlockLg/BlockTopLg/InlineLg/LeftLg`);
бюджет вписан в том же шаге.

**Ловушки подсчёта (обе проверены на живых числах).** (а) `Margin="0,2,-4,0"`
НЕ матчится паттерном значения `[0-9.,]+` (минус), но МАТЧИТСЯ детектором
«литерала» `[0-9.]` после кавычки — счётчик бюджета и детектор литералов
разъехались на 1, и страж падал «миграция развернулась назад» на честно
пересчитанном бюджете. Лечится: сначала ТЕБЕ реши, что делать с отрицательными,
потом выписывай бюджет — детектор и счётчик должны видеть одно и то же.
(б) Скан для бюджета делай ТЕМ ЖЕ regex, что и страж, а не «похожим» —
иначе получишь расхождение в 1–2 литерала на файл с минусами/нулями.

**Проверка:** проба вставки `Margin="6"` в чистый файл — падают ОБА стража
с точным файлом и значением (`6 > бюджета 5`; `Margin="6"` вне шкалы);
полный прогон 2394/2394; обе темы по 17 кадров — **базлайны не изменились
ни на один пиксель** (токены = те же числа, миграция эквивалентна по виду).

---

### 35. Кадр обязан зависеть от вида, а не от часов: дата и каретка (ВЫСОКИЙ)

**Симптом.** Ровно через сутки после перегенерации базлайнов прогон без единой правки
кода разошёлся на 5 кадров из 35: `04a-sidebar-open` (dark+light) — «18.09.2026» в
базлайне против «20.09.2026» в прогоне, `05-print-toolbar` (dark+light) — та же дата
в шапке печатного КП, `03-quickadd-invalid-light` — 15 пикселей каретки. То есть
контракт «расхождение базлайна = изменение вида» (§30) ломался сам по себе, раз в
сутки, ещё до того, как кто-то трогал код.

**Причины.** (а) Приложение печатает СИСТЕМНУЮ дату: дата договора
(`ClientInfo.ContractDate` = `DateTime.Today` для нового заказа) видна в сайдбаре
«Заказчик», а та же дата уходит в шапку КП («№ 2-51 от <дата договора>»,
`FlowDocumentBuilder`); харнесс её не фиксировал, поэтому кадр зависел от суток
прогона. (б) Каретка: приложение САМО фокусирует поле с ошибкой
(`QuickAddControl.AddItem.cs`), а каретка живёт по таймеру `CaretBlinkTime` (на этой
машине 0–2 мс — «горит» почти постоянно, а в кадр попадает по случайной фазе).
Важно: пересечение двух прогонов В ОДИН ДЕНЬ ничего не доказывает — прошлая проверка
§30 сравнивала кадры одного дня, поэтому дефект и не всплыл.

**Решение (`.tools/uiverify.ps1`).**
(1) Фиксация входных данных кадра: дата договора ставится харнессом через штатное
поле приложения (`DpContractDate`, `ValuePattern.SetValue`) и **читается обратно**
(выбрано `05.05.2025` — день равен месяцу, поэтому значение не зависит от порядка
«дд.мм»/«мм.дд» в культуре системы); не удалось зафиксировать — прогон падает, кадр
не пишется. Приложение при этом не меняется: дата фиксируется только на время
прогона.
(2) Перед КАЖДЫМ кадром `Assert-FocusNotInTextEdit`: фокус в `Edit`/`Document` —
ОШИБКА, а не предупреждение (в кадре была бы каретка). Снятие фокуса доводится до
факта: клик по инертной точке, повторные попытки, а если фокус всё равно в поле —
перевод на нейтральную КНОПКУ (`BtnAdd`): у неё нет ни каретки, ни шаблонной рамки
«в фокусе», а `FluentFocusVisual` гасится инъекцией мыши.

**Проверка (2026-09-20).** Базлайны перегенерированы ОДИН раз: изменились 4 кадра
тёмной темы (03/04a/05/06b) и 3 светлой (04a/05/06b), и все разобраны глазами —
04a: дата 18.09 → 05.05 плюс чип «Есть изменения» (появился потому, что дату
выставляет харнесс), 05: дата в шапке КП, 03: ушла каретка, 06b: ушло кольцо
фокуса в поле поиска. Ни одного «необъяснённого» пикселя из-за миграции отступов
(шаг 6) нет. Затем два прогона ОДНОГО дня из РАЗНЫХ стартовых состояний — обычный
и «первый запуск после обновления» (`LastSeenVersion` понижается в `settings.json`,
приложение показывает «Что нового», харнесс его закрывает; улика — версия в
`settings.json` после прогона снова текущая): **17/17 и 17/17 пиксель-в-пиксель**
(плюс третий прогон тёмной темы — тоже совпал).

**Стражи не вакуумные (две точки внедрения в параметрах харнесса).**
`-InjectNoDateFix` (дата не фиксируется) — расходятся РОВНО 04a и 05, остальные 15
совпадают (вырезка: «№ 2-51 от 05.05.2025» против «№ 2-51 от 20.09.2026»);
`-InjectFocusLeak` (снятие фокуса отключено) — прогон ПАДАЕТ на кадре 03 с диагнозом
`фокус стоит в текстовом поле (id='TxtQuickWidth')`, и кадра с кареткой не появляется
(записаны только 01 и 02).

**Правило.** Всё, что кадр берёт из окружения (дата, время, фокус, курсор, тема),
либо фиксируется харнессом И проверяется чтением обратно, либо объявляется входным
данным сцены. «Сегодня совпало» — не доказательство: совпадение надо показывать на
разных стартовых состояниях и на разных сутках, иначе базлайны гниют сами, а первый
же реальный дефект спишут на «шум базлайна».

### 36. Перегенерация update-log.json: BOM, несортированный json и формат секции релиза (ВЫСОКИЙ)

**Симптом.** Перед релизом 3.53.0 `tools/release/generate-update-log.ps1` перестал
запускаться: `TerminatorExpectedAtEndOfString`, «отсутствует закрывающий знак }».
А json после прогона выглядел протухшим («новейшая запись 3.48.7») — при том, что
в хвосте файла лежали актуальные 3.49–3.53.

**Причины.** (а) Скрипт — UTF-8 БЕЗ BOM и с кириллическими строками («Исправление»):
Windows PowerShell 5.1 читает его как ANSI, мультбайтовые последовательности съедают
терминаторы строк — парс падает (то же правило, что у всех .ps1 с кириллицей —
см. §1–§2; здесь оно было нарушено в release-инструментах). (б) `update-log.json`
ХРАНИТСЯ НЕОТСОРТИРОВАННЫМ: сортирует в рантайме `UpdateLog.AllNewestFirst` (date
desc, version desc), поэтому «первая запись файла» — не «новейшая версия», и оценка
свежести по `items[:5]` врёт. (в) Генератор парсит только секции формата
`## X.Y.Z — YYYY-MM-DD`: секция `## Unreleased — v3.53.0: …` в json не попала бы
ВООБЩЕ, и «Что нового» после обновления осталось бы пустым.

**Решение (семантика скрипта).** Датированные секции CHANGELOG — источник истины
для даты/заголовка/пунктов (иначе устаревшая запись 3.53.0 от 14.09 пережила бы
финализацию секции), но поле `type` остаётся курируемым из json: таксономию чипов
(«Новинка»/«Улучшение»/«Исправление»/«Техническое») эвристика по тексту не
восстанавливает. Версии без датированной секции переносятся в json как есть.
Скрипт идемпотентен (повторный запуск байт-в-байт) и падает, если ни одной
датированной секции нет. Замер: 73 записи (67 + 3.49–3.53), дублей версий 0.

**Правило.** Перед релизом: (1) секция версии в CHANGELOG обязана иметь формат
`## X.Y.Z — YYYY-MM-DD` и `###`-заголовок; (2) запуск `generate-update-log.ps1`,
проверка «новейшая запись = релизная версия» по ОТСОРТИРОВАНному списку; (3)
`extract-release-notes.ps1` берёт последнюю запись — она обязана совпасть с версией
в csproj.

### 37. AppSettingsService.SaveSettings глотает ошибки записи (СРЕДНИЙ)

**Симптом.** Тест на переустановку шрифта при устаревшей версии комплекта падал
странно: `SaveInstalledFontVersion("0.9")` не бросил ничего, но запись в settings
не появилась, и сервис честно решил «записи нет — шрифт ставили не мы».

**Причина.** `SaveSettings` оборачивает `File.WriteAllText` в try/catch и пишет
ошибку только в Debug-лог. Каталог в тесте создаёт `InstallBundle` — а тест звал
Save ДО него: `DirectoryNotFoundException` проглотился молча.

**Следствия.** (а) В тестах каталог под SettingsPath надо создавать явно (сделано
в ctor `FontSelfInstallServiceTests`). (б) В продакшене «тихая» потеря записи
означает: маркер версии шрифта не сохранится, и при следующем старте
`EnsureInstalled` сочтёт запись отсутствующей и НЕ переустановит устаревший
комплект — поэтому проверка версии написана так, что пустая запись трактуется как
«нет доверия» (не трогаем), а не «надо ставить».

**Правило.** Тихое глотание ошибок в персистентном слое — не «устойчивость», а
скрытая потеря данных: для записи настроек обязателен тест на «каталога нет», а
семантика «пустая запись» должна быть явно решена для каждого читателя.

### 38. Календарь DatePicker: попапу нужен СВОЙ ребёнок, а клеткам — СВОИ свойства (ВЫСОКИЙ)

**Симптом.** В тёмной теме выпадающий календарь поля «Дата» — белый квадрат из
другой ОС: светлая сетка, чёрные цифры. При этом фон и рамку `Calendar` стиль
задавал правильно, а тесты дизайн-системы были зелёными.

**Причина — две независимые ловушки, обе видны только на живом приложении.**

1. **`PART_Popup` требует ребёнком САМ `Calendar`.** DatePicker берёт календарь как
   `popup.Child as Calendar`; если в шаблоне положить обёртку (например, `Border`
   с хромом попапа и `Calendar` внутри), фреймворк заменит ребёнка СВОИМ
   календарём — без стиля вообще. Наружу это выглядит как «стиль не применился»,
   а причина в структуре дерева. Хром попапа (поверхность/рамка/скругление)
   поэтому рисует шаблон самого `Calendar`.
2. **Клетки календаря создаёт код.** 42 `CalendarDayButton` и 12 `CalendarButton`
   добавляет в сетку шаблона `CalendarItem` его код, а не разметка. До таких
   элементов не доходят НИ неявные стили из ресурсов приложения, НИ стили из
   ресурсов шаблона (замер: `Style=null`, применяется дефолт Aero2), а
   `Style.Resources` внутри стиля `Calendar` — тем более. Единственный рабочий
   путь — свойства `Calendar.CalendarDayButtonStyle` / `CalendarButtonStyle`
   (именно для этого они и существуют).

**Как проверять.** Только `popup.Child as Calendar` настоящего `DatePicker`:
`Design/CalendarThemeTests` открывает календарь попапа без окна и проверяет
42 клетки с моим шаблоном (пилюля `Radius.Pill`, акцент на выбранном дне),
7 приглушённых подписей дней (`DayTitleTemplate` по компонентному ключу),
12 клеток режима года и хром. Плюс живой замер: съёмка области попапа в обеих
темах (средняя яркость 39 против 246; доля почти-белых 0,5% против 94,9%;
доминирующий насыщенный цвет — акцент темы, то есть заливка выбранного дня).

**Правило.** Стиль, приложенный к контролу, и стиль, дошедший до его
код-созданных частей, — разные утверждения. Проверять надо на НАСТОЯЩЕМ
контроле (попап/диалог из шаблона), а не на экземпляре, собранном рукой в тесте:
первый покажет подмену ребёнка, второй — нет.

### 39. update-log.json — пользовательский текст, CHANGELOG.md — технический (ВЫСОКИЙ)

**Симптом.** Владелец: «я же говорил, что не нужно писать в „историю обновлений“
то, что вообще не касается обычного пользователя!» Запись 3.53.0 в
`update-log.json` состояла из 22 пунктов технического разбора CHANGELOG: пути
(`Themes/Tokens.Spacing.xaml`), токены (`Radius.StripeInner`), замеры (26px против
36px), счётчики прогонов (2394/2394), ссылки на GOTCHAS §20–§38. Ровно этот текст увидел бы пользователь в окне «Что нового» сразу после обновления — и это повторное
нарушение: правило «только пользовательские изменения, без техжаргона» уже стояло в
RELEASE_PROCESS (шаг 1.6), но инструмент ему противоречил. Тем же проходом владелец
попросил убрать мусор из всего, что написано после 3.49.0, и записи 3.50.0–3.52.0
переписаны на язык результата («Playtest-раунд», «Архитектурный проход», счётчики
прогонов, кадры `.tools/shots`, «+22 теста» — всё это читал заказчик). Следом тем же
вычищен и сам 3.49.0: 5 пунктов на 4489 знаков (имена тестов, отступы штампа в
DIP/мм, 2128/2128) → 7 пунктов на 1595 знаков про то, что видит и трогает заказчик.
Третьим заходом — весь ряд 3.48.x: 3.48.5 держал 149 пунктов на 44 821 знак
(журнал разработки: имена модулей, TFM, счётчики 2102/2102 и 1602/1602, миграция
папки agents/, утёкший в текст встроенный админ-пароль), 3.48.7 — 9 пунктов на
3987 знаков (XPS-сериализация, `mirrorUrl`, внутренние константы картинки) → 13
пунктов на 3548 знаков и 4 на 942. 3.48.3, 3.48.4 и 3.48.6 уже были
пользовательскими — их не трогали.

**Причина.** `tools/release/generate-update-log.ps1` при финализации релиза брал
title/changes ИЗ CHANGELOG (решение §36 — «иначе устаревшая запись переживёт
финализацию секции»). То есть документированный процесс «написать пользовательский
текст руками» затирался запуском генератора: человеческая формулировка и
технический разбор живут по разные стороны от скрипта, и скрипт вёл в неправильную.

**Решение.** Голос разделён по источникам: `CHANGELOG.md` — технический журнал
(файлы, замеры, GOTCHAS, тесты), `update-log.json` — текст для пользователя.
Генератор: version/date всегда из CHANGELOG (там финализируется дата релиза), а
title/changes/type — из json, если запись этой версии там уже есть (курированный
текст не подменяется); запись, которой в json нет, создаётся из CHANGELOG как
ЧЕРНОВИК и печатает warning. Страж `Services/UpdateLogVoiceTests.cs` (5 тестов)
роняет сборку на техжаргоне в пользовательских записях (`update-log.json` и
`releases.json` с версии 3.48.3 — самой старой переписанной; записи 3.48.0–3.48.2 в журнале не существуют): маркеры — обратные кавычки, `§`/GOTCHAS, имена
файлов, идентификаторы вида `Radius.Card`, px, счётчики прогонов, hex-цвета, классы
платформы и список техжаргона; плюс бюджет карточки (≤14 пунктов, ≤480 знаков на
пункт — прежние пункты были до 2000 знаков). Два теста держат сам детектор живым:
вырезанный текст прежней записи 3.53.0 обязан ловиться, а пользовательская
формулировка — нет. Проверено пробой: вставленный пункт с `Radius.Card`, GOTCHAS
§39, 2394/2394 pass и 26px уронил тест с версией, номером пункта и маркером; тот же
прогон генератора сохраняет курированный текст байт-в-байт (26 записей, черновиков 0).

**Правило.** Пользовательская запись отвечает на «что стало лучше для меня», а не на
«что изменено в коде». Технический разбор остаётся в CHANGELOG.md и GOTCHAS; в json
попадает только то, что заказчик заметит сам. Пишешь новый пункт — спроси себя,
поймёт ли его замерщик в офисе.

### 41. Хук, который «перекладывает файл в индекс», утаскивает чужие правки (ВЫСОКИЙ)

**Симптом.** Рабочее дерево обычно смешанное: в одном документе лежат правки разных
задач, застейджена только часть. Хук даты «Last verified» вызывал `git add -- <файл>`
и тем самым клал в индекс ВСЁ содержимое рабочего файла, а не свою строку.

**Замер сценарием (не чтением кода).** Одноразовый репозиторий, документ с двумя
независимыми правками — A в индексе, B только в рабочем дереве, — плюс посторонний
незастейдженный файл. Коммит запускается командой `git commit`, хук — как его
запускает git:

| | до коммита | после коммита (старый хук) |
|---|---|---|
| индекс | 2 строки (A) | — |
| рабочий файл сверх индекса | 2 строки (B) | **0** — B уехала в коммит |
| посторонний файл | 1 строка | 1 строка (не тронут) |
| содержимое коммита | — | A + строка даты + **B** (7 вставок) |

Незастейдженная правка молча становилась частью чужого коммита и из `git status`
пропадала — из рабочего дерева её уже не вернуть.

**Решение.** В индекс уходит РОВНО строка записи, и считается она по содержимому
ИНДЕКСА, а не рабочего файла (они разные): `git show :<файл>` → вставка строки →
`git hash-object -w --stdin` → `git update-index --cacheinfo <mode>,<sha>,<path>`
(mode — из `git ls-files -s`). Файл на диске по-прежнему получает дату (иначе #7
увидит старую), а правило вставки одно на оба пути (`Add-LastVerifiedStamp`), поэтому
индекс и рабочее дерево не могут разойтись молча. Замер после правки: коммит = A +
строка даты (4 вставки, 0 удалений), B остаётся незастейдженной и видна в статусе;
обычный случай (документ застейджен целиком) — 4 вставки и чистое дерево; документ с
уже сегодняшней датой — no-op.

**Вторая находка, из того же замера.** `$text.StartsWith([char]0xFEFF)` в Windows
PowerShell 5.1 НЕ детектит BOM: на строке без BOM возвращает `True` (char в этом
вызове приводится к пустой строке, а `StartsWith("")` истинно для любого текста —
замер: первый символ `#`, ответ `True`). Верный ответ даёт сравнение кода символа:
`([int]$text[0]) -eq 0xFEFF`. Цена ошибки: скрипт писал файл с `UTF8Encoding($true)` и
добавлял BOM тому, у кого его не было — лишняя правка первой строки в коммите (так
BOM появился в `agents/README.md` в коммите `9bcf1b7`: `23 20 41` → `ef bb bf`).
Исправлено в обоих скриптах (`sync-last-verified.ps1`, `sync-version.ps1`).

**Ещё ловушка.** Счётчик в скрипте нельзя назвать `$staged`: PowerShell не различает
регистр, и `$staged = 0` — это сам switch `$Staged`; скрипт падал на присваивании до
первого файла.

**Правило.** Хук, который правит файл и «перекладывает» его в индекс, обязан
перекладывать ТОЛЬКО свою правку (plumbing по blob'у индекса), а не файл целиком.
Проверка — сценарий staged+unstaged в одноразовом репозитории: если после коммита
рабочее дерево чистое, хук утащил чужие правки.

### 40. Дата «Last verified»: ручной шаг, который валидатор ловит только после коммита (СРЕДНИЙ)

**Симптом.** Правишь `agents/docs/*.md` — `validate-docs.ps1` #7 ругается
«STALE: MODULES.md - doc says 2026-09-14, git says 2026-09-21», и дату приходится
проставлять руками в каждом тронутом документе. На этой ручной простановке агент
спотыкался дважды за одну задачу.

**Причина.** Механизм был один и релизный: `sync-version.ps1` вставляет запись
только когда в csproj сменилась ВЕРСИЯ. При правке текста версия та же → документ
пропускается молча, а дата остаётся прошлой.

**Решение.** `agents/scripts/sync-last-verified.ps1` (дата + версия из csproj в
первую запись секции, идемпотентно) и `install-git-hooks.ps1` — локальный
`pre-commit`, который зовёт его в режиме `-Staged` и кладёт в индекс ТОЛЬКО строку
записи (почему именно так — §41). Дата ставится ДНЁМ КОММИТА: именно с ней сравнивает #7, поэтому правка,
закоммиченная через полночь, не «отстаёт» от собственной даты. `-Check` — проверка
без записи (exit 1) для ручного прогона и CI; `arc-check.ps1` синхронизирует даты
шагом [6] (проверено пробой: испорченная на 2020-01-01 дата в GOTCHAS даёт exit 1 с
диагнозом, а хук на staged-документе вставил дату и положил в индекс строку записи).

**Две ловушки.** (1) Хуки git НЕ версионируются: после клона и в новом worktree
`.git/hooks/pre-commit` отсутствует — запусти `install-git-hooks.ps1` заново (скрипт
откажется перезаписывать чужой хук без `-Force`, а путь берёт через
`git rev-parse --git-path` — в worktree это общий git-dir, а не файл `.git`).
(2) Окончания строк в `agents/docs` НЕ едины: `GOTCHAS.md`/`CURRENT_STATE.md` живут
на LF, `MODULES.md`/`RELEASE_PROCESS.md`/`MULTI_AGENT_ARC_CALC_CONTROL.md` — на CRLF
(git правит их на коммите), поэтому вставка строки с «своим» переводом строк даёт
смешанные окончания и шумный diff — скрипт берёт окончание из самого файла.

**Правило.** Дату «Last verified» руками не правят: её ставит инструмент в день
коммита. Ручная простановка — признак того, что хук не установлен.

---

## Риски по категориям

| Категория | Риск | Уровень |
|-----------|------|---------|
| Расчёты | Утечка Anwis-формул на не-Anwis | КРИТИЧНЫЙ |
| Расчёты | Изменение формул без согласования | КРИТИЧНЫЙ |
| Расчёты | Сохранение derived-полей в JSON | КРИТИЧНЫЙ |
| Цены | Потеря пользовательских цен при обновлении | ВЫСОКИЙ |
| КП | Сломанная вёрстка из-за неэкранированных символов | ВЫСОКИЙ |
| КП | Изменение формата без согласования | ВЫСОКИЙ |
| Завод | Неправильные размеры в тексте для производства | КРИТИЧНЫЙ |
| Автообновление | Рассинхронизация версий | ВЫСОКИЙ |
| Автообновление | Неправильный SHA-256 в releases.json | ВЫСОКИЙ |
| Данные | Потеря заказов при миграции путей | ВЫСОКИЙ |
| UI | Краш при переключении темы | СРЕДНИЙ |
| UI | `Width="Auto"` колонки не растёт при наборе (`LostFocus` без `PropertyChanged`) | СРЕДНИЙ |
| UI | Программный MinWidth колонки перезаписывает XAML-минимум; кап содержимого Auto-колонки (§33) | ВЫСОКИЙ |
| UI | Минимум колонки «впритык» к подписи (ноль запаса): шапка режется многоточием при объявленном MinWidth (§33) | СРЕДНИЙ |
| UI | DataGridTextColumn SelectAll race (отложенный BeginInvoke проигрывает первому keystroke, текст дописывается) | СРЕДНИЙ |
| UI | `{Binding Converter={DynamicResource ...}}` — компилируется, краш в рантайме (Converter не DP) | СРЕДНИЙ |
| UI | Неявный стиль базового типа не применяется к производным (стиль `Window` и наследники) | ВЫСОКИЙ |
| UI | Очистка на `Completed`: отмена анимации оставляет элемент в промежуточном состоянии | СРЕДНИЙ |
| UI | Вторая палитра литералами в C# (бейджи/статусы) не видна страху по XAML | СРЕДНИЙ |
| UI | Смена шрифта: «вырожденная» ступень размера даёт скачок высоты чернил (Inter@13: 9→11px) | СРЕДНИЙ |
| UI | Высоту строки таблицы задаёт элемент в клетке: кнопка/тумблер раздувают строку, шкала строк «не действует» | СРЕДНИЙ |
| UI | Дробный размер шрифта (12.5/10.5/11.5): субпиксельная шкала «вытягивает» глиф | СРЕДНИЙ |
| Дизайн | Шкала отступов без стража: токены есть — принуждения нет; счётчик бюджета и детектор литералов разъезжаются на отрицательных значениях (§34) | СРЕДНИЙ |
| UI | Вне-шкальный целый размер (10/15/17/26) мимо токенов ломает иерархию | СРЕДНИЙ |
| UI | Карточка 10 против шапки `10,10,0,0` и карточки-соседа 12 — ступенька на стыке | СРЕДНИЙ |
| UI | Радиусы в C# числами на месте: разметка на токенах, код — вне шкалы и вне стражей | СРЕДНИЙ |
| UI | `Radius.Pill` (999) на широком элементе: WPF не клампит радиус до полусферы — выходит эллипс вместо капсулы | СРЕДНИЙ |
| UI | Симметричный отступ у правого выравнивания: длинные числа слипаются с соседней колонкой | СРЕДНИЙ |
| Тесты | Смок на занятом столе: клик/кадр без доказательства «своего» окна — тихий брак базлайнов (§30) | ВЫСОКИЙ |
| Тесты | Кадр зависит от часов и истории ввода: базлайны гниют раз в сутки (дата в 04a/05), каретка в 03 (§35) | ВЫСОКИЙ |
| UI | Семья шрифта литералом мимо токена: tofu (глиф без MDL2-фолбэка на Win10), опечатка в семье не ловится никем (§31) | ВЫСОКИЙ |
| Обновления | Per-user установка шрифта: GDI держит файлы (перезапись), using закрывает ключ, тесты пишут в реальный HKCU (§32) | ВЫСОКИЙ |
| Обновления | update-log.json не перегенерирован перед релизом (формат секции/BOM/несортированный файл): «Что нового» после обновления пустое (§36) | ВЫСОКИЙ |
| Обновления | Технический разбор из CHANGELOG уезжает в «Что нового»: генератор берёт title/changes из CHANGELOG вместо курированной записи json (§39) | ВЫСОКИЙ |
| Документация | Дата «Last verified» проставляется руками: правка документа и валидатор #7 расходятся, пока кто-то не вспомнит; при правке текста `sync-version.ps1` документ пропускает (§40) | СРЕДНИЙ |
| Документация | Хук даты кладёт в индекс файл целиком: незастейдженные правки уезжают в чужой коммит и исчезают из `git status`; `StartsWith([char]0xFEFF)` не детектит BOM и дописывает его в файл (§41) | ВЫСОКИЙ |
| Данные | SaveSettings глотает ошибки записи: тихая потеря маркеров в settings.json; в тестах каталог обязателен заранее (§37) | СРЕДНИЙ |
| UI | Попап в шаблоне: не-`Calendar` ребёнок `PART_Popup` молча заменяется своим календарём без стиля; клетки создаёт код — нужны `CalendarDayButtonStyle`/`CalendarButtonStyle` (§38) | ВЫСОКИЙ |
| Доступность | Кнопка без UIA-имени: Диктор читает глиф, UIA-Invoke невозможен | СРЕДНИЙ |
## Source files

- `MosquitoNetCalculator/Models/OrderItem.cs`
- `MosquitoNetCalculator/Models/AnwisSize.cs`
- `MosquitoNetCalculator/Services/PriceService.cs`
- `MosquitoNetCalculator/Services/UpdateService.cs`
- `MosquitoNetCalculator/Services/WatchdogService.cs`
- `MosquitoNetCalculator/Services/PrintService.cs`
- `MosquitoNetCalculator/Services/ThemeService.cs`
- `MosquitoNetCalculator/Services/AppSettingsService.cs`
- `MosquitoNetCalculator/Helpers/Motion.cs`
- `MosquitoNetCalculator/Themes/MiscStyles.xaml`
- `MosquitoNetCalculator/Themes/DataGridStyles.xaml`

---

## Last verified
2026-09-21 (v3.53.0) — хук даты «Last verified» кладёт в индекс только свою строку (сценарий staged+unstaged больше не утаскивает чужие правки), BOM-детект исправлен в обоих скриптах; §41.

2026-09-21 (v3.53.0) — запись 3.53.0 в update-log.json переписана пользовательским языком, генератор больше не затирает курированный текст, страж `UpdateLogVoiceTests`; §39.

2026-09-20 (v3.53.0) — релизная подготовка: CHANGELOG датирован, update-log.json перегенерирован; §36–§37.

2026-09-21 (v3.53.0) — календарь DatePicker тематизирован целиком (клетки дня/месяца, хром попапа); §38.

2026-09-14 (v3.53.0) — auto-synced from csproj (sync-version.ps1, CONTROL#13).

2026-09-14 (v3.52.0) — auto-synced from csproj (sync-version.ps1, CONTROL#13).

2026-09-14 (v3.51.0) — auto-synced from csproj (sync-version.ps1, CONTROL#13).

2026-09-11 (v3.50.0) — auto-synced from csproj (sync-version.ps1, CONTROL#13).

2026-09-06 (v3.49.0) — auto-synced from csproj (sync-version.ps1, CONTROL#13).

2026-08-30 (v3.48.7) — auto-synced from csproj (sync-version.ps1, CONTROL#13).

2026-08-30 (v3.48.6) — auto-synced from csproj (sync-version.ps1, CONTROL#13).

2026-08-30 (v3.48.4) — auto-synced from csproj (sync-version.ps1, CONTROL#13).

2026-08-27 (v3.47.4) — auto-synced from csproj (sync-version.ps1, CONTROL#13).


2026-08-20 — maintenance pass (CONTROL#13): дата верификации синхронизирована с git-историей; содержимое сверено с текущим состоянием (v3.47.3, 1511/1511 tests pass).

2026-07-22 (v3.47.3 — URGENT bugfix per-linear-meter legacy JSON для Отлив/Козырёк:
строгий `isLegacyLoad` детектор + sign-flip exclusion для per-linear-meter +
5 регрессионных тестов. **1253/1253 tests pass**.)
