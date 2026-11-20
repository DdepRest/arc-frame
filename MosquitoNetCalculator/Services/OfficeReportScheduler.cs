using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Периодическая отправка отчёта офиса в gist (админ-панель).
    /// Пока программа открыта, каждые <see cref="DefaultInterval"/> (2 ч)
    /// тихо шлётся свежий отчёт — статусы и статистика в панели остаются
    /// актуальными в течение рабочего дня. Стартовый отчёт уходит при запуске
    /// программы (MainWindow_Loaded), так что редкий интервал не «слепит»
    /// панель: свежесть гарантирована запуском + раз в 2 часа.
    ///
    /// Контракт (по образцу <see cref="UpdateCheckScheduler"/>):
    /// • Tick — через <see cref="DispatcherTimer"/> на UI-потоке.
    /// • Решение «пора ли слать» — чистая функция <see cref="ShouldSendAt"/>
    ///   (тестируется без WPF).
    /// • <see cref="OnSendDue"/> вызывается fire-and-forget с try/catch-обёрткой.
    /// • Start ставит <see cref="LastSendTime"/> = Now(): стартовый отчёт уже
    ///   отправлен в MainWindow_Loaded, первый периодический — через Interval.
    /// </summary>
    public sealed class OfficeReportScheduler
    {
        /// <summary>
        /// Production-интервал между периодическими отправками: 2 часа.
        /// (Редкие автоматические считки: запуск программы + каждые 2 ч.)
        /// </summary>
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(2);

        /// <summary>
        /// Быстрый интервал повтора при сбое отправки (30 сек):
        /// если стартовая отправка или периодический отчёт не удались
        /// (например, сеть ещё инициализируется после загрузки ОС),
        /// повторяем через 30 секунд вместо ожидания 2 часов.
        /// </summary>
        public static readonly TimeSpan RapidRetryInterval = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Допустимая погрешность системного таймера (1 сек) для защиты от пропуска
        /// тика из-за джиттера таймера Windows (например, тик на несколько миллисекунд раньше).
        /// </summary>
        public static readonly TimeSpan TimerJitterTolerance = TimeSpan.FromSeconds(1);

        private DispatcherTimer? _timer;
        private DateTime _lastSendTime = DateTime.MinValue;
        private DateTime? _retryDueTime;

        /// <summary>Интервал между периодическими отправками.</summary>
        public TimeSpan Interval { get; set; } = DefaultInterval;

        /// <summary>Интервал повтора при сбое отправки.</summary>
        public TimeSpan RetryInterval { get; set; } = RapidRetryInterval;

        /// <summary>True, если запланирован быстрый повтор.</summary>
        public bool IsRetryPending => _retryDueTime.HasValue;

        /// <summary>Время запланированного повтора (или null).</summary>
        public DateTime? RetryDueTime => _retryDueTime;

        /// <summary>Текущий интервал внутреннего таймера (для тестов).</summary>
        public TimeSpan? CurrentTimerInterval => _timer?.Interval;

        /// <summary>
        /// Источник времени. Production-default <c>() => DateTime.Now</c>;
        /// в тестах подменяется на фейковый провайдер.
        /// </summary>
        public Func<DateTime> Now { get; set; } = () => DateTime.Now;

        /// <summary>Callback «пора отправить отчёт». Вызывается fire-and-forget.</summary>
        public Func<Task>? OnSendDue { get; set; }

        /// <summary>True пока таймер активен (Start был вызван и Stop — нет).</summary>
        public bool IsRunning => _timer != null;

        /// <summary>Время последней отметки «отправка стартовала» (или MinValue).</summary>
        public DateTime LastSendTime => _lastSendTime;

        /// <summary>
        /// Запускает таймер. Идемпотентно: повторный вызов без <see cref="Stop"/> — no-op.
        /// Ставит <c>LastSendTime = Now()</c>, чтобы первый периодический отчёт ушёл
        /// через Interval (стартовый уже отправлен в MainWindow_Loaded).
        /// </summary>
        public void Start()
        {
            if (_timer != null) return;
            _lastSendTime = Now();
            var timerInterval = _retryDueTime.HasValue
                ? (_retryDueTime.Value > _lastSendTime ? _retryDueTime.Value - _lastSendTime : RetryInterval)
                : Interval;
            if (timerInterval <= TimeSpan.Zero)
                timerInterval = RetryInterval;
            _timer = new DispatcherTimer { Interval = timerInterval };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        /// <summary>Останавливает таймер. Идемпотентно; state сохраняется.</summary>
        public void Stop()
        {
            if (_timer == null) return;
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
            _retryDueTime = null;
        }

        /// <summary>
        /// Планирует быстрый повтор отправки (по умолчанию через <see cref="RapidRetryInterval"/>, 30 сек).
        /// Переводит таймер на интервал повтора, если таймер уже запущен.
        /// Потокобезопасно: вызов из фонового потока маршалится в UI-поток таймера.
        /// </summary>
        public void ScheduleRetry(TimeSpan? delay = null)
        {
            var retryDelay = delay ?? RetryInterval;
            _retryDueTime = Now().Add(retryDelay);

            void ApplyTimerInterval()
            {
                if (_timer == null) return;
                _timer.Stop();
                _timer.Interval = retryDelay;
                _timer.Start();
            }

            if (_timer != null)
            {
                if (_timer.Dispatcher.CheckAccess())
                    ApplyTimerInterval();
                else
                    _timer.Dispatcher.BeginInvoke((Action)ApplyTimerInterval);
            }
        }

        /// <summary>
        /// Восстанавливает стандартный интервал таймера (2 ч), если таймер работал на интервале повтора.
        /// Потокобезопасно: вызов из фонового потока маршалится в UI-поток таймера.
        /// </summary>
        private void RestoreDefaultInterval()
        {
            void Apply()
            {
                if (_timer == null) return;
                if (_timer.Interval != Interval)
                    _timer.Interval = Interval;
            }

            if (_timer != null)
            {
                if (_timer.Dispatcher.CheckAccess())
                    Apply();
                else
                    _timer.Dispatcher.BeginInvoke((Action)Apply);
            }
        }

        /// <summary>
        /// Уведомление о результате отправки: при false планирует быстрый повтор (30 сек),
        /// при true — сбрасывает ожидание повтора и возвращает таймер на стандартный интервал.
        /// </summary>
        public void NotifySendResult(bool success)
        {
            if (success)
            {
                _retryDueTime = null;
                RestoreDefaultInterval();
            }
            else
            {
                ScheduleRetry();
            }
        }

        /// <summary>
        /// Отметка «отправка только что стартовала» — вызывается в <see cref="OnTick"/>
        /// перед fire-and-forget вызовом <see cref="OnSendDue"/>. Публичный метод
        /// (как UpdateCheckScheduler.MarkChecked) — для тестов и ручного сброса.
        /// </summary>
        public void MarkSent()
        {
            _lastSendTime = Now();
            _retryDueTime = null;
            RestoreDefaultInterval();
        }

        /// <summary>
        /// Pure-логика «пора ли отправлять»: прошло ≥ Interval с последней отправки
        /// или наступило время запланированного быстрого повтора (при предыдущем сбое).
        /// Включает защиту от джиттера системного таймера Windows (<see cref="TimerJitterTolerance"/>).
        /// MinValue (отправок не было) — разрешено (первый Start).
        /// </summary>
        public bool ShouldSendAt(DateTime now)
        {
            if (_retryDueTime.HasValue)
                return now >= _retryDueTime.Value - TimerJitterTolerance;

            if (_lastSendTime == DateTime.MinValue) return true;
            return now - _lastSendTime >= Interval - TimerJitterTolerance;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            var now = Now();
            if (!ShouldSendAt(now))
            {
                // Если retry был отменён, но таймер тикнул с нестандартным интервалом —
                // восстанавливаем базовый интервал (защита от утечки 30-секундного тика).
                if (!_retryDueTime.HasValue && _timer != null && _timer.Interval != Interval)
                {
                    _timer.Interval = Interval;
                }
                return;
            }
            _lastSendTime = now;
            _retryDueTime = null;
            if (_timer != null && _timer.Interval != Interval)
            {
                _timer.Interval = Interval;
            }
            if (OnSendDue != null)
                _ = SafeInvoke(OnSendDue);
        }

        /// <summary>Fire-and-forget с try/catch: ловит синхронные броски (до возврата Task).</summary>
        private async Task SafeInvoke(Func<Task> callback)
        {
            try
            {
                await callback().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OfficeReportScheduler] OnSendDue threw: {ex}");
                ScheduleRetry();
            }
        }
    }
}
