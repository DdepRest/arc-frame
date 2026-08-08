using System;
using System.IO;
using System.Linq;
using MosquitoNetCalculator.Models;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    public sealed class AiAssistantControlTests
    {
        [Fact]
        public void BusyStatus_IsVisibleDuringStreaming_AndShowsThinkingAndTypingPhases()
        {
            var xaml = File.ReadAllText(LocateSource("Controls/AiAssistantControl.xaml"));
            var codeBehind = File.ReadAllText(LocateSource("Controls/AiAssistantControl.xaml.cs"));
            var viewModel = File.ReadAllText(LocateSource("ViewModels/AiAssistantViewModel.cs"));

            // The overlay is bound straight to IsBusy — no code-behind timing
            // race can leave it hidden while a request is in flight.
            Assert.Contains("x:Name=\"TypingIndicator\"", xaml);
            Assert.Contains("Visibility=\"{Binding IsBusy, Converter={StaticResource BoolToVisibility}}\"", xaml);
            Assert.Contains("Text=\"{Binding StatusText}\"", xaml);
            Assert.DoesNotContain("TypingIndicator.Visibility", codeBehind);

            // The single overlay indicator handles both phases: empty StatusText
            // with pulsing dots for «ожидание», then «Печатает…» after first token.
            // No separate inline bubble indicator — one status, one place.
            Assert.Contains("StatusText = \"Думает…\"", viewModel);
            Assert.Contains("StatusText = \"Печатает…\"", viewModel);
            Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml);
            Assert.Contains("AutomationProperties.Name=\"Состояние ответа AI\"", xaml);
        }

        [Fact]
        public void ClarificationForm_RendersInAssistantBubble_WhenHasForm()
        {
            var xaml = File.ReadAllText(LocateSource("Controls/AiAssistantControl.xaml"));
            var codeBehind = File.ReadAllText(LocateSource("Controls/AiAssistantControl.xaml.cs"));

            // The form card is bound to HasClarificationForm and shows only in
            // the assistant bubble (right next to the message text).
            Assert.Contains("Binding HasClarificationForm", xaml);
            Assert.Contains("Заполните параметры", xaml);
            Assert.Contains("ClarificationForm.ProductTypes", xaml);
            Assert.Contains("ClarificationForm.WidthText", xaml);
            Assert.Contains("ClarificationForm.HeightText", xaml);
            Assert.Contains("ClarificationForm.SelectedAnwisMode", xaml);
            Assert.Contains("ClarificationForm.SelectedInstallation", xaml);
            Assert.Contains("BtnSubmitClarification_Click", xaml);

            // Submit is wired to the ViewModel through the message DataContext.
            Assert.Contains("SubmitClarificationForm(msg)", codeBehind);
        }

        [Fact]
        public void AllStaticResourceKeys_AreResolvableFromThemesOrLocally()
        {
            // Regression guard for the runtime XamlParseException «Предоставление
            // значения для StaticResourceHolder вызвало исключение»: the plan card
            // used DialogOutlineButton which lived only in AiApiKeyDialog's local
            // window resources and crashed the template the moment a plan message
            // (fresh or restored from chat history) was rendered. Every
            // StaticResource key referenced from this control must be defined
            // either in the control's own resources or in the app theme
            // dictionaries that App.xaml merges.
            var xaml = File.ReadAllText(LocateSource("Controls/AiAssistantControl.xaml"));
            var themesDir = Path.GetDirectoryName(LocateSource("Themes/Brushes.xaml"))!;

            var usedKeys = System.Text.RegularExpressions.Regex
                .Matches(xaml, @"\{StaticResource ([A-Za-z0-9_]+)\}")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .ToList();

            Assert.NotEmpty(usedKeys);
            foreach (var key in usedKeys)
            {
                bool definedLocally = xaml.Contains($"x:Key=\"{key}\"");
                bool definedInTheme = Directory.GetFiles(themesDir, "*.xaml")
                    .Any(f => File.ReadAllText(f).Contains($"x:Key=\"{key}\""));

                Assert.True(definedLocally || definedInTheme,
                    $"StaticResource '{key}' is used by AiAssistantControl.xaml but defined " +
                    $"neither in the control's resources nor in Themes/*.xaml. " +
                    $"It must not be window-local (e.g. AiApiKeyDialog) — see regression " +
                    $"for DialogOutlineButton.");
            }
        }

        [Fact]
        public void SlashAutocomplete_PopupIsWiredToCommandCatalog()
        {
            var xaml = File.ReadAllText(LocateSource("Controls/AiAssistantControl.xaml"));
            var codeBehind = File.ReadAllText(LocateSource("Controls/AiAssistantControl.xaml.cs"));

            // Typing «/» must surface the offline slash-command catalog with
            // descriptions so managers discover commands without /help.
            Assert.Contains("x:Name=\"SlashPopup\"", xaml);
            Assert.Contains("x:Name=\"SlashCommandList\"", xaml);
            Assert.Contains("Binding Command", xaml);
            Assert.Contains("Binding Description", xaml);
            Assert.Contains("AiLocalCommandRouter.Commands", codeBehind);
            Assert.Contains("UpdateSlashSuggestions()", codeBehind);
            Assert.Contains("InsertSlashCommand()", codeBehind);
            Assert.Contains("SlashCommandList_PreviewMouseLeftButtonUp", xaml);
        }

        [Fact]
        public void DevelopmentOverlay_LeavesPanelVisibleButCapturesAllInteraction()
        {
            var xaml = File.ReadAllText(LocateSource("Controls/AiAssistantControl.xaml"));
            var codeBehind = File.ReadAllText(LocateSource("Controls/AiAssistantControl.xaml.cs"));

            // The feature is intentionally preview-only: the overlay must sit
            // above the whole control and intercept input while keeping the
            // existing chat layout visible underneath it.
            Assert.Contains("x:Name=\"DevelopmentOverlay\"", xaml);
            Assert.Contains("Grid.RowSpan=\"2\"", xaml);
            Assert.Contains("Panel.ZIndex=\"100\"", xaml);
            Assert.Contains("IsHitTestVisible=\"True\"", xaml);
            Assert.Contains("Focusable=\"True\"", xaml);
            Assert.Contains("KeyboardNavigation.TabNavigation=\"None\"", xaml);
            Assert.Contains("DevelopmentOverlay.Focus()", codeBehind);
            Assert.Contains("В РАЗРАБОТКЕ", xaml);
            Assert.Contains("действия временно отключены", xaml);
        }

        [Fact]
        public void AiPanels_DoNotExposeModelOrProviderLabels()
        {
            var controlXaml = File.ReadAllText(LocateSource("Controls/AiAssistantControl.xaml"));
            var windowXaml = File.ReadAllText(LocateSource("Controls/AiAssistantWindow.xaml"));

            Assert.DoesNotContain("{Binding ModelLabel}", controlXaml);
            Assert.DoesNotContain("{Binding MetricsLabel}", controlXaml);
            Assert.DoesNotContain("{Binding CurrentModel}", windowXaml);
            Assert.DoesNotContain("{Binding ApiKeyStatusText}", windowXaml);
            Assert.DoesNotContain("Модель и провайдер", controlXaml);
        }

        [Fact]
        public void ApiKeyMenu_RemainsVisibleButDoesNotOpenDialog()
        {
            var xaml = File.ReadAllText(LocateSource("Controls/TitleBarControl.xaml"));
            var codeBehind = File.ReadAllText(LocateSource("Controls/TitleBarControl.xaml.cs"));

            Assert.Contains("AI Ассистент — API ключ", xaml);
            Assert.Contains("Click=\"MenuAiApiKey_Click\"", xaml);
            Assert.Contains("IsEnabled=\"False\"", xaml);
            Assert.Contains("ToolTip=\"AI Ассистент — в разработке\"", xaml);
            Assert.DoesNotContain("new AiApiKeyDialog", codeBehind);
            Assert.DoesNotContain("ShowDialog()", codeBehind);
        }

        [Fact]
        public void IsThinking_IsTrueOnlyForEmptyStreamingMessages()
        {
            var message = new AiChatMessage { IsStreaming = true };

            Assert.True(message.IsThinking);

            message.Text = "Первый фрагмент";
            Assert.False(message.IsThinking);

            message.Text = string.Empty;
            message.IsStreaming = false;
            Assert.False(message.IsThinking);
        }

        [Fact]
        public void DisplayText_HidesRawJsonProtocolWhileStreaming()
        {
            // The model streams its action block as raw JSON — the user must
            // never see «{ "action": … }» being typed out.
            var msg = new AiChatMessage { IsStreaming = true, Text = "{\"action\":\"add_item\"" };

            Assert.True(msg.IsRawProtocol);
            Assert.Equal("", msg.DisplayText);

            // Once finalization replaces the text with the parsed friendly reply,
            // the bubble shows it.
            msg.Text = "Проверьте параметры и нажмите «Выполнить»:";
            Assert.False(msg.IsRawProtocol);
            Assert.Equal("Проверьте параметры и нажмите «Выполнить»:", msg.DisplayText);

            // Fenced JSON blocks are protocol too.
            msg.Text = "```json\n{\"action\":\"clear_all\"}";
            msg.IsStreaming = true;
            Assert.True(msg.IsRawProtocol);
        }

        [Fact]
        public void Xaml_AssistantBubbleBindsToDisplayText_AndPlanCardShowsPendingState()
        {
            var xaml = File.ReadAllText(LocateSource("Controls/AiAssistantControl.xaml"));
            var viewModel = File.ReadAllText(LocateSource("ViewModels/AiAssistantViewModel.cs"));

            // Bubble text uses DisplayText (hides raw JSON), not raw Text.
            Assert.Contains("MarkdownRenderer.Text=\"{Binding DisplayText}\"", xaml);
            // The plan card must make the pending state explicit: nothing has
            // been applied until «Выполнить» is pressed.
            Assert.Contains("Действие ещё не выполнено", xaml);
            Assert.Contains("ПОДТВЕРДИТЕ", xaml);
            // The ViewModel replaces the model's past-tense «Добавлено: …» reply
            // with a neutral lead-in while the action awaits confirmation.
            Assert.Contains("Проверьте параметры и нажмите «Выполнить»", viewModel);
            Assert.Contains("msg.Text = ConfirmationLead(plan)", viewModel);
        }

        private static string LocateSource(string relativePath)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "MosquitoNetCalculator", relativePath);
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                $"Could not locate source file '{relativePath}' from '{AppContext.BaseDirectory}'.");
        }
    }
}
