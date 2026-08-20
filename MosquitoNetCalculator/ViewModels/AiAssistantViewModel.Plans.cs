using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.ViewModels
{
    /// <summary>
    /// Stage-3 (REFACTORING_PLAN_BIG_FILES.md §4 Фаза D):
    /// plan-lifecycle partial of <see cref="AiAssistantViewModel"/>.
    /// Owns the plan dictionary + the user-driven plan actions
    /// (Submit/Confirm/Cancel/Undo) and the executor callbacks.
    /// The streaming-side partial handles parse + safety policy.
    /// </summary>
    public sealed partial class AiAssistantViewModel
    {
        private readonly Dictionary<string, AiChatMessage> _planMessages = new();
        private readonly object _planLock = new();

        public void SubmitClarificationForm(AiChatMessage msg)
        {
            if (msg.ClarificationForm is not { } form) return;

            if (!form.TryBuildCommand(out var command, out var error))
            {
                // Show the validation problem inline instead of a silent no-op.
                Messages.Add(new AiChatMessage
                {
                    Text = error ?? "⚠ Проверьте заполненные параметры.",
                    IsUser = false
                });
                return;
            }

            // «Don't invent» audit-trail: after the form succeeds the command
            // is safe by construction, but we still run the safety policy
            // exactly once through the central source so a regression in
            // form validation cannot slip through silently. Lock the path
            // with a test; if the assertion ever fires the form is leaking
            // an unsafe command into the preview pipeline.
            var builtCommands = new[] { command! };
            var leftover = AiPlanSafetyPolicy.Classify(builtCommands, form.BuildSummaryText());
            if (leftover != AiPlanSafetyPolicy.MissingField.None)
            {
                Messages.Add(new AiChatMessage
                {
                    Text = "⚠ Внутренняя проверка: форма выпустила небезопасную команду. " +
                           AiPlanSafetyPolicy.MissingReasonText(leftover),
                    IsUser = false
                });
                return;
            }

            // Hide the form card — the plan preview bubble replaces it.
            msg.ClarificationForm = null;

            // Echo the user's selection as a normal user message.
            Messages.Add(new AiChatMessage
            {
                Text = form.BuildSummaryText(),
                IsUser = true
            });

            // Plan preview. Nothing touches the order until the user confirms —
            // the plan card shows the exact parameters with «Выполнить»/«Отмена».
            var plan = AiPlanBuilder.FromCommand(
                command!,
                sourceUserText: Messages.LastOrDefault(m => m.IsUser)?.Text,
                reply: "Проверьте параметры и нажмите «Выполнить».");
            // Run the validator so NeedsClarification / MissingField flags are
            // freshly computed on the just-built plan (third canonical path
            // through the policy, alongside plan-mode and finalization).
            AiPlanValidator.Validate(plan);
            var confirm = new AiChatMessage
            {
                Text = plan.ReplyText,
                IsUser = false,
                ActionPlan = plan,
                IsAwaitingConfirmation = true
            };
            plan.SourceMessageId = confirm.MessageId;
            Messages.Add(confirm);
            lock (_planLock) _planMessages[plan.PlanId] = confirm;

            // Persist like a normal exchange (fire-and-forget file I/O).
            var historyToSave = Messages.ToList();
            Task.Run(() => AppSettingsServiceAi.SaveChatHistory(historyToSave));
        }

        /// <summary>
        /// User pressed «Выполнить» on the plan card. Guards against double
        /// execution (regenerate safety) and fires <see cref="PlanReceived"/>.
        /// </summary>
        public void ConfirmPlan(AiChatMessage msg)
        {
            if (msg.ActionPlan is not { } plan) return;
            // Double-execution guard (regenerate safety): once the card left the
            // awaiting state it can never be confirmed again.
            if (!msg.IsAwaitingConfirmation || msg.IsExecuted || msg.IsCancelled
                || plan.Status is AiPlanStatus.Executed or AiPlanStatus.Executing)
                return;

            msg.IsAwaitingConfirmation = false;
            plan.Status = AiPlanStatus.AwaitingConfirmation;
            msg.IsAction = true;
            msg.ActionSummary = GetPlanSummary(plan);
            PlanReceived?.Invoke(plan);
        }

        /// <summary>User pressed «Отмена» on the plan card — nothing executes.</summary>
        public void CancelPlan(AiChatMessage msg)
        {
            if (msg.ActionPlan is not { } plan) return;
            plan.Status = AiPlanStatus.Cancelled;
            msg.IsAwaitingConfirmation = false;
            msg.IsCancelled = true;
            msg.ActionSummary = "Отменено";
            SaveHistoryQuietly();
        }

        /// <summary>
        /// Reported back by the plan executor (MainWindow) after the plan ran.
        /// Updates the plan card bubble with the outcome and enables Undo.
        /// </summary>
        public void OnPlanExecuted(string planId, AiExecutionResult result)
        {
            AiChatMessage? msg;
            lock (_planLock)
            {
                if (!_planMessages.TryGetValue(planId, out msg)) return;
            }

            msg.ExecutionResult = result;
            if (msg.ActionPlan is { } plan)
                plan.Status = result.Success ? AiPlanStatus.Executed : (result.RolledBack ? AiPlanStatus.RolledBack : AiPlanStatus.Failed);
            msg.IsExecuted = result.Success;
            msg.CanUndo = result.Success && msg.ActionPlan is { IsReadOnly: false };
            msg.IsAction = true;
            msg.ActionSummary = result.Success
                ? result.Summary
                : $"⚠ {result.Error ?? result.Summary}";
            // Replace the «Проверьте параметры…» lead (which was written while
            // the card awaited confirmation) with the actual outcome.
            msg.Text = result.Success
                ? $"✅ Готово: {result.Summary}"
                : $"⚠ Не удалось применить: {result.Error ?? result.Summary}";
            StatusText = result.Success ? "Готово ✓" : "Ошибка";
        }

        /// <summary>User pressed «Отменить действие» on an executed plan card.</summary>
        public void RequestUndo(AiChatMessage msg)
        {
            if (!msg.CanUndo || msg.ActionPlan is not { } plan) return;
            msg.CanUndo = false;
            msg.ActionSummary = "↩ Отменяю действие AI…";
            UndoRequested?.Invoke();
        }

        /// <summary>
        /// MainWindow reports that a safe undo is impossible (manual edits
        /// happened after the AI action) — hide the button and explain why.
        /// </summary>
        public void OnPlanUndoBlocked(string planId)
        {
            AiChatMessage? msg;
            lock (_planLock)
            {
                if (!_planMessages.TryGetValue(planId, out msg)) return;
                _planMessages.Remove(planId);
            }
            msg.CanUndo = false;
            msg.ActionSummary = "↩ Отмена AI недоступна: после действия были другие изменения. Используйте Ctrl+Z.";
        }

        /// <summary>MainWindow confirms the AI undo was performed.</summary>
        public void OnPlanUndone(string planId)
        {
            AiChatMessage? msg;
            lock (_planLock)
            {
                if (!_planMessages.TryGetValue(planId, out msg)) return;
                _planMessages.Remove(planId);
            }
            msg.CanUndo = false;
            msg.IsExecuted = false;
            msg.IsCancelled = true;
            msg.ActionSummary = "↩ Действие AI отменено";
            SaveHistoryQuietly();
        }

        /// <summary>
        /// Executes a locally-routed slash command: adds the user message and
        /// the assistant reply, fires Undo/Redo events, builds plans for
        /// mutating commands and explanations for «/объясни». No network, no
        /// tokens.
        /// </summary>
    }
}
