using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
                SelectedAnwisMode = "ББ 60",
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
        public void FinalizeStreamingMessage_GuessedAnwisMode_ShowsPrefilledForm()
        {
            var vm = new AiAssistantViewModel();
            vm.Messages.Add(new AiChatMessage { Text = "ПМС Anwis. бел\r\n4 739х1116", IsUser = true });

            var modelReply = "{\"action\":\"add_item\",\"params\":{\"type\":\"Anwis\",\"color\":\"Белый\",\"width\":739,\"height\":1116,\"quantity\":4,\"anwis_mode\":\"ББ60\"}}";
            var msg = new AiChatMessage { Text = modelReply, IsUser = false, IsStreaming = true };

            var finalize = typeof(AiAssistantViewModel).GetMethod(
                "FinalizeStreamingMessage", BindingFlags.NonPublic | BindingFlags.Instance)!;
            finalize.Invoke(vm, new object[] { msg, modelReply });

            Assert.NotNull(msg.ClarificationForm);
            Assert.Equal("Anwis", msg.ClarificationForm!.SelectedType);
            Assert.Equal("Белый", msg.ClarificationForm.SelectedColor);
            Assert.Equal("739", msg.ClarificationForm.WidthText);
            Assert.Equal("1116", msg.ClarificationForm.HeightText);
            Assert.Equal("4", msg.ClarificationForm.QuantityText);
        }

        /// <summary>
        /// The model recovered the full Anwis parameters (type/color/size/qty)
        /// from earlier context even though the latest user text doesn't spell
        /// them out. The card must pre-fill from the parsed command, not show
        /// up blank.
        /// </summary>
        [Fact]
        public void FinalizeStreamingMessage_GuessedModeWithSparseUserText_PrefillsFromParsedCommand()
        {
            var vm = new AiAssistantViewModel();
            vm.Messages.Add(new AiChatMessage { Text = "сделай сетку Anwis", IsUser = true });

            var modelReply = "{\"action\":\"add_item\",\"params\":{\"type\":\"Anwis\",\"color\":\"Коричневый\",\"width\":619,\"height\":1295,\"quantity\":2,\"anwis_mode\":\"ББ60\"}}";
            var msg = new AiChatMessage { Text = modelReply, IsUser = false, IsStreaming = true };

            var finalize = typeof(AiAssistantViewModel).GetMethod(
                "FinalizeStreamingMessage", BindingFlags.NonPublic | BindingFlags.Instance)!;
            finalize.Invoke(vm, new object[] { msg, modelReply });

            Assert.NotNull(msg.ClarificationForm);
            Assert.Equal("Anwis", msg.ClarificationForm!.SelectedType);
            Assert.Equal("Коричневый", msg.ClarificationForm.SelectedColor);
            Assert.Equal("619", msg.ClarificationForm.WidthText);
            Assert.Equal("1295", msg.ClarificationForm.HeightText);
            Assert.Equal("2", msg.ClarificationForm.QuantityText);
            Assert.Equal(AiClarificationForm.UnspecifiedAnwisMode, msg.ClarificationForm.SelectedAnwisMode);
        }

        [Fact]
        public void FinalizeStreamingMessage_SplitUserMessages_PrefillsFromAllOfThem()
        {
            var vm = new AiAssistantViewModel();
            // The manager sent the request as two separate messages.
            vm.Messages.Add(new AiChatMessage { Text = "ПМС Anwis. бел", IsUser = true });
            vm.Messages.Add(new AiChatMessage { Text = "4 739х1116", IsUser = true });

            var modelReply = "{\"action\":\"add_item\",\"params\":{\"type\":\"Anwis\",\"color\":\"Белый\",\"width\":739,\"height\":1116,\"quantity\":4,\"anwis_mode\":\"ББ60\"}}";
            var msg = new AiChatMessage { Text = modelReply, IsUser = false, IsStreaming = true };

            var finalize = typeof(AiAssistantViewModel).GetMethod(
                "FinalizeStreamingMessage", BindingFlags.NonPublic | BindingFlags.Instance)!;
            finalize.Invoke(vm, new object[] { msg, modelReply });

            Assert.NotNull(msg.ClarificationForm);
            Assert.Equal("Anwis", msg.ClarificationForm!.SelectedType);
            Assert.Equal("Белый", msg.ClarificationForm.SelectedColor);
            Assert.Equal("739", msg.ClarificationForm.WidthText);
            Assert.Equal("1116", msg.ClarificationForm.HeightText);
            Assert.Equal("4", msg.ClarificationForm.QuantityText);
        }

        /// <summary>
        /// Manager sends only a screenshot whose file name encodes the whole
        /// order («ПМС Anwis, бел. 1 619x1295.png») and types no caption at
        /// all. The clarification card would have come up empty even though
        /// the answer is sitting right in the filename — the card must
        /// pre-fill from AttachmentLabels.
        /// </summary>
        [Fact]
        public void FinalizeStreamingMessage_PhotoOnlyWithEncodedFilename_PrefillsCard()
        {
            var vm = new AiAssistantViewModel();
            // Empty Text (no caption), one attachment whose label carries the
            // full order: «ПМС Anwis, бел. 1 619x1295.png». Mirrors the exact
            // shape of the bug report from the manager.
            vm.Messages.Add(new AiChatMessage
            {
                Text = "",
                IsUser = true,
                AttachmentCount = 1,
                AttachmentLabels = { "ПМС Anwis, бел. 1 619x1295.png" }
            });

            var modelReply = "Какой режим Anwis использовать? ББ60, ББ70, ПП, Проём или Габарит?";
            var msg = new AiChatMessage { Text = modelReply, IsUser = false, IsStreaming = true };

            var finalize = typeof(AiAssistantViewModel).GetMethod(
                "FinalizeStreamingMessage", BindingFlags.NonPublic | BindingFlags.Instance)!;
            finalize.Invoke(vm, new object[] { msg, modelReply });

            Assert.NotNull(msg.ClarificationForm);
            Assert.Equal("Anwis", msg.ClarificationForm!.SelectedType);
            Assert.Equal("Белый", msg.ClarificationForm.SelectedColor);
            Assert.Equal("619", msg.ClarificationForm.WidthText);
            Assert.Equal("1295", msg.ClarificationForm.HeightText);
            Assert.Equal("1", msg.ClarificationForm.QuantityText);
        }

        /// <summary>
        /// Manager types text AND attaches a screenshot whose filename
        /// carries extra info (e.g. the size is in the filename only). Both
        /// must merge into the prefill source.
        /// </summary>
        [Fact]
        public void FinalizeStreamingMessage_TextPlusAttachmentLabel_MergesIntoPrefill()
        {
            var vm = new AiAssistantViewModel();
            // Text mentions «ПМС Anwis бел», filename carries the size and
            // quantity — PreFillFromRequest must merge both.
            vm.Messages.Add(new AiChatMessage
            {
                Text = "ПМС Anwis бел",
                IsUser = true,
                AttachmentCount = 1,
                AttachmentLabels = { "анвис белый 700x1400 4шт.png" }
            });

            var modelReply = "{\"action\":\"add_item\",\"params\":{\"type\":\"Anwis\",\"color\":\"Белый\",\"width\":700,\"height\":1400,\"quantity\":4,\"anwis_mode\":\"ББ60\"}}";
            var msg = new AiChatMessage { Text = modelReply, IsUser = false, IsStreaming = true };

            var finalize = typeof(AiAssistantViewModel).GetMethod(
                "FinalizeStreamingMessage", BindingFlags.NonPublic | BindingFlags.Instance)!;
            finalize.Invoke(vm, new object[] { msg, modelReply });

            Assert.NotNull(msg.ClarificationForm);
            Assert.Equal("700", msg.ClarificationForm!.WidthText);
            Assert.Equal("1400", msg.ClarificationForm.HeightText);
            // «4 шт» wins over any leading number because the regex matches
            // the explicit unit suffix first.
            Assert.Equal("4", msg.ClarificationForm.QuantityText);
            Assert.Equal("Белый", msg.ClarificationForm.SelectedColor);
        }

        /// <summary>
        /// The attached screenshot has a useless, generic file name
        /// («Снимок.PNG») and the manager typed nothing. Windows.Media.Ocr
        /// was run at send time and stuffed the order text into
        /// AttachmentOcr — the card must pre-fill from those pixels too.
        /// </summary>
        [Fact]
        public void FinalizeStreamingMessage_OcrTextOnGenericFilename_PrefillsCard()
        {
            var vm = new AiAssistantViewModel();
            vm.Messages.Add(new AiChatMessage
            {
                Text = "",
                IsUser = true,
                AttachmentCount = 1,
                AttachmentLabels = { "Снимок.PNG" },
                // Simulating what AttachmentOcrService.ExtractAsync wrote into
                // the message at send time.
                AttachmentOcr = { "ПМС Anwis, бел. 1 619x1295" }
            });

            var modelReply = "Какой режим Anwis использовать?";
            var msg = new AiChatMessage { Text = modelReply, IsUser = false, IsStreaming = true };

            var finalize = typeof(AiAssistantViewModel).GetMethod(
                "FinalizeStreamingMessage", BindingFlags.NonPublic | BindingFlags.Instance)!;
            finalize.Invoke(vm, new object[] { msg, modelReply });

            Assert.NotNull(msg.ClarificationForm);
            Assert.Equal("Белый", msg.ClarificationForm!.SelectedColor);
            Assert.Equal("619", msg.ClarificationForm.WidthText);
            Assert.Equal("1295", msg.ClarificationForm.HeightText);
            Assert.Equal("1", msg.ClarificationForm.QuantityText);
        }

        /// <summary>
        /// A photo the local OCR couldn't read, answered by a vision model in
        /// plain text that spells out the parameters. The card must not come up
        /// blank — it pre-fills from the reply, while the Anwis profile stays
        /// unselected because the reply only lists the options.
        /// </summary>
        [Fact]
        public void FinalizeStreamingMessage_PlainTextReplyWithParams_PrefillsFromReply()
        {
            var vm = new AiAssistantViewModel();
            vm.Messages.Add(new AiChatMessage
            {
                Text = "",
                IsUser = true,
                AttachmentCount = 1,
                AttachmentLabels = { "IMG_1234.jpg" }
                // No AttachmentOcr — Windows OCR couldn't read this photo.
            });

            var modelReply = "Вижу сетку Anwis 700×1400 белый, 2 шт. Какой режим использовать? ББ60, ББ70, ПП, Проём или Габарит?";
            var msg = new AiChatMessage { Text = modelReply, IsUser = false, IsStreaming = true };

            var finalize = typeof(AiAssistantViewModel).GetMethod(
                "FinalizeStreamingMessage", BindingFlags.NonPublic | BindingFlags.Instance)!;
            finalize.Invoke(vm, new object[] { msg, modelReply });

            Assert.NotNull(msg.ClarificationForm);
            Assert.Equal("Anwis", msg.ClarificationForm!.SelectedType);
            Assert.Equal("Белый", msg.ClarificationForm.SelectedColor);
            Assert.Equal("700", msg.ClarificationForm.WidthText);
            Assert.Equal("1400", msg.ClarificationForm.HeightText);
            Assert.Equal("2", msg.ClarificationForm.QuantityText);
            // The reply's «ББ60, ББ70, ПП…» is an options list, not a user pick.
            Assert.Equal(AiClarificationForm.UnspecifiedAnwisMode, msg.ClarificationForm.SelectedAnwisMode);
        }

        /// <summary>
        /// A sized non-Anwis product (Отлив) parsed from an image arrives with
        /// width/height the model never saw (0×0). It must not execute as a
        /// broken item — the same pre-filled card path that guards the Anwis
        /// mode now guards every product's dimensions.
        /// </summary>
        [Fact]
        public void FinalizeStreamingMessage_MissingDimensionsOnSizedProduct_ShowsPrefilledForm()
        {
            var vm = new AiAssistantViewModel();
            vm.Messages.Add(new AiChatMessage { Text = "добавь отлив белый", IsUser = true });

            var modelReply = "{\"action\":\"add_item\",\"params\":{\"type\":\"Отлив\",\"color\":\"Белый\",\"width\":0,\"height\":0,\"quantity\":1}}";
            var msg = new AiChatMessage { Text = modelReply, IsUser = false, IsStreaming = true };

            var finalize = typeof(AiAssistantViewModel).GetMethod(
                "FinalizeStreamingMessage", BindingFlags.NonPublic | BindingFlags.Instance)!;
            finalize.Invoke(vm, new object[] { msg, modelReply });

            Assert.NotNull(msg.ClarificationForm);
            Assert.Equal("Отлив", msg.ClarificationForm!.SelectedType);
            Assert.Equal("Белый", msg.ClarificationForm.SelectedColor);
            Assert.Equal("", msg.ClarificationForm.WidthText);
            Assert.Equal("", msg.ClarificationForm.HeightText);
            Assert.Contains("Не хватает параметров", msg.Text);
        }

        /// <summary>
        /// «отлив бел 170 900» names type/color/size but not монтаж. The AI must
        /// not silently default «Без монтажа» — it shows the pre-filled card and
        /// asks instead of executing.
        /// </summary>
        [Fact]
        public void FinalizeStreamingMessage_MissingInstallation_ShowsPrefilledForm()
        {
            var vm = new AiAssistantViewModel();
            vm.Messages.Add(new AiChatMessage { Text = "отлив бел 170 900", IsUser = true });

            var modelReply = "{\"action\":\"add_item\",\"params\":{\"type\":\"Отлив\",\"color\":\"Белый\",\"width\":170,\"height\":900,\"quantity\":1}}";
            var msg = new AiChatMessage { Text = modelReply, IsUser = false, IsStreaming = true };

            var finalize = typeof(AiAssistantViewModel).GetMethod(
                "FinalizeStreamingMessage", BindingFlags.NonPublic | BindingFlags.Instance)!;
            finalize.Invoke(vm, new object[] { msg, modelReply });

            Assert.NotNull(msg.ClarificationForm);
            Assert.Equal("Отлив", msg.ClarificationForm!.SelectedType);
            Assert.Equal("Белый", msg.ClarificationForm.SelectedColor);
            Assert.Equal("170", msg.ClarificationForm.WidthText);
            Assert.Equal("900", msg.ClarificationForm.HeightText);
            Assert.Contains("монтаж", msg.Text, StringComparison.OrdinalIgnoreCase);
            // The card must expose the installation choice (Отлив is applicable).
            Assert.True(msg.ClarificationForm.ShowInstallation);
        }

        /// <summary>
        /// «отлив бел 170 900 с монтажом» names the installation, so the card
        /// must NOT appear — the plan preview awaits confirmation instead.
        /// </summary>
        [Fact]
        public void FinalizeStreamingMessage_InstallationSpecified_ShowsPlanNotForm()
        {
            var vm = new AiAssistantViewModel();
            vm.Messages.Add(new AiChatMessage { Text = "отлив бел 170 900 с монтажом", IsUser = true });

            var modelReply = "{\"action\":\"add_item\",\"params\":{\"type\":\"Отлив\",\"color\":\"Белый\",\"width\":170,\"height\":900,\"quantity\":1,\"installation_mode\":0}}";
            var msg = new AiChatMessage { Text = modelReply, IsUser = false, IsStreaming = true };

            var finalize = typeof(AiAssistantViewModel).GetMethod(
                "FinalizeStreamingMessage", BindingFlags.NonPublic | BindingFlags.Instance)!;
            finalize.Invoke(vm, new object[] { msg, modelReply });

            Assert.Null(msg.ClarificationForm);
            Assert.NotNull(msg.ActionPlan);
            Assert.True(msg.IsAwaitingConfirmation);
            Assert.Equal(0, msg.ActionPlan!.Steps[0].Params.InstallationMode);
        }

        /// <summary>
        /// When several parameters are missing at once, the message names the most
        /// critical one first: Anwis mode &gt; dimensions &gt; installation.
        /// </summary>
        [Fact]
        public void FinalizeStreamingMessage_MissingParams_MessagePriority()
        {
            var finalize = typeof(AiAssistantViewModel).GetMethod(
                "FinalizeStreamingMessage", BindingFlags.NonPublic | BindingFlags.Instance)!;

            // Anwis without mode AND without installation → Anwis wins.
            var vm = new AiAssistantViewModel();
            vm.Messages.Add(new AiChatMessage { Text = "Добавь сетку Anwis белый 739×1116", IsUser = true });
            var modelReply = "{\"action\":\"add_item\",\"params\":{\"type\":\"Anwis\",\"color\":\"Белый\",\"width\":739,\"height\":1116,\"quantity\":1,\"anwis_mode\":\"ББ60\"}}";
            var msg = new AiChatMessage { Text = modelReply, IsUser = false, IsStreaming = true };
            finalize.Invoke(vm, new object[] { msg, modelReply });
            Assert.Contains("режим Anwis", msg.Text);
            Assert.DoesNotContain("монтаж", msg.Text, StringComparison.OrdinalIgnoreCase);

            // Sized product with 0×0 and no installation → dimensions win over монтаж.
            var vm2 = new AiAssistantViewModel();
            vm2.Messages.Add(new AiChatMessage { Text = "добавь отлив белый", IsUser = true });
            var modelReply2 = "{\"action\":\"add_item\",\"params\":{\"type\":\"Отлив\",\"color\":\"Белый\",\"width\":0,\"height\":0,\"quantity\":1}}";
            var msg2 = new AiChatMessage { Text = modelReply2, IsUser = false, IsStreaming = true };
            finalize.Invoke(vm2, new object[] { msg2, modelReply2 });
            Assert.Contains("Не хватает параметров", msg2.Text);
            Assert.DoesNotContain("Не указан монтаж", msg2.Text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildModelUserText_MergesTextLabelsAndOcr()
        {
            var merged = AiAssistantViewModel.BuildModelUserText(
                "сделай сетку",
                new[] { "Снимок.PNG" },
                new[] { "ПМС Anwis, бел. 1 619x1295" });

            Assert.Contains("сделай сетку", merged);
            Assert.Contains("Файл: Снимок.PNG", merged);
            Assert.Contains("Текст с картинки: ПМС Anwis, бел. 1 619x1295", merged);
        }

        [Theory]
        [InlineData(new[] { "" }, true)]
        [InlineData(new[] { "   " }, true)]
        [InlineData(new[] { "", "ПМС Anwis 700x1400" }, false)]
        [InlineData(new[] { "ПМС Anwis 700x1400" }, false)]
        public void BuildOcrWarning_OnlyWhenEveryImageUnrecognized(string[] ocrLines, bool expectWarning)
        {
            var warning = AiAssistantViewModel.BuildOcrWarning(ocrLines);
            Assert.Equal(expectWarning, warning != null);
        }

        [Fact]
        public void BuildOcrWarning_NoImages_ReturnsNull()
        {
            Assert.Null(AiAssistantViewModel.BuildOcrWarning(Array.Empty<string>()));
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

        [Fact]
        public void Attachments_EnableSendWithoutText_AndClearOnAddRemove()
        {
            var vm = new AiAssistantViewModel();
            Assert.False(vm.CanSend);
            Assert.False(vm.HasAttachments);

            var attachment = new AiImageAttachment
            {
                FileName = "photo.png",
                DataUrl = "data:image/png;base64,AAAA",
                SizeLabel = "1 КБ"
            };

            // An image alone makes the composer sendable.
            vm.AddAttachment(attachment);
            Assert.True(vm.HasAttachments);
            Assert.True(vm.CanSend);
            Assert.Single(vm.Attachments);

            vm.RemoveAttachment(attachment);
            Assert.False(vm.HasAttachments);
            Assert.False(vm.CanSend);
            Assert.Empty(vm.Attachments);
        }

        [Fact]
        public void AiChatMessage_AttachmentCount_IsRuntimeOnly()
        {
            var message = new AiChatMessage { AttachmentCount = 2 };
            Assert.True(message.HasAttachments);

            // Attachment payloads must never be persisted into chat history.
            var json = System.Text.Json.JsonSerializer.Serialize(message);
            Assert.DoesNotContain("AttachmentCount", json);
        }

        [Fact]
        public void AiImageAttachment_OcrStatus_TransitionsAcrossStates()
        {
            var attachment = new AiImageAttachment
            {
                FileName = "photo.png",
                DataUrl = "data:image/png;base64,AAAA",
                SizeLabel = "1 КБ"
            };

            // Fresh attachment: OCR hasn't finished yet.
            Assert.Equal("pending", attachment.OcrStatus);
            Assert.Equal("…", attachment.OcrStatusGlyph);

            // OCR finished but recognized nothing.
            attachment.OcrText = "";
            Assert.Equal("empty", attachment.OcrStatus);
            Assert.Equal("⚠", attachment.OcrStatusGlyph);

            // The failure reason is surfaced in the tooltip.
            attachment.OcrFailureReason = "Текст на фото не найден.";
            Assert.Equal("Текст на фото не найден.", attachment.OcrStatusToolTip);

            // OCR recognized text.
            attachment.OcrText = "ПМС Anwis 700x1400";
            Assert.Equal("ok", attachment.OcrStatus);
            Assert.Equal("✓", attachment.OcrStatusGlyph);
        }
    }
}
