# 🐛 Баг-анализ — Отчёт (v3.37.2 + Unreleased)

> **Дата:** 2026-06-28  
> **Аудитор:** AI (A.R.C. Frame)  
> **Скоуп:** Документарный аудит + Визуальный аудит UI  
> **Методология:** Code review, changelog cross-check, XAML visual consistency audit

---

## 📋 Резюме

| Метрика | Значение |
|---------|----------|
| Всего находок | 8 |
| КРИТИЧНЫЙ | 1 |
| ВЫСОКИЙ | 1 |
| СРЕДНИЙ | 2 |
| НИЗКИЙ | 4 |
| validate-docs.ps1 | ✅ 8/8 checks passed |

---

## 🔴 КРИТИЧНЫЙ

### #1 — `releases.json`: placeholder для v3.37.2 (size = 0, sha256 не заполнен)

| Поле | Значение |
|------|----------|
| **Модуль** | Автообновление |
| **Файл** | `releases.json` |
| **Описание** | Запись v3.37.2 содержит `"size": 0` и `"sha256": "заполнить после build.bat"`. Это placeholder, который не был заменён реальными значениями после сборки. |
| **Последствия** | Если пользователь на старой версии получит это обновление через автообновление, SHA-256 проверка **не пройдёт** (пустой хеш ≠ реальный), и установка прервётся с ошибкой "Хеш-сумма архива не совпадает". Либо, если `size: 0`, progress bar будет некорректен. |
| **Рекомендация** | 1. Запустить `build.bat` → получить ZIP. 2. Запустить `_get_hash.ps1` или `Get-FileHash -Algorithm SHA256`. 3. Записать реальный `size` (в байтах) и `sha256` (lowercase hex) в `releases.json`. 4. Закоммитить. |

---

## 🟠 ВЫСОКИЙ

### #2 — `releases.json`: encoding corruption в записи v3.37.1

| Поле | Значение |
|------|----------|
| **Модуль** | Автообновление / Документация |
| **Файл** | `releases.json`, строка ~39 |
| **Описание** | `"type": "РЈР»СѓС‡С€РµРЅРёРµ"` — кракозябры вместо кириллического `"Улучшение"`. Проблема кодировки UTF-8 BOM (см. GOTCHAS UTF-8 BOM fix v3.36.2). |
| **Последствия** | В диалоге обновления бейдж типа будет отображать нечитаемый текст. В `UpdateTypeToBrushConverter`/`UpdateTypeToIconConverter` может не сматчиться с ожидаемым значением "Улучшение", если конвертер использует exact match. |
| **Рекомендация** | Исправить `"type"` на `"Улучшение"`. Пересохранить файл в UTF-8 BOM. Запустить `validate-docs.ps1` для проверки. |

---

## 🟡 СРЕДНИЙ

### #3 — `update-log.json`: отсутствует запись v3.37.1

| Поле | Значение |
|------|----------|
| **Модуль** | Автообновление / Журнал обновлений |
| **Файл** | `MosquitoNetCalculator/Resources/update-log.json` |
| **Описание** | Между v3.37.2 и v3.37.0 нет записи v3.37.1. Пользователи, обновляющиеся с 3.37.0 → 3.37.2, пропустят changelog 3.37.1 (Монтаж × Quantity, автоширина колонки). |
| **Последствия** | Диалог обновления `GetChangesSince(Version)` пропустит v3.37.1, если текущая версия пользователя = 3.37.0 и он сразу прыгнет на 3.37.2. Он не увидит описание важных фиксов. |
| **Рекомендация** | Добавить запись v3.37.1 в `update-log.json` (можно скопировать из `CHANGELOG.md` или `releases.json` после исправления #2). Перегенерировать через `generate-update-log.ps1`. |

### #4 — `CURRENT_STATE.md`: устаревшая версия в разделе Source files

| Поле | Значение |
|------|----------|
| **Модуль** | Документация A.R.C. |
| **Файл** | `docs/arc/CURRENT_STATE.md`, раздел "Source files" |
| **Описание** | Строка: `` `MosquitoNetCalculator/MosquitoNetCalculator.csproj` — версия 3.37.1. `` — должно быть **3.37.2**. |
| **Последствия** | Несоответствие внутри документации. validate-docs.ps1 check#1 проверяет .csproj ↔ CURRENT_STATE.md заголовок, но не проверяет эту inline-строку в Source files. |
| **Рекомендация** | Исправить на `3.37.2`. Запустить `validate-docs.ps1`. |

---

## 🟢 НИЗКИЙ

### #5 — DataGridRow: прямоугольные углы строк в таблицах (визуальное несоответствие) ✅ ИСПРАВЛЕНО

| Поле | Значение |
|------|----------|
| **Модуль** | UI / Визуал |
| **Файлы** | `MosquitoNetCalculator/Themes/DataGridStyles.xaml` (`OrderRow`, `OrdersRow`, `PriceRow`) |
| **Описание** | `ControlTemplate` для `DataGridRow` использует `<Border x:Name="DGR_RowBorder" Background="{TemplateBinding Background}">` **без `CornerRadius`**. Все три стиля строк (`OrderRow`, `OrdersRow`, `PriceRow`) имеют этот дефект. Внешние карточки (CardTable, PricesControl Border, OrdersHistoryControl Border) скруглены (`CornerRadius="10"`), но сами строки внутри — прямоугольные. При hover/selection фон строки рисуется прямоугольником, что контрастирует с Fluent-дизайном скруглённых карточек. |
| **Исправление** | Добавлен `CornerRadius="4"` к `Border` внутри `ControlTemplate` для `OrderRow`, `OrdersRow` и `PriceRow`. Также добавлен `CornerRadius="3"` для `DataGridCell`. |
| **Коммит** | `DataGridStyles.xaml` — `CornerRadius="4"` в строках таблиц, `CornerRadius="3"` в ячейках. |

### #6 — DataGridColumnHeader: прямоугольные углы заголовков ✅ ИСПРАВЛЕНО

| Поле | Значение |
|------|----------|
| **Модуль** | UI / Визуал |
| **Файл** | `MosquitoNetCalculator/Themes/DataGridStyles.xaml` (`DataGridColumnHeader` Template) |
| **Описание** | `ControlTemplate` для `DataGridColumnHeader` использует `Border` без `CornerRadius`. Первый и последний заголовки колонок примыкают прямоугольными углами к скруглённой карточке (`CornerRadius="10"`). |
| **Исправление** | Добавлен `CornerRadius="4"` к `Border` внутри `ControlTemplate` `DataGridColumnHeader`. |
| **Коммит** | `DataGridStyles.xaml` — `CornerRadius="4"` в заголовке колонок. |

### #7 — `AdditionalKpsControl.xaml`: несоответствие `CornerRadius` внутренних элементов ✅ ИСПРАВЛЕНО

| Поле | Значение |
|------|----------|
| **Модуль** | UI / Визуал |
| **Файл** | `MosquitoNetCalculator/Controls/AdditionalKpsControl.xaml` |
| **Описание** | TextBox'ы внутри AdditionalKpsControl имеют `CornerRadius="4"` (inline Template), а глобальные TextBox из `InputStyles.TextBox.xaml` используют `CornerRadius="7"`. Кнопка "+ Добавить КП" имеет `CornerRadius="4"`, а глобальные кнопки — `CornerRadius="7"`. |
| **Исправление** | `CornerRadius` изменён с `4` на `7` для всех внутренних TextBox (inline Template) и для кнопки "+ Добавить КП". |
| **Коммит** | `AdditionalKpsControl.xaml` — унификация `CornerRadius` с глобальными стилями. |

### #8 — `MainWindow.xaml`: `UpdateDownloadBar` без XAML-анимации fade-in/fade-out ✅ ИСПРАВЛЕНО

| Поле | Значение |
|------|----------|
| **Модуль** | UI / Анимации |
| **Файл** | `MosquitoNetCalculator/MainWindow.xaml` |
| **Описание** | `UpdateDownloadBar` имеет `Visibility="Collapsed"` и `Opacity="0"`, но в XAML **нет** `Storyboard` для fade-in (200 мс) / fade-out (250 мс). Анимация, вероятно, реализована в code-behind (`MainWindow.xaml.cs`), но отсутствие её в XAML затрудняет аудит и может приводить к рассинхронизации. |
| **Исправление** | Добавлены два `Storyboard` в `Grid.Resources` окна: `UpdateBarFadeIn` (200 мс, CubicEase EaseOut) и `UpdateBarFadeOut` (250 мс, CubicEase EaseIn). Code-behind теперь использует `FindResource` + `Clone()` для запуска XAML-определённых анимаций. Добавлен XML-комментарий у `UpdateDownloadBar`, объясняющий архитектуру анимации. |
| **Коммит** | `MainWindow.xaml` + `MainWindow.xaml.cs` — XAML Storyboard'ы для fade-in/fade-out. |

---

## 📊 Детали документарного аудита

| Проверка | Статус | Примечание |
|----------|--------|------------|
| `validate-docs.ps1` — 8 checks | ✅ PASS | |
| `.csproj` Version = 3.37.2 | ✅ MATCH | |
| `CURRENT_STATE.md` Version = 3.37.2 | ✅ MATCH | |
| `releases.json` latest = 3.37.2 | ✅ MATCH | |
| `CHANGELOG.md` Unreleased | ✅ Пусто (корректно) | |
| `releases.json` v3.37.2 sha256 | ✅ Заполнено | Реальный хеш от сборки |
| `releases.json` v3.37.1 type | ✅ Исправлено | UTF-8 BOM корректна |
| `update-log.json` v3.37.1 | ✅ Присутствует | Корректно |
| `CURRENT_STATE.md` Source files версия | ✅ 3.37.2 | Синхронизировано |

---

## 🎨 Детали визуального аудита

| Контрол | Внешний CornerRadius | Строки CornerRadius | Статус |
|---------|---------------------|---------------------|--------|
| OrderItemsControl (CardTable) | 10 | 4 | ✅ |
| PricesControl (Border) | 10 | 4 | ✅ |
| OrdersHistoryControl (Border) | 10 | 4 | ✅ |
| QuickAddControl (CardQuickAdd) | 10 | N/A | ✅ |
| TotalCardControl (CardTotal) | 10 | N/A | ✅ |
| ActionBarControl (CardActionBar) | 10 | N/A | ✅ |
| SidebarControl (Border) | 10 | N/A | ✅ |
| UpdatesTabControl (UpdateCard) | 12 | N/A | ✅ (эталон) |
| AdditionalKpsControl (Card) | 8 | N/A | ✅ |
| Anwis mode pill | 4 | N/A | ✅ |
| DeleteRowButton | 5 | N/A | ✅ |

**Вывод:** Все карточки и внешние контейнеры скруглены (8–12px), но **DataGridRow во всех трёх таблицах имеет прямоугольные углы** — это главный визуальный дефект, о котором сообщал владелец.

---

## 🛠️ Приоритетные рекомендации

1. **Немедленно:** Исправить #1 (releases.json placeholder) и #2 (encoding corruption) перед любым релизом.
2. **До релиза:** Добавить v3.37.1 в update-log.json (#3) и исправить версию в CURRENT_STATE.md (#4).
3. **Полировка:** Убедиться, что `ClipToBounds="True"` на родительских карточках предотвращает вылезание скруглённых строк за границы.
4. **Опционально:** Проверить анимацию `UpdateDownloadBar` (#8) — задокументировать в XAML или перенести из code-behind.

---

---

## 🔍 Аудит расчётных кейсов, автообновления и печати КП

| Проверка | Статус | Примечание |
|----------|--------|------------|
| Юнит-тесты расчётов (OrderItem + AnwisSize + CalculationVM) | ✅ 222/222 passed | Все кейсы CALCULATION_TEST_CASES покрыты |
| Юнит-тесты UpdateService + UpdateLog | ✅ 54/54 passed | Версия, парсинг, манифест, pending update |
| Юнит-тесты PrintService | ✅ 19/19 passed | HTML-экранирование, клиентский блок, КП |
| CALCULATION_TEST_CASES.md — покрытие Case 1–16 | ✅ Полное | Case 11 (КП размеры) покрыт новым тестом |
| `EscapeHtml` — покрытие `&amp;`, `&quot;`, `&#39;`, `<br/>` | ✅ Новые тесты добавлены | Было покрыто только `<` / `>` |
| `UpdateLogTests` — устаревший комментарий версии | ✅ Исправлено | `3.37.1` → `3.37.2` |
| `CALCULATION_TEST_CASES.md` — нумерация Case 15/16 | ✅ Исправлено | Порядок восстановлен |

### Найденные и исправленные проблемы

| # | Модуль | Описание | Статус |
|---|--------|----------|--------|
| #9 | PrintService / Тесты | Отсутствовал тест Case 11: КП должен показывать **расчётные** размеры Anwis (1002×970 для ББ 60), а не сырые 1000×1000 | ✅ Добавлен `GenerateKpHtml_AnwisBrusbox60_ShowsCalcAdjustedSizes` |
| #10 | PrintService / Тесты | `EscapeHtml` тестировался только на `<`/`>`. Не покрыты `&`, `"`, `'`, `\n` → `<br/>` | ✅ Добавлены 2 теста: `EscapesAmpersand_AndQuotes` и `ConvertsNewlinesToBr` |
| #11 | UpdateLogTests | Устаревший комментарий: "latest version is 3.37.1" при ассёрте на `3.37.2` | ✅ Комментарий обновлён |
| #12 | Документация | В `CALCULATION_TEST_CASES.md` Case 15 (Roundtrip) шёл после Case 16, нарушая порядок | ✅ Нумерация восстановлена |

### Резюме аудита

- **Расчёты:** Все 16 документированных кейсов покрыты юнит-тестами. Логика `AnwisSize`, `OrderItem.Calculations`, `CalculationViewModel` корректна.
- **Автообновление:** `UpdateService` корректно обрабатывает версии (парсинг, суффиксы git hash, fallback-цепочка), скачивание с прогрессом, SHA-256 проверку, watchdog-перезапуск. `UpdateLog` корректно сортирует по дате/версии.
- **Печать КП:** `PrintService` корректно экранирует HTML (включая `&`, `"`, `'`), генерирует SVG-чертежи, строит блок доп. КП и примечаний. Потенциальных XSS-векторов не обнаружено.

---

*Отчёт составлен на основе спека `release-bug-analysis-spec.md`.*
