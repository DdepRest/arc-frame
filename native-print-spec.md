# Нативная печать КП (Native Print) — Спецификация

## 1. Резюме

Замена текущей печати через WebView2/браузер на нативную WPF-печать через `PrintDialog` + `FlowDocument`.
Полный отказ от HTML-шаблона и WebView2.

---

## 2. Текущее состояние

### Текущий флоу печати

```
ActionBarControl.BtnPrintKp_Click()
  → PrintService.GenerateKpHtml() — генерирует HTML из данных заказа
  → PrintPreviewWindow — открывает WebView2, загружает HTML из temp-файла
  → window.print() — открывает диалог печати браузера (Chromium)
```

### Файлы, участвующие в печати

| Файл | Роль | Судьба |
|---|---|---|
| `Services/PrintService.cs` | Генерация HTML из OrderItem + ClientInfo | ✅ Переписать под FlowDocument |
| `Resources/print_template.html` | HTML-шаблон + CSS | ❌ Удалить |
| `PrintPreviewWindow.xaml` | Окно предпросмотра (WebView2) | ✅ Переписать под FlowDocumentReader |
| `PrintPreviewWindow.xaml.cs` | Загрузка HTML, вызов window.print(), fallback | ✅ Переписать: FlowDocument + PrintDialog |
| `Controls/ActionBarControl.xaml.cs` | Кнопка «Печать КП», вызов PrintService | ✅ Минимальные правки (тип возврата) |
| `Services/PrintService.cs` — `GetDrawingSvg()` | SVG-строки для каждого типа товара | ✅ Конвертировать в WPF Geometry |
| `MosquitoNetCalculator.csproj` | `<EmbeddedResource>` для print_template.html | ❌ Удалить ресурс |
| `MosquitoNetCalculator.Tests/Services/PrintServiceTests.cs` | 16 тестов HTML-генерации | ✅ Переписать тесты под FlowDocument |
| `MosquitoNetCalculator.Tests/App/AppLifecycleTests.cs` | Проверяет наличие print_template.html | ❌ Удалить секцию про print_template |

### Зависимость от WebView2

- Пакет `Microsoft.Web.WebView2` (v1.0.2210.55)
- Требует WebView2 Evergreen Runtime на машине пользователя
- fallback: открыть в браузере, fallback: сохранить на рабочий стол
- Временные файлы .html в %TEMP%

---

## 3. Требования (из обсуждения с пользователем)

### Функциональные

| # | Требование | Приоритет |
|---|---|---|
| F1 | Использовать WPF `PrintDialog` + `FlowDocument` вместо WebView2 | P0 |
| F2 | Сохранить окно предпросмотра (переписать под `FlowDocumentReader`) | P0 |
| F3 | Полная конвертация HTML/CSS дизайна в WPF, никакого HTML | P0 |
| F4 | Формат A4, книжная ориентация, поля 16-30 мм | P0 |
| F5 | SVG-рисунки продуктов → WPF `StreamGeometry` / `Path` | P0 |
| F6 | Колонтитулы на каждой странице: сверху «КП №...» (или «б/н», если номер пуст), снизу «Страница X из Y» + дата | P1 |
| F7 | FlowDocument (автоперенос текста) — не FixedDocument | P0 |
| F8 | FlowDocumentReader зум по умолчанию: Fit Page Width (100%) | P0 |
| F9 | Чередование фона строк таблицы (чётные — серый, как в HTML) — программно через TableRow.Background | P1 |

### Нет requirements (из обсуждения)

| Решение | Обоснование |
|---|---|
| ❌ **Не делать «Сохранить как PDF» сейчас** | Пользователь предложил вместо этого «Сохранить в эксель» — отдельная фича |
| ❌ **Не делать выбор формата A4/A3** | Только A4 |
| ❌ **Не оставлять WebView2 fallback** | После отладки WPF-печати WebView2 удаляется полностью |

---

## 4. Дизайн решения

### 4.1 Новая архитектура

```
ActionBarControl.BtnPrintKp_Click()
  → PrintService2.CreateFlowDocument() — создаёт FlowDocument из данных заказа
  → PrintPreviewWindow2 — показывает FlowDocumentReader
  → кнопка «Печать» → PrintDialog.PrintDocument()
  → кнопка «Закрыть»
```

### 4.2 Классы

#### `Services/PrintService.cs` — переработка

```csharp
public class PrintService
{
    // НОВЫЙ: создаёт FlowDocument для предпросмотра/печати
    public FlowDocument CreateFlowDocument(
        List<OrderItem> items,
        ClientInfo clientInfo,
        double totalAmount,
        string amountInWords)

    // СТАРЫЙ: GenerateKpHtml — удалить
    // СТАРЫЙ: LoadTemplate — удалить (print_template.html не нужен)
    // СТАРЫЙ: FillTemplate — удалить
    // СТАРЫЙ: EscapeHtml — удалить
    // СТАРЫЙ: GetDrawingSvg → НОВЫЙ: GetDrawingGeometry (возвращает Geometry)
    private static Geometry GetDrawingGeometry(string name, double width, double height)
}
```

#### `Models/PrintDocumentBuilder.cs` — [НОВЫЙ]

Вспомогательный класс для построения FlowDocument. Инкапсулирует:

- Создание документа (A4 размеры, поля, шрифты)
- Заголовок документа («Коммерческое предложение»)
- Блок заказчика (имя, телефон, адрес)
- Таблица товаров (12 колонок) — через `Table`
- Блок итогов (сумма прописью, доп. КП)
- Блок примечаний
- Блок условий
- Блок подписей
- Колонтитулы (HeaderFooter)

```csharp
public static class PrintDocumentBuilder
{
    public static FlowDocument BuildDocument(
        List<OrderItem> validItems,
        ClientInfo clientInfo,
        double totalAmount,
        string amountInWords)
}
```

#### `PrintPreviewWindow.xaml` — переработка

```xml
<!-- ВМЕСТО WebView2 → FlowDocumentReader -->
<FlowDocumentReader x:Name="DocumentReader" Grid.Row="1"
    IsToolBarVisible="False"
    IsPageViewEnabled="True"
    IsTwoPageViewEnabled="False"
    IsPrintEnabled="False"
    ViewingMode="Page"/>
```

```csharp
// ВМЕСТО WebView2 + HTML + temp files
public partial class PrintPreviewWindow : Window
{
    public PrintPreviewWindow(FlowDocument document)
    {
        InitializeComponent();
        DocumentReader.Document = document;
    }

    private void BtnPrint_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() == true)
        {
            // Масштабируем FlowDocument под выбранный принтер
            DocumentReader.Document.PageHeight = dlg.PrintableAreaHeight;
            DocumentReader.Document.PageWidth = dlg.PrintableAreaWidth;
            dlg.PrintDocument(
                ((IDocumentPaginatorSource)DocumentReader.Document).DocumentPaginator,
                "Коммерческое предложение");
        }
    }
}
```

### 4.3 Макет A4 (как в текущем HTML)

**Внутренняя область:** 210×297 мм, поля: верх 30 мм, низ 14 мм, бока 16 мм.

```
┌─────────────────────────────────────┐
│  КП № 1-5               Header     │  ← колонтитул
├─────────────────────────────────────┤
│  КОММЕРЧЕСКОЕ ПРЕДЛОЖЕНИЕ          │
│  Договор № 1-5 от 15.01.2026       │
├─────────────────────────────────────┤
│  Заказчик: Иванов И.И.             │
│  Телефон: +7 999 123-45-67         │
│  Адрес: г. Москва, ул. Пушкина     │
├─────────────────────────────────────┤
│  ┌──────┬──────┬────┬────┬──┬──┐  │
│  │ №  │Наим. │Цвет│Ш,мм│В,мм│К-во│... │
│  ├──────┼──────┼────┼────┼──┼──┤  │
│  │  1  │Anwis │Бел │1000│2000│ 2 │... │
│  └──────┴──────┴────┴────┴──┴──┘  │
│  ИТОГО:                  3 600,00 │
├─────────────────────────────────────┤
│  ИТОГО: 3 600,00 руб.              │
│  (Три тысячи шестьсот руб. 00 коп) │
├─────────────────────────────────────┤
│  Условия...                         │
├─────────────────────────────────────┤
│  Исполнитель        Заказчик        │
│  _____________      _____________   │
├─────────────────────────────────────┤
│  Страница 1 из 2    23.06.2026     │  ← колонтитул
└─────────────────────────────────────┘
```

### 4.4 SVG → WPF Geometry

Каждый SVG-рисунок конвертируется в WPF `StreamGeometry`:

| Товар | Размер viewBox | Тип фигуры |
|---|---|---|
| Anwis | 100×54 | Rectangle + corner clips (4 path ops) |
| На навесах | 100×54 | Rectangle + 2 hinge circles (ellipse) |
| Отлив | 100×36 | Cross-section path (L-shaped) |
| Козырёк | 100×54 | Rectangle with angled top line |
| Короб | 100×54 | Double rectangle (thick frame) |
| ПСУЛ | 100×54 | Thick border rectangle |
| Откос материал | 100×36 | Trapezoid cross-section |
| Работа/Брус/Пояс/Доставка/Уплотнение | 40×20 | Text label only |

Для сложных фигур используются `StreamGeometryContext` (Arc, Bezier, Line).
Каждая получает `DrawingImage` для отображения в ячейке таблицы FlowDocument.

### 4.5 FlowDocument Table

Таблица КП содержит 12 колонок. В WPF `Table`:

```xml
<Table>
    <Table.Columns>
        <TableColumn Width="3.5*"/>  <!-- № -->
        <TableColumn Width="15*"/>   <!-- Наименование -->
        <TableColumn Width="7.5*"/>  <!-- Цвет -->
        <TableColumn Width="6.5*"/>  <!-- Ш, мм -->
        <TableColumn Width="6.5*"/>  <!-- В, мм -->
        <TableColumn Width="7*"/>    <!-- Кол-во -->
        <TableColumn Width="7.5*"/>  <!-- Монтаж -->
        <TableColumn Width="9*"/>    <!-- Площ./Дл. -->
        <TableColumn Width="4*"/>    <!-- Ед. -->
        <TableColumn Width="9.5*"/>  <!-- Цена -->
        <TableColumn Width="11.5*"/> <!-- Сумма -->
        <TableColumn Width="14*"/>   <!-- Чертёж -->
    </Table.Columns>
</Table>
```

### 4.6 Колонтитулы

FlowDocument не поддерживает нативные колонтитулы. Решение:
- Использовать `PageContent` с `FixedPage` для нижнего колонтитула ИЛИ
- Разделить содержимое на секции и добавить колонтитул как часть содержимого ИЛИ
- Использовать `PageHeaderFooter` через кастомный `DocumentPaginator`

**Рекомендация:** Кастомный `DocumentPaginator`, который добавляет колонтитулы на каждую страницу.

```csharp
internal class PrintPaginator : DocumentPaginator
{
    private readonly DocumentPaginator _source;
    // Добавляет Header (КП №...) и Footer (стр. X из Y) на каждую страницу
}
```

### 4.7 Технические ограничения FlowDocument

| Ограничение | Решение |
|---|---|
| **`TableCell` не поддерживает `Image` напрямую** | Использовать `BlockUIContainer` внутри `TableCell`, в него поместить `Image` |
| **`Table` не поддерживает чередование фона строк** | Программно задавать `TableRow.Background` для чётных строк (как сейчас в CSS: `tr:nth-child(even)`) |
| **`FlowDocument` не имеет нативных колонтитулов** | Кастомный `DocumentPaginator`-декоратор: обернуть существующий `DocumentPaginator` из `FlowDocument`, перехватить `DocumentPage.Visual` и добавить header/footer |
| **`DocumentPaginator` не может модифицировать страницу постфактум** | `DocumentPage.Visual` — это `ContainerVisual`. После получения страницы нужно: 1) создать новый `ContainerVisual`, 2) добавить header/footer как дочерние `DrawingVisual`, 3) добавить оригинальный `Visual` |
| **Шрифт 8.5pt в таблице** | По умолчанию `FlowDocument` использует 12pt. Все `Paragraph` внутри таблицы должны явно задавать `FontSize="8.5pt"` |
| **12 колонок на A4** | Сумма `Width="3.5+15+7.5+6.5+6.5+7+7.5+9+4+9.5+11.5+14"` = 101.5*. Если вылезает за край — скорректировать пропорции. Использовать `TableCell.TextAlignment` для выравнивания |
| **`FlowDocumentReader` / `FlowDocumentScrollViewer`** | `FlowDocumentReader` тяжелее. Если не нужны поиск и несколько режимов просмотра — использовать `FlowDocumentScrollViewer` с `IsToolBarVisible="False"` |

### 4.8 Диаграмма классов

```
┌──────────────────────────────────────────────────────────┐
│                    PrintPreviewWindow                    │
│  ┌────────────────────────────────────────────────┐     │
│  │ FlowDocumentReader (или FlowDocumentScrollViewer) │     │
│  │   Document = PrintDocumentBuilder.BuildDocument()  │     │
│  └────────────────────────────────────────────────┘     │
│  [Печать] → PrintDialog → PrintPaginator → принтер      │
│  [Закрыть]                                              │
└──────────────────────────────────────────────────────────┘
                      ▲
                      │ FlowDocument
┌──────────────────────────────────────────────────────────┐
│               PrintDocumentBuilder (static)               │
│  BuildDocument(items, clientInfo, total, amountInWords)  │
│    → создаёт FlowDocument с:                            │
│       - Свойства страницы (A4, поля, шрифты)             │
│       - HeaderBlock (КОММЕРЧЕСКОЕ ПРЕДЛОЖЕНИЕ)           │
│       - ClientBlock (заказчик, телефон, адрес)            │
│       - ItemsTable (12 колонок, чередование фона)        │
│       - TotalBlock (сумма, пропись, доп. КП)             │
│       - NotesBlock (примечания)                          │
│       - TermsBlock (условия)                             │
│       - SignatureBlock (подписи)                         │
└──────────────────────────────────────────────────────────┘
                      ▲
                      │ Geometry
┌──────────────────────────────────────────────────────────┐
│                    PrintService                          │
│  GetDrawingGeometry(name, width, height) → Geometry      │
│    - Anwis → PathGeometry (frame + corner clips)         │
│    - Отлив → PathGeometry (L-cross-section)              │
│    - Козырёк → PathGeometry (trapezoid)                  │
│    - Короб → PathGeometry (double rect)                  │
│    - ПСУЛ → PathGeometry (thick border)                  │
│    - Откос → PathGeometry (trapezoid)                    │
│    - На навесах → PathGeometry (rect + circles)          │
│    - Работа/Брус/Пояс/Доставка/Уплотнение → null (text)  │
└──────────────────────────────────────────────────────────┘
                      ▲
┌──────────────────────────────────────────────────────────┐
│               PrintPaginator (internal)                   │
│  - Декоратор над FlowDocument.DocumentPaginator          │
│  - GetPage(pageNumber):                                  │
│    1. Получить оригинальную DocumentPage                 │
│    2. Создать ContainerVisual                            │
│    3. Добавить DrawingVisual с header (КП №...)          │
│    4. Добавить DrawingVisual с footer (стр. X из Y)      │
│    5. Добавить оригинальный Visual (содержимое)          │
│    6. Вернуть новую DocumentPage                         │
└──────────────────────────────────────────────────────────┘
```

---

## 5. План миграции

### Шаг 0: Подготовка

- Собрать эталонный HTML-вывод для каждого сценария (с/без доп. КП, с/без заметок, много позиций)
- Сравнивать с WPF-выводом при разработке

### Шаг 1: PrintDocumentBuilder (новый файл)

- `Models/PrintDocumentBuilder.cs`
- Создание FlowDocument с A4-page setup
- Заголовок, блок клиента, блок условий, блок подписей
- Без таблицы товаров (на следующем шаге)
- Unit-тесты на содержимое документа

### Шаг 2: Таблица товаров

- 12-колоночная таблица в FlowDocument
- Вставка строк для каждой позиции (номер, наименование, цвет, размеры, кол-во, монтаж, площадь, цена, сумма, чертёж)
- Строка ИТОГО
- Блок итогов (сумма прописью)
- Доп. КП блок
- Примечания блок
- Тесты на каждый тип данных

### Шаг 3: SVG → Geometry

- Метод `GetDrawingGeometry()` вместо `GetDrawingSvg()`
- Все 5 SVG-рисунков в WPF пути
- Текстовые метки для простых товаров
- `DrawingImage` для вставки в ячейку таблицы
- Визуальная верификация: сравнить с текущим HTML-выводом

### Шаг 4: Колонтитулы

- Кастомный `DocumentPaginator` с header/footer
- Верх: «КП № [номер]»
- Низ: «Страница X из Y» + дата

### Шаг 5: PrintPreviewWindow

- Замена WebView2 на `FlowDocumentReader`
- Новая кнопка «Печать» → `PrintDialog`
- Удаление: WebView2, временные файлы, fallback-код
- Обновление XAML (новые стили под тему)

### Шаг 6: Удаление старого кода

- Удалить `Resources/print_template.html`
- Удалить `GenerateKpHtml()`, `LoadTemplate()`, `FillTemplate()`, `EscapeHtml()` из PrintService
- Удалить `GetDrawingSvg()` (заменён на `GetDrawingGeometry`)
- Удалить пакет `Microsoft.Web.WebView2` из .csproj
- Удалить WebView2 runtime dependency из документации

### Шаг 7: Тесты

- Переписать `PrintServiceTests.cs` под FlowDocument:
  - `CreateFlowDocument_ReturnsNull_WhenNoValidItems`
  - `CreateFlowDocument_ContainsClientInfo`
  - `CreateFlowDocument_ContainsItemsTable`
  - `CreateFlowDocument_ContainsTotal`
  - `CreateFlowDocument_ContainsAdditionalKp`
  - `CreateFlowDocument_ContainsNotes`
  - `CreateFlowDocument_ContainsInstallMark`
  - `CreateFlowDocument_PaginatedCorrectly`
- Удалить тесты, проверяющие HTML-строки

---

## 6. Файлы: создание, изменение, удаление

### Новые файлы
| Файл | Содержимое |
|---|---|
| `MosquitoNetCalculator/Models/PrintDocumentBuilder.cs` | Построение FlowDocument |
| `MosquitoNetCalculator/Helpers/PrintPaginator.cs` | Кастомный DocumentPaginator с колонтитулами |

### Изменяемые файлы
| Файл | Изменения |
|---|---|
| `MosquitoNetCalculator/Services/PrintService.cs` | `GetDrawingSvg()` → `GetDrawingGeometry()`; удалить HTML-методы; добавить `CreateFlowDocument()` |
| `MosquitoNetCalculator/PrintPreviewWindow.xaml` | WebView2 → FlowDocumentReader; новые стили |
| `MosquitoNetCalculator/PrintPreviewWindow.xaml.cs` | Полная переработка: загрузка FlowDocument, PrintDialog |
| `MosquitoNetCalculator/Controls/ActionBarControl.xaml.cs` | `BtnPrintKp_Click` — передача FlowDocument вместо HTML |
| `MosquitoNetCalculator/MosquitoNetCalculator.csproj` | Удалить WebView2 package; удалить print_template.html EmbeddedResource |
| `MosquitoNetCalculator.Tests/Services/PrintServiceTests.cs` | Все тесты под FlowDocument |

### Удаляемые файлы
| Файл | Причина |
|---|---|
| `MosquitoNetCalculator/Resources/print_template.html` | Больше не нужен |
| `MosquitoNetCalculator.Tests/App/AppLifecycleTests.cs` — секция про print_template.html | Устаревший тест ресурса |

---

## 7. Граничные случаи

| Сценарий | Ожидаемое поведение |
|---|---|
| Нет позиций в заказе | Показать тост «Добавьте хотя бы одну позицию», не открывать предпросмотр |
| Все позиции с Total = 0 | Не показывать их в таблице (как в текущей реализации) |
| Одна позиция | Одностраничный документ |
| 50+ позиций | Многостраничный документ, заголовок таблицы повторяется на каждой странице (TableHeaderRow) |
| Длинное название товара (>20 символов) | Перенос текста в ячейке (TextWrapping) |
| Очень длинная заметка | Перенос текста в блоке примечаний |
| Спецсимволы в имени/адресе | WPF TextBlock безопасен для любых Unicode (XSS не применим) |
| Принтер не установлен | PrintDialog покажет ошибку Windows |
| Отмена в диалоге печати | Ничего не происходит, окно предпросмотра остаётся открытым |

---

## 8. Отложенные / связанные задачи

| Задача | Описание | Когда |
|---|---|---|
| **«Сохранить в эксель»** | Экспорт КП в Excel (.xlsx) вместо PDF | Отдельная фича (предложено пользователем) |
| Экспорт КП в PDF | Если понадобится — через PrintDocument с записью в файл | После основной печати |
| Тёмная тема в предпросмотре | Сейчас предпросмотр всегда светлый. FlowDocument можно стилизовать под тему | После миграции |

---

## 9. Критерии готовности

1. Сборка 0 ошибок, warnings 0
2. Все тесты проходят (новые + старые, не связанные с печатью)
3. FlowDocument содержит те же данные, что и HTML-версия
4. SVG-рисунки выглядят идентично
5. Предпросмотр открывается ≤ 1 сек (против 2-3 сек сейчас из-за WebView2)
6. Печать на принтер работает
7. Многостраничные документы имеют колонтитулы
8. WebView2 пакет удалён из зависимостей
