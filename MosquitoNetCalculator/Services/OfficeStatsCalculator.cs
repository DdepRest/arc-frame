using System;
using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Чистая логика секции «Статистика» админ-панели (без сети и UI) — покрыта тестами.
    /// Берёт те же отчёты gist, что и <see cref="OfficeStatusCalculator"/>, и показывает
    /// по каждому офису кол-во заказов в его программах.
    /// В одном офисе может быть несколько УСТРОЙСТВ — заказы суммируются по всем
    /// устройствам офиса (каждое устройство шлёт свой отчёт).
    /// </summary>
    public static class OfficeStatsCalculator
    {
        /// <summary>
        /// Строит строки статистики: все известные офисы из <paramref name="knownOffices"/>
        /// плюс неизвестные, найденные в отчётах. Для офиса без отчёта — OrderCount = null.
        /// </summary>
        public static IReadOnlyList<OfficeStatsRow> BuildRows(
            IEnumerable<LocationOption> knownOffices,
            IReadOnlyList<OfficeReport> reports,
            string currentPrefix)
        {
            var byPrefix = reports
                .GroupBy(r => r.Prefix)
                .ToDictionary(
                    g => g.Key,
                    g => new OfficeStatsData(
                        g.OrderByDescending(r => r.ReportedAtUtc ?? DateTimeOffset.MinValue).First().LocationName,
                        // Один ПК = одно устройство (дедупликация по имени машины),
                        // чтобы две копии программы на одном компьютере не считались
                        // дважды в статистике.
                        OfficeDeviceGrouping.DistinctDevices(g).ToList()));

            var rows = new List<OfficeStatsRow>();
            foreach (var office in knownOffices)
                rows.Add(BuildRow(office.Prefix, office.LocationName, byPrefix, currentPrefix));

            foreach (var (prefix, data) in byPrefix)
            {
                if (knownOffices.All(o => o.Prefix != prefix))
                    rows.Add(BuildRow(prefix, data.LocationName, byPrefix, currentPrefix));
            }

            return rows;
        }

        /// <summary>Суммарное кол-во заказов по всем офисам (null-значения пропускаются).</summary>
        public static int SumOrderCounts(IReadOnlyList<OfficeStatsRow> rows)
            => rows.Sum(r => r.OrderCount ?? 0);

        private static OfficeStatsRow BuildRow(
            string prefix,
            string fallbackLocationName,
            IReadOnlyDictionary<string, OfficeStatsData> byPrefix,
            string currentPrefix)
        {
            if (!byPrefix.TryGetValue(prefix, out var data))
                return NoData(prefix, fallbackLocationName, currentPrefix);

            // По каждому устройству берём новейший отчёт и суммируем заказы —
            // итог = кол-во заказов во всех программах офиса.
            var devices = data.Devices;
            var newest = devices.OrderByDescending(d => d.ReportedAtUtc ?? DateTimeOffset.MinValue).First();
            var reportedAt = newest.ReportedAtUtc;
            if (reportedAt == null)
                return NoData(prefix, fallbackLocationName, currentPrefix);

            return new OfficeStatsRow
            {
                Prefix = prefix,
                LocationName = string.IsNullOrWhiteSpace(data.LocationName)
                    ? fallbackLocationName
                    : data.LocationName,
                OrderCount = devices.Sum(d => d.OrderCount),
                DeviceCount = devices.Count,
                LastReportAt = reportedAt,
                IsCurrentOffice = prefix == currentPrefix,
            };
        }

        private static OfficeStatsRow NoData(string prefix, string locationName, string currentPrefix) => new()
        {
            Prefix = prefix,
            LocationName = locationName,
            OrderCount = null,
            LastReportAt = null,
            IsCurrentOffice = prefix == currentPrefix,
        };

        /// <summary>Название офиса (из отчётов) + устройства этого офиса (новейший отчёт каждого).</summary>
        private sealed record OfficeStatsData(string LocationName, IReadOnlyList<OfficeReport> Devices);
    }
}
