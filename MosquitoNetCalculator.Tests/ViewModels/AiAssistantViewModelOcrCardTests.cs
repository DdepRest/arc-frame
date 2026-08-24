using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.ViewModels;
using Xunit;

namespace MosquitoNetCalculator.Tests.ViewModels
{
    /// <summary>
    /// Regression: attaching a photo whose text local OCR already read must
    /// NEVER end in a blank clarification card. Even when the (possibly weak)
    /// model answers with meaningless prose instead of a structured add_item,
    /// the card is shown pre-filled from the OCR text so the manager keeps the
    /// already-known data (type, color, dimensions, quantity).
    /// </summary>
    public sealed class AiAssistantViewModelOcrCardTests
    {
        private static void SetPrivateField(object target, string name, object value)
        {
            var field = typeof(AiAssistantViewModel).GetField(
                name, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"Field {name} not found");
            field.SetValue(target, value);
        }

        private static void Finalize(AiAssistantViewModel vm, AiChatMessage msg, string reply)
        {
            var finalize = typeof(AiAssistantViewModel).GetMethod(
                "FinalizeStreamingMessage", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("FinalizeStreamingMessage not found");
            finalize.Invoke(vm, new object[] { msg, reply });
        }

        [Fact]
        public void ImageWithOcr_ModelAnswersUselessText_CardStillShownPrefilled()
        {
            // Exactly the user report: photo attached, OCR read «ПМС Anwis, бел.
            // 1 371x1217», but the model replied with meaningless prose (no JSON).
            // The card must appear pre-filled, not blank.
            var vm = new AiAssistantViewModel();
            // Reproduces SendMessageAsync's fallback: no typed text → OCR text is
            // used as the user bubble text, so the form pre-fills from it.
            vm.Messages.Add(new AiChatMessage
            {
                Text = "ПМС Anwis, бел. 1 371x1217",
                IsUser = true,
                AttachmentOcr = new List<string> { "ПМС Anwis, бел. 1 371x1217" }
            });

            SetPrivateField(vm, "_currentTurnHadImage", true);
            SetPrivateField(vm, "_currentTurnHadOcr", true);

            const string modelReply = "Опишите, пожалуйста, параметры подробнее.";
            var assistantMsg = new AiChatMessage { Text = modelReply, IsUser = false, IsStreaming = true };
            Finalize(vm, assistantMsg, modelReply);

            Assert.NotNull(assistantMsg.ClarificationForm);
            var form = assistantMsg.ClarificationForm!;
            Assert.Equal("Anwis", form.SelectedType);
            Assert.Equal("Белый", form.SelectedColor);
            Assert.Equal("371", form.WidthText);
            Assert.Equal("1217", form.HeightText);
            Assert.Equal("1", form.QuantityText);
        }

        [Fact]
        public void ImageWithCompactOcr_ModelConfirmsPair_UserBubbleShowsSplitDimension()
        {
            // Raw OCR glued the width/height («3711217»); the bubble must NOT
            // start with the digit soup — it stays empty until the model's reply
            // confirms the exact pair «371×1217», then fills with the readable
            // form. No guessing.
            var vm = new AiAssistantViewModel();
            vm.Messages.Add(new AiChatMessage
            {
                Text = "", // hidden: glued OCR without a confirmed separator
                IsUser = true,
                AttachmentCount = 1,
                AttachmentOcr = new List<string> { "ПМС Anwis, бел. 1 3711217" }
            });

            SetPrivateField(vm, "_currentTurnHadImage", true);
            SetPrivateField(vm, "_currentTurnHadOcr", true);

            const string modelReply =
                "Уточните режим для сетки Anwis 371×1217 мм: ББ60, ББ70, ПП, Проём или Габарит?";
            var assistantMsg = new AiChatMessage { Text = modelReply, IsUser = false, IsStreaming = true };
            Finalize(vm, assistantMsg, modelReply);

            var userBubble = vm.Messages.Last(m => m.IsUser);
            Assert.Contains("371×1217", userBubble.Text);
            Assert.DoesNotContain("3711217", userBubble.Text);
            Assert.Contains("371×1217", userBubble.AttachmentOcr[0]);

            // Card itself still has the split dimensions.
            Assert.NotNull(assistantMsg.ClarificationForm);
            Assert.Equal("371", assistantMsg.ClarificationForm!.WidthText);
            Assert.Equal("1217", assistantMsg.ClarificationForm.HeightText);
        }

        [Fact]
        public void ImageWithCompactOcr_ModelDoesNotConfirmPair_BubbleStaysHidden()
        {
            // No independent source confirms the pair (the reply names no
            // dimensions) → the glued OCR must never surface in the bubble.
            var vm = new AiAssistantViewModel();
            vm.Messages.Add(new AiChatMessage
            {
                Text = "",
                IsUser = true,
                AttachmentCount = 1,
                AttachmentOcr = new List<string> { "ПМС Anwis, бел. 1 3711217" }
            });

            SetPrivateField(vm, "_currentTurnHadImage", true);
            SetPrivateField(vm, "_currentTurnHadOcr", true);

            const string modelReply = "Уточните, пожалуйста, размеры сетки.";
            var assistantMsg = new AiChatMessage { Text = modelReply, IsUser = false, IsStreaming = true };
            Finalize(vm, assistantMsg, modelReply);

            var userBubble = vm.Messages.Last(m => m.IsUser);
            Assert.Equal("", userBubble.Text);
            Assert.Equal("ПМС Anwis, бел. 1 3711217", userBubble.AttachmentOcr[0]);
        }

        [Fact]
        public void CompactOcrEchoedByModel_AssistantHeaderSanitized_NoRawDigits()
        {
            // Exact scenario from the user screenshot: weak model echoes the
            // raw OCR input verbatim («ПМС Anwis, бел. 1 3711217»). The
            // assistant reply header must NOT display the raw compact digits —
            // the card already shows the pre-filled known values; dimensions
            // that can't be split stay empty for manual entry.
            var vm = new AiAssistantViewModel();
            vm.Messages.Add(new AiChatMessage
            {
                Text = "",
                IsUser = true,
                AttachmentCount = 1,
                AttachmentOcr = new List<string> { "ПМС Anwis, бел. 1 3711217" }
            });

            SetPrivateField(vm, "_currentTurnHadImage", true);
            SetPrivateField(vm, "_currentTurnHadOcr", true);

            const string modelReply = "ПМС Anwis, бел. 1 3711217";
            var assistantMsg = new AiChatMessage { Text = modelReply, IsUser = false, IsStreaming = true };
            Finalize(vm, assistantMsg, modelReply);

            // Header must NOT contain the raw compact digit run.
            Assert.DoesNotContain("3711217", assistantMsg.Text);
            Assert.Equal("Уточните параметры:", assistantMsg.Text);
            // Card is still shown with known pre-filled values.
            Assert.NotNull(assistantMsg.ClarificationForm);
            var form = assistantMsg.ClarificationForm!;
            Assert.Equal("Anwis", form.SelectedType);
            Assert.Equal("Белый", form.SelectedColor);
            Assert.Equal("1", form.QuantityText);
            // Dimensions are empty — no confirmed separator, no guessing.
            Assert.True(string.IsNullOrEmpty(form.WidthText));
            Assert.True(string.IsNullOrEmpty(form.HeightText));
        }

        [Fact]
        public void ConfirmedPairInModelReply_AssistantHeaderKeepsReadableDimensions()
        {
            // When the model's reply contains a readable dimension pair
            // («371×1217»), the header is NOT sanitized — ShouldHideOcrFromBubble
            // returns false because DimensionRegex matches.
            var vm = new AiAssistantViewModel();
            vm.Messages.Add(new AiChatMessage
            {
                Text = "",
                IsUser = true,
                AttachmentCount = 1,
                AttachmentOcr = new List<string> { "ПМС Anwis, бел. 1 3711217" }
            });

            SetPrivateField(vm, "_currentTurnHadImage", true);
            SetPrivateField(vm, "_currentTurnHadOcr", true);

            const string modelReply =
                "Уточните режим для сетки Anwis 371×1217 мм: ББ60, ББ70, ПП, Проём или Габарит?";
            var assistantMsg = new AiChatMessage { Text = modelReply, IsUser = false, IsStreaming = true };
            Finalize(vm, assistantMsg, modelReply);

            // Confirmed pair — header keeps the model's readable text.
            Assert.Contains("371×1217", assistantMsg.Text);
            Assert.DoesNotContain("3711217", assistantMsg.Text);
            Assert.NotNull(assistantMsg.ClarificationForm);
        }

        [Fact]
        public void ImageWithoutOcr_ModelAnswersUselessText_CardStillShown()
        {
            // OCR produced nothing (no text on the photo / unreadable). The card
            // must STILL be attached — otherwise the manager gets only a useless
            // AI question and no way to enter the values interactively.
            var vm = new AiAssistantViewModel();
            vm.Messages.Add(new AiChatMessage
            {
                Text = "",
                IsUser = true,
                AttachmentOcr = new List<string> { string.Empty },
                AttachmentCount = 1
            });

            SetPrivateField(vm, "_currentTurnHadImage", true);
            SetPrivateField(vm, "_currentTurnHadOcr", false);

            const string modelReply = "Не удалось распознать данные. Уточните параметры.";
            var assistantMsg = new AiChatMessage { Text = modelReply, IsUser = false, IsStreaming = true };
            Finalize(vm, assistantMsg, modelReply);

            // Card is present; fields are defaults (nothing known) — the manager
            // fills them manually.
            Assert.NotNull(assistantMsg.ClarificationForm);
        }

        [Fact]
        public void ImageWithOcr_ModelReturnsEmptyClarification_CardStillShownPrefilled()
        {
            // Even a structured-but-empty clarification (no params) must not lose
            // the OCR-known values.
            var vm = new AiAssistantViewModel();
            vm.Messages.Add(new AiChatMessage
            {
                Text = "ПМС Anwis, бел. 1 371x1217",
                IsUser = true,
                AttachmentOcr = new List<string> { "ПМС Anwis, бел. 1 371x1217" }
            });

            SetPrivateField(vm, "_currentTurnHadImage", true);
            SetPrivateField(vm, "_currentTurnHadOcr", true);

            const string modelReply = "Какой режим Anwis использовать? ББ60, ББ70, ПП, Проём или Габарит?";
            var assistantMsg = new AiChatMessage { Text = modelReply, IsUser = false, IsStreaming = true };
            Finalize(vm, assistantMsg, modelReply);

            Assert.NotNull(assistantMsg.ClarificationForm);
            var form = assistantMsg.ClarificationForm!;
            Assert.Equal("Anwis", form.SelectedType);
            Assert.Equal("Белый", form.SelectedColor);
            Assert.Equal("371", form.WidthText);
            Assert.Equal("1217", form.HeightText);
            Assert.Equal("1", form.QuantityText);
        }

        [Fact]
        public void ClarificationForm_HasEmptyDimensions_TrueWhenBothFieldsEmpty()
        {
            var form = new AiClarificationForm("сетка Anwis белая");
            Assert.True(form.HasEmptyDimensions);

            form.WidthText = "700";
            Assert.False(form.HasEmptyDimensions);

            form.WidthText = "";
            form.HeightText = "1400";
            Assert.False(form.HasEmptyDimensions);

            form.WidthText = "700";
            form.HeightText = "1400";
            Assert.False(form.HasEmptyDimensions);
        }

        [Fact]
        public void ImageWithCompactOcr_DimensionsNotConfirmed_RetryUserTextStored()
        {
            // When OCR reads digits without a separator and the model doesn't
            // confirm them, the card has empty dimensions. RetryUserText must be
            // stored so the retry button can re-send the original request.
            var vm = new AiAssistantViewModel();
            vm.Messages.Add(new AiChatMessage
            {
                Text = "",
                IsUser = true,
                AttachmentCount = 1,
                AttachmentOcr = new List<string> { "ПМС Anwis, бел. 1 3711217" }
            });

            SetPrivateField(vm, "_currentTurnHadImage", true);
            SetPrivateField(vm, "_currentTurnHadOcr", true);

            const string modelReply = "Уточните, пожалуйста, размеры сетки.";
            var assistantMsg = new AiChatMessage { Text = modelReply, IsUser = false, IsStreaming = true };
            Finalize(vm, assistantMsg, modelReply);

            // Form has empty dimensions — the compact OCR wasn't confirmed.
            Assert.NotNull(assistantMsg.ClarificationForm);
            Assert.True(assistantMsg.ClarificationForm!.HasEmptyDimensions);

            // RetryUserText carries the original user request for the retry button.
            Assert.NotNull(assistantMsg.RetryUserText);
            Assert.Contains("3711217", assistantMsg.RetryUserText);
        }

        [Fact]
        public void FromReply_FillsDimensions_WhenModelAnswerContainsConfirmedPair()
        {
            // The model replied with a readable pair («371×1217»), but the
            // OCR/user text only has compact digits or no dimensions at all.
            // FromReply must harvest the confirmed pair and fill the form fields
            // so the user doesn't have to retype.
            var form = new AiClarificationForm(
                "ПМС Anwis, бел. 1 3711217",  // userRequest — compact, no match
                knownParams: null,
                replyText: "Уточните режим для сетки Anwis 371×1217 мм: ББ60, ББ70, ПП, Проём или Габарит?");

            Assert.Equal("371", form.WidthText);
            Assert.Equal("1217", form.HeightText);
            Assert.Equal("Anwis", form.SelectedType);
            Assert.Equal("Белый", form.SelectedColor);
        }

        [Fact]
        public void FromReply_DoesNotFillDimensions_WhenReplyHasOnlyCompactDigits()
        {
            // The model echoed the raw OCR digit soup — no confirmed separator.
            // FromReply must NOT split «3711217» into guessed dimensions.
            var form = new AiClarificationForm(
                "ПМС Anwis, бел. 1 3711217",
                knownParams: null,
                replyText: "ПМС Anwis, бел. 1 3711217");

            Assert.True(string.IsNullOrEmpty(form.WidthText));
            Assert.True(string.IsNullOrEmpty(form.HeightText));
            Assert.Equal("Белый", form.SelectedColor);  // color still pre-filled
            Assert.Equal("1", form.QuantityText);       // quantity still pre-filled
        }
    }
}