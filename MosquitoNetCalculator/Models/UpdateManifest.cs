using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// DTO для манифеста releases.json, скачиваемого с GitHub Releases.
    /// Поля: latest, minRequired, releases[] с version/url/size/sha256.
    /// </summary>
    public class UpdateManifest
    {
        /// <summary>Версия последнего релиза для быстрого сравнения (например "3.34.0").</summary>
        [JsonPropertyName("latest")]
        public string Latest { get; set; } = "";

        /// <summary>Минимальная версия для автообновления. Пустая строка = без ограничений.</summary>
        [JsonPropertyName("minRequired")]
        public string MinRequired { get; set; } = "";

        /// <summary>Список всех релизов (первый в массиве — самый свежий).</summary>
        [JsonPropertyName("releases")]
        public List<ReleaseInfo> Releases { get; set; } = new();
    }

    /// <summary>
    /// DTO для одной записи релиза внутри манифеста.
    /// </summary>
    public class ReleaseInfo
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("date")]
        public string Date { get; set; } = "";

        /// <summary>Одно из: Новинка / Улучшение / Исправление.</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("changes")]
        public List<string> Changes { get; set; } = new();

        /// <summary>URL к .zip на github.com (download URL, не API).</summary>
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        /// <summary>Размер .zip в байтах (для progress bar).</summary>
        [JsonPropertyName("size")]
        public long Size { get; set; }

        /// <summary>SHA-256 хеш .zip в hex-формате (lowercase).</summary>
        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = "";
    }
}
