# Задача: центрировать картинку чертежа в ячейке таблицы КП

## Контекст

Предыдущие фиксы (обрезка текста в таблице + качество предпросмотра —
см. `ai-coder-task-print-fixes.md`) уже применены и работают. Осталась
косметическая, но заметная проблема: картинка чертежа в последней колонке
таблицы («Чертёж») прижата к верхнему краю ячейки, а не стоит по центру —
на скриншоте видно пустое место снизу под иконкой, тогда как текст в
остальных колонках строки визуально центрирован по вертикали.

Затронутые файлы:
- `MosquitoNetCalculator/Services/PrintService.Drawings.cs`
  (метод `CreateDrawingImageElement`)
- `MosquitoNetCalculator/Services/PrintService.FlowDocument.cs`
  (место сборки `imageCell` в `BuildItemsTable`)

## Причина

1. `imageCell.Padding = new Thickness(0)` — у всех остальных `TableCell` в
   таблице padding `(4, 5, 4, 5)`. Из-за нулевого padding именно в этой
   ячейке картинка сидит вплотную к верхней границе, а не там же, где
   визуальный центр строки у соседних текстовых ячеек.
2. `Image` внутри `CreateDrawingImageElement` уже имеет
   `HorizontalAlignment.Center` / `VerticalAlignment.Center`, но будучи
   вставленным напрямую в `BlockUIContainer` без промежуточного
   растягивающегося контейнера, это не всегда даёт полное центрирование
   по высоте всей ячейки в `FlowDocument`-таблицах — `BlockUIContainer`
   не всегда стретчит контент так, как ожидается.

## Что сделать

### 1. Добавить обёртку-контейнер, гарантирующую центрирование

В `PrintService.Drawings.cs` рядом с `CreateDrawingImageElement` добавить
вспомогательный метод:

```csharp
// Гарантирует, что content центрируется по вертикали и горизонтали
// внутри всей доступной площади ячейки (BlockUIContainer сам по себе
// не всегда растягивает контент на нужную высоту).
internal static UIElement WrapForCentering(UIElement content)
{
    var grid = new Grid
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch
    };
    grid.Children.Add(content); // content уже сам Center/Center
    return grid;
}
```

`CreateDrawingImageElement` менять не нужно — `HorizontalAlignment.Center`/
`VerticalAlignment.Center` там уже стоят правильно, проблема не в самом
`Image`, а в контейнере вокруг него.

### 2. Вернуть нормальный padding ячейке чертежа

В `PrintService.FlowDocument.cs`, там, где собирается `imageCell`:

```csharp
var imageCell = new TableCell
{
    BorderBrush = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
    BorderThickness = new Thickness(1),
    Padding = new Thickness(4, 5, 4, 5)   // было Thickness(0) — согласовать с остальными ячейками
};

var drawingImage = CreateDrawingImageElement(item.Name, item.Width, item.Height, displayWidth: 30);
var centeredContent = PrintService.WrapForCentering(drawingImage);

imageCell.Blocks.Add(new BlockUIContainer(centeredContent) { Margin = new Thickness(0) });
row.Cells.Add(imageCell);
```

### 3. ВАЖНЫЙ НЮАНС — пересчитать displayWidth, иначе снова будет наложение

Возврат padding `(4,5,4,5)` уменьшает полезную ширину колонки «Чертёж» на
8 DIP по горизонтали (было `Padding = 0`, значит вся usable-ширина шла
под картинку). По старому расчёту в коде (v3.44.11):

```
usable = colWidth(0.08 × 673 ≈ 54 DIP) − border(2) = 52 DIP
Image displayWidth = 36 DIP  →  запас всего 16 DIP
```

С учётом нового padding 4+4:
```
usable = 54 − border(2) − padding(8) = 44 DIP
```
36 DIP всё ещё влезает, но запас становится минимальным (8 DIP). Чтобы не
получить регресс (картинка снова начнёт наезжать на рамку/соседнюю
колонку при чуть большем displayWidth в будущем), **уменьшить
`displayWidth` до 28–30 DIP** при вызове `CreateDrawingImageElement`
внутри `BuildItemsTable` — картинка чертежа декоративная/иконка-подсказка,
уменьшение на 6-8 DIP визуально не критично, а место для центрирования
не съедает.

Альтернативный вариант (если 30 DIP визуально мал) — увеличить долю
колонки «Чертёж» в `widths[]` с `0.08` на `0.09`, компенсировав за счёт
`Сумма` (`0.13 → 0.12`) — суммарно доли должны остаться 1.0. Выбрать
один из двух вариантов, не делать оба одновременно (иначе таблица снова
"поплывёт" по ширине других колонок и придётся повторно тестировать
проблему 1 из предыдущей задачи).

### 4. Не трогать `EdgeMode`/`BitmapScalingMode` логику

Правки в этой задаче не должны затрагивать `forPrinting`-ветвление
`RenderOptions.SetEdgeMode`, добавленное в предыдущей задаче — оно уже
работает верно (превью сглаженное, печать чёткая). Здесь только
центрирование, ширина/padding.

## Критерии приёмки

1. В превью и в PDF/печати картинка чертежа визуально стоит по центру
   ячейки (равные отступы сверху/снизу, слева/справа), а не прижата к
   верхнему краю.
2. При этом высота строки таблицы (`TableRow`) не увеличилась и не
   уменьшилась заметно по сравнению с текущим состоянием — центрирование
   не должно "раздувать" строку.
3. Никакого наложения картинки на границу ячейки/соседнюю колонку — при
   тех же тестовых товарах, что использовались в предыдущей задаче
   (включая позиции с самыми длинными названиями/крупными размерами).
4. Регрессионный прогон уже существующих тестов
   (`PrintServiceTests.cs`, `CollatedPrintPaginatorTests.cs`,
   `AppLifecycleTests`) остаётся зелёным.
5. Приложить/обновить скриншот предпросмотра КП с товаром "Anwis" (как
   в исходном баг-репорте) — визуально сверить центрирование иконки в
   строке.

## Не трогать

- `RenderOptions` (`EdgeMode`/`BitmapScalingMode`) — уже настроено верно
  в предыдущей задаче.
- Логику `MakeNonWrappingCell`/`FormatIntWithNbsp`/safety-net авто-фита
  шрифта — не относится к этой задаче.
- Векторный SVG-путь PDF (`PrintService.Pdf.cs`) — чертежи там рендерятся
  отдельно через `Svg(GetDrawingSvg(...))`, эта задача касается только
  WPF `Image` в `FlowDocument`-таблице.
