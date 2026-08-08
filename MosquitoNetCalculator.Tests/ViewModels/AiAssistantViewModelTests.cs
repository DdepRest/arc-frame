using System;
using System.IO;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.ViewModels;
using Xunit;

namespace MosquitoNetCalculator.Tests.ViewModels
{
    [Collection("FileSystem")]
    public class AiAssistantViewModelTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _originalAiSettingsPath;

        public AiAssistantViewModelTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "mnc_ai_vm_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _originalAiSettingsPath = AppSettingsServiceAi.AiSettingsPath;
            AppSettingsServiceAi.AiSettingsPath = Path.Combine(_tempDir, "ai-settings.json");
        }

        public void Dispose()
        {
            AppSettingsServiceAi.AiSettingsPath = _originalAiSettingsPath;
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        [Fact]
        public void Constructor_WithNoHistory_ShowsWelcomeMessage()
        {
            var vm = new AiAssistantViewModel();

            Assert.Single(vm.Messages);
            Assert.Contains("Здравствуйте", vm.Messages[0].Text);
            Assert.False(vm.Messages[0].IsUser);
        }

        [Fact]
        public void Constructor_WithSavedHistory_LoadsMessages()
        {
            var saved = new[]
            {
                new AiChatMessage { Text = "User question", IsUser = true },
                new AiChatMessage { Text = "Assistant reply", IsUser = false }
            };
            AppSettingsServiceAi.SaveChatHistory(saved);

            var vm = new AiAssistantViewModel();

            Assert.Equal(2, vm.Messages.Count);
            Assert.Equal("User question", vm.Messages[0].Text);
            Assert.True(vm.Messages[0].IsUser);
            Assert.Equal("Assistant reply", vm.Messages[1].Text);
            Assert.False(vm.Messages[1].IsUser);
        }

        [Fact]
        public void SubmitClarificationForm_AttachesPlan_ConfirmationFiresPlanReceived()
        {
            var vm = new AiAssistantViewModel();
            var welcome = vm.Messages[0];

            var form = new AiClarificationForm
            {
                SelectedType = "Anwis",
                SelectedColor = "Белый",
                WidthText = "700",
                HeightText = "1400",
                QuantityText = "1",
                SelectedInstallation = "С монтажом"
            };
            var msg = new AiChatMessage { Text = "Уточните параметры", IsUser = false, ClarificationForm = form };
            vm.Messages.Add(msg);

            AiCommand? received = null;
            AiActionPlan? receivedPlan = null;
            vm.CommandReceived += c => received = c;
            vm.PlanReceived += p => receivedPlan = p;

            vm.SubmitClarificationForm(msg);

            // The form card is hidden and the selection is echoed in chat.
            Assert.Null(msg.ClarificationForm);
            Assert.Contains(vm.Messages, m => m.IsUser && m.Text.Contains("Добавить:"));

            // Agent Mode: a plan preview awaits confirmation — nothing executed yet.
            var confirm = vm.Messages.Last();
            Assert.False(confirm.IsUser);
            Assert.True(confirm.HasActionPlan);
            Assert.True(confirm.IsAwaitingConfirmation);
            Assert.Null(received);
            Assert.Null(receivedPlan);

            // User presses «Выполнить» → the plan fires, still no direct execution.
            vm.ConfirmPlan(confirm);

            Assert.NotNull(receivedPlan);
            Assert.Single(receivedPlan!.Steps);
            var step = receivedPlan.Steps[0];
            Assert.Equal(AiCommandType.AddItem, step.CommandType);
            Assert.Equal("Anwis", step.Params.Type);
            Assert.Equal(700, step.Params.Width);
            Assert.Equal(1400, step.Params.Height);
            Assert.Equal(0, step.Params.InstallationMode); // С монтажом
            Assert.True(receivedPlan.RequiresConfirmation);
            Assert.Null(received);

            // Welcome bubble stays untouched at the head of the chat.
            Assert.Same(welcome, vm.Messages[0]);
        }

        [Fact]
        public void ConfirmPlan_GuardsAgainstDoubleExecution()
        {
            var vm = new AiAssistantViewModel();
            var plan = AiPlanBuilder.FromCommand(new AiCommand
            {
                Type = AiCommandType.AddItem,
                Params = new AiCommandParams { Type = "Anwis", Width = 700, Height = 1400 }
            });
            var msg = new AiChatMessage { IsUser = false, ActionPlan = plan, IsAwaitingConfirmation = true };
            vm.Messages.Add(msg);

            int fired = 0;
            vm.PlanReceived += _ => fired++;

            vm.ConfirmPlan(msg);
            vm.ConfirmPlan(msg); // duplicate press

            Assert.Equal(1, fired);
        }

        [Fact]
        public void CancelPlan_MarksMessageCancelled_AndNeverFires()
        {
            var vm = new AiAssistantViewModel();
            var plan = AiPlanBuilder.FromCommand(new AiCommand { Type = AiCommandType.ClearAll });
            var msg = new AiChatMessage { IsUser = false, ActionPlan = plan, IsAwaitingConfirmation = true };
            vm.Messages.Add(msg);

            int fired = 0;
            vm.PlanReceived += _ => fired++;

            vm.CancelPlan(msg);

            Assert.True(msg.IsCancelled);
            Assert.False(msg.IsAwaitingConfirmation);
            vm.ConfirmPlan(msg);
            Assert.Equal(0, fired);
        }

        [Fact]
        public void SubmitClarificationForm_InvalidDimensions_ShowsErrorWithoutCommand()
        {
            var vm = new AiAssistantViewModel();

            var form = new AiClarificationForm { WidthText = "abc", HeightText = "1400" };
            var msg = new AiChatMessage { Text = "Уточните параметры", IsUser = false, ClarificationForm = form };
            vm.Messages.Add(msg);

            AiCommand? received = null;
            vm.CommandReceived += c => received = c;

            vm.SubmitClarificationForm(msg);

            Assert.Null(received);
            Assert.Contains(vm.Messages, m => !m.IsUser && m.Text.Contains("⚠"));
            // The form stays so the user can correct the input.
            Assert.NotNull(msg.ClarificationForm);
        }

        [Fact]
        public async System.Threading.Tasks.Task SendMessageAsync_SlashCommand_DoesNotLockBusy()
        {
            var vm = new AiAssistantViewModel();
            vm.InputText = "/товары";

            await vm.SendMessageAsync();

            // The early return must release the busy flag, otherwise the
            // composer stays locked forever after any local command.
            Assert.False(vm.IsBusy);
            Assert.Contains(vm.Messages, m => !m.IsUser && m.Text.Contains("## Каталог товаров"));
        }

        [Fact]
        public async System.Threading.Tasks.Task SendMessageAsync_UnknownSlash_ShowsHelp_AndStaysUnlocked()
        {
            var vm = new AiAssistantViewModel();
            vm.InputText = "/незнаю";

            await vm.SendMessageAsync();

            Assert.False(vm.IsBusy);
            Assert.Contains(vm.Messages, m => !m.IsUser && m.Text.Contains("Неизвестная команда"));
        }

        [Fact]
        public void ClearChat_ClearsPersistedHistory()
        {
            var saved = new[] { new AiChatMessage { Text = "Old", IsUser = true } };
            AppSettingsServiceAi.SaveChatHistory(saved);

            var vm = new AiAssistantViewModel();
            vm.ClearChat();

            var reloaded = new AiAssistantViewModel();
            Assert.Single(reloaded.Messages);
            Assert.Contains("Здравствуйте", reloaded.Messages[0].Text);
        }
    }
}
