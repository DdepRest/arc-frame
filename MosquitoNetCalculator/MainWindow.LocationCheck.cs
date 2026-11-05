using System;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator
{
    /// <summary>
    /// Периодическая проверка местоположения (неназойливый вариант).
    ///
    /// Решение «пора ли спрашивать» — чистая функция
    /// <see cref="LocationCheckService.ShouldPrompt"/>; здесь только проводка:
    /// показать полосу, записать ответ пользователя. Триггер ровно один —
    /// запуск программы (MainWindow_Loaded), поэтому напоминание не может
    /// всплыть посреди работы с заказом.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// Показывает баннер проверки, если с последнего подтверждения прошло
        /// 30 дней (или подтверждения не было вовсе) и нет активной отсрочки.
        /// Вызывается один раз за запуск, на старте.
        /// </summary>
        internal void MaybeShowLocationCheck()
        {
            if (!LocationCheckService.ShouldPrompt(
                    AppSettingsService.LoadLocationConfirmedAtUtc(),
                    AppSettingsService.LoadLocationSnoozeUntilUtc(),
                    DateTime.UtcNow))
                return;

            LocationBanner.Show(
                AppSettingsService.LoadLocationName(),
                AppSettingsService.LoadContractPrefix());
        }

        /// <summary>
        /// «Всё верно»: продлеваем тишину на 30 дней. Тост — единственная
        /// обратная связь: без него баннер беззвучно исчезает и непонятно,
        /// сохранилось ли что-то.
        /// </summary>
        private void LocationBanner_Confirmed(object sender, EventArgs e)
        {
            AppSettingsService.MarkLocationConfirmed();
            LocationBanner.Hide();
            ToastService.ShowToast("Местоположение подтверждено", ToastType.Success);
        }

        /// <summary>
        /// «Изменить»: открываем существующее окно выбора (оно само пишет
        /// отметку подтверждения). Отмена окна оставляет баннер на месте —
        /// иначе вопрос исчез бы без ответа.
        /// </summary>
        private void LocationBanner_ChangeRequested(object sender, EventArgs e)
        {
            if (OpenWelcomeWindow())
                LocationBanner.Hide();
        }

        /// <summary>
        /// «✕»: откладываем проверку на 3 дня. Не подтверждение — ошибка не
        /// проживёт месяцы, но и напоминание не вернётся в тот же день.
        /// </summary>
        private void LocationBanner_Snoozed(object sender, EventArgs e)
        {
            AppSettingsService.SaveLocationSnoozeUntilUtc(
                DateTime.UtcNow + LocationCheckService.SnoozeInterval);
            LocationBanner.Hide();
        }
    }
}
