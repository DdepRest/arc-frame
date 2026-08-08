using System.Collections.Generic;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class AiExplanationContextTests
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
        public void Build_RegularItem_CarriesAppNumbers()
        {
            var item = MakeItem("Отлив", "Коричневый", 200, 1500, 2, 2150);

            var ctx = AiExplanationContextBuilder.Build(item, 1);

            Assert.Equal("Отлив", ctx.Name);
            Assert.Equal("Коричневый", ctx.Color);
            Assert.Equal(200, ctx.WidthInput);
            Assert.Equal(1500, ctx.HeightInput);
            Assert.Equal(2150, ctx.Price);
            Assert.Equal(2, ctx.Quantity);
            Assert.Null(ctx.SlopeGrandTotal);
        }

        [Fact]
        public void Build_Anwis_DistinguishesSizeLayers()
        {
            var item = new OrderItem { Name = "Anwis", Color = "Белый", Width = 702, Height = 1370, Price = 1800 };
            item.SetAnwisModeQuiet(AnwisSizeMode.Брусбокс60);

            var ctx = AiExplanationContextBuilder.Build(item, 2);

            Assert.Equal(700, ctx.WidthInput, 2);
            Assert.Equal(702, ctx.WidthCalc, 2);
            Assert.Equal(682, ctx.WidthFactory, 2);
            Assert.Equal("ББ60", ctx.AnwisModeLabel);
            var text = ctx.ToText();
            Assert.Contains("Введённые размеры", text);
            Assert.Contains("Расчётные размеры", text);
            Assert.Contains("Заводские размеры", text);
            Assert.Contains("Итог в заказе", text);
        }

        [Fact]
        public void Build_Slope_CarriesMaterialsAndLabor()
        {
            var item = new OrderItem { Name = "Откос", Color = "", Width = 0, Height = 0, Quantity = 1, Price = 0 };
            item.SlopeData = new SlopeCalculation
            {
                WidthMm = 1500,
                HeightMm = 700,
                DepthM = 0.3,
                WindowCount = 1
            };

            var ctx = AiExplanationContextBuilder.Build(item, 1);

            Assert.True(ctx.SlopeGrandTotal.HasValue);
            Assert.True(ctx.SlopeTotalMaterials.HasValue);
            Assert.True(ctx.SlopeTotalLabor.HasValue);
            Assert.Equal(1500, ctx.SlopeWidthMm);
            Assert.Equal(700, ctx.SlopeHeightMm);
            var text = ctx.ToText();
            Assert.Contains("Окно: 1500×700 мм", text);
            Assert.Contains("Материалы:", text);
            Assert.Contains("Итого за откосы:", text);
        }

        [Fact]
        public void BuildText_LastItem()
        {
            var items = new List<OrderItem>
            {
                MakeItem("Anwis", "Белый", 700, 1400, 1, 1800),
                MakeItem("Отлив", "Белый", 200, 1500, 1, 2150)
            };

            var text = AiExplanationContextBuilder.BuildTextForLast(items);

            Assert.Contains("позиция 2: Отлив", text);
        }

        [Fact]
        public void BuildText_OutOfRange_ReportsAvailable()
        {
            var items = new List<OrderItem> { MakeItem("Anwis", "Белый", 700, 1400, 1, 1800) };

            var text = AiExplanationContextBuilder.BuildText(items, 5);

            Assert.Contains("Позиции 5 нет", text);
            Assert.Contains("1", text);
        }

        [Fact]
        public void BuildText_EmptyOrder_Message()
        {
            Assert.Contains("Заказ пуст", AiExplanationContextBuilder.BuildTextForAll(new List<OrderItem>()));
        }
    }
}
