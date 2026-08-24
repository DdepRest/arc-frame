using System.Reflection;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.ViewModels;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Regression coverage for the reported scenario «отлив бел 170 900 без монтажа».
    /// Verifies that a parsed add_item with installation_mode=1 is routed to the
    /// plan-preview (confirmation card) — NOT the clarification form — and that the
    /// step carries the explicit installation mode through to execution.
    /// </summary>
    public class AiAddOtlivStaReproTests
    {
        private const string UserText = "отлив бел 170 900 без монтажа";
        private const string ModelReply =
            "{\"action\":\"add_item\",\"params\":{\"type\":\"Отлив\",\"color\":\"Белый\",\"width\":170,\"height\":900,\"quantity\":1,\"installation_mode\":1}}";

        [Fact]
        public void FinalizeStreamingMessage_OtlivWithoutInstallation_ShowsPlanNotForm()
        {
            var vm = new AiAssistantViewModel();
            vm.Messages.Add(new AiChatMessage { Text = UserText, IsUser = true });

            var msg = new AiChatMessage { Text = ModelReply, IsUser = false, IsStreaming = true };
            var finalize = typeof(AiAssistantViewModel).GetMethod(
                "FinalizeStreamingMessage", BindingFlags.NonPublic | BindingFlags.Instance)!;
            finalize.Invoke(vm, new object[] { msg, ModelReply });

            Assert.Null(msg.ClarificationForm);
            Assert.NotNull(msg.ActionPlan);
            Assert.True(msg.IsAwaitingConfirmation);
            var step = Assert.Single(msg.ActionPlan!.Steps);
            Assert.Equal(AiCommandType.AddItem, step.CommandType);
            Assert.Equal("Отлив", step.Params.Type);
            Assert.Equal(170, step.Params.Width);
            Assert.Equal(900, step.Params.Height);
            Assert.Equal(1, step.Params.InstallationMode);
        }
    }
}
