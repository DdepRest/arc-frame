using System;

namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// Result of probing a single AI model: whether it answered a minimal chat
    /// request. Produced by the "auto-analysis of available models" feature and
    /// persisted so future sessions can skip models known to be dead.
    /// </summary>
    public sealed class AiModelAvailability
    {
        public string Id { get; set; } = "";

        /// <summary>Provider that serves this model.</summary>
        public AiProvider Provider { get; set; } = AiProvider.OpenRouter;

        /// <summary>True when the model answered a minimal chat probe.</summary>
        public bool IsAvailable { get; set; }

        /// <summary>HTTP status returned by the probe (null on network failure).</summary>
        public int? StatusCode { get; set; }

        /// <summary>Human-readable probe result (\"OK\", \"ключ исчерпан\", …).</summary>
        public string Detail { get; set; } = "";

        /// <summary>UTC timestamp of the probe, used for cache freshness.</summary>
        public DateTime? CheckedAt { get; set; }
    }
}
