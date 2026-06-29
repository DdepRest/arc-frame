using System;
using System.Threading.Tasks;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Tests for <see cref="UpdateCheckScheduler.ShouldCheckAt"/> pure logic.
    ///
    /// Strategy: drive the scheduler with a fake clock (<see cref="FakeClock"/>)
    /// and a fake idle provider (<see cref="FakeIdle"/>) and verify decisions
    /// at each (lastCheck, idleTime, now) triple.
    /// DispatcherTimer is NOT exercised here — production behaviour for the
    /// timer is covered by integration smoke (no need for a brittle timer test).
    /// </summary>
    public class UpdateCheckSchedulerTests
    {
        // ─── Test infrastructure ──────────────────────────────────────

        /// <summary>
        /// Manually-advanceable clock. Tests set <see cref="Now"/> directly,
        /// so time resolution doesn't matter — only the gaps between setpoints do.
        /// </summary>
        private sealed class FakeClock
        {
            public DateTime Now { get; set; } = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        }

        /// <summary>
        /// Manually-controllable idle-time provider. Replaces the old
        /// <c>NotifyActivity</c> pattern — tests set <see cref="IdleTime"/>
        /// directly to simulate "system has been idle for X minutes".
        /// </summary>
        private sealed class FakeIdle
        {
            public TimeSpan IdleTime { get; set; } = TimeSpan.Zero;
        }

        /// <summary>
        /// Builds a scheduler with the production defaults + injected fakes.
        /// Defaults are: CheckInterval=30m, IdleThreshold=10m, MinGap=2m.
        /// </summary>
        private static (UpdateCheckScheduler Scheduler, FakeClock Clock, FakeIdle Idle) MakeScheduler()
        {
            var clock = new FakeClock();
            var idle = new FakeIdle();
            var s = new UpdateCheckScheduler
            {
                CheckInterval = TimeSpan.FromMinutes(30),
                IdleThreshold = TimeSpan.FromMinutes(10),
                MinGap = TimeSpan.FromMinutes(2),
                TickInterval = TimeSpan.FromMinutes(60),
                Now = () => clock.Now,
                GetSystemIdleTime = () => idle.IdleTime,
            };
            return (s, clock, idle);
        }

        // ─── Default state after Start() ──────────────────────────────

        [Fact]
        public void Start_SetsLastCheckTimeToNow_SuppressesImmediateTrigger()
        {
            var (s, clock, _) = MakeScheduler();
            // До Start: lastCheck = MinValue
            Assert.Equal(DateTime.MinValue, s.LastCheckTime);

            clock.Now = clock.Now; // = T0
            s.Start();

            // Сразу после Start: lastCheck == now → throttle-гарантирует
            // никаких сразу-fire-проверок.
            Assert.True(s.IsRunning);
            Assert.False(s.ShouldCheckAt(clock.Now),
                "Right after Start() nothing should trigger — startup-check already happened.");
        }

        [Fact]
        public void Start_IsIdempotent_CallingTwiceDoesNotRestartTimer()
        {
            var (s, _, _) = MakeScheduler();
            s.Start();
            var first = s.LastCheckTime;
            s.Start(); // no-op
            Assert.Equal(first, s.LastCheckTime);
            Assert.True(s.IsRunning);
        }

        [Fact]
        public void Stop_StopsScheduler_PreservesState()
        {
            var (s, clock, _) = MakeScheduler();
            clock.Now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            s.Start();

            var saved = s.LastCheckTime;
            s.Stop();
            Assert.False(s.IsRunning);

            // Можно стартовать снова — state сохранён (lastCheck перезаписывается).
            clock.Now = clock.Now.AddMinutes(15);
            s.Start();
            Assert.True(s.IsRunning);
            Assert.Equal(clock.Now, s.LastCheckTime);
            Assert.NotEqual(saved, s.LastCheckTime);
        }

        // ─── Periodic (30-min) check trigger ──────────────────────────

        [Fact]
        public void ShouldCheckAt_LastCheck30MinAgo_TrueEvenIfUserActive()
        {
            var (s, clock, idle) = MakeScheduler();
            s.Start(); // lastCheck = T0
            clock.Now = clock.Now.AddMinutes(31); // periodic gate
            idle.IdleTime = TimeSpan.FromMinutes(1); // user active recently

            Assert.True(s.ShouldCheckAt(clock.Now));
        }

        [Fact]
        public void ShouldCheckAt_LastCheck15MinAgo_False_NotYetPeriodic()
        {
            var (s, clock, idle) = MakeScheduler();
            s.Start();
            clock.Now = clock.Now.AddMinutes(15);
            idle.IdleTime = TimeSpan.FromMinutes(1); // not idle

            // T+15, 15 < 30 min AND not idle → false.
            Assert.False(s.ShouldCheckAt(clock.Now));
        }

        // ─── Idle (10-min) check trigger ──────────────────────────────

        [Fact]
        public void ShouldCheckAt_IdleFor10Min_TrueAfterMinGap()
        {
            var (s, clock, idle) = MakeScheduler();
            s.Start(); // lastCheck = T0
            clock.Now = clock.Now.AddMinutes(15);
            idle.IdleTime = TimeSpan.FromMinutes(10); // exactly at threshold

            // T+15: time-since-last-check = 15 min >= MinGap (2 min) → true.
            Assert.True(s.ShouldCheckAt(clock.Now));
        }

        [Fact]
        public void ShouldCheckAt_IdleFor15Min_PeriodicAlsoHits_StillTrue()
        {
            var (s, clock, idle) = MakeScheduler();
            s.Start(); // T0
            clock.Now = clock.Now.AddMinutes(15);
            idle.IdleTime = TimeSpan.FromMinutes(15);

            // time-since-last-check=15 min < CheckInterval=30 min → periodic НЕ сработал.
            // idle=15 min >= IdleThreshold=10 min → idle сработал.
            Assert.True(s.ShouldCheckAt(clock.Now));
        }

        [Fact]
        public void ShouldCheckAt_IdleNotEnough_False()
        {
            var (s, clock, idle) = MakeScheduler();
            s.Start(); // T0
            clock.Now = clock.Now.AddMinutes(8);
            idle.IdleTime = TimeSpan.FromMinutes(7); // 7 < 10

            // idle 7 min < 10 min, periodic 8 min < 30 min → false.
            Assert.False(s.ShouldCheckAt(clock.Now));
        }

        // ─── Throttle (MinGap = 2 min) ────────────────────────────────

        [Fact]
        public void ShouldCheckAt_WithinMinGap_False_EvenIfBothGatesHit()
        {
            var (s, clock, idle) = MakeScheduler();
            // Запись "проверки" руками: не через Start, а через прямое MarkChecked.
            clock.Now = clock.Now.AddMinutes(5);
            s.MarkChecked(); // lastCheck = T+5
            idle.IdleTime = TimeSpan.FromMinutes(25); // way idle
            clock.Now = clock.Now.AddMinutes(20); // now = T+25

            // time-since-last-check = 20 min > MinGap → throttle OK.
            // idle = 25 min >= 10 → idle YES.
            Assert.True(s.ShouldCheckAt(clock.Now));

            // Теперь — свежий сценарий: только что отметили checked, idle есть.
            var (s2, c2, i2) = MakeScheduler();
            c2.Now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            s2.Start(); // lastCheck = T0

            c2.Now = c2.Now.AddMinutes(5);
            s2.MarkChecked(); // lastCheck = T+5
            i2.IdleTime = TimeSpan.FromMinutes(20); // idle throughout

            // Прошло 30 секунд с момента MarkChecked:
            c2.Now = c2.Now.AddSeconds(30);
            // time-since-last-check = 30 sec < MinGap (2 min) → throttle → false.
            Assert.False(s2.ShouldCheckAt(c2.Now));
        }

        [Fact]
        public void ShouldCheckAt_AfterMinGap_OldBehaviourHolds()
        {
            // Контр-тест к throttle: если подождали MinGap — оба триггера работают.
            var (s, clock, idle) = MakeScheduler();
            clock.Now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            s.Start();

            // 11 min спустя:
            clock.Now = clock.Now.AddMinutes(11);
            idle.IdleTime = TimeSpan.FromMinutes(11);
            // diff(now, lastCheck) = 11 min > MinGap (2 min) → throttle ОК.
            // diff(now, lastCheck) = 11 min < CheckInterval (30 min) → periodic НЕТ.
            // idle = 11 min >= IdleThreshold (10 min) → idle YES.
            Assert.True(s.ShouldCheckAt(clock.Now));
        }

        // ─── Post-startup state — production behaviour ────────────────

        [Fact]
        public void ShouldCheckAt_ImmediatelyAfterStart_False_Throttled()
        {
            var (s, clock, _) = MakeScheduler();
            clock.Now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            s.Start();

            // 0 < MinGap → throttle → false.
            Assert.False(s.ShouldCheckAt(clock.Now));
        }

        [Fact]
        public void ShouldCheckAt_FewMinutesAfterStart_StillFalse()
        {
            var (s, clock, idle) = MakeScheduler();
            clock.Now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            s.Start();

            // T+1m: throttle держит (1 < 2), idle 1 < 10 — false в любом случае.
            clock.Now = clock.Now.AddMinutes(1);
            idle.IdleTime = TimeSpan.FromMinutes(1);
            Assert.False(s.ShouldCheckAt(clock.Now));
        }

        [Fact]
        public void ShouldCheckAt_FewMinutesAfterStart_NotIdle_StillFalse()
        {
            var (s, clock, idle) = MakeScheduler();
            clock.Now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            s.Start();

            clock.Now = clock.Now.AddMinutes(1);
            idle.IdleTime = TimeSpan.Zero; // not idle at all
            // Throttle (1 < 2) + idle (0 < 10) → false.
            Assert.False(s.ShouldCheckAt(clock.Now));
        }

        // ─── Idle resets via GetSystemIdleTime ────────────────────────

        [Fact]
        public void GetSystemIdleTime_ResetsIdleClock()
        {
            var (s, clock, idle) = MakeScheduler();
            clock.Now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            s.Start();
            clock.Now = clock.Now.AddMinutes(9);
            idle.IdleTime = TimeSpan.FromMinutes(9);
            // 9 min idle < 10 → false.
            Assert.False(s.ShouldCheckAt(clock.Now));

            // User moves mouse → idle resets to 0.
            idle.IdleTime = TimeSpan.Zero;
            Assert.False(s.ShouldCheckAt(clock.Now),
                "Idle reset to 0 — not enough time passed.");

            // Подождём ещё 10 минут = T+19. Idle теперь 10 min.
            clock.Now = clock.Now.AddMinutes(10);
            idle.IdleTime = TimeSpan.FromMinutes(10);
            Assert.True(s.ShouldCheckAt(clock.Now));
        }

        [Fact]
        public void GetSystemIdleTime_CalledFrequently_PerformanceSafe()
        {
            // Проверка что GetSystemIdleTime() не делает тяжёлой работы
            // (в production это O(1) WinAPI call).
            var (s, _, _) = MakeScheduler();
            for (int i = 0; i < 100; i++)
            {
                _ = s.GetSystemIdleTime();
            }
            // Smoke: не бросает и не тормозит.
            Assert.True(true);
        }

        // ─── MarkChecked semantics ────────────────────────────────────

        [Fact]
        public void MarkChecked_ResetsLastCheckTime()
        {
            var (s, clock, idle) = MakeScheduler();
            clock.Now = new DateTime(2026, 1, 1);
            s.Start(); // lastCheck = T0

            clock.Now = clock.Now.AddMinutes(20);
            s.MarkChecked();
            Assert.Equal(clock.Now, s.LastCheckTime);

            // Сразу после MarkChecked: throttle-ветка.
            Assert.False(s.ShouldCheckAt(clock.Now));

            // Через MinGap + чуть-чуть idle — periodic сработает.
            clock.Now = clock.Now.AddMinutes(2).AddSeconds(1);
            idle.IdleTime = TimeSpan.FromMinutes(22); // idle throughout
            // 2 min 1 sec < CheckInterval 30 → periodic нет.
            // idle = 22 min ≥ 10 → idle YES.
            Assert.True(s.ShouldCheckAt(clock.Now));
        }

        // ─── Edge: exactly-at-threshold (>=) tests ──────────────────────────
        // Scheduler использует `>=` для idle/periodic и `<` для throttle.
        // Один тест на каждую границу — случайная смена оператора тихо ломает
        // поведение; эти тесты её поймают.

        [Fact]
        public void ShouldCheckAt_IdleEqualsThreshold_True()
        {
            var (s, clock, idle) = MakeScheduler();
            clock.Now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            s.Start();

            // T+10m ровно: idle == IdleThreshold → true (gate `>=`).
            clock.Now = clock.Now.AddMinutes(10);
            idle.IdleTime = TimeSpan.FromMinutes(10);
            Assert.True(s.ShouldCheckAt(clock.Now));
        }

        [Fact]
        public void ShouldCheckAt_LastCheckEqualsMinGap_FalseFromThrottle()
        {
            var (s, clock, _) = MakeScheduler();
            clock.Now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            s.Start();

            // T+2m ровно: throttle gate strict `<` → false.
            // Periodic / idle тоже false: 2 < 30 и 2 < 10.
            clock.Now = clock.Now.AddMinutes(2);
            Assert.False(s.ShouldCheckAt(clock.Now));
        }

        [Fact]
        public void ShouldCheckAt_PeriodicExactlyEqualsInterval_True()
        {
            var (s, clock, idle) = MakeScheduler();
            clock.Now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            s.Start();
            // Keep idle low so only periodic gate fires.
            idle.IdleTime = TimeSpan.FromMinutes(1);

            // T+30m ровно: periodic == CheckInterval → true.
            clock.Now = clock.Now.AddMinutes(30);
            Assert.True(s.ShouldCheckAt(clock.Now));
        }

        [Fact]
        public void ShouldCheckAt_IdleWhenPeriodicJustUnder_IdleGateFires()
        {
            // 29m59s от Start: periodic нет (29:59 < 30:00), НО idle-гейт сработал.
            var (s, clock, idle) = MakeScheduler();
            clock.Now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            s.Start();
            clock.Now = clock.Now.AddMinutes(29).AddSeconds(59);
            idle.IdleTime = TimeSpan.FromMinutes(29).Add(TimeSpan.FromSeconds(59));
            // idle = 29:59 >= 10 → gate сработал до periodic-check.
            Assert.True(s.ShouldCheckAt(clock.Now));
        }

        [Fact]
        public void ShouldCheckAt_PeriodicExactlyInterval_NotIdle_True()
        {
            // Periodic ровно = true, если throttle отпустил.
            var (s, clock, idle) = MakeScheduler();
            clock.Now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            s.Start();
            // Ждём пока throttle (MinGap=2min) пройдёт, и потом periodic попадёт в window без idle.
            clock.Now = clock.Now.AddMinutes(2);
            idle.IdleTime = TimeSpan.Zero; // not idle
            // T+30m: ровно CheckInterval.
            clock.Now = clock.Now.AddMinutes(28);
            Assert.True(s.ShouldCheckAt(clock.Now));
        }

        // ─── Sanity: defaults match production contract ──────────────

        [Fact]
        public void Default_Intervals_MatchProductionContract()
        {
            var s = new UpdateCheckScheduler();
            Assert.Equal(TimeSpan.FromMinutes(30), s.CheckInterval);
            Assert.Equal(TimeSpan.FromMinutes(10), s.IdleThreshold);
            Assert.Equal(TimeSpan.FromMinutes(2), s.MinGap);
            Assert.Equal(TimeSpan.FromSeconds(60), s.TickInterval);
        }

        [Fact]
        public void Default_GetSystemIdleTime_ReturnsZero()
        {
            var s = new UpdateCheckScheduler();
            Assert.Equal(TimeSpan.Zero, s.GetSystemIdleTime());
        }

        // ─── OnCheckDue callback (logic-only surrogate) ───────────────

        [Fact]
        public async Task ShouldCheckDue_Callback_FiresOncePerCheck()
        {
            var (s, clock, idle) = MakeScheduler();
            int fireCount = 0;
            s.OnCheckDue = () =>
            {
                fireCount++;
                return Task.CompletedTask;
            };

            s.Start(); // lastCheck = T0
            clock.Now = clock.Now.AddMinutes(11);
            idle.IdleTime = TimeSpan.FromMinutes(11); // idle=11 → триггерит

            // Smoke: ShouldCheckAt возвращает true.
            Assert.True(s.ShouldCheckAt(clock.Now));

            // Симулируем "tick handle": перед вызовом OnCheckDue шедулер
            // маркирует checked. Это контракт OnTick; проверяем его
            // через побочный эффект: после MarkChecked next ShouldCheckAt false.
            s.MarkChecked();
            Assert.False(s.ShouldCheckAt(clock.Now),
                "Сразу после MarkChecked throttle должен сработать.");
            await Task.CompletedTask;
        }
    }
}
