using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class FactoryTextServiceTests
    {
        // ─── Generate: Ш:/В: format ────────────────────────────

        [Fact]
        public void Generate_NonAnwisItem_ContainsShVLabels()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Отлив", Width = 1000, Height = 200, Quantity = 1, Price = 500, Total = 500 }
            };
            var selectable = FactoryTextService.BuildSelectableItems(items, new List<AdditionalKpItem>());
            foreach (var si in selectable) si.IsSelected = true;

            var text = FactoryTextService.Generate("", selectable);

            Assert.Contains("Ш: 1000", text);
            Assert.Contains("В: 200", text);
        }

        [Fact]
        public void Generate_AnwisItem_Brusbox60_ContainsShVLabels()
        {
            var items = new List<OrderItem>
            {
                new()
                {
                    Name = "Anwis", Width = 1002, Height = 1170, Quantity = 1, Price = 1800, Total = 1800,
                    AnwisSizeMode = AnwisSizeMode.Брусбокс60
                }
            };
            var selectable = FactoryTextService.BuildSelectableItems(items, new List<AdditionalKpItem>());
            foreach (var si in selectable) si.IsSelected = true;

            var text = FactoryTextService.Generate("", selectable);

            // Брусбокс 60 (stored calc: 1002×1170, copy: stored − 20 = 982×1150)
            Assert.Contains("Ш: 982", text);
            Assert.Contains("В: 1150", text);
        }

        [Fact]
        public void Generate_AnwisItem_Profiplast_ContainsShVLabels()
        {
            var items = new List<OrderItem>
            {
                new()
                {
                    Name = "Anwis", AnwisSizeMode = AnwisSizeMode.Профипласт,
                    Width = 1000, Height = 1200, Quantity = 2, Price = 1800, Total = 3600,
                }
            };
            var selectable = FactoryTextService.BuildSelectableItems(items, new List<AdditionalKpItem>());
            foreach (var si in selectable) si.IsSelected = true;

            var text = FactoryTextService.Generate("", selectable);

            // Профипласт: width - 20, height - 20
            Assert.Contains("Ш: 980", text);
            Assert.Contains("В: 1180", text);
        }

        [Fact]
        public void Generate_RazmerProema_ShowsOriginalDimensions()
        {
            var items = new List<OrderItem>
            {
                new()
                {
                    Name = "Anwis", AnwisSizeMode = AnwisSizeMode.РазмерПроёма,
                    Width = 820, Height = 1020, Quantity = 1, Price = 1800, Total = 1800,
                }
            };
            var selectable = FactoryTextService.BuildSelectableItems(items, new List<AdditionalKpItem>());
            foreach (var si in selectable) si.IsSelected = true;

            var text = FactoryTextService.Generate("", selectable);

            // Размер проёма: stored calc = raw+20 = 820×1020, copy = stored−20 = 800×1000
            Assert.Contains("Ш: 800", text);
            Assert.Contains("В: 1000", text);
        }

        [Fact]
        public void Generate_AnbisItem_Gabaritny_ContainsShVLabels()
        {
            var items = new List<OrderItem>
            {
                new()
                {
                    Name = "Anwis", AnwisSizeMode = AnwisSizeMode.Габаритный,
                    Width = 1000, Height = 1200, Quantity = 1, Price = 1800, Total = 1800,
                }
            };
            var selectable = FactoryTextService.BuildSelectableItems(items, new List<AdditionalKpItem>());
            foreach (var si in selectable) si.IsSelected = true;

            var text = FactoryTextService.Generate("", selectable);

            // Габаритный: width - 20, height - 20
            Assert.Contains("Ш: 980", text);
            Assert.Contains("В: 1180", text);
        }

        [Fact]
        public void Generate_AnwisSectionHeader_ContainsShortModeLabel()
        {
            var items = new List<OrderItem>
            {
                new()
                {
                    Name = "Anwis", Width = 1002, Height = 1170, Quantity = 1, Price = 1800, Total = 1800,
                    AnwisSizeMode = AnwisSizeMode.Брусбокс60
                }
            };
            var selectable = FactoryTextService.BuildSelectableItems(items, new List<AdditionalKpItem>());
            foreach (var si in selectable) si.IsSelected = true;

            var text = FactoryTextService.Generate("", selectable);

            Assert.Contains("Anwis, размер проёма (ББ 60):", text);
            Assert.DoesNotContain("\nAnwis:", text); // plain header without mode
        }

        // ─── Generate: Anwis mode grouping ──────────────────────

        [Fact]
        public void Generate_AnwisItemsWithDifferentModes_SplitIntoSeparateSections()
        {
            var items = new List<OrderItem>
            {
                new()
                {
                    Name = "Anwis", Width = 1002, Height = 1170, Quantity = 1, Price = 1800, Total = 1800,
                    AnwisSizeMode = AnwisSizeMode.Брусбокс60
                },
                new()
                {
                    Name = "Anwis", Width = 800, Height = 1000, Quantity = 1, Price = 1800, Total = 1800,
                    AnwisSizeMode = AnwisSizeMode.Профипласт
                }
            };
            var selectable = FactoryTextService.BuildSelectableItems(items, new List<AdditionalKpItem>());
            foreach (var si in selectable) si.IsSelected = true;

            var text = FactoryTextService.Generate("", selectable);

            // Different modes → separate sections with short mode labels.
            Assert.Contains("Anwis, размер проёма (ББ 60):", text);
            Assert.Contains("Anwis, размер проёма (ПП):", text);
        }

        [Fact]
        public void Generate_AnwisSameMode_GroupsTogether()
        {
            var items = new List<OrderItem>
            {
                new()
                {
                    Name = "Anwis", Width = 1002, Height = 1170, Quantity = 1, Price = 1800, Total = 1800,
                    AnwisSizeMode = AnwisSizeMode.Брусбокс60
                },
                new()
                {
                    Name = "Anwis", Width = 802, Height = 970, Quantity = 1, Price = 1800, Total = 1800,
                    AnwisSizeMode = AnwisSizeMode.Брусбокс60
                }
            };
            var selectable = FactoryTextService.BuildSelectableItems(items, new List<AdditionalKpItem>());
            foreach (var si in selectable) si.IsSelected = true;

            var text = FactoryTextService.Generate("", selectable);

            // Same mode → one section header with short mode label.
            int headerCount = text.Split('\n').Count(
                l => l.Trim() == "Anwis, размер проёма (ББ 60):");
            Assert.Equal(1, headerCount);
        }

        [Fact]
        public void Generate_NonAnwisItem_GroupsByName()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Отлив", Width = 1000, Height = 200, Quantity = 1, Price = 500, Total = 500 },
                new() { Name = "Отлив", Width = 800, Height = 150, Quantity = 2, Price = 500, Total = 1000 }
            };
            var selectable = FactoryTextService.BuildSelectableItems(items, new List<AdditionalKpItem>());
            foreach (var si in selectable) si.IsSelected = true;

            var text = FactoryTextService.Generate("", selectable);

            int headerCount = text.Split('\n').Count(l => l.Trim() == "Отлив:");
            Assert.Equal(1, headerCount);
        }

        // ─── Generate: address and KPs ──────────────────────────

        [Fact]
        public void Generate_IncludesAddress()
        {
            var text = FactoryTextService.Generate("ул. Пушкина, д. 10", new List<FactoryTextService.SelectableItem>());
            Assert.Contains("Адрес: ул. Пушкина, д. 10", text);
        }

        [Fact]
        public void Generate_IncludesAdditionalKp()
        {
            var kps = new List<AdditionalKpItem>
            {
                new() { Number = "2-100", Amount = 5000, IsActive = true }
            };
            var selectable = FactoryTextService.BuildSelectableItems(new List<OrderItem>(), kps);

            var text = FactoryTextService.Generate("", selectable);

            Assert.Contains("К КП № 2-100", text);
        }

        // ─── BuildSelectableItems ───────────────────────────────

        [Fact]
        public void BuildSelectableItems_AnwisDisplayName_IsCompact()
        {
            var items = new List<OrderItem>
            {
                new()
                {
                    Name = "Anwis", Width = 1002, Height = 1170, Quantity = 1, Price = 1800, Total = 1800,
                    AnwisSizeMode = AnwisSizeMode.Брусбокс60
                }
            };
            var selectable = FactoryTextService.BuildSelectableItems(items, new List<AdditionalKpItem>());

            Assert.Single(selectable);
            Assert.Equal("Anwis", selectable[0].DisplayName);
        }

        [Fact]
        public void BuildSelectableItems_AnwisDetail_ShowsOriginalSizes()
        {
            var items = new List<OrderItem>
            {
                new()
                {
                    Name = "Anwis", AnwisSizeMode = AnwisSizeMode.Брусбокс70,
                    Width = 998, Height = 1170, Quantity = 2, Price = 1800, Total = 3600,
                }
            };
            var selectable = FactoryTextService.BuildSelectableItems(items, new List<AdditionalKpItem>());

            Assert.Single(selectable);
            Assert.Equal("Anwis", selectable[0].DisplayName);
            // Detail shows stored (calc-adjusted) sizes: 998×1170
            Assert.Contains("998", selectable[0].Detail);
            Assert.Contains("1170", selectable[0].Detail);
            Assert.Contains("2 шт.", selectable[0].Detail);
        }

        [Fact]
        public void BuildSelectableItems_NonAnwisItem_ShowsName()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Отлив", Width = 1000, Height = 200, Quantity = 1, Price = 500, Total = 500 }
            };
            var selectable = FactoryTextService.BuildSelectableItems(items, new List<AdditionalKpItem>());

            Assert.Single(selectable);
            Assert.Equal("Отлив", selectable[0].DisplayName);
        }

        [Fact]
        public void BuildSelectableItems_ManualPiece_ShowsQuantityOnly()
        {
            var items = new List<OrderItem>
            {
                new() { Name = "Работа", Quantity = 3, Price = 1000, Total = 3000 }
            };
            var selectable = FactoryTextService.BuildSelectableItems(items, new List<AdditionalKpItem>());

            Assert.Single(selectable);
            Assert.Contains("3 шт.", selectable[0].Detail);
        }

        // ─── Auto-selection: production vs non-production ────────

        [Theory]
        [InlineData("Брус")]
        [InlineData("Пояс")]
        [InlineData("Работа")]
        [InlineData("ПСУЛ")]
        [InlineData("Отлив")]
        [InlineData("Доставка")]
        [InlineData("Откос материал")]
        [InlineData("Уплотнение")]
        [InlineData("Короб")]
        public void BuildSelectableItems_NonProduction_IsNotSelected(string productName)
        {
            var items = new List<OrderItem>
            {
                new() { Name = productName, Width = 100, Height = 100, Quantity = 1, Price = 500, Total = 500 }
            };
            var selectable = FactoryTextService.BuildSelectableItems(items, new List<AdditionalKpItem>());

            Assert.Single(selectable);
            Assert.False(selectable[0].IsSelected);
        }

        [Theory]
        [InlineData("Anwis")]
        [InlineData("На навесах")]
        [InlineData("Оконная на метал. крепл.")]
        [InlineData("Козырёк")]
        public void BuildSelectableItems_Production_IsSelected(string productName)
        {
            var items = new List<OrderItem>
            {
                new() { Name = productName, Width = 1000, Height = 1000, Quantity = 1, Price = 1800, Total = 1800 }
            };
            var selectable = FactoryTextService.BuildSelectableItems(items, new List<AdditionalKpItem>());

            Assert.Single(selectable);
            Assert.True(selectable[0].IsSelected);
        }

        // ─── Антикошка display name tests ───────────────────────

        [Fact]
        public void BuildSelectableItems_AnticatItem_HasCorrectDisplayName()
        {
            var items = new List<OrderItem>
            {
                new()
                {
                    Name = "Anwis", Width = 1000, Height = 1000, Quantity = 1, Price = 3800, Total = 3800,
                    IsAnticat = true
                }
            };
            var selectable = FactoryTextService.BuildSelectableItems(items, new List<AdditionalKpItem>());

            Assert.Single(selectable);
            Assert.Equal("Anwis (Антикошка)", selectable[0].DisplayName);
        }

        [Fact]
        public void Generate_AnticatItem_ContainsDisplayNameInHeader()
        {
            var items = new List<OrderItem>
            {
                new()
                {
                    Name = "Anwis", Width = 1002, Height = 1170, Quantity = 1, Price = 3800, Total = 3800,
                    AnwisSizeMode = AnwisSizeMode.Брусбокс60,
                    IsAnticat = true
                }
            };
            var selectable = FactoryTextService.BuildSelectableItems(items, new List<AdditionalKpItem>());
            foreach (var si in selectable) si.IsSelected = true;

            var text = FactoryTextService.Generate("", selectable);

            Assert.Contains("Anwis (Антикошка), размер проёма (ББ 60):", text);
        }

        [Fact]
        public void Generate_AnticatAndRegularAnwis_AreInSeparateSections()
        {
            var items = new List<OrderItem>
            {
                new()
                {
                    Name = "Anwis", Width = 1002, Height = 1170, Quantity = 1, Price = 1800, Total = 1800,
                    AnwisSizeMode = AnwisSizeMode.Брусбокс60,
                    IsAnticat = false
                },
                new()
                {
                    Name = "Anwis", Width = 800, Height = 1000, Quantity = 1, Price = 3800, Total = 3800,
                    AnwisSizeMode = AnwisSizeMode.Брусбокс60,
                    IsAnticat = true
                }
            };
            var selectable = FactoryTextService.BuildSelectableItems(items, new List<AdditionalKpItem>());
            foreach (var si in selectable) si.IsSelected = true;

            var text = FactoryTextService.Generate("", selectable);

            // Different DisplayName → separate sections.
            Assert.Contains("Anwis, размер проёма (ББ 60):", text);
            Assert.Contains("Anwis (Антикошка), размер проёма (ББ 60):", text);
        }
    }
}
