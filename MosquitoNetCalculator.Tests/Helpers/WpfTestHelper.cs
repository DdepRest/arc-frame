using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;

namespace MosquitoNetCalculator.Tests.Helpers
{
    /// <summary>
    /// Runs WPF-dependent tests on a SINGLE process-wide STA thread.
    ///
    /// <para><b>Why one thread (v3.53.1, flaky full runs).</b> Every
    /// <c>RunOnSta</c> used to spin a FRESH STA thread per test. WPF
    /// <c>Application</c> resources (styles, brushes) are
    /// <c>DispatcherObject</c>-based: they are materialized on the thread that
    /// first touches them and sealed/frozen there. A later test on another
    /// thread then hit
    /// <c>InvalidOperationException «Вызывающий поток не может получить доступ…»</c>
    /// inside <c>DependencyObject.SetValueCommon</c>/<c>StyleHelper.SealIfSealable</c>
    /// — the same suite passed or failed run-to-run (50 / 0 / 31 / 0 failures on
    /// identical binaries) depending on timing of which thread materialized
    /// which resource first. One long-lived STA thread makes ownership
    /// deterministic: materialize, seal and consume always happen on the same
    /// dispatcher.</para>
    ///
    /// <para>WPF-статика (Application, ресурсы, D3D-слой) одна на процесс, поэтому
    /// все STA-тела сериализуются ещё и процесс-глобальным
    /// <see cref="WpfGate"/> — его держит вызывающий поток на время ожидания,
    /// а тело выполняет рабочая нить.</para>
    /// </summary>
    public static class WpfTestHelper
    {
        /// <summary>
        /// Процесс-глобальный замок WPF-тестов (см. summary выше).
        /// </summary>
        internal static readonly object WpfGate = new();

        private sealed class StaRequest
        {
            public Func<object?>? Work;
            public readonly ManualResetEventSlim Done = new(false);
            public object? Result;
            public Exception? Error;
        }

        private sealed class StaWorker
        {
            public readonly BlockingCollection<StaRequest> Queue = new();
            public readonly Thread Thread;

            public StaWorker()
            {
                Thread = new Thread(() =>
                {
                    // Никогда не роняем процесс: исключение из цикла обрывает
                    // обслуживание всех будущих тестов.
                    try
                    {
                        foreach (var req in Queue.GetConsumingEnumerable())
                        {
                            try { req.Result = req.Work!(); }
                            catch (Exception ex)
                            {
                                req.Error = ex;
                                // Главная улика: тело упало на РАБОЧЕЙ ните —
                                // фиксируем, кому на самом деле принадлежат
                                // ресурсы приложения в этот момент.
                                if (ex.Message.Contains("поток"))
                                {
                                    try
                                    {
                                        var app = Application.Current;
                                        TestAppThemes.Diag(
                                            "CROSS-THREAD-FAIL worker=t" + Thread.CurrentThread.ManagedThreadId +
                                            " app=" + (app == null ? "null" : $"#{app.GetHashCode()} dispatcher=t{app.Dispatcher.Thread.ManagedThreadId}") +
                                            " err=" + ex.Message);
                                    }
                                    catch { /* best effort */ }
                                }
                            }
                            finally { req.Done.Set(); }
                        }
                    }
                    catch
                    {
                        // Queue.Dispose() или завершение процесса — выходим.
                    }
                })
                {
                    IsBackground = true,   // зависшая нить не удерживает тест-хост
                    Name = "xunit-wpf-sta",
                };
                Thread.SetApartmentState(ApartmentState.STA);
                Thread.Start();
                TestAppThemes.Diag($"WORKER-START t{Thread.ManagedThreadId}");
            }
        }

        private static volatile StaWorker? _worker;

        /// <summary>
        /// Runs a function on the shared STA thread and returns the result.
        /// Throws if the function throws.
        /// </summary>
        /// <param name="ensure">
        /// <c>true</c> — перед телом поднимается приложение с темами
        /// (<see cref="TestAppThemes.Ensure"/>); <c>false</c> — тело само
        /// решает, нужен ли Application (тесты fallback-путей без ресурсов).
        /// </param>
        public static T RunOnSta<T>(Func<T> action, int timeoutMs = 30000, bool ensure = true)
        {
            // Уже на рабочей ните (вложенное RunOnSta из тела теста) —
            // выполняем инлайн: класть запрос в собственную очередь значило
            // бы ждать самому себе (deadlock с удерживающим WpfGate).
            var existing = _worker;
            if (existing != null && ReferenceEquals(existing.Thread, Thread.CurrentThread))
                return RunInline(action, ensure);

            lock (WpfGate)
            {
                var worker = _worker ??= new StaWorker();
                var req = new StaRequest { Work = () => RunInline(action, ensure) };
                worker.Queue.Add(req);

                if (!req.Done.Wait(timeoutMs))
                {
                    // Тело зависло. Оставляем старую нить фоновой и заводим
                    // свежую: статика WPF принадлежит умершей ните, чистим
                    // её, чтобы следующий Ensure() пересоздал приложение на
                    // новой рабочей ните (Best effort: старая нить могла
                    // зависнуть посреди манипуляций со статикой).
                    ReplaceWorker(worker);
                    throw new TimeoutException(
                        $"STA test thread did not complete in {timeoutMs} ms — worker replaced.");
                }

                if (req.Error != null)
                    ExceptionDispatchInfo.Capture(req.Error).Throw(); // сохранить оригинальный стек
                return (T)req.Result!;
            }
        }

        /// <summary>
        /// Runs an action on the shared STA thread.
        /// Throws if the action throws.
        /// </summary>
        public static void RunOnSta(Action action, int timeoutMs = 30000, bool ensure = true)
        {
            RunOnSta(() =>
            {
                action();
                return true;
            }, timeoutMs, ensure);
        }

        private static T RunInline<T>(Func<T> action, bool ensure)
        {
            if (ensure) TestAppThemes.Ensure();
            return action();
        }

        private static void ReplaceWorker(StaWorker failed)
        {
            if (!ReferenceEquals(_worker, failed)) return; // кто-то уже заменил
            _worker = null;
            try
            {
                // Старая нить владеет статикой; новый Ensure() на новой ните
                // обязан увидеть чистое состояние, а не чужой мёртвый
                // Dispatcher (иначе снова кросс-поточные VerifyAccess).
                TestAppThemes.ResetStatics();
            }
            catch
            {
                // best effort — таймаут и так означает красный прогон
            }
        }
    }
}
