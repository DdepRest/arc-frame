using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.ViewModels
{
    public sealed partial class AiAssistantViewModel : INotifyPropertyChanged
    {
        private readonly Services.AiAssistantService _aiService;
        private string _inputText = string.Empty;
        private bool _isBusy;
        private string _statusText = "Готов к работе";
        private string? _ocrWarning;
        private bool _hasApiKey;
        private string _currentModel = Services.AiAssistantService.OpenRouterFreeRouter;
        private string _apiKeyStatusText = "API ключ не настроен";
        private CancellationTokenSource? _cts;

        // _planMessages / _planLock moved to AiAssistantViewModel.Plans.cs partial.

        public ObservableCollection<AiChatMessage> Messages { get; } = new();

        /// <summary>Images staged in the composer for the next message (runtime-only).</summary>
        public ObservableCollection<AiImageAttachment> Attachments { get; } = new();

        public bool HasAttachments => Attachments.Count > 0;

        /// <summary>
        /// Optional provider that returns the structured current order
        /// (<see cref="AiOrderContext"/>). Injected by MainWindow so both the
        /// AI system prompt and the local slash commands see the same data.
        /// </summary>
        public Func<AiOrderContext?>? OrderContextProvider { get; set; }

        /// <summary>
        /// Optional provider for the raw order items (used by «/объясни» to
        /// explain real calculation data without an LLM round-trip).
        /// </summary>
        public Func<IReadOnlyList<OrderItem>>? OrderItemsProvider { get; set; }

        public string InputText
        {
            get => _inputText;
            set
            {
                _inputText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSend));
                OnPropertyChanged(nameof(CanSendOrCancel));
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSend));
                OnPropertyChanged(nameof(CanSendOrCancel));
            }
        }

        public bool CanSend => !IsBusy && (!string.IsNullOrWhiteSpace(InputText) || Attachments.Count > 0);
        public bool CanSendOrCancel => CanSend || IsBusy;

        public void AddAttachment(AiImageAttachment attachment)
        {
            if (attachment == null) return;
            Attachments.Add(attachment);
            OnPropertyChanged(nameof(HasAttachments));
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanSendOrCancel));
        }

        /// <summary>
        /// Stages an image and starts OCR immediately, so the composer warning
        /// appears before the manager hits send (not after). The OCR result is
        /// cached on the attachment and reused by <see cref="SendMessageAsync"/>.
        /// </summary>
        public void AddAttachmentWithOcr(AiImageAttachment attachment)
        {
            AddAttachment(attachment);
            _ = RunOcrAsync(attachment);
        }

        public void RemoveAttachment(AiImageAttachment attachment)
        {
            if (attachment == null || !Attachments.Remove(attachment)) return;
            OnPropertyChanged(nameof(HasAttachments));
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanSendOrCancel));
            RecomputeOcrWarning();
        }

        private void HandleLocalRoute(RouteResult route, string userText)
        {
            switch (route.Kind)
            {
                case RouteKind.Undo:
                    Messages.Add(new AiChatMessage { Text = route.Message, IsUser = false });
                    UndoRequested?.Invoke();
                    break;

                case RouteKind.Redo:
                    Messages.Add(new AiChatMessage { Text = route.Message, IsUser = false });
                    RedoRequested?.Invoke();
                    break;

                case RouteKind.ClearPlan when route.Commands.Count > 0:
                {
                    // Fourth canonical command-building path: the local slash
                    // router («/очистить»). Run the validator before showing the
                    // preview so NeedsClarification is set uniformly. Today
                    // ClearAll is always safe but the policy is the same one
                    // every other path uses.
                    var plan = AiPlanBuilder.FromCommands(route.Commands, userText, route.Message);
                    AiPlanValidator.Validate(plan);
                    var msg = new AiChatMessage
                    {
                        Text = route.Message,
                        IsUser = false,
                        ActionPlan = plan,
                        IsAwaitingConfirmation = true
                    };
                    plan.SourceMessageId = msg.MessageId;
                    Messages.Add(msg);
                    lock (_planLock) _planMessages[plan.PlanId] = msg;
                    break;
                }

                case RouteKind.Explain:
                    if (!string.IsNullOrWhiteSpace(route.Message))
                        Messages.Add(new AiChatMessage { Text = route.Message, IsUser = false });
                    var items = OrderItemsProvider?.Invoke() ?? Array.Empty<OrderItem>();
                    var explanation = route.ExplainTarget switch
                    {
                        ExplainTarget.All => AiExplanationContextBuilder.BuildTextForAll(items),
                        ExplainTarget.Index => AiExplanationContextBuilder.BuildText(items, route.ExplainIndex),
                        _ => AiExplanationContextBuilder.BuildTextForLast(items)
                    };
                    Messages.Add(new AiChatMessage { Text = explanation, IsUser = false });
                    break;

                default:
                    Messages.Add(new AiChatMessage { Text = route.Message, IsUser = false });
                    break;
            }
        }

        private static void RecordMetrics(
            long durationMs, bool succeeded, AiStreamInfo? info, int historySize, int orderContextSize)
        {
            AiTelemetryService.Instance.RecordRequest(new AiRequestMetrics
            {
                Provider = info?.Provider,
                Model = info?.ModelLabel,
                DurationMs = durationMs,
                Succeeded = succeeded,
                Attempt = info?.Attempt,
                FallbackUsed = info?.FallbackUsed ?? false,
                HistorySize = historySize,
                OrderContextSize = orderContextSize
            });
        }

        private static string BuildMetricsLabel(long durationMs, AiStreamInfo? info)
        {
            var parts = new List<string> { $"{durationMs / 1000.0:0.0} с" };
            if (info != null)
            {
                parts.Add($"попытка {info.Attempt}");
                parts.Add(info.FallbackUsed ? "фолбэк" : "без фолбэка");
            }
            return string.Join(" · ", parts);
        }

        /// <summary>
        /// Neutral lead-in shown above the plan card while the action awaits
        /// confirmation. Replaces the model's potentially past-tense reply
        /// («Добавлено: …») so the user knows nothing has been applied yet.
        /// </summary>
        private static string ConfirmationLead(AiActionPlan plan)
        {
            if (plan.Steps.Count == 1)
                return "Проверьте параметры и нажмите «Выполнить», чтобы применить изменение:";
            return $"Проверьте план из {plan.Steps.Count} шагов и нажмите «Выполнить», чтобы применить изменения:";
        }

        private static string GetPlanSummary(AiActionPlan plan)
        {
            if (plan.Steps.Count == 1)
                return GetActionSummary(plan.Steps[0].ToCommand());
            return $"✅ Подтверждено: {plan.Steps.Count} действия";
        }

        private void SaveHistoryQuietly()
        {
            var historyToSave = Messages.ToList();
            Task.Run(() => AppSettingsServiceAi.SaveChatHistory(historyToSave));
        }

        /// <summary>
        /// Called when the user clicks «Повторить с другой моделью» on a
        /// clarification card whose dimensions couldn't be read. Re-sends the
        /// original user request so a different free model gets a chance to
        /// produce a better answer — no retyping needed.
        /// </summary>
        public async Task RetryClarification(AiChatMessage botMsg)
        {
            if (IsBusy) return;
            var text = botMsg.RetryUserText;
            if (string.IsNullOrWhiteSpace(text)) return;

            InputText = text;
            await SendMessageAsync();
        }

        public void Cancel() => _cts?.Cancel();

        public void ClearChat()
        {
            Messages.Clear();
            lock (_planLock) _planMessages.Clear();
            Messages.Add(new AiChatMessage { Text = "Чат очищен. Чем могу помочь?", IsUser = false });
            StatusText = "Готов к работе";
            AppSettingsServiceAi.SaveChatHistory(Array.Empty<AiChatMessage>());
        }

        private List<(string Role, string Content)> GetConversationHistory()
        {
            var history = new List<(string Role, string Content)>();
            foreach (var msg in Messages)
                history.Add((msg.IsUser ? "user" : "assistant", msg.Text));
            return history;
        }

        private static string GetActionSummary(AiCommand command)
        {
            return command.Type switch
            {
                AiCommandType.AddItem => $"➕ {command.Params.Type} {command.Params.Color} {command.Params.Width}×{command.Params.Height} ×{command.Params.Quantity}",
                AiCommandType.DeleteLast => "🗑 Удалена последняя позиция",
                AiCommandType.DeleteItems => "🗑 Удалены позиции по фильтру",
                AiCommandType.ClearAll => "🗑 Очищен весь расчёт",
                AiCommandType.ListProducts => "📋 Список товаров",
                AiCommandType.CalcSlope => $"🏗 Просчёт откосов {command.Params.Width}×{command.Params.Height} мм, глубина {command.Params.Depth} мм, {command.Params.Quantity} отк.",
                _ => "✅ Команда выполнена"
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
