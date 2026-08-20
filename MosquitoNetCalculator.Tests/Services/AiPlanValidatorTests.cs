using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class AiPlanValidatorTests
    {
        private static AiCommand AddItem(string type, int w, int h, string color = "Белый", double qty = 1)
            => new()
            {
                Type = AiCommandType.AddItem,
                Params = new AiCommandParams { Type = type, Color = color, Width = w, Height = h, Quantity = qty }
            };

        [Fact]
        public void ValidAddItem_Passes_AndRequiresConfirmation()
        {
            var plan = AiPlanBuilder.FromCommand(AddItem("Anwis", 700, 1400));

            var r = AiPlanValidator.Validate(plan);

            Assert.True(r.IsValid);
            Assert.True(r.RequiresConfirmation);
            Assert.Empty(r.StepResults[0].Messages);
        }

        [Fact]
        public void AddItem_UnknownProduct_Fails()
        {
            var plan = AiPlanBuilder.FromCommand(AddItem("Богомол", 700, 1400));

            var r = AiPlanValidator.Validate(plan);

            Assert.False(r.IsValid);
            Assert.Contains(r.StepResults[0].Messages, m => m.Contains("отсутствует в каталоге"));
        }

        [Fact]
        public void AddItem_ZeroSize_Fails()
        {
            var plan = AiPlanBuilder.FromCommand(AddItem("Anwis", 0, 1400));

            Assert.False(AiPlanValidator.Validate(plan).IsValid);
        }

        [Fact]
        public void AddItem_ManualPiece_DoesNotRequireDimensions()
        {
            var plan = AiPlanBuilder.FromCommand(new AiCommand
            {
                Type = AiCommandType.AddItem,
                Params = new AiCommandParams { Type = "Работа", Width = 0, Height = 0, Quantity = 1, Price = 5000 }
            });

            Assert.True(AiPlanValidator.Validate(plan).IsValid);
        }

        [Fact]
        public void AddItem_ColorNotInPalette_Fails()
        {
            var plan = AiPlanBuilder.FromCommand(AddItem("Anwis", 700, 1400, color: "Синий"));

            var r = AiPlanValidator.Validate(plan);

            Assert.False(r.IsValid);
            Assert.Contains(r.StepResults[0].Messages, m => m.Contains("не предусмотрен"));
        }

        [Fact]
        public void AddItem_EmptyColor_IsDefault_NotBlocking()
        {
            var plan = AiPlanBuilder.FromCommand(AddItem("Anwis", 700, 1400, color: ""));

            Assert.True(AiPlanValidator.Validate(plan).IsValid);
        }

        [Fact]
        public void CalcSlope_RequiresPositiveDimensions()
        {
            var plan = AiPlanBuilder.FromCommand(new AiCommand
            {
                Type = AiCommandType.CalcSlope,
                Params = new AiCommandParams { Width = 1500, Height = 700, Depth = 0, Quantity = 1 }
            });

            var r = AiPlanValidator.Validate(plan);

            Assert.False(r.IsValid);
            Assert.False(r.RequiresConfirmation);
        }

        [Fact]
        public void CalcSlope_Valid_DoesNotRequireConfirmation()
        {
            var plan = AiPlanBuilder.FromCommand(new AiCommand
            {
                Type = AiCommandType.CalcSlope,
                Params = new AiCommandParams { Width = 1500, Height = 700, Depth = 300, Quantity = 1 }
            });

            var r = AiPlanValidator.Validate(plan);

            Assert.True(r.IsValid);
            Assert.False(r.RequiresConfirmation); // opens the overlay, user reviews there
        }

        [Fact]
        public void UpdateItems_WithoutChanges_Fails()
        {
            var plan = AiPlanBuilder.FromCommand(new AiCommand { Type = AiCommandType.UpdateItems });

            Assert.False(AiPlanValidator.Validate(plan).IsValid);
        }

        [Fact]
        public void UpdateItems_KnownCategory_Passes()
        {
            var plan = AiPlanBuilder.FromCommand(new AiCommand
            {
                Type = AiCommandType.UpdateItems,
                Params = new AiCommandParams { TargetProduct = "сетки", UpdateColor = "Коричневый" }
            });

            Assert.True(AiPlanValidator.Validate(plan).IsValid);
        }

        [Fact]
        public void UpdateItems_UnknownTarget_Fails()
        {
            var plan = AiPlanBuilder.FromCommand(new AiCommand
            {
                Type = AiCommandType.UpdateItems,
                Params = new AiCommandParams { TargetProduct = "Богомол", UpdatePrice = 900 }
            });

            Assert.False(AiPlanValidator.Validate(plan).IsValid);
        }

        [Fact]
        public void DeleteItems_UnknownTarget_Fails()
        {
            var plan = AiPlanBuilder.FromCommand(new AiCommand
            {
                Type = AiCommandType.DeleteItems,
                Params = new AiCommandParams { TargetProduct = "Богомол" }
            });

            Assert.False(AiPlanValidator.Validate(plan).IsValid);
        }

        [Fact]
        public void ClearAll_And_DeleteLast_RequireConfirmation()
        {
            Assert.True(AiPlanValidator.Validate(AiPlanBuilder.FromCommand(new AiCommand { Type = AiCommandType.ClearAll })).RequiresConfirmation);
            Assert.True(AiPlanValidator.Validate(AiPlanBuilder.FromCommand(new AiCommand { Type = AiCommandType.DeleteLast })).RequiresConfirmation);
        }

        [Fact]
        public void ListProducts_IsReadOnly_NoConfirmation()
        {
            var plan = AiPlanBuilder.FromCommand(new AiCommand { Type = AiCommandType.ListProducts });

            var r = AiPlanValidator.Validate(plan);

            Assert.True(r.IsValid);
            Assert.False(r.RequiresConfirmation);
            Assert.True(plan.IsReadOnly);
        }

        /// <summary>
        /// Stage-1 hardening (centralised safety policy): the validator
        /// must propagate the NeedsClarification flag the safety policy
        /// produces, both onto <see cref="AiPlanValidationResult"/> and
        /// onto the plan itself, so all four command-building paths share
        /// one answer.
        /// </summary>
        [Fact]
        public void Validate_AnwisWithoutMode_PropagatesNeedsClarification()
        {
            var plan = AiPlanBuilder.FromCommand(
                new AiCommand
                {
                    Type = AiCommandType.AddItem,
                    Params = new AiCommandParams { Type = "Anwis", Color = "Белый", Width = 700, Height = 1400 }
                },
                sourceUserText: "Сделай сетку 700x1400 бел");

            var r = AiPlanValidator.Validate(plan);

            Assert.True(r.NeedsClarification);
            Assert.Equal(AiPlanSafetyPolicy.MissingField.AnwisMode, r.MissingField);
            Assert.True(plan.NeedsClarification);
        }

        [Fact]
        public void Validate_FullySpecifiedPlan_DoesNotFlagClarification()
        {
            var plan = AiPlanBuilder.FromCommand(
                new AiCommand
                {
                    Type = AiCommandType.AddItem,
                    Params = new AiCommandParams
                    {
                        Type = "Anwis", Color = "Белый", Width = 700, Height = 1400,
                        AnwisMode = AnwisSizeMode.Брусбокс60, InstallationMode = 0
                    }
                },
                sourceUserText: "Сделай сетку Anwis 700x1400 бел ПП с монтажом");

            var r = AiPlanValidator.Validate(plan);

            Assert.False(r.NeedsClarification);
            Assert.Equal(AiPlanSafetyPolicy.MissingField.None, r.MissingField);
            Assert.False(plan.NeedsClarification);
        }
    }
}
