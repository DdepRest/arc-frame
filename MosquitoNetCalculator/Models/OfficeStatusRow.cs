using System;
using System.Collections.Generic;

namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// Статус офиса в админ-панели.
    /// </summary>
    public enum OfficeStatus
    {
        /// <summary>Отчёт свежий и версия == последний релиз (или последняя версия неизвестна).</summary>
        UpToDate,

        /// <summary>Отчёт свежий, но версия &lt; последнего релиза — офис не обновился.</summary>
        Outdated,

        /// <summary>Отчёта нет, он старше порога свежести, или версия/время не читаются.</summary>
        NoData
    }

    /// <summary>
    /// Одна строка админ-панели: офис + вычисленный статус.
    /// Иммутабельна; статусы считает <see cref="Services.OfficeStatusCalculator"/>.
    /// В одном офисе может быть несколько устройств — строка агрегирует их
    /// (Status — по свежим устройствам, Devices — детализация по каждому).
    /// </summary>
    public sealed class OfficeStatusRow
    {
        public string Prefix { get; init; } = "";
        public string LocationName { get; init; } = "";

        /// <summary>Версия из новейшего свежего отчёта офиса, «—» если отчёта нет.</summary>
        public string Version { get; init; } = "—";

        /// <summary>Время последнего отчёта (UTC), null если отчёта нет.</summary>
        public DateTimeOffset? LastReportAt { get; init; }

        public OfficeStatus Status { get; init; }

        /// <summary>True для офиса, на котором сейчас открыта панель.</summary>
        public bool IsCurrentOffice { get; init; }

        /// <summary>Кол-во устройств офиса, приславших отчёт (свежих и нет).</summary>
        public int DeviceCount { get; init; }

        /// <summary>Детализация по устройствам офиса (версия и статус каждого ПК).</summary>
        public IReadOnlyList<OfficeDeviceRow> Devices { get; init; } = Array.Empty<OfficeDeviceRow>();

        /// <summary>
        /// Русская форма счёта устройств для чипа: «1 устройство»,
        /// «2 устройства», «5 устройств»; пустая строка при 0.
        /// </summary>
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

        /// <summary>Русский текст статуса для бейджа.</summary>
        public string StatusText => Status switch
        {
            OfficeStatus.UpToDate => "Актуальна",
            OfficeStatus.Outdated => "Устарела",
            _ => "Нет данных",
        };

        /// <summary>
        /// Юникод-глиф статуса для иконки в карточке: ✓ — актуальна,
        /// ! — устарела, ? — нет данных. Рядом с цветом помогает глазу
        /// быстро «цеплять» статус даже без цвета.
        /// </summary>
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
    }
}
