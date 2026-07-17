using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class FlowDocumentBuilderTests
    {
        private readonly FlowDocumentBuilder _builder = new();

        [Fact]
        public void Build_ReturnsNull_WhenNoValidItems()
        {
            var result = WpfTestHelper.RunOnSta(() =>
                _builder.Build(new List<OrderItem> { new() { Name = "", Total = 0 } }, new ClientInfo(), 0, ""));
            Assert.Null(result);
        }

        [Fact]
        public void Build_ReturnsNull_WhenEmptyList()
        {
            var result = WpfTestHelper.RunOnSta(() =>
                _builder.Build(new List<OrderItem>(), new ClientInfo(), 0, ""));
            Assert.Null(result);
        }

        [Fact]
        public void Build_ReturnsDocument_WithValidItems()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1800 }
            };
            var result = WpfTestHelper.RunOnSta(() =>
                _builder.Build(items, new ClientInfo { ContractNumber = "1-1" }, 1800, ""));
            Assert.NotNull(result);
            Assert.NotEmpty(result!.Blocks);
        }

        [Fact]
        public void Build_ContainsHeaderAndContractLine()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1800 }
            };
            WpfTestHelper.RunOnSta(() =>
            {
                var doc = _builder.Build(items, new ClientInfo { ContractNumber = "1-1", ContractDate = new System.DateTime(2026, 1, 15) }, 1800, "");
                Assert.NotNull(doc);
                var text = ExtractText(doc!);
                Assert.Contains("КОММЕРЧЕСКОЕ ПРЕДЛОЖЕНИЕ", text);
                Assert.Contains("1-1", text);
                Assert.Contains("15.01.2026", text);
            });
        }

        [Fact]
        public void Build_SkipsItemsWithZeroOrNegativeTotal()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Total = 0 },
                new() { Name = "Отлив", Total = 100 }
            };
            WpfTestHelper.RunOnSta(() =>
            {
                var doc = _builder.Build(items, new ClientInfo(), 100, "");
                var text = ExtractText(doc!);
                Assert.DoesNotContain("Anwis", text);
                Assert.Contains("Отлив", text);
            });
        }

        [Fact]
        public void Build_SlopeItem_WithProfileEconomy_ShowsEconomyNote()
        {
            var slopeData = new SlopeCalculation
            {
                WidthMm = 1000,
                HeightMm = 1000,
                DepthM = 0.15,
                WindowCount = 2,
                IsProfileEconomyApplied = true
            };
            slopeData.Sandwich.Quantity = 1;
            slopeData.Sandwich.Price = 5000;
            var slopeItem = new OrderItem
            {
                Name = "Откос",
                Width = 1000,
                Height = 1000,
                Quantity = 2,
                SlopeData = slopeData
            };

            WpfTestHelper.RunOnSta(() =>
            {
                var doc = _builder.Build(new List<OrderItem> { slopeItem }, new ClientInfo(), slopeItem.Total, "");
                Assert.NotNull(doc);
                var text = ExtractText(doc!);
                Assert.Contains("(с экономией)", text);
            });
        }

        [Fact]
        public void Build_SlopeItem_WithoutProfileEconomy_DoesNotShowEconomyNote()
        {
            var slopeData = new SlopeCalculation
            {
                WidthMm = 1000,
                HeightMm = 1000,
                DepthM = 0.15,
                WindowCount = 2,
                IsProfileEconomyApplied = false
            };
            slopeData.Sandwich.Quantity = 1;
            slopeData.Sandwich.Price = 5000;
            var slopeItem = new OrderItem
            {
                Name = "Откос",
                Width = 1000,
                Height = 1000,
                Quantity = 2,
                SlopeData = slopeData
            };

            WpfTestHelper.RunOnSta(() =>
            {
                var doc = _builder.Build(new List<OrderItem> { slopeItem }, new ClientInfo(), slopeItem.Total, "");
                Assert.NotNull(doc);
                var text = ExtractText(doc!);
                Assert.DoesNotContain("(с экономией)", text);
            });
        }

        [Fact]
        public void Build_FormattedNotes_RendersBoldItalicAndColor()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1800 }
            };
            var client = new ClientInfo
            {
                Notes = "**важно** *курсив* [color=#D32F2F]красный[/color]\n- пункт"
            };

            WpfTestHelper.RunOnSta(() =>
            {
                var doc = _builder.Build(items, client, 1800, "");
                Assert.NotNull(doc);

                var notesSection = doc!.Blocks.OfType<Section>().FirstOrDefault(s =>
                {
                    var text = ExtractText(s);
                    return text.Contains("ПРИМЕЧАНИЯ");
                });
                Assert.NotNull(notesSection);

                var paragraphs = notesSection!.Blocks.OfType<Paragraph>().ToList();
                Assert.Equal(3, paragraphs.Count);

                var firstParagraphRuns = paragraphs[1].Inlines.OfType<Run>().ToList();
                Assert.Contains(firstParagraphRuns, r => r.Text == "важно" && r.FontWeight == FontWeights.Bold);
                Assert.Contains(firstParagraphRuns, r => r.Text == "курсив" && r.FontStyle == FontStyles.Italic);
                Assert.Contains(firstParagraphRuns, r => r.Text == "красный" && r.Foreground != null);

                var secondParagraphRuns = paragraphs[2].Inlines.OfType<Run>().ToList();
                Assert.Contains(secondParagraphRuns, r => r.Text == "• ");
                Assert.Contains(secondParagraphRuns, r => r.Text == "пункт");
            });
        }

        [Fact]
        public void BuildClientBlock_UsesGridWithAutoLabelColumn()
        {
            var client = new ClientInfo
            {
                ClientName = "Иванов И.И.",
                ClientPhone = "+7-999-123-45-67",
                ClientAddress = "РТУТНАЯ 10"
            };
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1800 }
            };

            WpfTestHelper.RunOnSta(() =>
            {
                var doc = _builder.Build(items, client, 1800, "");
                Assert.NotNull(doc);

                // Client block is a BlockUIContainer (Grid inside)
                var block = doc!.Blocks.OfType<BlockUIContainer>().FirstOrDefault();
                Assert.NotNull(block);
                var grid = block!.Child as Grid;
                Assert.NotNull(grid);

                // 2 columns: Auto (label) + Star (value)
                Assert.Equal(2, grid!.ColumnDefinitions.Count);
                Assert.Equal(GridUnitType.Auto, grid.ColumnDefinitions[0].Width.GridUnitType);
                Assert.Equal(GridUnitType.Star, grid.ColumnDefinitions[1].Width.GridUnitType);

                // 3 rows + 3 bottom-border Rectangles = 9 children
                Assert.Equal(9, grid.Children.Count);

                // Label TextBlocks should be SemiBold
                var labelTbs = grid.Children.OfType<TextBlock>()
                    .Where(tb => Grid.GetColumn(tb) == 0).ToList();
                Assert.Equal(3, labelTbs.Count);
                Assert.All(labelTbs, tb => Assert.Equal(FontWeights.SemiBold, tb.FontWeight));

                Assert.Equal("Заказчик:", labelTbs[0].Text);
                Assert.Equal("Телефон:",   labelTbs[1].Text);
                Assert.Equal("Адрес:",     labelTbs[2].Text);
            });
        }

        [Fact]
        public void BuildClientBlock_SkipsEmptyFields()
        {
            var client = new ClientInfo
            {
                ClientName = "Иванов И.И.",
                ClientPhone = "",
                ClientAddress = null!
            };
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1800 }
            };

            WpfTestHelper.RunOnSta(() =>
            {
                var doc = _builder.Build(items, client, 1800, "");
                Assert.NotNull(doc);

                var block = doc!.Blocks.OfType<BlockUIContainer>().FirstOrDefault();
                Assert.NotNull(block);
                var grid = block!.Child as Grid;
                Assert.NotNull(grid);

                // Only 1 row: label + value + border = 3 children
                Assert.Equal(3, grid!.Children.Count);

                var labels = grid.Children.OfType<TextBlock>()
                    .Where(tb => Grid.GetColumn(tb) == 0).ToList();
                Assert.Single(labels);
                Assert.Equal("Заказчик:", labels[0].Text);
            });
        }

        [Fact]
        public void BuildClientBlock_AllFieldsEmpty_ReturnsEmptySection()
        {
            var client = new ClientInfo
            {
                ClientName = null!,
                ClientPhone = "",
                ClientAddress = "  "
            };
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1800 }
            };

            WpfTestHelper.RunOnSta(() =>
            {
                var doc = _builder.Build(items, client, 1800, "");
                Assert.NotNull(doc);

                // No BlockUIContainer — fallback empty Section was returned
                var containers = doc!.Blocks.OfType<BlockUIContainer>().ToList();
                Assert.Empty(containers);
            });
        }

        [Fact]
        public void BuildClientBlock_StripsNewlinesInValue()
        {
            var client = new ClientInfo
            {
                ClientName = "Иванов\r\nИ.И.",
                ClientPhone = null!,
                ClientAddress = null!
            };
            var items = new List<OrderItem>
            {
                new() { Name = "Anwis", Color = "Белый", Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1800 }
            };

            WpfTestHelper.RunOnSta(() =>
            {
                var doc = _builder.Build(items, client, 1800, "");
                Assert.NotNull(doc);

                var block = doc!.Blocks.OfType<BlockUIContainer>().FirstOrDefault();
                Assert.NotNull(block);
                var grid = block!.Child as Grid;
                Assert.NotNull(grid);

                var valueTb = grid!.Children.OfType<TextBlock>()
                    .First(tb => Grid.GetColumn(tb) == 1);
                Assert.DoesNotContain("\r\n", valueTb.Text);
                Assert.DoesNotContain("\n", valueTb.Text);
                Assert.Equal("Иванов И.И.", valueTb.Text);
            });
        }

        private static string ExtractText(FlowDocument doc)
        {
            var sb = new System.Text.StringBuilder();
            ExtractTextFromBlocks(doc.Blocks, sb);
            return sb.ToString();
        }

        private static string ExtractText(Section section)
        {
            var sb = new System.Text.StringBuilder();
            ExtractTextFromBlocks(section.Blocks, sb);
            return sb.ToString();
        }

        private static void ExtractTextFromBlocks(System.Collections.IEnumerable blocks, System.Text.StringBuilder sb)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case Paragraph p:
                        foreach (var inline in p.Inlines.OfType<Run>())
                            sb.Append(inline.Text);
                        sb.Append(' ');
                        break;
                    case Table t:
                        foreach (var rowGroup in t.RowGroups)
                            foreach (var row in rowGroup.Rows)
                                foreach (var cell in row.Cells)
                                    ExtractTextFromBlocks(cell.Blocks, sb);
                        break;
                    case Section s:
                        ExtractTextFromBlocks(s.Blocks, sb);
                        break;
                    case BlockUIContainer bcu:
                        ExtractTextFromUielement(bcu.Child, sb);
                        break;
                }
            }
        }

        private static void ExtractTextFromUielement(UIElement? element, System.Text.StringBuilder sb)
        {
            if (element == null) return;
            if (element is TextBlock tb)
            {
                sb.Append(tb.Text);
                sb.Append(' ');
            }
            if (element is Panel panel)
            {
                foreach (UIElement child in panel.Children)
                    ExtractTextFromUielement(child, sb);
            }
        }
    }
}
