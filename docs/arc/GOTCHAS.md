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

**Кейс:** `docs/arc/CALCULATION_TEST_CASES.md#Case 16` (16/16b/16c).

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

## Source files

- `MosquitoNetCalculator/Models/OrderItem.cs`
- `MosquitoNetCalculator/Models/AnwisSize.cs`
- `MosquitoNetCalculator/Services/PriceService.cs`
- `MosquitoNetCalculator/Services/UpdateService.cs`
- `MosquitoNetCalculator/Services/WatchdogService.cs`
- `MosquitoNetCalculator/Services/PrintService.cs`
- `MosquitoNetCalculator/Services/ThemeService.cs`
- `MosquitoNetCalculator/Services/AppSettingsService.cs`

## Last verified

2026-06-27 (Цена auto-width fix (GOTCHAS#13) — 636/636 tests pass)
