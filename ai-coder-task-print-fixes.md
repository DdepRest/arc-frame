# Задача: исправить обрезку текста в таблице КП и низкое качество предпросмотра печати

## Контекст

Печать нативная на WPF (`FlowDocument` → `PrintPreviewControl`/`PrintQueue`/QuestPDF),
без HTML/WebView2. Есть две проблемы:

1. Названия/числа в 12-колоночной таблице накладываются на соседние ячейки
   (выглядит как «обрезка», хотя текст физически цел, просто отрисован НЕ Wrap
   и без Trimming — и вылезает за границы своей колонки).
2. Предпросмотр (`FlowDocumentPageViewer`) показывает чертежи «зубчатыми»,
   низкого качества — из-за `EdgeMode.Aliased`, включённого для печати 600 DPI,
   но применяемого и к экранному рендеру.

Затронутые файлы:
- `MosquitoNetCalculator/Services/PrintService.FlowDocument.cs`
- `MosquitoNetCalculator/Services/PrintService.Drawings.cs`
- `MosquitoNetCalculator/Controls/PrintPreviewControl.xaml` / `.xaml.cs`
- `MosquitoNetCalculator/Services/PrintService.cs` (точки вызова `BuildFlowDocument`)

---

## Проблема 1: наложение текста в узких колонках

### Причина

`MakeNonWrappingCell` в `PrintService.FlowDocument.cs` использует
`TextWrapping.NoWrap` + `TextTrimming.None` — это значит: если текст шире
выделенной колонки, WPF **не обрежет** его и не покажет «…», а просто
нарисует шире, чем ячейка. Хвост наезжает на соседнюю `TableCell`
(с её белым фоном и серой рамкой) — визуально выглядит как обрезка.

Конкретные триггеры:

- `FormatIntWithNbsp` всегда вставляет группировку тысяч через NBSP
  (`"1002"` → `"1\u00A0002"`), что **удлиняет** число даже когда группировка
  не нужна (4 цифры и меньше) — а именно колонки Ш/В самые узкие
  (60.6 / 53.8 DIP).
- Колонка «№» — всего 0.04 × 673 ≈ 26.9 DIP, с учётом border(2)+padding(8)+
  margin(4) остаётся ~13 DIP на текст — на пределе уже для двузначных номеров.
- Ширины колонок (`widths[]`) подбирались вручную под конкретные тестовые
  строки (v3.44.6 → v3.44.12, судя по комментариям) — без авто-фидбека от
  реальной измеренной ширины текста. Любые новые данные (длиннее, чем в
  тестах) снова будут вызывать наложение.

### Что сделать

**1.1. Ограничить группировку тысяч порогом.**
В `FormatIntWithNbsp` — не форматировать с NBSP-разделителем числа короче
5 значащих цифр (там разделитель не нужен и только удлиняет строку):

```csharp
private static string FormatIntWithNbsp(int value)
{
    return value >= 10_000
        ? value.ToString("N0", CultureInfo.InvariantCulture).Replace(' ', '\u00A0')
        : value.ToString(CultureInfo.InvariantCulture);
}
```

**1.2. Для колонки «№» не применять `FormatIntWithNbsp` вообще** — это просто
`idx.ToString(CultureInfo.InvariantCulture)`, номер строки не бывает 5-значным.

**1.3. Перераспределить доли ширины колонок** — забрать немного у «Площ./Дл.»
и «Сумма» (там обычно есть запас) в пользу «№», «Наименование», «Ш», «В»:

```csharp
double[] widths = {
    0.05, 0.115, 0.08, 0.09, 0.08, 0.07,   // № / Наим / Цвет / Ш / В / Кол-во
    0.07, 0.085, 0.05, 0.10, 0.125, 0.08   // Монтаж / Площ / Ед / Цена / Сумма / Чертёж
};
```
(сумма долей должна остаться 1.0 — проверить после правки).

**1.4. Добавить safety-net в `MakeNonWrappingCell`** — измерять реальную
ширину текста через `FormattedText` и, если он не влезает в доступную
ширину колонки, чуть уменьшать `FontSize` (не ниже 75% от исходного),
вместо молчаливого наложения на соседнюю ячейку:

```csharp
private static TableCell MakeNonWrappingCell(
    string text, double fontSize,
    TextAlignment textAlignment, HorizontalAlignment horizontalAlignment,
    double availableWidthDip,   // ширина колонки в DIP минус border(2)+padding(8)+margin(4)
    bool isBold = false)
{
    var typeface = new Typeface(
        new FontFamily("Segoe UI"), FontStyles.Normal,
        isBold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);

    double effectiveSize = fontSize;
    var dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow ?? new Window());

    double Measure(double size) => new FormattedText(
        text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        typeface, size, Brushes.Black, dpi.PixelsPerDip).WidthIncludingTrailingWhitespace;

    while (availableWidthDip > 0
           && Measure(effectiveSize) > availableWidthDip
           && effectiveSize > fontSize * 0.75)
    {
        effectiveSize -= 0.5;
    }

    var tb = new TextBlock
    {
        Text = text ?? "",
        TextAlignment = textAlignment,
        FontSize = effectiveSize,
        FontWeight = isBold ? FontWeights.SemiBold : FontWeights.Normal,
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.None,
        HorizontalAlignment = horizontalAlignment,
        VerticalAlignment = VerticalAlignment.Center,
        LineHeight = effectiveSize * 1.35,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight
    };

    var horizontalMargin = horizontalAlignment == HorizontalAlignment.Right ? 3 : 2;
    var container = new BlockUIContainer(tb)
    {
        Margin = new Thickness(horizontalMargin, 4, horizontalMargin, 0)
    };
    return MakeCell(container);
}
```

Потребуется прокинуть `availableWidthDip` во все вызовы
`MakeCenteredCell`/`MakeRightAlignedCell` — по одному числу на колонку,
вычисленному из `widths[i] * 673 - 14` (border+padding+margin, как уже
считалось в существующих комментариях v3.44.11/12). Если проще —
завести константы:

```csharp
private const double TableContentWidthDip = 673.0;
private static double ColWidthDip(int colIndex, double[] widths) =>
    widths[colIndex] * TableContentWidthDip;
private static double UsableWidthDip(double colWidthDip) => colWidthDip - 14; // border+padding+margin
```

**Не трогать** колонку «Наименование» — там уже правильно используется
`Paragraph` с обычным Wrap (единственная колонка с явным переносом),
это не источник проблемы.

---

## Проблема 2: низкое качество предпросмотра (чертежи «зубчатые»)

### Причина

`CreateDrawingImageElement` в `PrintService.Drawings.cs` жёстко ставит:
```csharp
RenderOptions.SetEdgeMode(img, EdgeMode.Aliased);
RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
```
Это attached property на `Image`, который используется **и** в превью
(`FlowDocumentPageViewer`, экран ~96 DPI), **и** при реальной печати
(XPS, 600 DPI). На 600 DPI `Aliased` незаметен, а на экране при зуме
даёт видимую лесенку на тонких линиях/штрихпунктире чертежа.

### Что сделать

**2.1. Добавить параметр цели рендера** во всю цепочку вызовов:

```csharp
// PrintService.Drawings.cs
internal static Image CreateDrawingImageElement(
    string name, double width, double height, double displayWidth = 70,
    bool forPrinting = false)
{
    var img = new Image
    {
        Source = GetDrawingImage(name, width, height),
        Width = displayWidth,
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        SnapsToDevicePixels = true,
        UseLayoutRounding = true
    };

    if (forPrinting)
    {
        // Только реальная печать 600 DPI — на экране Aliased даёт
        // видимую лесенку на линиях чертежа.
        RenderOptions.SetEdgeMode(img, EdgeMode.Aliased);
    }
    else
    {
        RenderOptions.SetEdgeMode(img, EdgeMode.Unspecified); // сглаживание для экрана
    }
    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality); // в обоих случаях

    return img;
}
```

**2.2. Прокинуть флаг через `BuildFlowDocument`:**
```csharp
// PrintService.FlowDocument.cs
public static FlowDocument BuildFlowDocument(
    IEnumerable<OrderItem> items, Client client, decimal total, string words,
    bool forPrinting = false)
{
    // ...
    var drawingImage = CreateDrawingImageElement(
        item.Name, item.Width, item.Height, displayWidth: 36, forPrinting: forPrinting);
    // ...
}
```

**2.3. Обновить все точки вызова `BuildFlowDocument`:**
- Построение для предпросмотра (в `PrintPreviewControl.xaml.cs` / там, откуда
  документ подаётся в `FlowDocumentPageViewer`) → `forPrinting: false`.
- Построение перед `SendToQueue` (реальная печать в `PrintService.cs`) →
  `forPrinting: true`.
- Экспорт в PDF (`PrintService.Pdf.cs`) чертежи не берёт из этого пути
  (использует векторный `Svg(GetDrawingSvg(...))`), поэтому если там где-то
  всё же вызывается `BuildFlowDocument` — можно передавать `false`, это не
  влияет на PDF-вывод.

**2.4. Улучшить сглаживание текста в самом вьюере превью** —
в `PrintPreviewControl.xaml`, на `FlowDocumentPageViewer`:
```xml
<FlowDocumentPageViewer
    x:Name="Viewer"
    TextOptions.TextFormattingMode="Display"
    TextOptions.TextRenderingMode="ClearType"
    UseLayoutRounding="True"
    SnapsToDevicePixels="True" />
```

---

## Критерии приёмки

1. Тестовые сценарии с длинными названиями (`"Антикошка на раму Оконная на
   метал. крепл."`) и 5-значными размерами (например `12500 × 9800`) —
   текст в соседних колонках («Ш», «В», «Кол-во», «Цена», «Сумма») **не
   накладывается** друг на друга ни в превью, ни на печатной странице/PDF.
2. Номер позиции `idx` вплоть до 3-значного (`999`) отображается полностью
   в колонке «№» без наложения на «Наименование».
3. В `PrintPreviewControl` чертёж (векторные линии, штрихпунктир) выглядит
   сглаженным при 100% и увеличенном зуме — без видимой лесенки.
4. На реальной печати (или экспорте XPS) чертёж остаётся чётким/резким,
   без «замыливания» линий (регрессия по исходному хотфиксу v3.х
   с `EdgeMode.Aliased` не допускается).
5. Прогнать существующие тесты:
   `PrintServiceTests.cs`, `CollatedPrintPaginatorTests.cs`,
   `AppLifecycleTests.PrintPreviewWindow_OpensWithoutNRE_DuringInitialXamlParse`
   — все должны остаться зелёными (788/789, с учётом известного flake).
6. Добавить/обновить unit-тест на `FormatIntWithNbsp`: проверить, что для
   значений < 10000 нет NBSP-разделителя, а для >= 10000 — есть.
7. (Опционально, но желательно) Добавить unit-тест, который строит
   `BuildFlowDocument` с заведомо длинными тестовыми значениями и через
   `VisualTreeHelper` проверяет, что фактические `ActualWidth` соседних
   `TableCell` не пересекаются (bounding box check), — чтобы регрессия
   наложения текста ловилась автоматически, а не руками на глаз.

## Не трогать

- Пропорции/логику колонки «Наименование» — там уже корректный Wrap.
- Векторный SVG-путь PDF-экспорта (`PrintService.Pdf.cs`) — проблема его
  не касается.
- `CollatedPrintPaginator` / `PrintPaginator` — колонтитулы и пагинация
  работают штатно, эта задача их не затрагивает.
