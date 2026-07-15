# Нативная печать КП с разбивкой по копиям — Спецификация

> **Статус:** Draft — собраны требования из 4 раундов интервью.
> **Цель:** Полный отказ от WebView2 + унифицированное окно предпросмотра с настройками печати + collated copies.
> **Версия:** 1.3 (2026-07-04 — §8.7: стратегия обработки ошибок PrintQueue.AddJob)

---

## 1. Резюме

Замена текущей печати через WebView2/HTML на полностью нативную WPF-печать с **единым окном**, объединяющим предпросмотр и настройки печати:

- **Предпросмотр:** `FlowDocumentReader` (нативный WPF)
- **Боковая панель (слева):** настройки печати — принтер, копии, разбивка по копиям, цветность, диапазон страниц
- **Печать:** напрямую через `PrintQueue.AddJob` (без `PrintDialog`)
- **Коллация:** кастомный `CollatedPrintPaginator` — декоратор, повторяющий страницы N раз в правильном порядке
- **Колонтитулы:** через существующий `PrintPaginator` (№ договора + нумерация страниц)
- **Чертежи:** через существующие `DrawingImage` (SVG → WPF Geometry для preview)
- **Полное удаление:** WebView2, HTML-шаблон, legacy-код, флаг `useNativePrint`

### Что уходит

| Удаляем | Причина |
|---|---|
| `Microsoft.Web.WebView2` (NuGet) | Зависимость от браузерного рантайма |
| `Resources/print_template.html` | HTML-шаблон |
| `PrintService.HtmlBuilder.cs` | HTML-генерация |
| `PrintService.SvgDrawer.cs` | SVG-строки (уже заменены на Drawings.cs) |
| `PrintService.Template.cs` | Загрузка HTML-шаблона |
| `PrintPreviewWindow.xaml` + `.xaml.cs` (старые) | WebView2-based окно |
| `DependencyCheckerService` — WebView2-часть | Проверка WebView2 Runtime |
| `App.xaml.cs` — вызов `CheckAndNotifyAsync()` | Toast о WebView2 |
| `AppSettingsService` — флаг `useNativePrint` | Больше не нужен (нет fallback) |

### Что приходит

| Добавляем | Роль |
|---|---|
| `PrintPreviewWindow.xaml` + `.xaml.cs` | **Переписан:** FlowDocumentReader + левая боковая панель |
| `Helpers/CollatedPrintPaginator.cs` | Декоратор: range filter + copies × collation ordering |
| `Controls/NumericUpDownControl.xaml` + `.xaml.cs` | UserControl: поле ввода копий с кнопками ± |
| (существующие файлы остаются) | `PrintService.FlowDocument.cs`, `.Drawings.cs`, `.Pdf.cs`, `PrintPaginator.cs` |

---

## 2. Текущее состояние (что уже готово vs. что нужно сделать)

### 2.1 Уже реализовано (backend)

| Файл | Статус | Что делает |
|---|---|---|
| `PrintService.FlowDocument.cs` | ✅ Готов | `BuildFlowDocument()` — строит A4 FlowDocument |
| `PrintService.Drawings.cs` | ✅ Готов | SVG + WPF DrawingImage для 13 товаров |
| `PrintService.Pdf.cs` | ✅ Готов | `ExportPdf()` через QuestPDF |
| `Helpers/PrintPaginator.cs` | ✅ Готов | Header/footer (№ договора + стр. X из Y) |
| `QuestPDF` (NuGet) | ✅ Установлен | PDF-библиотека |

### 2.2 Требует реализации (frontend + wiring)

| Задача | Приоритет |
|---|---|
| Переписать `PrintPreviewWindow.xaml` — FlowDocumentReader + левый sidebar | P0 |
| Переписать `PrintPreviewWindow.xaml.cs` — логика боковой панели + печать | P0 |
| Создать `Helpers/CollatedPrintPaginator.cs` — range + copies + collation | P0 |
| Обновить `PrintService.cs` — метод `Print()` без `PrintDialog`, через `PrintQueue.AddJob` | P0 |
| Обновить `ActionBarControl.xaml.cs` — `BtnPrintKp_Click` на новый путь | P0 |
| Удалить legacy: WebView2, HTML, `useNativePrint` флаг | P0 |
| Обновить тесты | P0 |
| Кнопка «Сохранить PDF» — disabled placeholder | P1 |

---

## 3. Функциональные требования

| # | Требование | Приоритет | Источник |
|---|---|---|---|
| **F1** | Единое окно: предпросмотр FlowDocumentReader + левая боковая панель настроек | P0 | R1 Q1 |
| **F2** | Выбор принтера через ComboBox прямо в боковой панели (список из `LocalPrintServer`) | P0 | R2 Q2 |
| **F3** | Кол-во копий: NumericUpDown без上限а, с предупреждением при >10 | P0 | R4 Q2 |
| **F4** | Разбивка по копиям (collation): CheckBox, включена по умолчанию | P0 | R1 Q3 |
| **F5** | При collation = on: печать 1,2,3, 1,2,3, ...; при off: 1,1, 2,2, 3,3, ... | P0 | Исходный запрос |
| **F6** | Цветность: ToggleButton (Цветная / Ч/б) | P1 | R2 Q3 |
| **F7** | Диапазон страниц: CheckBox «Все страницы» (default on) + поля «с _ по _» и «Страница _» | P0 | R3 Q1 |
| **F8** | Визуальная подсветка выбранного диапазона в превью (не входящие в диапазон — затемнены) | P0 | R4 Q3 |
| **F9** | Печать через `PrintQueue.AddJob` напрямую, без `PrintDialog` | P0 | R1 Q2 |
| **F10** | Прогресс отправки копий: ProgressBar + текст «Копия 2 из 5...» в боковой панели | P1 | R2 Q4 |
| **F11** | Последовательная отправка копий в очередь принтера | P0 | R2 Q3 |
| **F12** | Окно закрывается после отправки всех копий (прогресс завершён) | P0 | R3 Q3 |
| **F13** | Настройки печати сохраняются в памяти на время жизни заказа (пока MainWindow открыт) | P0 | R4 Q1 |
| **F14** | При повторном открытии окна для того же заказа — настройки восстанавливаются | P0 | R3 Q3 |
| **F15** | Боковая панель всегда видна (не сворачивается) | P0 | R3 Q2 |
| **F16** | Кнопка «Сохранить PDF» — disabled placeholder (на будущее) | P1 | R2 Q4 |
| **F17** | Кнопка «Закрыть» — закрывает окно без печати | P0 | — |
| **F18** | Пустой КП → toast «Добавьте хотя бы одну позицию», окно не открывается | P0 | Из v2.2 F8 |
| **F19** | Полное удаление WebView2, HTML-шаблона, legacy-кода, флага `useNativePrint` | P0 | R4 Q4 |
| **F20** | Обработка ошибок PrintQueue.AddJob: классификация исключений, retryable/non-retryable диалоги, Debug-логирование, UI-состояния до/во время/после ошибки | P0 | §8.7 |

### Анти-требования

| Решение | Обоснование |
|---|---|
| ❌ Не открываем PrintDialog | Полностью своё окно (R1 Q2) |
| ❌ Не оставляем HTML/WebView2 fallback | Полное удаление (R4 Q4) |
| ❌ Не делаем сворачиваемую боковую панель | Всегда видна (R3 Q2) |
| ❌ Не сохраняем настройки печати в JSON/файл | Только в памяти (R4 Q1) |

---

## 4. Архитектура

### 4.1 Общий флоу

```
ActionBarControl.BtnPrintKp_Click()
  → PrintService.BuildFlowDocument(items, clientInfo, total, amountInWords)
  → new PrintPreviewWindow(flowDocument, clientInfo, printSettings?)
     ├── [Печать] →
     │     PrintService.PrintDirect(document, settings) →
     │       CollatedPrintPaginator(
     │         PrintPaginator(FlowDocumentPaginator, contract, date),
     │         copies, collated, pageRange
     │       ) →
     │       PrintQueue.Current.AddJob("КП №...", paginator)
     │       (последовательно для каждой копии — с прогрессом)
     │     → окно закрывается
     ├── [Сохранить PDF] → disabled (placeholder)
     └── [Закрыть] → окно закрывается
```

### 4.2 Диаграмма классов

```
┌──────────────────────────────────────────────────────────────────┐
│              PrintPreviewWindow (ПОЛНОСТЬЮ ПЕРЕПИСАН)            │
│  ┌───────────────────────────┬────────────────────────────────┐ │
│  │  Левая боковая панель     │  FlowDocumentReader            │ │
│  │  ┌─────────────────────┐  │  ┌──────────────────────────┐ │ │
│  │  │ Принтер: [ComboBox] │  │  │                          │ │ │
│  │  │ Копии:  [ 1] [±]    │  │  │   Предпросмотр КП        │ │ │
│  │  │ ☑ Разбить по копиям │  │  │                          │ │ │
│  │  │ Цвет: [Цветная/ЧБ]  │  │  │   (FlowDocument)         │ │ │
│  │  │ ☑ Все страницы      │  │  │                          │ │ │
│  │  │   с [_] по [_]      │  │  │                          │ │ │
│  │  │   Страница [_]      │  │  │                          │ │ │
│  │  │                     │  │  │                          │ │ │
│  │  │ [████████░░░] 60%   │  │  │                          │ │ │
│  │  │ Копия 3 из 5...     │  │  │                          │ │ │
│  │  │                     │  │  │                          │ │ │
│  │  │ [   Печать   ]      │  │  │                          │ │ │
│  │  │ [ Сохранить PDF ]*  │  │  │                          │ │ │
│  │  │ [   Закрыть   ]     │  │  │                          │ │ │
│  │  └─────────────────────┘  │  └──────────────────────────┘ │ │
│  └───────────────────────────┴────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────┘
                      ▲
┌──────────────────────────────────────────────────────────────────┐
│  CollatedPrintPaginator : DocumentPaginator  (NEW)               │
│  Декоратор над PrintPaginator:                                    │
│    - Фильтрация страниц по диапазону                              │
│    - Размножение на N копий                                      │
│    - Collation ordering: 1,2,3,1,2,3 или 1,1,2,2,3,3           │
└──────────────────────────────────────────────────────────────────┘
                      ▲
┌──────────────────────────────────────────────────────────────────┐
│  PrintPaginator : DocumentPaginator  (существующий)              │
│  Header: «Договор № 1-5» (правый верх)                           │
│  Footer: «Страница X из Y» + дата (низ)                          │
└──────────────────────────────────────────────────────────────────┘
                      ▲
┌──────────────────────────────────────────────────────────────────┐
│  FlowDocument.DocumentPaginator  (встроенный WPF)                │
└──────────────────────────────────────────────────────────────────┘
```

### 4.3 PrintSettings (in-memory модель)

```csharp
/// <summary>
/// Настройки печати, сохраняемые в памяти на время жизни заказа.
/// Живут в MainWindowViewModel или MainWindow; НЕ сериализуются.
/// </summary>
internal class PrintSettings
{
    /// <summary>Имя выбранного принтера (null = принтер по умолчанию).</summary>
    public string? PrinterName { get; set; }

    /// <summary>Кол-во копий (≥ 1).</summary>
    public int Copies { get; set; } = 1;

    /// <summary>Разбивка по копиям (collation).</summary>
    public bool Collated { get; set; } = true;

    /// <summary>true = цветная, false = ч/б.</summary>
    public bool Color { get; set; } = true;

    /// <summary>true = печатать все страницы.</summary>
    public bool AllPages { get; set; } = true;

    /// <summary>Начальная страница диапазона (1-based, только при AllPages=false).</summary>
    public int PageFrom { get; set; } = 1;

    /// <summary>Конечная страница диапазона (1-based, только при AllPages=false).</summary>
    public int PageTo { get; set; } = 1;

    /// <summary>Отдельная страница (альтернатива диапазону, только при AllPages=false).</summary>
    public int? SinglePage { get; set; }

    /// <summary>Создаёт копию настроек (для snapshot при открытии окна).</summary>
    public PrintSettings Clone() => (PrintSettings)MemberwiseClone();
}
```

### 4.4 Место хранения настроек

```
MainWindow
  └── PrintSettings _lastPrintSettings = new();  // in-memory only
```

- При открытии `PrintPreviewWindow` — передаётся клон `_lastPrintSettings.Clone()`.
- При закрытии окна после печати — `_lastPrintSettings` обновляется из актуальных настроек окна.
- При новом заказе — сбрасывается в `new PrintSettings()`.
- Не сохраняется в JSON / %AppData% / OrderData.

---

## 5. CollatedPrintPaginator — детальное проектирование

### 5.1 Контракт

```csharp
/// <summary>
/// DocumentPaginator-декоратор, добавляющий:
/// 1. Фильтрацию по диапазону страниц (AllPages / from-to / single page).
/// 2. Размножение на N копий.
/// 3. Collation ordering:
///    - Collated=true:  1,2,...,M, 1,2,...,M, ... (N раз)
///    - Collated=false: 1,1,...,1, 2,2,...,2, ... (каждая страница N раз)
/// </summary>
internal sealed class CollatedPrintPaginator : DocumentPaginator
{
    private readonly DocumentPaginator _source;
    private readonly int _copies;
    private readonly bool _collated;
    private readonly int _pageFrom;   // 0-based, inclusive
    private readonly int _pageTo;     // 0-based, inclusive
    private readonly int _rangePageCount; // _pageTo - _pageFrom + 1

    public CollatedPrintPaginator(
        DocumentPaginator source,
        int copies,
        bool collated,
        int pageFrom,    // 1-based
        int pageTo       // 1-based, inclusive
    )
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _copies = Math.Max(1, copies);
        _collated = collated;
        _pageFrom = Math.Clamp(pageFrom - 1, 0, source.PageCount - 1);
        _pageTo = Math.Clamp(pageTo - 1, _pageFrom, source.PageCount - 1);
        _rangePageCount = _pageTo - _pageFrom + 1;

        // Force source pagination
        _source.ComputePageCount();
    }

    // Для single-page режима — удобный constructor
    public static CollatedPrintPaginator SinglePage(
        DocumentPaginator source, int copies, bool collated, int page)
    {
        return new CollatedPrintPaginator(source, copies, collated, page, page);
    }

    // Для AllPages режима
    public static CollatedPrintPaginator AllPages(
        DocumentPaginator source, int copies, bool collated)
    {
        return new CollatedPrintPaginator(source, copies, collated, 1, source.PageCount);
    }

    // ── DocumentPaginator contract ───────────────────────
    public override Size PageSize
    {
        get => _source.PageSize;
        set => _source.PageSize = value;
    }

    public override bool IsPageCountValid => true;
    public override int PageCount => _rangePageCount * _copies;
    public override IDocumentPaginatorSource Source => _source.Source;

    public override DocumentPage GetPage(int pageNumber)
    {
        // pageNumber: 0 .. PageCount-1 (in our virtual space)
        int sourceIndex;
        if (_collated)
        {
            // Collated: 0→p0, 1→p1, ..., M-1→p(M-1), M→p0, M+1→p1, ...
            sourceIndex = _pageFrom + (pageNumber % _rangePageCount);
        }
        else
        {
            // Non-collated: 0→p0, 1→p0, ..., C-1→p0, C→p1, C+1→p1, ...
            sourceIndex = _pageFrom + (pageNumber / _copies);
        }

        return _source.GetPage(sourceIndex);
    }
}
```

### 5.2 Пример: 5 копий, 3 страницы, collated

```
CollatedPaginator.PageCount = 3 × 5 = 15

GetPage(0)  → source.GetPage(0)   // стр.1, копия 1
GetPage(1)  → source.GetPage(1)   // стр.2, копия 1
GetPage(2)  → source.GetPage(2)   // стр.3, копия 1
GetPage(3)  → source.GetPage(0)   // стр.1, копия 2
GetPage(4)  → source.GetPage(1)   // стр.2, копия 2
...
GetPage(12) → source.GetPage(0)   // стр.1, копия 5
GetPage(13) → source.GetPage(1)   // стр.2, копия 5
GetPage(14) → source.GetPage(2)   // стр.3, копия 5
```

### 5.3 Пример: 5 копий, 3 страницы, НЕ collated

```
GetPage(0)  → source.GetPage(0)   // стр.1, копия 1
GetPage(1)  → source.GetPage(0)   // стр.1, копия 2
GetPage(2)  → source.GetPage(0)   // стр.1, копия 3
GetPage(3)  → source.GetPage(0)   // стр.1, копия 4
GetPage(4)  → source.GetPage(0)   // стр.1, копия 5
GetPage(5)  → source.GetPage(1)   // стр.2, копия 1
...
GetPage(14) → source.GetPage(2)   // стр.3, копия 5
```

---

## 6. Печать без PrintDialog (PrintQueue.AddJob)

### 6.1 Обновлённый PrintService.Print()

```csharp
// PrintService.cs — новый метод
public async Task PrintDirect(
    FlowDocument document,
    PrintSettings settings,
    string contractNumber,
    IProgress<(int copy, int total)>? progress = null)
{
    var sourcePaginator = ((IDocumentPaginatorSource)document).DocumentPaginator;

    // Step 1: Add headers/footers
    var headerPaginator = new PrintPaginator(sourcePaginator, contractNumber, DateTime.Now);

    // Step 2: Apply range + copies + collation
    CollatedPrintPaginator collatedPaginator = settings.AllPages
        ? CollatedPrintPaginator.AllPages(headerPaginator, settings.Copies, settings.Collated)
        : settings.SinglePage.HasValue
            ? CollatedPrintPaginator.SinglePage(headerPaginator, settings.Copies, settings.Collated, settings.SinglePage.Value)
            : new CollatedPrintPaginator(headerPaginator, settings.Copies, settings.Collated, settings.PageFrom, settings.PageTo);

    // Step 3: Get printer queue
    var server = new LocalPrintServer();
    var queue = string.IsNullOrEmpty(settings.PrinterName)
        ? server.DefaultPrintQueue
        : server.GetPrintQueue(settings.PrinterName);
    // Fallback to default if named printer not found
    queue ??= server.DefaultPrintQueue;

    // Step 4: Configure PrintTicket (color/BW)
    var ticket = queue.DefaultPrintTicket.Clone();
    ticket.OutputColor = settings.Color
        ? OutputColor.Color
        : OutputColor.Monochrome;

    // Step 5: Send to queue
    string jobName = string.IsNullOrWhiteSpace(contractNumber)
        ? "КП"
        : $"КП №{contractNumber.Trim()}";

    // Use PrintQueue.AddJob — синхронно, но с прогрессом через collatedPaginator
    queue.AddJob(jobName, collatedPaginator, ticket);
}
```

**Важно:** `PrintQueue.AddJob` с `DocumentPaginator` уже отправляет ВСЕ страницы пагинатора как одно задание. Копии уже учтены в `CollatedPrintPaginator.PageCount`. Поэтому **одно** `AddJob` отправляет все копии сразу. Прогресс в UI — чисто косметический (ProgressBar заполняется после отправки, с небольшой задержкой для плавности).

Однако, если мы хотим *реальный* прогресс и возможность отмены между копиями, нужно отправлять копии по одной через `PrintQueue.AddJob` для каждой:

```csharp
// Альтернатива: N отдельных заданий для прогресса
for (int copy = 0; copy < settings.Copies; copy++)
{
    progress?.Report((copy + 1, settings.Copies));

    // Создаём paginator для одной копии
    var singleCopyPaginator = CreateSingleCopyPaginator(headerPaginator, settings, copy);
    queue.AddJob($"{jobName} (копия {copy + 1})", singleCopyPaginator, ticket);
}
```

**Решение:** использовать вариант с одним `AddJob` (все копии в одном задании — `CollatedPrintPaginator` сам считает `PageCount = rangePageCount × copies`). Прогресс-бар заполняется плавно после отправки задания в очередь (через `async Task.Delay` или `ProgressBarUpdateAnimator`). Это надёжнее, чем несколько заданий.

### 6.2 System.Printing reference

Нужно добавить reference на `System.Printing` в `.csproj` (уже может быть для `PrintPaginator`):

```xml
<Reference Include="System.Printing" />
```

И using:
```csharp
using System.Printing;
```

---

## 7. Выбор принтера (ComboBox)

### 7.1 Заполнение списка

```csharp
private void PopulatePrinterList()
{
    using var server = new LocalPrintServer();
    var queues = server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections });

    PrinterCombo.Items.Clear();
    foreach (var q in queues.OrderBy(q => q.Name))
    {
        PrinterCombo.Items.Add(q.FullName);
    }

    // Select default (or previously selected)
    string target = _settings.PrinterName ?? server.DefaultPrintQueue?.FullName ?? "";
    if (!string.IsNullOrEmpty(target) && PrinterCombo.Items.Contains(target))
        PrinterCombo.SelectedItem = target;
    else if (PrinterCombo.Items.Count > 0)
        PrinterCombo.SelectedIndex = 0;
}
```

### 7.2 Обновление списка

Список принтеров обновляется при открытии окна. Не обновляется динамически (нет смысла — пользователь не подключает принтеры во время предпросмотра).

---

## 8. Боковая панель — дизайн и поведение

### 8.1 Структура (сверху вниз)

```
┌─────────────────────────┐
│ ПРИНТЕР                 │   ← Section label
│ [Выпадающий список    ▾]│   ← ComboBox (все установленные принтеры)
│                         │
│ КОПИИ                   │   ← Section label
│ [ 1] [-] [+]            │   ← NumericUpDown + кнопки ±
│                         │
│ ☑ Разбить по копиям     │   ← CheckBox (включён по умолчанию)
│                         │
│ ЦВЕТ                    │   ← Section label
│ [Цветная | Ч/б]         │   ← ToggleButton (segmented, как AnwisSizeMode)
│                         │
│ СТРАНИЦЫ                │   ← Section label
│ ☑ Все страницы          │   ← CheckBox (включён по умолчанию)
│   с [_] по [_]          │   ← Два TextBox/NumericUpDown (disabled при AllPages)
│   или                   │
│   Страница [_]          │   ← TextBox/NumericUpDown (disabled при AllPages)
│                         │
│ ─────────────────────── │
│ [████████░░░] 60%       │   ← ProgressBar (скрыт до печати)
│ Копия 3 из 5...         │   ← TextBlock (скрыт до печати)
│ ─────────────────────── │
│                         │
│ [   Печать   ]          │   ← Primary Button
│ [ Сохранить PDF ]*      │   ← Disabled GhostButton (на будущее)
│ [   Закрыть   ]         │   ← GhostButton
└─────────────────────────┘
```

### 8.2 Стили

- Фон панели: `Surface` (из темы)
- Ширина панели: ~240px (фиксированная, не сворачивается)
- Разделитель между панелью и превью: `Border` 1px, цвет `Border`
- Кнопки: существующие стили (`PrimaryButton`, `GhostButton`)

### 8.3 Валидация копий (>10)

При нажатии «Печать»:
```csharp
if (_settings.Copies > 10)
{
    var result = MessageBox.Show(
        $"Количество копий ({_settings.Copies}) больше 10. Вы точно хотите распечатать?",
        "Подтверждение",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question);
    if (result != MessageBoxResult.Yes)
        return;
}
```

### 8.4 Прогресс печати

После отправки задания в очередь:
```csharp
// Показываем прогресс (косметический — задание уже в очереди)
PrintButton.IsEnabled = false;
ProgressBar.Visibility = Visibility.Visible;
ProgressText.Visibility = Visibility.Visible;

for (int i = 1; i <= _settings.Copies; i++)
{
    ProgressBar.Value = (double)i / _settings.Copies * 100;
    ProgressText.Text = $"Копия {i} из {_settings.Copies}...";
    await Task.Delay(200); // плавная анимация
}

// Закрываем окно
_savedSettings = _settings.Clone();
Close();
```

**Причина косметического прогресса:** `PrintQueue.AddJob` с `DocumentPaginator` отправляет всё задание сразу. Реальный прогресс печати отслеживать сложно (нужен polling `PrintJob.IsCompleted`). Для UX достаточно показать плавное заполнение.

### 8.5 Подсветка диапазона страниц в превью — визуальное затемнение

Когда `AllPages = false`, FlowDocumentReader показывает ВСЕ страницы документа, но страницы **вне выбранного диапазона** визуально затемняются полупрозрачным оверлеем с поясняющим текстом. Страницы внутри диапазона показываются без затемнения.

#### 8.5.1 Архитектурное решение

FlowDocumentReader управляет своим внутренним визуальным деревом — **нельзя** инжектировать оверлей внутрь отдельных страниц. Вместо этого используется **внешний overlay** (полупрозрачный `Border`) поверх всего контрола, размещённый в той же ячейке `Grid`:

```
┌─────────────────────────────────────────┐
│  Grid (Column 2)                        │
│  ┌─────────────────────────────────────┐│
│  │  FlowDocumentReader  (Z=0)         ││
│  │  ┌───────────────────────────────┐  ││
│  │  │  Стр. 5 (вне диапазона 2-4)  │  ││
│  │  │  ░░░░░░░░░░░░░░░░░░░░░░░░░░░  │  ││
│  │  │  (затемнена)                  │  ││
│  │  └───────────────────────────────┘  ││
│  └─────────────────────────────────────┘│
│  ┌─────────────────────────────────────┐│
│  │  DimmingOverlay Border (Z=1)        ││
│  │  Visibility = Visible/Collapsed     ││
│  │  Background = #99FFFFFF (60%)      ││  ← полупрозрачный
│  │  IsHitTestVisible = False           ││  ← не блокирует scroll/zoom
│  │  ┌───────────────────────────────┐  ││
│  │  │  TextBlock (по центру)        │  ││
│  │  │  "Стр. 5 не входит в          │  ││
│  │  │   диапазон печати (2–4)"      │  ││
│  │  └───────────────────────────────┘  ││
│  └─────────────────────────────────────┘│
└─────────────────────────────────────────┘
```

#### 8.5.2 Как узнать текущую страницу

`FlowDocumentReader` не имеет публичного свойства `PageNumber` или события `PageChanged`. При `ViewingMode="Page"` внутри он использует `FlowDocumentPageViewer` (наследник `DocumentViewerBase`), у которого есть `MasterPageNumber`.

**Получение внутреннего viewer'а через VisualTreeHelper:**

```csharp
private FlowDocumentPageViewer? _innerViewer;

private void Window_Loaded(object sender, RoutedEventArgs e)
{
    _innerViewer = FindVisualChild<FlowDocumentPageViewer>(DocumentReader);
    if (_innerViewer != null)
    {
        // Подписываемся на изменения MasterPageNumber через DependencyPropertyDescriptor
        var dpd = DependencyPropertyDescriptor.FromProperty(
            FlowDocumentPageViewer.MasterPageNumberProperty,
            typeof(FlowDocumentPageViewer));
        dpd.AddValueChanged(_innerViewer, (s, args) => UpdateDimmingOverlay());
    }
    // Первичное обновление
    UpdateDimmingOverlay();
}

// Рекурсивный поиск визуального потомка заданного типа
private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
{
    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
    {
        var child = VisualTreeHelper.GetChild(parent, i);
        if (child is T typed) return typed;
        var deep = FindVisualChild<T>(child);
        if (deep != null) return deep;
    }
    return null;
}
```

**Почему `DependencyPropertyDescriptor`, а не `PropertyChanged`?**
- `MasterPageNumber` — это dependency property. У `FlowDocumentPageViewer` нет явного `PropertyChanged` события.
- `DependencyPropertyDescriptor.AddValueChanged` — стандартный WPF-паттерн для подписки на изменения DP.
- `PageViewsChanged` (событие `DocumentViewerBase`) тоже работает, но оно содержит детали о добавлении/удалении страничных view'ов, что избыточно.

#### 8.5.3 Логика затемнения

Метод `UpdateDimmingOverlay()` вызывается в двух случаях:
1. При навигации по страницам (`MasterPageNumber` изменился).
2. При изменении настроек диапазона в боковой панели (`AllPages`, `PageFrom`, `PageTo`, `SinglePage`).

```csharp
/// <summary>
/// Обновляет видимость затемняющего оверлея в зависимости от того,
/// находится ли текущая страница в выбранном диапазоне печати.
/// Вызывается при навигации по страницам И при изменении настроек.
/// </summary>
private void UpdateDimmingOverlay()
{
    if (_innerViewer == null || _innerViewer.PageCount == 0)
    {
        // Документ ещё не пагинирован — оверлей скрыт
        DimmingOverlay.Visibility = Visibility.Collapsed;
        return;
    }

    int totalPages = _innerViewer.PageCount;
    int currentPage = _innerViewer.MasterPageNumber;

    // Определяем диапазон печати для текста подсказки
    string rangeText;
    bool isOutOfRange;

    if (AllPagesCheck.IsChecked == true)
    {
        // Все страницы — затемнения нет
        isOutOfRange = false;
        rangeText = "";
    }
    else if (int.TryParse(SinglePageBox.Text, out int sp) && sp > 0)
    {
        // Режим «Страница N»
        isOutOfRange = (currentPage != sp);
        rangeText = $"({sp})";
    }
    else if (int.TryParse(PageFromBox.Text, out int from) &&
             int.TryParse(PageToBox.Text, out int to))
    {
        // Режим диапазона «с–по»
        // Нормализуем (если пользователь перепутал from/to — свапаем)
        int f = Math.Min(from, to);
        int t = Math.Max(from, to);
        isOutOfRange = (currentPage < f || currentPage > t);
        rangeText = f == t ? $"({f})" : $"({f}–{t})";
    }
    else
    {
        // Поля диапазона пусты или некорректны — считаем «все страницы»
        isOutOfRange = false;
        rangeText = "";
    }

    if (isOutOfRange)
    {
        DimmingText.Text = $"Стр. {currentPage} не входит в диапазон печати {rangeText}";
        DimmingOverlay.Visibility = Visibility.Visible;
    }
    else
    {
        DimmingOverlay.Visibility = Visibility.Collapsed;
    }

    // Обновляем текстовую подсказку в боковой панели (range hint)
    RangeHint.Text = AllPagesCheck.IsChecked == true
        ? $"Все страницы ({totalPages} стр.)"
        : $"На печать: {rangeText} ({GetPageCountForRange()} стр. из {totalPages})";
}

private int GetPageCountForRange()
{
    if (AllPagesCheck.IsChecked == true) return _innerViewer?.PageCount ?? 0;
    if (int.TryParse(SinglePageBox.Text, out int sp) && sp > 0) return 1;
    if (int.TryParse(PageFromBox.Text, out int from) && int.TryParse(PageToBox.Text, out int to))
        return Math.Max(0, Math.Max(from, to) - Math.Min(from, to) + 1);
    return _innerViewer?.PageCount ?? 0;
}
```

#### 8.5.4 Точки вызова UpdateDimmingOverlay()

| Событие | Где подписываемся |
|---|---|
| Изменение страницы (пользователь скроллит) | `DependencyPropertyDescriptor.AddValueChanged` на `MasterPageNumberProperty` (в `Window_Loaded`) |
| Изменение `AllPagesCheck` | `Checked` и `Unchecked` обработчики |
| Изменение `PageFromBox.Text` | `TextChanged` (с debounce 200ms через `DispatcherTimer` для избежания лага при наборе) |
| Изменение `PageToBox.Text` | `TextChanged` (same debounce) |
| Изменение `SinglePageBox.Text` | `TextChanged` (same debounce) |

**Debounce для TextChanged:** при быстром наборе (например, «15») не нужно дёргать UpdateDimmingOverlay на каждом символе. Используется таймер на 200ms:

```csharp
private readonly DispatcherTimer _textChangedDebounce = new()
{
    Interval = TimeSpan.FromMilliseconds(200)
};

// В конструкторе:
_textChangedDebounce.Tick += (s, e) =>
{
    _textChangedDebounce.Stop();
    UpdateDimmingOverlay();
};

private void RangeField_TextChanged(object sender, TextChangedEventArgs e)
{
    _textChangedDebounce.Stop();        // сбрасываем предыдущий таймер
    _textChangedDebounce.Start();        // запускаем заново
}
```

#### 8.5.5 Визуальное оформление оверлея

- **Фон:** `#99FFFFFF` — белый с ~60% непрозрачности (достаточно чтобы прочитать текст документа, но явно видно что страница «неактивна»).
- **Текст:** по центру оверлея, крупный (24pt), серый (`#666666`), `FontWeight=Bold`.
- **Формат текста:** `"Стр. {N} не входит в диапазон печати ({from}–{to})"` или `"Стр. {N} не входит в диапазон печати ({single})"`.
- **`IsHitTestVisible="False"`** — обязательно, иначе оверлей перехватит клики/скролл и FlowDocumentReader перестанет реагировать на пользователя.
- **Fade-анимация (опционально, v1):** можно добавить `Opacity` анимацию 0→1 за 150ms при появлении оверлея. Без анимации — просто `Visibility` toggle.

#### 8.5.6 Граничные случаи

| Сценарий | Поведение |
|---|---|
| Документ ещё не пагинирован (`PageCount == 0`) | Оверлей скрыт |
| `_innerViewer` не найден (теоретически невозможно при ViewingMode=Page) | Оверлей скрыт, graceful degradation |
| Пользователь ввёл «200» в PageFrom при 5 страницах | `isOutOfRange` = true для всех страниц (всегда затемнено). Это корректно — подсказывает пользователю что диапазон некорректен. |
| Пользователь ввёл буквы вместо цифр | `int.TryParse` → false → fallback на «все страницы» (оверлей скрыт). |
| `SinglePageBox` И `PageFrom/PageTo` заполнены одновременно | Приоритет у `SinglePageBox` (проверяется первым). |
| AllPages выключен, но поля пусты | Fallback на «все страницы» (оверлей скрыт, range hint показывает «Все»). |
| Пользователь быстро листает страницы (PgDown зажат) | `AddValueChanged` вызывается на каждое изменение → оверлей мигает. Debounce НЕ применяется к page changes (нужна мгновенная реакция). Это нормально — overlay очень лёгкий (всего один Border + TextBlock). |

### 8.6 NumericUpDown для копий — детальный дизайн

Поле ввода количества копий реализовано как отдельный `UserControl` (`NumericUpDownControl`), чтобы инкапсулировать всю логику валидации и поведения кнопок.

#### 8.6.1 Компоненты

```
┌──────────────────────────────────────┐
│  Grid (3 колонки: 28, *, 28)        │
│  ┌──────┬──────────────────┬──────┐ │
│  │  −   │       1          │  +   │ │
│  │ 28×28│    TextBox       │ 28×28│ │
│  │Repeat│   TextAlignment   │Repeat│ │
│  │Button│    = Center       │Button│ │
│  └──────┴──────────────────┴──────┘ │
│   Margin между TextBox и кнопками:  │
│   TextBox.Margin = "8,0"           │
│   Общая высота: 32px                │
└──────────────────────────────────────┘
```

#### 8.6.2 RepeatButton (кнопки ±)

Кнопки «−» и «+» используют `RepeatButton` — нативный WPF-контрол, который автоматически поддерживает **hold-to-repeat** (зажатие → быстрая прокрутка):

- **`Delay` = 300 ms** — пауза перед началом повтора (как в стандартном ScrollBar).
- **`Interval` = 50 ms** — интервал между повторными нажатиями (20 нажатий/сек).
- **Стиль:** повторяет `GhostButton` из темы приложения, адаптированный под `RepeatButton` (см. §8.6.4).
- **Размер:** 28×28 px (квадратные).
- **Контент:** `−` (U+2212, минус) и `+` (U+002B, плюс).

**Почему RepeatButton, а не Button + DispatcherTimer?**
- `RepeatButton` — встроенный WPF-контрол, специально спроектированный для hold-to-repeat.
- Не требует ручного управления таймерами, очистки при `MouseUp`, защиты от утечек.
- `Delay` и `Interval` настраиваются декларативно в XAML.

#### 8.6.3 TextBox — валидация ввода

TextBox в центре принимает только цифры. Используется **несколько уровней защиты**:

**Уровень 1 — `PreviewTextInput` (посимвольный фильтр):**
```csharp
private static readonly Regex _digitsOnly = new Regex("[^0-9]+");

private void ValueTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
{
    // Блокируем любой не-цифровой символ на этапе ввода
    e.Handled = _digitsOnly.IsMatch(e.Text);
}
```

**Уровень 2 — `DataObject.Pasting` (защита от вставки):**
```csharp
private void ValueTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
{
    if (e.DataObject.GetDataPresent(typeof(string)))
    {
        string text = (string)e.DataObject.GetData(typeof(string));
        if (_digitsOnly.IsMatch(text))
            e.CancelCommand();  // Отменяем paste с не-цифрами
    }
    else
        e.CancelCommand();  // Отменяем paste не-строковых данных
}
```

**Уровень 3 — `LostFocus` (восстановление при пустом/некорректном вводе):**
```csharp
private void ValueTextBox_LostFocus(object sender, RoutedEventArgs e)
{
    // Если пользователь стёр всё и ушёл — возвращаем последнее валидное значение
    if (string.IsNullOrWhiteSpace(ValueTextBox.Text) ||
        !int.TryParse(ValueTextBox.Text, out int result) ||
        result < 1)
    {
        UpdateTextBox(Value);  // Value = последнее валидное значение из DP
    }
}
```

**Уровень 4 — `TextChanged` (обновление связанного свойства):**
```csharp
private void ValueTextBox_TextChanged(object sender, TextChangedEventArgs e)
{
    if (_isUpdatingText) return;  // Защита от рекурсии (см. ниже)
    if (int.TryParse(ValueTextBox.Text, out int result) && result >= 1)
        Value = result;
}
```

**Защита от рекурсии `_isUpdatingText`:**
Когда `Value` DependencyProperty меняется программно (например, через кнопку ±), `OnValueChanged` обновляет `TextBox.Text`. Без флага `_isUpdatingText` это вызвало бы `TextChanged` → обратную запись в `Value` → потенциальную рекурсию.

#### 8.6.4 Стиль RepeatButton (GhostRepeatButton)

Стиль является точной копией `GhostButton` из `ButtonStyles.xaml`, но таргетирует `RepeatButton`:

```xml
<Style x:Key="GhostRepeatButton" TargetType="RepeatButton">
    <Setter Property="Background" Value="{DynamicResource GhostBg}"/>
    <Setter Property="Foreground" Value="{DynamicResource TextPrimary}"/>
    <Setter Property="FontSize" Value="14"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="BorderBrush" Value="{DynamicResource GhostBorder}"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Delay" Value="300"/>
    <Setter Property="Interval" Value="50"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="RepeatButton">
                <Border x:Name="Bd"
                        Background="{TemplateBinding Background}"
                        CornerRadius="7"
                        BorderThickness="{TemplateBinding BorderThickness}"
                        BorderBrush="{TemplateBinding BorderBrush}"
                        SnapsToDevicePixels="True">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="Bd" Property="Background" Value="{DynamicResource AccentLight}"/>
                        <Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource Accent}"/>
                        <Setter Property="Foreground" Value="{DynamicResource Accent}"/>
                    </Trigger>
                    <Trigger Property="IsPressed" Value="True">
                        <Setter TargetName="Bd" Property="Background" Value="{DynamicResource AccentLight}"/>
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                        <Setter Property="Opacity" Value="0.5"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

**Почему отдельный стиль, а не `BasedOn GhostButton`?**
WPF не разрешает `BasedOn` между разными `TargetType`. Поэтому стиль дублирует визуальные свойства GhostButton, но таргетирует `RepeatButton`.

#### 8.6.5 DependencyProperty `Value`

```csharp
public static readonly DependencyProperty ValueProperty =
    DependencyProperty.Register(
        nameof(Value),
        typeof(int),
        typeof(NumericUpDownControl),
        new FrameworkPropertyMetadata(
            1,                                          // default = 1
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnValueChanged));

public int Value
{
    get => (int)GetValue(ValueProperty);
    set => SetValue(ValueProperty, value);
}

private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
{
    if (d is NumericUpDownControl control)
        control.UpdateTextBox((int)e.NewValue);
}

private void UpdateTextBox(int val)
{
    if (val < 1) val = 1;  // clamp to min
    _isUpdatingText = true;
    ValueTextBox.Text = val.ToString();
    _isUpdatingText = false;
}
```

**Особенности:**
- `FrameworkPropertyMetadataOptions.BindsTwoWayByDefault` — двухсторонний binding «из коробки».
- Clamp к 1 в `UpdateTextBox` — даже если кто-то программно установит 0 или -5, TextBox покажет 1.
- `OnValueChanged` static → вызывает экземплярный `UpdateTextBox` через cast.

#### 8.6.6 Поведение кнопок ±

```csharp
private void Minus_Click(object sender, RoutedEventArgs e)
{
    if (Value > 1)
        Value--;
}

private void Plus_Click(object sender, RoutedEventArgs e)
{
    Value++;
}
```

- **Минимум = 1:** кнопка «−» не уменьшает ниже 1.
- **Максимум:** отсутствует. При очень больших значениях (>10) срабатывает предупреждение в `Print_Click` (см. §8.3), но NumericUpDown сам не ограничивает.
- **Hold-to-repeat:** при зажатии «+» на 2 секунды значение увеличится примерно на: `(2000 − 300) / 50 ≈ 34` единицы. Для поля копий (обычно 1–10) это займёт доли секунды.

#### 8.6.7 Интеграция в PrintPreviewWindow

**XAML (Namespace):**
```xml
xmlns:controls="clr-namespace:MosquitoNetCalculator.Controls"
```

**XAML (использование — заменяет старый Grid с TextBox и кнопками):**
```xml
<!-- Копии -->
<TextBlock Text="КОПИИ" Style="{StaticResource SectionLabel}"/>
<controls:NumericUpDownControl x:Name="CopiesControl"
                               Margin="0,4,0,0"
                               Height="32"
                               Value="1"/>
```

**Code-behind (чтение значения при печати):**
```csharp
_settings.Copies = CopiesControl.Value;
```

**Code-behind (сброс к настройкам из памяти при открытии окна):**
```csharp
// В конструкторе PrintPreviewWindow, после InitializeComponent():
CopiesControl.Value = _settings.Copies;
CollatedCheck.IsChecked = _settings.Collated;
// ... и т.д. для остальных полей
```

#### 8.6.8 Граничные случаи NumericUpDown

| Сценарий | Поведение |
|---|---|
| Пользователь вводит «0» | Допускается в процессе набора (например, «10» начинается с «1» → «10»). При `LostFocus` с чистым «0» → сброс к последнему валидному значению. |
| Пользователь вводит буквы | `PreviewTextInput` блокирует — символы не появляются в TextBox. |
| Пользователь вставляет «abc123» | `Pasting` handler — вставка отменяется (содержит буквы). |
| Пользователь вставляет «456» | Допускается — только цифры. `TextChanged` обновляет `Value`. |
| Пользователь стирает всё и уходит | `LostFocus` → сброс к последнему валидному `Value`. |
| Пользователь зажимает «+» | `RepeatButton` авто-повтор: после 300ms паузы, каждые 50ms +1. |
| Пользователь зажимает «−» | Останавливается на 1 (условие `Value > 1`). |
| Программная установка `Value = -5` | `UpdateTextBox` зажимает к 1. TextBox показывает «1». |
| Программная установка `Value = 999999` | Допускается. При печати сработает предупреждение >10. |
| Binding из PrintSettings.Copies (по умолчанию = 1) | При старте TextBox показывает «1». |

### 8.7 Стратегия обработки ошибок печати (PrintQueue.AddJob)

При прямой печати через `PrintQueue.AddJob` без `PrintDialog` приложение **само** отвечает за обработку всех исключений. Системный `PrintDialog` внутри себя ловит и показывает стандартные диалоги ошибок — мы должны реализовать эквивалент.

#### 8.7.1 Таксономия исключений System.Printing

| Исключение | Причина | Retry? | Тип ошибки |
|---|---|---|---|
| `PrintQueueException` | Принтер offline, нет бумаги, toner low, замятие, дверца открыта | ✅ Да (после устранения) | Printer |
| `PrintSystemException` | Спулер печати остановлен, очередь недоступна, повреждены драйверы | ❌ Нет (системная проблема) | System |
| `UnauthorizedAccessException` | Нет прав на печать на выбранный принтер (сетевой принтер, политики) | ❌ Нет (права) | Access |
| `InvalidOperationException` | Очередь в недопустимом состоянии (paused + deleted одновременно, etc.) | ❌ Нет | Queue |
| `ArgumentException` / `ArgumentNullException` | Баг: paginator = null, пустое имя задания | ❌ Нет (наша ошибка) | Bug |

#### 8.7.2 Уровни обработки

Обработка ошибок реализуется на **трёх уровнях**:

**Уровень 1 — PrintService.PrintDirect() (бизнес-логика):**

Метод `PrintDirect` в `PrintService.cs` оборачивает `queue.AddJob(...)` в try/catch, классифицирует исключение и возвращает результат вызывающему коду через `PrintResult`:

```csharp
/// <summary>Результат попытки отправки задания в очередь печати.</summary>
public enum PrintResultType
{
    Success,
    PrinterOffline,
    PrinterOutOfPaper,
    PrinterTonerLow,
    PrinterError,          // прочие ошибки принтера
    SpoolerStopped,         // спулер остановлен
    AccessDenied,           // нет прав
    QueueError,             // очередь в недопустимом состоянии
    Unknown                 // неклассифицированная ошибка
}

public readonly struct PrintResult
{
    public PrintResultType Type { get; init; }
    public string UserMessage { get; init; }    // для пользователя
    public string? DebugMessage { get; init; }  // для лога
    public bool IsRetryable { get; init; }

    public static PrintResult Ok() => new() { Type = PrintResultType.Success };
}

// В PrintService.cs:
public static PrintResult SendToQueue(
    PrintQueue queue,
    string jobName,
    DocumentPaginator paginator,
    PrintTicket ticket)
{
    try
    {
        queue.AddJob(jobName, paginator, ticket);
        return PrintResult.Ok();
    }
    catch (PrintQueueException pqEx)
    {
        var msg = pqEx.Message.ToLowerInvariant();
        var (type, userMsg) = (msg) switch
        {
            var m when m.Contains("offline") || m.Contains("отключ") =>
                (PrintResultType.PrinterOffline,
                 $"Принтер «{queue.Name}» не подключён или выключен."),
            var m when m.Contains("paper") || m.Contains("бумаг") =>
                (PrintResultType.PrinterOutOfPaper,
                 $"В принтере «{queue.Name}» закончилась бумага."),
            var m when m.Contains("toner") || m.Contains("тонер") || m.Contains("чернил") =>
                (PrintResultType.PrinterTonerLow,
                 $"В принтере «{queue.Name}» низкий уровень тонера/чернил."),
            _ =>
                (PrintResultType.PrinterError,
                 $"Ошибка принтера «{queue.Name}»: {pqEx.Message}")
        };
        Debug.WriteLine($"[PrintService] PrintQueueException ({type}): {pqEx.Message}");
        return new PrintResult
        {
            Type = type,
            UserMessage = userMsg,
            DebugMessage = pqEx.ToString(),
            IsRetryable = true
        };
    }
    catch (PrintSystemException psEx)
    {
        Debug.WriteLine($"[PrintService] PrintSystemException: {psEx.Message}");
        return new PrintResult
        {
            Type = PrintResultType.SpoolerStopped,
            UserMessage = "Служба печати Windows остановлена или недоступна. " +
                          "Проверьте, запущен ли «Диспетчер очереди печати» (services.msc).",
            DebugMessage = psEx.ToString(),
            IsRetryable = false
        };
    }
    catch (UnauthorizedAccessException uaEx)
    {
        Debug.WriteLine($"[PrintService] UnauthorizedAccess: {uaEx.Message}");
        return new PrintResult
        {
            Type = PrintResultType.AccessDenied,
            UserMessage = $"Нет доступа к принтеру «{queue.Name}». " +
                          "Обратитесь к системному администратору.",
            DebugMessage = uaEx.ToString(),
            IsRetryable = false
        };
    }
    catch (InvalidOperationException ioEx)
    {
        Debug.WriteLine($"[PrintService] InvalidOperation: {ioEx.Message}");
        return new PrintResult
        {
            Type = PrintResultType.QueueError,
            UserMessage = $"Очередь печати «{queue.Name}» в недопустимом состоянии. " +
                          "Попробуйте перезапустить очередь печати.",
            DebugMessage = ioEx.ToString(),
            IsRetryable = false
        };
    }
    catch (Exception ex) // неожиданное
    {
        Debug.WriteLine($"[PrintService] Unexpected: {ex}");
        return new PrintResult
        {
            Type = PrintResultType.Unknown,
            UserMessage = $"Неожиданная ошибка печати: {ex.Message}",
            DebugMessage = ex.ToString(),
            IsRetryable = false
        };
    }
}
```

**Уровень 2 — PrintPreviewWindow (UI-реакция):**

Code-behind окна предпросмотра получает `PrintResult` и решает, как показать ошибку пользователю:

```csharp
private async void Print_Click(object sender, RoutedEventArgs e)
{
    // ... валидация копий > 10 (см. §8.3) ...

    // Собираем настройки
    _settings.Copies = CopiesControl.Value;
    _settings.Collated = CollatedCheck.IsChecked == true;
    // ... остальные поля ...

    PrintButton.IsEnabled = false;
    ProgressBar.Visibility = Visibility.Visible;
    ProgressText.Visibility = Visibility.Visible;

    try
    {
        var result = PrintService.SendToQueue(queue, jobName, paginator, ticket);

        if (result.Type == PrintResultType.Success)
        {
            // Успех — показываем прогресс и закрываем (см. §8.4)
            await SimulateProgressAsync(_settings.Copies);
            _savedSettings = _settings.Clone();
            Close();
        }
        else if (result.IsRetryable)
        {
            // Retryable ошибка — диалог с вариантами
            var dlgResult = MessageBox.Show(
                result.UserMessage + "\n\nПовторить отправку на печать?",
                "Ошибка принтера",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (dlgResult == MessageBoxResult.Yes)
            {
                // Возвращаем кнопку и пробуем снова
                PrintButton.IsEnabled = true;
                ProgressBar.Visibility = Visibility.Collapsed;
                ProgressText.Visibility = Visibility.Collapsed;
                return;  // пользователь жмёт «Печать» снова
            }
            // No → окно остаётся открытым (можно закрыть вручную)
            PrintButton.IsEnabled = true;
            ProgressBar.Visibility = Visibility.Collapsed;
            ProgressText.Visibility = Visibility.Collapsed;
        }
        else
        {
            // Не-retryable ошибка — MessageBox + окно остаётся открытым
            MessageBox.Show(
                result.UserMessage,
                "Ошибка печати",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            PrintButton.IsEnabled = true;
            ProgressBar.Visibility = Visibility.Collapsed;
            ProgressText.Visibility = Visibility.Collapsed;
        }
    }
    catch (Exception ex)
    {
        // Защита от неожиданных исключений в самом try-блоке
        Debug.WriteLine($"[PrintPreviewWindow] Print_Click unexpected: {ex}");
        MessageBox.Show(
            $"Неожиданная ошибка: {ex.Message}",
            "Ошибка",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        PrintButton.IsEnabled = true;
        ProgressBar.Visibility = Visibility.Collapsed;
        ProgressText.Visibility = Visibility.Collapsed;
    }
}
```

**Уровень 3 — Глобальный лог (Debug + Trace):**

Все ошибки печати пишутся в `Debug.WriteLine` / `Trace.WriteLine` с префиксом `[PrintService]`:
- Полный stack trace через `.ToString()` для диагностики.
- Категория ошибки в сообщении для фильтрации.

**Логирование в файл (out of scope для v1):** в будущем можно добавить `File.AppendAllText` в `%AppData%\MosquitoNetCalculator\print-errors.log` для сбора статистики ошибок печати у пользователей. Сейчас достаточно Debug/Trace (видны разработчику при attached debugger'е).

#### 8.7.3 Матрица реакций UI

| PrintResultType | Иконка | Заголовок | Кнопки | Окно после |
|---|---|---|---|---|
| `PrinterOffline` | Warning | «Ошибка принтера» | Повторить / Отмена | Остаётся открытым |
| `PrinterOutOfPaper` | Warning | «Ошибка принтера» | Повторить / Отмена | Остаётся открытым |
| `PrinterTonerLow` | Warning | «Ошибка принтера» | Повторить / Отмена | Остаётся открытым |
| `PrinterError` | Warning | «Ошибка принтера» | Повторить / Отмена | Остаётся открытым |
| `SpoolerStopped` | Error | «Ошибка печати» | OK | Остаётся открытым |
| `AccessDenied` | Error | «Ошибка печати» | OK | Остаётся открытым |
| `QueueError` | Error | «Ошибка печати» | OK | Остаётся открытым |
| `Unknown` | Error | «Ошибка» | OK | Остаётся открытым |

**Ключевое правило:** после ЛЮБОЙ ошибки кнопка «Печать» снова enabled, прогресс-бар скрыт, окно **не закрывается** (пользователь может изменить настройки и попробовать снова, или закрыть вручную).

#### 8.7.4 UI-состояния панели при ошибке

```
ДО печати:
  [ Печать ]  ← enabled
  [████████░░░] ← скрыт
  Копия 3 из 5 ← скрыт

ВО ВРЕМЯ печати:
  [ Печать ]  ← disabled
  [████████░░░] ← видим, 60%
  Копия 3 из 5... ← видим

ПОСЛЕ retryable ошибки (пользователь нажал «Повторить»):
  [ Печать ]  ← снова enabled
  [████████░░░] ← скрыт
  Копия 3 из 5 ← скрыт

ПОСЛЕ non-retryable ошибки (пользователь нажал «ОК»):
  [ Печать ]  ← снова enabled
  [████████░░░] ← скрыт
  Копия 3 из 5 ← скрыт
  (пользователь может закрыть окно или попробовать другой принтер)

ПОСЛЕ успешной печати:
  (окно закрыто)
```

#### 8.7.5 Отказоустойчивость: повторная попытка с тем же принтером

Retryable ошибки (offline, paper, toner) допускают повторную отправку **того же** задания на **тот же** принтер после нажатия «Повторить». При этом:
- `CollatedPrintPaginator` пересоздаётся **заново** (старый paginator мог быть частично потреблён спулером — безопаснее пересоздать).
- Имя задания не меняется (спулер сам разберётся с дубликатами).

#### 8.7.6 Что НЕ обрабатываем в v1 (известные ограничения)

| Сценарий | Почему не обрабатываем |
|---|---|
| Задание успешно добавлено в очередь, но принтер завис позже (paper jam после spooling) | Это ответственность спулера печати и пользователя. Приложение не может отследить статус задания после `AddJob` без polling `PrintJob.IsCompleted`. |
| Частичная печать (принтер напечатал 2 из 5 копий и остановился) | Отслеживание прогресса печати в реальном времени — сложный polling, выходит за рамки v1. |
| Логирование ошибок в файл | `Debug.WriteLine` достаточно для v1; файловый лог — улучшение на будущее. |
| Автоматический retry с экспоненциальной задержкой | Overengineering для WPF-приложения. Ручной retry через диалог — достаточен. |

---

## 9. PrintPreviewWindow — XAML (скелет)

```xml
<Window x:Class="MosquitoNetCalculator.PrintPreviewWindow"
        Title="Предпросмотр КП"
        Width="1300" Height="780"
        MinWidth="1000" MinHeight="600"
        WindowStartupLocation="CenterOwner"
        Background="{DynamicResource Surface}">

    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="240"/>
            <ColumnDefinition Width="1"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <!-- Левая боковая панель -->
        <ScrollViewer Grid.Column="0" VerticalScrollBarVisibility="Auto">
            <StackPanel Margin="16,16">
                <!-- Принтер -->
                <TextBlock Text="ПРИНТЕР" Style="{StaticResource SectionLabel}"/>
                <ComboBox x:Name="PrinterCombo" Margin="0,4,0,12"/>

                <!-- Копии -->
                <TextBlock Text="КОПИИ" Style="{StaticResource SectionLabel}"/>
                <controls:NumericUpDownControl x:Name="CopiesControl"
                                               Margin="0,4,0,0"
                                               Height="32"
                                               Value="1"/>

                <!-- Collation -->
                <CheckBox x:Name="CollatedCheck" Content="Разбить по копиям"
                          IsChecked="True" Margin="0,10,0,0"/>

                <!-- Цветность -->
                <TextBlock Text="ЦВЕТ" Style="{StaticResource SectionLabel}" Margin="0,12,0,0"/>
                <StackPanel Orientation="Horizontal" Margin="0,4,0,12">
                    <RadioButton x:Name="ColorBtn" Content="Цветная" IsChecked="True"
                                 GroupName="ColorGroup"/>
                    <RadioButton x:Name="BwBtn" Content="Ч/б" Margin="12,0,0,0"
                                 GroupName="ColorGroup"/>
                </StackPanel>

                <!-- Страницы -->
                <TextBlock Text="СТРАНИЦЫ" Style="{StaticResource SectionLabel}"/>
                <CheckBox x:Name="AllPagesCheck" Content="Все страницы"
                          IsChecked="True" Checked="AllPages_Changed" Unchecked="AllPages_Changed"/>
                <Grid IsEnabled="{Binding IsChecked, ElementName=AllPagesCheck, Converter={StaticResource InverseBool}}" Margin="16,4,0,0">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>
                    <StackPanel Grid.Row="0" Orientation="Horizontal">
                        <TextBlock Text="с" VerticalAlignment="Center"/>
                        <TextBox x:Name="PageFromBox" Width="50" Margin="8,0" Text="1"/>
                        <TextBlock Text="по" VerticalAlignment="Center"/>
                        <TextBox x:Name="PageToBox" Width="50" Margin="8,0" Text="1"/>
                    </StackPanel>
                    <TextBlock Grid.Row="1" Text="или" Margin="0,4" Foreground="{DynamicResource TextMuted}"/>
                    <StackPanel Grid.Row="2" Orientation="Horizontal">
                        <TextBlock Text="Страница" VerticalAlignment="Center"/>
                        <TextBox x:Name="SinglePageBox" Width="50" Margin="8,0"/>
                    </StackPanel>
                </Grid>

                <!-- Диапазон-подсказка -->
                <TextBlock x:Name="RangeHint" Margin="0,8,0,0"
                           Foreground="{DynamicResource TextMuted}" FontSize="10"
                           TextWrapping="Wrap"/>

                <!-- Прогресс -->
                <ProgressBar x:Name="ProgressBar" Height="4" Margin="0,16,0,0"
                             Visibility="Collapsed" Minimum="0" Maximum="100"/>
                <TextBlock x:Name="ProgressText" Margin="0,4,0,0"
                           Visibility="Collapsed" FontSize="11"
                           Foreground="{DynamicResource TextMuted}"/>

                <!-- Кнопки -->
                <Button x:Name="PrintButton" Content="Печать" Margin="0,16,0,0"
                        Style="{DynamicResource PrimaryButton}"
                        Click="Print_Click" Height="36"/>
                <Button x:Name="SavePdfButton" Content="Сохранить PDF" Margin="0,8,0,0"
                        Style="{DynamicResource GhostButton}"
                        IsEnabled="False" ToolTip="Будет доступно в следующем обновлении"
                        Height="32"/>
                <Button Content="Закрыть" Margin="0,8,0,0"
                        Style="{DynamicResource GhostButton}"
                        Click="Close_Click" Height="32"/>
            </StackPanel>
        </ScrollViewer>

        <!-- Разделитель -->
        <Border Grid.Column="1" Background="{DynamicResource Border}"/>

        <!-- Предпросмотр + затемняющий оверлей -->
        <Grid Grid.Column="2">
            <!-- FlowDocumentReader (слой 0) -->
            <FlowDocumentReader x:Name="DocumentReader"
                IsToolBarVisible="False"
                IsPageViewEnabled="True"
                IsTwoPageViewEnabled="False"
                IsPrintEnabled="False"
                ViewingMode="Page"/>

            <!-- Затемняющий оверлей для страниц вне диапазона (слой 1) -->
            <!-- Показывается только когда текущая страница вне выбранного диапазона -->
            <Border x:Name="DimmingOverlay"
                    Background="#99FFFFFF"
                    IsHitTestVisible="False"
                    Visibility="Collapsed">
                <TextBlock x:Name="DimmingText"
                           Text="Стр. X не входит в диапазон печати"
                           VerticalAlignment="Center"
                           HorizontalAlignment="Center"
                           FontSize="24"
                           Foreground="#666666"
                           FontWeight="Bold"
                           TextWrapping="Wrap"
                           TextAlignment="Center"/>
            </Border>
        </Grid>
    </Grid>
</Window>
```

---

## 10. Обновление ActionBarControl

```csharp
private void BtnPrintKp_Click(object sender, RoutedEventArgs e)
{
    if (!TryGetMainWindow(nameof(BtnPrintKp_Click), out var mw)) return;
    var validItems = mw.OrderItems.Where(i => !string.IsNullOrEmpty(i.Name) && i.IsActive && i.Total > 0).ToList();
    if (validItems.Count == 0)
    {
        ToastService.ShowToast("Добавьте хотя бы одну позицию.", ToastType.Warning);
        return;
    }

    double total = validItems.Sum(i => i.TotalWithDeduction);
    string amountInWords = AmountInWordsService.Convert(total);

    // Нативный путь (единственный — legacy удалён)
    var document = mw.PrintService.BuildFlowDocument(validItems, mw.ClientInfo, total, amountInWords);
    if (document == null)
    {
        ToastService.ShowToast("Нет данных для печати.", ToastType.Warning);
        return;
    }

    var preview = new PrintPreviewWindow(document, mw.PrintService, mw.ClientInfo.ContractNumber, mw.LastPrintSettings)
    {
        Owner = mw
    };
    preview.ShowDialog();

    // Сохраняем настройки после закрытия окна
    mw.LastPrintSettings = preview.GetSettings();
}
```

---

## 11. Тест-план

### 11.1 Существующие тесты (оставить/обновить)

| Файл | Действие |
|---|---|
| `PrintServiceTests.cs` | Обновить: убрать тесты HTML-генерации, добавить тесты `PrintDirect` |
| `PrintServiceTests.cs` (FlowDocument) | Оставить: T1-T23 из v2.2 (проверка структуры FlowDocument) |
| `PrintServiceTests.cs` (PDF) | Оставить: T24-T27 из v2.2 |
| `AppLifecycleTests.cs` | Удалить `Print_Template_Has_No_Slash_Dogovor`, обновить §7 |
| `ManualChecklistTests.cs` | Обновить §7 под новый нативный flow |

### 11.2 Новые тесты

#### CollatedPrintPaginatorTests

| # | Тест | Что проверяет |
|---|---|---|
| C1 | `Collated_ThreePages_FiveCopies_PageCount15` | PageCount = 3 × 5 = 15 |
| C2 | `Collated_PageOrder_1_2_3_1_2_3` | GetPage sequence matches collated order |
| C3 | `NonCollated_PageOrder_1_1_2_2_3_3` | GetPage sequence matches non-collated order |
| C4 | `RangeFilter_Pages2to4_OnlyThosePages` | Диапазон фильтрует страницы |
| C5 | `RangeFilter_ThenCollation_CorrectOrder` | Диапазон + коллация работают вместе |
| C6 | `SinglePage_Mode_ReturnsOnlyThatPage` | Режим «Страница N» |
| C7 | `AllPages_SameAsFullRange` | AllPages = range 1..PageCount |
| C8 | `PageCount_WithRange_IsFilteredCount_×Copies` | PageCount = (pageTo-pageFrom+1) × copies |
| C9 | `GetPage_OutOfRange_Throws` | ArgumentOutOfRangeException |

#### PrintPreviewWindowTests (интеграционные/STA)

| # | Тест | Что проверяет |
|---|---|---|
| P1 | `Window_Opens_WithFlowDocument` | Окно открывается с FlowDocument в Reader |
| P2 | `Sidebar_Shows_DefaultSettings` | Настройки по умолчанию: copies=1, collated=on, allPages=on, color=on |
| P3 | `CopiesValidation_WarnsOver10` | MessageBox при copies > 10 |
| P4 | `AllPagesUncheck_EnablesRangeFields` | Поля диапазона становятся enabled |
| P5 | `RangeHint_Updates_WhenSettingsChange` | Текст подсказки обновляется |
| P6 | `PrintButton_TriggersPrintQueue` | PrintQueue.AddJob вызывается |
| P7 | `CloseButton_ClosesWindow` | Окно закрывается |
| P8 | `SettingsPersist_BetweenOpens` | Настройки сохраняются между открытиями (in-memory) |

---

## 12. План реализации (8 шагов)

### Шаг 1: CollatedPrintPaginator

- `Helpers/CollatedPrintPaginator.cs` — новый файл
- Реализация range filter + copies × collation
- Юнит-тесты (C1–C9)

### Шаг 2: Обновить PrintService.Print()

- `PrintService.cs` — новый метод `PrintDirect()` или переписать существующий `Print()`
- Убрать `PrintDialog`, использовать `PrintQueue.AddJob`
- Добавить `IProgress` для прогресса

### Шаг 3: PrintPreviewWindow — переписать XAML

- `PrintPreviewWindow.xaml` — заменить WebView2 на FlowDocumentReader + левую панель
- Стили по существующей теме

### Шаг 4: PrintPreviewWindow — code-behind

- `PrintPreviewWindow.xaml.cs` — логика боковой панели, ComboBox принтеров, кнопки ±, валидация
- Взаимодействие с `PrintService.PrintDirect()`
- Прогресс-бар

### Шаг 5: ActionBarControl — обновить BtnPrintKp_Click

- `ActionBarControl.xaml.cs` — новый путь без HTML

### Шаг 6: Удаление legacy

- Удалить WebView2 NuGet package
- Удалить `print_template.html`, `HtmlBuilder.cs`, `SvgDrawer.cs`, `Template.cs`
- Удалить WebView2-часть из `DependencyCheckerService`
- Удалить `CheckAndNotifyAsync()` из `App.xaml.cs`
- Удалить флаг `useNativePrint` из `AppSettingsService`

### Шаг 7: Тесты

- `CollatedPrintPaginatorTests` (9 новых)
- Обновить `PrintServiceTests` (убрать HTML тесты, добавить `PrintDirect`)
- Обновить `AppLifecycleTests`, `ManualChecklistTests`

### Шаг 8: Верификация

- `dotnet build -c Release` — 0 errors
- `dotnet test` — все тесты проходят
- Ручная проверка печати на реальном принтере
- `validate-docs.ps1` — все проверки ОК
- `what-to-update.ps1` + обновление документации

---

## 13. Граничные случаи

| Сценарий | Ожидаемое поведение |
|---|---|
| Нет позиций | Toast, окно не открывается |
| 1 страница, 10 копий | 10-страничное задание (collated или no) |
| Copies = 1 | Collation не имеет эффекта (1,2,3 = 1,2,3) |
| Copies > 10 | MessageBox с подтверждением |
| Copies = 0 (ошибка ввода) | Кнопка «Печать» disabled, валидация не даёт уйти в 0 |
| PageFrom > PageTo | Автокоррекция (swap или disabled кнопки Печать) |
| PageFrom = PageTo = 3 | Печатается только страница 3 |
| SinglePage = 5 | Печатается только страница 5 |
| SinglePage > PageCount | Кнопка «Печать» disabled, красная подсветка поля |
| Принтер не выбран (null) | Использовать принтер по умолчанию |
| Принтер по умолчанию не найден | Показать ошибку в боковой панели, кнопка «Печать» disabled |
| Выбранный принтер отключён | `PrintQueue.AddJob` кинет исключение → показать ошибку |
| Нет принтеров в системе | Список пуст, кнопка «Печать» disabled, показать предупреждение |
| Закрытие окна без печати | Настройки НЕ сохраняются (только при успешной печати) |
| Повторное открытие окна | Настройки восстанавливаются из `MainWindow.LastPrintSettings` |
| DPI > 150% | FlowDocumentReader корректно масштабирует |
| FlowDocument пустой (0 блоков) | Не должно происходить (BuildFlowDocument возвращает null) |

---

## 14. Критерии готовности

1. ✅ `dotnet build -c Release` — 0 errors, 0 warnings
2. ✅ `dotnet test` — все тесты проходят
3. ✅ Пакет `Microsoft.Web.WebView2` удалён из зависимостей
4. ✅ Никаких HTML-файлов, temp-файлов, WebView2-кода в проекте
5. ✅ Предпросмотр открывается ≤ 1 сек
6. ✅ Collated-печать: 5 копий × 3 страницы = 15 страниц в порядке 1,2,3,1,2,3,...
7. ✅ Non-collated: 5 копий × 3 страницы = 15 страниц в порядке 1,1,1,1,1,2,2,...
8. ✅ Диапазон страниц работает
9. ✅ Выбор принтера через ComboBox
10. ✅ Предупреждение при copies > 10
11. ✅ Прогресс-бар при отправке задания
12. ✅ Настройки сохраняются в памяти между открытиями окна
13. ✅ Кнопка «Сохранить PDF» disabled (placeholder для будущего)

---

## 15. История изменений

| Дата | Версия | Изменения |
|---|---|---|
| 2026-07-04 | 1.3 | §8.7 добавлен: стратегия обработки ошибок `PrintQueue.AddJob`. Таксономия исключений (5 типов: `PrintQueueException`, `PrintSystemException`, `UnauthorizedAccessException`, `InvalidOperationException`, `ArgumentException`), классификация retryable/non-retryable. Три уровня обработки: `PrintService.SendToQueue()` с `PrintResult` enum (9 значений), `PrintPreviewWindow.Print_Click` с MessageBox-диалогами (Повторить/Отмена для retryable, OK для non-retryable), Debug.WriteLine логирование. Матрица реакций UI (8 типов ошибок × иконка/заголовок/кнопки), диаграмма UI-состояний панели. F20 добавлен. |
| 2026-07-04 | 1.2 | §8.6 добавлен: детальный дизайн `NumericUpDownControl` (UserControl с `RepeatButton` ±, 4 уровня валидации ввода, `DependencyProperty Value`, `GhostRepeatButton` стиль, таблица граничных случаев). `NumericUpDownControl` добавлен в «Что приходит». §9 XAML обновлён: `Grid` с TextBox/кнопками заменён на `<controls:NumericUpDownControl>`, добавлен `xmlns:controls` namespace.
