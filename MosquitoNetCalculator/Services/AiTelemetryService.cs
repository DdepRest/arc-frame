using System;
using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Session-scoped AI telemetry. Privacy-safe by construction: it stores
    /// only aggregate numbers and model/provider labels — never keys, prompts,
    /// order contents or client data.
    /// </summary>
    public sealed class AiTelemetryService
    {
        private static readonly Lazy<AiTelemetryService> _instance = new(() => new AiTelemetryService());
        public static AiTelemetryService Instance => _instance.Value;

        private readonly object _lock = new();
        private readonly List<AiRequestMetrics> _requests = new();
        private readonly HashSet<string> _modelsUsed = new(StringComparer.OrdinalIgnoreCase);

        private AiTelemetryService() { }

        public void RecordRequest(AiRequestMetrics metrics)
        {
            lock (_lock)
            {
                _requests.Add(metrics);
                if (!string.IsNullOrWhiteSpace(metrics.Model))
                    _modelsUsed.Add(metrics.Model);
            }
        }

        public void ResetSession()
        {
            lock (_lock)
            {
                _requests.Clear();
                _modelsUsed.Clear();
            }
        }

        public AiSessionSummary SessionSummary
        {
            get
            {
                lock (_lock)
                {
                    if (_requests.Count == 0)
                        return new AiSessionSummary { ModelsUsed = Array.Empty<string>() };

                    var last = _requests[^1];
                    return new AiSessionSummary
                    {
                        Requests = _requests.Count,
                        Succeeded = _requests.Count(r => r.Succeeded),
                        Fallbacks = _requests.Count(r => r.FallbackUsed),
                        AverageDurationMs = _requests.Average(r => r.DurationMs),
                        ModelsUsed = _modelsUsed.OrderBy(m => m).ToList(),
                        LastModel = last.Model,
                        LastProvider = last.Provider?.ToString()
                    };
                }
            }
        }
    }
}
