using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// Lifecycle of an <see cref="AiActionPlan"/> (and each of its steps).
    /// </summary>
    public enum AiPlanStatus
    {
        Draft,
        NeedsClarification,
        ReadyForPreview,
        AwaitingConfirmation,
        Executing,
        Executed,
        Cancelled,
        Failed,
        RolledBack
    }

    /// <summary>
    /// High-level mode of an AI reply. Mirrors the JSON contract the model is
    /// asked to produce: a plain answer, a clarification request, a plan of
    /// actions, or an explanation of an existing calculation.
    /// </summary>
    public enum AiPlanMode
    {
        Answer,
        Clarification,
        Plan,
        Explanation
    }

    /// <summary>
    /// One unit of work inside an <see cref="AiActionPlan"/>. Wraps a single
    /// <see cref="AiCommand"/> plus plan-time bookkeeping (preview text,
    /// validation messages, per-step status, inline-editable properties).
    /// </summary>
    public sealed class AiActionStep : INotifyPropertyChanged
    {
        private string _previewText = "";
        private bool _hasDiscrepancy;
        private string? _discrepancyWarning;

        public string StepId { get; } = Guid.NewGuid().ToString("N");

        public AiCommandType CommandType { get; init; }

        public AiCommandParams Params { get; init; } = new();

        /// <summary>Short human-readable description shown in the plan card.</summary>
        public string PreviewText
        {
            get => string.IsNullOrWhiteSpace(_previewText) ? BuildDynamicPreviewText() : _previewText;
            init => _previewText = value;
        }

        public bool IsItemStep => CommandType is AiCommandType.AddItem or AiCommandType.UpdateItemByIndex;

        public string ItemType
        {
            get => Params.Type;
            set
            {
                if (Params.Type == value) return;
                Params.Type = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsAnwis));
                OnPropertyChanged(nameof(AvailableColors));
                OnPropertyChanged(nameof(HasColorChoices));
                RefreshPreview();
            }
        }

        public IReadOnlyList<string> AvailableColors
        {
            get
            {
                var colors = Services.AiFactsProvider.GetColorsFor(ItemType);
                if (colors.Count == 0) return new[] { "Белый", "Коричневый", "Антрацит" };
                return colors;
            }
        }

        public bool HasColorChoices => AvailableColors.Count > 0;

        public int Width
        {
            get => Params.Width;
            set
            {
                if (Params.Width == value) return;
                Params.Width = value;
                OnPropertyChanged();
                RefreshPreview();
            }
        }

        public int Height
        {
            get => Params.Height;
            set
            {
                if (Params.Height == value) return;
                Params.Height = value;
                OnPropertyChanged();
                RefreshPreview();
            }
        }

        public string Color
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Params.Color) && IsItemStep)
                {
                    var def = Services.AiFactsProvider.GetDefaultColor(ItemType);
                    return !string.IsNullOrWhiteSpace(def) ? def : "Белый";
                }
                return Params.Color;
            }
            set
            {
                if (Params.Color == value) return;
                Params.Color = value;
                OnPropertyChanged();
                RefreshPreview();
            }
        }

        public double Quantity
        {
            get => Params.Quantity;
            set
            {
                if (Math.Abs(Params.Quantity - value) < 0.001) return;
                Params.Quantity = value;
                OnPropertyChanged();
                RefreshPreview();
            }
        }

        public AnwisSizeMode AnwisMode
        {
            get => Params.AnwisMode;
            set
            {
                if (Params.AnwisMode == value) return;
                Params.AnwisMode = value;
                OnPropertyChanged();
                RefreshPreview();
            }
        }

        public int InstallationMode
        {
            get => Params.InstallationMode;
            set
            {
                if (Params.InstallationMode == value) return;
                Params.InstallationMode = value;
                OnPropertyChanged();
                RefreshPreview();
            }
        }

        public bool IsAnwis => (Params.Type?.Contains("anwis", StringComparison.OrdinalIgnoreCase) ?? false) ||
                               (Params.Type?.Contains("анвис", StringComparison.OrdinalIgnoreCase) ?? false);

        public bool HasDiscrepancy
        {
            get => _hasDiscrepancy;
            set
            {
                if (_hasDiscrepancy == value) return;
                _hasDiscrepancy = value;
                OnPropertyChanged();
            }
        }

        public string? DiscrepancyWarning
        {
            get => _discrepancyWarning;
            set
            {
                if (_discrepancyWarning == value) return;
                _discrepancyWarning = value;
                OnPropertyChanged();
            }
        }

        public string ItemHeader
        {
            get
            {
                if (CommandType == AiCommandType.UpdateItemByIndex)
                    return $"#{Params.ItemIndex}: {Params.Type}";
                return Params.Type;
            }
        }

        public bool IsAnticat => Params.IsAnticat == true;

        public string AnwisModeLabel => Services.AiKeywordLexicon.AnwisModeLabel(Params.AnwisMode);

        public string InstallationLabel => Params.InstallationMode switch
        {
            0 => "С монтажом",
            1 => "Без монтажа",
            2 => "В конструкцию",
            _ => ""
        };

        public bool HasInstallationLabel => !string.IsNullOrEmpty(InstallationLabel);

        private void RefreshPreview()
        {
            _previewText = BuildDynamicPreviewText();
            OnPropertyChanged(nameof(PreviewText));
            OnPropertyChanged(nameof(ItemHeader));
            OnPropertyChanged(nameof(IsAnticat));
            OnPropertyChanged(nameof(AnwisModeLabel));
            OnPropertyChanged(nameof(InstallationLabel));
            OnPropertyChanged(nameof(HasInstallationLabel));
        }

        private string BuildDynamicPreviewText()
        {
            if (CommandType == AiCommandType.AddItem)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"Добавить {Params.Type}");
                if (!string.IsNullOrWhiteSpace(Params.Color)) sb.Append($", {Params.Color}");
                if (Params.Width > 0 && Params.Height > 0) sb.Append($", {Params.Width}×{Params.Height} мм");
                if (Params.Quantity > 1) sb.Append($" ({Params.Quantity} шт)");
                if (IsAnwis && Params.AnwisMode != AnwisSizeMode.РазмерПроёма) sb.Append($" [{Params.AnwisMode}]");
                if (Params.InstallationMode == 0) sb.Append(" с монтажом");
                return sb.ToString();
            }
            return _previewText;
        }

        /// <summary>Local validation messages (empty when the step is valid).</summary>
        public List<string> ValidationMessages { get; } = new();

        public AiPlanStatus Status { get; set; } = AiPlanStatus.Draft;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>Converts the step back to an executable command.</summary>
        public AiCommand ToCommand() => new() { Type = CommandType, Params = Params };
    }

    /// <summary>
    /// Structured, locally-validated description of what the AI wants to do to
    /// the order. Every mutating request is turned into a plan BEFORE any
    /// change reaches the calculation: the plan is validated against the
    /// catalog, shown to the user as a preview, confirmed, executed atomically
    /// and (optionally) rolled back as a whole.
    ///
    /// Runtime-only UI state: never persisted into chat history.
    /// </summary>
    public sealed class AiActionPlan
    {
        public string PlanId { get; } = Guid.NewGuid().ToString("N");

        /// <summary>Id of the request this plan was produced from (regenerate guard).</summary>
        public string RequestId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>Id of the chat message that carries this plan (regenerate guard).</summary>
        public string? SourceMessageId { get; set; }

        public string SourceUserText { get; init; } = "";

        /// <summary>The assistant's reply text accompanying the plan.</summary>
        public string ReplyText { get; init; } = "";

        /// <summary>Reasoning or explanation behind the generated plan.</summary>
        public string? Reasoning { get; set; }

        public AiPlanMode Mode { get; init; } = AiPlanMode.Plan;

        public ObservableCollection<AiActionStep> Steps { get; } = new();

        public void RemoveStep(AiActionStep step)
        {
            if (step != null)
                Steps.Remove(step);
        }

        /// <summary>
        /// True when the plan must be explicitly confirmed before execution
        /// (any mutating action: add/update/delete/clear). Read-only or
        /// overlay-opening plans (list products, calc slope) do not require it.
        /// </summary>
        public bool RequiresConfirmation { get; set; }

        /// <summary>True when the plan never touches the order.</summary>
        public bool IsReadOnly { get; init; }

        /// <summary>
        /// True when the plan must surface a clarification card before
        /// execution. Set by <see cref="AiPlanValidator.Validate"/> and
        /// by the parsers (plan-mode + legacy single-action) so any
        /// plan-building pipeline is consistent.
        /// </summary>
        public bool NeedsClarification { get; set; }

        public AiPlanStatus Status { get; set; } = AiPlanStatus.Draft;

        public DateTime CreatedAt { get; init; } = DateTime.Now;

        public DateTime? ExecutedAt { get; set; }

        /// <summary>Model label that produced the plan (shown in telemetry).</summary>
        public string? ProducedBy { get; set; }
    }

    /// <summary>Outcome of one executed step.</summary>
    public sealed class AiStepExecutionResult
    {
        public string StepId { get; init; } = "";
        public string PreviewText { get; init; } = "";
        public bool Success { get; init; }
        public string? Error { get; init; }
    }

    /// <summary>Outcome of executing a whole plan.</summary>
    public sealed class AiExecutionResult
    {
        public bool Success { get; init; }
        public bool RolledBack { get; init; }
        public string? Error { get; init; }
        public List<AiStepExecutionResult> StepResults { get; } = new();

        /// <summary>User-visible summary, e.g. «Выполнено 3 из 3 действий».</summary>
        public string Summary { get; init; } = "";
    }

    /// <summary>
    /// Regenerate / duplicate-execution guard data: RequestId, SourceMessageId
    /// and Status live directly on <see cref="AiActionPlan"/> so a re-generated
    /// reply can never re-run an already-executed action.
    /// </summary>
}
