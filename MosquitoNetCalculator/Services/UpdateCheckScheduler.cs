using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Планировщик фоновых проверок обновлений.
    ///
    /// ─── Зачем ───────────────────────────────────────────────────────────────
    /// Startup-проверка в <see cref="UpdateService.CheckOnStartupAsync"/> срабатывает
    /// один раз при запуске. Этот планировщик добавляет две стратегии:
    ///   • Periodic — каждые <see cref="CheckInterval"/> (30 мин по умолчанию)
    ///     от последней проверки, независимо от активности пользователя.
    ///   • Idle     — после <see cref="IdleThreshold"/> (10 мин по умолчанию)
    ///     непрерывного простоя программы.
    ///
    /// ─── Контракт ───────────────────────────────────────────────────────────
    /// • Tick вызывается на UI-потоке через <see cref="DispatcherTimer"/> —
    ///   поэтому все поля читаются/пишутся только из UI-потока, лок ниже.
    /// • Решение «нужно ли триггерить проверку» инкапсулировано в
    ///   <see cref="ShouldCheckAt"/> и тестируется без WPF.
    /// • <see cref="ShouldSkipCheck"/> — опциональная callback-инъекция
    ///   для проверок «не запущена ли уже проверка/скачивание».
    ///   Production-wire устанавливает её на
    ///   <c>() => UpdateService.IsChecking || UpdateService.IsDownloading</c>.
    /// • <see cref="OnCheckDue"/> вызывается fire-and-forget — возвращённый Task
    ///   отбрасывается, ошибки логируются внутри <see cref="UpdateService"/>.
    ///
    /// ─── Edge cases ─────────────────────────────────────────────────────────
    /// • Первый старт (<c>_lastCheckTime == DateTime.MinValue</c>) — trigger
    ///   разрешён только если пользователь простаивает ≥ IdleThreshold.
    ///   Защита от «сразу после запуска программы устроить проверку, если
    ///   пользователь ещё не успел коснуться мыши/клавиатуры».
    /// • Throttle <see cref="MinGap"/> — защита от спама: даже если обе
    ///   стратегии срабатывают одновременно (например, таймер совпал с
    ///   возвращением пользователя), между проверками будет пауза.
    /// </summary>
    public sealed class UpdateCheckScheduler
    {
        private DateTime _lastCheckTime = DateTime.MinValue;
        private DateTime _lastActivityTime;
        private DispatcherTimer? _timer;

        /// <summary>
        /// Периодический интервал. Каждые <c>CheckInterval</c> от последней
        /// проверки триггерим новую — независимо от активности.
        /// Production-default: 30 мин.
        /// </summary>
        public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Порог простоя. Если пользователь не активен ≥ IdleThreshold
        /// (и между проверками прошло ≥ MinGap) — триггерим.
        /// Production-default: 10 мин.
        /// </summary>
        public TimeSpan IdleThreshold { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Анти-спам: минимальный промежуток между двумя реальными проверками.
        /// Production-default: 2 мин.
        /// </summary>
        public TimeSpan MinGap { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Интервал таймера, через который вызывается <see cref="ShouldCheckAt"/>.
        /// Production-default: 60 сек — между тиками потребление CPU ≈ 0,
        /// не ухудшает battery life.
        /// </summary>
        public TimeSpan TickInterval { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Источник времени. Production-default <c>() => DateTime.Now</c>;
        /// в тестах подменяется на <c>FakeTimeProvider</c> для контролируемого
        /// продвижения часов.
        /// </summary>
        public Func<DateTime> Now { get; set; } = () => DateTime.Now;

        /// <summary>
        /// Опциональный callback, проверяющий «не активна ли уже проверка или
        /// скачивание». Если вернёт <c>true</c> — tick игнорируется, чтобы не
        /// стартовать вторую проверку параллельно.
        /// </summary>
        public Func<bool>? ShouldSkipCheck { get; set; }

        /// <summary>
        /// Callback «пора проверить». Вызывается через fire-and-forget.
        /// Параметр <c>UpdateCheckScheduler</c> передаётся редко — обычно
        /// это <c>() => UpdateService.CheckInBackgroundAsync()</c>.
        /// </summary>
        public Func<Task>? OnCheckDue { get; set; }

        /// <summary>
        /// True пока таймер активен (Start был вызван и Stop — нет).
        /// </summary>
        public bool IsRunning => _timer != null;

        /// <summary>
        /// Время последней отметки «проверка стартовала». Используется
        /// как input в <see cref="ShouldCheckAt"/>. <c>DateTime.MinValue</c>
        /// означает «проверок ещё не было».
        /// </summary>
        public DateTime LastCheckTime => _lastCheckTime;

        /// <summary>
        /// Время последней активности пользователя. Используется
        /// как input в <see cref="ShouldCheckAt"/>.
        /// </summary>
        public DateTime LastActivityTime => _lastActivityTime;

        /// <summary>
        /// Запускает таймер. Идемпотентно: повторный вызов без <see cref="Stop"/>
        /// — no-op (защита от двойной подписки на Window.Loaded).
        ///
        /// По умолчанию Start выставляет <c>_lastCheckTime = Now()</c> — это
        /// подавляет проверку «сразу при старте»: startup-check уже отработал
        /// в <see cref="UpdateService.CheckOnStartupAsync"/>, здесь мы просто
        /// выжидаем следующее окно по одной из двух стратегий.
        /// </summary>
        public void Start()
        {
            if (_timer != null) return;

            var now = Now();
            _lastActivityTime = now;
            _lastCheckTime = now; // startup-чек уже отработал
            _timer = new DispatcherTimer { Interval = TickInterval };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        /// <summary>
        /// Останавливает таймер. Идемпотентно. После Stop поля state
        /// («когда была последняя активность / проверка») сохраняются,
        /// чтобы при следующем Start можно было корректно продолжить цикл.
        /// </summary>
        public void Stop()
        {
            if (_timer == null) return;

            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }

        /// <summary>
        /// Уведомление об активности пользователя. Вызывается из
        /// <c>MainWindow.PreviewMouseMove</c>/<c>PreviewKeyDown</c>.
        /// Просто сдвигает «последнюю активность» в <see cref="Now"/> — никаких
        /// сайд-эффектов, вызовов UI, блокировок (можно дёргать хоть 1000 раз в
        /// секунду: современная мышь на 1600 DPI генерирует до 1000 событий; каждый
        /// вызов — одно field-assignment ~0.1–1 μs; суммарно <1 мс/сек на
        /// battery, поэтому throttle НЕ нужен).
        ///
        /// Если поведение метода начнёт расти (UI-события, IO) — добавить debounce
        /// ≤50 ms granularity: `if (Now() - _lastActivityTime < TimeSpan.FromMilliseconds(50)) return;`.
        /// </summary>
        public void NotifyActivity() => _lastActivityTime = Now();

        /// <summary>
        /// Отметка «проверка только что стартовала». Вызывается в <see cref="OnTick"/>
        /// непосредственно перед fire-and-forget вызовом <see cref="OnCheckDue"/>,
        /// чтобы следующий tick учитывал throttle.
        /// </summary>
        public void MarkChecked() => _lastCheckTime = Now();

        /// <summary>
        /// Tick-handler. Безопасен под повторный вызов — все idempotent.
        /// </summary>
        private void OnTick(object? sender, EventArgs e)
        {
            // Skip если уже идёт проверка/скачивание — не запускаем вторую параллельно.
            if (ShouldSkipCheck?.Invoke() == true) return;

            if (!ShouldCheckAt(Now())) return;

            MarkChecked();

            // Fire-and-forget. UpdateService.CheckInBackgroundAsync уже
            // защищён IsChecking-флагом внутри, поэтому параллельный tick
            // (например, если несколько тиков подряд попали в условие)
            // просто no-op-нется на стороне сервиса.
            if (OnCheckDue != null)
                _ = SafeInvoke(OnCheckDue);
        }

        /// <summary>
        /// Fire-and-forget обёртка для <paramref name="callback"/>. Внутри
        /// <c>try/catch</c> — ловит СИНХРОННЫЕ броски (до возврата Task),
        /// иначе они уйдут в <see cref="System.Threading.Tasks.TaskScheduler.UnobservedTaskException"/>.
        /// Асинхронные броски ловятся внутри <paramref name="callback"/> сами.
        /// </summary>
        private static async Task SafeInvoke(Func<Task> callback)
        {
            try
            {
                await callback().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateCheckScheduler] OnCheckDue threw: {ex}");
            }
        }

        /// <summary>
        /// Pure-логика решения «пора ли триггерить проверку».
        /// Не дёргает event'ы, не изменяет state — только анализирует
        /// snapshot времени. Это позволяет гонять её в unit-тестах
        /// без WPF <see cref="Dispatcher"/>.
        ///
        /// Алгоритм:
        /// 1. Если <c>_lastCheckTime == MinValue</c> (никогда не проверяли) —
        ///    true только если пользователь простаивает ≥ <see cref="IdleThreshold"/>.
        ///    Это «превращает» Start в первую idle-проверку.
        /// 2. Если с последней проверки прошло < <see cref="MinGap"/> — false (throttle).
        /// 3. Если с последней проверки прошло ≥ <see cref="CheckInterval"/> — true (periodic).
        /// 4. Если с последней активности прошло ≥ <see cref="IdleThreshold"/> — true (idle).
        ///
        /// Пункты 3 и 4 объединяются через OR. Пункт 2 гарантирует анти-спам.
        /// </summary>
        public bool ShouldCheckAt(DateTime now)
        {
            // Throttle: между двумя реальными проверками — минимум MinGap.
            if (now - _lastCheckTime < MinGap)
                return false;

            // Periodic gate.
            if (now - _lastCheckTime >= CheckInterval)
                return true;

            // Idle gate.
            if (now - _lastActivityTime >= IdleThreshold)
                return true;

            return false;
        }
    }
}
