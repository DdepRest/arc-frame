using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    public class AiTelemetryServiceTests
    {
        [Fact]
        public void FreshSession_HasZeroRequests()
        {
            var s = AiTelemetryService.Instance.SessionSummary;

            Assert.Equal(0, s.Requests);
        }

        [Fact]
        public void RecordRequest_UpdatesSummary()
        {
            var t = AiTelemetryService.Instance;
            t.ResetSession();

            t.RecordRequest(new AiRequestMetrics { Provider = AiProvider.Nvidia, Model = "NVIDIA · Nemotron", DurationMs = 2400, Succeeded = true });
            t.RecordRequest(new AiRequestMetrics { Provider = AiProvider.OpenRouter, Model = "Google Gemma", DurationMs = 800, Succeeded = true });
            t.RecordRequest(new AiRequestMetrics { Provider = AiProvider.Nvidia, Model = "NVIDIA · Nemotron", DurationMs = 5000, Succeeded = false, FallbackUsed = true });

            var s = t.SessionSummary;

            Assert.Equal(3, s.Requests);
            Assert.Equal(2, s.Succeeded);
            Assert.Equal(1, s.Failed);
            Assert.Equal(1, s.Fallbacks);
            Assert.Equal(2, s.ModelsUsed.Count);
            Assert.Contains("NVIDIA · Nemotron", s.ModelsUsed);
            Assert.Equal("NVIDIA · Nemotron", s.LastModel);
        }

        [Fact]
        public void ResetSession_Clears()
        {
            var t = AiTelemetryService.Instance;
            t.RecordRequest(new AiRequestMetrics { Succeeded = true, DurationMs = 100 });
            t.ResetSession();

            Assert.Equal(0, t.SessionSummary.Requests);
        }
    }
}
