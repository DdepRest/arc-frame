using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator
{
    public partial class MainWindow
    {
        // ── Mica backdrop (Windows 11 only) ─────────────────────────────────
        // Requires Windows 11 build 22000+. No-op on Windows 10 and earlier.
        private const int DWMWA_MICA = 1029;                 // Win11 21H2 (22000)
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;    // Win11 22H2 (22621)
        private const int DWMSBT_MAINWINDOW = 2;             // Mica

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        /// <summary>
        /// Enables Mica backdrop on Windows 11. On older systems, silently no-ops.
        /// Also makes the TitleBar semi-transparent so the Mica texture shows through.
        /// Subscribes to ThemeChanged so the transparency survives light↔dark switches.
        /// </summary>
        internal void EnableMicaBackdrop()
        {
            int build = Environment.OSVersion.Version.Build;
            if (build < 22000) return; // Windows 10 and below

            var hwnd = new WindowInteropHelper(this).Handle;
            if (build >= 22621)
            {
                int backdrop = DWMSBT_MAINWINDOW;
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            }
            else
            {
                int mica = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_MICA, ref mica, sizeof(int));
            }

            // Apply semi-transparent TitleBar and subscribe to theme changes
            // so the alpha-blended Surface colour stays in sync after light↔dark switch.
            ApplyMicaTitleBar();
            ThemeService.ThemeChanged += ApplyMicaTitleBar;
        }

        /// <summary>
        /// Re-applies the 85%-opacity Surface colour to the TitleBar.
        ///
        /// Reads the TARGET surface colour from ThemeService.GetCurrentSurfaceColor()
        /// (NOT from FindResource) — during a theme-switch callback the Surface
        /// brush may be mid-animation and still showing the OLD theme's colour,
        /// so FindResource would return stale data and the title bar would get
        /// stuck on the wrong theme. GetCurrentSurfaceColor reads the definitive
        /// target colour directly from the colour dictionary.
        ///
        /// Always creates a NEW separate brush (never reuses the shared
        /// {DynamicResource Surface} brush from App.Resources — modifying that
        /// would change the opacity of every Surface consumer in the app).
        /// The snap is visually unnoticeable on a thin 32px title bar strip
        /// alongside the 280ms animated transition on the rest of the app.
        /// </summary>
        private void ApplyMicaTitleBar()
        {
            try
            {
                if (TitleBarControl?.TitleBarBorder == null || Application.Current == null) return;
                var surfaceColor = ThemeService.GetCurrentSurfaceColor();
                TitleBarControl.TitleBarBorder.Background = new SolidColorBrush(
                    Color.FromArgb(0xD9, surfaceColor.R, surfaceColor.G, surfaceColor.B));
            }
            catch { /* best-effort; Mica is cosmetic */ }
        }

        // ── Fix maximized window covering the taskbar (WindowStyle="None") ──
        private const int WM_GETMINMAXINFO = 0x0024;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMaxTrackSize;
            public POINT ptMinTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                    mmi.ptMaxPosition.X = mi.rcWork.Left;
                    mmi.ptMaxPosition.Y = mi.rcWork.Top;
                    mmi.ptMaxSize.X = mi.rcWork.Right - mi.rcWork.Left;
                    mmi.ptMaxSize.Y = mi.rcWork.Bottom - mi.rcWork.Top;
                    Marshal.StructureToPtr(mmi, lParam, true);
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (!SuppressPrefixSave && Sidebar?.TxtPrefix != null)
                AppSettingsService.SaveContractPrefix(Sidebar.TxtPrefix.Text);
            if (!ViewModel.UndoRedo.IsDirty) return;
            if (Application.Current == null || Application.Current.Dispatcher.HasShutdownStarted)
                return;
            var choice = DialogService.ShowSaveDiscardCancel("В текущем расчёте есть несохранённые изменения.\nСохранить перед выходом?", owner: this);
            if (choice == Services.SaveDiscardCancelResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (choice == Services.SaveDiscardCancelResult.Save)
            {
                ApplicationCommands.Save.Execute(null, this);
                if (ViewModel.UndoRedo.IsDirty)
                {
                    e.Cancel = true;
                    return;
                }
            }
        }
    }
}
