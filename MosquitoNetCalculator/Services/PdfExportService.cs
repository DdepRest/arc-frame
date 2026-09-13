using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using MosquitoNetCalculator.Models;
using QuestPDF.Elements;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Exports a КП as a PDF file via QuestPDF. Mirrors
    /// <see cref="FlowDocumentBuilder.Build"/> layout but uses QuestPDF's
    /// fluent API for native vector output.
    /// When <see cref="PrintSettings.IncludeProductionCopy"/> is set, the output
    /// contains two sequential page sets: the customer copy (first) and the
    /// production copy with the «В ПРОИЗВОДСТВО» stamp on its first page.
    /// </summary>
    public class PdfExportService
    {
        static PdfExportService()
        {
            QuestPDF.Settings.EnableDebugging = true;
        }

        // Padding added to the widest measured amount when sizing the amount
        // ConstantItem — keeps the right edge a hair away from the row's
        // border so bold digits don't visually touch it.
        private const float AmountColumnPaddingPt = 4f;

        // Default row font size (matches page.DefaultTextStyle .FontSize(9)).
        private const float DefaultRowFontPt = 9f;
        // Grand-total row font size (in BuildAdditionalKpPdf).
        private const float GrandTotalFontPt = 12f;

        /// <summary>
        /// Exports a КП as a PDF file. Appends a stamped production copy when
        /// <paramref name="attemptSettings"/><c>.IncludeProductionCopy</c> is true.
        /// </summary>
        public void Export(
            string filePath,
            List<OrderItem> items,
            ClientInfo clientInfo,
            double totalAmount,
            string amountInWords,
            PrintSettings? attemptSettings = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("filePath must be non-empty", nameof(filePath));

            var valid = (items ?? new List<OrderItem>())
                .Where(i => !string.IsNullOrEmpty(i.Name) && i.Total > 0)
                .ToList();
            clientInfo ??= new ClientInfo();

            bool includeProductionStamp = attemptSettings?.IncludeProductionCopy == true;
            int copies = Math.Max(0, attemptSettings?.Copies ?? 1);

            if (copies == 0 && !includeProductionStamp)
                throw new ArgumentException("Nothing to print: set at least one copy or enable the production copy.", nameof(attemptSettings));

            // «Копии» — чистые листы заказчика (без печати), повторённые copies раз;
            // копия «В производство» — ВСЕГДА ОДНА, отдельным блоком со штампом.
            byte[]? stampBytes = includeProductionStamp ? ProductionStampImage.TryLoadBytes() : null;
            if (includeProductionStamp && stampBytes == null)
                Debug.WriteLine("[PdfExportService] Production stamp image not found — PDF completed without stamp.");

            // QuestPDF рисует page.Foreground() на КАЖДОЙ странице секции (это постраничный
            // «водяной знак»), поэтому «только на первый лист производственного комплекта»
            // нельзя сделать статическим элементом. Используем IDynamicComponent: QuestPDF
            // вызывает Compose на каждой странице, а DynamicContext.PageNumber содержит её
            // сквозной номер. Номер первого листа производственного комплекта = страницы
            // чистых копий заказчика + 1 — считаем их отдельным проходом генерации.
            int stampOnPageNumber = -1;
            if (includeProductionStamp)
            {
                int customerPages = copies > 0
                    ? CountCustomerPages(copies, valid, clientInfo, totalAmount, amountInWords)
                    : 0;
                stampOnPageNumber = customerPages + 1;
            }

            // Режим страниц (All/Single/Range) — та же семантика, что и в физической печати:
            // PageSelection.GetSelectedSourcePages выбирает страницы ОДНОГО комплекта КП,
            // «Копии» повторяют выбранный набор, производственная копия идёт целиком отдельным
            // блоком в конце. PageMode.All (дефолт и самый частый случай) идёт быстрым путём
            // без разборки PDF (ветка ниже). Для Single/Range: генерируем один комплект КП +
            // производственную копию во временный файл, затем DocumentOperation.TakePages
            // (QuestPDF ≥ 2024.12) собирает итог в нужном порядке:
            //   [стр. N₁, N₂, …] × copies  +  весь производственный блок.
            // Штамп — часть контента производственной страницы, поэтому при любом выборе
            // остаётся на её первом листе.
            if (attemptSettings != null && attemptSettings.Pages != PageMode.All)
            {
                string sourcePath = Path.Combine(Path.GetTempPath(), $"arc-pdf-src-{Guid.NewGuid():N}.pdf");
                try
                {
                    int stampTargetPage = -1;
                    if (includeProductionStamp)
                    {
                        // Штамп внутри исходника — на первом листе производственного блока:
                        // чистый комплект КП занимает CountCustomerPages(1) страниц.
                        int kpPages = CountCustomerPages(1, valid, clientInfo, totalAmount, amountInWords);
                        stampTargetPage = kpPages + 1;
                    }

                    var source = Document.Create(container =>
                    {
                        container.Page(page => BuildKpPage(page, valid, clientInfo, totalAmount, amountInWords, includeProductionStamp: false));
                        if (includeProductionStamp)
                            container.Page(page => BuildKpPage(page, valid, clientInfo, totalAmount, amountInWords,
                                includeProductionStamp: true, stampBytes: stampBytes, stampOnPageNumber: stampTargetPage));
                    });
                    source.GeneratePdf(sourcePath);

                    // ВАЖНО: производственный блок — это ПОЛНЫЙ комплект КП (те же позиции),
                    // он тоже многостраничный. Число страниц ЧИСТОГО комплекта считаем
                    // отдельным проходом (CountCustomerPages(1)), а не «всего страниц минус 1»:
                    // исходник = чистый комплект + производственный (оба по kpPages страниц).
                    int kpPageCount = CountCustomerPages(1, valid, clientInfo, totalAmount, amountInWords);
                    var selected = PageSelection.GetSelectedSourcePages(attemptSettings, kpPageCount); // 0-based

                    var operation = DocumentOperation.LoadFile(sourcePath);
                    bool any = false;
                    for (int c = 0; c < copies; c++)
                    {
                        foreach (int pageIndex in selected)
                        {
                            operation.TakePages($"{pageIndex + 1}");
                            any = true;
                        }
                    }
                    if (includeProductionStamp)
                        operation.TakePages($"{kpPageCount + 1}-z"); // производственный блок целиком

                    operation.Save(filePath);
                    if (!any)
                        throw new ArgumentException("Nothing to print: the selected page range is empty.", nameof(attemptSettings));
                }
                finally
                {
                    try { if (File.Exists(sourcePath)) File.Delete(sourcePath); } catch { /* cleanup best-effort */ }
                }
                return;
            }

            var document = Document.Create(container =>
            {
                for (int i = 0; i < copies; i++)
                    container.Page(page => BuildKpPage(page, valid, clientInfo, totalAmount, amountInWords, includeProductionStamp: false));

                if (includeProductionStamp)
                    container.Page(page => BuildKpPage(page, valid, clientInfo, totalAmount, amountInWords,
                        includeProductionStamp: true, stampBytes: stampBytes, stampOnPageNumber: stampOnPageNumber));
            });

            document.GeneratePdf(filePath);
        }

        /// <summary>
        /// Генерирует только чистые копии заказчика (в память) и возвращает число
        /// страниц, которые они занимают в PDF. Нужно, чтобы знать номер первого листа
        /// производственного комплекта при сквозной нумерации страниц QuestPDF.
        /// </summary>
        private static int CountCustomerPages(
            int copies,
            List<OrderItem> valid,
            ClientInfo clientInfo,
            double totalAmount,
            string amountInWords)
        {
            var countingDocument = Document.Create(container =>
            {
                for (int i = 0; i < copies; i++)
                    container.Page(page => BuildKpPage(page, valid, clientInfo, totalAmount, amountInWords, includeProductionStamp: false));
            });

            using var ms = new MemoryStream();
            countingDocument.GeneratePdf(ms);
            return CountPdfPages(ms.ToArray());
        }

        private static int CountPdfPages(byte[] pdfBytes)
        {
            if (pdfBytes == null || pdfBytes.Length == 0) return 0;
            string text = Encoding.Latin1.GetString(pdfBytes);
            // QuestPDF пишет объекты страниц несжатыми: /Type /Page встречается по разу на лист.
            return Regex.Matches(text, @"/Type\s*/Page[\s>/]").Count;
        }

        private static void BuildKpPage(
            PageDescriptor page,
            List<OrderItem> valid,
            ClientInfo clientInfo,
            double totalAmount,
            string amountInWords,
            bool includeProductionStamp,
            byte[]? stampBytes = null,
            int stampOnPageNumber = -1)
        {
            bool hasItems = valid.Count > 0;

            page.Size(PageSizes.A4);
            page.MarginLeft(16, Unit.Millimetre);
            page.MarginTop(30, Unit.Millimetre);
            page.MarginRight(16, Unit.Millimetre);
            page.MarginBottom(14, Unit.Millimetre);
            page.DefaultTextStyle(t => t
                .FontFamily(Fonts.SegoeUI)
                .FontSize(9)
                .FontColor(Colors.Black));

            page.Header().PaddingBottom(6).Element(c =>
            {
                if (string.IsNullOrWhiteSpace(clientInfo.ContractNumber))
                    return;
                string num = clientInfo.ContractNumber.Trim();
                c.AlignRight().Text($"Договор № {num}").FontSize(7).FontColor(Colors.Grey.Darken1).Italic();
            });

            page.Footer().PaddingTop(6).Element(c =>
            {
                c.Row(r =>
                {
                    r.RelativeItem().Text(t =>
                    {
                        t.Span("Страница ").FontSize(7).FontColor(Colors.Grey.Darken1).Italic();
                        t.CurrentPageNumber().FontSize(7).FontColor(Colors.Grey.Darken1).Italic();
                        t.Span(" из ").FontSize(7).FontColor(Colors.Grey.Darken1).Italic();
                        t.TotalPages().FontSize(7).FontColor(Colors.Grey.Darken1).Italic();
                    });
                    r.RelativeItem().AlignRight().Text(
                        clientInfo.ContractDate.ToString("dd.MM.yyyy"))
                        .FontSize(7).FontColor(Colors.Grey.Darken1).Italic();
                });
            });

            page.Content().Column(col =>
            {
                col.Spacing(8);

                col.Item().AlignCenter().Text("КОММЕРЧЕСКОЕ ПРЕДЛОЖЕНИЕ")
                    .FontSize(13.5f).Bold().LetterSpacing(0.2f);

                col.Item().AlignCenter().Text(t =>
                {
                    string num = string.IsNullOrWhiteSpace(clientInfo.ContractNumber) ? "б/н" : clientInfo.ContractNumber.Trim();
                    string date = clientInfo.ContractDate.ToString("dd.MM.yyyy");
                    t.Span($"Договор № {num} от {date}").FontSize(9);
                });

                col.Item().Element(c =>
                {
                    if (!HasAnyClientField(clientInfo)) return;
                    c.Table(t =>
                    {
                        t.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(82);
                            cd.RelativeColumn();
                        });
                        AddClientRowPdf(t, "Заказчик:", clientInfo.ClientName);
                        AddClientRowPdf(t, "Телефон:", clientInfo.ClientPhone);
                        AddClientRowPdf(t, "Адрес:", clientInfo.ClientAddress);
                    });
                });

                if (hasItems)
                {
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(20);
                            cd.RelativeColumn(3);
                            cd.RelativeColumn(1.2f);
                            cd.RelativeColumn(1.3f);
                            cd.RelativeColumn(1.3f);
                            cd.RelativeColumn(1.4f);
                            cd.RelativeColumn(1.9f);
                            cd.RelativeColumn(1.8f);
                            cd.ConstantColumn(25);
                            cd.RelativeColumn(1.9f);
                            cd.RelativeColumn(2.3f);
                            cd.RelativeColumn(2.8f);
                        });

                        t.Header(h =>
                        {
                            h.Cell().Element(EHeaderCell).Text("#").AlignCenter();
                            h.Cell().Element(EHeaderCell).Text("Наименование");
                            h.Cell().Element(EHeaderCell).Text("Цвет").AlignCenter();
                            h.Cell().Element(EHeaderCell).Text("Ш, мм").AlignCenter();
                            h.Cell().Element(EHeaderCell).Text("В, мм").AlignCenter();
                            h.Cell().Element(EHeaderCell).Text("Кол-во").AlignCenter();
                            h.Cell().Element(EHeaderCell).Text("Монтаж").AlignCenter();
                            h.Cell().Element(EHeaderCell).Text("Площ./Дл.").AlignCenter();
                            h.Cell().Element(EHeaderCell).Text("Ед.").AlignCenter();
                            h.Cell().Element(EHeaderCell).Text("Цена").AlignRight();
                            h.Cell().Element(EHeaderCell).Text("Сумма").AlignRight();
                            h.Cell().Element(EHeaderCell).Text("Чертёж").AlignCenter();
                        });

                        int idx = 1;
                        bool alt = false;
                        foreach (var item in valid)
                        {
                            var bg = alt ? Colors.Grey.Lighten3 : Colors.White;
                            alt = !alt;

                            t.Cell().Element(c2 => EDataCell(c2, bg)).Text(idx.ToString()).AlignCenter();
                            t.Cell().Element(c2 => EDataCell(c2, bg)).Text(item.PrintDisplayName ?? "").FontSize(8.5f);
                            t.Cell().Element(c2 => EDataCell(c2, bg)).Text(item.Color ?? "").AlignCenter().FontSize(8.5f);
                            t.Cell().Element(c2 => EDataCell(c2, bg)).Text(item.Width > 0 ? item.Width.ToString("F0") : "").AlignCenter().FontSize(8.5f);
                            t.Cell().Element(c2 => EDataCell(c2, bg)).Text(item.Height > 0 ? item.Height.ToString("F0") : "").AlignCenter().FontSize(8.5f);
                            t.Cell().Element(c2 => EDataCell(c2, bg)).Text(item.QuantityDisplay).AlignCenter().FontSize(8.5f);
                            t.Cell().Element(c2 => EDataCell(c2, bg)).Text(item.KpInstallationCellText ?? "").AlignCenter().FontSize(8.5f);
                            if (item.IsAmountOnly || (item.IsQuantityOptional && item.Quantity <= 1))
                                t.Cell().Element(c2 => EDataCell(c2, bg)).Text("").AlignCenter();
                            else
                                t.Cell().Element(c2 => EDataCell(c2, bg)).Text(item.CalculatedValue.ToString("F3")).AlignCenter().FontSize(8.5f);
                            t.Cell().Element(c2 => EDataCell(c2, bg)).Text(item.Unit ?? "").AlignCenter().FontSize(8.5f);
                            t.Cell().Element(c2 => EDataCell(c2, bg)).Text(MoneyFormatService.Format(item.Price)).AlignRight().FontSize(8.5f);
                            t.Cell().Element(c2 => EDataCell(c2, bg)).Text(MoneyFormatService.Format(item.TotalWithDeduction)).AlignRight().FontSize(8.5f);

                            t.Cell().Element(c2 => EDataCell(c2, bg)).AlignCenter().AlignMiddle().Width(55).Svg(DrawingService.GetDrawingSvg(item.Name, item.Width, item.Height));

                            idx++;
                        }

                        double grand = valid.Sum(i => i.TotalWithDeduction);
                        t.Cell().ColumnSpan(10).Element(ETotalRowLabelCell).AlignRight().PaddingRight(8).Text("ИТОГО:").Bold().FontSize(9);
                        t.Cell().Element(ETotalRowCell).AlignRight().PaddingRight(6).Text(MoneyFormatService.Format(grand)).Bold().FontSize(9);
                        t.Cell().Element(ETotalRowCell).Text("");
                    });
                }

                col.Item().PaddingTop(14).BorderTop(2).BorderColor(Colors.Black).AlignRight().Text(t =>
                {
                    t.Span("ИТОГО: ").Bold().FontSize(10);
                    t.Span($"{MoneyFormatService.Format(totalAmount)} руб.").Bold().FontSize(13);
                });
                col.Item().AlignRight().PaddingTop(4).Text(amountInWords ?? "")
                    .Italic().FontSize(8.5f).FontColor(Colors.Grey.Darken2);

                if (clientInfo.HasAdditionalKp && clientInfo.AdditionalKps.Any(k => k.IsActive))
                    col.Item().Element(c => BuildAdditionalKpPdf(c, clientInfo, totalAmount));

                if (!string.IsNullOrWhiteSpace(clientInfo.Notes))
                {
                    col.Item().Background(Colors.Grey.Lighten4).BorderLeft(3).BorderColor(Colors.Grey.Medium)
                        .Padding(8).Text(t =>
                        {
                            t.Line("ПРИМЕЧАНИЯ").Bold().FontSize(8.5f);
                            var noteLines = NotesFormatter.Parse(clientInfo.Notes);
                            for (int i = 0; i < noteLines.Count; i++)
                            {
                                var line = noteLines[i];
                                if (line.IsListItem)
                                    t.Span("• ").FontSize(8.5f);

                                foreach (var segment in line.Segments)
                                {
                                    var span = t.Span(segment.Text).FontSize(8.5f);
                                    if (segment.IsBold) span.Bold();
                                    if (segment.IsItalic) span.Italic();
                                    if (!string.IsNullOrWhiteSpace(segment.ColorTag))
                                    {
                                        try
                                        {
                                            span.FontColor(segment.ColorTag);
                                        }
                                        catch
                                        {
                                            // Unknown color — keep default PDF color.
                                        }
                                    }
                                }

                                if (i < noteLines.Count - 1)
                                    t.EmptyLine();
                            }
                        });
                }

                col.Item().PaddingTop(20).BorderTop(1).BorderColor(Colors.Grey.Medium).PaddingTop(8).Text(t =>
                {
                    t.Line("УСЛОВИЯ").Bold().FontSize(8);
                    t.Line("– Срок действия коммерческого предложения — 5 рабочих дней.").FontSize(7.5f);
                    t.Line("– Оплата производится на основании счёта.").FontSize(7.5f);
                    t.Span("– Цены указаны с учётом стоимости материалов.").FontSize(7.5f);
                });                col.Item().PaddingTop(32).Row(r =>
                {
                    r.RelativeItem().Text("Исполнитель _____________").Bold().FontSize(9);
                    r.RelativeItem().Text("Заказчик _____________").Bold().FontSize(9);
                });
            });

            if (includeProductionStamp)
                page.Foreground().Dynamic(new ProductionStampOnPageComponent(stampOnPageNumber, stampBytes));
        }

        /// <summary>
        /// Накладывает изображение печати «В ПРОИЗВОДСТВО» только на ПЕРВЫЙ лист
        /// производственного комплекта. Используется page.Foreground() (не влияет на поток
        /// контента — печать попадает в свободную зону в верхнем левом углу, не сдвигая
        /// заголовок/таблицу), но статический Foreground QuestPDF повторял бы штамп на
        /// каждой странице секции. Поэтому элемент динамический: Compose вызывается на
        /// каждой странице, и штамп рисуется лишь когда DynamicContext.PageNumber совпадает
        /// с номером первого листа производственного комплекта. При недоступности файла
        /// печать пропускается — документ всё равно формируется.
        /// </summary>
        private sealed class ProductionStampOnPageComponent : IDynamicComponent
        {
            private readonly int _pageNumber;
            private readonly byte[]? _imageBytes;

            public ProductionStampOnPageComponent(int pageNumber, byte[]? imageBytes)
            {
                _pageNumber = pageNumber;
                _imageBytes = imageBytes;
            }

            public DynamicComponentComposeResult Compose(DynamicContext context)
            {
                IElement content = _imageBytes != null && context.PageNumber == _pageNumber
                    ? context.CreateElement(c => c
                        .AlignLeft()
                        .AlignTop()
                        // Левый край — на левой линии контента КП (см. LeftOffsetMm).
                        .PaddingLeft((float)ProductionStampImage.LeftOffsetMm, Unit.Millimetre)
                        .PaddingTop((float)ProductionStampImage.TopOffsetMm, Unit.Millimetre)
                        .Width((float)ProductionStampImage.WidthMm, Unit.Millimetre)
                        .Height((float)ProductionStampImage.HeightMm, Unit.Millimetre)
                        .Image(_imageBytes))
                    : context.CreateElement(_ => { });

                return new DynamicComponentComposeResult { Content = content, HasMoreContent = false };
            }
        }

        private static bool HasAnyClientField(ClientInfo c) =>
            !string.IsNullOrWhiteSpace(c.ClientName)
            || !string.IsNullOrWhiteSpace(c.ClientPhone)
            || !string.IsNullOrWhiteSpace(c.ClientAddress);

        private static void AddClientRowPdf(QuestPDF.Fluent.TableDescriptor t, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var cleanValue = value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (cleanValue.Length == 0) return;
            t.Cell().BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingRight(8).PaddingVertical(2)
                .Text(label).SemiBold().FontSize(8.5f);
            t.Cell().BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingBottom(1)
                .Text(cleanValue).SemiBold().FontSize(9);
        }

        private static IContainer EHeaderCell(IContainer c) =>
            c.Background(Colors.Grey.Lighten2).Border(1).BorderColor(Colors.Grey.Medium)
             .Padding(4).DefaultTextStyle(x => x.Bold().FontSize(7.5f));

        private static IContainer EDataCell(IContainer c, string bg) =>
            c.Background(bg).Border(1).BorderColor(Colors.Grey.Medium).Padding(4);

        private static IContainer ETotalRowCell(IContainer c) =>
            c.Background(Colors.Grey.Lighten3).Border(1).BorderColor(Colors.Grey.Medium).Padding(4);

        private static IContainer ETotalRowLabelCell(IContainer c) =>
            c.Background(Colors.Grey.Lighten3).Border(1).BorderColor(Colors.Grey.Medium).Padding(4);

        private static void BuildAdditionalKpPdf(IContainer c, ClientInfo clientInfo, double mainTotal)
        {
            var kps = clientInfo.AdditionalKps.Where(k => k.IsActive).ToList();
            double kpSum = kps.Sum(k => k.Amount);

            c.Column(col2 =>
            {
                col2.Item().PaddingTop(12).Text(kps.Count == 1 ? "Дополнительное КП" : "Дополнительные КП")
                    .Bold().FontSize(9);

                if (kpSum <= 0)
                {
                    foreach (var kp in kps.Where(k => !string.IsNullOrWhiteSpace(k.Number)))
                        col2.Item().Text($"К данному заказу прилагается КП № {kp.Number.Trim()}").FontSize(9);
                    return;
                }

                // Size the amount column to fit the widest rendered sum + " руб." suffix.
                // Measures each row at its own font size/weight so the column grows
                // with longer sums instead of wrapping "руб." to a second line.
                var amountRows = kps.Select(k => (amount: k.Amount, isGrand: false))
                    .Append((mainTotal, false))
                    .Concat(kps.Where(k => k.Amount > 0).Select(k => (amount: k.Amount, isGrand: false)))
                    .Append((mainTotal + kpSum, true));
                float amountColumnPt = ComputeAmountColumnWidth(amountRows);

                col2.Item().Background(Colors.Grey.Lighten4).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(box =>
                {
                    foreach (var kp in kps)
                    {
                        string left = string.IsNullOrWhiteSpace(kp.Number)
                            ? "Дополнительное КП" : $"К данному заказу прилагается КП № {kp.Number.Trim()}";
                        box.Item().Row(r =>
                        {
                            r.RelativeItem().Text(left).FontSize(9);
                            r.ConstantItem(amountColumnPt).AlignRight().Text($"{MoneyFormatService.Format(kp.Amount)} руб.").SemiBold().FontSize(9);
                        });
                    }

                    box.Item().PaddingTop(8).Row(r =>
                    {
                        r.RelativeItem().Text("Сумма основного КП:");
                        r.ConstantItem(amountColumnPt).AlignRight().Text($"{MoneyFormatService.Format(mainTotal)} руб.").SemiBold();
                    });
                    foreach (var kp in kps.Where(k => k.Amount > 0))
                    {
                        string lbl = string.IsNullOrWhiteSpace(kp.Number)
                            ? "Сумма доп. КП" : $"Сумма доп. КП № {kp.Number.Trim()}";
                        box.Item().Row(r =>
                        {
                            r.RelativeItem().Text($"{lbl}:");
                            r.ConstantItem(amountColumnPt).AlignRight().Text($"{MoneyFormatService.Format(kp.Amount)} руб.").SemiBold();
                        });
                    }
                    box.Item().BorderTop(1.5f).BorderColor(Colors.Black).PaddingTop(4).Row(r =>
                    {
                        r.RelativeItem().Text("ОБЩИЙ ИТОГ:").Bold().FontSize(10.5f);
                        r.ConstantItem(amountColumnPt).AlignRight().Text($"{MoneyFormatService.Format(mainTotal + kpSum)} руб.").Bold().FontSize(12);
                    });
                    box.Item().PaddingTop(4).Text(AmountInWordsService.Convert(mainTotal + kpSum)).Italic().FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                });
            });
        }

        /// <summary>
        /// Returns the widest rendered width (in PDF points) of every amount
        /// rendered at the matching row's font size and weight, plus padding.
        /// The grand-total row uses 12 pt Bold; all others use the default
        /// 9 pt SemiBold.
        /// </summary>
        private static float ComputeAmountColumnWidth(
            IEnumerable<(double amount, bool isGrand)> rows)
        {
            float widest = 0f;
            foreach (var (amount, isGrand) in rows)
            {
                float fontSize = isGrand ? GrandTotalFontPt : DefaultRowFontPt;
                string rendered = $"{MoneyFormatService.Format(amount)} руб.";
                float measured = MeasureTextWidthPt(rendered, fontSize, bold: true);
                if (measured > widest) widest = measured;
            }
            return widest + AmountColumnPaddingPt;
        }

        /// <summary>
        /// Measures a text width using System.Drawing.Graphics (Skia-backed
        /// measurement that matches QuestPDF's renderer closely).
        /// Sets <see cref="System.Drawing.Graphics.PageUnit"/> to Point so the returned
        /// width is in PDF points (1pt = 1/72 in), matching what
        /// <c>ConstantItem</c> expects — the 96-DPI default would have
        /// produced pixels and made the column ~25% too narrow.
        /// "Bold" is intentional for both SemiBold (600) and Bold (700)
        /// rows: <see cref="System.Drawing.FontStyle"/> has no SemiBold variant, and
        /// measuring as Bold slightly over-sizes regular rows, which is
        /// the safer (never-wrap) direction.
        /// </summary>
        private static float MeasureTextWidthPt(string text, float fontSizePt, bool bold)
        {
            // 1×1 bitmap + Graphics is the standard lightweight way to call
            // MeasureString without spinning up an on-screen window.
            using var bitmap = new System.Drawing.Bitmap(1, 1);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.PageUnit = System.Drawing.GraphicsUnit.Point;
            var style = bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular;
            using var font = new System.Drawing.Font("Segoe UI", fontSizePt, style);
            return graphics.MeasureString(text, font).Width;
        }
    }
}
