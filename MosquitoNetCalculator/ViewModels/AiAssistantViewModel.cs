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
    public sealed class AiAssistantViewModel : INotifyPropertyChanged
    {
        private readonly Services.AiAssistantService _aiService;
        private string _inputText = string.Empty;
        private bool _isBusy;
        private string _statusText = "Готов к работе";
        private bool _hasApiKey;
        private string _currentModel = "google/gemma-3-27b-it:free";
        private string _apiKeyStatusText = "API ключ не настроен";
        private CancellationTokenSource? _cts;

        // PlanId → message carrying it. Kept for the whole life of the plan so
        // execution results and Undo/Redo can report back to the right bubble.
        private readonly Dictionary<string, AiChatMessage> _planMessages = new();
        private readonly object _planLock = new();

        public ObservableCollection<AiChatMessage> Messages { get; } = new();

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

        public bool CanSend => !IsBusy && !string.IsNullOrWhiteSpace(InputText);
        public bool CanSendOrCancel => CanSend || IsBusy;

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public bool HasApiKey
        {
            get => _hasApiKey;
            private set { _hasApiKey = value; OnPropertyChanged(); }
        }

        public string CurrentModel
        {
            get => _currentModel;
            private set { _currentModel = value; OnPropertyChanged(); }
        }

        public string ApiKeyStatusText
        {
            get => _apiKeyStatusText;
            private set { _apiKeyStatusText = value; OnPropertyChanged(); }
        }

        public event Action<AiCommand>? CommandReceived;

        /// <summary>
        /// Fired when the user confirms a plan (single step or batch). The
        /// handler executes it atomically and reports back via
        /// <see cref="OnPlanExecuted"/>.
        /// </summary>
        public event Action<AiActionPlan>? PlanReceived;

        /// <summary>Fired by local commands «/отменить» and the plan's Undo button.</summary>
        public event Action? UndoRequested;

        /// <summary>Fired by the local command «/повторить».</summary>
        public event Action? RedoRequested;

        public AiAssistantViewModel()
        {
            _aiService = new Services.AiAssistantService();

            CurrentModel = AppSettingsServiceAi.LoadAiModel() ?? "google/gemma-3-27b-it:free";
            var apiKey = AppSettingsServiceAi.LoadAiApiKey();
            var nvidiaKey = AppSettingsServiceAi.LoadAiNvidiaApiKey();
            bool hasUserKey = !string.IsNullOrWhiteSpace(apiKey) || !string.IsNullOrWhiteSpace(nvidiaKey);
            HasApiKey = hasUserKey || Services.AiAssistantService.HasEmbeddedKeys;
            ApiKeyStatusText = hasUserKey
                ? $"API ключ настроен\nМодель: {CurrentModel}"
                : $"Встроенные ключи (бесплатные)\nМодель: {CurrentModel}";

            var history = AppSettingsServiceAi.LoadChatHistory();
            if (history.Count == 0)
            {
                Messages.Add(new AiChatMessage
                {
                    Text = "Здравствуйте! Я AI-ассистент A.R.C. Frame.\n\nЯ могу:\n• Добавлять товары в расчёт по описанию\n• Отвечать на вопросы о программе\n• Помогать с оформлением заказа и подготовкой КП\n• Просчитывать откосы из сэндвича по размерам\n\nПопробуйте: «Сделай сетку Anwis 700х1400 бб60 белую»",
                    IsUser = false,
                    AnimateTyping = true
                });
            }
            else
            {
                foreach (var msg in history)
                    Messages.Add(msg);
            }
        }

        public async Task SendMessageAsync()
        {
            if (!CanSend) return;

            IsBusy = true;
            // The overlay indicator shows pulsing dots + status text in one place.
            // «Думает…» fills the pre-token gap; switches to «Печатает…»
            // when the first token arrives.
            StatusText = "Думает…";

            var userText = InputText.Trim();
            InputText = string.Empty;

            // The service appends userText itself to the request. Snapshot only
            // the conversation that existed before this message to avoid sending
            // the current user text twice in the prompt.
            var conversationHistory = GetConversationHistory();
            Messages.Add(new AiChatMessage { Text = userText, IsUser = true });

            // Local slash commands: instant, offline, zero tokens.
            var orderContext = OrderContextProvider?.Invoke();
            if (AiLocalCommandRouter.TryRoute(
                    userText, orderContext, CurrentModel, AiTelemetryService.Instance.SessionSummary)
                is { IsHandled: true } route)
            {
                // Local commands are instant and synchronous — clear the busy
                // flag here, otherwise the early return would skip the finally
                // block and leave the composer permanently locked.
                IsBusy = false;
                HandleLocalRoute(route, userText);
                var historyToSave = Messages.ToList();
                await Task.Run(() => AppSettingsServiceAi.SaveChatHistory(historyToSave));
                StatusText = "Готово ✓";
                return;
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            // Structured context for both the prompt and telemetry size metrics.
            var orderContextText = orderContext?.ToPromptText() ?? "";
            // The snapshot above was taken on the UI thread before the current
            // message was added. ObservableCollection must not be enumerated from
            // the worker thread.
            var requestToken = _cts.Token;

            // Create a streaming placeholder — NOT added to Messages yet.
            // The empty bubble stays hidden while «Думает…» is visible;
            // it appears only when the first text chunk arrives.
            var streamingMsg = new AiChatMessage
            {
                Text = string.Empty,
                IsUser = false,
                IsStreaming = true
            };
            bool streamingMsgAdded = false;

            var dispatcher = Application.Current?.Dispatcher;
            var pendingText = new StringBuilder();
            var pendingTextLock = new object();
            DispatcherTimer? streamFlushTimer = null;

            // Do not enqueue one Dispatcher callback per SSE chunk: a fast model
            // can produce hundreds of chunks per second and starve WPF input,
            // making the whole application look frozen. Buffer chunks off-thread
            // and publish at most 20 UI updates per second.
            void FlushPendingText()
            {
                string text;
                lock (pendingTextLock)
                {
                    if (pendingText.Length == 0) return;
                    text = pendingText.ToString();
                    pendingText.Clear();
                }
                if (!streamingMsgAdded)
                {
                    streamingMsgAdded = true;
                    Messages.Add(streamingMsg);
                }
                streamingMsg.Text += text;
            }

            void StopStreamFlushTimer()
            {
                if (streamFlushTimer == null) return;
                streamFlushTimer.Stop();
                streamFlushTimer = null;
                FlushPendingText();
            }

            void InvokeOnUi(Action action, bool wait)
            {
                if (dispatcher == null || dispatcher.CheckAccess())
                {
                    action();
                    return;
                }

                if (wait)
                    dispatcher.Invoke(action, DispatcherPriority.Normal);
                else
                    dispatcher.BeginInvoke(action, DispatcherPriority.Background);
            }

            if (dispatcher != null)
            {
                streamFlushTimer = new DispatcherTimer(
                    TimeSpan.FromMilliseconds(50),
                    DispatcherPriority.Background,
                    (_, _) => FlushPendingText(),
                    dispatcher);
                streamFlushTimer.Start();
            }

            // Per-request telemetry (provider/model/attempt/fallback/duration).
            var sw = System.Diagnostics.Stopwatch.StartNew();
            AiStreamInfo? streamInfo = null;

            try
            {
                // Run the network/stream reader on a pool thread. Without this,
                // WPF's SynchronizationContext can resume the service on the UI
                // thread and defeat the throttled UI buffer below.
                await Task.Run(() => _aiService.SendStreamingAsync(
                    userText,
                    conversationHistory,
                    onChunk: chunk =>
                    {
                        if (dispatcher == null || dispatcher.CheckAccess())
                        {
                            streamingMsg.Text += chunk;
                            return;
                        }

                        lock (pendingTextLock)
                            pendingText.Append(chunk);
                    },
                    onModelUsed: modelLabel =>
                    {
                        InvokeOnUi(() =>
                        {
                            streamingMsg.ModelLabel = modelLabel;
                            CurrentModel = modelLabel;
                            ApiKeyStatusText = HasApiKey
                                ? $"API ключ настроен\nМодель: {modelLabel}"
                                : $"Встроенные ключи (бесплатные)\nМодель: {modelLabel}";
                            // Keep the visible request indicator in a clear phase;
                            // provider/model details remain internal telemetry.
                            StatusText = "Печатает…";
                        }, wait: true);
                    },
                    onStreamInfo: info =>
                    {
                        InvokeOnUi(() => streamInfo = info, wait: true);
                    },
                    onDone: fullText =>
                    {
                        // Finalize synchronously on the UI thread so the last
                        // buffered text is visible before markdown/action parsing.
                        // If no chunks arrived, the message was never added —
                        // ensure it appears so «Пустой ответ» is visible.
                        InvokeOnUi(() =>
                        {
                            StopStreamFlushTimer();
                            if (!streamingMsgAdded)
                            {
                                streamingMsgAdded = true;
                                Messages.Add(streamingMsg);
                            }
                            RecordMetrics(sw.ElapsedMilliseconds, succeeded: true, streamInfo,
                                conversationHistory.Count, orderContextText.Length);
                            streamingMsg.MetricsLabel = BuildMetricsLabel(sw.ElapsedMilliseconds, streamInfo);
                            FinalizeStreamingMessage(streamingMsg, fullText);
                        }, wait: true);
                    },
                    onError: errorText =>
                    {
                        InvokeOnUi(() =>
                        {
                            StopStreamFlushTimer();
                            RecordMetrics(sw.ElapsedMilliseconds, succeeded: false, streamInfo,
                                conversationHistory.Count, orderContextText.Length);
                            streamingMsg.MetricsLabel = BuildMetricsLabel(sw.ElapsedMilliseconds, streamInfo);
                            HandleStreamError(streamingMsg, errorText);
                        }, wait: true);
                    },
                    orderContext: orderContextText,
                    ct: requestToken));
            }
            catch (OperationCanceledException)
            {
                InvokeOnUi(StopStreamFlushTimer, wait: true);
                if (streamingMsgAdded && streamingMsg.Text.Length > 0)
                {
                    streamingMsg.IsStreaming = false;
                    streamingMsg.Text += "\n\n*Отменено*";
                }
                else if (streamingMsgAdded)
                {
                    Messages.Remove(streamingMsg);
                }
                StatusText = "Отменено";
            }
            catch (Exception ex)
            {
                InvokeOnUi(StopStreamFlushTimer, wait: true);
                if (streamingMsgAdded)
                    Messages.Remove(streamingMsg);
                Messages.Add(new AiChatMessage
                {
                    Text = $"⚠ Ошибка: {ex.Message}\n\nПроверьте настройки API-ключа и подключение к интернету.",
                    IsUser = false
                });
                StatusText = "Ошибка";
            }
            finally
            {
                IsBusy = false;
                _cts?.Dispose();
                _cts = null;
                // Snapshot on the UI thread before offloading file I/O. The
                // ObservableCollection is bound to WPF and must not be enumerated
                // from a worker thread.
                var historyToSave = Messages.ToList();
                await Task.Run(() => AppSettingsServiceAi.SaveChatHistory(historyToSave));
            }
        }

        /// <summary>
        /// Finalizes a streaming message: parses for commands, sets action badge,
        /// and fires CommandReceived if an action was found.
        /// </summary>
        private void FinalizeStreamingMessage(AiChatMessage msg, string fullText)
        {
            msg.IsStreaming = false;

            if (string.IsNullOrWhiteSpace(fullText))
            {
                msg.Text = "⚠ Пустой ответ от модели. Попробуйте переформулировать запрос.";
                StatusText = "Готово ✓";
                return;
            }

            var (parsed, isValid) = AiCommandParser.TryParse(fullText, fullText);

            if (isValid && parsed.Plan is { Steps.Count: > 0 } plan)
            {
                // Plan-mode reply: every mutating action goes through the
                // preview → confirm → execute pipeline. Read-only/safe plans
                // (list products, calc slope) run immediately.
                msg.Text = parsed.Reply;
                msg.ActionPlan = plan;
                plan.SourceMessageId = msg.MessageId;
                lock (_planLock) _planMessages[plan.PlanId] = msg;

                if (plan.RequiresConfirmation)
                {
                    msg.IsAwaitingConfirmation = true;
                    // The model's own reply can already claim «Добавлено: …»
                    // (past tense) while the action is only proposed. The user
                    // must not read a false confirmation — the plan card is the
                    // source of truth until «Выполнить» is pressed.
                    msg.Text = ConfirmationLead(plan);
                }
                else
                {
                    msg.IsAction = true;
                    msg.ActionSummary = GetPlanSummary(plan);
                    if (plan.Steps.Count == 1)
                        CommandReceived?.Invoke(plan.Steps[0].ToCommand());
                    else
                        PlanReceived?.Invoke(plan);
                }
            }
            else if (isValid && parsed.Action != null)
            {
                // Legacy single-action contract: wrap into the SAME plan pipeline
                // so the confirmation policy is uniform (mutating actions always
                // show a preview; read-only ones run immediately).
                var legacyPlan = AiPlanBuilder.FromCommand(
                    parsed.Action, Messages.LastOrDefault(m => m.IsUser)?.Text, parsed.Reply);
                msg.Text = parsed.Reply;
                msg.ActionPlan = legacyPlan;
                legacyPlan.SourceMessageId = msg.MessageId;
                lock (_planLock) _planMessages[legacyPlan.PlanId] = msg;

                if (legacyPlan.RequiresConfirmation)
                {
                    msg.IsAwaitingConfirmation = true;
                    msg.Text = ConfirmationLead(legacyPlan);
                }
                else
                {
                    msg.IsAction = true;
                    msg.ActionSummary = GetPlanSummary(legacyPlan);
                    CommandReceived?.Invoke(legacyPlan.Steps[0].ToCommand());
                }
            }
            else
            {
                msg.Text = fullText;
                // When the AI asks back for missing parameters («Сделай сетку» →
                // «Уточните: тип, размеры…»), attach an interactive form card so
                // the user can pick values with ComboBoxes instead of typing a
                // second prompt.
                if (AiClarificationForm.LooksLikeClarification(fullText))
                {
                    // Filter the offered products to the family the user asked for:
                    // «Сделай сетку» → only mesh products, not the whole catalog.
                    var lastUserText = Messages.LastOrDefault(m => m.IsUser)?.Text;
                    msg.ClarificationForm = new AiClarificationForm(lastUserText);
                }
            }

            StatusText = "Готово ✓";
        }

        /// <summary>
        /// Handles streaming errors: removes the placeholder if no text was received,
        /// or marks the partial message as errored.
        /// </summary>
        private void HandleStreamError(AiChatMessage msg, string errorText)
        {
            msg.IsStreaming = false;

            if (errorText == "stream_cancelled")
            {
                if (msg.Text.Length > 0)
                    msg.Text += "\n\n*Отменено*";
                else
                    Messages.Remove(msg);
                StatusText = "Отменено";
                return;
            }

            if (msg.Text.Length > 0)
            {
                msg.Text += "\n\n⚠ Соединение прервано.";
            }
            else
            {
                Messages.Remove(msg);
                Messages.Add(new AiChatMessage
                {
                    Text = errorText,
                    IsUser = false
                });
            }

            StatusText = "Ошибка";
        }

        /// <summary>
        /// Called when the user fills the clarification form card attached to
        /// <paramref name="msg"/> and presses «Добавить в расчёт».
        /// Builds an AddItem command from the form values, echoes the selection
        /// as a user message, shows the confirmation bubble and fires
        /// <see cref="CommandReceived"/> — all WITHOUT a second round-trip to
        /// the LLM.
        /// </summary>
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
                    var plan = AiPlanBuilder.FromCommands(route.Commands, userText, route.Message);
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
