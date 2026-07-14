using System;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Tests for <see cref="IdleDetector"/> — WinAPI idle detection
    /// extracted from UpdateService (Phase 2).
    ///
    /// These tests verify the basic contract: GetIdleTime returns a
    /// non-negative TimeSpan. We can't mock GetLastInputInfo (it's a
    /// P/Invoke), so we test the observable behavior on the real system.
    /// </summary>
    public class IdleDetectorTests
    {
        [Fact]
        public void GetIdleTime_ReturnsNonNegativeTimeSpan()
        {
            var idle = IdleDetector.GetIdleTime();
            Assert.True(idle >= TimeSpan.Zero,
                $"Idle time should be non-negative, got {idle}");
        }

        [Fact]
        public void GetIdleTime_DoesNotThrow()
        {
            // Smoke test — calling GetIdleTime should never throw.
            // It's a WinAPI call that either succeeds or returns Zero.
            var ex = Record.Exception(() => IdleDetector.GetIdleTime());
            Assert.Null(ex);
        }

        [Fact]
        public void GetIdleTime_WithinReasonableBounds()
        {
            // On a test runner, idle time should be relatively small
            // (the test process is actively running). We check it's
            // less than 24 hours as a sanity check — anything more
            // would indicate a bug in the WinAPI call or environment.
            var idle = IdleDetector.GetIdleTime();
            Assert.True(idle < TimeSpan.FromHours(24),
                $"Idle time {idle} exceeds 24h — likely a bug");
        }
    }
}
