using System.Collections.Generic;
using System.Linq;
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

        private static string ExtractText(FlowDocument doc)
        {
            var sb = new System.Text.StringBuilder();
            ExtractTextFromBlocks(doc.Blocks, sb);
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
                }
            }
        }
    }
}
