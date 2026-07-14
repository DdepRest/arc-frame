using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MosquitoNetCalculator.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Exports a КП as a PDF file via QuestPDF. Mirrors
    /// <see cref="FlowDocumentBuilder.Build"/> layout but uses QuestPDF's
    /// fluent API for native vector output.
    /// </summary>
    public class PdfExportService
    {
        static PdfExportService()
        {
            QuestPDF.Settings.EnableDebugging = true;
        }

        /// <summary>
        /// Exports a КП as a PDF file.
        /// </summary>
        public void Export(
            string filePath,
            List<OrderItem> items,
            ClientInfo clientInfo,
            double totalAmount,
            string amountInWords)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("filePath must be non-empty", nameof(filePath));

            var valid = (items ?? new List<OrderItem>())
                .Where(i => !string.IsNullOrEmpty(i.Name) && i.Total > 0)
                .ToList();
            clientInfo ??= new ClientInfo();
            bool hasItems = valid.Count > 0;

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
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
                                    cd.RelativeColumn(1.5f);
                                    cd.RelativeColumn(1.3f);
                                    cd.RelativeColumn(1.3f);
                                    cd.RelativeColumn(1.4f);
                                    cd.RelativeColumn(1.5f);
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
                                    t.Cell().Element(c2 => EDataCell(c2, bg)).Text(item.DisplayName ?? "").FontSize(8.5f);
                                    t.Cell().Element(c2 => EDataCell(c2, bg)).Text(item.Color ?? "").AlignCenter().FontSize(8.5f);
                                    t.Cell().Element(c2 => EDataCell(c2, bg)).Text(item.Width.ToString("F0")).AlignCenter().FontSize(8.5f);
                                    t.Cell().Element(c2 => EDataCell(c2, bg)).Text(item.Height.ToString("F0")).AlignCenter().FontSize(8.5f);
                                    t.Cell().Element(c2 => EDataCell(c2, bg)).Text(item.QuantityDisplay).AlignCenter().FontSize(8.5f);
                                    t.Cell().Element(c2 => EDataCell(c2, bg)).Text(item.KpInstallationDisplay ?? "").AlignCenter().FontSize(8.5f);
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
                            col.Item().Background(Colors.Grey.Lighten4).BorderLeft(3).BorderColor(Colors.Grey.Medium)
                                .Padding(8).Text(t =>
                                {
                                    t.Line("ПРИМЕЧАНИЯ").Bold().FontSize(8.5f);
                                    t.Span(clientInfo.Notes.Trim()).FontSize(8.5f);
                                });

                        col.Item().PaddingTop(20).BorderTop(1).BorderColor(Colors.Grey.Medium).PaddingTop(8).Text(t =>
                        {
                            t.Line("УСЛОВИЯ").Bold().FontSize(8);
                            t.Line("– Срок действия коммерческого предложения — 5 рабочих дней.").FontSize(7.5f);
                            t.Line("– Оплата производится на основании счёта.").FontSize(7.5f);
                            t.Span("– Цены указаны с учётом стоимости материалов.").FontSize(7.5f);
                        });

                        col.Item().PaddingTop(32).Row(r =>
                        {
                            r.RelativeItem().Text("Исполнитель _____________").Bold().FontSize(9);
                            r.RelativeItem().Text("Заказчик _____________").Bold().FontSize(9);
                        });
                    });
                });
            });

            document.GeneratePdf(filePath);
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
                .Text(cleanValue).FontSize(9);
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

                col2.Item().Background(Colors.Grey.Lighten4).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(box =>
                {
                    foreach (var kp in kps)
                    {
                        string left = string.IsNullOrWhiteSpace(kp.Number)
                            ? "Дополнительное КП" : $"К данному заказу прилагается КП № {kp.Number.Trim()}";
                        box.Item().Row(r =>
                        {
                            r.RelativeItem().Text(left).FontSize(9);
                            r.ConstantItem(80).AlignRight().Text($"{MoneyFormatService.Format(kp.Amount)} руб.").SemiBold().FontSize(9);
                        });
                    }

                    box.Item().PaddingTop(8).Row(r =>
                    {
                        r.RelativeItem().Text("Сумма основного КП:");
                        r.ConstantItem(80).AlignRight().Text($"{MoneyFormatService.Format(mainTotal)} руб.").SemiBold();
                    });
                    foreach (var kp in kps.Where(k => k.Amount > 0))
                    {
                        string lbl = string.IsNullOrWhiteSpace(kp.Number)
                            ? "Сумма доп. КП" : $"Сумма доп. КП № {kp.Number.Trim()}";
                        box.Item().Row(r =>
                        {
                            r.RelativeItem().Text($"{lbl}:");
                            r.ConstantItem(80).AlignRight().Text($"{MoneyFormatService.Format(kp.Amount)} руб.").SemiBold();
                        });
                    }
                    box.Item().BorderTop(1.5f).BorderColor(Colors.Black).PaddingTop(4).Row(r =>
                    {
                        r.RelativeItem().Text("ОБЩИЙ ИТОГ:").Bold().FontSize(10.5f);
                        r.ConstantItem(80).AlignRight().Text($"{MoneyFormatService.Format(mainTotal + kpSum)} руб.").Bold().FontSize(12);
                    });
                    box.Item().PaddingTop(4).Text(AmountInWordsService.Convert(mainTotal + kpSum)).Italic().FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                });
            });
        }
    }
}
