using System;
using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Чистая логика статусов офисов (без сети и UI) — покрыта юнит-тестами.
    ///
    /// В одном офисе может быть несколько УСТРОЙСТВ (ПК). Отчёты группируются
    /// по устройству (deviceId), статус считается для каждого устройства отдельно,
    /// а строка офиса агрегирует их:
    ///   • статус офиса — по СВЕЖИМ устройствам: есть хоть одно устаревшее →
    ///     Outdated; все свежие актуальны → UpToDate; свежих нет → NoData;
    ///   • DeviceCount / Devices — сколько устройств в офисе и версия каждого.
    ///
    /// Статус устройства:
    ///   UpToDate — отчёт свежий (моложе <see cref="StaleThreshold"/>) и версия
    ///              == последний релиз; если последняя версия неизвестна
    ///              (GitHub недоступен), свежий отчёт тоже считается UpToDate;
    ///   Outdated — отчёт свежий, но версия &lt; последнего релиза;
    ///   NoData   — отчёта нет, он старше порога, либо версия/время не читаются.
    /// </summary>
    public static class OfficeStatusCalculator
    {
        /// <summary>
        /// Отчёт старше этого порога считается «нет данных» (ПК выключен,
        /// программа давно не запускалась, потеряна связь с gist).
        /// </summary>
        public static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(72);

        /// <summary>
        /// Строит строки панели: все известные офисы из <paramref name="knownOffices"/>
        /// плюс неизвестные офисы, найденные в отчётах (будущие точки — показываются
        /// даже до обновления списка офисов).
        /// </summary>
        public static IReadOnlyList<OfficeStatusRow> BuildRows(
            IEnumerable<LocationOption> knownOffices,
            IReadOnlyList<OfficeReport> reports,
            Version? latestVersion,
            DateTimeOffset now,
            string currentPrefix)
        {
            var byPrefix = reports
                .GroupBy(r => r.Prefix)
                .ToDictionary(
                    g => g.Key,
                    g => new OfficeDevices(
                        g.OrderByDescending(r => r.ReportedAtUtc ?? DateTimeOffset.MinValue).First().LocationName,
                        BuildDevices(g, latestVersion, now)));

            var rows = new List<OfficeStatusRow>();
            foreach (var office in knownOffices)
                rows.Add(BuildRow(office.Prefix, office.LocationName, byPrefix, currentPrefix));

            foreach (var (prefix, data) in byPrefix)
            {
                if (knownOffices.All(o => o.Prefix != prefix))
                    rows.Add(BuildRow(prefix, data.LocationName, byPrefix, currentPrefix));
            }

            return rows;
        }

        /// <summary>
        /// Группирует отчёты офиса по устройствам: один ПК = одно устройство
        /// (см. <see cref="OfficeDeviceGrouping.DistinctDevices"/> — дедупликация
        /// по имени машины, чтобы две копии программы на одном ПК не считались
        /// двумя устройствами). Возвращает список, отсортированный по имени.
        /// </summary>
        private static IReadOnlyList<OfficeDeviceRow> BuildDevices(
            IEnumerable<OfficeReport> reports,
            Version? latestVersion,
            DateTimeOffset now)
        {
            return OfficeDeviceGrouping.DistinctDevices(reports)
                .Select(r => BuildDevice(r, latestVersion, now))
                .OrderBy(d => d.DeviceLabel, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>Статус одного устройства из его новейшего отчёта.</summary>
        private static OfficeDeviceRow BuildDevice(OfficeReport report, Version? latestVersion, DateTimeOffset now)
        {
            var reportedAt = report.ReportedAtUtc;
            if (reportedAt == null || now - reportedAt.Value > StaleThreshold)
            {
                return new OfficeDeviceRow
                {
                    DeviceId = report.DeviceId,
                    DeviceName = report.DeviceName,
                    Version = report.Version,
                    LastReportAt = reportedAt,
                    Status = OfficeStatus.NoData,
                };
            }

            var status = OfficeStatus.UpToDate;
            if (latestVersion != null
                && Version.TryParse(report.Version, out var installed)
                && installed < latestVersion)
            {
                status = OfficeStatus.Outdated;
            }

            return new OfficeDeviceRow
            {
                DeviceId = report.DeviceId,
                DeviceName = report.DeviceName,
                Version = report.Version,
                LastReportAt = reportedAt,
                Status = status,
            };
        }

        private static OfficeStatusRow BuildRow(
            string prefix,
            string fallbackLocationName,
            IReadOnlyDictionary<string, OfficeDevices> byPrefix,
            string currentPrefix)
        {
            if (!byPrefix.TryGetValue(prefix, out var data))
                return NoData(prefix, fallbackLocationName, currentPrefix);

            var fresh = data.Devices.Where(d => d.Status != OfficeStatus.NoData).ToList();
            if (fresh.Count == 0)
            {
                // Отчёты есть, но все протухли: статус «нет данных», при этом
                // устройства офиса остаются видны (последние известные версии).
                return NoData(prefix, fallbackLocationName, currentPrefix, data.Devices);
            }

            var status = fresh.Any(d => d.Status == OfficeStatus.Outdated)
                ? OfficeStatus.Outdated
                : OfficeStatus.UpToDate;

            // Новейший свежий отчёт — его версия/время показываются в шапке карточки.
            var newest = fresh.OrderByDescending(d => d.LastReportAt ?? DateTimeOffset.MinValue).First();

            return new OfficeStatusRow
            {
                Prefix = prefix,
                LocationName = string.IsNullOrWhiteSpace(data.LocationName)
                    ? fallbackLocationName
                    : data.LocationName,
                Version = newest.Version,
                LastReportAt = newest.LastReportAt,
                Status = status,
                IsCurrentOffice = prefix == currentPrefix,
                DeviceCount = data.Devices.Count,
                Devices = data.Devices,
            };
        }

        private static OfficeStatusRow NoData(
            string prefix,
            string locationName,
            string currentPrefix,
            IReadOnlyList<OfficeDeviceRow>? devices = null) => new()
        {
            Prefix = prefix,
            LocationName = locationName,
            Version = "—",
            LastReportAt = null,
            Status = OfficeStatus.NoData,
            IsCurrentOffice = prefix == currentPrefix,
            DeviceCount = devices?.Count ?? 0,
            Devices = devices ?? Array.Empty<OfficeDeviceRow>(),
        };

        /// <summary>Название офиса (из отчётов) + устройства этого офиса.</summary>
        private sealed record OfficeDevices(string LocationName, IReadOnlyList<OfficeDeviceRow> Devices);
    }
}
