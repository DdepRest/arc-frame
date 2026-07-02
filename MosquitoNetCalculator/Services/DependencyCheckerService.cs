using System;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Runtime dependency checker. Complements the installer checks by providing
    /// in-app diagnostics when the app is launched without installation (portable)
    /// or when a dependency was removed after installation.
    /// </summary>
    public static class DependencyCheckerService
    {
        public const string WebView2DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
        public const string VCRedistDownloadUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";

        /// <summary>
        /// Checks whether WebView2 Runtime is installed and returns its version.
        /// </summary>
        public static bool IsWebView2Installed(out string? version)
        {
            try
            {
                version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                return !string.IsNullOrEmpty(version);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                version = null;
                return false;
            }
        }

        /// <summary>
        /// Checks whether VC++ Redistributable 2015-2022 (x64) is installed.
        /// </summary>
        public static bool IsVCRedistInstalled(out string? version)
        {
            version = null;
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
                if (key == null) return false;

                var installed = key.GetValue("Installed") as int?;
                if (installed != 1) return false;

                version = key.GetValue("Version") as string;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Fire-and-forget check that notifies the user if WebView2 is missing.
        /// Should be called after the main window is shown and ToastService is initialized.
        /// </summary>
        public static async Task CheckAndNotifyAsync()
        {
            try
            {
                // Let the UI settle before showing a toast so the user sees the
                // main interface first and the toast doesn't feel intrusive.
                await Task.Delay(2000);

                if (!IsWebView2Installed(out _))
                {
                    ToastService.ShowToast(
                        "Для предпросмотра КП требуется WebView2 Runtime. " +
                        "Установщик установит его автоматически, либо скачайте вручную с сайта Microsoft.",
                        ToastType.Warning,
                        durationMs: 6000);
                }
            }
            catch
            {
                // Best-effort diagnostic — never crash the app for a dependency toast.
            }
        }
    }
}
