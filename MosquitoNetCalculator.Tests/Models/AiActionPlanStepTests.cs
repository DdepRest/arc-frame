using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Models
{
    public class AiActionPlanStepTests
    {
        [Fact]
        public void AvailableColors_IncludesDefaults_AndCustomColor()
        {
            var step = new AiActionStep
            {
                CommandType = AiCommandType.AddItem,
                Params = new AiCommandParams
                {
                    Type = "Оконная сетка на навесах",
                    Color = "Золотой дуб"
                }
            };

            var colors = step.AvailableColors;
            Assert.Contains("Белый", colors);
            Assert.Contains("Золотой дуб", colors);
        }

        [Fact]
        public void AvailableColors_FallbackColors_WhenUnknownProduct()
        {
            var step = new AiActionStep
            {
                CommandType = AiCommandType.AddItem,
                Params = new AiCommandParams
                {
                    Type = "Неизвестный товар"
                }
            };

            var colors = step.AvailableColors;
            Assert.NotEmpty(colors);
            Assert.Contains("Белый", colors);
            Assert.Contains("Коричневый", colors);
            Assert.Contains("Антрацит", colors);
        }

        [Theory]
        [InlineData("Anwis", true)]
        [InlineData("Оконная сетка на навесах", true)]
        [InlineData("Оконная сетка на метал. крепл.", true)]
        [InlineData("Дверная сетка", false)]
        [InlineData("Отлив", false)]
        public void IsAnwis_DelegatesToAnwisSizeService(string productType, bool expectedIsAnwis)
        {
            var step = new AiActionStep
            {
                CommandType = AiCommandType.AddItem,
                Params = new AiCommandParams
                {
                    Type = productType
                }
            };

            Assert.Equal(expectedIsAnwis, step.IsAnwis);
        }

        [Fact]
        public void Color_DefaultsToWhite_WhenUnspecifiedForItemStep()
        {
            var step = new AiActionStep
            {
                CommandType = AiCommandType.AddItem,
                Params = new AiCommandParams
                {
                    Type = "Оконная сетка на метал. крепл."
                }
            };

            Assert.Equal("Белый", step.Color);
        }

        [Fact]
        public void SwapDimensions_SwapsWidthAndHeight_OnItemStep()
        {
            var step = new AiActionStep
            {
                CommandType = AiCommandType.AddItem,
                Width = 600,
                Height = 1400
            };

            step.SwapDimensions();

            Assert.Equal(1400, step.Width);
            Assert.Equal(600, step.Height);
        }

        [Fact]
        public void SwapAllDimensions_SwapsAllItemStepsInPlan()
        {
            var plan = new AiActionPlan();
            var step1 = new AiActionStep { CommandType = AiCommandType.AddItem, Width = 500, Height = 1200 };
            var step2 = new AiActionStep { CommandType = AiCommandType.AddItem, Width = 800, Height = 1500 };
            var nonItem = new AiActionStep { CommandType = AiCommandType.ClearAll };

            plan.Steps.Add(step1);
            plan.Steps.Add(step2);
            plan.Steps.Add(nonItem);

            Assert.True(plan.HasItemStepsWithDimensions);

            plan.SwapAllDimensions();

            Assert.Equal(1200, step1.Width);
            Assert.Equal(500, step1.Height);
            Assert.Equal(1500, step2.Width);
            Assert.Equal(800, step2.Height);
        }

        [Fact]
        public void HasItemStepsWithDimensions_False_WhenNoStepsOrNoDimensions()
        {
            var plan = new AiActionPlan();
            Assert.False(plan.HasItemStepsWithDimensions);

            plan.Steps.Add(new AiActionStep { CommandType = AiCommandType.ClearAll });
            Assert.False(plan.HasItemStepsWithDimensions);

            var itemStep = new AiActionStep { CommandType = AiCommandType.AddItem, Width = 0, Height = 0 };
            plan.Steps.Add(itemStep);
            Assert.False(plan.HasItemStepsWithDimensions);

            itemStep.Width = 500;
            Assert.True(plan.HasItemStepsWithDimensions);
        }
    }

}
