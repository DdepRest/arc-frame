using System;
using System.IO;
using System.Reflection;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Manage post-update .exe swap, rollback, and self-test — replaces Velopack's hooks.
    ///
    /// ─── What this does ─────────────────────────────────────────────────────
    /// Velopack is gone. The new flow (driven from UpdateService.CheckAndApplyAsync):
    ///   1. UpdateService downloads ZIP, verifies SHA-256, gets a temp ZIP path.
    ///   2. WatchdogService.StageUpdate() backs up current .exe to .exe.bak,
    ///      copies the ZIP into BaseDirectory as arc-update.zip, writes
    ///      arc-update-watchdog.bat.
    ///   3. UpdateService launches the watchdog.bat and calls Application.Shutdown.
    ///   4. The watchdog .bat waits for the running app to exit, then:
    ///      - extracts the new ZIP into a temp dir;
    ///      - runs the new .exe with --self-test (must exit 0);
    ///      - on success: copies the new .exe over the running one, deletes .bak,
    ///        deletes update.zip, and starts the (now-updated) .exe normally;
    ///      - on failure: deletes update.zip, leaves .exe.bak untouched so
    ///        the previous version can be restarted, and runs it.
    ///   5. On next launch, App.OnStartup calls HandleStartup. If the watchdog.bat
    ///      from a previous crashed attempt is still here, we delete it so it
    ///      doesn't fire spuriously.
    ///
    /// ─── File convention (all in AppContext.BaseDirectory) ─────────────────
    ///   arc-update-watchdog.bat   (deleted after successful run)
    ///   arc-update.zip            (deleted after successful run)
    ///   MosquitoNetCalculator.exe (current running — locked while running)
    ///   MosquitoNetCalculator.exe.bak (most recent valid backup)
    /// </summary>
    public static class WatchdogService
    {
        public const string SelfTestArg = "--self-test";

        public const string ExeFileName = "MosquitoNetCalculator.exe";
        public const string BackupSuffix = ".bak";
        public const string UpdateZipName = "arc-update.zip";
        public const string WatchdogBatName = "arc-update-watchdog.bat";

        // Computed paths — every one lives next to the running .exe.
        public static string BasePath => AppContext.BaseDirectory;
        public static string ExePath => Path.Combine(BasePath, ExeFileName);
        public static string ExeBakPath => ExePath + BackupSuffix;
        public static string UpdateZipPath => Path.Combine(BasePath, UpdateZipName);
        public static string WatchdogPath => Path.Combine(BasePath, WatchdogBatName);

        /// <summary>
        /// Called from App.OnStartup as the very first thing (before
        /// base.OnStartup and before any Window creation).
        /// 
        /// Returns true if startup should proceed normally, or
        /// false if HandleStartup has issued an Environment.Exit (for
        /// the self-test flow). Callers should return from OnStartup
        /// when this returns false.
        /// </summary>
        public static bool HandleStartup(string[] args)
        {
            try
            {
                // --self-test: validate the .exe can load, then exit with code.
                // The watchdog .bat uses this to sanity-check a downloaded .exe
                // BEFORE replacing the running one.
                if (args != null && Array.Exists(args, a =>
                    string.Equals(a, SelfTestArg, StringComparison.OrdinalIgnoreCase)))
                {
                    int code = RunSelfTest();
                    Environment.Exit(code);
                    return false; // unreachable
                }

                // Wipe any leftover watchdog.bat from a previous crashed attempt.
                if (File.Exists(WatchdogPath))
                {
                    try { File.Delete(WatchdogPath); } catch { /* swallow */ }
                }

                return true;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Returns 0 if the running .exe appears coherent, 1 otherwise.
        /// </summary>
        private static int RunSelfTest()
        {
            try
            {
                FileInfo fi = new FileInfo(ExePath);
                if (!fi.Exists || fi.Length < 1024) return 1;

                using (FileStream fs = File.OpenRead(ExePath))
                {
                    fs.Seek(-100, SeekOrigin.End);
                    byte[] tail = new byte[100];
                    int read = fs.Read(tail, 0, tail.Length);
                    if (read < 100) return 1;
                }

                Assembly? entry = Assembly.GetEntryAssembly();
                if (entry == null) return 1;
                _ = entry.Location;

                return 0;
            }
            catch
            {
                return 1;
            }
        }

        /// <summary>
        /// Stage an update for the next run. Called by UpdateService just
        /// before shutdown.
        /// 
        /// Order matters: .bat first, ZIP second, .exe.bak third.
        /// Throws if staging fails.
        /// </summary>
        public static void StageUpdate(string downloadedZipPath)
        {
            if (!File.Exists(downloadedZipPath))
                throw new FileNotFoundException("Update ZIP not found", downloadedZipPath);

            Directory.CreateDirectory(BasePath);

            // 1. Commit the watchdog .bat FIRST.
            File.WriteAllText(WatchdogPath, BuildWatchdogBat());

            // 2. Commit the ZIP.
            File.Copy(downloadedZipPath, UpdateZipPath, overwrite: true);

            // 3. Commit the backup copy of the current .exe.
            if (File.Exists(ExePath))
                File.Copy(ExePath, ExeBakPath, overwrite: true);
        }

        /// <summary>
        /// The .bat script that performs the actual .exe swap after app exit.
        /// Pure ASCII — .bat files use OEM (cp866) by default and Cyrillic
        /// can corrupt parsing.
        /// </summary>
        private static string BuildWatchdogBat()
        {
            return
"@echo off\r\n" +
"setlocal EnableExtensions\r\n" +
"REM ===== ARC-Frame auto-update watchdog (auto-generated) =====\r\n" +
"REM Pure ASCII only - cp866/OEM compatible.\r\n" +
"\r\n" +
"set EXE=MosquitoNetCalculator.exe\r\n" +
"set BAK=%EXE%.bak\r\n" +
"set ZIP=arc-update.zip\r\n" +
"set WRK=%TEMP%\\arc-update-work~\r\n" +
"set HERE=%~dp0\r\n" +
"set _copyfail=0\r\n" +
"\r\n" +
"REM --- 1. Wait up to 120 seconds for the running app to exit ---\r\n" +
"set /a _n=0\r\n" +
":waitloop\r\n" +
"if %_n% geq 120 goto :dontexit\r\n" +
"set /a _n+=1\r\n" +
"timeout /t 1 /nobreak > nul\r\n" +
"tasklist /fi \"imagename eq %EXE%\" 2> nul | find /i \"%EXE%\" > nul\r\n" +
"if not errorlevel 1 goto :waitloop\r\n" +
":dontexit\r\n" +
"\r\n" +
"REM Final check - if app is STILL alive, abort\r\n" +
"tasklist /fi \"imagename eq %EXE%\" 2> nul | find /i \"%EXE%\" > nul\r\n" +
"if not errorlevel 1 (\r\n" +
"    echo ARC-Frame update ABORTED: app still running after 120s.\r\n" +
"    del \"%HERE%%ZIP%\" 2> nul\r\n" +
"    del \"%~f0\" 2> nul\r\n" +
"    exit /b 1\r\n" +
")\r\n" +
"\r\n" +
"REM --- 2. Extract downloaded ZIP into a clean working dir ---\r\n" +
"if exist \"%WRK%\" rd /s /q \"%WRK%\"\r\n" +
"mkdir \"%WRK%\" 2> nul\r\n" +
"powershell -NoProfile -ExecutionPolicy Bypass -Command \"Expand-Archive -Force -Path '%HERE%%ZIP%' -DestinationPath '%WRK%'\" 1> nul 2> nul\r\n" +
"if not exist \"%WRK%\\%EXE%\" goto :rollback\r\n" +
"\r\n" +
"REM --- 3. Self-test the new .exe ---\r\n" +
"\"%WRK%\\%EXE%\" --self-test\r\n" +
"set _rc=%ERRORLEVEL%\r\n" +
"if not \"%_rc%\"==\"0\" goto :rollback\r\n" +
"\r\n" +
"REM --- 4. Update successful - copy new files into BaseDirectory ---\r\n" +
"copy /y \"%WRK%\\%EXE%\" \"%HERE%%EXE%\" > nul\r\n" +
"if not \"%ERRORLEVEL%\"==\"0\" goto :rollback_after_copy\r\n" +
"xcopy /y /e /q /r \"%WRK%\\*\" \"%HERE%\" > nul 2>&1\r\n" +
"if not \"%ERRORLEVEL%\"==\"0\" goto :rollback_after_copy\r\n" +
"if exist \"%WRK%\" rd /s /q \"%WRK%\"\r\n" +
"del \"%HERE%%ZIP%\" 2> nul\r\n" +
"if exist \"%HERE%%BAK%\" del \"%HERE%%BAK%\"\r\n" +
"goto :start\r\n" +
"\r\n" +
":rollback_after_copy\r\n" +
"set _copyfail=1\r\n" +
"if exist \"%HERE%%BAK%\" copy /y \"%HERE%%BAK%\" \"%HERE%%EXE%\" > nul\r\n" +
"goto :start\r\n" +
"\r\n" +
":rollback\r\n" +
"if exist \"%WRK%\" rd /s /q \"%WRK%\"\r\n" +
"del \"%HERE%%ZIP%\" 2> nul\r\n" +
"echo ARC-Frame update ROLLBACK: New version failed self-test.\r\n" +
"\r\n" +
":start\r\n" +
"start \"\" \"%HERE%%EXE%\"\r\n" +
"\r\n" +
":end\r\n" +
"del \"%~f0\" 2> nul\r\n" +
"if \"%_copyfail%\"==\"1\" exit /b 2\r\n" +
"exit /b 0\r\n" +
"endlocal\r\n";
        }
    }
}
