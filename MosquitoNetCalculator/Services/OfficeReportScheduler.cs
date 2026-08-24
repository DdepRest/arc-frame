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

        private DispatcherTimer? _timer;
        private DateTime _lastSendTime = DateTime.MinValue;

        /// <summary>Интервал между периодическими отправками.</summary>
        public TimeSpan Interval { get; set; } = DefaultInterval;

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
            _timer = new DispatcherTimer { Interval = Interval };
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
        }

        /// <summary>
        /// Отметка «отправка только что стартовала» — вызывается в <see cref="OnTick"/>
        /// перед fire-and-forget вызовом <see cref="OnSendDue"/>. Публичный метод
        /// (как UpdateCheckScheduler.MarkChecked) — для тестов и ручного сброса.
        /// </summary>
        public void MarkSent() => _lastSendTime = Now();

        /// <summary>
        /// Pure-логика «пора ли отправлять»: прошло ≥ Interval с последней отправки.
        /// MinValue (отправок не было) — разрешено (первый Start).
        /// </summary>
        public bool ShouldSendAt(DateTime now)
        {
            if (_lastSendTime == DateTime.MinValue) return true;
            return now - _lastSendTime >= Interval;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            var now = Now();
            if (!ShouldSendAt(now)) return;
            _lastSendTime = now;
            if (OnSendDue != null)
                _ = SafeInvoke(OnSendDue);
        }

        /// <summary>Fire-and-forget с try/catch: ловит синхронные броски (до возврата Task).</summary>
        private static async Task SafeInvoke(Func<Task> callback)
        {
            try
            {
                await callback().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OfficeReportScheduler] OnSendDue threw: {ex}");
            }
        }
    }
}
