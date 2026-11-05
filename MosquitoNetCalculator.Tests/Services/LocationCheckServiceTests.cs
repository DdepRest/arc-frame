using System;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Чистая логика периодической проверки местоположения: 30 дней срок
    /// годности подтверждения, 3 дня отсрочка по «✕», часы переводятся в UTC.
    /// Без WPF и без файлов — ровно то, что дёргает MainWindow на старте.
    /// </summary>
    public class LocationCheckServiceTests
    {
        private static readonly DateTime Now = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

        // ── Срок годности подтверждения ────────────────────────────────

        [Fact]
        public void NeverConfirmed_Asks()
        {
            // Чистая установка, обновление с версии без отметки, битая дата.
            Assert.True(LocationCheckService.ShouldPrompt(null, null, Now));
        }

        [Fact]
        public void ConfirmedYesterday_DoesNotAsk()
        {
            Assert.False(LocationCheckService.ShouldPrompt(Now.AddDays(-1), null, Now));
        }

        [Fact]
        public void Confirmed29DaysAgo_DoesNotAsk()
        {
            Assert.False(LocationCheckService.ShouldPrompt(Now.AddDays(-29), null, Now));
        }

        [Fact]
        public void Confirmed30DaysAgo_Asks()
        {
            // Ровно на границе — уже проверяем (интервал инклюзивный).
            Assert.True(LocationCheckService.ShouldPrompt(Now.AddDays(-30), null, Now));
        }

        [Fact]
        public void Confirmed45DaysAgo_Asks()
        {
            Assert.True(LocationCheckService.ShouldPrompt(Now.AddDays(-45), null, Now));
        }

        [Fact]
        public void ConfirmationFromTheFuture_DoesNotAsk()
        {
            // Часы съехали назад: лишний вопрос хуже, чем пропуск.
            Assert.False(LocationCheckService.ShouldPrompt(Now.AddDays(2), null, Now));
        }

        // ── Отсрочка по «✕» ────────────────────────────────────────────

        [Fact]
        public void SnoozedInFuture_SuppressesStaleConfirmation()
        {
            Assert.False(LocationCheckService.ShouldPrompt(
                Now.AddDays(-90), Now.AddDays(1), Now));
        }

        [Fact]
        public void SnoozeExpired_AndConfirmationStale_Asks()
        {
            Assert.True(LocationCheckService.ShouldPrompt(
                Now.AddDays(-60), Now.AddDays(-1), Now));
        }

        [Fact]
        public void SnoozeExpired_ButConfirmationFresh_DoesNotAsk()
        {
            Assert.False(LocationCheckService.ShouldPrompt(
                Now.AddDays(-5), Now.AddDays(-1), Now));
        }

        [Fact]
        public void SnoozeOnBoundary_IsExpired()
        {
            // Отсрочка «до момента T» действует, пока now < T; ровно в T она
            // истекла — так же инклюзивно, как и 30-дневный срок.
            Assert.True(LocationCheckService.ShouldPrompt(null, Now, Now));
        }

        // ── Нормализация часового пояса ────────────────────────────────

        [Fact]
        public void LocalKindConfirmation_IsConvertedToUtc()
        {
            // Локальное время «31 день назад» — по UTC это тоже просрочка;
            // значение не должно теряться из-за DateTimeKind.Local.
            DateTime localStale = Now.AddDays(-31).ToLocalTime();
            Assert.Equal(DateTimeKind.Local, localStale.Kind);

            Assert.True(LocationCheckService.ShouldPrompt(localStale, null, Now));
        }

        // ── Текст сводки для баннера ───────────────────────────────────

        [Fact]
        public void BuildSummary_JoinsLocationAndPrefix()
        {
            Assert.Equal(
                "Рудакова 76 — «Компания „Уют”» · префикс 2",
                LocationCheckService.BuildSummary("Рудакова 76 — «Компания „Уют”»", "2"));
        }

        [Fact]
        public void BuildSummary_WithoutSavedName_FallsBackToPrefix()
        {
            // Старый settings.json: имя локации появилось позже префикса.
            Assert.Equal("префикс договора 3", LocationCheckService.BuildSummary("", "3"));
            Assert.Equal("префикс договора 3", LocationCheckService.BuildSummary("   ", "3"));
        }

        [Fact]
        public void BuildSummary_WithoutAnyData_SaysSo()
        {
            Assert.Equal("местоположение не задано", LocationCheckService.BuildSummary(null, null));
        }

        [Fact]
        public void BuildSummary_TrimsWhitespace()
        {
            Assert.Equal("Офис · префикс 4", LocationCheckService.BuildSummary("  Офис  ", " 4 "));
        }

        // ── Подсказка в админ-панели ──────────────────────────────────

        [Fact]
        public void BuildAdminHint_FreshConfirmation_IsSilent()
        {
            var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
            Assert.Equal(string.Empty,
                LocationCheckService.BuildAdminHint(now.AddDays(-29), now));
        }

        [Fact]
        public void BuildAdminHint_StaleConfirmation_ReportsDays()
        {
            var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
            Assert.Equal("локация не подтверждена 62 дн.",
                LocationCheckService.BuildAdminHint(now.AddDays(-62), now));
        }

        [Fact]
        public void BuildAdminHint_OnBoundary_ReportsFullThirtyDays()
        {
            var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
            Assert.Equal("локация не подтверждена 30 дн.",
                LocationCheckService.BuildAdminHint(now.AddDays(-30), now));
        }

        [Fact]
        public void BuildAdminHint_NoData_TellsItWasNeverConfirmed()
        {
            // Старое устройство/отчёт без поля: молчать нельзя, иначе
            // отсутствие данных выглядело бы как «всё в порядке».
            Assert.Equal("локация не подтверждена",
                LocationCheckService.BuildAdminHint(null, DateTimeOffset.Now));
        }

        [Fact]
        public void BuildAdminHint_FutureConfirmation_IsSilent()
        {
            var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
            Assert.Equal(string.Empty,
                LocationCheckService.BuildAdminHint(now.AddDays(2), now));
        }
    }
}
