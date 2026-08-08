using System.Collections.Generic;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.ViewModels;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class AiOrderContextBuilderTests
    {
        private static OrderItem MakeItem(string name, string color, double w, double h, double qty, double price)
            => new()
            {
                Name = name,
                Color = color,
                Width = w,
                Height = h,
                Quantity = qty,
                Price = price
            };

        [Fact]
        public void Build_PopulatesTotalsAndItems()
        {
            var items = new List<OrderItem>
            {
                MakeItem("Отлив", "Коричневый", 200, 1500, 2, 2150),
                MakeItem("Anwis", "Белый", 700, 1400, 1, 1800)
            };
            var totals = new TotalInfo
            {
                Count = 2,
                Total = 9000,
                TotalArea = 1.5,
                TotalLinear = 6.8,
                TotalPieces = 3
            };

            var ctx = AiOrderContextBuilder.Build(items, totals, additionalKpTotal: 500);

            Assert.Equal(2, ctx.Count);
            Assert.Equal(9000, ctx.Total);
            Assert.Equal(8500, ctx.ItemsTotal);
            Assert.Equal(500, ctx.AdditionalKpTotal);
            Assert.Equal(2, ctx.Items.Count);
            Assert.Equal("Отлив", ctx.Items[0].Name);
            Assert.Equal("фасадные", ctx.Items[0].Category);
            Assert.Equal("сетки", ctx.Items[1].Category);
        }

        [Fact]
        public void Build_AnwisSizeLayers_AreSeparate()
        {
            var item = new OrderItem { Name = "Anwis", Color = "Белый", Width = 702, Height = 1370, Price = 1800 };
            item.SetAnwisModeQuiet(AnwisSizeMode.Брусбокс60);
            // Stored 702×1370 under ББ60 → input 700×1400, factory 682×1350.
            var sizes = item.Размеры;
            Assert.Equal(700, sizes.ШиринаОтображение, 2);

            var ctx = AiOrderContextBuilder.Build(
                new[] { item }, new TotalInfo { Count = 1, Total = 1800 }, 0);

            var info = ctx.Items[0];
            Assert.Equal(700, info.WidthInput, 2);
            Assert.Equal(1400, info.HeightInput, 2);
            Assert.Equal(702, info.WidthCalc, 2);
            Assert.Equal(682, info.WidthFactory, 2);
            Assert.Equal("ББ60", info.AnwisModeLabel);
        }

        [Fact]
        public void Build_GroupingByCategory_TotalsOnlyActive()
        {
            var active = MakeItem("Anwis", "Белый", 700, 1400, 1, 1800);
            var inactive = MakeItem("Отлив", "Белый", 200, 1500, 1, 2150);
            inactive.IsActive = false;

            var ctx = AiOrderContextBuilder.Build(
                new[] { active, inactive }, new TotalInfo { Count = 1, Total = 1800 }, 0);

            Assert.Single(ctx.GroupsByCategory);
            Assert.Equal("сетки", ctx.GroupsByCategory[0].Key);
        }

        [Fact]
        public void ToPromptText_ContainsItemsAndTotals()
        {
            var items = new List<OrderItem> { MakeItem("Anwis", "Белый", 700, 1400, 1, 1800) };
            var ctx = AiOrderContextBuilder.Build(
                items, new TotalInfo { Count = 1, Total = 1800, TotalArea = 0.98 }, 0);

            var text = ctx.ToPromptText();

            Assert.Contains("## ТЕКУЩИЙ ЗАКАЗ", text);
            Assert.Contains("Anwis Белый", text);
            Assert.Contains("1. Anwis", text);
            Assert.Contains("итог", text);
        }

        [Fact]
        public void FormatBrief_EmptyOrder()
        {
            var ctx = new AiOrderContext { Count = 0, Total = 0 };

            Assert.Contains("Позиций: 0", ctx.FormatBrief());
        }
    }
}
