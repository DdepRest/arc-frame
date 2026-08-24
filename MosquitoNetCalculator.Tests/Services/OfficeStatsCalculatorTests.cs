using System;
using System.Collections.Generic;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Чистая логика секции «Статистика» — OfficeStatsCalculator.
    /// </summary>
    public class OfficeStatsCalculatorTests
    {
        private static readonly IReadOnlyList<LocationOption> Offices = new List<LocationOption>
        {
            new("1", "Красношапки 44 — «Дом Окон+»"),
            new("2", "Рудакова 76 — «Компания „Уют”»"),
        };

        private static readonly DateTimeOffset Now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

        private static OfficeReport Report(string prefix, int orderCount, string locationName = "") => new()
        {
            Prefix = prefix,
            LocationName = locationName,
            Version = "3.47.4",
            ReportedAt = Now.AddHours(-1).ToString("o"),
            OrderCount = orderCount,
        };

        [Fact]
        public void BuildRows_AllKnownOffices_WithCounts()
        {
            var reports = new[]
            {
                Report("1", 12),
                Report("2", 7),
            };

            var rows = OfficeStatsCalculator.BuildRows(Offices, reports, "2");

            Assert.Equal(2, rows.Count);
            Assert.Equal(12, rows[0].OrderCount);
            Assert.Equal("12", rows[0].OrderCountDisplay);
            Assert.Equal(7, rows[1].OrderCount);
        }

        [Fact]
        public void BuildRows_NoReport_NullCount()
        {
            var rows = OfficeStatsCalculator.BuildRows(Offices, Array.Empty<OfficeReport>(), "2");

            Assert.Equal(2, rows.Count);
            Assert.Null(rows[0].OrderCount);
            Assert.Equal("—", rows[0].OrderCountDisplay);
        }

        [Fact]
        public void BuildRows_UnknownOfficeInReports_AddedAsRow()
        {
            var reports = new[] { Report("9", 3, "Новый филиал") };

            var rows = OfficeStatsCalculator.BuildRows(Offices, reports, "2");

            Assert.Equal(3, rows.Count);
            Assert.Equal("9", rows[2].Prefix);
            Assert.Equal("Новый филиал", rows[2].LocationName);
            Assert.Equal(3, rows[2].OrderCount);
        }

        [Fact]
        public void BuildRows_CurrentOffice_Flagged()
        {
            var reports = new[] { Report("2", 5) };

            var rows = OfficeStatsCalculator.BuildRows(Offices, reports, "2");

            Assert.False(rows[0].IsCurrentOffice);
            Assert.True(rows[1].IsCurrentOffice);
        }

        [Fact]
        public void BuildRows_MultipleReports_SameOffice_UsesNewest()
        {
            var old = Report("1", 4);
            old.ReportedAt = Now.AddHours(-20).ToString("o");
            var fresh = Report("1", 11);
            fresh.ReportedAt = Now.AddHours(-1).ToString("o");

            var rows = OfficeStatsCalculator.BuildRows(Offices, new[] { old, fresh }, "2");

            Assert.Equal(11, rows[0].OrderCount);
        }

        [Fact]
        public void SumOrderCounts_SumsNonNullCounts()
        {
            var reports = new[] { Report("1", 12), Report("2", 7) };
            var rows = OfficeStatsCalculator.BuildRows(Offices, reports, "2");

            Assert.Equal(19, OfficeStatsCalculator.SumOrderCounts(rows));
        }

        [Fact]
        public void SumOrderCounts_IgnoresMissingReports()
        {
            var reports = new[] { Report("1", 12) };
            var rows = OfficeStatsCalculator.BuildRows(Offices, reports, "2");

            Assert.Equal(12, OfficeStatsCalculator.SumOrderCounts(rows));
        }

        [Fact]
        public void BuildRows_OldReportCountStillShown_OrderCountDoesNotGoStale()
        {
            // В отличие от статусов (порог свежести 72ч), статистика показывает
            // последнее известное значение — свежесть видна по времени отчёта.
            var report = Report("1", 42);
            report.ReportedAt = Now.AddHours(-200).ToString("o");

            var rows = OfficeStatsCalculator.BuildRows(Offices, new[] { report }, "2");

            Assert.Equal(42, rows[0].OrderCount);
            Assert.NotNull(rows[0].LastReportAt);
        }

        // ─── Несколько устройств в одном офисе ────────────────────────────

        private static OfficeReport DeviceReport(string prefix, string deviceId, int orderCount,
            string deviceName = "", string locationName = "") => new()
        {
            Prefix = prefix,
            DeviceId = deviceId,
            DeviceName = deviceName,
            LocationName = locationName,
            Version = "3.47.4",
            ReportedAt = Now.AddHours(-1).ToString("o"),
            OrderCount = orderCount,
        };

        [Fact]
        public void BuildRows_MultipleDevices_OrderCountSumsAcrossDevices()
        {
            var reports = new[]
            {
                DeviceReport("1", "devA", 10, "PK-A"),
                DeviceReport("1", "devB", 7, "PK-B"),
            };

            var rows = OfficeStatsCalculator.BuildRows(Offices, reports, "2");

            var row = rows[0];
            Assert.Equal(17, row.OrderCount); // 10 + 7 — сумма по устройствам офиса
            Assert.Equal(2, row.DeviceCount);
        }

        [Fact]
        public void BuildRows_DeviceDuplicateReport_NewestPerDeviceWins()
        {
            var old = DeviceReport("1", "devA", 4, "PK-A");
            old.ReportedAt = Now.AddHours(-20).ToString("o");
            var fresh = DeviceReport("1", "devA", 9, "PK-A");
            fresh.ReportedAt = Now.AddHours(-1).ToString("o");
            var other = DeviceReport("1", "devB", 3, "PK-B");

            var rows = OfficeStatsCalculator.BuildRows(Offices, new[] { old, fresh, other }, "2");

            Assert.Equal(12, rows[0].OrderCount); // 9 (новейший devA) + 3 (devB)
            Assert.Equal(2, rows[0].DeviceCount);
        }

        [Fact]
        public void BuildRows_TwoCopiesSameMachine_CountedOnce()
        {
            // Одна машина, две копии программы (обычная + dev) с РАЗНЫМИ deviceId,
            // но одним именем ПК — в статистике заказы считаются один раз (новейший).
            var old = DeviceReport("1", "guidA", 10, "PK-1");
            old.ReportedAt = Now.AddHours(-2).ToString("o");
            var fresh = DeviceReport("1", "guidB", 15, "PK-1");
            fresh.ReportedAt = Now.AddHours(-1).ToString("o");

            var rows = OfficeStatsCalculator.BuildRows(Offices, new[] { old, fresh }, "2");

            var row = rows[0];
            Assert.Equal(1, row.DeviceCount);
            Assert.Equal(15, row.OrderCount); // НЕ 25 — то же устройство, новейший отчёт
        }

        [Fact]
        public void BuildRows_LegacyPlusNamedDevice_CountedOnce()
        {
            // Старая сборка (легаси office-1.json, 8 заказов) + новая (office-1-{id}.json,
            // 12 заказов) на одном ПК — берётся только новейшее устройство.
            var legacy = Report("1", 8);
            legacy.ReportedAt = Now.AddHours(-2).ToString("o");
            var fresh = DeviceReport("1", "guidA", 12, "PK-1");
            fresh.ReportedAt = Now.AddHours(-1).ToString("o");

            var rows = OfficeStatsCalculator.BuildRows(Offices, new[] { legacy, fresh }, "2");

            var row = rows[0];
            Assert.Equal(1, row.DeviceCount);
            Assert.Equal(12, row.OrderCount); // легаси-запись отброшена
        }
    }
}
