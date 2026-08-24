using System;

namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// Строка секции «Статистика» админ-панели: офис + кол-во заказов в его программах.
    /// Считается из тех же отчётов gist, что и статусы обновлений: при нескольких
    /// устройствах в офисе заказы суммируются по всем устройствам.
    /// </summary>
    public sealed class OfficeStatsRow
    {
        public string Prefix { get; init; } = "";
        public string LocationName { get; init; } = "";

        /// <summary>Кол-во заказов офиса (сумма по устройствам); null = отчёта нет.</summary>
        public int? OrderCount { get; init; }

        /// <summary>Кол-во устройств офиса, приславших отчёт.</summary>
        public int DeviceCount { get; init; }

        /// <summary>Русская форма счёта устройств для чипа: «1 устройство», «2 устройства», «5 устройств».</summary>
        public string DeviceCountDisplay
        {
            get
            {
                if (DeviceCount <= 0) return "";
                int m100 = DeviceCount % 100;
                int m10 = DeviceCount % 10;
                string noun = (m100 >= 11 && m100 <= 14) || m10 == 0 || (m10 >= 5 && m10 <= 9)
                    ? "устройств"
                    : m10 == 1
                        ? "устройство"
                        : "устройства";
                return $"{DeviceCount} {noun}";
            }
        }

        /// <summary>Время последнего отчёта (UTC), null если отчёта нет.</summary>
        public DateTimeOffset? LastReportAt { get; init; }

        public bool IsCurrentOffice { get; init; }

        public string OrderCountDisplay => OrderCount?.ToString() ?? "—";

        public string LastReportDisplay
        {
            get
            {
                if (LastReportAt == null) return "—";
                var local = LastReportAt.Value.ToLocalTime();
                var now = DateTimeOffset.Now;
                var today = now.Date;
                if (local.Date == today) return $"сегодня, {local:HH:mm}";
                if (local.Date == today.AddDays(-1)) return $"вчера, {local:HH:mm}";
                if (local.Date >= today.AddDays(-6)) return $"{(int)(today - local.Date).TotalDays} дн. назад";
                return local.ToString("dd.MM.yyyy");
            }
        }
    }
}
