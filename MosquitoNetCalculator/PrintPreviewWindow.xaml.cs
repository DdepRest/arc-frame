using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator
{
    /// <summary>
    /// Отдельное окно предпросмотра КП — тонкая обёртка над PrintPreviewControl.
    /// Содержит только Window chrome (title bar, иконка, WndProc для maximized).
    /// Вся логика предпросмотра и печати — в PrintPreviewControl.
    /// </summary>
    public partial class PrintPreviewWindow : Window
    {
        private readonly PrintPreviewControl _previewControl;

        // ── Win32 for maximized/cover taskbar ──
        private const int WM_GETMINMAXINFO = 0x0024;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

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
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        public PrintPreviewWindow(
            System.Windows.Documents.FlowDocument document,
            PrintService printService,
            string contractNumber,
            PrintSettings? savedSettings)
        {
            InitializeComponent();

            // Icon loading
            try
            {
                Icon = new BitmapImage(new Uri(
                    "pack://application:,,,/MosquitoNetCalculator;component/Resources/app_icon.png",
                    UriKind.Absolute));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PrintPreviewWindow] Failed to load icon: {ex.Message}");
            }

            _previewControl = PreviewControl;
            _previewControl.Initialize(document, printService, contractNumber, savedSettings);
            _previewControl.Closed += (_, _) => Close();

            // Adaptive initial size
            var workArea = SystemParameters.WorkArea;
            const double desiredW = 1150.0;
            const double desiredH = 1300.0;
            double scaleX = workArea.Width * 0.95 / desiredW;
            double scaleY = workArea.Height * 0.95 / desiredH;
            double scale = Math.Min(1.0, Math.Min(scaleX, scaleY));
            this.Width = Math.Max(MinWidth, desiredW * scale);
            this.Height = Math.Max(MinHeight, desiredH * scale);

            SourceInitialized += OnSourceInitialized;
        }

        /// <summary>Возвращает текущие настройки печати.</summary>
        public PrintSettings GetSettings() => _previewControl.GetSettings();

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
                HwndSource.FromHwnd(handle)?.AddHook(WndProc);
        }

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

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                var hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                hwndSource?.RemoveHook(WndProc);
            }
            catch { /* best-effort */ }
            base.OnClosed(e);
        }
    }
}
