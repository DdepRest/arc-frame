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
        private string? _ocrWarning;
        private bool _hasApiKey;
        private string _currentModel = "google/gemma-3-27b-it:free";
        private string _apiKeyStatusText = "API ключ не настроен";
        private CancellationTokenSource? _cts;

        // PlanId → message carrying it. Kept for the whole life of the plan so
        // execution results and Undo/Redo can report back to the right bubble.
        private readonly Dictionary<string, AiChatMessage> _planMessages = new();
        private readonly object _planLock = new();

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

        /// <summary>
        /// OCRs one staged image and refreshes the composer warning when done.
        /// Kept on the calling (UI) thread so WinRT's BitmapDecoder can
        /// initialise; a fire-and-forget await is safe because the send path
        /// re-runs OCR for any image still pending.
        /// </summary>
        private async Task RunOcrAsync(AiImageAttachment attachment)
        {
            var bytes = AttachmentOcrService.TryDecodeDataUrl(attachment.DataUrl);
            if (bytes == null)
            {
                attachment.OcrText = string.Empty;
                attachment.OcrFailureReason = "Не удалось декодировать изображение.";
            }
            else
            {
                var result = await AttachmentOcrService.ExtractAsync(bytes);
                attachment.OcrText = result.Text;
                attachment.OcrFailureReason = result.FailureReason;
            }
            RecomputeOcrWarning();
        }

        /// <summary>
        /// Warns when every staged photo has been OCR'd and none yielded text.
        /// Null <see cref="AiImageAttachment.OcrText"/> means OCR is still
        /// running — no warning until every image has a result.
        /// </summary>
        private void RecomputeOcrWarning()
        {
            if (Attachments.Count == 0 || Attachments.Any(a => a.OcrText == null))
            {
                OcrWarning = null;
                return;
            }
            OcrWarning = BuildOcrWarning(Attachments.Select(a => a.OcrText ?? string.Empty).ToList());
        }

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

        /// <summary>Warning shown in the composer when local OCR couldn't read any attached photo.</summary>
        public string? OcrWarning
        {
            get => _ocrWarning;
            private set
            {
                if (_ocrWarning == value) return;
                _ocrWarning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasOcrWarning));
            }
        }

        public bool HasOcrWarning => !string.IsNullOrWhiteSpace(OcrWarning);

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
            OcrWarning = null;

            var userText = InputText.Trim();
            InputText = string.Empty;

            // Snapshot staged images; they travel with THIS message only
            // (history keeps text, so past attachments are not re-sent).
            var stagedAttachments = Attachments.ToList();
            var imageDataUrls = stagedAttachments.Select(a => a.DataUrl).ToList();
            // Filename capture: managers often paste a screenshot whose file
            // name encodes the order («ПМС Anwis, бел. 1 619x1295.png») instead
            // of typing anything. Pre-fill the clarification card from those
            // labels so the form isn't empty. Kept runtime-only — past chats
            // restored from disk don't carry labels (they were never persisted).
            var attachmentLabels = stagedAttachments.Select(a => a.FileName).ToList();
            // OCR already ran at attach time (AddAttachmentWithOcr). Reuse that
            // result; only re-run for images still pending because the manager
            // sent before the background OCR finished. Windows.Media.Ocr stays
            // on the UI thread — offloading it to Task.Run would fail to
            // initialise BitmapDecoder. The whole step collapses to empty
            // strings if no OCR language pack is installed — the send pipeline
            // must never blow up on OCR failure.
            var ocrLines = new List<string>(stagedAttachments.Count);
            foreach (var attachment in stagedAttachments)
            {
                if (attachment.OcrText != null)
                {
                    ocrLines.Add(attachment.OcrText);
                    continue;
                }
                var bytes = AttachmentOcrService.TryDecodeDataUrl(attachment.DataUrl);
                if (bytes == null) { ocrLines.Add(string.Empty); continue; }
                var result = await AttachmentOcrService.ExtractAsync(bytes);
                ocrLines.Add(result.Text);
            }
            OcrWarning = BuildOcrWarning(ocrLines);
            Attachments.Clear();
            OnPropertyChanged(nameof(HasAttachments));

            // The model only receives the raw image bytes via image_url; a
            // text-only fallback model (or a low-quality photo) would miss the
            // intent entirely. Feed the filename and locally OCR'd text into
            // the prompt too, so ANY model understands an order encoded in a
            // picture — for every product, not just Anwis meshes.
            var modelUserText = BuildModelUserText(userText, attachmentLabels, ocrLines);

            // The service appends the current message (modelUserText) itself to
            // the request. Snapshot only the conversation that existed before
            // this message to avoid sending the current text twice in the prompt.
            var conversationHistory = GetConversationHistory();
            Messages.Add(new AiChatMessage
            {
                Text = userText,
                IsUser = true,
                AttachmentCount = imageDataUrls.Count,
                AttachmentLabels = attachmentLabels,
                AttachmentOcr = ocrLines
            });

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
                    modelUserText,
                    conversationHistory,
                    imageDataUrls: imageDataUrls.Count > 0 ? imageDataUrls : null,
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

            // Use the real user text (not the model's reply) as the plan source
            // and for the local Anwis-mode safety check below. Recent consecutive
            // user messages are merged: managers often send «ПМС Anwis. бел» and
            // «4 739х1116» as two lines/messages.
            var lastUserText = Messages.LastOrDefault(m => m.IsUser)?.Text ?? "";
            var userRequest = GetRecentUserRequest();
            var (parsed, isValid) = AiCommandParser.TryParse(fullText, userRequest);

            // The model can answer an add request by silently inventing critical
            // data — a guessed Anwis profile (ББ60) or dimensions it never saw.
            // Never execute invented data (CONTROL): show the pre-filled
            // clarification card instead, for every product type. The single
            // source of truth for this policy lives in
            // <see cref="AiPlanSafetyPolicy"/>; this VM no longer hard-codes
            // the per-rule checks.
            var parsedCommands = GetParsedCommands(parsed);
            var missing = AiPlanSafetyPolicy.Classify(parsedCommands, userRequest);
            if (isValid && missing != AiPlanSafetyPolicy.MissingField.None)
            {
                var addItem = parsedCommands.FirstOrDefault(c => c.Type == AiCommandType.AddItem);
                msg.Text = AiPlanSafetyPolicy.MissingReasonText(missing);
                // The model already produced concrete AddItem parameters — even
                // when the raw user text doesn't spell them out, the card must
                // come up pre-filled instead of blank. (The guessed Anwis mode
                // is intentionally left out so the user picks it themselves.)
                msg.ClarificationForm = new AiClarificationForm(userRequest, addItem?.Params);
                StatusText = "Готово ✓";
                return;
            }

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
                    parsed.Action, lastUserText, parsed.Reply);
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
                // Prefer the parsed reply over the raw streamed text: it strips
                // the ```json protocol block and surfaces validation overrides
                // («⚠ Для Anwis укажите режим…») instead of the raw action JSON.
                msg.Text = string.IsNullOrWhiteSpace(parsed.Reply) ? fullText : parsed.Reply;

                // When the AI asks back for missing parameters («Сделай сетку» →
                // «Уточните: тип, размеры…»), attach an interactive form card so
                // the user can pick values with ComboBoxes instead of typing a
                // second prompt. An explicit mode=clarification, a clarifying
                // reply text, or an incomplete Anwis add request all attach it.
                if (parsed.Mode == AiPlanMode.Clarification
                    || AiClarificationForm.ShouldShowForm(userRequest, msg.Text))
                {
                    // Filter the offered products to the family the user asked for:
                    // «Сделай сетку» → only mesh products, not the whole catalog.
                    // Pre-fill from any parsed AddItem params too, so values the
                    // model recovered from an image/context aren't dropped.
                    msg.ClarificationForm = new AiClarificationForm(
                        userRequest, GetKnownAddItemParams(parsedCommands), msg.Text);
                }
            }

            StatusText = "Готово ✓";
        }

        /// <summary>Flattens a parsed response into commands (plan steps or legacy action).</summary>
        private static IReadOnlyList<AiCommand> GetParsedCommands(AiResponse parsed)
        {
            if (parsed.Plan is { } plan)
                return plan.Steps.Select(s => s.ToCommand()).ToList();
            if (parsed.Action is { } action)
                return new[] { action };
            return Array.Empty<AiCommand>();
        }

        /// <summary>First AddItem command's params, or null — the richest pre-fill source.</summary>
        private static AiCommandParams? GetKnownAddItemParams(IReadOnlyList<AiCommand> commands)
            => commands.FirstOrDefault(c => c.Type == AiCommandType.AddItem)?.Params;

        /// <summary>
        /// Text of the current user turn: the last user message plus any user
        /// messages typed immediately before it (no assistant reply between
        /// them). Managers often split one request across two sends
        /// («ПМС Anwis. бел» then «4 739х1116») — the clarification card must
        /// pre-fill from all of them, not just the last one.
        /// </summary>
        private string GetRecentUserRequest()
        {
            int lastUser = -1;
            for (int i = Messages.Count - 1; i >= 0; i--)
            {
                if (Messages[i].IsUser) { lastUser = i; break; }
            }
            if (lastUser < 0) return string.Empty;

            // Walk back over consecutive user messages, merging typed text,
            // attachment filenames, and OCR'd image text. This way pasting
            // «Снимок.PNG» (no useful name, no caption) still opens the
            // clarification card pre-filled from the pixels of the picture.
            var parts = new List<string>();
            for (int i = lastUser; i >= 0 && Messages[i].IsUser; i--)
            {
                parts.Add(Messages[i].Text);
                foreach (var label in Messages[i].AttachmentLabels)
                    parts.Add(label);
                foreach (var ocr in Messages[i].AttachmentOcr)
                    if (!string.IsNullOrWhiteSpace(ocr)) parts.Add(ocr);
            }
            parts.Reverse();
            return string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        /// <summary>
        /// Merges the typed text with attachment filenames and locally OCR'd
        /// image text into the single prompt string the model sees. The image
        /// bytes travel separately as an <c>image_url</c> part; this text form
        /// guarantees even a text-only fallback model still understands the
        /// order encoded in a picture.
        /// </summary>
        internal static string BuildModelUserText(
            string userText,
            IReadOnlyList<string> attachmentLabels,
            IReadOnlyList<string> ocrLines)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(userText))
                parts.Add(userText);
            if (attachmentLabels != null)
                foreach (var label in attachmentLabels)
                    if (!string.IsNullOrWhiteSpace(label))
                        parts.Add($"Файл: {label}");
            if (ocrLines != null)
                foreach (var ocr in ocrLines)
                    if (!string.IsNullOrWhiteSpace(ocr))
                        parts.Add($"Текст с картинки: {ocr}");
            return string.Join("\n", parts);
        }

        /// <summary>
        /// Returns a composer warning when every attached photo came back with no
        /// OCR text (no language pack, a real photo the engine can't read, etc.).
        /// The vision model may still have seen the image, but the manager should
        /// know the text fallback had nothing to work with.
        /// </summary>
        internal static string? BuildOcrWarning(IReadOnlyList<string> ocrLines)
            => ocrLines is { Count: > 0 } && ocrLines.All(string.IsNullOrWhiteSpace)
                ? "⚠ Не удалось распознать текст на фото — опишите параметры текстом."
                : null;

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
