using System;
using System.Threading;

namespace MosquitoNetCalculator.Tests.Helpers
{
    /// <summary>
    /// Helper for running WPF-dependent tests on STA threads.
    /// xUnit runs tests on MTA by default, but WPF objects like
    /// FormattedText, DrawingImage, and Visual require STA.
    /// </summary>
    public static class WpfTestHelper
    {
        /// <summary>
        /// Runs a function on a dedicated STA thread and returns the result.
        /// Throws if the function throws.
        /// </summary>
        public static T RunOnSta<T>(Func<T> action, int timeoutMs = 30000)
        {
            T result = default!;
            Exception? error = null;
            var gate = new ManualResetEventSlim(false);

            var t = new Thread(() =>
            {
                try { result = action(); }
                catch (Exception ex) { error = ex; }
                finally { gate.Set(); }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();

            if (!gate.Wait(timeoutMs))
                throw new TimeoutException("STA test thread did not complete in time.");
            t.Join();

            if (error != null)
                throw error;
            return result;
        }

        /// <summary>
        /// Runs an action on a dedicated STA thread.
        /// Throws if the action throws.
        /// </summary>
        public static void RunOnSta(Action action, int timeoutMs = 30000)
        {
            RunOnSta(() =>
            {
                action();
                return true;
            }, timeoutMs);
        }
    }
}
