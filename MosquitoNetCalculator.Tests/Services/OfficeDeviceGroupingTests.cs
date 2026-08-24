using System;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Дедупликация устройств офиса: одна физическая машина = одно устройство,
    /// даже если она прислала несколько отчётов (две копии программы на ПК:
    /// обычная + dev, или гонка первой генерации deviceId).
    /// </summary>
    public class OfficeDeviceGroupingTests
    {
        private static readonly DateTimeOffset Now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

        private static OfficeReport Report(string deviceId, string deviceName, DateTimeOffset reportedAt) => new()
        {
            Prefix = "1",
            DeviceId = deviceId,
            DeviceName = deviceName,
            Version = "3.47.4",
            ReportedAt = reportedAt.ToString("o"),
        };

        [Fact]
        public void DistinctDevices_SameMachineName_DifferentDeviceIds_OneDevice_NewestWins()
        {
            // ПК «PK-1» запущен в двух копиях (обычная + dev) → два разных deviceId,
            // но имя машины одно — это ОДНО устройство.
            var reports = new[]
            {
                Report("guidA", "PK-1", Now.AddHours(-2)),
                Report("guidB", "PK-1", Now.AddHours(-1)),
            };

            var devices = OfficeDeviceGrouping.DistinctDevices(reports);

            var single = Assert.Single(devices);
            Assert.Equal("guidB", single.DeviceId); // новейший отчёт
            Assert.Equal("PK-1", single.DeviceName);
        }

        [Fact]
        public void DistinctDevices_DifferentMachineNames_TwoDevices()
        {
            var reports = new[]
            {
                Report("guidA", "PK-1", Now.AddHours(-1)),
                Report("guidB", "PK-2", Now.AddHours(-2)),
            };

            Assert.Equal(2, OfficeDeviceGrouping.DistinctDevices(reports).Count);
        }

        [Fact]
        public void DistinctDevices_MachineNameCaseInsensitive()
        {
            var reports = new[]
            {
                Report("guidA", "PK-1", Now.AddHours(-2)),
                Report("guidB", "pk-1", Now.AddHours(-1)),
            };

            var single = Assert.Single(OfficeDeviceGrouping.DistinctDevices(reports));
            Assert.Equal("guidB", single.DeviceId);
        }

        [Fact]
        public void DistinctDevices_LegacyOnly_OneAnonymousDevice()
        {
            // Только легаси-отчёты (без deviceId и имени) — одно устройство офиса.
            var reports = new[]
            {
                Report("", "", Now.AddHours(-2)),
                Report("", "", Now.AddHours(-1)),
            };

            var single = Assert.Single(OfficeDeviceGrouping.DistinctDevices(reports));
            Assert.Equal("", single.DeviceId);
        }

        [Fact]
        public void DistinctDevices_LegacyPlusNamed_LegacyDropped()
        {
            // Легаси-запись (старая сборка, файл office-{prefix}.json) + именованное
            // устройство (новая сборка) того же ПК — легаси отбрасывается.
            var reports = new[]
            {
                Report("", "", Now.AddHours(-1)),
                Report("guidA", "PK-1", Now.AddHours(-1)),
            };

            var single = Assert.Single(OfficeDeviceGrouping.DistinctDevices(reports));
            Assert.Equal("PK-1", single.DeviceName);
        }

        [Fact]
        public void DistinctDevices_DeviceIdWithoutName_KeptAsDevice()
        {
            // deviceId есть, имени нет — это не легаси, устройство сохраняется.
            var reports = new[]
            {
                Report("guidA", "", Now.AddHours(-1)),
            };

            var single = Assert.Single(OfficeDeviceGrouping.DistinctDevices(reports));
            Assert.Equal("guidA", single.DeviceId);
        }
    }
}
