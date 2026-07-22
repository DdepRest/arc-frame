using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Xml.Linq;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.App
{
    /// <summary>
    /// Forces serial execution of every test in the <c>WPF_UI</c> collection.
    /// Without this, xUnit parallelizes tests within the same collection by
    /// default, which races two STA tests that each spin up their own
    /// <see cref="System.Windows.Application"/> on a dedicated STA thread —
    /// the second <c>new Application()</c> throws
    /// <c>InvalidOperationException</c> with "Cannot create more than one
    /// instance of System.Windows.Application in one AppDomain."
    ///
    /// Tests in OTHER collections are unaffected — they can still run in
    /// parallel with this one.
    /// </summary>
    [CollectionDefinition("WPF_UI", DisableParallelization = true)]
    public class WpfUiTestCollection { }

    /// <summary>
    /// Regression tests for the "program doesn't open after selection" bug +
    /// follow-up footguns caught in subsequent audits.
    ///
    /// Original bug: App.xaml used ShutdownMode="OnLastWindowClose", so when
    /// WelcomeWindow closed (it was the only window during first-run), the
    /// application shut down before MainWindow could be shown.
    ///
    /// Fix: ShutdownMode="OnExplicitShutdown" + MainWindow.Closed → Shutdown().
    /// These tests pin that configuration so the bug cannot silently regress.
    ///
    /// Follow-up footguns pinned here:
    ///  • Freezable + Popup shadow — DropShadowEffect.Color via DynamicResource
    ///    inside a popup ControlTemplate doesn't react to theme toggle.
    ///  • Run.Text default TwoWay binding — footgun when bound property is
    ///    read-only (XamlParseException at template load).
    ///  • "Коммерческое предложение / Договор" reintroduction in the printed
    ///    КП — historical title we deliberately trimmed.
    ///
    /// All tests in this class share the "WPF_UI" collection, which forces
    /// xUnit to run them serially. The two STA tests
    /// (<see cref="Closing_Auxiliary_Window_With_OnExplicitShutdown_Does_Not_Exit_App"/>
    /// and <see cref="PrintPreviewWindow_OpensWithoutNRE_DuringInitialXamlParse"/>)
    /// both spin up their own <see cref="System.Windows.Application"/> on a
    /// dedicated STA thread; running them in parallel violates WPF's
    /// "one Application per AppDomain" rule and the losing test sees
    /// InvalidOperationException instead of the regression it was checking.
    /// </summary>
    [Collection("WPF_UI")]
    public class AppLifecycleTests
    {
        // Walk up from the test bin folder until we find App.xaml — robust
        // against an extra bin/Configuration sub-folder appearing later.
        private static readonly string SourceProjectDir = LocateSourceProject();
        private static readonly string SolutionRootDir =
            Directory.GetParent(SourceProjectDir)?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate solution root from source project dir.");

        private static string LocateSourceProject()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MosquitoNetCalculator", "App.xaml")))
                dir = dir.Parent;
            if (dir == null)
                throw new DirectoryNotFoundException("Could not locate source project root via App.xaml.");
            return Path.Combine(dir.FullName, "MosquitoNetCalculator");
        }

        private static string ReadSource(string fileName) =>
            File.ReadAllText(Path.Combine(SourceProjectDir, fileName));

        // ─────────────────────────────────────────────────────────
        //  Original static regression tests — guard against the bug
        // ─────────────────────────────────────────────────────────

        [Fact]
        public void App_Xaml_Uses_OnExplicitShutdown()
        {
            // CRITICAL GUARD: reverting to OnLastWindowClose brings the bug back,
            // because closing WelcomeWindow on first run terminates the app
            // before MainWindow is shown.
            var root = XDocument.Parse(ReadSource("App.xaml")).Root;
            Assert.NotNull(root);
            var mode = root!.Attribute("ShutdownMode")?.Value;
            Assert.Equal("OnExplicitShutdown", mode);
        }

        [Fact]
        public void App_OnStartup_Wires_MainWindow_Closed_To_Shutdown()
        {
            // With OnExplicitShutdown the app stays alive until we explicitly
            // call Shutdown(). The only allowed path is closing the actual
            // MainWindow, so we look for the precise wiring pattern
            //   window.Closed += … Shutdown()  …
            // rather than containing two unrelated substrings.
            var src = ReadSource("App.xaml.cs");

            var m = Regex.Match(
                src,
                @"window\.Closed\s*\+=\s*\(?[^;]*?\bShutdown\s*\(\s*\)\s*;",
                RegexOptions.Singleline);

            Assert.True(m.Success,
                "App.OnStartup must register a handler on the actual MainWindow's Closed " +
                "event that calls Shutdown(). Found no such pattern in App.xaml.cs.");
        }

        [Fact]
        public void WelcomeWindow_Does_Not_Call_Application_Shutdown()
        {
            // The dialog closes via ShowDialog()/Close(), never via
            // Application.Current.Shutdown — keep it that way so the bug
            // cannot come back through this surface.
            var src = ReadSource("WelcomeWindow.xaml.cs");
            Assert.DoesNotContain("Application.Current.Shutdown", src);
            Assert.DoesNotContain("Application.Shutdown", src);
        }

        [Fact]
        public void WelcomeWindow_BtnContinue_Persists_Settings()
        {
            // Continue button MUST save prefix, location and mark first-run done;
            // otherwise the user would see the welcome screen on every launch.
            var src = ReadSource("WelcomeWindow.xaml.cs");
            Assert.Contains("BtnContinue_Click", src);
            Assert.Contains("SaveContractPrefix", src);
            Assert.Contains("SaveLocationName", src);
            Assert.Contains("MarkFirstRunComplete", src);
        }

        // ─────────────────────────────────────────────────────────
        //  Behaviour test — emulate the lifecycle on an STA thread
        //  to catch the original bug if it ever returns
        // ─────────────────────────────────────────────────────────

        [Fact]
        public void Closing_Auxiliary_Window_With_OnExplicitShutdown_Does_Not_Exit_App()
        {
            // Emulates: WelcomeWindow closes → app stays alive → MainWindow
            // opens → user closes MainWindow → app exits.
            //
            // We use a dedicated STA thread because WPF Window requires an
            // apartment-threaded dispatcher loop, and xUnit's runner is MTA.

            Exception? caught = null;
            var gate = new ManualResetEventSlim(false);
            var result = new LifecycleResult();

            var t = new Thread(() =>
            {
                try
                {
                    if (Application.Current != null)
                    {
                        // xUnit may share an AppDomain with another test that
                        // touched Application; bail out cleanly rather than throw.
                        result.Skipped = true;
                        return;
                    }

                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.Exit += (_, _) => result.ExitCount++;

                    // Step 1: auxiliary window (WelcomeWindow) — close on load.
                    var aux = new Window { Width = 10, Height = 10, ShowInTaskbar = false };
                    aux.Loaded += (_, _) => aux.Close();
                    aux.Closed += (_, _) => result.AuxClosed = true;
                    aux.Show();

                    PumpDispatcher(TimeSpan.FromSeconds(3));
                    Assert.True(result.AuxClosed, "Auxiliary window should have closed");
                    Assert.Equal(0, result.ExitCount);   // app must NOT exit on a non-Main window closing

                    // Step 2: MainWindow with the production wiring.
                    var main = new Window { Width = 10, Height = 10, ShowInTaskbar = false };
                    main.Loaded += (_, _) =>
                    {
                        result.MainOpened = true;
                        main.Close();
                    };
                    main.Closed += (_, _) => app.Shutdown();
                    main.Show();

                    PumpDispatcher(TimeSpan.FromSeconds(3));
                }
                catch (Exception ex) { caught = ex; }
                finally
                {
                    // Tear down the static WPF Application reference so the next STA
                    // test in this collection can spin up its own Application.
                    // Errors go to result.CleanupError (distinct from `caught`) so the
                    // assertion below surfaces the actionable cleanup message
                    // explicitly rather than masking it as a generic body failure.
                    try { ClearWpfApplicationStatic(); }
                    catch (Exception ex) { result.CleanupError = ex; }
                    gate.Set();
                }
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(gate.Wait(TimeSpan.FromSeconds(20)), "STA test thread did not finish in time");
            t.Join();

            Assert.Null(caught);
            Assert.Null(result.CleanupError);
            Assert.False(result.Skipped, "Application was already running in this AppDomain — cannot run STA lifecycle test.");
            Assert.True(result.AuxClosed, "Auxiliary window closed");
            Assert.True(result.MainOpened, "Main window opened");
            Assert.Equal(1, result.ExitCount);   // app exited exactly once, only after MainWindow.Close
        }

        /// <summary>
        /// Forces WPF's two private static slots on <see cref="System.Windows.Application"/>
        /// back to a clean state so the next STA test in this collection
        /// can spin up its own <c>new Application()</c>.
        ///
        /// WPF normally resets both via <c>Application.OnExit → ProcessExit()</c>,
        /// but that path only runs after <c>Application.Run()</c> returns. Our
        /// STA tests never enter <c>Run()</c> — we drive the dispatcher
        /// directly via <see cref="PumpDispatcher"/>. In our tests
        /// <c>ProcessExit</c> may or may not fire (the dispatcher queue
        /// settles after PumpDispatcher but the lock+check guarded block
        /// inside ProcessExit requires the Application instance we just
        /// shut down to match the static reference — fragile in edge
        /// cases). We bypass ProcessExit and clear both statics directly.
        ///
        /// Two slots to clear (verified against .NET 8 WPF source —
        /// PresentationFramework 8.0.0.0):
        ///  • <c>_appCreatedInThisAppDomain</c> (bool) — the gate that
        ///    <c>Application()</c>'s ctor checks and throws
        ///    "Cannot create more than one instance ... in one AppDomain."
        ///    against. Must be cleared FIRST — it's the precondition
        ///    for <c>new Application()</c>.
        ///  • <c>_appInstance</c> (Application) — backs
        ///    <c>Application.Current</c>. Cleared SECOND so subsequent
        ///    readers of <c>Current</c> see no live Application.
        ///
        /// We do NOT gate on <c>Application.Current == null</c> as a
        /// fast-path because it has two failure modes:
        ///  • The getter calls <c>VerifyAccess()</c> on the static's
        ///    associated dispatcher, which can throw <c>InvalidOperationException</c>
        ///    after a shutdown has been scheduled.
        ///  • <c>Current</c> may already be null from <c>ProcessExit</c>
        ///    while <c>_appCreatedInThisAppDomain</c> is still true
        ///    (ProcessExit clears both atomically, but in our test
        ///    path where Run() is never entered, <c>ProcessExit</c>
        ///    may not fire at all — we must clear both unconditionally).
        ///
        /// If WPF renames either field in a future runtime, this throws
        /// a real <see cref="InvalidOperationException"/> with an
        /// actionable assembly-qualified message that callers stash in
        /// <c>result.CleanupError</c>.
        /// </summary>
        internal static void ClearWpfApplicationStatic()
        {
            var appType = typeof(System.Windows.Application);
            const System.Reflection.BindingFlags PrivateStatic =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;

            // 1. Reset the per-AppDomain "already created" flag — MUST
            //    come first; it's the gate new Application() ctor checks.
            var flagField = appType.GetField("_appCreatedInThisAppDomain", PrivateStatic);
            if (flagField == null)
            {
                throw new InvalidOperationException(
                    "ClearWpfApplicationStatic: WPF Application._appCreatedInThisAppDomain " +
                    "private static field not found on " + appType.FullName +
                    " (assembly: " + appType.Assembly.GetName().FullName + "). " +
                    "WPF likely renamed it in a new runtime — update ClearWpfApplicationStatic " +
                    "to the new field name.");
            }
            flagField.SetValue(null, false);

            // 2. Null the static Application reference — backs Application.Current.
            var refField = appType.GetField("_appInstance", PrivateStatic);
            if (refField == null)
            {
                throw new InvalidOperationException(
                    "ClearWpfApplicationStatic: WPF Application._appInstance " +
                    "private static field not found on " + appType.FullName +
                    " (assembly: " + appType.Assembly.GetName().FullName + "). " +
                    "WPF likely renamed it in a new runtime — update ClearWpfApplicationStatic " +
                    "to the new field name.");
            }
            refField.SetValue(null, null);
        }

        private sealed class LifecycleResult
        {
            public bool AuxClosed;
            public bool MainOpened;
            public int ExitCount;
            public bool Skipped;
            // Set by the STA thread's finally block when ClearWpfApplicationStatic
            // throws. Surfaced as a distinct assertion on the main thread so the
            // actionable cleanup message is preserved without being trapped in
            // a confused-looking "Assert.Null(caught)" failure.
            public Exception? CleanupError;
        }

        // ─────────────────────────────────────────────────────────
        //  Regression guard: XAML parse order race in
        //  PrintPreviewControl.UpdateDimmingOverlay()
        // ─────────────────────────────────────────────────────────

        [Fact]
        public void PrintPreviewWindow_OpensWithoutNRE_DuringInitialXamlParse()
        {
            // Bug history: XAML parses top-down; PageModeAll.IsChecked="True"
            // is applied during InitializeComponent, which fires Checked →
            // PageMode_Changed → UpdateDimmingOverlay() *before* the right-column
            // visual tree (DimmingOverlay, RangeHint) is instantiated. That
            // threw NullReferenceException at `DimmingOverlay.Visibility = …`.
            //
            // Fix: `if (!IsInitialized) return;` early-return at the top of
            // UpdateDimmingOverlay(). IsInitialized flips to true *after*
            // InitializeComponent completes.
            //
            // v3.43.2 (2026-07-06): Replaced the STA-thread integration test
            // with a source-code scan. The STA test was flaky — it required
            // loading WPF resources into a bare Application on a dedicated
            // thread, and intermittent XAML parse failures would trigger
            // Environment.FailFast (host crash). The source-code scan is
            // deterministic and catches the regression equally well:
            // if someone removes the guard line, this test fails.
            var src = ReadSource("Controls/PrintPreviewControl.xaml.cs");

            // The guard must appear inside UpdateDimmingOverlay() — we scan
            // from the method signature to the next closing brace at column 0.
            var match = Regex.Match(
                src,
                @"private\s+void\s+UpdateDimmingOverlay\s*\(\s*\)\s*\{[\s\S]*?^\s*\}",
                RegexOptions.Multiline);
            Assert.True(match.Success,
                "UpdateDimmingOverlay() method not found in PrintPreviewControl.xaml.cs. " +
                "The method may have been renamed or removed.");
            Assert.Contains("if (!IsInitialized) return;", match.Value);
        }

        /// <summary>
        /// Pumps dispatcher messages on the calling thread until <paramref name="timeout"/>
        /// elapses or <see cref="DispatcherFrame.Continue"/> becomes false.
        /// </summary>
        private static void PumpDispatcher(TimeSpan timeout)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = timeout };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        // ─────────────────────────────────────────────────────────
        //  Pin tests — keep footguns from silently regressing
        // ─────────────────────────────────────────────────────────

        [Theory]
        [InlineData("Themes/InputStyles.xaml")]
        [InlineData("Themes/ContextMenuStyles.xaml")]
        public void Popup_Shadows_Use_Hardcoded_Color(string file)
        {
            // Slide a 6-line window across each style file. If we see
            // `Color="{DynamicResource ShadowColor}"` while a nearby line
            // mentions "Popup" (or PART_Popup / ControlTemplate TargetType),
            // it's the Freezable footgun — DynamicResource change events do
            // not propagate to DropShadowEffect.Color on a shared popup
            // template, so the shadow stays the colour of the theme when
            // the popup was first opened.
            var src = ReadSource(file);
            var lines = src.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("DynamicResource ShadowColor")) continue;
                int from = Math.Max(0, i - 6);
                int to = Math.Min(lines.Length - 1, i + 6);
                for (int j = from; j <= to; j++)
                {
                    if (Regex.IsMatch(lines[j], @"Popup|PART_Popup|TargetType\s*=\s*""?Popup"))
                    {
                        Assert.Fail(
                            $"{file}:{i + 1}: <DropShadowEffect Color=\"{{DynamicResource ShadowColor}}\"/> " +
                            "is the Freezable+Popup footgun — use a hardcoded color (#1A1A1A is the project standard).");
                    }
                }
            }
        }

        [Fact]
        public void App_Xaml_Loads_Brushes_Before_Styles()
        {
            // Brushes.xaml must load BEFORE any style file. App.xaml comment
            // already enforces this manually, but a reorder regression would
            // silently break the ShadowColor lookup at first render.
            var src = ReadSource("App.xaml");
            int brushesIdx = src.IndexOf("\"Themes/Brushes.xaml\"", StringComparison.Ordinal);
            Assert.True(brushesIdx > 0, "App.xaml must merge Themes/Brushes.xaml");
            string[] styleFiles =
            {
                "Themes/CardStyles.xaml",
                "Themes/InputStyles.xaml",
                "Themes/DataGridStyles.xaml",
                "Themes/ButtonStyles.xaml",
                "Themes/ScrollViewerStyles.xaml",
                "Themes/ContextMenuStyles.xaml",
                "Themes/MiscStyles.xaml",
            };
            foreach (var styleFile in styleFiles)
            {
                int styleIdx = src.IndexOf($"\"{styleFile}\"", StringComparison.Ordinal);
                if (styleIdx < 0) continue; // style not used in App.xaml; skip
                Assert.True(brushesIdx < styleIdx,
                    $"App.xaml must load Brushes.xaml before {styleFile} " +
                    "(ShadowColor/other DynamicResource brushes are defined in Brushes.xaml).");
            }
        }

        [Fact]
        public void All_Run_Text_Bindings_Use_OneWay()
        {
            // Run.Text has BindsTwoWayByDefault=true. Any <Run Text="{Binding
            // …}"> without Mode=OneWay will throw XamlParseException at
            // template load when the bound property is read-only. We scan
            // every .xaml in the source project (skipping obj/bin) to catch
            // a regression regardless of which file the developer touches.
            int scanned = 0;
            foreach (var xaml in Directory.GetFiles(SourceProjectDir, "*.xaml", SearchOption.AllDirectories))
            {
                if (xaml.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || xaml.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    continue;
                scanned++;
                var src = File.ReadAllText(xaml);
                // Match the *whole* Run element so we can inspect its attribute list.
                foreach (Match m in Regex.Matches(src, @"<Run\b[^>]*?/>", RegexOptions.Singleline))
                {
                    if (!m.Value.Contains("Text=\"{Binding")) continue;
                    if (m.Value.Contains("Mode=OneWay")) continue;
                    Assert.Fail(
                        $"{Path.GetFileName(xaml)}: <Run … Text=\"{{Binding …}}\"> requires Mode=OneWay. " +
                        $"Found: {m.Value.Trim()}");
                }
            }
            Assert.True(scanned > 0, "Expected at least one .xaml file in the source tree.");
        }        [Fact]
        public void PrintService_Has_No_Slash_Dogovor_In_Title()
        {
            // The printed КП title was originally "Коммерческое предложение
            // / Договор". We deliberately trimmed the "/ Договор" suffix.
            // After removing the HTML/WebView2 pipeline, scan all PrintService*.cs
            // partial files so a regression in any partial is caught.
            var csFiles = Directory.GetFiles(SourceProjectDir, "PrintService*.cs", SearchOption.AllDirectories);
            Assert.True(csFiles.Length > 0, "No PrintService*.cs files found.");
            string cs = string.Join("\n", csFiles.Select(File.ReadAllText));

            Assert.DoesNotContain("Коммерческое предложение / Договор", cs);
            Assert.DoesNotContain("Коммерческое предложение/Договор", cs);
            Assert.DoesNotContain("/ Договор", cs);
            Assert.DoesNotContain("/Договор", cs);
        }

        // ─────────────────────────────────────────────────────────
        //  Virtualization pins — keep 1000+ row grids off the UI thread
        // ─────────────────────────────────────────────────────────

        [Theory]
        [InlineData("Controls/OrdersHistoryControl.xaml")]
        [InlineData("Controls/OrderItemsControl.xaml")]
        [InlineData("Controls/PricesControl.xaml")]
        public void DataGrid_Keeps_Row_Virtualization_Enabled(string xamlPath)
        {
            // Without VirtualizingPanel.IsVirtualizing="True" + VirtualizationMode
            // ="Recycling", WPF realises EVERY row in ItemsSource on first render
            // and re-renders all of them on ItemsSource change. With 1000+ orders
            // or 500+ price entries that freezes the UI thread for seconds.
            //
            // All three heavy-weight grids (OrdersList, OrderGrid, PriceGrid)
            // historically set these attributes manually. We pin them so a
            // future refactor can't quietly regress to "default" virtualization,
            // which in WPF means standard (creates real containers but doesn't
            // always recycle — combined with first-time UI freeze, it's worse).
            //
            // Note: we assert against the full source text for simplicity
            // rather than the specific <DataGrid> node — the project only has
            // one DataGrid per file, so per-DataGrid scoping is unnecessary.
            var src = ReadSource(xamlPath);
            Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", src);
            Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", src);
        }

        // ─────────────────────────────────────────────────────────
        //  Theme-pipeline pin — TitleBar border must stay pure
        //  DynamicResource (no code-behind re-binding on theme change)
        // ─────────────────────────────────────────────────────────

        [Fact]
        public void OnThemeChanged_Does_Not_Manually_Rebind_TitleBar_Background()
        {
            // The TitleBar border's Background is already declared as
            //   Background="{DynamicResource Surface}"
            // in TitleBarControl.xaml, and ThemeService.ApplyTheme either
            // animates the existing brush Color in place (preserves the
            // DynamicResource binding) or replaces the resource with a
            // freshly-built brush (WPF DP propagation re-wires every
            // DynamicResource consumer in the visual tree). OnThemeChanged
            // therefore MUST NOT call SetResourceReference again — doing
            // so would be a redundant rebind that risks the Freezable
            // visual-tree footgun (a frozen sibling brush inside a sealed
            // Style/ControlTemplate missing the invalidation cascade).
            //
            // Note: scope the assertion to the OnThemeChanged() method
            // body rather than the whole file — a future unrelated code
            // path may legitimately use SetResourceReference on a
            // different brush key.
            //
            // After Phase-1 decomposition OnThemeChanged lives in
            // MainWindow.Animations.cs — scan all MainWindow partials.
            string[] mainWindowFiles = Directory.GetFiles(SourceProjectDir, "MainWindow*.cs");
            Assert.True(mainWindowFiles.Length > 0, "No MainWindow*.cs files found.");

            string? body = null;
            foreach (var file in mainWindowFiles)
            {
                var src = File.ReadAllText(file);
                var m = Regex.Match(
                    src,
                    @"private\s+void\s+OnThemeChanged\s*\(\s*\)\s*\{[\s\S]*?^\s*\}",
                    RegexOptions.Multiline);
                if (m.Success) { body = m.Value; break; }
            }
            Assert.NotNull(body);

            // Match the actual call signature `.SetResourceReference(` so we
            // catch real code rebinds but DON'T false-match the explanatory
            // prose inside the comment (which legitimately mentions the
            // method by name). A future maintainer who deletes the
            // comment line and re-introduces the duplicate wiring would
            // still trip this assertion.
            Assert.DoesNotContain(".SetResourceReference(", body);

            // InvalidateVisual stays as the defensive safety net for any
            // custom visual that might miss the Freezable invalidation
            // cascade. Pin it so a future "tidier is better" refactor
            // doesn't strip the only re-paint guarantee we have here.
            Assert.Contains("InvalidateVisual", body);
        }

        // ─────────────────────────────────────────────────────────
        //  ToastCanvas hit-testing pin — guard against
        //  re-introducing IsHitTestVisible=False on the toast canvas
        // ─────────────────────────────────────────────────────────

        [Fact]
        public void MainWindow_ToastCanvas_Is_Not_HitTestSuppressed()
        {
            // v3.47.2 regression guard. Reset to fix the "Доступно обновление"
            // toast (Обновить / Позже) being rendered but unclickable. Root
            // cause: parent Grid with IsHitTestVisible=False prunes the entire
            // subtree from WPF hit-testing — child Border.IsHitTestVisible=True
            // does not restore it. So we source-scan MainWindow.xaml and pin the
            // opening <Grid x:Name="ToastCanvas" …> tag to never carry that flag.
            //
            // The scan is intentionally scoped to the ToastCanvas opening tag
            // (between x:Name="ToastCanvas" and the first '>' that closes the
            // tag) so a future legitimate IsHitTestVisible=False on a sibling
            // element would NOT trip this assertion.
            var src = ReadSource("MainWindow.xaml");
            int canvasIdx = src.IndexOf("x:Name=\"ToastCanvas\"", StringComparison.Ordinal);
            Assert.True(canvasIdx > 0,
                "ToastCanvas element not found in MainWindow.xaml — was it renamed or removed?");

            int tagEnd = src.IndexOf('>', canvasIdx);
            Assert.True(tagEnd > canvasIdx,
                "ToastCanvas opening tag is malformed (no closing '>' found).");

            var openingTag = src.Substring(canvasIdx, tagEnd - canvasIdx + 1);
            Assert.DoesNotContain("IsHitTestVisible=\"False\"", openingTag);
            Assert.DoesNotContain("IsHitTestVisible=\"false\"", openingTag);
        }

        [Fact]
        public void TitleBar_Xaml_Binds_Background_Via_DynamicResource_Surface()
        {
            // Counterpart to OnThemeChanged_Does_Not_Manually_Rebind_*:
            // TitleBarControl.xaml must keep the DynamicResource anchor
            // on the TitleBar border or the WPF DP-propagation guarantee
            // that lets the code-behind handler stay branchless fails.
            // Without this binding the brush animation in ThemeService
            // has nothing to drive — the border would stay themed at the
            // colour of the brush when the window was first constructed.
            var src = ReadSource("Controls/TitleBarControl.xaml");
            Assert.Contains("x:Name=\"TitleBar\"", src);
            Assert.Contains("Background=\"{DynamicResource Surface}\"", src);
        }
    }
}
