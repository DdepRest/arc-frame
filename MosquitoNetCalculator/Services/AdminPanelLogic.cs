using System;
using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// ЧИСТАЯ ПОЛИТИКА панели «Обновления» админки: все строки-решения и
    /// решения «что показать» — в одном месте, без WPF и IO. Контрол
    /// (<see cref="MosquitoNetCalculator.Controls.AdminPanelControl"/>) только
    /// применяет результаты к элементам. Владельцы состояния:
    /// • отчёты/файлы gist — <see cref="OfficeReportService"/> (домен);
    /// • вычисленные строки офисов — <see cref="OfficeStatusCalculator"/> (домен);
    /// • vis-состояние контролов — сам контрол (презентация);
    /// • эта статика — единственный владелец ФРАЗ и правил «что показывать».
    /// Ничего не знает о WPF-типах (Visibility и т.п.) — только факты и строки.
    /// </summary>
    internal static class AdminPanelLogic
    {
        // ─── Пустые состояния вкладки «Обновления» ───

        /// <summary>Данных нет вовсе (отчётов ни от одного офиса).</summary>
        public static bool IsNoData(IReadOnlyList<OfficeStatusRow> rows) => rows.Count == 0;

        /// <summary>Есть данные, но поиск/фильтр отсеял всё.</summary>
        public static bool IsFilterNoMatch(IReadOnlyList<OfficeStatusRow> rows, IEnumerable<object> view) =>
            rows.Count > 0 && !view.Any();

        /// <summary>Заголовок пустого состояния: свести оба случая в одну фразу.</summary>
        public static string EmptyTitle(bool filterNoMatch) =>
            filterNoMatch ? "Ничего не найдено" : "Отчёты ещё не получены";

        /// <summary>Подсказка пустого состояния.</summary>
        public static string EmptyHint(bool filterNoMatch) =>
            filterNoMatch
                ? "Измените поиск или выберите фильтр «Все»."
                : "Запустите программу в каждом офисе — здесь появятся статусы.";

        // ─── Подсказка сводной карточки (единственный владелец фразы) ───

        /// <summary>
        /// Подсказка аффордансов сводки: описывает оба реальных способа
        /// отправить напоминание. Пустая строка, когда напоминание не нужно.
        /// </summary>
        public static string SummaryHint(int outdatedOffices) =>
            outdatedOffices > 0
                ? "«Напоминание всем» — одно сообщение по всем устаревшим офисам; клик по карточке — напоминание только для неё."
                : "Все устройства в актуальной версии.";

        // ─── Маркер «недавно отвязаны» ───

        /// <summary>
        /// Текст маркера недавно отвязанных (gist после удаления файла ничего
        /// о них не несёт — честный in-session сигнал); null = маркер скрыть.
        /// </summary>
        public static string? JustUnboundText(IReadOnlyCollection<string> deviceLabels) =>
            deviceLabels.Count == 0
                ? null
                : "Недавно отвязаны: " + string.Join(", ", deviceLabels) + " — вернутся с отчётом";

        /// <summary>deviceId отвязанных, вернувшихся с отчётом (их убрать из маркера).</summary>
        public static IReadOnlyList<string> JustUnboundReturned(
            IEnumerable<string> justUnboundIds, IEnumerable<string> reportingDeviceIds) =>
            justUnboundIds.Intersect(reportingDeviceIds.ToHashSet()).ToList();

        // ─── Тексты напоминаний (буфер обмена) ───

        /// <summary>«доступна новая версия vX» / без «v…», когда GitHub недоступен.</summary>
        public static string VersionLine(Version? latestVersion) =>
            latestVersion != null
                ? $"В программе «A.R.C. Frame» доступна новая версия v{latestVersion}.\n"
                : "В программе «A.R.C. Frame» доступна новая версия.\n";

        /// <summary>Фолбэк-ссылка, когда адрес скачивания неизвестен.</summary>
        public const string FallbackDownloadUrl =
            "(ссылка недоступна — см. последнюю версию в разделе «Обновления»)";

        /// <summary>Одиночное напоминание устаревшему офису (клик по карточке).</summary>
        public static string SingleReminderText(
            string locationName, string? outdatedDeviceLabel, string outdatedDeviceVersion,
            Version? latestVersion, string downloadUrl)
        {
            string currentLine = outdatedDeviceLabel != null
                ? $"На устройстве «{outdatedDeviceLabel}» установлена v{outdatedDeviceVersion}.\n"
                : $"Текущая версия у вас: v{outdatedDeviceVersion}.\n";

            return
                "Здравствуйте! 👋\n\n" +
                VersionLine(latestVersion) +
                $"Скачайте: {downloadUrl}\n\n" +
                currentLine +
                "После обновления программа сама отчитается — спасибо!";
        }

        /// <summary>Напоминание-дайджест по нескольким устаревшим офисам.</summary>
        public static string BulkReminderText(
            IReadOnlyList<(string LocationName, string DeviceLabel, string Version)> outdatedDevices,
            IReadOnlyList<string> fallbackOfficeLines,
            Version? latestVersion, string downloadUrl)
        {
            var lines = outdatedDevices
                .Select(d => $"• {d.LocationName} — «{d.DeviceLabel}»: установлена v{d.Version}")
                .ToList();
            string body = lines.Count > 0
                ? string.Join("\n", lines)
                : string.Join("\n", fallbackOfficeLines);

            return
                "Здравствуйте! 👋\n\n" +
                VersionLine(latestVersion) +
                $"Скачайте: {downloadUrl}\n\n" +
                "Требуют обновления:\n" +
                body + "\n\n" +
                "После обновления программа сама отчитается — спасибо!";
        }

        // ─── Шапка «обновлено … · авто через …» ───

        /// <summary>Строка «обновлено {when}{ · авто через N}» для шапки панели.</summary>
        public static string RefreshStatusText(
            DateTimeOffset lastRefreshedLocal, DateTimeOffset nowLocal, TimeSpan? autoInterval)
        {
            if (lastRefreshedLocal == default) return string.Empty;

            var secondsAgo = (int)Math.Floor((nowLocal - lastRefreshedLocal).TotalSeconds);
            string when = secondsAgo < 5 ? "только что" :
                          secondsAgo < 60 ? $"{secondsAgo} сек назад" :
                          $"{lastRefreshedLocal:HH:mm}";

            string autoPart = autoInterval switch
            {
                null => "",
                { TotalHours: >= 1 } h => $" · авто каждые {h.TotalHours:0} ч",
                { TotalMinutes: >= 1 } m => $" · авто через {Math.Max(1, (int)Math.Ceiling(m.TotalMinutes - secondsAgo / 60.0))} мин",
                { } s => $" · авто через {Math.Max(1, (int)Math.Ceiling(s.TotalSeconds - secondsAgo))} сек",
            };

            return $"обновлено {when}{autoPart}";
        }
    }
}
