using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator
{
    public partial class MainWindow
    {
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
            if (!SuppressPrefixSave)
                AppSettingsService.SaveContractPrefix(Sidebar.TxtPrefix.Text);
            if (!ViewModel.UndoRedo.IsDirty) return;
            if (Application.Current == null || Application.Current.Dispatcher.HasShutdownStarted)
                return;
            var choice = DialogService.ShowSaveDiscardCancel("В текущем расчёте есть несохранённые изменения.\nСохранить перед выходом?", owner: this);
            if (choice == Services.DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (choice == Services.DialogResult.Save)
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
