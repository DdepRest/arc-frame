using System;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Неназойливая периодическая проверка местоположения.
    ///
    /// Зачем: местоположение определяет префикс нумерации договоров, и однажды
    /// выбранный не тот офис живёт в settings.json годами — ошибку никто не
    /// замечает. Вместо модального «выберите местоположение» (его откладывают
    /// не читая) программа раз в <see cref="ConfirmInterval"/> тихо показывает
    /// полосу под заголовком на старте, а закрытие «✕» откладывает проверку
    /// всего на <see cref="SnoozeInterval"/>.
    ///
    /// Контракт — по образцу <see cref="OfficeReportScheduler"/> /
    /// <see cref="UpdateCheckScheduler"/>: решение «пора ли спрашивать» —
    /// чистая функция <see cref="ShouldPrompt"/>, тестируется без WPF и без
    /// файлов; таймеров нет вообще, единственный триггер — запуск программы.
    /// </summary>
    public static class LocationCheckService
    {
        /// <summary>
        /// Срок «годности» подтверждения: раз в 30 дней выбор стоит сверить.
        /// </summary>
        public static readonly TimeSpan ConfirmInterval = TimeSpan.FromDays(30);

        /// <summary>
        /// Отсрочка по «✕» в баннере: закрытие не отменяет проверку, а
        /// переносит её на 3 дня — ошибка не проживёт месяцы, но и напоминание
        /// не вернётся в тот же день.
        /// </summary>
        public static readonly TimeSpan SnoozeInterval = TimeSpan.FromDays(3);

        /// <summary>
        /// Пора ли показывать проверку местоположения.
        /// </summary>
        /// <param name="confirmedAtUtc">
        /// Когда пользователь последний раз подтвердил выбор (UTC).
        /// <c>null</c> — подтверждения не было: чистая установка, обновление
        /// с версии без отметки, битая дата в settings.json.
        /// </param>
        /// <param name="snoozedUntilUtc">
        /// До какого момента проверка отложена (UTC) или <c>null</c>.
        /// </param>
        /// <param name="nowUtc">Текущий момент (UTC).</param>
        public static bool ShouldPrompt(
            DateTime? confirmedAtUtc,
            DateTime? snoozedUntilUtc,
            DateTime nowUtc)
        {
            DateTime now = AsUtc(nowUtc);

            // Мягкая отсрочка сильнее срока годности: закрытый «✕» баннер
            // не возвращается, пока не истекли 3 дня.
            if (snoozedUntilUtc.HasValue && now < AsUtc(snoozedUntilUtc.Value))
                return false;

            if (!confirmedAtUtc.HasValue)
                return true;

            // Отрицательную разницу (часы съехали назад, отметка «из будущего»)
            // считаем свежим подтверждением: лишний вопрос хуже, чем пропуск.
            return now - AsUtc(confirmedAtUtc.Value) >= ConfirmInterval;
        }

        /// <summary>
        /// Подсказка для чипа устройства в админ-панели: когда местоположение
        /// давно не подтверждали. Пустая строка — всё в порядке (свежее
        /// подтверждение), чтобы не засорять чип шумом.
        ///
        /// <c>null</c> (старый отчёт без поля или устройство ни разу не
        /// подтверждало) — тоже повод показать подсказку: молчание здесь
        /// означало бы «всё хорошо» там, где данных просто нет.
        /// </summary>
        public static string BuildAdminHint(DateTimeOffset? confirmedAtUtc, DateTimeOffset now)
        {
            if (!confirmedAtUtc.HasValue)
                return "локация не подтверждена";

            var age = now - confirmedAtUtc.Value;
            if (age < ConfirmInterval)
                return string.Empty;

            // Полные сутки: 30 дней 23 часа — это «30 дн.», а не «31».
            int days = (int)Math.Floor(age.TotalDays);
            return $"локация не подтверждена {days} дн.";
        }

        /// <summary>
        /// Текст для баннера: имя офиса + префикс договора, например
        /// «Рудакова 76 — «Компания „Уют”» · префикс 2». На установках, где имя
        /// локации ещё не сохранялось (старый settings.json), остаётся честное
        /// «префикс договора 2» — без пустой строки на месте названия.
        /// </summary>
        public static string BuildSummary(string? locationName, string? prefix)
        {
            string name = (locationName ?? string.Empty).Trim();
            string prefixText = (prefix ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(prefixText))
                return string.IsNullOrEmpty(name) ? "местоположение не задано" : name;

            if (string.IsNullOrEmpty(name))
                return $"префикс договора {prefixText}";

            return $"{name} · префикс {prefixText}";
        }

        /// <summary>
        /// Приводит момент к UTC. <see cref="DateTimeKind.Local"/> пересчитывается,
        /// <see cref="DateTimeKind.Unspecified"/> трактуется как уже UTC —
        /// в settings.json отметки пишутся только в UTC.
        /// </summary>
        private static DateTime AsUtc(DateTime value)
            => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
    }
}
