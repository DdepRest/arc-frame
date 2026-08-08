using System;
using System.Collections.Generic;
using System.Linq;

namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// Runtime info about the model that actually answered a streaming request.
    /// Raised by <see cref="Services.AiAssistantService"/> on the first token.
    /// </summary>
    public sealed class AiStreamInfo
    {
        public string ModelLabel { get; init; } = "";
        public AiProvider Provider { get; init; }
        public int Attempt { get; init; } = 1;
        public bool FallbackUsed { get; init; }
    }

    /// <summary>
    /// Per-request telemetry. Privacy rule: never stores API keys, full prompt
    /// texts, order contents or client data — only sizes and aggregates.
    /// </summary>
    public sealed class AiRequestMetrics
    {
        public AiProvider? Provider { get; init; }
        public string? Model { get; init; }
        public DateTime StartedAt { get; init; } = DateTime.Now;
        public long DurationMs { get; init; }
        public bool Succeeded { get; init; }
        public int? Attempt { get; init; }
        public bool FallbackUsed { get; init; }
        public int? HttpStatus { get; init; }
        public int HistorySize { get; init; }
        public int OrderContextSize { get; init; }
        public int PlanStepsCount { get; init; }
        public int? PromptTokens { get; init; }
        public int? CompletionTokens { get; init; }
    }

    /// <summary>Aggregated session statistics (shown by «/статус»).</summary>
    public sealed class AiSessionSummary
    {
        public int Requests { get; init; }
        public int Succeeded { get; init; }
        public int Failed => Requests - Succeeded;
        public int Fallbacks { get; init; }
        public double AverageDurationMs { get; init; }
        public IReadOnlyList<string> ModelsUsed { get; init; } = Array.Empty<string>();
        public string? LastModel { get; init; }
        public string? LastProvider { get; init; }

        public string FormatBrief()
        {
            var avg = AverageDurationMs > 0 ? $", среднее время: {AverageDurationMs / 1000.0:0.0} с" : "";
            var models = ModelsUsed.Count > 0 ? $", модели: {string.Join(", ", ModelsUsed.Take(3))}" : "";
            return $"Запросов: {Requests}, успешных: {Succeeded}, фолбэков: {Fallbacks}{avg}{models}.";
        }
    }
}
