using System;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Чистые тесты подсказки «последняя связь» на чипе устройства:
    /// свежий отчёт — пусто; давность — читаемый текст с «·».
    /// </summary>
    public class DeviceLastSeenHintTests
    {
        private static DateTimeOffset Utc(string s) => DateTimeOffset.Parse(s);

        [Fact]
        public void FreshReport_Empty()
        {
            var now = Utc("2026-09-06T12:00:00Z");
            Assert.Equal("", DeviceLastSeenHint.Text(Utc("2026-09-06T11:30:00Z"), now));
            Assert.Equal("", DeviceLastSeenHint.Text(Utc("2026-09-06T00:30:00Z"), now));
        }

        [Fact]
        public void NoReport_Empty()
        {
            Assert.Equal("", DeviceLastSeenHint.Text(null, Utc("2026-09-06T12:00:00Z")));
        }

        [Fact]
        public void Boundary_AtThreshold_Shows()
        {
            var now = Utc("2026-09-06T12:00:00Z");
            Assert.NotEqual("", DeviceLastSeenHint.Text(now.AddHours(-12), now));
        }

        [Fact]
        public void Yesterday_ShowsLocalTime()
        {
            var now = DateTimeOffset.Parse("2026-09-06T21:00:00+03:00");
            var hint = DeviceLastSeenHint.Text(Utc("2026-09-05T07:10:00Z"), now.ToOffset(TimeSpan.Zero));
            Assert.Contains("вчера", hint);
            Assert.Contains("10:10", hint);
            Assert.StartsWith("· ", hint);
        }

        [Fact]
        public void FewDaysBack_ShowsDays()
        {
            var now = Utc("2026-09-06T12:00:00Z");
            Assert.Contains("3 дн. назад", DeviceLastSeenHint.Text(now.AddDays(-3), now));
        }

        [Fact]
        public void OldReport_ShowsDate()
        {
            var now = Utc("2026-09-06T12:00:00Z");
            Assert.Contains("12.05.2026", DeviceLastSeenHint.Text(now.AddDays(-117), now));
        }
    }
}
