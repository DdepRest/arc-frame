using System;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class AiCommandParserPlanModeTests
    {
        [Fact]
        public void PlanMode_ParsesMultipleSteps()
        {
            var content = """
                Конечно, вот план:
                ```json
                {
                  "mode": "plan",
                  "reply": "Я подготовил план добавления двух позиций.",
                  "requires_confirmation": true,
                  "steps": [
                    { "action": "add_item", "params": { "type": "Anwis", "color": "Белый", "width": 700, "height": 1400, "anwis_mode": "ББ60", "quantity": 1 } },
                    { "action": "add_item", "params": { "type": "Отлив", "color": "Коричневый", "width": 200, "height": 1500, "quantity": 2 } }
                  ]
                }
                ```
                """;

            var (response, isValid) = AiCommandParser.TryParse(content, "добавь две позиции");

            Assert.True(isValid);
            Assert.NotNull(response.Plan);
            Assert.Equal(2, response.Plan!.Steps.Count);
            Assert.True(response.Plan.RequiresConfirmation);
            Assert.Equal(AiPlanMode.Plan, response.Plan.Mode);
            Assert.Equal(AiCommandType.AddItem, response.Plan.Steps[0].CommandType);
            Assert.Equal("Anwis", response.Plan.Steps[0].Params.Type);
            Assert.Equal(700, response.Plan.Steps[0].Params.Width);
            Assert.Equal("Отлив", response.Plan.Steps[1].Params.Type);
        }

        [Fact]
        public void PlanMode_AnswerMode_ReturnsPlainReply()
        {
            var content = """{"mode":"answer","reply":"В заказе 2 позиции на сумму 3900 ₽."}""";

            var (response, isValid) = AiCommandParser.TryParse(content, "сколько позиций?");

            Assert.True(isValid);
            Assert.Null(response.Plan);
            Assert.Null(response.Action);
            Assert.Contains("3900", response.Reply);
        }

        [Fact]
        public void PlanMode_Clarification_SetsResponseMode()
        {
            var content = """{"mode":"clarification","reply":"Какой режим Anwis использовать? ББ60, ББ70, ПП, Проём или Габарит?"}""";

            var (response, isValid) = AiCommandParser.TryParse(content, "Добавь сетку Anwis 739×1116");

            Assert.True(isValid);
            Assert.Null(response.Plan);
            Assert.Null(response.Action);
            Assert.Equal(AiPlanMode.Clarification, response.Mode);
            Assert.Contains("Какой режим", response.Reply);
        }

        [Fact]
        public void PlanMode_AddItemWithoutMode_SetsClarificationMode()
        {
            // The model produced an add_item for Anwis but omitted the mode —
            // the parser answers with the validation override and must mark it
            // as clarification so the UI attaches the parameter form.
            var content = """{"mode":"plan","steps":[{"action":"add_item","params":{"type":"Anwis","color":"Белый","width":739,"height":1116,"quantity":4}}]}""";

            var (response, isValid) = AiCommandParser.TryParse(content, "Добавь сетку Anwis белый 739×1116 4 шт");

            Assert.True(isValid);
            Assert.Null(response.Plan);
            Assert.Null(response.Action);
            Assert.Equal(AiPlanMode.Clarification, response.Mode);
            Assert.Contains("укажите режим", response.Reply, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PlanMode_ExplicitConfirmationFalse_WinsOverMutatingStep()
        {
            var content = """
                {"mode":"plan","requires_confirmation":false,"steps":[
                  {"action":"clear_all"}
                ]}
                """;

            var (response, isValid) = AiCommandParser.TryParse(content, "очисти");

            Assert.True(isValid);
            // The local policy always confirms destructive actions even if the
            // model claims otherwise (CONTROL: prompt is not a replacement for
            // local validation).
            Assert.True(response.Plan!.RequiresConfirmation);
        }

        [Fact]
        public void PlanMode_CalcSlopeMissingParams_ReturnsReplyOverride()
        {
            var content = """
                {"mode":"plan","steps":[
                  {"action":"calc_slope","params":{"width":1500,"height":700}}
                ]}
                """;

            var (response, isValid) = AiCommandParser.TryParse(content, "откос 1500x700");

            Assert.True(isValid);
            Assert.Null(response.Plan);
            Assert.Contains("глубину", response.Reply);
        }

        [Fact]
        public void PlanMode_WithInvalidStep_IsInvalid()
        {
            var content = """
                {"mode":"plan","steps":[
                  {"action":"unknown_action","params":{}}
                ]}
                """;

            var (response, isValid) = AiCommandParser.TryParse(content, "сделай что-нибудь");

            Assert.False(isValid);
            Assert.Null(response.Plan);
        }

        [Fact]
        public void LegacySingleAction_StillProducesAction()
        {
            var content = """{"action":"add_item","params":{"type":"Anwis","width":500,"height":500,"anwis_mode":"ББ60"}}""";

            var (response, isValid) = AiCommandParser.TryParse(content, "сетка 500x500");

            Assert.True(isValid);
            Assert.NotNull(response.Action);
            Assert.Equal(AiCommandType.AddItem, response.Action!.Type);
            Assert.Null(response.Plan);
        }

        [Fact]
        public void LegacySingleAction_WithModeAndSteps_GoesThroughPlan()
        {
            var content = """
                {"mode":"plan","steps":[{"action":"delete_last"}]}
                """;

            var (response, isValid) = AiCommandParser.TryParse(content, "удали последний");

            Assert.True(isValid);
            Assert.NotNull(response.Plan);
            Assert.Equal(AiCommandType.DeleteLast, response.Plan!.Steps[0].CommandType);
            Assert.True(response.Plan.RequiresConfirmation);
        }
    }
}
