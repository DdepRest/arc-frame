using System;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Короткое «последняя связь» для чипа устройства в админ-панели: показывает
    /// давность последнего отчёта, когда она уже заметна («вчера 09:10»,
    /// «3 дн. назад»), и молчит для свежих устройств — статус чипа и так правдив.
    /// Чистая функция времени → текст; ноль состояния, легко тестируется.
    /// </summary>
    internal static class DeviceLastSeenHint
    {
        /// <summary>Тише для свежих отчётов: давность меньше порога не показываем.</summary>
        public static readonly TimeSpan StaleHintAfter = TimeSpan.FromHours(12);

        /// <summary>
        /// «· вчера 09:10» / «· 3 дн. назад» / «· 12.05.2026» — или пусто,
        /// если отчёта нет (чип уже «устройство ❓») или он свежее порога.
        /// </summary>
        public static string Text(DateTimeOffset? lastReportAtUtc, DateTimeOffset nowUtc)
        {
            if (lastReportAtUtc == null) return string.Empty;

            var age = nowUtc - lastReportAtUtc.Value;
            if (age < StaleHintAfter) return string.Empty;

            var local = lastReportAtUtc.Value.ToLocalTime();
            var today = nowUtc.ToLocalTime().Date;

            if (local.Date == today) return $"· сегодня {local:HH:mm}";
            if (local.Date == today.AddDays(-1)) return $"· вчера {local:HH:mm}";
            if (local.Date >= today.AddDays(-6))
                return $"· {(int)(today - local.Date).TotalDays} дн. назад";

            return $"· {local:dd.MM.yyyy}";
        }
    }
}
