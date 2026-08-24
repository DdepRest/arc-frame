using System;
using System.Globalization;
using System.Text.Json.Serialization;

namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// Отчёт устройства о своей версии — содержимое файла
    /// <c>office-{prefix}-{deviceId}.json</c> в секретном GitHub Gist
    /// (единое хранилище отчётов всех офисов).
    /// Каждое УСТРОЙСТВО обновляет ТОЛЬКО свой файл (в одном офисе может
    /// быть несколько ПК — они не перетирают друг друга); админ-панель
    /// читает весь gist, группирует по офисам и строит статусы.
    /// </summary>
    public sealed class OfficeReport
    {
        /// <summary>Номер офиса (префикс договора, см. LocationOptions).</summary>
        [JsonPropertyName("prefix")]
        public string Prefix { get; set; } = "";

        /// <summary>
        /// Стабильный ID устройства (GUID из settings.json, см.
        /// AppSettingsService.LoadOrCreateDeviceId). Старые отчёты без него
        /// читаются как пустая строка — они представляют «легаси»-устройство офиса.
        /// </summary>
        [JsonPropertyName("deviceId")]
        public string DeviceId { get; set; } = "";

        /// <summary>
        /// Человекочитаемое имя устройства (Environment.MachineName) — чтобы
        /// в админ-панели было видно, КАКОЙ именно ПК офиса не обновился.
        /// Необязательное поле; старые отчёты читаются как пустая строка.
        /// </summary>
        [JsonPropertyName("deviceName")]
        public string DeviceName { get; set; } = "";

        /// <summary>Человекочитаемое название офиса, например «Красношапки 44 — „Дом Окон+”».</summary>
        [JsonPropertyName("locationName")]
        public string LocationName { get; set; } = "";

        /// <summary>Установленная версия программы (например "3.47.4").</summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        /// <summary>UTC ISO-8601 момент отправки отчёта.</summary>
        [JsonPropertyName("reportedAt")]
        public string ReportedAt { get; set; } = "";

        /// <summary>
        /// Кол-во заказов, сохранённых в программе на этом ПК (файлы в %AppData%).
        /// Дополнительное поле для секции «Статистика» админ-панели; старые отчёты
        /// без него читаются как 0 — формат обратно совместим.
        /// </summary>
        [JsonPropertyName("orderCount")]
        public int OrderCount { get; set; }

        /// <summary>
        /// Распарсенное время отчёта (UTC). Null, если поле отсутствует или битое —
        /// такой отчёт считается «нет данных».
        /// </summary>
        [JsonIgnore]
        public DateTimeOffset? ReportedAtUtc =>
            DateTimeOffset.TryParse(ReportedAt, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dt)
                ? dt
                : null;
    }
}
