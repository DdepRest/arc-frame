using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Builds an A4 FlowDocument for in-app preview and physical printing.
    /// </summary>
    public class FlowDocumentBuilder
    {
        private const double MmToDip = 96.0 / 25.4;
        private const double TableContentWidthDip = 670.0;
        private const double CellOverheadDip = 14.0;
        private const double DrawingColumnWidthDip = 0.09 * TableContentWidthDip;

        /// <summary>
        /// Builds an A4 FlowDocument for in-app preview and physical printing.
        /// Returns <c>null</c> if there are no valid (non-empty, Total &gt; 0) items.
        /// </summary>
        public FlowDocument? Build(
            List<OrderItem> items,
            ClientInfo clientInfo,
            double totalAmount,
            string amountInWords)
        {
            var valid = (items ?? new List<OrderItem>())
                .Where(i => !string.IsNullOrEmpty(i.Name) && i.Total > 0)
                .ToList();
            if (valid.Count == 0) return null;

            clientInfo ??= new ClientInfo();

            var doc = new FlowDocument
            {
                PageWidth = 210 * MmToDip,
                PageHeight = 297 * MmToDip,
                PagePadding = new Thickness(
                    16 * MmToDip,
                    30 * MmToDip,
                    16 * MmToDip,
                    14 * MmToDip),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 9 * 96.0 / 72.0,
                ColumnWidth = TableContentWidthDip,
                IsColumnWidthFlexible = false,
                IsOptimalParagraphEnabled = false,
                IsHyphenationEnabled = false,
                Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)),
                Background = Brushes.White,
                TextAlignment = TextAlignment.Left
            };

            doc.Blocks.Add(BuildDocumentHeader(clientInfo));
            doc.Blocks.Add(BuildContractLine(clientInfo));
            doc.Blocks.Add(BuildClientBlock(clientInfo));
            doc.Blocks.Add(BuildItemsTable(valid));
            doc.Blocks.Add(BuildTotalSection(totalAmount, amountInWords, clientInfo));
            BuildAdditionalKpSection(doc, clientInfo, totalAmount);
            BuildNotesSection(doc, clientInfo);
            doc.Blocks.Add(BuildTermsAndSignatureFigure());

            return doc;
        }

        private static Paragraph BuildDocumentHeader(ClientInfo _)
        {
            var p = new Paragraph
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6),
                FontSize = 13.5 * 96.0 / 72.0,
                FontWeight = FontWeights.Bold
            };
            p.Inlines.Add(new Run("КОММЕРЧЕСКОЕ ПРЕДЛОЖЕНИЕ"));
            return p;
        }

        private static Paragraph BuildContractLine(ClientInfo clientInfo)
        {
            var p = new Paragraph
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12),
                FontSize = 11 * 96.0 / 72.0,
                FontWeight = FontWeights.SemiBold
            };
            string num = string.IsNullOrWhiteSpace(clientInfo.ContractNumber) ? "б/н" : clientInfo.ContractNumber.Trim();
            string date = clientInfo.ContractDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            p.Inlines.Add(new Run($"№ {num} от {date}"));
            return p;
        }

        private static Block BuildClientBlock(ClientInfo clientInfo)
        {
            // Grid-in-BlockUIContainer avoids WPF FlowDocument Table column-
            // measurement bugs. Auto column fits the longest label; Star column
            // takes remaining space — value text starts right after the label.
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 12),
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto,
                MinWidth = 0
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            var rowDefs = new List<RowDefinition>();

            var borderBrush = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
            borderBrush.Freeze();
            var clientFontSize = 10 * 96.0 / 72.0;

            AddClientGridRow(grid, rowDefs, "Заказчик:", clientInfo.ClientName,
                clientFontSize, borderBrush);
            AddClientGridRow(grid, rowDefs, "Телефон:",   clientInfo.ClientPhone,
                clientFontSize, borderBrush);
            AddClientGridRow(grid, rowDefs, "Адрес:",     clientInfo.ClientAddress,
                clientFontSize, borderBrush);

            foreach (var rd in rowDefs)
                grid.RowDefinitions.Add(rd);

            // Fallback: if no rows were added (all fields empty), return
            // an empty Section to avoid layout issues.
            if (grid.RowDefinitions.Count == 0)
                return new Section();

            return new BlockUIContainer(grid);
        }

        private static void AddClientGridRow(
            Grid grid, List<RowDefinition> rowDefs,
            string label, string? value,
            double fontSize, Brush borderBrush)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var cleanValue = value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (cleanValue.Length == 0) return;

            int rowIdx = rowDefs.Count;
            rowDefs.Add(new RowDefinition { Height = GridLength.Auto });

            var labelTb = new TextBlock
            {
                Text = label,
                FontSize = fontSize,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 6, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(labelTb, rowIdx);
            Grid.SetColumn(labelTb, 0);
            grid.Children.Add(labelTb);

            var valueTb = new TextBlock
            {
                Text = cleanValue,
                FontSize = fontSize,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(valueTb, rowIdx);
            Grid.SetColumn(valueTb, 1);
            grid.Children.Add(valueTb);

            // Simulate bottom border via a 1px-thick Border at the bottom
            var line = new System.Windows.Shapes.Rectangle
            {
                Height = 1,
                Fill = borderBrush,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            Grid.SetRow(line, rowIdx);
            Grid.SetColumn(line, 0);
            Grid.SetColumnSpan(line, 2);
            grid.Children.Add(line);
        }

        private static Table BuildItemsTable(List<OrderItem> validItems)
        {
            var t = new Table
            {
                CellSpacing = 0,
                TextAlignment = TextAlignment.Left
            };

            double[] widths = {
                0.04, 0.115, 0.12, 0.075, 0.07, 0.07,
                0.07, 0.08, 0.05, 0.09, 0.13, 0.09
            };

            var usableWidths = new double[widths.Length];
            for (int i = 0; i < widths.Length; i++)
                usableWidths[i] = widths[i] * TableContentWidthDip - CellOverheadDip;
            foreach (double w in widths)
                t.Columns.Add(new TableColumn { Width = new GridLength(w, GridUnitType.Star) });

            var bodyFontSize = 9 * 96.0 / 72.0;
            var headerPt = 8.0;

            var header = new TableRowGroup();
            var hrow = new TableRow
            {
                Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                FontSize = headerPt * 96.0 / 72.0,
                FontWeight = FontWeights.SemiBold
            };
            string[] headers = {
                "№", "Наименование", "Цвет", "Ш,\u00A0мм", "В,\u00A0мм", "Кол-во",
                "Монтаж", "Площ./Дл.", "Ед.", "Цена", "Сумма", "Чертёж"
            };
            for (int i = 0; i < headers.Length; i++)
            {
                if (i == 7)
                {
                    var p = new Paragraph(new Run(headers[i]))
                    {
                        TextAlignment = AlignByIndex(i),
                        FontWeight = FontWeights.SemiBold,
                        FontSize = headerPt * 96.0 / 72.0,
                        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                        LineHeight = headerPt * 96.0 / 72.0 * 1.35,
                        Margin = new Thickness(3, 4, 3, 4)
                    };
                    hrow.Cells.Add(MakeCell(p));
                }
                else
                {
                    hrow.Cells.Add(MakeNonWrappingCell(
                        headers[i],
                        headerPt * 96.0 / 72.0,
                        AlignByIndex(i),
                        AlignByIndex(i) == TextAlignment.Right ? HorizontalAlignment.Right : HorizontalAlignment.Center,
                        usableWidths[i],
                        isBold: true));
                }
            }
            header.Rows.Add(hrow);
            t.RowGroups.Add(header);

            var body = new TableRowGroup();
            bool alt = false;
            int idx = 1;
            foreach (var item in validItems)
            {
                var row = new TableRow
                {
                    Background = alt ? new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)) : Brushes.Transparent,
                    FontSize = bodyFontSize
                };

                row.Cells.Add(MakeCenteredCell(idx.ToString(), bodyFontSize, usableWidths[0]));
                if (item.IsSlope && item.SlopeData != null)
                {
                    var slopeNamePara = new Paragraph
                    {
                        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                        LineHeight = bodyFontSize * 1.4,
                        Margin = new Thickness(3, 4, 3, 4)
                    };
                    slopeNamePara.Inlines.Add(new Run(item.DisplayName ?? ""));
                    slopeNamePara.Inlines.Add(new LineBreak());
                    int depthMm = (int)(item.SlopeData.DepthM * 1000);
                    string economyNote = item.SlopeData.IsProfileEconomyApplied
                        ? " (с экономией)"
                        : "";
                    var dimsRun = new Run($"В: {item.Height} Ш: {item.Width} Г: {depthMm}{economyNote}")
                    {
                        FontSize = bodyFontSize * 0.8,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88))
                    };
                    slopeNamePara.Inlines.Add(dimsRun);
                    row.Cells.Add(MakeCell(slopeNamePara));
                }
                else
                {
                    row.Cells.Add(MakeCell(new Paragraph(new Run(item.DisplayName ?? ""))
                    {
                        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                        LineHeight = bodyFontSize * 1.4,
                        Margin = new Thickness(3, 4, 3, 4)
                    }));
                }
                row.Cells.Add(MakeCenteredCell(item.Color ?? "", bodyFontSize, usableWidths[2]));
                row.Cells.Add(MakeCenteredCell(FormatIntWithNbsp(item.Width), bodyFontSize, usableWidths[3]));
                row.Cells.Add(MakeCenteredCell(FormatIntWithNbsp(item.Height), bodyFontSize, usableWidths[4]));
                row.Cells.Add(MakeCenteredCell(item.QuantityDisplay, bodyFontSize, usableWidths[5]));
                row.Cells.Add(MakeCenteredCell(item.KpInstallationDisplay ?? "", bodyFontSize, usableWidths[6]));
                if (item.IsAmountOnly || (item.IsQuantityOptional && item.Quantity <= 1))
                    row.Cells.Add(MakeCenteredCell("", bodyFontSize, usableWidths[7]));
                else
                    row.Cells.Add(MakeCenteredCell(item.CalculatedValue.ToString("F3", CultureInfo.InvariantCulture), bodyFontSize, usableWidths[7]));
                row.Cells.Add(MakeCenteredCell(item.Unit ?? "", bodyFontSize, usableWidths[8]));
                row.Cells.Add(MakeRightAlignedCell(MoneyFormatService.Format(item.Price), usableWidths[9]));
                row.Cells.Add(MakeRightAlignedCell(MoneyFormatService.Format(item.TotalWithDeduction), usableWidths[10]));

                var imageCell = new TableCell
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(4, 5, 4, 5)
                };
                int estLines = item.IsSlope ? 2 : (item.DisplayName?.Length ?? 0) > 28 ? 2 : 1;
                double textRowHeight = estLines * bodyFontSize * 1.35 + 10;
                double drawingImageHeight = 54.0 / 100.0 * 44.0;
                double minGridHeight = Math.Max(drawingImageHeight, textRowHeight);
                var drawingImage = DrawingService.CreateDrawingImageElement(item.Name, item.Width, item.Height, displayWidth: 42);
                var centeredContent = DrawingService.WrapForCentering(drawingImage, minGridHeight);
                imageCell.Blocks.Add(new BlockUIContainer(centeredContent) { Margin = new Thickness(0) });
                row.Cells.Add(imageCell);

                body.Rows.Add(row);
                alt = !alt;
                idx++;
            }

            double grand = validItems.Sum(i => i.TotalWithDeduction);
            var totalRow = new TableRow
            {
                Background = new SolidColorBrush(Color.FromRgb(0xD5, 0xD5, 0xD5)),
                FontWeight = FontWeights.Bold,
                FontSize = 9 * 96.0 / 72.0
            };
            var totalLabelCell = new TableCell(new Paragraph(new Run("ИТОГО:"))
            {
                TextAlignment = TextAlignment.Right,
                FontWeight = FontWeights.Bold,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                LineHeight = 9 * 96.0 / 72.0 * 1.35
            })
            {
                ColumnSpan = 10,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                BorderThickness = new Thickness(1, 2, 1, 1)
            };
            var totalSumCell = new TableCell(new Paragraph(new Run(MoneyFormatService.Format(grand)))
            {
                TextAlignment = TextAlignment.Right,
                FontWeight = FontWeights.Bold,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                LineHeight = 9 * 96.0 / 72.0 * 1.35
            })
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                BorderThickness = new Thickness(1, 2, 1, 1)
            };
            var totalEmpty = new TableCell(new Paragraph(new Run("")))
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                BorderThickness = new Thickness(1, 2, 1, 1)
            };
            totalRow.Cells.Add(totalLabelCell);
            totalRow.Cells.Add(totalSumCell);
            totalRow.Cells.Add(totalEmpty);
            body.Rows.Add(totalRow);
            t.RowGroups.Add(body);
            return t;
        }

        private static TextAlignment AlignByIndex(int i)
        {
            if (i == 1) return TextAlignment.Left;
            if (i is 9 or 10) return TextAlignment.Right;
            return TextAlignment.Center;
        }

        private static readonly NumberFormatInfo IntWithNbsp = CreateIntWithNbspFormat();

        private static NumberFormatInfo CreateIntWithNbspFormat()
        {
            var nf = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            nf.NumberGroupSeparator = "\u00A0";
            nf.NumberDecimalDigits = 0;
            return nf;
        }

        private static string FormatIntWithNbsp(double value)
        {
            long v = (long)value;
            return v >= 10_000
                ? v.ToString("N0", IntWithNbsp)
                : v.ToString(CultureInfo.InvariantCulture);
        }

        private static TableCell MakeCell(Block content)
        {
            var cellBorder = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
            cellBorder.Freeze();
            return new TableCell(content)
            {
                BorderBrush = cellBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 5, 4, 5)
            };
        }

        private static TableCell MakeNonWrappingCell(
            string text,
            double fontSize,
            TextAlignment textAlignment,
            HorizontalAlignment horizontalAlignment,
            double availableWidthDip,
            bool isBold = false)
        {
            text ??= "";

            double effectiveSize = fontSize;
            if (availableWidthDip > 0 && !string.IsNullOrEmpty(text))
            {
                var typeface = new Typeface(
                    new FontFamily("Segoe UI"),
                    FontStyles.Normal,
                    isBold ? FontWeights.SemiBold : FontWeights.Normal,
                    FontStretches.Normal);

                double Measure(double size) => new FormattedText(
                    text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    typeface, size, Brushes.Black, 1.0).WidthIncludingTrailingWhitespace;

                while (Measure(effectiveSize) > availableWidthDip
                       && effectiveSize > fontSize * 0.75)
                {
                    effectiveSize -= 0.5;
                }
            }

            var tb = new TextBlock
            {
                Text = text,
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
                Margin = new Thickness(horizontalMargin, 4, horizontalMargin, 4)
            };
            return MakeCell(container);
        }

        private static TableCell MakeCenteredCell(string text, double fontSize, double availableWidthDip)
            => MakeNonWrappingCell(text, fontSize, TextAlignment.Center, HorizontalAlignment.Center, availableWidthDip);

        private static TableCell MakeRightAlignedCell(string text, double availableWidthDip)
            => MakeNonWrappingCell(
                text,
                9 * 96.0 / 72.0,
                TextAlignment.Right,
                HorizontalAlignment.Right,
                availableWidthDip);

        private static Section BuildTotalSection(double totalAmount, string amountInWords, ClientInfo clientInfo)
        {
            bool isSubtotal = clientInfo != null &&
                              clientInfo.HasAdditionalKp &&
                              clientInfo.AdditionalKps != null &&
                              clientInfo.AdditionalKps.Any(k => k.IsActive && k.Amount > 0);

            double labelPt = isSubtotal ? 9.0 : 10.0;
            double amountPt = isSubtotal ? 11.0 : 13.0;

            var sec = new Section
            {
                Margin = new Thickness(0, 14, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                BorderThickness = new Thickness(0, 2, 0, 0)
            };
            var p = new Paragraph
            {
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, 8, DrawingColumnWidthDip, 0)
            };
            p.Inlines.Add(new Run("ИТОГО: ") { FontSize = labelPt * 96.0 / 72.0, FontWeight = FontWeights.Bold });
            p.Inlines.Add(new Run(MoneyFormatService.Format(totalAmount) + " руб.")
            {
                FontSize = amountPt * 96.0 / 72.0,
                FontWeight = FontWeights.Bold
            });
            sec.Blocks.Add(p);

            var pw = new Paragraph
            {
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, 4, DrawingColumnWidthDip, 0),
                FontStyle = FontStyles.Italic,
                FontSize = 8.5 * 96.0 / 72.0,
                Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44))
            };
            pw.Inlines.Add(new Run(amountInWords ?? ""));
            sec.Blocks.Add(pw);
            return sec;
        }

        private static void BuildAdditionalKpSection(FlowDocument doc, ClientInfo clientInfo, double mainTotal)
        {
            if (!clientInfo.HasAdditionalKp) return;
            var kps = clientInfo.AdditionalKps.Where(k => k.IsActive).ToList();
            if (kps.Count == 0) return;

            double kpSum = kps.Sum(k => k.Amount);

            var titlePara = new Paragraph(new Run(kps.Count == 1 ? "Дополнительное КП" : "Дополнительные КП"))
            {
                FontWeight = FontWeights.Bold,
                FontSize = 9 * 96.0 / 72.0,
                Margin = new Thickness(0, 12, 0, 4)
            };
            doc.Blocks.Add(titlePara);

            if (kpSum <= 0)
            {
                foreach (var kp in kps)
                {
                    if (string.IsNullOrWhiteSpace(kp.Number)) continue;
                    doc.Blocks.Add(new Paragraph(new Run($"К данному заказу прилагается КП № {kp.Number.Trim()}")));
                }
                return;
            }

            double bodyFont = 9 * 96.0 / 72.0;
            double grandFont = 10.5 * 96.0 / 72.0;
            double grandAmountFont = 12 * 96.0 / 72.0;
            double italicFont = 8.5 * 96.0 / 72.0;

            double labelStarWidth = 0.72;
            double amountStarWidth = 0.28;

            Section box = new()
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 6, 0, 6)
            };

            var table = new Table { CellSpacing = 0, Margin = new Thickness(0) };
            table.Columns.Add(new TableColumn { Width = new GridLength(labelStarWidth, GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width = new GridLength(amountStarWidth, GridUnitType.Star) });
            var group = new TableRowGroup();

            void AddRow(string label, string amount, double fontSize,
                FontWeight labelWeight, FontWeight amountWeight,
                Thickness? topBorder = null, double? amountFontSize = null)
            {
                var row = new TableRow();
                var labelPara = new Paragraph(new Run(label))
                {
                    FontWeight = labelWeight,
                    FontSize = fontSize,
                    Margin = new Thickness(0, 2, 0, 2)
                };
                var amountPara = new Paragraph(new Run(amount))
                {
                    FontWeight = amountWeight,
                    FontSize = amountFontSize ?? fontSize,
                    TextAlignment = TextAlignment.Right,
                    Margin = new Thickness(0, 2, 0, 2)
                };
                var labelCell = new TableCell(labelPara) { Padding = new Thickness(0) };
                var amountCell = new TableCell(amountPara) { Padding = new Thickness(0) };
                if (topBorder.HasValue)
                {
                    var border = topBorder.Value;
                    labelCell.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                    labelCell.BorderThickness = border;
                    labelCell.Padding = new Thickness(0, 5, 0, 0);
                    amountCell.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
                    amountCell.BorderThickness = border;
                    amountCell.Padding = new Thickness(0, 5, 0, 0);
                }
                row.Cells.Add(labelCell);
                row.Cells.Add(amountCell);
                group.Rows.Add(row);
            }

            foreach (var kp in kps)
            {
                string left = string.IsNullOrWhiteSpace(kp.Number)
                    ? "Дополнительное КП"
                    : $"К данному заказу прилагается КП № {kp.Number.Trim()}";
                AddRow(left, MoneyFormatService.Format(kp.Amount) + " руб.", bodyFont,
                    FontWeights.Normal, FontWeights.SemiBold);
            }

            AddRow("Сумма основного КП:", MoneyFormatService.Format(mainTotal) + " руб.", bodyFont,
                FontWeights.Medium, FontWeights.SemiBold);
            foreach (var kp in kps.Where(k => k.Amount > 0))
            {
                string lbl = string.IsNullOrWhiteSpace(kp.Number)
                    ? "Сумма доп. КП"
                    : $"Сумма доп. КП № {kp.Number.Trim()}";
                AddRow(lbl, MoneyFormatService.Format(kp.Amount) + " руб.", bodyFont,
                    FontWeights.Medium, FontWeights.SemiBold);
            }

            double grand = mainTotal + kpSum;
            AddRow("ОБЩИЙ ИТОГ К ОПЛАТЕ:", MoneyFormatService.Format(grand) + " руб.", grandFont,
                FontWeights.Bold, FontWeights.Bold,
                topBorder: new Thickness(0, 1.5, 0, 0),
                amountFontSize: grandAmountFont);

            table.RowGroups.Add(group);
            box.Blocks.Add(table);

            box.Blocks.Add(new Paragraph(new Run(AmountInWordsService.Convert(grand)))
            {
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = italicFont,
                Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44))
            });
            doc.Blocks.Add(box);
        }

        private static void BuildNotesSection(FlowDocument doc, ClientInfo clientInfo)
        {
            if (string.IsNullOrWhiteSpace(clientInfo.Notes)) return;
            var sec = new Section
            {
                Margin = new Thickness(0, 12, 0, 0),
                Padding = new Thickness(12, 8, 12, 8),
                Background = new SolidColorBrush(Color.FromRgb(0xF9, 0xF9, 0xF9)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                BorderThickness = new Thickness(3, 0, 0, 0)
            };
            sec.Blocks.Add(new Paragraph(new Run("ПРИМЕЧАНИЯ")
            {
                FontWeight = FontWeights.Bold,
                FontSize = 8.5 * 96.0 / 72.0
            }));

            var noteFontSize = 8.5 * 96.0 / 72.0;
            foreach (var line in NotesFormatter.Parse(clientInfo.Notes))
            {
                var p = new Paragraph
                {
                    FontSize = noteFontSize,
                    Margin = new Thickness(line.IsListItem ? 12 : 0, 3, 0, 0)
                };

                foreach (var inline in NotesRenderer.ToInlines(line))
                    p.Inlines.Add(inline);

                sec.Blocks.Add(p);
            }

            doc.Blocks.Add(sec);
        }

        private static Section BuildTermsAndSignatureFigure()
        {
            var container = new Section
            {
                Margin = new Thickness(0, 40, 0, 0)
            };

            var termsSec = new Section
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 8, 0, 0),
                Margin = new Thickness(0, 0, 0, 20)
            };
            termsSec.Blocks.Add(new Paragraph(new Run("УСЛОВИЯ"))
            {
                FontWeight = FontWeights.Bold,
                FontSize = 8 * 96.0 / 72.0
            });
            string[] items = {
                "— Срок действия коммерческого предложения — 5 рабочих дней.",
                "— Оплата производится на основании счёта.",
                "— Цены указаны с учётом стоимости материалов."
            };
            foreach (var line in items)
                termsSec.Blocks.Add(new Paragraph(new Run(line))
                {
                    FontSize = 7.5 * 96.0 / 72.0,
                    Margin = new Thickness(0, 1, 0, 1)
                });
            container.Blocks.Add(termsSec);

            var sigTable = new Table { CellSpacing = 0 };
            sigTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            sigTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
            var rowGroup = new TableRowGroup();
            var row = new TableRow();

            var left = new TableCell(new Paragraph(new Run("Исполнитель _____________"))
            {
                FontSize = 9 * 96.0 / 72.0,
                FontWeight = FontWeights.SemiBold
            });
            left.Blocks.Add(new Paragraph(new Run("(подпись, печать)")
            {
                FontSize = 7.5 * 96.0 / 72.0,
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88))
            }));

            var right = new TableCell(new Paragraph(new Run("Заказчик _____________"))
            {
                FontSize = 9 * 96.0 / 72.0,
                FontWeight = FontWeights.SemiBold
            });
            right.Blocks.Add(new Paragraph(new Run("(подпись)")
            {
                FontSize = 7.5 * 96.0 / 72.0,
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88))
            }));

            var sigLine = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            left.BorderBrush = sigLine;
            left.BorderThickness = new Thickness(0, 0, 0, 1);
            right.BorderBrush = sigLine;
            right.BorderThickness = new Thickness(0, 0, 0, 1);

            row.Cells.Add(left);
            row.Cells.Add(right);
            rowGroup.Rows.Add(row);
            sigTable.RowGroups.Add(rowGroup);
            container.Blocks.Add(sigTable);

            return container;
        }
    }
}
