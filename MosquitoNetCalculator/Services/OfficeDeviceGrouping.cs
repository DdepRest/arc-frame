using System;
using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Идентичность устройств офиса при построении админ-панели (статусы/статистика).
    ///
    /// Проблема: одна и та же физическая машина может прислать НЕСКОЛЬКО отчётов
    /// с разными <c>deviceId</c> — например, когда на ПК запущены две копии
    /// программы (обычная версия + dev-сборка) или при гонке первой генерации ID.
    /// Чтобы панель не считала один ПК дважды, отчёты группируются по «человеческому»
    /// имени машины (<see cref="OfficeReport.DeviceName"/> = Environment.MachineName):
    /// один ПК = одно устройство (берётся новейший отчёт).
    ///
    /// Легаси-отчёты (без deviceId и имени — старый формат файла
    /// <c>office-{prefix}.json</c>) — это запись того же ПК ДО перехода на
    /// по-устройственные файлы. Если в офисе есть хоть одно именованное устройство,
    /// легаси-запись отбрасывается (иначе она дублировала бы уже обновившийся ПК);
    /// если именованных нет — легаси показывается как одно устройство офиса.
    /// </summary>
    internal static class OfficeDeviceGrouping
    {
        /// <summary>
        /// Возвращает по одному НОВЕЙШЕМУ отчёту на устройство офиса.
        /// Группировка: по имени машины (если есть), иначе по deviceId,
        /// иначе все легаси-отчёты — одно «легаси»-устройство.
        /// </summary>
        public static IReadOnlyList<OfficeReport> DistinctDevices(IEnumerable<OfficeReport> reports)
        {
            var groups = reports
                .GroupBy(DeviceKey)
                .Select(g => g.OrderByDescending(r => r.ReportedAtUtc ?? DateTimeOffset.MinValue).First())
                .ToList();

            // Офис с именованными устройствами уже «перенёс» легаси-запись
            // (тот же ПК до обновления) — не дублируем её отдельным устройством.
            bool hasNamed = groups.Any(r => !string.IsNullOrWhiteSpace(r.DeviceName));
            return hasNamed
                ? groups.Where(r => !IsAnonymousLegacy(r)).ToList()
                : groups;
        }

        private static string DeviceKey(OfficeReport r)
        {
            if (!string.IsNullOrWhiteSpace(r.DeviceName))
                return "name:" + r.DeviceName.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(r.DeviceId))
                return "id:" + r.DeviceId;
            return "legacy";
        }

        private static bool IsAnonymousLegacy(OfficeReport r)
            => string.IsNullOrWhiteSpace(r.DeviceId) && string.IsNullOrWhiteSpace(r.DeviceName);
    }
}
