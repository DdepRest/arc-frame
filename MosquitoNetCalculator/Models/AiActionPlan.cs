using System;
using System.Collections.Generic;
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
    /// validation messages, per-step status).
    /// </summary>
    public sealed class AiActionStep
    {
        public string StepId { get; } = Guid.NewGuid().ToString("N");

        public AiCommandType CommandType { get; init; }

        public AiCommandParams Params { get; init; } = new();

        /// <summary>Short human-readable description shown in the plan card.</summary>
        public string PreviewText { get; init; } = "";

        /// <summary>Local validation messages (empty when the step is valid).</summary>
        public List<string> ValidationMessages { get; } = new();

        public AiPlanStatus Status { get; set; } = AiPlanStatus.Draft;

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

        public AiPlanMode Mode { get; init; } = AiPlanMode.Plan;

        public List<AiActionStep> Steps { get; } = new();

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
