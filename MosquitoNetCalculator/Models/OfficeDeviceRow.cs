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
        /// Отметка администратора в режиме «Отвязать выбранные» (только UI-
        /// состояние, не бизнес-данные). Живёт НА МОДЕЛИ, а не в отдельном
        /// словаре панели: виртуализирующий список переиспользует контейнеры,
        /// и TwoWay-биндинг при каждой подготовке контейнера перечитывает
        /// актуальное значение — визуальное состояние не может разойтись
        /// с тем, что реально будет отвязано (playtest поймал такой «ghost»).
        /// </summary>
        public bool IsChecked { get; set; }

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

        /// <summary>Юникод-глиф статуса (Segoe Fluent Icons, рендерится TextBlock'ом
        /// с FontFamily="Segoe Fluent Icons"): E73E ✓ — актуальна, E7BA ⚠ — устарела,
        /// E9CE ? — нет данных.</summary>
        public string StatusGlyph => Status switch
        {
            OfficeStatus.UpToDate => "\uE73E",   // CheckMark
            OfficeStatus.Outdated => "\uE7BA",   // Warning
            _ => "\uE9CE",                       // Unknown
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

        /// <summary>
        /// Короткое «последняя связь» на чипе: «· сегодня 14:32» / «· вчера 09:10» /
        /// «· 3 дн. назад» — чтобы отличить «ПК выключен неделю» от «отчитался
        /// час назад» (аудит SPEC). Пусто для свежего (< StaleHintAfter) —
        /// текущий статус и так правдив; чистая логика в
        /// <see cref="MosquitoNetCalculator.Services.DeviceLastSeenHint"/>.
        /// </summary>
        public string LastSeenHint => Services.DeviceLastSeenHint.Text(LastReportAt, DateTimeOffset.Now);
    }
}
