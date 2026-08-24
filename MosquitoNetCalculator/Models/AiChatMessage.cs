using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace MosquitoNetCalculator.Models
{
    /// <summary>Represents a single message in the AI assistant chat.</summary>
    public sealed class AiChatMessage : INotifyPropertyChanged
    {
        private string _text = string.Empty;
        private bool _isStreaming;
        private bool _isAction;
        private string? _actionSummary;
        private string? _modelLabel;
        private AiClarificationForm? _clarificationForm;
        private AiActionPlan? _actionPlan;
        private bool _isAwaitingConfirmation;
        private bool _isExecuted;
        private bool _isCancelled;
        private bool _canUndo;
        private AiExecutionResult? _executionResult;
        private string? _metricsLabel;
        private int _attachmentCount;
        // Runtime-only labels of the images attached to this user message.
        // Filenames like «ПМС Anwis, бел. 1 619x1295.png» carry the whole
        // order inline — when the user pastes a screenshot and forgets to type
        // a caption, PreFillFromRequest would otherwise see empty text and
        // open a blank card. Chat history persists only Text + AttachmentCount;
        // the labels are skipped from serialization ([JsonIgnore]).
        private List<string>? _attachmentLabels;
        // OCR'd text of each attached image. The same screenshot often has a
        // generic file name but its pixels carry the actual order — running
        // Windows.Media.Ocr at send time gives PreFillFromRequest something
        // real to chew on.
        private List<string>? _attachmentOcr;

        public string Text
        {
            get => _text;
            set
            {
                if (_text == value) return;
                _text = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsThinking));
                OnPropertyChanged(nameof(DisplayText));
                OnPropertyChanged(nameof(IsRawProtocol));
            }
        }

        /// <summary>
        /// True while the stream is emitting the raw JSON action block (the
        /// model types «{ "action": … }» token by token). The raw JSON is
        /// transient protocol data — showing it in the chat looks like the AI
        /// is "typing code". The bubble hides it (see <see cref="DisplayText"/>
        /// and the XAML binding) until the reply is parsed into a friendly
        /// confirmation.
        /// </summary>
        [JsonIgnore]
        public bool IsRawProtocol =>
            IsStreaming && LooksLikeRawProtocol(Text);

        /// <summary>
        /// Text actually shown in the bubble. While the stream is emitting raw
        /// JSON (protocol), the bubble shows nothing — the friendly parsed reply
        /// replaces it at finalization.
        /// </summary>
        [JsonIgnore]
        public string DisplayText => IsRawProtocol ? "" : Text;

        private static bool LooksLikeRawProtocol(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.TrimStart();
            return t.StartsWith("{") || t.StartsWith("[")
                   || t.StartsWith("```json", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsUser { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.Now;

        /// <summary>Runtime identity used by plan/regenerate guards.</summary>
        [JsonIgnore]
        public string MessageId { get; } = Guid.NewGuid().ToString("N");

        public bool IsAction
        {
            get => _isAction;
            set { if (_isAction != value) { _isAction = value; OnPropertyChanged(); } }
        }

        public string? ActionSummary
        {
            get => _actionSummary;
            set { if (_actionSummary != value) { _actionSummary = value; OnPropertyChanged(); } }
        }

        /// <summary>Short provider/model badge shown under an assistant reply.</summary>
        public string? ModelLabel
        {
            get => _modelLabel;
            set { if (_modelLabel != value) { _modelLabel = value; OnPropertyChanged(); } }
        }

        /// <summary>True while the SSE stream is still delivering chunks.</summary>
        [JsonIgnore]
        public bool IsStreaming
        {
            get => _isStreaming;
            set
            {
                if (_isStreaming == value) return;
                _isStreaming = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsThinking));
            }
        }

        /// <summary>True before the first non-whitespace streaming token arrives.</summary>
        [JsonIgnore]
        public bool IsThinking => IsStreaming && string.IsNullOrWhiteSpace(Text);

        /// <summary>
        /// Interactive parameter card shown inside the assistant bubble when the
        /// AI replies with a clarification (no command executed). Runtime-only —
        /// never persisted to chat history.
        /// </summary>
        [JsonIgnore]
        public AiClarificationForm? ClarificationForm
        {
            get => _clarificationForm;
            set
            {
                if (ReferenceEquals(_clarificationForm, value)) return;
                _clarificationForm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasClarificationForm));
            }
        }

        /// <summary>True when the message carries an interactive parameter form.</summary>
        [JsonIgnore]
        public bool HasClarificationForm => ClarificationForm != null;

        /// <summary>
        /// Structured action plan attached to an assistant reply. When
        /// <see cref="IsAwaitingConfirmation"/> is true the plan card shows a
        /// preview with «Выполнить»/«Отмена»; nothing touches the order until
        /// the user confirms. Runtime-only — never persisted.
        /// </summary>
        [JsonIgnore]
        public AiActionPlan? ActionPlan
        {
            get => _actionPlan;
            set
            {
                if (ReferenceEquals(_actionPlan, value)) return;
                _actionPlan = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasActionPlan));
                OnPropertyChanged(nameof(ShowPlanCard));
                OnPropertyChanged(nameof(ShowExecutedPlan));
                OnPropertyChanged(nameof(ShowCancelledPlan));
            }
        }

        /// <summary>True when the message carries an action plan.</summary>
        [JsonIgnore]
        public bool HasActionPlan => ActionPlan != null;

        /// <summary>True while the plan card is waiting for the user's decision.</summary>
        [JsonIgnore]
        public bool IsAwaitingConfirmation
        {
            get => _isAwaitingConfirmation;
            set
            {
                if (_isAwaitingConfirmation != value)
                {
                    _isAwaitingConfirmation = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ShowPlanCard));
                }
            }
        }

        /// <summary>True after the plan was executed successfully.</summary>
        [JsonIgnore]
        public bool IsExecuted
        {
            get => _isExecuted;
            set
            {
                if (_isExecuted != value)
                {
                    _isExecuted = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ShowExecutedPlan));
                    OnPropertyChanged(nameof(ShowCancelledPlan));
                }
            }
        }

        /// <summary>True when the plan was rejected/rolled back by the user.</summary>
        [JsonIgnore]
        public bool IsCancelled
        {
            get => _isCancelled;
            set
            {
                if (_isCancelled != value)
                {
                    _isCancelled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ShowCancelledPlan));
                    OnPropertyChanged(nameof(ShowPlanCard));
                }
            }
        }

        /// <summary>Card visible while awaiting the user's decision.</summary>
        [JsonIgnore]
        public bool ShowPlanCard => HasActionPlan && IsAwaitingConfirmation;

        /// <summary>Result strip shown after successful execution.</summary>
        [JsonIgnore]
        public bool ShowExecutedPlan => HasActionPlan && IsExecuted;

        /// <summary>Cancelled/rejected note.</summary>
        [JsonIgnore]
        public bool ShowCancelledPlan => HasActionPlan && IsCancelled && !IsExecuted;

        /// <summary>True when an executed plan can still be undone from the chat.</summary>
        [JsonIgnore]
        public bool CanUndo
        {
            get => _canUndo;
            set { if (_canUndo != value) { _canUndo = value; OnPropertyChanged(); } }
        }

        /// <summary>Outcome reported back after the plan executed.</summary>
        [JsonIgnore]
        public AiExecutionResult? ExecutionResult
        {
            get => _executionResult;
            set { if (!ReferenceEquals(_executionResult, value)) { _executionResult = value; OnPropertyChanged(); } }
        }

        /// <summary>Short telemetry line: duration · attempt · fallback.</summary>
        [JsonIgnore]
        public string? MetricsLabel
        {
            get => _metricsLabel;
            set { if (_metricsLabel != value) { _metricsLabel = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Number of images attached to this user message. Runtime-only — the
        /// base64 payloads are never persisted, only a count for the bubble icon.
        /// </summary>
        [JsonIgnore]
        public int AttachmentCount
        {
            get => _attachmentCount;
            set
            {
                if (_attachmentCount == value) return;
                _attachmentCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasAttachments));
            }
        }

        /// <summary>True when this user message carried at least one image.</summary>
        [JsonIgnore]
        public bool HasAttachments => AttachmentCount > 0;

        /// <summary>Filenames of images attached to this user message (runtime-only).</summary>
        [JsonIgnore]
        public List<string> AttachmentLabels
        {
            get => _attachmentLabels ??= new List<string>();
            set => _attachmentLabels = value;
        }

        /// <summary>OCR'd text of each attached image (runtime-only).</summary>
        [JsonIgnore]
        public List<string> AttachmentOcr
        {
            get => _attachmentOcr ??= new List<string>();
            set => _attachmentOcr = value;
        }

        /// <summary>Plan id shortcut for the plan card bindings.</summary>
        [JsonIgnore]
        public string? PlanId => ActionPlan?.PlanId;

        /// <summary>
        /// Original user request text stored on the bot reply for retry.
        /// When the clarification card has empty dimensions (OCR failed),
        /// the retry button re-sends this exact text to a different model
        /// without requiring the user to retype anything. Runtime-only.
        /// </summary>
        [JsonIgnore]
        public string? RetryUserText { get; set; }

        /// <summary>Read-only shortcut: true when the message is an action that was confirmed and run.</summary>
        [JsonIgnore]
        public bool ShowExecutedActions => IsAction && (IsExecuted || CanUndo);

        /// <summary>Legacy typewriter animation flag for the welcome message.</summary>
        [JsonIgnore]
        public bool AnimateTyping { get; init; } = false;

        /// <summary>Runtime-only guard preventing repeated welcome animation.</summary>
        [JsonIgnore]
        public bool HasAnimated { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        [JsonConstructor]
        public AiChatMessage() { }
    }
}
