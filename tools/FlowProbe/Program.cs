using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace FlowProbe;

/// <summary>
/// Diagnostic tool — replicates the WPF's <c>VelopackFlowSource</c> update
/// check for <c>ARC-Frame</c> on the <c>win</c> channel without launching
/// the GUI. Equivalent to running the program on a test machine.
///
/// Exit codes:
///   0 — published update found (e.g. "3.33.0")
///   1 — Flow returned null (no published update yet)
///   2 — network/protocol error
///
/// Why we inject a <c>TestVelopackLocator</c> explicitly:
///   <c>UpdateManager</c>'s default <c>CheckForUpdatesAsync()</c> throws
///   "No VelopackLocator has been set" unless <c>VelopackApp.Build().Run()</c>
///   initialized the global locator (which only happens in an actual Velopack
///   install). The 4-arg ctor <c>(IUpdateSource, UpdateOptions, ILogger, IVelopackLocator)</c>
///   bypasses that requirement entirely.
/// </summary>
internal static class Program
{
    private static async Task<int> Main()
    {
        Console.WriteLine("=== Velopack Flow probe for ARC-Frame / win ===");
        Console.WriteLine("    (mirrors UpdateService.GetManager() -> CheckForUpdatesAsync())");
        try
        {
            var source = new VelopackFlowSource();

            // Use a fake locator so Velopack doesn't try to read a real install.
            // version "0.0.1" is below every released version, so Flow will
            // return the LATEST published release — not filtered by "current".
            var locator = new TestVelopackLocator(
                appId: "ARC-Frame",
                version: "0.0.1",
                packagesDir: "dummy_packages_dir");

            // ExplicitChannel must match the vpk publish --channel used in
            // deploy-flow.bat (currently "win").
            var options = new UpdateOptions { ExplicitChannel = "win" };

            // 3-arg positional ctor. Velopack 1.2.0's actual signature
            // (verified by CS1739 error: the named-arg "logger" doesn't exist)
            // is UpdateManager(IUpdateSource source, UpdateOptions options, IVelopackLocator locator)
            // — no ILogger parameter at all in the 1.2.0 ctor.
            var mgr = new UpdateManager(source, options, locator);

            var info = await mgr.CheckForUpdatesAsync();

            if (info == null)
            {
                Console.WriteLine("RESULT: NO_UPDATE_AVAILABLE");
                Console.WriteLine("  -> Flow 'win' channel returned null — release is still in Draft.");
                return 1;
            }

            var rel = info.TargetFullRelease;
            Console.WriteLine($"RESULT: UPDATE_AVAILABLE {rel.Version}");
            Console.WriteLine($"  FileName: {rel.FileName}");
            Console.WriteLine($"  Size:     {rel.Size} bytes");
            Console.WriteLine($"  SHA1:     {rel.SHA1}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"  -> {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }
            return 2;
        }
    }
}
