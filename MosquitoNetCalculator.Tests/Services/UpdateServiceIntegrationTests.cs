using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Integration tests for <see cref="UpdateService"/> network layer.
    /// Uses mocked <see cref="HttpMessageHandler"/> to avoid real HTTP calls.
    ///
    /// Belongs to the "FileSystem" collection (same as AppSettingsServiceTests,
    /// PriceServiceTests, OrderStorageServiceTests, etc.) because every test
    /// in this class temporarily redirects <see cref="AppSettingsService.SettingsPath"/>
    /// to a temp file. Without explicit collection membership, xUnit runs
    /// this class in parallel with any FileSystem-collection test — both
    /// mutate the same <c>static</c> path, causing <c>PendingUpdateVersion</c>
    /// to be written to or read from the wrong file (the other test's temp).
    /// </summary>
    [Collection("FileSystem")]
    public class UpdateServiceIntegrationTests
    {
        private static HttpClient CreateMockClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            var mockHandler = new MockHttpMessageHandler(handler);
            return new HttpClient(mockHandler);
        }

        private static string GetTempFilePath()
            => Path.Combine(Path.GetTempPath(), $"arc-test-{Guid.NewGuid():N}.tmp");

        /// <summary>
        /// Synchronous IProgress&lt;T&gt; that records values immediately — avoids
        /// SynchronizationContext-posting flakiness that Progress&lt;T&gt; can introduce.
        /// </summary>
        private class ImmediateProgress : IProgress<int>
        {
            public System.Collections.Generic.List<int> Values { get; } = new();
            public void Report(int value) => Values.Add(value);
        }

        // ─── FetchManifestAsync tests ──────────────────────────

        [Fact]
        public async Task FetchManifestAsync_ReturnsManifest_OnSuccess()
        {
            var manifest = new UpdateManifest
            {
                Latest = "3.37.2",
                Releases = new() { new ReleaseInfo { Version = "3.37.2", Url = "http://example.com/zip", Sha256 = "abc" } }
            };
            var json = JsonSerializer.Serialize(manifest);
            var client = CreateMockClient((req, ct) =>
            {
                Assert.Contains("releases.json", req.RequestUri!.ToString());
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                });
            });

            var result = await UpdateService.FetchManifestAsync(client);

            Assert.NotNull(result);
            Assert.Equal("3.37.2", result!.Latest);
            Assert.Single(result.Releases);
        }

        [Fact]
        public async Task FetchManifestAsync_ReturnsNull_OnNonSuccessStatus()
        {
            var client = CreateMockClient((req, ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

            var result = await UpdateService.FetchManifestAsync(client);

            Assert.Null(result);
        }

        [Fact]
        public async Task FetchManifestAsync_ReturnsNull_OnException()
        {
            var client = CreateMockClient((req, ct) =>
                throw new HttpRequestException("Network error"));

            var result = await UpdateService.FetchManifestAsync(client);

            Assert.Null(result);
        }

        // ─── DownloadWithProgressAsync tests ───────────────────

        [Fact]
        public async Task DownloadWithProgressAsync_DownloadsFile_AndReportsProgress()
        {
            // Use a payload larger than one buffer (8192 bytes) so progress
            // reports intermediate percentages, not just a single 100%.
            var expectedContent = new byte[20_000];
            new Random(42).NextBytes(expectedContent);
            var client = CreateMockClient((req, ct) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent(expectedContent);
                response.Content.Headers.ContentLength = expectedContent.Length;
                return Task.FromResult(response);
            });

            var tempPath = GetTempFilePath();
            try
            {
                var progress = new ImmediateProgress();

                await UpdateService.DownloadWithProgressAsync(
                    "http://example.com/test.zip", tempPath, progress, client);

                Assert.True(File.Exists(tempPath));
                var actualContent = await File.ReadAllBytesAsync(tempPath);
                Assert.Equal(expectedContent, actualContent);
                Assert.Contains(100, progress.Values);
                // Should have reported at least one intermediate percentage
                Assert.True(progress.Values.Count > 1, "Progress should report intermediate values for a 20KB payload");
                Assert.All(progress.Values, p => Assert.InRange(p, 0, 100));
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public async Task DownloadWithProgressAsync_Throws_OnNonSuccess()
        {
            var client = CreateMockClient((req, ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

            var tempPath = GetTempFilePath();
            try
            {
                await Assert.ThrowsAsync<HttpRequestException>(() =>
                    UpdateService.DownloadWithProgressAsync(
                        "http://example.com/test.zip", tempPath, new ImmediateProgress(), client));
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public async Task DownloadWithProgressAsync_Reports100_WhenNoContentLength()
        {
            var expectedContent = "No content length provided"u8.ToArray();
            var client = CreateMockClient((req, ct) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent(expectedContent);
                // No Content-Length header set
                return Task.FromResult(response);
            });

            var tempPath = GetTempFilePath();
            try
            {
                var progress = new ImmediateProgress();

                await UpdateService.DownloadWithProgressAsync(
                    "http://example.com/test.zip", tempPath, progress, client);

                Assert.True(File.Exists(tempPath));
                Assert.Contains(100, progress.Values);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public async Task DownloadWithProgressAsync_Reports100_WhenZeroContentLength()
        {
            var client = CreateMockClient((req, ct) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new ByteArrayContent(Array.Empty<byte>());
                response.Content.Headers.ContentLength = 0;
                return Task.FromResult(response);
            });

            var tempPath = GetTempFilePath();
            try
            {
                var progress = new ImmediateProgress();

                await UpdateService.DownloadWithProgressAsync(
                    "http://example.com/test.zip", tempPath, progress, client);

                Assert.True(File.Exists(tempPath));
                Assert.Equal(0, new FileInfo(tempPath).Length);
                Assert.Contains(100, progress.Values);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        // ─── UpdateDetected event tests ──────────────────────────
        //
        // Background scheduler flow: CheckInBackgroundAsync → FetchManifestAsync
        // → GetAvailableUpdate → if newer release → show toast + fire
        // UpdateDetected event (consumed by MainWindow and forwarded to
        // MainWindowViewModel.AddNewUpdate). These tests verify the event
        // contract end-to-end with mocked HTTP.
        //
        // NOTE: UpdateService.CheckInBackgroundAsync calls
        // Application.Current?.MainWindow which is null in xUnit tests →
        // we'll skip CheckInBackgroundAsync in these tests and use a
        // small probe unit instead, OR refactor CheckInBackground to take
        // an optional owner parameter. For now we test the dispatch logic
        // by subscribing to UpdateDetected and asserting that a custom
        // probe of the same release-detection-equivalent path would
        // raise the event.
        //
        // Strategy: simulate what CheckInBackgroundAsync DOES upon
        // detecting a release — call the same FireUpdateDetected path
        // indirectly by invoking a Manifest fetch that finds a newer
        // version. Since the path is encapsulated, we test it via the
        // manifest fetch + GetAvailableUpdate, then make our own probe
        // subscriber that mirrors what CheckInBackgroundAsync does.
        // This is a contract assertion, not an integration of the full
        // flow; if the flow changes, this test will need to evolve.

        [Fact]
        public void UpdateDetected_HandlerRuns_AndReceivesProbeViaFireUpdateDetected()
        {
            // FireUpdateDetected is `internal` (InternalsVisibleTo) so we can
            // call it without reflection. This is the canonical fire path the
            // production code uses from both CheckInBackgroundAsync AND
            // RunUpdateFlowAsync — no magic, no bypass.
            var received = new System.Collections.Generic.List<UpdateItem>();
            Action<UpdateItem> handler = item => received.Add(item);
            UpdateService.UpdateDetected += handler;
            try
            {
                var probe = new UpdateItem
                {
                    Version = "TestStub",
                    Type = "Доступно",
                    Title = "Доступно обновление vTestStub",
                    Date = DateTime.UtcNow,
                    Changes = new System.Collections.Generic.List<string> { "stub" }
                };

                UpdateService.FireUpdateDetected(probe);

                Assert.Single(received);
                Assert.Same(probe, received[0]);
                Assert.Contains("TestStub", received[0].Title);
            }
            finally
            {
                UpdateService.UpdateDetected -= handler;
            }
        }

        [Fact]
        public void UpdateDetected_HandlerSwallows_ExceptionsFromSubscribers()
        {
            // Per FireUpdateDetected contract: a throwing subscriber must
            // NOT short-circuit subsequent subscribers. The implementation
            // iterates GetInvocationList() so each handler is in its own
            // try/catch. This is the assertion that protects the multi-
            // subscriber pipeline from one rogue handler.
            var received = new System.Collections.Generic.List<string>();
            Action<UpdateItem> throwing = item => throw new InvalidOperationException("simulated");
            Action<UpdateItem> ok = item => received.Add(item.Version);
            UpdateService.UpdateDetected += throwing;
            UpdateService.UpdateDetected += ok;
            try
            {
                var probe = new UpdateItem
                {
                    Version = "AfterThrow", Type = "Доступно", Title = "x",
                    Date = DateTime.UtcNow, Changes = new System.Collections.Generic.List<string> { "y" }
                };

                UpdateService.FireUpdateDetected(probe);

                Assert.Single(received);
                Assert.Equal("AfterThrow", received[0]);
            }
            finally
            {
                UpdateService.UpdateDetected -= throwing;
                UpdateService.UpdateDetected -= ok;
            }
        }

        // The constants below let tests assert against STUB-anchored strings
        // without ever writing the translated literal in the assertion body.
        // If UX ever rewrites the stub message, only the constant moves.
        private const string StubType = "Доступно";
        private const string StubIntentKeyword = "обновление";  // stable Russian intent word
        private const string StubFormatVersionTag = "v";          // marker for {version} token

        [Fact]
        public void UpdateDetected_CreateReleaseStub_HasExpectedShape()
        {
            // Locks the contract of the stub UpdateItem so the two fire
            // sites (background + startup manual) can't diverge. If anyone
            // changes the message text, this test tells the reviewer that
            // a deliberate UX change happened.
            //
            // Avoided: Assert.Single(Changes) is over-specified (any UX
            // addition of a link/secondary hint would force this test to
            // change). Avoided: DateTime.UtcNow delta < N — flakey against
            // CI clock skew; we only guard against future-dated clocks
            // because the helper bakes UtcNow at construction.
            //
            // Joined all messages into one string so we don't index [0]
            // implicitly depending on assert ordering — defensive against
            // future XUnit reordering or refactor that drops NotEmpty().
            var item = UpdateService.CreateReleaseStub("3.42.5");
            var allMessages = string.Join("\n", item.Changes);

            Assert.Equal("3.42.5", item.Version);
            Assert.Equal(StubType, item.Type);
            Assert.Contains(StubFormatVersionTag + "3.42.5", item.Title);
            Assert.NotEmpty(item.Changes);
            Assert.Contains(StubIntentKeyword, allMessages, StringComparison.OrdinalIgnoreCase);
            Assert.True(item.Date <= DateTime.UtcNow,
                "Date must not be future-dated (system clock guard)");
            Assert.True(item.Date > DateTime.UtcNow.AddMinutes(-5),
                "Date must be recent (helper stamps UtcNow at construction)");
        }

        [Fact]
        public async Task FetchManifestAsync_NewerReleaseAvailable_DoesNotFireEvent()
        {
            // Fetching does NOT itself raise UpdateDetected — only the
            // orchestration layer (CheckInBackgroundAsync) decides to
            // fire it after matching against CurrentVersion. This test
            // guards against a future regression where FetchManifest
            // side-fires the event.
            var receivedBefore = 0;
            Action<UpdateItem> probe = _ => receivedBefore++;
            UpdateService.UpdateDetected += probe;
            try
            {
                var manifest = new UpdateManifest
                {
                    Latest = "99.99.9",
                    Releases = new() { new ReleaseInfo { Version = "99.99.9", Url = "http://x", Sha256 = "a" } }
                };
                var json = JsonSerializer.Serialize(manifest);
                var client = CreateMockClient((req, ct) =>
                    Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json)
                    }));

                var result = await UpdateService.FetchManifestAsync(client);

                Assert.NotNull(result);
                Assert.Equal(0, receivedBefore);
            }
            finally
            {
                UpdateService.UpdateDetected -= probe;
            }
        }

        // ─── Mock helper ───────────────────────────────────────

        private class MockHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

            public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
            {
                _sendAsync = sendAsync;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return _sendAsync(request, cancellationToken);
            }
        }

        // ─── RunUpdateFlowAsync end-to-end tests ─────────────────────────
        //
        // These tests close the e2e gap left after lower-level coverage:
        // they assert that the production flow path —
        //   CheckAndApplyAsync → RunUpdateFlowAsync → FetchManifest → dialog
        //   → confirmed → FireUpdateDetected → download → ... —
        // actually invokes FireUpdateDetected as soon as the user accepts
        // the dialog, BEFORE downloading anything. The download step is
        // stubbed to throw so the test stops cleanly before StageUpdate
        // (which would write %AppData% files) and Application.Shutdown (would
        // tear down the test runner).
        //
        // Static-state pollution controls:
        //   • AppSettingsService.SettingsPath redirected to a fresh temp file
        //     so pending-update saves don't touch the user's real settings.
        //   • UpdateService.UpdateDetected subscribers manually added +
        //     removed in [Fact] scope.
        //   • ShowUpdateAvailableOverride reset to null in `finally` so a
        //     subsequent test doesn't inherit a "always-confirm" leak.
        //   • IsChecking / IsDownloading are reset by the production
        //     `finally` block — no manual cleanup needed.

        [Fact]
        public async Task RunUpdateFlowAsync_ConfirmedDialog_FiresUpdateDetected_AndStopsOnDownloadFailure()
        {
            // ─── Arrange ──────────────────────────────────────────────────────────
            const string TestVersion = "999.0.0";
            var originalSettingsPath = AppSettingsService.SettingsPath;
            var tempSettingsPath = Path.Combine(Path.GetTempPath(), $"arc-settings-test-{Guid.NewGuid():N}.json");

            // ─── HTTP mock: manifest is newer; the actual .zip download throws.
            // This drives RunUpdateFlowAsync past dialog/fire, then takes the
            // download-failure branch (which returns cleanly without calling
            // WatchdogService.StageUpdate or Application.Current.Shutdown).
            var mockClient = CreateMockClient((req, ct) =>
            {
                var url = req.RequestUri?.ToString() ?? "";
                if (url.Contains("releases.json"))
                {
                    var manifest = new UpdateManifest
                    {
                        Latest = TestVersion,
                        Releases = new()
                        {
                            new ReleaseInfo
                            {
                                Version = TestVersion,
                                Url = $"http://mock.test/{TestVersion}.zip",
                                Sha256 = ""
                            }
                        }
                    };
                    var json = JsonSerializer.Serialize(manifest);
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json)
                    });
                }
                // Download request — fail deliberately so the test ends before
                // StageUpdate / Shutdown.
                throw new HttpRequestException("Mock: download intended to fail for test cleanup");
            });

            UpdateItem? capturedItem = null;
            Action<UpdateItem> probe = item => capturedItem = item;
            UpdateService.UpdateDetected += probe;
            UpdateService.ShowUpdateAvailableOverride = (_, _) => true; // simulate user clicking "Скачать и установить"

            var settingsSavedAtEnd = (string?)null;
            var tempHelperIsAlive = true;
            AppSettingsService.SettingsPath = tempSettingsPath;

            try
            {
                // ─── Act ───────────────────────────────────────────────────────────────
                await UpdateService.RunUpdateFlowAsync(owner: null, isAutomatic: true, httpClient: mockClient);

                // ─── Assert ────────────────────────────────────────────────────────────
                Assert.NotNull(capturedItem);
                Assert.Equal(TestVersion, capturedItem!.Version);
                Assert.Equal("Доступно", capturedItem.Type);
                Assert.Contains($"v{TestVersion}", capturedItem.Title);
                Assert.NotEmpty(capturedItem.Changes);
                // Stub contract preserved from CreateReleaseStub.

                // Production cleanup: finally block in RunUpdateFlowAsync resets flags.
                Assert.False(UpdateService.IsChecking);
                Assert.False(UpdateService.IsDownloading);

                // Capture AppSettings side-effect to verify in teardown.
                //
                // Production invariants (documented in RunUpdateFlowAsync):
                //   • `release == null` (no update available)  → SavePendingUpdateVersion(null)
                //   • `confirmed = false` (user deferred)       → SavePendingUpdateVersion(manifest.Latest)
                //   • `confirmed = true` → download success      → SavePendingUpdateVersion(null)
                //   • `confirmed = true` → download FAILURE      → NO pending save (this test's path)
                //
                // The last invariant is intentional: the user actively accepted the
                // dialog, but the installer couldn't reach the network. Leaving
                // pending UNCHANGED lets the next scheduler tick retry cleanly
                // without falsely holding state.
                settingsSavedAtEnd = AppSettingsService.LoadPendingUpdateVersion();
            }
            finally
            {
                // ─── Cleanup ──────────────────────────────────────────────────────────
                UpdateService.UpdateDetected -= probe;
                UpdateService.ShowUpdateAvailableOverride = null;
                AppSettingsService.SettingsPath = originalSettingsPath;
                if (tempHelperIsAlive && File.Exists(tempSettingsPath))
                    File.Delete(tempSettingsPath);
            }

            // Confirmed → download → fail preserves the (null) initial pending state.
            Assert.Null(settingsSavedAtEnd);
        }

        [Fact]
        public async Task RunUpdateFlowAsync_CancelledDialog_DoesNotFireUpdateDetected_PendingSaved()
        {
            // ─── Arrange ──────────────────────────────────────────────────────────
            const string TestVersion = "999.0.1";
            var originalSettingsPath = AppSettingsService.SettingsPath;
            var tempSettingsPath = Path.Combine(Path.GetTempPath(), $"arc-settings-test-{Guid.NewGuid():N}.json");
            AppSettingsService.SettingsPath = tempSettingsPath;

            var manifest = new UpdateManifest
            {
                Latest = TestVersion,
                Releases = new()
                {
                    new ReleaseInfo { Version = TestVersion, Url = "http://x", Sha256 = "" }
                }
            };
            var mockClient = CreateMockClient((req, ct) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(manifest))
                }));

            UpdateItem? captured = null;
            Action<UpdateItem> probe = item => captured = item;
            UpdateService.UpdateDetected += probe;
            UpdateService.ShowUpdateAvailableOverride = (_, _) => false; // user clicked "Отложить"

            try
            {
                // ─── Act ───────────────────────────────────────────────────────────────
                await UpdateService.RunUpdateFlowAsync(owner: null, isAutomatic: true, httpClient: mockClient);

                // ─── Assert ────────────────────────────────────────────────────────────
                Assert.Null(captured);
                Assert.Equal(TestVersion, AppSettingsService.LoadPendingUpdateVersion());
                Assert.False(UpdateService.IsChecking);
                Assert.False(UpdateService.IsDownloading);
            }
            finally
            {
                UpdateService.UpdateDetected -= probe;
                UpdateService.ShowUpdateAvailableOverride = null;
                AppSettingsService.SettingsPath = originalSettingsPath;
                if (File.Exists(tempSettingsPath))
                    File.Delete(tempSettingsPath);
            }
        }

        [Fact]
        public async Task RunUpdateFlowAsync_NoNewRelease_NoFire_NoPendingUpdate()
        {
            // ─── Arrange ──────────────────────────────────────────────────────────
            // Manifest.Latest is BELOW UpdateService.CurrentVersion (whatever
            // the assembly version is) so GetAvailableUpdate returns null.
            // RunUpdateFlowAsync should early-return after toast ("Обновлений
            // нет ✓") without firing UpdateDetected at all.
            var originalSettingsPath = AppSettingsService.SettingsPath;
            var tempSettingsPath = Path.Combine(Path.GetTempPath(), $"arc-settings-test-{Guid.NewGuid():N}.json");
            AppSettingsService.SettingsPath = tempSettingsPath;

            var manifest = new UpdateManifest
            {
                Latest = "1.0.0",
                Releases = new() { new ReleaseInfo { Version = "1.0.0", Url = "http://x", Sha256 = "" } }
            };
            var mockClient = CreateMockClient((req, ct) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(manifest))
                }));

            UpdateItem? captured = null;
            Action<UpdateItem> probe = item => captured = item;
            UpdateService.UpdateDetected += probe;
            // Override not set: even if reached, dialog would never fire because
            // no new release was found. We still null any leftover override from
            // a previous test for safety.
            UpdateService.ShowUpdateAvailableOverride = null;

            try
            {
                // ─── Act ───────────────────────────────────────────────────────────────
                await UpdateService.RunUpdateFlowAsync(owner: null, isAutomatic: true, httpClient: mockClient);

                // ─── Assert ────────────────────────────────────────────────────────────
                Assert.Null(captured);
                // Pending update explicitly cleared.
                Assert.Null(AppSettingsService.LoadPendingUpdateVersion());
                Assert.False(UpdateService.IsChecking);
                Assert.False(UpdateService.IsDownloading);
            }
            finally
            {
                UpdateService.UpdateDetected -= probe;
                UpdateService.ShowUpdateAvailableOverride = null;
                AppSettingsService.SettingsPath = originalSettingsPath;
                if (File.Exists(tempSettingsPath))
                    File.Delete(tempSettingsPath);
            }
        }

        // ─── v3.48.0 (Phase 2.A): UAC cancellation handling ──────────────────────
        //
        // Production flow when user declines runas (1223) or admin elevation is
        // unavailable (740/1313): RunUpdateFlowAsync must NOT silently fall back
        // to a non-elevated Process.Start (the previous behaviour left users on
        // Program Files seeing «✓ Установка» while no files actually moved). The
        // new contract:
        //   1. StageUpdate + PreFlightCheck ran successfully
        //   2. LaunchWatchdogForTest throws Win32Exception(1223) in test seam
        //   3. CleanupStagedUpdate runs (StageDir + .bat removed)
        //   4. SavePendingUpdateVersion(Latest) — next scheduler tick retries
        //   5. IsChecking / IsDownloading reset for the next RunUpdateFlowAsync attempt
        [Fact]
        public async Task RunUpdateFlowAsync_UacCancel_CleansUpAndSavesPendingVersion()
        {
            const string TestVersion = "999.0.5";

            var originalSettingsPath = AppSettingsService.SettingsPath;
            var tempSettingsPath = Path.Combine(Path.GetTempPath(),
                $"arc-settings-uac-{Guid.NewGuid():N}.json");
            AppSettingsService.SettingsPath = tempSettingsPath;

            var originalDataDir = WatchdogService.UpdateDataDir;
            var tempWatchdogDir = Path.Combine(Path.GetTempPath(),
                $"arc-watchdog-uac-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempWatchdogDir);
            WatchdogService.UpdateDataDir = tempWatchdogDir;

            // Pre-compute SHA-256 of a minimal valid in-memory ZIP so the verify
            // step passes and RunUpdateFlowAsync reaches the post-verify launch
            // branch where UAC cancellation happens.
            var zipBytes = UacTestHelpers.CreateMinimalZipBytes();
            var zipSha = UacTestHelpers.ComputeSha256Hex(zipBytes);

            var mockClient = CreateMockClient((req, ct) =>
            {
                var url = req.RequestUri?.ToString() ?? "";
                if (url.Contains("releases.json"))
                {
                    var manifest = new UpdateManifest
                    {
                        Latest = TestVersion,
                        Releases = new()
                        {
                            new ReleaseInfo
                            {
                                Version = TestVersion,
                                Url = "http://mock.test/uac-test.zip",
                                Sha256 = zipSha
                            }
                        }
                    };
                    var json = JsonSerializer.Serialize(manifest);
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json)
                    });
                }
                if (url.Contains("uac-test.zip"))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(zipBytes)
                    });
                }
                throw new InvalidOperationException($"Unexpected URL: {url}");
            });

            UpdateItem? captured = null;
            Action<UpdateItem> probe = item => captured = item;
            UpdateService.UpdateDetected += probe;
            UpdateService.ShowUpdateAvailableOverride = (_, _) => true; // user confirmed
            UpdateService.LaunchWatchdogForTest = _ => throw new System.ComponentModel.Win32Exception(
                1223, "ERROR_CANCELLED — test seam simulates UAC decline");

            try
            {
                await UpdateService.RunUpdateFlowAsync(owner: null, isAutomatic: true, httpClient: mockClient);

                // ── Post-cancel invariants ──────────────────────────────────
                // 1. Pending version saved for next-boot retry
                Assert.Equal(TestVersion, AppSettingsService.LoadPendingUpdateVersion());
                // 2. RunUpdateFlowAsync finished cleanly without throwing Application.Shutdown
                Assert.False(UpdateService.IsChecking);
                Assert.False(UpdateService.IsDownloading);
                // 3. UpdateDetected DID fire (user confirmed the dialog BEFORE we
                //    reached UAC step — captures the same semantics as real flow)
                Assert.NotNull(captured);
                Assert.Equal(TestVersion, captured!.Version);
                // 4. CleanupStagedUpdate ran — StageDir + .bat gone
                Assert.False(Directory.Exists(Path.Combine(tempWatchdogDir, "arc-update-stage")),
                    "StageDir must be cleaned up after UAC cancel");
                Assert.False(File.Exists(Path.Combine(tempWatchdogDir, "arc-update-watchdog.bat")),
                    "Watchdog .bat must be cleaned up after UAC cancel");
            }
            finally
            {
                UpdateService.UpdateDetected -= probe;
                UpdateService.ShowUpdateAvailableOverride = null;
                UpdateService.LaunchWatchdogForTest = null;
                AppSettingsService.SettingsPath = originalSettingsPath;
                WatchdogService.UpdateDataDir = originalDataDir;
                if (File.Exists(tempSettingsPath)) File.Delete(tempSettingsPath);
                try { if (Directory.Exists(tempWatchdogDir)) Directory.Delete(tempWatchdogDir, recursive: true); } catch { }
            }
        }

    }

    // ─── Helpers used by v3.48.0 UAC e2e test ──────────────────────────────────
    // Kept outside the test class so they don't show up in xUnit test discovery.
    internal static class UacTestHelpers
    {
        /// <summary>
        /// Build a 1-entry in-memory ZIP ('stub.txt') that mirrors the shape
        /// the production release pipeline produces (ZIP containing the
        /// update payload). Returned as a single byte array.
        /// </summary>
        public static byte[] CreateMinimalZipBytes()
        {
            using var ms = new MemoryStream();
            using (var archive = new System.IO.Compression.ZipArchive(
                ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry("stub.txt");
                using var es = entry.Open();
                var payload = System.Text.Encoding.UTF8.GetBytes("v3.48.0-uac-cancel-test");
                es.Write(payload, 0, payload.Length);
            }
            return ms.ToArray();
        }

        /// <summary>
        /// Hex SHA-256 of a byte array (matching what the production
        /// <c>UpdateVerifier.ComputeSha256</c> would produce). Lower-case,
        /// 64 chars, no prefix.
        /// </summary>
        public static string ComputeSha256Hex(byte[] data)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(data);
            var sb = new System.Text.StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
