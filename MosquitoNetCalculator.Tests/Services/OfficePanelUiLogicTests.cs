using System;
using System.Linq;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Чистые тесты модели строки офиса (прогресс устройств) и фильтра/группировки
    /// админ-панели (AdminPanelControl.OfficeRowFilter — чистая логика без WPF).
    /// </summary>
    public class OfficePanelUiLogicTests
    {
        private static OfficeDeviceRow Device(OfficeStatus status) => new()
        {
            DeviceId = Guid.NewGuid().ToString("N"),
            DeviceName = "PK-" + Guid.NewGuid().ToString("N")[..4],
            Version = "3.49.0",
            LastReportAt = DateTimeOffset.UtcNow,
            Status = status,
        };

        private static OfficeStatusRow Row(OfficeStatus status, params OfficeDeviceRow[] devices) => new()
        {
            Prefix = "2",
            LocationName = "Рудакова 76 — «Компания Уют»",
            Status = status,
            DeviceCount = devices.Length,
            Devices = devices,
        };

        // ─── OfficeStatusRow: прогресс устройств офиса ───

        [Fact]
        public void ProgressText_ShowsUpToDateFraction()
        {
            var row = Row(OfficeStatus.Outdated, Device(OfficeStatus.UpToDate), Device(OfficeStatus.UpToDate), Device(OfficeStatus.Outdated));
            Assert.Equal("2/3", row.ProgressText);
            Assert.Equal(2, row.UpToDateCount);
            Assert.Equal(1, row.OutdatedCount);
        }

        [Fact]
        public void UpToDateRatio_ZeroDevices_IsZero()
        {
            var row = Row(OfficeStatus.NoData);
            Assert.Equal(0.0, row.UpToDateRatio);
            Assert.Equal("0/0", row.ProgressText);
        }

        [Fact]
        public void UpToDateRatio_AllUpToDate_IsOne()
        {
            var row = Row(OfficeStatus.UpToDate, Device(OfficeStatus.UpToDate), Device(OfficeStatus.UpToDate));
            Assert.Equal(1.0, row.UpToDateRatio);
        }

        // ─── OfficeRowFilter: поиск + чипы статуса ───

        [Fact]
        public void Filter_All_CatchesEverything()
        {
            var row = Row(OfficeStatus.Outdated, Device(OfficeStatus.Outdated));
            Assert.True(AdminPanelControl.OfficeRowFilter.Matches(row, "", "All"));
            Assert.True(AdminPanelControl.OfficeRowFilter.Matches(row, "   ", "All"));
        }

        [Fact]
        public void Filter_ByStatusChip()
        {
            var outdated = Row(OfficeStatus.Outdated, Device(OfficeStatus.Outdated));
            var upToDate = Row(OfficeStatus.UpToDate, Device(OfficeStatus.UpToDate));

            Assert.True(AdminPanelControl.OfficeRowFilter.Matches(outdated, "", "Outdated"));
            Assert.False(AdminPanelControl.OfficeRowFilter.Matches(upToDate, "", "Outdated"));
            Assert.True(AdminPanelControl.OfficeRowFilter.Matches(upToDate, "", "UpToDate"));
            Assert.False(AdminPanelControl.OfficeRowFilter.Matches(outdated, "", "NoData"));
        }

        [Fact]
        public void Filter_SearchByName_AndByPrefix_CaseInsensitive()
        {
            var row = Row(OfficeStatus.UpToDate, Device(OfficeStatus.UpToDate));

            Assert.True(AdminPanelControl.OfficeRowFilter.Matches(row, "рудакова", "All"));
            Assert.True(AdminPanelControl.OfficeRowFilter.Matches(row, "РУДАКОВА", "All"));
            Assert.True(AdminPanelControl.OfficeRowFilter.Matches(row, "2", "All"));      // префикс
            Assert.False(AdminPanelControl.OfficeRowFilter.Matches(row, "красношапки", "All"));
        }

        [Fact]
        public void Filter_SearchPlusChip_CombinedWithAnd()
        {
            var outdated = Row(OfficeStatus.Outdated, Device(OfficeStatus.Outdated));

            // Название совпадает, но чип — не тот: строка скрыта.
            Assert.False(AdminPanelControl.OfficeRowFilter.Matches(outdated, "рудакова", "UpToDate"));
            // Совпадают оба условия — строка видна.
            Assert.True(AdminPanelControl.OfficeRowFilter.Matches(outdated, "рудакова", "Outdated"));
        }

        // ─── GroupTitles через конвертер группировки (чистая часть) ───

        [Fact]
        public void OfficeReportFileName_Helper_StaysDeterministic()
        {
            // Имена файлов, по которым строится ручная отвязка, не должны меняться.
            Assert.Equal("office-2-abc.json", OfficeReportService.ReportFileName("2", "abc"));
            Assert.Equal("office-2.json", OfficeReportService.ReportFileName("2", ""));
        }
    }
}
