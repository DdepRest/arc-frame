using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// The «don't invent» safety policy is the single source of truth
    /// for whether an AI-produced plan needs a clarification card
    /// before reaching the user. Every code path that builds a plan
    /// (LLM plan-mode, legacy single-action, clarification form submit,
    /// slash command router) must agree with this test class.
    /// </summary>
    public class AiPlanSafetyPolicyTests
    {
        private static AiCommand AddAnwis(int w, int h, double qty = 1, int? install = null)
            => new()
            {
                Type = AiCommandType.AddItem,
                Params = new AiCommandParams
                {
                    Type = "Anwis", Color = "Белый", Width = w, Height = h,
                    Quantity = qty, AnwisMode = AnwisSizeMode.Брусбокс60,
                    InstallationMode = install ?? -1
                }
            };

        private static AiCommand AddOtiv (int w, int h, string color)
            => new()
            {
                Type = AiCommandType.AddItem,
                Params = new AiCommandParams
                {
                    Type = "Отлив", Color = color, Width = w, Height = h,
                    Quantity = 1, InstallationMode = -1
                }
            };

        private static AiCommand AddKorob (int w, int h)
            => new()
            {
                Type = AiCommandType.AddItem,
                Params = new AiCommandParams
                {
                    Type = "Короб", Color = "Белый", Width = w, Height = h,
                    Quantity = 1
                }
            };

        private static AiCommand UpdateItems (string target)
            => new()
            {
                Type = AiCommandType.UpdateItems,
                Params = new AiCommandParams { TargetProduct = target, UpdateAnwisMode = AnwisSizeMode.Брусбокс60 }
            };

        // ── Anwis without mode ────────────────────────────────────

        [Fact]
        public void NeedsClarification_AnwisWithoutUserNamedMode_ReturnsTrue()
        {
            var commands = new[] { AddAnwis(700, 1400) };
            Assert.Equal(AiPlanSafetyPolicy.MissingField.AnwisMode,
                AiPlanSafetyPolicy.Classify(commands, "Сделай сетку 700x1400 бел"));
            Assert.True(AiPlanSafetyPolicy.NeedsClarification(commands, "Сделай сетку 700x1400 бел"));
        }

        [Fact]
        public void NeedsClarification_AnwisWithUserNamedModeAndInstallation_ReturnsFalse()
        {
            var commands = new[] { AddAnwis(700, 1400, install: 0) };
            var missing = AiPlanSafetyPolicy.Classify(commands, "Сделай сетку Anwis 700x1400 бел ПП с монтажом");
            Assert.Equal(AiPlanSafetyPolicy.MissingField.None, missing);
            Assert.False(AiPlanSafetyPolicy.NeedsClarification(commands, "Сделай сетку Anwis 700x1400 бел ПП с монтажом"));
        }

        // ── Dimensions missing ───────────────────────────────────

        [Fact]
        public void NeedsClarification_OtivWithoutDimensions_ReturnsTrue()
        {
            var commands = new[] { AddOtiv(0, 0, "Белый") };
            Assert.Equal(AiPlanSafetyPolicy.MissingField.Dimensions,
                AiPlanSafetyPolicy.Classify(commands, "Отлив белый с монтажом"));
        }

        [Fact]
        public void NeedsClarification_ManualPieceWithoutDimensions_ReturnsFalse()
        {
            // «Доставка 500» is a manual piece — no dimensions required.
            var commands = new[] { new AiCommand
            {
                Type = AiCommandType.AddItem,
                Params = new AiCommandParams { Type = "Доставка", Quantity = 1, Price = 500 }
            }};
            Assert.Equal(AiPlanSafetyPolicy.MissingField.None,
                AiPlanSafetyPolicy.Classify(commands, "Доставка 500"));
        }

        // ── Installation ─────────────────────────────────────────

        [Fact]
        public void NeedsClarification_OtivWithoutInstallation_ReturnsTrue()
        {
            var commands = new[] { AddOtiv(170, 900, "Белый") };
            Assert.Equal(AiPlanSafetyPolicy.MissingField.InstallationMode,
                AiPlanSafetyPolicy.Classify(commands, "отлив бел 170 900"));
        }

        [Fact]
        public void NeedsClarification_OtivWithInstallationSpecified_ReturnsFalse()
        {
            var commands = new[] { AddOtiv(170, 900, "Белый") };
            Assert.Equal(AiPlanSafetyPolicy.MissingField.None,
                AiPlanSafetyPolicy.Classify(commands, "отлив бел 170 900 с монтажом"));
        }

        [Fact]
        public void NeedsClarification_KorobNoInstallationToggle_ReturnsFalse()
        {
            // Короб doesn't carry the installation toggle (product catalog says
            // installation is NOT applicable) — silently defaulting is allowed.
            var commands = new[] { AddKorob(200, 1500) };
            Assert.Equal(AiPlanSafetyPolicy.MissingField.None,
                AiPlanSafetyPolicy.Classify(commands, "короб белый 200x1500"));
        }

        // ── Update without target ────────────────────────────────

        [Fact]
        public void NeedsClarification_UpdateWithoutTarget_ReturnsTrue()
        {
            var commands = new[] { new AiCommand
            {
                Type = AiCommandType.UpdateItems,
                Params = new AiCommandParams { UpdateInstallationMode = 1 }
            }};
            Assert.Equal(AiPlanSafetyPolicy.MissingField.UpdateTarget,
                AiPlanSafetyPolicy.Classify(commands, "без монтажа"));
        }

        [Fact]
        public void NeedsClarification_UpdateWithNamedCategory_ReturnsFalse()
        {
            var commands = new[] { UpdateItems("сетки") };
            Assert.Equal(AiPlanSafetyPolicy.MissingField.None,
                AiPlanSafetyPolicy.Classify(commands, "Все сетки без монтажа"));
        }

        // ── Reason text covers every field ───────────────────────

        [Theory]
        [InlineData(AiPlanSafetyPolicy.MissingField.None, "")]
        [InlineData(AiPlanSafetyPolicy.MissingField.AnwisMode, "режим Anwis")]
        [InlineData(AiPlanSafetyPolicy.MissingField.Dimensions, "Не хватает параметров")]
        [InlineData(AiPlanSafetyPolicy.MissingField.InstallationMode, "Не указан монтаж")]
        [InlineData(AiPlanSafetyPolicy.MissingField.UpdateTarget, "к каким позициям")]
        public void MissingReasonText_EveryField_ReturnsNonEmptyUserMessage(
            AiPlanSafetyPolicy.MissingField field, string expectedContains)
        {
            var text = AiPlanSafetyPolicy.MissingReasonText(field);
            if (field == AiPlanSafetyPolicy.MissingField.None) Assert.Equal("", text);
            else Assert.Contains(expectedContains, text);
        }

        // ── Helpers ──────────────────────────────────────────────

        [Fact]
        public void Classify_NullList_IsNone()
        {
            Assert.Equal(AiPlanSafetyPolicy.MissingField.None,
                AiPlanSafetyPolicy.Classify(null, "any user text"));
        }

        [Fact]
        public void Classify_EmptyList_IsNone()
        {
            Assert.Equal(AiPlanSafetyPolicy.MissingField.None,
                AiPlanSafetyPolicy.Classify(System.Array.Empty<AiCommand>(), "any"));
        }

        [Fact]
        public void Classify_NotAddItemOrUpdate_IsNone()
        {
            var commands = new[] { new AiCommand { Type = AiCommandType.ClearAll } };
            Assert.Equal(AiPlanSafetyPolicy.MissingField.None,
                AiPlanSafetyPolicy.Classify(commands, "очисти"));
        }

        // Priority regression: Anwis-mode > dimensions > installation —
        // make sure multi-rule plans always report the highest-priority
        // missing field, not the first one encountered in iteration order.
        [Fact]
        public void Classify_PriorityOrder_AnwisFirstThenDimensionsThenInstallation()
        {
            // Anwis with no mode + no dimensions: AnwisMode wins.
            var cmds = new[] { AddAnwis(0, 0) };
            Assert.Equal(AiPlanSafetyPolicy.MissingField.AnwisMode,
                AiPlanSafetyPolicy.Classify(cmds, "сетка"));
        }
    }
}
