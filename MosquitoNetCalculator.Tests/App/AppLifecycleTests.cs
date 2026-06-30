using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Xml.Linq;
using Xunit;

namespace MosquitoNetCalculator.Tests.App
{
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
    /// </summary>
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
                finally { gate.Set(); }
            });

            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(gate.Wait(TimeSpan.FromSeconds(20)), "STA test thread did not finish in time");
            t.Join();

            Assert.Null(caught);
            Assert.False(result.Skipped, "Application was already running in this AppDomain — cannot run STA lifecycle test.");
            Assert.True(result.AuxClosed, "Auxiliary window closed");
            Assert.True(result.MainOpened, "Main window opened");
            Assert.Equal(1, result.ExitCount);   // app exited exactly once, only after MainWindow.Close
        }

        private sealed class LifecycleResult
        {
            public bool AuxClosed;
            public bool MainOpened;
            public int ExitCount;
            public bool Skipped;
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
        public void Print_Template_Has_No_Slash_Dogovor()
        {
            // The printed КП title was originally "Коммерческое предложение
            // / Договор". We deliberately trimmed the "/ Договор" suffix.
            // Any collapse that re-adds "/ Договор" in either the resource
            // template or the inline fallback (and the source-side print
            // service) is a regression.
            //
            // After Phase-2 decomposition FillTemplate (the inline fallback)
            // lives in PrintService.HtmlBuilder.cs — scan all PrintService*.cs
            // partial files so a regression in any partial is caught.
            string html = ReadSource("Resources/print_template.html");
            var csFiles = Directory.GetFiles(SourceProjectDir, "PrintService*.cs", SearchOption.AllDirectories);
            Assert.True(csFiles.Length > 0, "No PrintService*.cs files found.");
            string cs = string.Join("\n", csFiles.Select(File.ReadAllText));

            foreach (var (src, name) in new[] { (html, "print_template.html"), (cs, "PrintService*.cs") })
            {
                Assert.DoesNotContain("Коммерческое предложение / Договор", src);
                Assert.DoesNotContain("Коммерческое предложение/Договор", src);
                Assert.DoesNotContain("/ Договор", src);
                Assert.DoesNotContain("/Договор", src);
            }
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
