using System;
using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Чистая логика статусов офисов (без сети и настроек) — OfficeStatusCalculator.
    /// </summary>
    public class OfficeStatusCalculatorTests
    {
        private static readonly IReadOnlyList<LocationOption> Offices = new List<LocationOption>
        {
            new("1", "Красношапки 44 — «Дом Окон+»"),
            new("2", "Рудакова 76 — «Компания „Уют”»"),
        };

        private static readonly DateTimeOffset Now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

        private static OfficeReport Report(string prefix, string version, DateTimeOffset reportedAt, string locationName = "") => new()
        {
            Prefix = prefix,
            LocationName = locationName,
            Version = version,
            ReportedAt = reportedAt.ToString("o"),
        };

        [Fact]
        public void BuildRows_AllKnownOfficesPresent_NoReports_AllNoData()
        {
            var rows = OfficeStatusCalculator.BuildRows(Offices, Array.Empty<OfficeReport>(), new Version("3.47.4"), Now, "2");

            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Equal(OfficeStatus.NoData, r.Status));
            Assert.Equal("—", rows[0].Version);
        }

        [Fact]
        public void BuildRows_FreshReport_SameVersionAsLatest_UpToDate()
        {
            var reports = new[] { Report("1", "3.47.4", Now.AddHours(-1)) };

            var rows = OfficeStatusCalculator.BuildRows(Offices, reports, new Version("3.47.4"), Now, "2");

            Assert.Equal(OfficeStatus.UpToDate, rows[0].Status);
            Assert.Equal("3.47.4", rows[0].Version);
            Assert.Equal("Красношапки 44 — «Дом Окон+»", rows[0].LocationName);
        }

        [Fact]
        public void BuildRows_FreshReport_OlderThanLatest_Outdated()
        {
            var reports = new[] { Report("1", "3.46.1", Now.AddHours(-1)) };

            var rows = OfficeStatusCalculator.BuildRows(Offices, reports, new Version("3.47.4"), Now, "2");

            Assert.Equal(OfficeStatus.Outdated, rows[0].Status);
        }

        [Fact]
        public void BuildRows_ReportOlderThanStaleThreshold_NoData()
        {
            var reports = new[] { Report("1", "3.47.4", Now.AddHours(-80)) };

            var rows = OfficeStatusCalculator.BuildRows(Offices, reports, new Version("3.47.4"), Now, "2");

            Assert.Equal(OfficeStatus.NoData, rows[0].Status);
            Assert.Equal("—", rows[0].Version);
        }

        [Fact]
        public void BuildRows_LatestVersionUnknown_FreshReport_UpToDate()
        {
            var reports = new[] { Report("1", "3.47.4", Now.AddHours(-1)) };

            var rows = OfficeStatusCalculator.BuildRows(Offices, reports, latestVersion: null, Now, "2");

            Assert.Equal(OfficeStatus.UpToDate, rows[0].Status);
        }

        [Fact]
        public void BuildRows_BrokenTimestamp_NoData()
        {
            var report = Report("1", "3.47.4", Now.AddHours(-1));
            report.ReportedAt = "not-a-date";
            var reports = new[] { report };

            var rows = OfficeStatusCalculator.BuildRows(Offices, reports, new Version("3.47.4"), Now, "2");

            Assert.Equal(OfficeStatus.NoData, rows[0].Status);
        }

        [Fact]
        public void BuildRows_UnknownOfficeInReports_AddedAsRow()
        {
            var reports = new[] { Report("9", "3.47.4", Now.AddHours(-1), "Новый филиал") };

            var rows = OfficeStatusCalculator.BuildRows(Offices, reports, new Version("3.47.4"), Now, "2");

            Assert.Equal(3, rows.Count);
            var extra = rows[2];
            Assert.Equal("9", extra.Prefix);
            Assert.Equal("Новый филиал", extra.LocationName);
            Assert.Equal(OfficeStatus.UpToDate, extra.Status);
        }

        [Fact]
        public void BuildRows_CurrentOffice_Flagged()
        {
            var reports = new[] { Report("2", "3.47.4", Now.AddHours(-1)) };

            var rows = OfficeStatusCalculator.BuildRows(Offices, reports, new Version("3.47.4"), Now, "2");

            Assert.False(rows[0].IsCurrentOffice);
            Assert.True(rows[1].IsCurrentOffice);
        }

        [Fact]
        public void BuildRows_MultipleReportsForSameOffice_UsesNewest()
        {
            // Оба отчёта — от одного «легаси»-устройства (пустой deviceId):
            // берётся новейший.
            var reports = new[]
            {
                Report("1", "3.46.1", Now.AddHours(-20)),
                Report("1", "3.47.4", Now.AddHours(-1)),
            };

            var rows = OfficeStatusCalculator.BuildRows(Offices, reports, new Version("3.47.4"), Now, "2");

            Assert.Equal(OfficeStatus.UpToDate, rows[0].Status);
            Assert.Equal("3.47.4", rows[0].Version);
        }

        // ─── Несколько устройств в одном офисе ────────────────────────────

        private static OfficeReport DeviceReport(string prefix, string deviceId, string version,
            DateTimeOffset reportedAt, string deviceName = "", string locationName = "") => new()
        {
            Prefix = prefix,
            DeviceId = deviceId,
            DeviceName = deviceName,
            LocationName = locationName,
            Version = version,
            ReportedAt = reportedAt.ToString("o"),
        };

        [Fact]
        public void BuildRows_TwoDevicesSameVersion_OfficeUpToDate_DeviceCountTwo()
        {
            var reports = new[]
            {
                DeviceReport("1", "devA", "3.47.4", Now.AddHours(-1), "PK-A"),
                DeviceReport("1", "devB", "3.47.4", Now.AddHours(-2), "PK-B"),
            };

            var rows = OfficeStatusCalculator.BuildRows(Offices, reports, new Version("3.47.4"), Now, "2");

            var row = rows[0];
            Assert.Equal(OfficeStatus.UpToDate, row.Status);
            Assert.Equal(2, row.DeviceCount);
            Assert.Equal(2, row.Devices.Count);
            Assert.All(row.Devices, d => Assert.Equal(OfficeStatus.UpToDate, d.Status));
            Assert.Equal("PK-A", row.Devices[0].DeviceLabel);
        }

        [Fact]
        public void BuildRows_TwoDevices_OneOutdated_OfficeOutdated()
        {
            var reports = new[]
            {
                DeviceReport("1", "devA", "3.47.4", Now.AddHours(-1), "PK-A"),
                DeviceReport("1", "devB", "3.46.1", Now.AddHours(-2), "PK-B"),
            };

            var rows = OfficeStatusCalculator.BuildRows(Offices, reports, new Version("3.47.4"), Now, "2");

            var row = rows[0];
            Assert.Equal(OfficeStatus.Outdated, row.Status); // есть хоть одно устаревшее устройство
            Assert.Equal(2, row.DeviceCount);
            Assert.Contains(row.Devices, d => d.Status == OfficeStatus.UpToDate);
            Assert.Contains(row.Devices, d => d.Status == OfficeStatus.Outdated && d.Version == "3.46.1");
        }

        [Fact]
        public void BuildRows_StaleDeviceListedAsNoData_FreshDeviceKeepsOfficeAlive()
        {
            var reports = new[]
            {
                DeviceReport("1", "devA", "3.47.4", Now.AddHours(-1), "PK-A"),
                // PK-B молчит 80 часов — протух, но виден в списке устройств.
                DeviceReport("1", "devB", "3.40.0", Now.AddHours(-80), "PK-B"),
            };

            var rows = OfficeStatusCalculator.BuildRows(Offices, reports, new Version("3.47.4"), Now, "2");

            var row = rows[0];
            Assert.Equal(OfficeStatus.UpToDate, row.Status); // по свежему устройству
            Assert.Equal(2, row.DeviceCount);               // оба устройства видны
            var stale = Assert.Single(row.Devices.Where(d => d.Status == OfficeStatus.NoData));
            Assert.Equal("PK-B", stale.DeviceLabel);
            Assert.Equal("3.40.0", stale.Version); // последняя известная версия
        }

        [Fact]
        public void BuildRows_AllDevicesStale_OfficeNoData_DevicesStillListed()
        {
            var reports = new[]
            {
                DeviceReport("1", "devA", "3.47.4", Now.AddHours(-80), "PK-A"),
            };

            var rows = OfficeStatusCalculator.BuildRows(Offices, reports, new Version("3.47.4"), Now, "2");

            var row = rows[0];
            Assert.Equal(OfficeStatus.NoData, row.Status);
            Assert.Equal("—", row.Version);
            Assert.Equal(1, row.DeviceCount);
            Assert.Equal(OfficeStatus.NoData, Assert.Single(row.Devices).Status);
        }

        [Fact]
        public void BuildRows_LegacyReport_CountsAsOneDevice()
        {
            // Отчёт без deviceId (старый формат) = одно устройство офиса.
            var reports = new[] { Report("1", "3.47.4", Now.AddHours(-1)) };

            var rows = OfficeStatusCalculator.BuildRows(Offices, reports, new Version("3.47.4"), Now, "2");

            var row = rows[0];
            Assert.Equal(OfficeStatus.UpToDate, row.Status);
            Assert.Equal(1, row.DeviceCount);
            Assert.Single(row.Devices);
        }

        [Fact]
        public void BuildRows_TwoCopiesSameMachine_OneDevice()
        {
            // Одна и та же машина прислала два отчёта с РАЗНЫМИ deviceId
            // (обычная версия + dev на одном ПК) — панель показывает одно устройство.
            var reports = new[]
            {
                DeviceReport("1", "guidA", "3.47.4", Now.AddHours(-2), "PK-1"),
                DeviceReport("1", "guidB", "3.47.4", Now.AddHours(-1), "PK-1"),
            };

            var rows = OfficeStatusCalculator.BuildRows(Offices, reports, new Version("3.47.4"), Now, "2");

            var row = rows[0];
            Assert.Equal(OfficeStatus.UpToDate, row.Status);
            Assert.Equal(1, row.DeviceCount);
            Assert.Single(row.Devices);
            Assert.Equal("guidB", row.Devices[0].DeviceId); // новейший отчёт
        }

        [Fact]
        public void BuildRows_LegacyPlusNamedDevice_SameDeviceShownOnce()
        {
            // Старая сборка (легаси-файл office-1.json) + новая (office-1-{id}.json)
            // на одном ПК — устройство не дублируется.
            var reports = new[]
            {
                Report("1", "3.46.1", Now.AddHours(-1)), // легаси, без deviceId/имени
                DeviceReport("1", "guidA", "3.47.4", Now.AddHours(-1), "PK-1"),
            };

            var rows = OfficeStatusCalculator.BuildRows(Offices, reports, new Version("3.47.4"), Now, "2");

            var row = rows[0];
            Assert.Equal(1, row.DeviceCount);
            var device = Assert.Single(row.Devices);
            Assert.Equal("PK-1", device.DeviceLabel);
            Assert.Equal(OfficeStatus.UpToDate, row.Status);
        }

        [Theory]
        [InlineData(OfficeStatus.UpToDate, "\u2713")]   // ✓
        [InlineData(OfficeStatus.Outdated, "\u26A0")]   // ⚠
        [InlineData(OfficeStatus.NoData,    "\u2753")]   // ❓
        public void StatusGlyph_MatchesStatus(OfficeStatus status, string expected)
        {
            var row = new OfficeStatusRow { Status = status };
            Assert.Equal(expected, row.StatusGlyph);
        }
    }
}
