using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class AiCommandParserTests
    {
        [Fact]
        public void Parse_PlainText_ReturnsReplyWithoutAction()
        {
            var response = AiCommandParser.Parse("Привет, как дела?", "Привет, как дела?");

            Assert.Equal("Привет, как дела?", response.Reply);
            Assert.Null(response.Action);
        }

        [Fact]
        public void Parse_EmptyContent_ReturnsEmptyReplyWithoutAction()
        {
            var response = AiCommandParser.Parse("   ", "...");

            Assert.Equal("Пустой ответ от AI.", response.Reply);
            Assert.Null(response.Action);
        }

        [Fact]
        public void Parse_AddItemJson_ReturnsAddItemCommand()
        {
            const string json = "{\"action\":\"add_item\",\"params\":{\"type\":\"Anwis\",\"color\":\"Белый\",\"width\":500,\"height\":600,\"quantity\":2,\"price\":1800,\"anwis_mode\":\"ББ60\"}}";

            var response = AiCommandParser.Parse(json, "...");

            Assert.NotNull(response.Action);
            Assert.Equal(AiCommandType.AddItem, response.Action!.Type);
            Assert.Equal("Anwis", response.Action.Params.Type);
            Assert.Equal("Белый", response.Action.Params.Color);
            Assert.Equal(500, response.Action.Params.Width);
            Assert.Equal(600, response.Action.Params.Height);
            Assert.Equal(2, response.Action.Params.Quantity);
            Assert.Equal(1800, response.Action.Params.Price);
            Assert.Equal(AnwisSizeMode.Брусбокс60, response.Action.Params.AnwisMode);
        }

        [Fact]
        public void Parse_AddItemMarkdownFence_ExtractsJson()
        {
            const string content = "Конечно!\n\n```json\n{\"action\":\"add_item\",\"params\":{\"type\":\"Отлив\",\"color\":\"Коричневый\",\"width\":200,\"height\":1500,\"quantity\":1,\"price\":2150}}\n```";

            var response = AiCommandParser.Parse(content, "...");

            Assert.NotNull(response.Action);
            Assert.Equal(AiCommandType.AddItem, response.Action!.Type);
            Assert.Equal("Отлив", response.Action.Params.Type);
            Assert.Equal(2150, response.Action.Params.Price);
        }

        [Fact]
        public void Parse_DeleteLast_ReturnsDeleteLastCommand()
        {
            var response = AiCommandParser.Parse("{\"action\":\"delete_last\"}", "...");

            Assert.NotNull(response.Action);
            Assert.Equal(AiCommandType.DeleteLast, response.Action!.Type);
        }

        [Fact]
        public void Parse_ClearAll_ReturnsClearAllCommand()
        {
            var response = AiCommandParser.Parse("{\"action\":\"clear_all\"}", "...");

            Assert.NotNull(response.Action);
            Assert.Equal(AiCommandType.ClearAll, response.Action!.Type);
        }

        [Fact]
        public void Parse_ListProducts_ReturnsListProductsCommand()
        {
            var response = AiCommandParser.Parse("{\"action\":\"list_products\"}", "...");

            Assert.NotNull(response.Action);
            Assert.Equal(AiCommandType.ListProducts, response.Action!.Type);
        }

        [Fact]
        public void Parse_UnknownAction_ReturnsReplyWithoutAction()
        {
            var response = AiCommandParser.Parse("{\"action\":\"unknown_action\"}", "...");

            Assert.Null(response.Action);
            Assert.Contains("unknown_action", response.Reply);
        }

        [Fact]
        public void Parse_InvalidJson_ReturnsReplyWithoutAction()
        {
            var response = AiCommandParser.Parse("{not valid json", "...");

            Assert.Null(response.Action);
            Assert.Equal("{not valid json", response.Reply);
        }

        [Fact]
        public void Parse_AddItemWithoutPrice_FillsDefaultPrice()
        {
            // Anwis requires anwis_mode (parser guard) — provide it so the test
            // exercises price auto-fill, not the mode-asking path.
            const string json = "{\"action\":\"add_item\",\"params\":{\"type\":\"Anwis\",\"color\":\"Коричневый\",\"width\":500,\"height\":500,\"anwis_mode\":\"ББ60\"}}";

            var response = AiCommandParser.Parse(json, "...");

            Assert.Equal(1900, response.Action!.Params.Price);
        }

        [Fact]
        public void Parse_AddItem_AnwisWithoutMode_ReturnsClarifyingReply()
        {
            // User report: «Добавь анвис корич 500 1000 в конструцию» was added
            // with a silently-defaulted ББ60. The parser must ask which Anwis
            // mode instead of defaulting, even if the model ignores the prompt.
            const string json = "{\"action\":\"add_item\",\"params\":{\"type\":\"Anwis\",\"color\":\"Коричневый\",\"width\":500,\"height\":1000}}";

            var response = AiCommandParser.Parse(json, "Добавь анвис корич 500 1000 в конструцию");

            Assert.Null(response.Action);
            Assert.Contains("Для Anwis укажите режим", response.Reply);
        }

        [Fact]
        public void Parse_AddItem_NonAnwisWithoutMode_StillParses()
        {
            // The mode-asking guard applies ONLY to Anwis — other products are
            // unaffected.
            const string json = "{\"action\":\"add_item\",\"params\":{\"type\":\"Отлив\",\"color\":\"Коричневый\",\"width\":200,\"height\":1500,\"quantity\":1,\"price\":2150}}";

            var response = AiCommandParser.Parse(json, "отлив 200 1500");

            Assert.NotNull(response.Action);
            Assert.Equal(AiCommandType.AddItem, response.Action!.Type);
            Assert.Equal("Отлив", response.Action.Params.Type);
        }

        [Fact]
        public void Parse_AddItemWithAnwisModePp_SetsModeCorrectly()
        {
            const string json = "{\"action\":\"add_item\",\"params\":{\"type\":\"Anwis\",\"color\":\"Белый\",\"anwis_mode\":\"ПП\"}}";

            var response = AiCommandParser.Parse(json, "...");

            Assert.Equal(AnwisSizeMode.Профипласт, response.Action!.Params.AnwisMode);
        }

        // --- TryParse validity tests ---

        [Fact]
        public void TryParse_EmptyContent_IsInvalid()
        {
            var (response, isValid) = AiCommandParser.TryParse("   ", "...");

            Assert.False(isValid);
            Assert.Equal("Пустой ответ от AI.", response.Reply);
            Assert.Null(response.Action);
        }

        [Fact]
        public void TryParse_PlainText_IsValid()
        {
            var (response, isValid) = AiCommandParser.TryParse("Привет!", "...");

            Assert.True(isValid);
            Assert.Equal("Привет!", response.Reply);
            Assert.Null(response.Action);
        }

        [Fact]
        public void TryParse_ValidAction_IsValid()
        {
            var (response, isValid) = AiCommandParser.TryParse("{\"action\":\"clear_all\"}", "...");

            Assert.True(isValid);
            Assert.NotNull(response.Action);
            Assert.Equal(AiCommandType.ClearAll, response.Action!.Type);
        }

        [Fact]
        public void TryParse_InvalidJsonBlock_IsInvalid()
        {
            var (response, isValid) = AiCommandParser.TryParse("```json\n{not valid}\n```", "...");

            Assert.False(isValid);
            Assert.Null(response.Action);
            Assert.Contains("{not valid}", response.Reply);
        }

        [Fact]
        public void TryParse_UnknownAction_IsInvalid()
        {
            var (response, isValid) = AiCommandParser.TryParse("{\"action\":\"fly_to_moon\"}", "...");

            Assert.False(isValid);
            Assert.Null(response.Action);
        }

        [Fact]
        public void Parse_CalcSlope_ReturnsCalcSlopeCommand()
        {
            const string json = "{\"action\":\"calc_slope\",\"params\":{\"width\":1500,\"height\":700,\"depth\":300,\"quantity\":2}}";

            var response = AiCommandParser.Parse(json, "Сделай просчёт откосы из сэндвича, в 1500 ш 700 г 300");

            Assert.NotNull(response.Action);
            Assert.Equal(AiCommandType.CalcSlope, response.Action!.Type);
            Assert.Equal(1500, response.Action.Params.Width);
            Assert.Equal(700, response.Action.Params.Height);
            Assert.Equal(300, response.Action.Params.Depth);
            Assert.Equal(2, response.Action.Params.Quantity);
        }

        [Fact]
        public void Parse_CalcSlope_DefaultsQuantityToOne()
        {
            const string json = "{\"action\":\"calc_slope\",\"params\":{\"width\":1200,\"height\":1400,\"depth\":200}}";

            var response = AiCommandParser.Parse(json, "откос 1200х1400 глубиной 200");

            Assert.NotNull(response.Action);
            Assert.Equal(AiCommandType.CalcSlope, response.Action!.Type);
            Assert.Equal(1200, response.Action.Params.Width);
            Assert.Equal(1400, response.Action.Params.Height);
            Assert.Equal(200, response.Action.Params.Depth);
            Assert.Equal(1, response.Action.Params.Quantity);
        }

        [Fact]
        public void Parse_CalcSlope_GeneratesConfirmationReply()
        {
            // Pure JSON block (no surrounding text) → the parser generates the
            // confirmation reply from the command.
            const string content = "```json\n{\"action\":\"calc_slope\",\"params\":{\"width\":1500,\"height\":700,\"depth\":300,\"quantity\":1}}\n```";

            var response = AiCommandParser.Parse(content, "откосы 1500х700");

            Assert.NotNull(response.Action);
            Assert.Equal(AiCommandType.CalcSlope, response.Action!.Type);
            Assert.Contains("Открыт просчёт откосов: 1500×700 мм", response.Reply);
        }

        [Fact]
        public void Parse_CalcSlope_MissingDimensions_ReturnsClarifyingReplyWithoutAction()
        {
            // Malformed AI JSON with a missing dimension (depth) must NOT open
            // the slope panel with zero/garbage values — the parser replies with
            // a clarifying question and no command (model is fallible despite
            // the system prompt).
            const string json = "{\"action\":\"calc_slope\",\"params\":{\"width\":1500,\"height\":700}}";

            var response = AiCommandParser.Parse(json, "откосы 1500х700");

            Assert.Null(response.Action);
            Assert.Contains("ширину, высоту и глубину", response.Reply);
        }

        [Fact]
        public void Parse_CalcSlope_ZeroDimensions_ReturnsClarifyingReplyWithoutAction()
        {
            // depth:0 (or any non-positive dimension) is equally rejected.
            const string json = "{\"action\":\"calc_slope\",\"params\":{\"width\":1500,\"height\":700,\"depth\":0}}";

            var response = AiCommandParser.Parse(json, "откосы 1500х700");

            Assert.Null(response.Action);
            Assert.Contains("ширину, высоту и глубину", response.Reply);
        }

        [Fact]
        public void Parse_AddItem_InConstruction_TextAlias_SetsInstallationMode2()
        {
            // User's real-world wording: «Добавь анвис корич 500 1000 в конструцию»
            // → installation_mode: 2 (в конструкцию).
            const string json = "{\"action\":\"add_item\",\"params\":{\"type\":\"Anwis\",\"color\":\"Коричневый\",\"width\":500,\"height\":1000,\"quantity\":1,\"price\":1900,\"anwis_mode\":\"ББ60\",\"installation_mode\":\"в конструцию\"}}";

            var response = AiCommandParser.Parse(json, "Добавь анвис корич 500 1000 в конструцию");

            Assert.NotNull(response.Action);
            Assert.Equal(AiCommandType.AddItem, response.Action!.Type);
            Assert.Equal(2, response.Action.Params.InstallationMode);
        }

        [Fact]
        public void Parse_AddItem_InConstruction_VariantAlias_SetsInstallationMode2()
        {
            // Model may emit the canonical spelling «в конструкцию».
            // anwis_mode is required by the parser guard for Anwis.
            const string json = "{\"action\":\"add_item\",\"params\":{\"type\":\"Anwis\",\"width\":500,\"height\":600,\"anwis_mode\":\"ББ60\",\"installation_mode\":\"в конструкцию\"}}";

            var response = AiCommandParser.Parse(json, "анвис 500 600 в конструцию");

            Assert.NotNull(response.Action);
            Assert.Equal(2, response.Action!.Params.InstallationMode);
        }

        [Fact]
        public void Parse_AddItem_NoInstallation_TextAlias_SetsInstallationMode1()
        {
            // anwis_mode is required by the parser guard for Anwis.
            const string json = "{\"action\":\"add_item\",\"params\":{\"type\":\"Anwis\",\"width\":500,\"height\":600,\"anwis_mode\":\"ББ60\",\"installation_mode\":\"без монтажа\"}}";

            var response = AiCommandParser.Parse(json, "анвис 500 600 без монтажа");

            Assert.NotNull(response.Action);
            Assert.Equal(1, response.Action!.Params.InstallationMode);
        }

        [Fact]
        public void Parse_AddItem_InstallationEnabled_NumericZero_SetsInstallationMode0()
        {
            // anwis_mode is required by the parser guard for Anwis.
            const string json = "{\"action\":\"add_item\",\"params\":{\"type\":\"Anwis\",\"width\":500,\"height\":600,\"anwis_mode\":\"ББ60\",\"installation_mode\":0}}";

            var response = AiCommandParser.Parse(json, "анвис 500 600 с монтажом");

            Assert.NotNull(response.Action);
            Assert.Equal(0, response.Action!.Params.InstallationMode);
        }

        [Fact]
        public void Parse_AddItem_WithoutInstallationMode_DefaultsToMinusOne()
        {
            // No installation_mode in the JSON → the program applies its own default.
            // Uses a NON-Anwis product so the Anwis-mode guard doesn't intercept.
            const string json = "{\"action\":\"add_item\",\"params\":{\"type\":\"Отлив\",\"color\":\"Коричневый\",\"width\":200,\"height\":1500,\"quantity\":1,\"price\":2150}}";

            var response = AiCommandParser.Parse(json, "отлив 200 1500");

            Assert.NotNull(response.Action);
            Assert.Equal(-1, response.Action!.Params.InstallationMode);
        }

        [Fact]
        public void Parse_AddItem_InConstruction_ConfirmationMentionsMode()
        {
            const string content = "```json\n{\"action\":\"add_item\",\"params\":{\"type\":\"Anwis\",\"color\":\"Коричневый\",\"width\":500,\"height\":1000,\"quantity\":1,\"price\":1900,\"anwis_mode\":\"ББ60\",\"installation_mode\":2}}\n```";

            var response = AiCommandParser.Parse(content, "анвис 500 1000 в конструцию");

            Assert.NotNull(response.Action);
            Assert.Contains(", в конструкцию", response.Reply);
        }

        [Fact]
        public void Parse_AddItem_700x1900_RealisticResponse_ParsesCorrectly()
        {
            // Regression: the user typed "700x1900" and the AI returned a
            // markdown-wrapped JSON action. The parser must extract it cleanly.
            const string content = "Добавлено: Anwis 700×1900\n```json\n{\"action\":\"add_item\",\"params\":{\"type\":\"Anwis\",\"color\":\"Белый\",\"width\":700,\"height\":1900,\"quantity\":1,\"price\":1800,\"anwis_mode\":\"ББ60\"}}\n```";

            var response = AiCommandParser.Parse(content, "700x1900");

            Assert.NotNull(response.Action);
            Assert.Equal(AiCommandType.AddItem, response.Action!.Type);
            Assert.Equal("Anwis", response.Action.Params.Type);
            Assert.Equal("Белый", response.Action.Params.Color);
            Assert.Equal(700, response.Action.Params.Width);
            Assert.Equal(1900, response.Action.Params.Height);
            Assert.Equal(1, response.Action.Params.Quantity);
            Assert.Equal(1800, response.Action.Params.Price);
            Assert.Equal(AnwisSizeMode.Брусбокс60, response.Action.Params.AnwisMode);
        }
    }
}
