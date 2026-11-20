using System;
using System.Threading.Tasks;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Тесты чистой логики <see cref="OfficeReportScheduler"/> (по образцу
    /// UpdateCheckSchedulerTests): DispatcherTimer не гоняется — тестируются
    /// решения ShouldSendAt, Start/Stop и fire-once контракт.
    /// </summary>
    public class OfficeReportSchedulerTests
    {
        private sealed class FakeClock
        {
            public DateTime Now { get; set; } = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        }

        private static (OfficeReportScheduler Scheduler, FakeClock Clock) MakeScheduler()
        {
            var clock = new FakeClock();
            var s = new OfficeReportScheduler
            {
                Interval = TimeSpan.FromMinutes(30),
                Now = () => clock.Now,
            };
            return (s, clock);
        }

        [Fact]
        public void DefaultInterval_MatchProductionContract()
        {
            // Редкие автоматические считки: запуск программы + каждые 2 часа.
            Assert.Equal(TimeSpan.FromHours(2), OfficeReportScheduler.DefaultInterval);
            Assert.Equal(OfficeReportScheduler.DefaultInterval, new OfficeReportScheduler().Interval);
        }

        [Fact]
        public void Start_SetsLastSendTime_SuppressesImmediateSend()
        {
            var (s, clock) = MakeScheduler();
            Assert.Equal(DateTime.MinValue, s.LastSendTime);

            s.Start();

            Assert.True(s.IsRunning);
            Assert.Equal(clock.Now, s.LastSendTime);
            Assert.False(s.ShouldSendAt(clock.Now), "Сразу после Start отправка не нужна — стартовый отчёт уже ушёл.");
        }

        [Fact]
        public void Start_IsIdempotent()
        {
            var (s, _) = MakeScheduler();
            s.Start();
            var first = s.LastSendTime;
            s.Start();
            Assert.Equal(first, s.LastSendTime);
        }

        [Fact]
        public void Stop_StopsAndRestartable()
        {
            var (s, clock) = MakeScheduler();
            s.Start();
            s.Stop();
            Assert.False(s.IsRunning);

            clock.Now = clock.Now.AddMinutes(10);
            s.Start();
            Assert.True(s.IsRunning);
            Assert.Equal(clock.Now, s.LastSendTime);
        }

        [Fact]
        public void ShouldSendAt_BeforeInterval_False()
        {
            var (s, clock) = MakeScheduler();
            s.Start();

            clock.Now = clock.Now.AddMinutes(29);
            Assert.False(s.ShouldSendAt(clock.Now));
        }

        [Fact]
        public void ShouldSendAt_ExactlyInterval_True()
        {
            var (s, clock) = MakeScheduler();
            s.Start();

            clock.Now = clock.Now.AddMinutes(30);
            Assert.True(s.ShouldSendAt(clock.Now));
        }

        [Fact]
        public void ShouldSendAt_AfterInterval_True()
        {
            var (s, clock) = MakeScheduler();
            s.Start();

            clock.Now = clock.Now.AddMinutes(31);
            Assert.True(s.ShouldSendAt(clock.Now));
        }

        [Fact]
        public void MarkSent_ResetsLastSendTime_SuppressesNext()
        {
            var (s, clock) = MakeScheduler();
            s.Start();

            clock.Now = clock.Now.AddMinutes(31);
            Assert.True(s.ShouldSendAt(clock.Now));

            s.MarkSent(); // «отправка стартовала»
            Assert.Equal(clock.Now, s.LastSendTime);
            Assert.False(s.ShouldSendAt(clock.Now), "Сразу после MarkSent — throttle до следующего Interval.");
        }

        [Fact]
        public void OnSendDue_FiresOncePerInterval_AfterMarkSent()
        {
            var (s, clock) = MakeScheduler();
            int fireCount = 0;
            s.OnSendDue = () => { fireCount++; return Task.CompletedTask; };

            s.Start(); // lastSend = T0

            // T+15: отправлять не пора.
            clock.Now = clock.Now.AddMinutes(15);
            Assert.False(s.ShouldSendAt(clock.Now));

            // T+31: пора; симулируем tick → MarkSent (контракт OnTick).
            clock.Now = clock.Now.AddMinutes(16);
            Assert.True(s.ShouldSendAt(clock.Now));
            s.MarkSent();

            // Следующие 29 минут — снова не пора (один раз за интервал).
            clock.Now = clock.Now.AddMinutes(29);
            Assert.False(s.ShouldSendAt(clock.Now));

            // Через полный интервал — снова пора.
            clock.Now = clock.Now.AddMinutes(1);
            Assert.True(s.ShouldSendAt(clock.Now));

            // Сам колбэк в тесте не дёргается (тестируется только чистая логика).
            Assert.Equal(0, fireCount);
        }

        [Fact]
        public void RapidRetryInterval_MatchProductionContract()
        {
            Assert.Equal(TimeSpan.FromSeconds(30), OfficeReportScheduler.RapidRetryInterval);
            Assert.Equal(OfficeReportScheduler.RapidRetryInterval, new OfficeReportScheduler().RetryInterval);
        }

        [Fact]
        public void ScheduleRetry_TriggersAfterRapidRetryInterval_InsteadOfFullInterval()
        {
            var (s, clock) = MakeScheduler();
            s.Start();

            // Сразу после старта отправка подавлена
            Assert.False(s.ShouldSendAt(clock.Now));
            Assert.False(s.IsRetryPending);

            // Сбой отправки — планируем быстрый повтор
            s.ScheduleRetry();

            Assert.True(s.IsRetryPending);
            Assert.Equal(clock.Now.AddSeconds(30), s.RetryDueTime);

            // Через 15 секунд — ещё рано
            clock.Now = clock.Now.AddSeconds(15);
            Assert.False(s.ShouldSendAt(clock.Now));

            // Через 30 секунд — пора слать повтор
            clock.Now = clock.Now.AddSeconds(15);
            Assert.True(s.ShouldSendAt(clock.Now));

            // После успешного MarkSent() retry сброшен, следующий отчёт только через полный Interval (30 мин)
            s.MarkSent();
            Assert.False(s.IsRetryPending);
            Assert.False(s.ShouldSendAt(clock.Now));

            clock.Now = clock.Now.AddMinutes(29);
            Assert.False(s.ShouldSendAt(clock.Now));

            clock.Now = clock.Now.AddMinutes(1);
            Assert.True(s.ShouldSendAt(clock.Now));
        }

        [Fact]
        public void NotifySendResult_False_SchedulesRetry_True_ClearsRetry()
        {
            var (s, clock) = MakeScheduler();
            s.Start();
            Assert.Equal(s.Interval, s.CurrentTimerInterval);

            s.NotifySendResult(false);
            Assert.True(s.IsRetryPending);
            Assert.Equal(s.RetryInterval, s.CurrentTimerInterval);

            s.NotifySendResult(true);
            Assert.False(s.IsRetryPending);
            Assert.Equal(s.Interval, s.CurrentTimerInterval);
        }

        [Fact]
        public void MarkSent_RestoresDefaultTimerInterval()
        {
            var (s, _) = MakeScheduler();
            s.Start();
            s.ScheduleRetry();
            Assert.Equal(s.RetryInterval, s.CurrentTimerInterval);

            s.MarkSent();
            Assert.False(s.IsRetryPending);
            Assert.Equal(s.Interval, s.CurrentTimerInterval);
        }

        [Fact]
        public void TimerJitterTolerance_AllowsSlightlyEarlyTick()
        {
            var (s, clock) = MakeScheduler();
            s.Start();

            // Retry scheduled for T+30s
            s.ScheduleRetry();

            // Windows timer ticks 50 ms before 30 seconds (due to standard 15.6ms OS timer resolution)
            var slightlyEarlyRetry = clock.Now.AddSeconds(30).AddMilliseconds(-50);
            Assert.True(s.ShouldSendAt(slightlyEarlyRetry), "Небольшой джиттер таймера не должен приводить к пропуску тика повтора");

            // Normal interval T+30 min: ticks 500 ms early
            s.MarkSent();
            var slightlyEarlyInterval = clock.Now.AddMinutes(30).AddMilliseconds(-500);
            Assert.True(s.ShouldSendAt(slightlyEarlyInterval), "Джиттер таймера не должен приводить к пропуску 2-часового планового отчёта");
        }

        [Fact]
        public void Stop_ClearsPendingRetry()
        {
            var (s, clock) = MakeScheduler();
            s.Start();
            s.ScheduleRetry();
            Assert.True(s.IsRetryPending);

            s.Stop();
            Assert.False(s.IsRetryPending);
        }
    }
}
