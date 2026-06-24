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

### 5. Автообновление: имена файлов и версии (КРИТИЧНО)

**Где:** `UpdateService.cs`, `WatchdogService.cs`, `releases.json`.

**Что может сломаться:**
- Имя ZIP в `releases.json` не совпадает с фактическим именем файла в GitHub Release.
- Версия в `.csproj` не совпадает с версией в `releases.json`.
- Изменение имени `ExeFileName` в `WatchdogService` без обновления `build.bat`.

**Правило:**
- Версия = единственный источник правды в `.csproj` (`<Version>X.Y.Z</Version>`).
- `releases.json` должен быть синхронизирован вручную (или через скрипт).
- Имя asset'а: `ARC-Frame-X.Y.Z-full.zip`.

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

2026-06-23 (версия 3.35.0)
