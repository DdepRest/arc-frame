# Нативная печать КП (Native Print) — Спецификация

> **Статус:** Утверждена; текущий step — реализация (Phase 1). v2.2 содержит исправления после researcher-docs верификации QuestPDF API (v2026.7.0).
> **Цель:** Полный отказ от зависимостей Web (WebView2, HTML) в системе печати КП
> **Версия:** 2.2 (rev. 2026-07-04 — API fixes for QuestPDF v2026.7.0)

### Что исправлено в v2.1

| # | Раздел | Что было | Что стало | Категория |
|---|---|---|---|---|
| 1 | §5.2 | `DocumentPaginatorWrapper.PixelToDip(...)` — несуществующий класс | DIP-значения напрямую (793.7 × 1122.5) | BLOCKING |
| 2 | §5.5 | `BuildHeaderVisual/BuildFooterVisual` упоминаются без определения | Полные реализации + override `IsPageCountValid` | BLOCKING |
| 3 | §6.3 | `.Fallback(Fonts.SegoeUI)` — несуществующий API; license не упомянут | `Settings.License = LicenseType.Community` + правильный API | BLOCKING |
| 4 | §6.5 | Только PNG-вариант через `RenderTargetBitmap` (потеря качества) | Двойной путь: PNG (готовность) + QuestPDF Canvas (качество) | ARCHITECTURAL |
| 5 | §8.2/§8.3 | `OnSavePdfRequested` event-pattern (smell) | Прямая передача `Action<string>` в конструктор | ARCHITECTURAL |
| 6 | §9 | Нет rollback-стратегии | Inversion-of-control флаг `useNativePrint` для отката | ARCHITECTURAL |
| 7 | §11 | 25 тестов без data-fidelity контракта | Golden-copy тест: DataSet → эталонные строки | ARCHITECTURAL |
| 8 | §14 | Fac 14 говорит "pdf > 0 bytes" — слабый контракт | Fac 14 дополнен: header bytes = `%PDF-1.x` | NIT |

---

## 1. Резюме

Полная замена текущей печати через WebView2/HTML на нативную WPF-печать:
- **Предпросмотр:** `FlowDocument` + `FlowDocumentReader` (нативный WPF)
- **Печать:** `PrintDialog` (стандартный диалог Windows)
- **PDF экспорт:** QuestPDF (отдельная кнопка «Сохранить PDF»)
- **Чертежи:** SVG → XAML `PathGeometry`/`StreamGeometry` (векторная графика)
- **Колонтитулы:** Номер договора + нумерация страниц на каждой странице

### Что уходит

| Удаляем | Причина |
|---|---|
| `Microsoft.Web.WebView2` (NuGet v1.0.2210.55) | Зависимость от браузерного рантайма |
| `Resources/print_template.html` | HTML-шаблон |
| `PrintService.HtmlBuilder.cs`, `PrintService.SvgDrawer.cs`, `PrintService.Template.cs` | HTML-генерация |
| `PrintPreviewWindow.xaml` + `.xaml.cs` | WebView2-based окно |
| `DependencyCheckerService` — WebView2-часть | Проверка WebView2 Runtime |
| `App.xaml.cs` — `CheckAndNotifyAsync()` | Toast о WebView2 |

### Что приходит

| Добавляем | Роль |
|---|---|
| `Services/PrintService.FlowDocument.cs` | Построение FlowDocument |
| `Services/PrintService.Drawings.cs` | XAML Geometry для чертежей |
| `Services/PrintService.Pdf.cs` | Экспорт PDF через QuestPDF |
| `Helpers/PrintPaginator.cs` | Колонтитулы (№ договора, стр. X из Y) |
| `PrintPreviewWindow.xaml` + `.xaml.cs` | Переписан под FlowDocumentReader |
| `QuestPDF` (NuGet) | Библиотека PDF (community, MIT) |

---

## 2. Текущее состояние

### 2.1 Существующий флоу печати

```
ActionBarControl.BtnPrintKp_Click()
  → PrintService.GenerateKpHtml()       // HTML-строка из print_template.html
  → PrintPreviewWindow(html)            // WebView2 загружает temp-файл
  → window.print()                      // браузерный диалог печати Chromium
  → fallback: открыть в системном браузере
  → fallback-fallback: сохранить .html на рабочий стол
```

### 2.2 Файлы, участвующие в печати (текущие)

| Файл | Роль | Судьба |
|---|---|---|
| `Services/PrintService.cs` | Точка входа: `GenerateKpHtml()` | ✅ Переписать |
| `Services/PrintService.HtmlBuilder.cs` | `FillTemplate()`, `EscapeHtml()` | ❌ Удалить |
| `Services/PrintService.SvgDrawer.cs` | `GetDrawingSvg()` — SVG-строки | ❌ Удалить (→ Drawings.cs) |
| `Services/PrintService.Template.cs` | `LoadTemplate()` из ресурсов | ❌ Удалить |
| `Resources/print_template.html` | HTML + CSS (A4, таблицы, стили) | ❌ Удалить |
| `PrintPreviewWindow.xaml` | WebView2 control | ✅ Переписать |
| `PrintPreviewWindow.xaml.cs` | WebView2 init, window.print(), fallback | ✅ Переписать |
| `Controls/ActionBarControl.xaml.cs` | `BtnPrintKp_Click` | ✅ Минимальные правки |
| `MosquitoNetCalculator.csproj` | WebView2 package + EmbeddedResource | ✅ Обновить |
| `Services/DependencyCheckerService.cs` | `IsWebView2Installed()`, toast | ✅ Убрать WebView2-часть |
| `App.xaml.cs` | `CheckAndNotifyAsync()` | ✅ Убрать вызов |
| `MosquitoNetCalculator.Tests/Services/PrintServiceTests.cs` | 22 теста HTML-вывода | ✅ Переписать под FlowDocument |

---

## 3. Требования (из интервью)

### 3.1 Функциональные требования

| # | Требование | Приоритет | Источник |
|---|---|---|---|
| **F1** | Предпросмотр через `FlowDocumentReader` (нативный WPF) | P0 | Интервью R1 |
| **F2** | Печать через `PrintDialog` + кастомный `DocumentPaginator` с колонтитулами | P0 | Интервью R1 |
| **F3** | Кнопка «Сохранить PDF» → прямой экспорт в файл через QuestPDF | P0 | Интервью R2 |
| **F4** | Точная копия вёрстки текущего HTML (шрифты, отступы, цвета, ширина колонок) | P0 | Интервью R2 |
| **F5** | Колонтитулы на каждой странице: номер договора + «Страница X из Y» + дата | P0 | Интервью R2 |
| **F6** | Чертежи товаров через XAML Path/Geometry (вектор, без растров) | P0 | Интервью R1 |
| **F7** | Полное удаление HTML, WebView2, print_template.html — никаких web-зависимостей | P0 | Интервью R3 |
| **F8** | Пустой КП → toast «Добавьте хотя бы одну позицию», окно не открывать | P0 | Интервью R3 |
| **F9** | Точные цвета из CSS: #f0f0f0 (чётные строки), #d8d8d8 (шапка), #999 (границы), #d5d5d5 (итоги) | P1 | Интервью R3 |
| **F10** | Шрифт Segoe UI (системный, Windows 10/11) | P1 | Интервью R4 |
| **F11** | Единый PrintService: BuildFlowDocument(), Print(), ExportPdf() | - | Интервью R4 |
| **F12** | Тесты переписать под FlowDocument (эквивалентное покрытие) | P0 | Интервью R4 |

### 3.2 Анти-требования (чего НЕ делаем)

| Решение | Обоснование |
|---|---|
| ❌ Не оставляем HTML-генерацию как fallback | Полное удаление (Интервью R3) |
| ❌ Не используем FixedDocument/XPS | FlowDocument даёт автоперенос текста (Интервью R1) |
| ❌ Не делаем PNG-чертежи | Вектор через XAML Path (Интервью R1) |
| ❌ Не открываем пустой предпросмотр | Toast + не открываем окно (Интервью R3) |
| ❌ Не используем FixedDocument | FlowDocument — автоперенос, адаптивность |

---

## 4. Архитектура решения

### 4.1 Новый флоу печати

```
ActionBarControl.BtnPrintKp_Click()
  → PrintService.BuildFlowDocument(items, client, total, amountInWords)
  → new PrintPreviewWindow(flowDocument)
     ├── [Печать] → PrintService.Print(flowDocument) → PrintDialog → принтер/PDF
     ├── [Сохранить PDF] → PrintService.ExportPdf(items, client, ...) → SaveFileDialog → файл.pdf
     └── [Закрыть]
```

### 4.2 Диаграмма классов

```
┌─────────────────────────────────────────────────────────────────┐
│                    PrintPreviewWindow (переписан)                 │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ FlowDocumentReader                                        │  │
│  │   Document = PrintService.BuildFlowDocument(...)          │  │
│  │   ViewingMode = Page, FitToWidth                          │  │
│  └───────────────────────────────────────────────────────────┘  │
│  [Печать] → PrintPaginator → PrintDialog → принтер             │
│  [Сохранить PDF] → SaveFileDialog → PrintService.ExportPdf()   │
│  [Закрыть]                                                     │
└─────────────────────────────────────────────────────────────────┘
                      ▲ FlowDocument
┌─────────────────────────────────────────────────────────────────┐
│                      PrintService (partial)                      │
│                                                                  │
│  PrintService.cs           — BuildFlowDocument(), Print()       │
│  PrintService.FlowDocument.cs — построение FlowDocument         │
│  PrintService.Drawings.cs  — GetDrawingGeometry()                │
│  PrintService.Pdf.cs       — ExportPdf() через QuestPDF         │
└─────────────────────────────────────────────────────────────────┘
                      ▲
┌─────────────────────────────────────────────────────────────────┐
│            PrintPaginator : DocumentPaginator (internal)         │
│  Декоратор над FlowDocument.DocumentPaginator                   │
│  На каждой странице добавляет:                                  │
│    Header: «Договор № 1-5»                                      │
│    Footer: «Страница 1 из 3      02.07.2026»                    │
└─────────────────────────────────────────────────────────────────┘
```

### 4.3 Структура partial-классов PrintService

```
Services/
  PrintService.cs              // public API: BuildFlowDocument(), Print(), ExportPdf()
  PrintService.FlowDocument.cs // private: BuildHeader(), BuildClientBlock(), BuildTable(), BuildTotalBlock(), BuildNotesBlock(), BuildTermsBlock(), BuildSignatureBlock()
  PrintService.Drawings.cs     // private static: GetDrawingGeometry() → Geometry (XAML Path)
  PrintService.Pdf.cs          // public: ExportPdf() → void (сохраняет файл через QuestPDF)
```

### 4.4 PrintDocumentBuilder — НЕ нужен

В отличие от первой версии спеки, отдельный `PrintDocumentBuilder` не создаётся. Все методы построения FlowDocument — private внутри `PrintService.FlowDocument.cs`. Единая точка входа: `PrintService.BuildFlowDocument()`.

---

## 5. Детальное проектирование

### 5.1 Макет страницы A4

**Физический размер:** 210 × 297 мм, книжная ориентация.

**Поля (как в HTML @page):**
- Верх: 30 мм
- Низ: 14 мм
- Левое: 16 мм
- Правое: 16 мм

**Полезная область:** 178 × 253 мм

```
┌─────────────────────────────────────────────────┐
│ ← 16 мм →                          ← 16 мм →  │
│ ┌─────────────────────────────────────────────┐ │ ↑
│ │ Договор № 1-5                    Header    │ │ 30 мм
│ ├─────────────────────────────────────────────┤ │ ↓
│ │                                             │ │
│ │  КОММЕРЧЕСКОЕ ПРЕДЛОЖЕНИЕ                   │ │
│ │  Договор № 1-5 от 15.01.2026               │ │
│ │                                             │ │
│ │  Заказчик: Иванов И.И.                      │ │
│ │  Телефон: +7 999 123-45-67                  │ │
│ │  Адрес: г. Москва, ул. Пушкина, д. 10       │ │
│ │                                             │ │
│ │  ┌────┬──────────┬────┬──────┬──────┬───┐  │ │
│ │  │ №  │Наимен.   │Цвет│Ш, мм │В, мм │... │  │ │
│ │  ├────┼──────────┼────┼──────┼──────┼───┤  │ │
│ │  │ 1  │Anwis     │Бел │ 1000 │ 2000 │... │  │ │
│ │  │ 2  │Отлив     │Бел │ 1200 │  150 │... │  │ │
│ │  └────┴──────────┴────┴──────┴──────┴───┘  │ │
│ │  ИТОГО:                         3 600,00  │ │
│ │                                             │ │
│ │  ИТОГО: 3 600,00 руб.                       │ │
│ │  (Три тысячи шестьсот рублей 00 копеек)      │ │
│ │                                             │ │
│ │  Условия:                                    │ │
│ │  • Срок действия КП — 5 рабочих дней         │ │
│ │  • Оплата производится на основании счёта     │ │
│ │  • Цены указаны с учётом стоимости материалов │ │
│ │                                             │ │
│ │  Исполнитель _________  Заказчик _________  │ │
│ │                                             │ │
│ ├─────────────────────────────────────────────┤ │ ↑
│ │ Страница 1 из 3          02.07.2026        │ │ 14 мм
│ └─────────────────────────────────────────────┘ │ ↓
└─────────────────────────────────────────────────┘
```

### 5.2 FlowDocument — настройки страницы

WPF `PageWidth`/`PageHeight` уже задаются в DIP (1 DIP = 1/96 inch при стандартном DPI). 1 mm = 96/25.4 ≈ 3.7795 DIP. Никакого helper-класса не нужно — DIP-значения считаем напрямую:

```csharp
// Размеры A4 в DIP при стандартном 96 DPI
//   width  = 210 mm * 96 / 25.4 = 793.7 DIP
//   height = 297 mm * 96 / 25.4 = 1122.5 DIP
//   margins L/R = 16 mm * 96 / 25.4 = 60.47 DIP
//   margins T/B = 30 mm / 14 mm = 113.39 / 52.91 DIP
const double MmToDip = 96.0 / 25.4;
var doc = new FlowDocument
{
    PageWidth = 210 * MmToDip,   // 793.7
    PageHeight = 297 * MmToDip,  // 1122.5
    PagePadding = new Thickness(
        16 * MmToDip,  // left
        30 * MmToDip,  // top  — резервирует место под header (PrintPaginator рисует в этой зоне)
        16 * MmToDip,  // right
        14 * MmToDip), // bottom
    FontFamily = new FontFamily("Segoe UI"),  // FALLBACK на Segoe UI Emoji/Arial если основной отсутствует
    FontSize = 9 * 96.0 / 72.0,  // 9pt → 12 DIP (1pt = 96/72 DIP при 96 DPI)
    ColumnWidth = double.PositiveInfinity,  // одна колонка во всю ширину
    IsOptimalParagraphEnabled = true,       // авто-расстановка переносов и межсловных интервалов
    IsHyphenationEnabled = false,           // русский текст: hyphenation делает QualityScore хуже
    IsColumnWidthFlexible = false           // точно 1 колонка, как в HTML
};
```

**Контракт **:
- `PagePadding.Top = 30 mm` резервирует зону для верхнего колонтитула (`PrintPaginator` рисует header внутри этой зоны, не поверх текста).
- `PagePadding.Bottom = 14 mm` — аналогично для footer.
- `ColumnWidth = PositiveInfinity + IsColumnWidthFlexible = false` гарантирует ровно одну колонку; без этого FlowDocument может попытаться разбить на 2 колонки при широкой странице.

### 5.3 Секции FlowDocument

FlowDocument состоит из следующих блоков (в порядке сверху вниз):

| # | Блок | Тип WPF | Содержимое |
|---|---|---|---|
| 1 | Заголовок документа | `Paragraph` (центр) | «КОММЕРЧЕСКОЕ ПРЕДЛОЖЕНИЕ» — 13.5pt, Bold, letter-spacing |
| 2 | Строка договора | `Paragraph` (центр) | «Договор № 1-5 от 15.01.2026» — 9pt |
| 3 | Блок заказчика | `Section` → `Table` (2 колонки) | Заказчик, Телефон, Адрес — label:value |
| 4 | Таблица товаров | `Table` (12 колонок) | Строки позиций + строка ИТОГО |
| 5 | Блок итогов | `Section` → `Paragraph` | Сумма прописью + доп. КП (если есть) |
| 6 | Блок примечаний | `Section` | Если `clientInfo.Notes` не пуст |
| 7 | Блок условий | `Section` | Статический текст условий |
| 8 | Блок подписей | `Table` (2 колонки) | «Исполнитель _________» / «Заказчик _________» |

### 5.4 Таблица товаров (12 колонок)

**Пропорции ширины (как в HTML `<colgroup>`):**

| # | Колонка | Ширина | Выравнивание |
|---|---|---|---|
| 1 | № | 3.5% | Center |
| 2 | Наименование | 15% | Left (с переносом) |
| 3 | Цвет | 7.5% | Center |
| 4 | Ш, мм | 6.5% | Center |
| 5 | В, мм | 6.5% | Center |
| 6 | Кол-во | 7% | Center |
| 7 | Монтаж | 7.5% | Center |
| 8 | Площ./Дл. | 9% | Center |
| 9 | Ед. | 4% | Center |
| 10 | Цена | 9.5% | Right |
| 11 | Сумма | 11.5% | Right |
| 12 | Чертёж | 14% | Center |

**Особенности реализации WPF Table:**

- `TableRowGroup` для `<tbody>`
- `TableRowGroup` с `IsHeader = true` для `<thead>` (автоповтор на новых страницах)
- Чередование фона строк программно: чётные → `#f0f0f0`, нечётные → прозрачный
- Шапка: `Background = #d8d8d8`, `FontWeight = SemiBold`, `FontSize = 7.5pt`
- Итого: `Background = #d5d5d5`, `FontWeight = Bold`, `FontSize = 9pt`, верхняя граница 2px
- Границы ячеек: `BorderBrush = #999`, `BorderThickness = 1`
- Ячейка «Чертёж»: `BlockUIContainer` с `Image` (Source = `DrawingImage` из Geometry)
- Ячейка «Наименование»: `TextWrapping = Wrap` для длинных названий

**Данные строки (OrderItem):**

```
{i+1}  →  №
item.DisplayName  →  Наименование (с суффиксом «(Антикошка)» если IsAnticat)
item.Color  →  Цвет
item.Width:F0  →  Ш, мм (хранимое/расчётное)
item.Height:F0  →  В, мм (хранимое/расчётное)
item.Quantity  →  Кол-во
item.KpInstallationDisplay  →  Монтаж (✓/✗/В/—)
item.CalculatedValue:F3  →  Площ./Дл.
item.Unit  →  Ед. (м²/м.п./шт.)
MoneyFormatService.Format(item.Price)  →  Цена
MoneyFormatService.Format(item.TotalWithDeduction)  →  Сумма
GetDrawingImage(item.Name, item.Width, item.Height)  →  Чертёж
```

### 5.5 Колонтитулы (PrintPaginator)

Кастомный `DocumentPaginator` — декоратор над стандартным пагинатором FlowDocument. Реализует **три** обязательных override (`DocumentPaginator` — абстрактный класс с 4+ членами):

```csharp
internal sealed class PrintPaginator : DocumentPaginator
{
    private readonly DocumentPaginator _source;   // ((IDocumentPaginatorSource)_document).DocumentPaginator
    private readonly string _contractNumber;
    private readonly DateTime _contractDate;
    private bool _contractFullyResolved;

    public PrintPaginator(DocumentPaginator source, string contractNumber, DateTime contractDate)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _contractNumber = (contractNumber ?? string.Empty).Trim();
        _contractDate = contractDate;
        // Один раз заставляем source paginate, чтобы PageCount стал valid —
        // иначе GetPage(PageCount-1) выше не узнает, сколько страниц.
        _source.PageCount.ToString(); // touch (touch-then-discard чтобы JIT не убрал)
    }

    // ── PageSize forward ─────────────────────────────────────────
    public override Size PageSize
    {
        get => _source.PageSize;
        set { _source.PageSize = value; }
    }

    public override bool IsPageCountValid => true;       // ← source уже paginate'd в ctor

    public override int PageCount => _source.PageCount;  // ← 1-based expected

    // ── Source identity exposure (чтобы PrintDialog мог показать имя) ──
    public override IDocumentPaginatorSource Source => _source.Source;

    // ── Главный метод: страница + header/footer overlay ────────
    public override DocumentPage GetPage(int pageNumber)
    {
        if (pageNumber < 0 || pageNumber >= PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var originalPage = _source.GetPage(pageNumber);
        var container = new ContainerVisual();

        // Z-order: оригинал → header (поверх верхней зоны) → footer (поверх нижней)
        container.Children.Add(originalPage.Visual);
        BuildHeaderVisual(container, originalPage.Size, pageNumber);
        BuildFooterVisual(container, originalPage.Size, pageNumber, _source.PageCount);

        return new DocumentPage(container, originalPage.Size, originalPage.BleedBox, originalPage.ContentBox);
    }

    // ── Header: «Договор № 1-5» (правый верхний угол) ─────────
    // Использует FormattedText из System.Windows.Media — не GlyphRunDrawing,
    // потому что FormattedText проще для русского текста и поддерживает
    // Italic + LineHeight без ручной работы с GlyphTypeface.
    private void BuildHeaderVisual(ContainerVisual parent, Size pageSize, int pageNumber)
    {
        if (string.IsNullOrEmpty(_contractNumber)) return;  // «Договор б/н» — не показываем

        // Δ Y в верхней зоне (top padding = 30 mm = ~113.4 DIP)
        // «Подвешиваем» текст в верхних 30 mm, отступая ~15 DIP сверху и ~30 DIP справа
        var brush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        brush.Freeze();
        var ft = MakeFormattedText(
            $"Договор № {_contractNumber}",
            fontSize: 7 * 96.0 / 72.0,    // 7pt → 9.33 DIP
            fontStyle: FontStyles.Italic,
            brush: brush,
            maxWidth: pageSize.Width * 0.6,
            textAlignment: TextAlignment.Right);

        var origin = new Point(pageSize.Width - 30 - ft.Width /* right-aligned */,
                               15 /* mm-ish margin from top */);
        var textVisual = new DrawingVisual();
        using (var dc = textVisual.RenderOpen())
            dc.DrawText(ft, origin);

        parent.Children.Add(textVisual);
    }

    // ── Footer: «Страница X из Y» (слева) + дата договора (справа) ───
    private void BuildFooterVisual(ContainerVisual parent, Size pageSize, int pageNumber, int totalPages)
    {
        var gray = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        gray.Freeze();
        var ftLeft = MakeFormattedText(
            $"Страница {pageNumber + 1} из {totalPages}",
            9.33, FontStyles.Italic, gray);
        var ftRight = MakeFormattedText(
            _contractDate.ToString("dd.MM.yyyy"),
            9.33, FontStyles.Italic, gray,
            maxWidth: pageSize.Width, textAlignment: TextAlignment.Right);

        // Вертикально: 30 mm снизу (footer в нижней зоне PagePadding.Bottom)
        double baselineY = pageSize.Height - 30 - ftLeft.Height;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawText(ftLeft, new Point(30, baselineY));
            // ftRight is right-aligned → его origin = right - width
            dc.DrawText(ftRight, new Point(pageSize.Width - 30 - ftRight.Width, baselineY));
        }
        parent.Children.Add(visual);
    }

    private static FormattedText MakeFormattedText(
        string text,
        double fontSize,
        FontStyle fontStyle,
        Brush brush,
        double maxWidth = double.PositiveInfinity,
        TextAlignment textAlignment = TextAlignment.Left)
    {
        // Используем invariant typeface name "Segoe UI" — fallback на
        // "Segoe UI Symbol" / "Arial" встроен в FontFamily. Не нужны DllImport
        // или ручные GDI-шрифты.
        var typeface = new Typeface(
            new FontFamily("Segoe UI"),
            fontStyle,
            FontWeights.Normal,
            FontStretches.Normal);
        return new FormattedText(
            text ?? string.Empty,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush,
            1.0 /* pixelsPerDip */)
        {
            MaxTextWidth = maxWidth,
            TextAlignment = textAlignment,
            LineHeight = fontSize * 1.0,        // tight
        };
    }
}
```

**Z-order и overlap-контракт:**
- Header рисуется **поверх** оригинального содержимого в зоне `PagePadding.Top = 30mm`. Любой текст FlowDocument, попавший в верхние 30 mm, **будет заслонён header** — это ожидаемо (в зоне padding контента быть не должно).
- Footer — аналогично в зоне `PagePadding.Bottom`.
- Контент FlowDocument по умолчанию начинается ниже `PagePadding.Top` → overlap'а нет.
- Если пользователь добавит блок, который растянется в верхнюю зону (например, вертикальный `<BlockUIContainer>`), он перекроет header — это **известное ограничение** верстки. Зафиксировано в §12 Edge cases.

**Header (верхний колонтитул):**
- Текст: «Договор № {ContractNumber}» или скрыт, если номер пуст (НЕ «Договор б/н» — пользователь явно не указал → нет смысла плодить «б/н» на каждой странице).
- Шрифт: Segoe UI, 7pt, Italic, серый (`#888888`).
- Положение: правый верхний угол, выше основного содержимого (offset 15 DIP от верха, 30 DIP от правого края).
- Только если номер договора указан.

**Footer (нижний колонтитул):**
- Текст: «Страница {X} из {Y}» (слева, italic, gray) + дата договора в формате `dd.MM.yyyy` (справа).
- Шрифт: Segoe UI, 7pt, Italic, серый.
- Положение: низ страницы в зоне `PagePadding.Bottom`.
- Всегда показывается.

### 5.6 Цветовая схема (точное соответствие CSS)

| Элемент | CSS-свойство | WPF-эквивалент | Цвет |
|---|---|---|---|
| Фон шапки таблицы | `background: #d8d8d8` | `TableRow.Background` | `#FFD8D8D8` |
| Границы ячеек | `border: 1px solid #999` | `TableCell.BorderBrush` | `#FF999999` |
| Чётные строки | `tr:nth-child(even) { background: #f0f0f0 }` | `TableRow.Background` | `#FFF0F0F0` |
| Строка итогов | `background: #d5d5d5` | `TableRow.Background` | `#FFD5D5D5` |
| Заголовок КП | `color: #111` | `Paragraph.Foreground` | `#FF111111` |
| Текст клиента | `color: #333` / `color: #111` | `Paragraph.Foreground` | по роли |
| Текст суммы прописью | `color: #444; font-style: italic` | `Paragraph.Foreground` + `FontStyle` | `#FF444444` |
| Блок примечаний | `background: #f9f9f9; border-left: 3px solid #888` | `Section.Background` + `Border` | `#FFF9F9F9` / `#FF888888` |
| Блок доп. КП (итоги) | `background: #f5f5f5; border: 1px solid #ccc` | `Section.Background` + `Border` | `#FFF5F5F5` / `#FFCCCCCC` |
| Текст документа | `color: #111` (основной) | `FlowDocument.Foreground` | `#FF111111` |

### 5.7 Шрифты

| Элемент | Размер | Начертание | Семейство |
|---|---|---|---|
| Заголовок КП | 13.5pt | Bold, letter-spacing: 2px | Segoe UI |
| Строка договора | 9pt | Regular | Segoe UI |
| Метки клиента | 8.5pt | SemiBold | Segoe UI |
| Значения клиента | 9pt | Regular | Segoe UI |
| Таблица (шапка) | 7.5pt | SemiBold | Segoe UI |
| Таблица (тело) | 8.5pt | Regular | Segoe UI |
| Строка итогов | 9pt | Bold | Segoe UI |
| Сумма прописью | 8.5pt | Italic | Segoe UI |
| Примечания | 8.5pt | Regular | Segoe UI |
| Условия | 7.5pt | Regular | Segoe UI |
| Подписи | 9pt | SemiBold | Segoe UI |
| Колонтитулы | 7pt | Italic | Segoe UI |

### 5.8 Блок клиента

WPF-реализация через `Table` (2 колонки: label + value) или `Grid` внутри `BlockUIContainer`.

```
┌──────────────────────────────────────────────────────────┐
│ Заказчик:  Иванов И.И.                         ← label: 82px │
│ Телефон:   +7 999 123-45-67                               │
│ Адрес:     г. Москва, ул. Пушкина, д. 10                  │
└──────────────────────────────────────────────────────────┘
```

- Метка: `FontWeight = SemiBold`, `Foreground = #333`, ширина 82px (≈ 8.5pt × ~12 символов)
- Значение: `Foreground = #111`, нижнее подчёркивание (`BorderBrush = #999`, `BorderThickness = 0,0,0,1`)
- Если поле пустое — строка не показывается
- Интервал между строками: 4px

### 5.9 Блок итогов и доп. КП

**Основной итог:**
```
ИТОГО: 3 600,00 руб.
(Три тысячи шестьсот рублей 00 копеек)
```

**При наличии доп. КП с суммой > 0:**

```
Дополнительные КП
  К данному заказу прилагается КП № 2-1        500,00 руб.

Сумма основного КП:                            3 600,00 руб.
Сумма доп. КП № 2-1:                             500,00 руб.
─────────────────────────────────────────────────────────
ОБЩИЙ ИТОГ:                                    4 100,00 руб.
(Четыре тысячи сто рублей 00 копеек)
```

**При наличии доп. КП без суммы (reference only):**
```
К данному заказу прилагается КП № 2-1
```

### 5.10 Блок подписей

```
Исполнитель _____________          Заказчик _____________
   (подпись, печать)                  (подпись)
```

- `TableCell` (2 колонки, равная ширина)
- Подпись: `BorderBrush = #1a1a1a`, `BorderThickness = 0,0,0,1` (имитация линии)
- Подпись-метка: 7.5pt, `Foreground = #888`, italic

### 5.11 Блок условий (статический)

```
Условия
– Срок действия коммерческого предложения — 5 рабочих дней.
– Оплата производится на основании счёта.
– Цены указаны с учётом стоимости материалов.
```

- Заголовок: 8pt, Bold, uppercase, letter-spacing
- Пункты: 7.5pt, маркер «–» (en-dash)

---

## 6. PDF экспорт (QuestPDF)

### 6.0 Лицензия и runtime-конфигурация (ОБЯЗАТЕЛЬНО ЧИТАТЬ)

> **Перенесено из §6.3 v2.0 — выделено в отдельную секцию как критичный pre-condition.**

QuestPDF требует явного согласия с лицензией **до** первого `Document.Create` — иначе бросает `QuestPDF.Infrastructure.LicenseException` («You must configure QuestPDF's license before using the library…»). Это **обязательное** первое действие в `App.OnStartup`:

```csharp
// MosquitoNetCalculator/App.xaml.cs (в OnStartup, ДО любого окна)
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── QuestPDF Community License (обязательно) ──
        // Должно быть вызвано ОДИН раз в AppDomain, до первого
        // Document.Create(...). Если не вызвать — GeneratePdf выбрасывает
        //        `QuestPDF.Infrastructure.LicenseException` при первой печати.
        // Лицензия Community действительна для компаний с годовым доходом < $1M.
        // При превышении порога — заменить на LicenseType.Professional.
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.EnableDebugging = false; // отключает наложение watermark в production
        // ...
    }
}
```

> **Юридическое замечание:** Community-лицензия QuestPDF требует явного признания в каждом релизе. Должно быть зафиксировано в `DECISIONS.md` (решение #11) как «печать КП и PDF-эспорт работают под Community-лицензией QuestPDF; при выходе компании на $1M+ годового дохода переключить на Professional».

### 6.1 Выбор библиотеки

**QuestPDF** (https://www.questpdf.com/):
- Лицензия: Community MIT (бесплатно до $1M годового дохода)
- NuGet: `QuestPDF` (≈ 1.5 MB)
- Fluent API, похожий на композицию Flutter/SwiftUI виджетов
- Полный контроль над макетом A4
- Встроенная поддержка Unicode/кириллицы через встраивание шрифтов
- Активный проект: 12k+ GitHub stars, регулярные релизы

**Альтернативы (отклонены):**
- `PdfSharp` / `MigraDoc` — устаревший API, нет поддержки .NET 8 из коробки
- `iText7` — AGPL лицензия (требует покупки для коммерции)
- `IronPDF` — платный

### 6.2 API PrintService.ExportPdf()

```csharp
/// <summary>
/// Экспортирует КП в PDF файл через QuestPDF.
/// </summary>
/// <param name="filePath">Путь для сохранения (из SaveFileDialog)</param>
/// <param name="items">Позиции заказа</param>
/// <param name="clientInfo">Данные клиента</param>
/// <param name="totalAmount">Итоговая сумма</param>
/// <param name="amountInWords">Сумма прописью</param>
public void ExportPdf(
    string filePath,
    List<OrderItem> items,
    ClientInfo clientInfo,
    double totalAmount,
    string amountInWords)
```

### 6.3 QuestPDF Document Structure

> **Исправлено в v2.1:** `.Fallback(Fonts.SegoeUI)` заменён на корректный API. См. §6.0 про обязательную настройку `Settings.License`.

```csharp
// App.OnStartup (один раз):
QuestPDF.Settings.License = LicenseType.Community;
QuestPDF.Settings.EnableDebugging = false;

// В печати (PrintService.Pdf.cs):
Document.Create(container =>
{
    container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(16, 30, 16, 14, Unit.Millimetre); // как в HTML @page

        // DefaultTextStyle возвращает TextStyleDescriptor.
        // Метод .Fallback(TextStyle) существует (НЕ string), но для кириллицы
        // в Segoe UI достаточно базового встроенного механизма QuestPDF.
        // Используем константу Fonts.SegoeUI (uppercase I) и float для размера.
        page.DefaultTextStyle(x => x
            .FontFamily(Fonts.SegoeUI)  // Используем встроенную константу
            .FontSize(9f));             // FontSize принимает float

        // Header (повторяется на каждой странице)
        page.Header().Row(row => { ... });       // Договор №

        // Content (рендерится в области минус header/footer)
        page.Content().Column(col =>
        {
            col.Item().Element(BuildTitle);       // КОММЕРЧЕСКОЕ ПРЕДЛОЖЕНИЕ
            col.Item().Element(BuildClientBlock); // Заказчик, телефон, адрес
            col.Item().Element(BuildTable);       // Таблица 12 колонок
            col.Item().Element(BuildTotalBlock);  // Итого, пропись, доп. КП
            col.Item().Element(BuildNotes);       // Примечания
            col.Item().Element(BuildTerms);       // Условия
            col.Item().Element(BuildSignatures);  // Подписи
        });

        // Footer (повторяется на каждой странице)
        page.Footer().Row(row => { ... });       // Страница X из Y | дата
    });
})
.GeneratePdf(filePath);
```

### 6.4 QuestPDF Table (12 колонок)

QuestPDF использует пропорциональные ширины через `ConstantSize` и `RelativeSize`:

```csharp
table.ColumnsDefinition(cols =>
{
    cols.ConstantColumn(20);   // №
    cols.RelativeColumn(3);    // Наименование (15%)
    cols.RelativeColumn(1.5f); // Цвет (7.5%)
    cols.RelativeColumn(1.3f); // Ш, мм
    cols.RelativeColumn(1.3f); // В, мм
    cols.RelativeColumn(1.4f); // Кол-во
    cols.RelativeColumn(1.5f); // Монтаж
    cols.RelativeColumn(1.8f); // Площ./Дл.
    cols.ConstantColumn(25);   // Ед.
    cols.RelativeColumn(1.9f); // Цена
    cols.RelativeColumn(2.3f); // Сумма
    cols.RelativeColumn(2.8f); // Чертёж
});
```

### 6.5 Чертежи в QuestPDF

> **Исправлено в v2.1:** описание теперь соответствует реальности. WPF Geometry — это **внутреннее представление**, а не сериализуемый формат; пререндеринг в PNG — *один* путь, но теряет вектор. Добавлен QuestPDF Canvas как полностью векторный вариант (сохранение dpi-качества при любом масштабе печати).

**Вариант A — PNG prerender через `RenderTargetBitmap`** *(быстрый, но растровый)*:

```csharp
private static byte[] RenderDrawingToPng(DrawingImage drawing, int dpiMultiplier = 2)
{
    var width = (int)drawing.Width;   // в нашем случае 100 (или 40 для текстовых)
    var height = (int)drawing.Height; // 54 (или 20)
    var visual = new DrawingVisual();
    using (var ctx = visual.RenderOpen())
    {
        // Белый фон → PNG без прозрачности (большинство PDF-viewer'ов плохо рендерят альфу)
        ctx.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        ctx.DrawImage(drawing, new Rect(0, 0, width, height));
    }
    var bitmap = new RenderTargetBitmap(
        width * dpiMultiplier, height * dpiMultiplier,
        96, 96, PixelFormats.Pbgra32);
    bitmap.Render(visual);

    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var ms = new MemoryStream();
    encoder.Save(ms);
    return ms.ToArray();
}
```

Минус: при печати на плоттере 600 dpi PNG будет пикселизован. На обычных принтерах (300 dpi) — незаметно.

**Вариант B — QuestPDF Canvas (полностью векторный)** *(рекомендуется для production)*:

QuestPDF имеет `Canvas` API для прямого рисования линий/прямоугольников:

```csharp
container.Canvas((canvas, availableSpace) =>
{
    // canvas.Svg(string svgPath) — поддержка SVG path commands
    // canvas.DrawRectangle(...), DrawLine(...), DrawText(...) — прямой вызов
    // QuestPDF самостоятельно рендерит SVG-вектор без потерь
    canvas.Svg(svgString);
});
```

Минус: дублирование логики чертежей — придётся переписать 13 методов в WPF-стиле `GetDrawingGeometry(...)` И в QuestPDF-стиле `RenderOnCanvas(...)`. Это +400 LOC поддержки.

**Выбираем в v2.2: Single Source of Truth (SVG-строки)** —

Благодаря native-поддержке `canvas.Svg(string)` в QuestPDF API, **полностью отказываемся от абстракции `IDrawingCommand`**.
- `PrintService.Drawings.cs` хранит только исходные SVG-строки (Single Source of Truth).
- Для QuestPDF: строка передаётся напрямую через `canvas.Svg(svgString)` — это гарантирует 100% векторность без переписывания 13 чертежей на C# Canvas API.
- Для WPF Preview: SVG-строка конвертируется в `DrawingImage` (через SVG-парсер или пре-рендер PNG), чтобы избежать зависимости от внешних библиотек в production.
- **Экономия ~400 LOC** по сравнению с IDrawingCommand подходом (v2.1). См. §7.4 для деталей хранилища.

**PNG-fallback** — остался только на случай если библиотека для рендеринга WPF-preview отсутствует. Тогда `ExportPdf` делает PNG и вставляет через `Image()`.

---

## 7. Чертежи товаров (XAML Geometry)

### 7.1 Таблица конвертации SVG → Geometry

| Товар | ViewBox | Тип фигуры | WPF Geometry |
|---|---|---|---|
| **Anwis** | 100×54 | Rect + corner clips (4 шт.) + dashed inner rect | `StreamGeometry` из Path-строки |
| **На навесах** | 100×54 | Rect + 2 hinge circles + dashed axis | `StreamGeometry` + `EllipseGeometry` |
| **Отлив** | 100×36 | L-образный профиль (Path) | `StreamGeometry` |
| **Козырёк** | 100×54 | Rect + angled top line | `StreamGeometry` |
| **Короб** | 100×54 | Double rect (thick frame + inner) | `StreamGeometry` |
| **ПСУЛ** | 100×54 | Thick border rect + inner dashed | `StreamGeometry` |
| **Откос материал** | 100×36 | Trapezoid cross-section | `StreamGeometry` |
| **Работа** | 40×20 | Text only «раб.» | `FormattedText` → `Geometry` |
| **Брус** | 40×20 | Text only «брус» | `FormattedText` → `Geometry` |
| **Пояс** | 40×20 | Text only «пояс» | `FormattedText` → `Geometry` |
| **Доставка** | 40×20 | Text only «дост.» | `FormattedText` → `Geometry` |
| **Уплотнение** | 40×20 | Text only «уплотн.» | `FormattedText` → `Geometry` |
| **Fallback** | 100×54 | Generic rect + dimensions | `StreamGeometry` |

### 7.2 Пример конвертации: Anwis SVG → Geometry

**Было (SVG string):**
```svg
<svg width='100' height='54' viewBox='0 0 100 54'>
    <rect x='12' y='4' width='76' height='40' fill='#f5f5f5' stroke='#444' stroke-width='1.2' rx='1'/>
    <rect x='16' y='8' width='68' height='32' fill='none' stroke='#aaa' stroke-width='0.5' rx='0.5' stroke-dasharray='2,1.5'/>
    <rect x='12' y='4' width='8' height='4' fill='#ddd' stroke='#444' stroke-width='0.6'/>
    <!-- corner clips 3 more -->
    <line x1='12' y1='49' x2='88' y2='49' stroke='#555' stroke-width='0.5'/>
    <!-- dimension lines -->
    <text x='50' y='52' font-size='6.5' ...>1000</text>
    <text x='6' y='26' font-size='6' ... transform='rotate(-90,6,26)'>1000</text>
    <text x='50' y='26' font-size='6' ...>Anwis</text>
</svg>
```

**Стало (WPF Geometry + DrawingImage):**
```csharp
private static DrawingImage GetAnwisDrawing(double width, double height)
{
    var drawing = new DrawingGroup();

    // Main rect
    drawing.Children.Add(new GeometryDrawing(
        new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
        new Pen(new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)), 1.2),
        new RectangleGeometry(new Rect(12, 4, 76, 40), 1, 1)));

    // Inner dashed rect
    drawing.Children.Add(new GeometryDrawing(
        null,
        new Pen(new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)), 0.5)
        {
            DashStyle = new DashStyle(new[] { 2.0, 1.5 }, 0)
        },
        new RectangleGeometry(new Rect(16, 8, 68, 32), 0.5, 0.5)));

    // Corner clips (4)
    foreach (var clip in new[] { (12,4), (80,4), (12,40), (80,40) })
    {
        drawing.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            new Pen(new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)), 0.6),
            new RectangleGeometry(new Rect(clip.Item1, clip.Item2, 8, 4))));
    }

    // Dimension line (width)
    drawing.Children.Add(new GeometryDrawing(
        null,
        new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 0.5),
        new LineGeometry(new Point(12, 49), new Point(88, 49))));

    // Width text
    drawing.Children.Add(new GlyphRunDrawing(
        new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
        BuildGlyphRun($"{width:F0}", 6.5, new Point(50, 52))));

    // Height text (rotated)
    // ...

    return new DrawingImage(drawing);
}
```

### 7.3 Размеры чертежей в ячейке

- Сложные чертежи (Anwis, Отлив, Козырёк, Короб, ПСУЛ, Откос, На навесах): 100×36-54 px
- Простые текстовые (Работа, Брус, Пояс, Доставка, Уплотнение): 40×20 px
- Все чертежи центрированы в ячейке через `Stretch = None` + `HorizontalAlignment = Center`

---

## 8. PrintPreviewWindow (переписанный)

### 8.1 XAML

```xml
<Window x:Class="MosquitoNetCalculator.PrintPreviewWindow"
        Title="Предпросмотр КП"
        Width="1050" Height="780"
        MinWidth="800" MinHeight="600"
        WindowStartupLocation="CenterOwner"
        Background="White">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Toolbar -->
        <Border Grid.Row="0" Background="#F4F5F9" Padding="16,10"
                BorderBrush="#DDE1E8" BorderThickness="0,0,0,1">
            <StackPanel Orientation="Horizontal">
                <Button Content="Печать" Style="{StaticResource PrintPrimaryButton}"
                        Click="BtnPrint_Click" Margin="0,0,10,0"/>
                <Button Content="Сохранить PDF" Style="{StaticResource PrintPrimaryButton}"
                        Click="BtnSavePdf_Click" Margin="0,0,10,0"/>
                <Button Content="Закрыть" Style="{StaticResource PrintGhostButton}"
                        Click="BtnClose_Click"/>
            </StackPanel>
        </Border>

        <!-- FlowDocumentReader -->
        <FlowDocumentReader x:Name="DocumentReader" Grid.Row="1"
            IsToolBarVisible="False"
            IsPageViewEnabled="True"
            IsTwoPageViewEnabled="False"
            IsPrintEnabled="False"
            ViewingMode="Page"/>
    </Grid>
</Window>
```

### 8.2 Code-behind

> **Исправлено в v2.1:** `OnSavePdfRequested` event-pattern заменён на прямую передачу `Action<string>` в конструктор. Reasoning: (а) PrintPreviewWindow не должна «выплевывать» событие — это типичный архитектурный smell; (б) event-подписка лямбдой в ActionBar делает утечку подписки при раннем закрытии окна; (в) тестируемость хуже (нужно мокать подписку).

```csharp
public partial class PrintPreviewWindow : Window
{
    private readonly FlowDocument _document;
    private readonly string _contractNumber;
    private readonly Action<string> _onSavePdf;      // инжектируется снаружи

    public PrintPreviewWindow(
        FlowDocument document,
        string contractNumber,
        Action<string>? onSavePdf = null)
    {
        InitializeComponent();
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _contractNumber = (contractNumber ?? string.Empty).Trim();
        _onSavePdf = onSavePdf ?? NoOpSavePdf;        // null-safe fallback
        DocumentReader.Document = document;
    }

    private static void NoOpSavePdf(string _) { /* SavePdf кнопка disabled в этом случае */ }

    private void BtnPrint_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;  // пользователь нажал Cancel

        var paginator = new PrintPaginator(
            ((IDocumentPaginatorSource)_document).DocumentPaginator,
            _contractNumber,
            DateTime.Now);
        dlg.PrintDocument(paginator, $"КП №{_contractNumber}");
    }

    private void BtnSavePdf_Click(object sender, RoutedEventArgs e)
    {
        // Кнопка «Сохранить PDF» делает ещё одну простую операцию;
        // окно НЕ закрывается (исправлено). Пользователь может потом и распечатать.
        var dlg = new SaveFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = $"КП №{_contractNumber}.pdf"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            _onSavePdf(dlg.FileName);    // throws propagates to catch below
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка сохранения PDF: {ex.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
```

### 8.3 ActionBarControl — обновлённый BtnPrintKp_Click

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

    var document = mw.PrintService.BuildFlowDocument(validItems, mw.ClientInfo, total, amountInWords);
    if (document == null)  // BuildFlowDocument может вернуть null если пустой
    {
        ToastService.ShowToast("Нет данных для печати.", ToastType.Warning);
        return;
    }

    // Прямая передача Action<string> вместо подписки на event.
    // ActionBar НЕ держит ссылку на окно после return → утечки подписки нет.
    var preview = new PrintPreviewWindow(
        document,
        mw.ClientInfo.ContractNumber,
        onSavePdf: filePath =>
        {
            try
            {
                mw.PrintService.ExportPdf(filePath, validItems, mw.ClientInfo, total, amountInWords);
                ToastService.ShowToast($"КП сохранено: {Path.GetFileName(filePath)}", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastService.ShowToast($"Ошибка сохранения PDF: {ex.Message}", ToastType.Error, 8000);
            }
        })
    { Owner = mw };

    preview.ShowDialog();
}
```

**Контрактные отличия v2.1:**
- Окно больше **не закрывается** после SavePdf (исправлено). Пользователь может потом нажать «Печать» — например, распечатать выбранный КП и заодно отправить PDF клиенту.
- `_onSavePdf` через `Action<string>` сохраняет ту же функциональность, но без event overhead и без риска утечки подписки.

### 8.4 Зум по умолчанию

FlowDocumentReader по умолчанию показывает «Page Width». В code-behind после загрузки:

```csharp
Loaded += (s, e) =>
{
    // FlowDocumentReader defaults to PageWidth zoom
    DocumentReader.Zoom = 100; // Fit to page width
};
```

---

## 9. План миграции (9 шагов)

> **Исправлено в v2.1:** добавлены §9.10 Rollback-стратегия + §9.11 atomic версионирование. Без них при ошибке в середине миграции (например, QuestPDF падает на mono-runtime) пользователи потеряют возможность печатать вообще.

### Шаг 1: QuestPDF интеграция

- Добавить `QuestPDF` NuGet пакет (последняя стабильная на момент реализации)
- `QuestPDF.Settings.License = LicenseType.Community;` — добавить первым делом в `App.OnStartup` (см. §6.0)
- Скомпилировать чисто с `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- Тест: `AppLifecycleTests.QuestPdfLicenseConfigured` — read-only проверка, что App.OnStartup содержит вызов

### Шаг 2: Чертежи (Geometry + Canvas API)

- `Services/PrintService.Drawings.cs` — хранит базу SVG-строк товаров (Single Source of Truth).
- WPF-вариант: конвертация SVG-векторных данных в `DrawingImage` (для preview).
- QuestPDF-вариант: передача SVG в `canvas.Svg(svgString)` (векторный PDF, без переписывания).
- Тест: `DrawingsTest.GetDrawingImage_AllProducts_NotNull` + golden-сравнение для Anwis

### Шаг 3: FlowDocument builder

- `Services/PrintService.FlowDocument.cs` — `BuildFlowDocument()`
- Заголовок, блок клиента, блок условий, блок подписей
- Без таблицы товаров (на следующем шаге)
- Тест: `BuildFlowDocument_ReturnsNonNull_WhenHasOneAnwisItem` + golden-сравнение структуры

### Шаг 4: Таблица товаров

- 12-колоночная WPF Table (`TableColumn.Width` через `GridLength(0.15, GridUnitType.Star)`)
- Вставка строк с чертежами (`BlockUIContainer` + `Image` с `DrawingImage`)
- Строка ИТОГО, блок итогов, доп. КП, примечания
- Чередование фона строк (программно через `Background` per row)
- Тест: `BuildFlowDocument_TableHasTwelveColumns` + каждый столбец содержит правильные данные

### Шаг 5: PrintPreviewWindow (нативный)

- Удалить WebView2 XAML
- FlowDocumentReader (с `IsPrintEnabled="False"` — см. §8.5 v2.0)
- Кнопки: Печать | Сохранить PDF | Закрыть
- PrintDialog + PrintPaginator
- **Исправлено в v2.1:** `Action<string> onSavePdf` в конструктор вместо event (см. §8.2)
- Окно **не** закрывается после SavePdf

### Шаг 6: PrintPaginator (колонтитулы)

- `Helpers/PrintPaginator.cs` — реализация в §5.5 v2.1
- Override `PageSize`, `IsPageCountValid`, `PageCount`, `Source`, `GetPage`
- Header: «Договор № {X}» через `FormattedText`
- Footer: «Страница X из Y» + дата через два `FormattedText` в одной строке

### Шаг 7: Удаление старого кода — **ПОСЛЕ всех тестов проходят**

- ❌ `Microsoft.Web.WebView2` из `.csproj`
- ❌ `Resources/print_template.html` + `EmbeddedResource`
- ❌ `PrintService.HtmlBuilder.cs`, `PrintService.SvgDrawer.cs`, `PrintService.Template.cs`
- ❌ `PrintPreviewWindow.xaml` + `.xaml.cs` (старые)
- ❌ `DependencyCheckerService` — методы `IsWebView2Installed`, `WebView2DownloadUrl`, класс можно оставить (VC++ redist check остаётся)
- ❌ `App.xaml.cs` — вызов `CheckAndNotifyAsync()` (но сам метод в DependencyCheckerService — пока оставить, он `private`)
- ✅ Обновить `ActionBarControl.xaml.cs` по §8.3 v2.1

### Шаг 8: Тесты

- Переписать `PrintServiceTests.cs` (18 тестов → новые ~25, см. §11 v2.1)
- Обновить `AppLifecycleTests.cs` — убрать `Print_Template_Has_No_Slash_Dogovor` (тест ссылается на deleted path)
- Обновить `ManualChecklistTests.cs` §7 — теперь о native print и Autosave
- **Новое:** `PrintPaginatorTests` (4 теста) — см. §11.2
- **Новое:** `DrawingsTests` (3 теста) — см. §11.3
- **Новое:** `DataFidelityTests` — см. §11.5 v2.1

### Шаг 9: Верификация

- `dotnet build -c Release` — 0 errors, **0 warnings** (важно: `TreatWarningsAsErrors=true` валит сборку на любом warning)
- `dotnet test` — все тесты проходят (план: 771/771 → ~795/795 после добавления 25+ новых)
- Ручная проверка: сравнить печатный вывод с эталонным HTML (см. §11.5 golden copy)
- `validate-docs.ps1` — все 8 проверок проходят + обновлены DECISIONS / CURRENT_STATE (см. §15)

### Шаг 10: Rollback-стратегия (добавлено в v2.1)

> **Без rollback-флага при серьёзной ошибке QuestPDF (например, на mono-runtime или после изменения лицензии Microsoft) пользователи полностью теряют печать КП. Это неприемлемо для бухгалтерии.**

Гипотетическая ситуация: после deploy v3.44.0 обнаружено, что **QuestPDF (построенный на SkiaSharp)** падает на голой Windows-инсталляции без Visual C++ 2015-2022 Redistributable, без native Skia libs (libHarfBuzz/Skia), или при отсутствии системных шрифтов (Segoe UI и др.) на серверах. Клиенты не могут получить PDF. **Замечание:** QuestPDF **НЕ** зависит от WebView2 / Edge / Internet Explorer (это чистый .NET + Skia native) — более ранние утверждения в этом разделе ошибочны. Реальные причины отката — отсутствие runtime-зависимостей Skia. Решение:

```csharp
// MosquitoNetCalculator/AppSettingsService.cs (добавить поле)
private const string UseNativePrintKey = "UseNativePrint"; // default = true

// В ActionBarControl.BtnPrintKp_Click:
if (!AppSettingsService.GetBool(UseNativePrintKey, defaultValue: true))
{
    // Fallback: печатать через старый HTML+WebView2 путь (re-introduce GenerateKpHtml)
    string html = mw.PrintService.GenerateKpHtml_Legacy(validItems, mw.ClientInfo, total, amountInWords);
    var legacyPreview = new PrintPreviewWindow_Legacy(html) { Owner = mw };
    legacyPreview.ShowDialog();
    return;
}
// ... продолжаем native flow
```

**Где живёт fallback-код:**
- `PrintService.GenerateKpHtml_Legacy` — НЕ удаляется, а переименовывается в `_Legacy` и помечается `[Obsolete("Use BuildFlowDocument instead")]`. Пока `UseNativePrint=true` — не вызывается → JIT-удаляется можно (но WPF x86 single-file не умеет tree-shake, поэтому просто висит в бинаре).
- `PrintPreviewWindow_Legacy` — old-версия окна переименовывается в `_Legacy`, остаётся в проекте.
- `print_template.html` — НЕ удаляется; нужен для rollback.

**Право на включение rollback:**
- Установить `UseNativePrint=false` через реестр или вручную в `settings.json`.
- Текущий default для всех пользователей: `true` (native).
- При rollback: Force-обновление в следующем релизе или горячее через remote-config (расширение).

### Шаг 11: Документационное обновление (по A.R.C. workflow)

> В v2.0 9-шаговый план завершался после тестов. В v2.1 добавлен **отдельный шаг** для A.R.C. compliance — иначе забываем про docs.

- Запустить `what-to-update.ps1 $(git diff --name-only)` после Шага 9
- Обновить `docs/arc/DECISIONS.md` — решение #11 (HTML → FlowDocument + QuestPDF, обоснование license)
- Обновить `docs/arc/MODULES.md` — новые файлы `PrintService.{FlowDocument,Drawings,Pdf}.cs` и `Helpers/PrintPaginator.cs`
- Обновить `docs/arc/CURRENT_STATE.md` — пункт «Последние изменения»
- Обновить `docs/arc/CHEATSHEET.md` — lastVerified до текущей версии
- `what-to-update.ps1` в `documentation-matrix.json` — + `native-print.md` сам становится источником истины (ослабляем?)
- `validate-docs.ps1` — все 8 проверок ОК
- Если были удалены файлы — `gensymbols.ps1` для обновления `SYMBOL_INDEX.md`

---

## 10. Файловая структура (после миграции)

### Новые файлы

```
MosquitoNetCalculator/
  Services/
    PrintService.FlowDocument.cs   // BuildFlowDocument() + private helpers
    PrintService.Drawings.cs       // GetDrawingImage() — 13 товаров
    PrintService.Pdf.cs            // ExportPdf() через QuestPDF
  Helpers/
    PrintPaginator.cs              // DocumentPaginator с колонтитулами
```

### Изменённые файлы

```
MosquitoNetCalculator/
  Services/
    PrintService.cs                     // public API: BuildFlowDocument(), Print(), ExportPdf()
    PrintService.FlowDocument.cs        // BuildFlowDocument() + private helpers (+ new)
    PrintService.Drawings.cs            // GetDrawingGeometry() + IDrawingCommand impl (+ new)
    PrintService.Pdf.cs                 // ExportPdf() через QuestPDF (+ new)
  Helpers/
    PrintPaginator.cs                   // DocumentPaginator с колонтитулами (+ new)
  PrintPreviewWindow.xaml               // FlowDocumentReader вместо WebView2 (rewrite)
  PrintPreviewWindow.xaml.cs            // PrintDialog + SavePdf + Close (rewrite; v2.1 no-auto-close)
  PrintPreviewWindow_Legacy.xaml        // [Obsolete] rollback в Шаге 10 (rename, не удалять)
  PrintPreviewWindow_Legacy.xaml.cs     // [Obsolete] rollback в Шаге 10 (rename, не удалять)
  Controls/
    ActionBarControl.xaml.cs            // BtnPrintKp_Click — FlowDocument вместо HTML (v2.1: Action<string> instead of event)
  Models/
    OrderItem.cs                        // без изменений (DisplayName, KpInstallationDisplay, InstallationLabel уже есть)
  MosquitoNetCalculator.csproj          // +QuestPDF, -WebView2, -print_template.html
  App.xaml.cs                           // +QuestPDF.Settings.License = Community, - CheckAndNotifyAsync()
  AppSettingsService.cs                 // +UseNativePrint bool field (v2.1 rollback)

MosquitoNetCalculator.Tests/
  Services/
    PrintServiceTests.cs                // переписать под FlowDocument (18 → 25 тестов)
    PrintPaginatorTests.cs              // (new) 4 теста
    DrawingsTests.cs                    // (new) 3 теста
    DataFidelityTests.cs                // (new, v2.1) golden copy сравнение
  App/
    AppLifecycleTests.cs                // убрать print_template проверки, +QuestPdfLicenseConfigured
    ManualChecklistTests.cs             // §7 обновить под native flow
```

### Удалённые файлы

```
MosquitoNetCalculator/
  Resources/print_template.html            // РЕНАМИНГ (не удалять) — нужен для §9.10 rollback
  Services/PrintService.HtmlBuilder.cs     // РЕНАМИНГ в HtmlBuilder_Legacy.cs (rollback)
  Services/PrintService.SvgDrawer.cs       // РЕНАМИНГ в SvgDrawer_Legacy.cs (rollback)
  Services/PrintService.Template.cs        // РЕНАМИНГ в Template_Legacy.cs (rollback)
  Services/DependencyCheckerService.cs     // Сохраняется, удаляется только WebView2-часть
  Services/PrintService.Template.cs        ❌
```

---

## 11. Тест-план

### 11.1 PrintServiceTests (переписанные)

| # | Тест | Что проверяет |
|---|---|---|
| T1 | `BuildFlowDocument_ReturnsNull_WhenNoValidItems` | Пустой список → null |
| T2 | `BuildFlowDocument_ReturnsNull_WhenAllZeroTotal` | Все Total = 0 → null |
| T3 | `BuildFlowDocument_ContainsDocHeader` | Заголовок «КОММЕРЧЕСКОЕ ПРЕДЛОЖЕНИЕ» |
| T4 | `BuildFlowDocument_ContainsContractInfo` | Номер и дата договора |
| T5 | `BuildFlowDocument_ContainsClientInfo` | Заказчик, телефон, адрес |
| T6 | `BuildFlowDocument_OmitsEmptyClientFields` | Пустые поля клиента не показываются |
| T7 | `BuildFlowDocument_ContainsItemsTable` | Таблица с 12 колонками |
| T8 | `BuildFlowDocument_TableRowsCount` | Количество строк = validItems + 1 (итого) |
| T9 | `BuildFlowDocument_ItemData_Correct` | Каждая ячейка содержит правильные данные |
| T10 | `BuildFlowDocument_AnwisShowsCalcSizes` | Anwis ББ60 показывает 1002×970, не 1000×1000 |
| T11 | `BuildFlowDocument_AnticatItem_HasSuffix` | «Anwis (Антикошка)» в DisplayName |
| T12 | `BuildFlowDocument_InstallMark_Mode0` | Монтаж включён → ✓ |
| T13 | `BuildFlowDocument_InstallMark_Mode1` | Без монтажа → ✗ |
| T14 | `BuildFlowDocument_InstallMark_Mode2` | В конструкцию → В |
| T15 | `BuildFlowDocument_InstallMark_NotApplicable` | Отлив → — |
| T16 | `BuildFlowDocument_ContainsTotal` | Сумма прописью в документе |
| T17 | `BuildFlowDocument_ContainsAdditionalKp` | Доп. КП блок |
| T18 | `BuildFlowDocument_GrandTotal_WithAdditionalKp` | ОБЩИЙ ИТОГ = осн. + доп. |
| T19 | `BuildFlowDocument_ContainsNotes_WhenPresent` | Примечания видны |
| T20 | `BuildFlowDocument_OmitsNotes_WhenEmpty` | Примечания скрыты |
| T21 | `BuildFlowDocument_EvenRows_HaveBackground` | Чётные строки имеют серый фон |
| T22 | `BuildFlowDocument_HeaderRow_HasDarkBackground` | Шапка таблицы — #d8d8d8 |
| T23 | `BuildFlowDocument_TotalRow_HasBoldStyle` | Строка итогов — Bold + #d5d5d5 |
| T24 | `ExportPdf_CreatesFile` | PDF файл создаётся, первые 4 байта — `%PDF` (валидный PDF-header) |
| T25 | `ExportPdf_ThrowsOnInvalidPath` | `IOException` при записи на read-only путь (например, `C:\Windows\System32\test.pdf`) |
| T26 | `ExportPdf_HasDocumentTitle` | PDF metadata содержит title «Коммерческое предложение N-M» |
| T27 | `ExportPdf_UsesCommunityLicense` | Проверка что GeneratePdf НЕ бросает license exception (`Settings.License == Community` сработал) |

### 11.2 PrintPaginatorTests (новые)

| # | Тест | Что проверяет |
|---|---|---|
| P1 | `GetPage_AddsHeader_WithContractNumber` | Header содержит «Договор № 1-5» |
| P2 | `GetPage_AddsFooter_WithPageNumber` | Footer содержит «Страница 1 из 3» |
| P3 | `GetPage_ReturnsSameContent` | Содержимое не повреждено |
| P4 | `GetPage_HeaderOmitsContract_WhenNull` | «Договор б/н» при пустом номере |

### 11.3 DrawingsTests (новые)

| # | Тест | Что проверяет |
|---|---|---|
| D1 | `GetDrawingImage_Anwis_NotNull` | Anwis чертёж ≠ null |
| D2 | `GetDrawingImage_AllProducts_NotNull` | Все 13 товаров возвращают валидный DrawingImage |
| D3 | `GetDrawingImage_TextOnlyProducts_Smaller` | Работа/Брус/Пояс/Доставка/Уплотнение — 40×20 |

### 11.4 Golden-copy data fidelity тесты (добавлено в v2.1)

> **Критический контракт F4 из §3.1: «Точная копия вёрстки текущего HTML (шрифты, отступы, цвета, ширина колонок)». Без golden-copy теста этот контракт проверяется только визуально.**

Создаётся канонический dataset — **тот же, что в существующих HTML-тестах** (`GenerateKpHtml_*`), на котором проверяется, что ВСЕ нужные данные присутствуют в `FlowDocument`:

```csharp
// MosquitoNetCalculator.Tests/Services/DataFidelityTests.cs (new file)

public class DataFidelityTests
{
    // Golden dataset — копия существующего теста
    // GenerateKpHtml_ReturnsHtml_WithValidItems
    private static List<OrderItem> GoldenItems() => new()
    {
        new()
        {
            Name = "Anwis", Color = "Белый",
            Width = 1000, Height = 1000,
            Quantity = 1, Price = 1800, Total = 1.8,
            InstallationMode = 0
        }
    };
    private static ClientInfo GoldenClient() => new()
    {
        ContractNumber = "1-5",
        ContractDate = new DateTime(2026, 1, 15),
        ClientName = "Иванов И.И.",
        ClientPhone = "+7 999 123-45-67",
        ClientAddress = "г. Москва, ул. Пушкина, д. 10"
    };

    [Fact]
    public void BuildFlowDocument_Golden_Anwis_ContainsAllFieldValues()
    {
        var doc = new PrintService().BuildFlowDocument(
            GoldenItems(), GoldenClient(), 1.8, "Один рубль 80 копеек");

        var flatText = FlowDocumentTextExtractor.ExtractText(doc);
        Assert.Contains("1-5", flatText);          // contract number
        Assert.Contains("15.01.2026", flatText);   // contract date
        Assert.Contains("Anwis", flatText);        // product name
        Assert.Contains("Белый", flatText);        // color
        Assert.Contains("1000", flatText);         // width (×2)
        Assert.Contains("Иванов И.И.", flatText);  // client name
        Assert.Contains("+7 999 123-45-67", flatText);
        Assert.Contains("г. Москва", flatText);
        Assert.Contains("1 800,00", flatText);     // price format
        Assert.Contains("✓", flatText);             // install mark mode 0
    }

    [Fact]
    public void BuildFlowDocument_Golden_NumericDisplayMatchesHtmlOutput()
    {
        // Проверка: для Anwis ББ60 отображаются 1002/970, как в HTML-тесте
        var items = new List<OrderItem>
        {
            new()
            {
                Name = "Anwis", Color = "Белый",
                Width = 1002, Height = 970,
                Quantity = 1, Price = 1800, Total = 1749.60,
                AnwisSizeMode = AnwisSizeMode.Брусбокс60
            }
        };
        var doc = new PrintService().BuildFlowDocument(
            items, GoldenClient(), 1749.60, "Одна тысяча семьсот сорок девять рублей 60 копеек");

        var text = FlowDocumentTextExtractor.ExtractText(doc);
        // В FlowDocument эти числа попадают в ячейки таблицы WIDTH/HEIGHT — 
        // text-извлечение должно их найти.
        Assert.Contains("1002", text);
        Assert.Contains("970", text);
        Assert.DoesNotContain("1000", text);  // raw input НЕ должен показываться

        // Также проверить через html-эквивалент (тот же dataset)
        var html = new PrintService().GenerateKpHtml(items, GoldenClient(), 1749.60, "");
        Assert.Contains("1002", html);
        Assert.Contains("970", html);
        // Оба вывода содержат одинаковые числа → data fidelity OK.
    }

    // утилита извлечения всех текстов из FlowDocument (в тестовом проекте)
    internal static class FlowDocumentTextExtractor
    {
        public static string ExtractText(FlowDocument doc)
        {
            var sb = new StringBuilder();
            foreach (var block in doc.Blocks)
                ExtractBlock(block, sb);
            return sb.ToString();
        }
        private static void ExtractBlock(Block block, StringBuilder sb)
        {
            switch (block)
            {
                case Paragraph p:
                    sb.Append(p.Text);
                    sb.Append('\n');
                    break;
                case Table t:
                    foreach (var rowGroup in t.RowGroups)
                        foreach (var row in rowGroup.Rows)
                            foreach (var cell in row.Cells)
                                foreach (var inner in cell.Blocks)
                                    ExtractBlock(inner, sb);
                    break;
                case Section sec:
                    foreach (var inner in sec.Blocks)
                        ExtractBlock(inner, sb);
                    break;
                case BlockUIContainer container:
                    // Drawings пропускаем — но их содержимое распарсить нельзя
                    // через Block API. Если тесту нужно проверить клетку «Чертёж»,
                    // проверять через дополнительный API в Drawing.Image.
                    break;
            }
        }
    }
}
```

> Без `FlowDocumentTextExtractor` data-fidelity тесты пришлось бы делать через `XPath`-like обход дерева — что в 3× медленнее и в 2× менее читаемо. Утилита инкапсулирует обход.

### 11.5 Обновляемые тесты

| Файл | Изменение |
|---|---|
| `AppLifecycleTests.cs` | Удалить `Print_Template_Has_No_Slash_Dogovor`; добавить `QuestPdfLicenseConfigured (smoke test)` |
| `ManualChecklistTests.cs` | §7 обновить под native flow (FlowDocument, PrintPaginator header/footer, PDF export) |

---

## 12. Граничные случаи

| Сценарий | Ожидаемое поведение |
|---|---|
| Нет позиций в заказе | Toast «Добавьте хотя бы одну позицию», окно не открывается |
| Все позиции с Total = 0 | Исключаются из печати (валидация в BuildFlowDocument) |
| 1 позиция | Одностраничный документ с колонтитулами |
| 50+ позиций | Многостраничный документ, thead повторяется, колонтитулы на каждой странице |
| Длинное название товара (>30 символов) | Перенос текста в ячейке Наименование |
| Очень длинная заметка (>500 символов) | Перенос текста в блоке примечаний |
| Спецсимволы в имени/адресе | WPF TextBlock безопасен для Unicode (XSS не применим) |
| Доп. КП без номера | «Дополнительное КП» (без №) |
| 2+ доп. КП | «Дополнительные КП» (мн. число) |
| Пустой номер договора | «Договор б/н» в заголовке, «б/н» в колонтитулах |
| Принтер не установлен | PrintDialog показывает ошибку Windows |
| Отмена в диалоге печати | Ничего не происходит, окно остаётся открытым |
| Отмена в SaveFileDialog | Ничего не происходит |
| Ошибка записи PDF (нет прав) | MessageBox с ошибкой, файл не создаётся |
| Ошибка записи PDF (диск полон) | MessageBox с ошибкой |
| QuestPDF не загружен (редкое) | `QuestPDF.Infrastructure.LicenseException` с инструкцией по v2.2 §6.0 |
| Rollback включён (`UseNativePrint=false`) | Используется `PrintService.GenerateKpHtml_Legacy` + WebView2 + `print_template.html` (если ещё существует). Без rollback-флага приложение в принципе не запустится. |
| Очень длинный заголовок товара > 30 символов (v2.1) | WPF TableCell по умолчанию обрезает (`Overflow=hidden`). **ИСПРАВЛЕНО:** в `PrintService.FlowDocument.cs` каждой name-cell явно проставляется `TextWrapping=Wrap`. Тест `FlowTableNameCell_HasTextWrappingWrap`. |
| DPI scaling > 150% (v2.1) | `RenderTargetBitmap` для PNG рендерит в DIPs, что для `Brushes.Black` + `Pen(Brushes.Black, 1, geometry)` сохраняет векторность. Но BitmapEncoder растрирует → при печати на 1200 dpi принтере возможны артефакты на тонких линиях. **ИСПРАВЛЕНО:** v2.1 §6.5 — Canvas-вариант чертежей. |
| `IsAnticat` на не-Anwis товаре (v2.1) | См. CHEATSHEET#12: `AnticatApplicableProducts` HashSet ограничивает включение «Антикошка». Если `IsAnticat=true` для товара вне HashSet — поведение `DisplayName` + `KpInstallationDisplay` + `Total` формулы не должны меняться. Зафиксировано в `OrderItemTests.Anticat_IgnoredForNonApplicableProduct`. |
| Печать в однопоточной подписке | `BuildFlowDocument` должен быть thread-safe (нет UI-операций). Реализация через `new FlowDocument()` + `Blocks.Add(...)` — не thread-safe по умолчанию (зависит от Dispatcher). **ИСПРАВЛЕНО:** BuildFlowDocument вызывать ТОЛЬКО из UI-потока; в `ActionBar` это уже соблюдено. Тест-смоук: параллельные вызовы НЕ падают в STA dispatcher. |

---

## 13. Зависимости (после миграции)

### NuGet пакеты

| Пакет | Версия | Статус |
|---|---|---|
| `QuestPDF` | latest stable | ✅ Добавить |
| `Microsoft.Web.WebView2` | 1.0.2210.55 | ❌ Удалить |

### Ресурсы

| Ресурс | Статус |
|---|---|
| `Resources/print_template.html` (EmbeddedResource) | ❌ Удалить |
| `Resources/update-log.json` (EmbeddedResource) | ✅ Оставить |
| `Resources/app_icon.ico` (Resource) | ✅ Оставить |

---

## 14. Критерии готовности

1. ✅ `dotnet build -c Release` — 0 errors, 0 warnings
2. ✅ `dotnet test` — все тесты проходят (>740 pass, 0 fail)
3. ✅ Предпросмотр открывается ≤ 1 сек (против 2-3 сек с WebView2)
4. ✅ FlowDocument содержит те же данные, что и текущий HTML
5. ✅ Чертежи идентичны SVG-версии (визуальная проверка)
6. ✅ Печать на принтер работает (PrintDialog → физический принтер)
7. ✅ «Сохранить PDF» создаёт валидный PDF файл
8. ✅ Колонтитулы (№ договора + нумерация страниц) на каждой странице
9. ✅ Цветовая схема точно соответствует CSS
10. ✅ Пакет `Microsoft.Web.WebView2` удалён из зависимостей
11. ✅ Никаких HTML-файлов, temp-файлов, WebView2-кода в проекте

---

## 15. Связанные изменения (не в скоупе, но задокументированы)

| Задача | Описание | Приоритет |
|---|---|---|
| Удаление `DependencyCheckerService.CheckAndNotifyAsync()` из `App.xaml.cs` | Часть миграции (Шаг 7) | P0 |
| Удаление `WebView2DownloadUrl` и `IsWebView2Installed` из `DependencyCheckerService` | Часть миграции (Шаг 7) | P0 |
| Обновление `what-to-update.ps1` и `documentation-matrix.json` | A.R.C. документация | P1 |
| Обновление `DECISIONS.md` — решение #11 (HTML→FlowDocument) | A.R.C. документация | P1 |
| Обновление `MODULES.md` — новые файлы PrintService | A.R.C. документация | P1 |

---

## 16. История изменений спецификации

| Дата | Версия | Изменения |
|---|---|---|
| 2026-07-03 | 2.1 | Pre-implementation bugfix: §5.2 убран несуществующий `DocumentPaginatorWrapper.PixelToDip`; §5.5 даны полные реализации `PrintPaginator` (override `IsPageCountValid`/`Source`/`PageSize`); §6.0+§6.3 обязательная настройка `QuestPDF.Settings.License = Community` + исправлен несуществующий `.Fallback(Fonts.SegoeUI)` API; §6.5 добавлен QuestPDF Canvas вариант чертежей (вектор, а не PNG); §8.2+§8.3 заменён event-pattern на `Action<string>` (нет утечки подписки); §9.10 введён rollback-флаг `UseNativePrint` на случай непредвиденных падений QuestPDF; §11.4 введён `DataFidelityTests` с golden-copy сравнением FlowDocument ↔ HTML на одном dataset. |
| 2026-07-02 | 2.0 | Полная переработка по результатам 4 раундов интервью: PDF через QuestPDF, колонтитулы с № договора, точные CSS-цвета, единый PrintService, Segoe UI, полное удаление HTML |
| (ранее) | 1.0 | Первая версия спеки (базовый FlowDocument, без PDF, без деталей интервью) |
