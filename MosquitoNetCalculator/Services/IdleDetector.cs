using System;
using System.Runtime.InteropServices;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// System idle detection via WinAPI <c>GetLastInputInfo</c>.
    ///
    /// Extracted from <see cref="UpdateService"/> (Phase 2 refactoring).
    /// Pure P/Invoke wrapper — no state, no dependencies.
    /// Used by <see cref="UpdateCheckScheduler"/> to determine when the
    /// user has been idle long enough to warrant a background update check.
    /// </summary>
    public static class IdleDetector
    {
        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        /// <summary>
        /// Returns the time span since the last user input (mouse or keyboard)
        /// across the entire system, using WinAPI <c>GetLastInputInfo</c>.
        /// Returns <see cref="TimeSpan.Zero"/> if the API call fails.
        /// </summary>
        public static TimeSpan GetIdleTime()
        {
            var plii = new LASTINPUTINFO();
            plii.cbSize = (uint)Marshal.SizeOf(plii);
            if (GetLastInputInfo(ref plii))
            {
                uint idleTicks = unchecked((uint)Environment.TickCount - plii.dwTime);
                return TimeSpan.FromMilliseconds(idleTicks);
            }
            return TimeSpan.Zero;
        }
    }
}
