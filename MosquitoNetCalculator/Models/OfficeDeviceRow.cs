using System;

namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// Одно устройство (ПК) офиса в админ-панели. В одном офисе может быть
    /// несколько устройств — каждое шлёт свой отчёт в gist, и панель строит
    /// строку офиса + список устройств (сколько их и какая версия у каждого).
    /// Иммутабельна; статусы считает <see cref="Services.OfficeStatusCalculator"/>.
    /// </summary>
    public sealed class OfficeDeviceRow
    {
        /// <summary>Стабильный ID устройства (GUID) или «» для легаси-отчётов.</summary>
        public string DeviceId { get; init; } = "";

        /// <summary>Имя устройства (имя ПК) или «» если не известно.</summary>
        public string DeviceName { get; init; } = "";

        /// <summary>Версия из отчёта устройства, «—» если отчёта нет.</summary>
        public string Version { get; init; } = "—";

        /// <summary>Время последнего отчёта устройства (UTC), null если отчёта нет.</summary>
        public DateTimeOffset? LastReportAt { get; init; }

        public OfficeStatus Status { get; init; }

        /// <summary>
        /// Подпись устройства для чипа: имя ПК, иначе короткий ID,
        /// иначе обобщённое «устройство» (легаси-отчёты).
        /// </summary>
        public string DeviceLabel
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(DeviceName)) return DeviceName;
                if (!string.IsNullOrWhiteSpace(DeviceId)) return DeviceId.Length > 8 ? DeviceId[..8] : DeviceId;
                return "устройство";
            }
        }

        /// <summary>Русский текст статуса устройства для тултипа/бейджа.</summary>
        public string StatusText => Status switch
        {
            OfficeStatus.UpToDate => "Актуальна",
            OfficeStatus.Outdated => "Устарела",
            _ => "Нет данных",
        };

        /// <summary>Юникод-глиф статуса: ✓ — актуальна, ⚠ — устарела, ? — нет данных.</summary>
        public string StatusGlyph => Status switch
        {
            OfficeStatus.UpToDate => "\u2713",   // ✓
            OfficeStatus.Outdated => "\u26A0",   // ⚠
            _ => "\u2753",                       // ❓
        };

        /// <summary>
        /// Время последнего отчёта для отображения (локальное): «сегодня, 14:32»,
        /// «вчера, 09:10», «3 дн. назад», иначе дата; «—» если отчёта нет.
        /// </summary>
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

        /// <summary>Полная подпись для тултипа чипа устройства.</summary>
        public string ToolTipText => $"{DeviceLabel} · {StatusText} · последний отчёт: {LastReportDisplay}";
    }
}
