using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator
{
    public partial class App : Application
    {
        // ── Undocumented uxtheme APIs: tell Windows this process supports dark mode.
        // Without these, DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE) is ignored
        // and the native title bar stays white regardless of the DWM attribute.
        //
        // The function lives at different ordinals on different Windows builds, so we
        // declare all of them and try them in order — whichever exists will be called.
        //   #132 — Windows 10 1903 (older)
        //   #133 — Windows 10 20H1+ / Windows 11 21H2
        //   #135 — Windows 11 22H2+ (most common today)
        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        private static extern int SetPreferredAppMode_135(int preferredAppMode);

        [DllImport("uxtheme.dll", EntryPoint = "#133", SetLastError = true)]
        private static extern int SetPreferredAppMode_133(int preferredAppMode);

        [DllImport("uxtheme.dll", EntryPoint = "#132", SetLastError = true)]
        private static extern int SetPreferredAppMode_132(int preferredAppMode);

        // FlushMenuThemes (ordinal #136) — invalidates uxtheme's cached theme info.
        // On some builds, uxtheme caches the theme decision the first time it's read
        // for a process, and never re-checks it. Flushing forces it to re-evaluate.
        [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = true)]
        private static extern void FlushMenuThemes();

        // Broadcast WM_SETTINGCHANGE so every top-level window in the system
        // re-evaluates its dark-mode state. Without this broadcast, only the
        // foreground window picks up the new theme — background windows keep
        // their old (cached) appearance.
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

        // SetPreferredAppMode enum values (from uxtheme reverse-engineering):
        //   0 = AppDefault — follow system theme
        //   1 = AllowDark  — app supports dark, DWM honours DWMWA_USE_IMMERSIVE_DARK_MODE
        //   2 = ForceDark  — force dark mode (overrides system)
        //   3 = ForceLight — force light mode (overrides system)
        private const int AllowDark = 1;
        private const int ForceDark = 2;
        private const int ForceLight = 3;

        /// <summary>
        /// Notifies Windows of the application's current theme preference.
        /// MUST be called whenever the theme changes — not just at startup —
        /// otherwise Windows continues to honour the old preference and the
        /// native title bar stays white when the user switches to dark mode.
        ///
        /// We try every known ordinal of SetPreferredAppMode and also broadcast
        /// WM_SETTINGCHANGE so the change propagates to every window in the
        /// system, not just the one with focus.
        /// </summary>
        public static void NotifyThemeChanged(bool isDark)
        {
            // Force the system into the theme we want — this overrides the OS-wide
            // dark/light setting, which is what a manual theme switcher needs.
            int mode = isDark ? ForceDark : ForceLight;

            // Try every known ordinal — whichever exists on this Windows build wins.
            try { SetPreferredAppMode_135(mode); } catch { }
            try { SetPreferredAppMode_133(mode); } catch { }
            try { SetPreferredAppMode_132(mode); } catch { }

            // Flush uxtheme's internal cache so it re-reads the theme preference
            // on the next WM_NCPAINT / WM_ERASEBKGND.
            try { FlushMenuThemes(); } catch { }

            // Broadcast WM_SETTINGCHANGE so every top-level window re-evaluates
            // its dark-mode state. lParam "ImmersiveColorSet" is the documented
            // name for the dark-mode setting the system listens on.
            try
            {
                SendMessageTimeout(
                    HWND_BROADCAST,
                    WM_SETTINGCHANGE,
                    IntPtr.Zero,
                    "ImmersiveColorSet",
                    SMTO_ABORTIFHUNG,
                    100,
                    out _);
            }
            catch { /* old Windows without the broadcast message */ }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // ── WatchdogService: replaces VelopackApp.Build().Run().
            // Runs FIRST, before WPF base.OnStartup, before any Window.
            // • If --self-test arg present → App.Exit with code 0/1 (watchdog.bat
            //   uses this to validate a downloaded .exe before swapping).
            // • Otherwise → wipes a leftover arc-update-watchdog.bat from a
            //   previous crashed attempt, then continues with normal startup.
            if (!WatchdogService.HandleStartup(e.Args))
            {
                return;
            }

            base.OnStartup(e);

            // Enable dark mode support for the entire process BEFORE any window is created.
            // This tells Windows to honour DWMWA_USE_IMMERSIVE_DARK_MODE on the title bar.
            // The value here is a placeholder — ThemeService.LoadTheme() will update it
            // with the actual saved preference via NotifyThemeChanged.
            try { SetPreferredAppMode_135(AllowDark); } catch { }
            try { SetPreferredAppMode_133(AllowDark); } catch { }
            try { SetPreferredAppMode_132(AllowDark); } catch { }

            // ── Set up global error handlers FIRST so any exception
            // during startup (including LoadTheme) is shown in a clear
            // MessageBox instead of the raw Windows 0xe0434352 dialog.
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                ShowError(args.ExceptionObject as Exception);
            };

            DispatcherUnhandledException += (s, args) =>
            {
                ShowError(args.Exception);
                args.Handled = true;
            };

            // Load theme before any window is created so StaticResource
            // resolves the correct colors during InitializeComponent.
            try
            {
                ThemeService.LoadTheme();
            }
            catch (Exception ex)
            {
                ShowError(ex);
                // Fall back to default Light theme so the app can still start
                ThemeService.ApplyTheme();
            }

            // ── Data migration: old versions stored orders/settings/prices
            // alongside the .exe (BaseDirectory). v3.28+ stores them in
            // %AppData%\MosquitoNetCalculator\ so they survive updates.
            // On first run of the new version, migrate existing data.
            MigrateDataToAppData();

            // ── First-run welcome window ──
            // ShutdownMode=OnExplicitShutdown is set in App.xaml — the app stays
            // alive until Shutdown() is called explicitly. We hook that to
            // MainWindow.Closed below so closing the welcome screen does NOT
            // exit the app (which is what OnLastWindowClose used to cause).
            if (AppSettingsService.IsFirstRun())
            {
                try
                {
                    var welcome = new WelcomeWindow(isFirstRun: true);
                    if (welcome.ShowDialog() != true)
                    {
                        // User closed without selecting a location — exit the app.
                        // The welcome dialog already set DialogResult=false (user
                        // clicked X) and did NOT call MarkFirstRunComplete(), so
                        // the welcome screen will appear again on next launch.
                        Shutdown();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    ShowError(ex);
                    // Ensure first run is marked even if welcome crashes
                    try { AppSettingsService.MarkFirstRunComplete(); } catch { }
                }
            }

            try
            {
                var window = new MainWindow();
                MainWindow = window;
                // Explicit shutdown when the user actually wants to quit:
                // close the main window → end the application.
                window.Closed += (_, _) => Shutdown();
                window.Show();

                // ── Background update check (silent, non-blocking) ──
                // Fire-and-forget after the main window is visible so
                // toast notifications have a valid owner.
                _ = UpdateService.CheckOnStartupAsync();
            }
            catch (Exception ex)
            {
                ShowError(ex);
                Shutdown();
            }
        }

        /// <summary>
        /// Migrates user data (orders, settings, prices) from the old location
        /// (alongside the .exe in BaseDirectory) to the new %AppData%\MosquitoNetCalculator\
        /// location. Only copies if the AppData location is empty — never overwrites.
        /// Called on every startup; exits early if migration already done.
        /// </summary>
        private static void MigrateDataToAppData()
        {
            try
            {
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MosquitoNetCalculator");
                string oldBaseDir = AppDomain.CurrentDomain.BaseDirectory;

                // Only migrate if AppData/orders/ doesn't exist yet
                if (Directory.Exists(Path.Combine(appDataDir, "orders")))
                    return;

                Directory.CreateDirectory(appDataDir);

                // Migrate orders
                string oldOrdersDir = Path.Combine(oldBaseDir, "orders");
                string newOrdersDir = Path.Combine(appDataDir, "orders");
                if (Directory.Exists(oldOrdersDir))
                {
                    Directory.CreateDirectory(newOrdersDir);
                    foreach (var file in Directory.GetFiles(oldOrdersDir, "*.json"))
                    {
                        string dest = Path.Combine(newOrdersDir, Path.GetFileName(file));
                        if (!File.Exists(dest))
                            File.Copy(file, dest);
                    }
                }

                // Migrate settings.json
                string oldSettings = Path.Combine(oldBaseDir, "settings.json");
                string newSettings = Path.Combine(appDataDir, "settings.json");
                if (File.Exists(oldSettings) && !File.Exists(newSettings))
                {
                    File.Copy(oldSettings, newSettings);
                }

                // Migrate prices.json
                string oldPrices = Path.Combine(oldBaseDir, "prices.json");
                string newPrices = Path.Combine(appDataDir, "prices.json");
                if (File.Exists(oldPrices) && !File.Exists(newPrices))
                {
                    File.Copy(oldPrices, newPrices);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] Data migration failed: {ex.Message}");
                // Non-fatal — app creates defaults if migration fails
            }
        }

        private static void ShowError(Exception? ex)
        {
            if (ex == null) return;

            var sb = new StringBuilder();
            var current = ex;
            int depth = 0;
            while (current != null && depth < 5)
            {
                if (depth > 0)
                    sb.AppendLine($"\n--- InnerException (level {depth}) ---");
                sb.AppendLine($"Type: {current.GetType().FullName}");
                sb.AppendLine($"Message: {current.Message}");
                if (!string.IsNullOrEmpty(current.StackTrace))
                    sb.AppendLine($"StackTrace:\n{current.StackTrace}");
                current = current.InnerException;
                depth++;
            }

            MessageBox.Show(sb.ToString(), "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
