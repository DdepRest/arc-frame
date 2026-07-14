using System;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Runtime dependency checker. Complements the installer checks by providing
    /// in-app diagnostics when the app is launched without installation (portable)
    /// or when a dependency was removed after installation.
    /// </summary>
    public static class DependencyCheckerService
    {
        public const string VCRedistDownloadUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";

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
    }
}
