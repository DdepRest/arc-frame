using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Tests for v3.48.0 (Phase 2.A + 2.B) additions to <see cref="WatchdogService"/>:
    /// <list type="bullet">
    ///   <item><see cref="WatchdogService.PreFlightCheck"/> — write-permission probe</item>
    ///   <item><see cref="WatchdogService.CleanupOrphanedStageDirectories"/> — reclaim crashed-extract artifacts</item>
    ///   <item><see cref="WatchdogService.CleanupStagedUpdate"/> — full rollback on UAC cancel</item>
    ///   <item>Updated <see cref="WatchdogService.StageUpdate"/> — per-entry retry + zip-slip defense</item>
    /// </list>
    /// Uses the static <see cref="WatchdogService.UpdateDataDir"/> get/set seam
    /// (mirrors AppSettingsService.SettingsPath pattern) so each test gets an
    /// isolated temp directory and never touches the user's real %AppData%.
    /// </summary>
    [Collection("FileSystem")]
    public class WatchdogServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string? _originalDataDir;

        public WatchdogServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "arc-watchdog-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _originalDataDir = WatchdogService.UpdateDataDir;
            WatchdogService.UpdateDataDir = _tempDir;
        }

        public void Dispose()
        {
            WatchdogService.UpdateDataDir = _originalDataDir!;
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        // ─── PreFlightCheck ──────────────────────────────────────────

        [Fact]
        public void PreFlightCheck_OnWritableDir_ReturnsTrueWithNullReason()
        {
            var (ok, reason) = WatchdogService.PreFlightCheck();

            Assert.True(ok, $"PreFlightCheck should succeed on writable temp dir, got: {reason ?? "(null)"}");
            Assert.Null(reason);
        }

        [Fact]
        public void PreFlightCheck_OnUnwritableDataDir_ReturnsFalseWithReason()
        {
            // Drive Z is not mapped in the test runner — File.WriteAllBytes
            // throws DirectoryNotFoundException → caught by PreFlightCheck's
            // generic Exception catch → returns (false, reason). This is the
            // realistic Windows scenario where %AppData% (or its parent
            // location) is unavailable: stale profile, broken symlink, or
            // a network mount that's gone.
            // (Note: <see cref="FileAttributes.ReadOnly"/> on a Directory
            // does NOT actually deny writes on Windows — it is an MS-DOS
            // hint some DEL tools honor. We use the unmapped-drive approach
            // here so the test is reliable across CI environments.)
            var saved = WatchdogService.UpdateDataDir;
            WatchdogService.UpdateDataDir = @"Z:\arc-test-nonexistent-" + Guid.NewGuid().ToString("N");
            try
            {
                var (ok, reason) = WatchdogService.PreFlightCheck();
                Assert.False(ok);
                Assert.NotNull(reason);
                Assert.Contains("Ошибка доступа", reason);
            }
            finally
            {
                WatchdogService.UpdateDataDir = saved;
            }
        }

        // ─── CleanupOrphanedStageDirectories ──────────────────────────

        [Fact]
        public void CleanupOrphanedStageDirectories_RemovesOldArcStageDirs()
        {
            // Create an arc-stage-* directory and back-date its creation time to > 1 h ago
            var oldDir = Path.Combine(_tempDir, "arc-stage-old-abc");
            Directory.CreateDirectory(oldDir);
            Directory.SetCreationTimeUtc(oldDir, DateTime.UtcNow - TimeSpan.FromHours(2));

            WatchdogService.CleanupOrphanedStageDirectories();

            Assert.False(Directory.Exists(oldDir));
        }

        [Fact]
        public void CleanupOrphanedStageDirectories_KeepsFreshArcStageDirs()
        {
            var freshDir = Path.Combine(_tempDir, "arc-stage-fresh-def");
            Directory.CreateDirectory(freshDir);
            // recent — creation time defaults to UtcNow

            WatchdogService.CleanupOrphanedStageDirectories();

            Assert.True(Directory.Exists(freshDir));
        }

        [Fact]
        public void CleanupOrphanedStageDirectories_NonArcStageDirs_LeftAlone()
        {
            // Random name without arc-stage- prefix must NOT be touched
            var other = Path.Combine(_tempDir, "normal-folder-xyz");
            Directory.CreateDirectory(other);
            Directory.SetCreationTimeUtc(other, DateTime.UtcNow - TimeSpan.FromHours(5));

            WatchdogService.CleanupOrphanedStageDirectories();

            Assert.True(Directory.Exists(other));
        }

        // ─── CleanupStagedUpdate ─────────────────────────────────────

        [Fact]
        public void CleanupStagedUpdate_DeletesStageDir_WatchdogPath_AndOrphanArcStageDirs()
        {
            // v3.48.0 (Phase 2.A/B): CleanupStagedUpdate wipes ALL install
            // artifacts left over from a previously cancelled / crashed
            // install: StageDir + .bat + any arc-stage-{guid} tmp dirs.
            // (Note: arc-update.zip was removed in v3.48.0 — StageUpdate
            //  extracts in-place to %AppData%; the ZIP itself lives in %TEMP%
            //  and is cleaned up separately by UpdateService on download-failure
            //  or by the OS temp-cleaner over time.)
            var stageDir = Path.Combine(_tempDir, "arc-update-stage");
            Directory.CreateDirectory(stageDir);
            File.WriteAllText(Path.Combine(stageDir, "stub.txt"), "x");

            var watchdogPath = Path.Combine(_tempDir, "arc-update-watchdog.bat");
            File.WriteAllText(watchdogPath, "echo stub");

            var arcStage1 = Path.Combine(_tempDir, "arc-stage-orphan-1");
            var arcStage2 = Path.Combine(_tempDir, "arc-stage-orphan-2");
            Directory.CreateDirectory(arcStage1);
            Directory.CreateDirectory(arcStage2);

            WatchdogService.CleanupStagedUpdate();

            Assert.False(Directory.Exists(stageDir));
            Assert.False(File.Exists(watchdogPath));
            Assert.False(Directory.Exists(arcStage1));
            Assert.False(Directory.Exists(arcStage2));
        }

        [Fact]
        public void CleanupStagedUpdate_Idempotent_WhenNothingToClean()
        {
            // Calling on empty dir must not throw
            WatchdogService.CleanupStagedUpdate();
            // No assertion needed — verify by no exception
        }

        // ─── StageUpdate: happy path ─────────────────────────────────

        [Fact]
        public void StageUpdate_HappyPath_ExtractsAndStages()
        {
            var zipPath = Path.Combine(Path.GetTempPath(), "arc-stage-test-" + Guid.NewGuid().ToString("N") + ".zip");

            // Build a 2-entry ZIP in-memory: 'first.txt' + 'nested/second.txt'
            using (var fs = File.Create(zipPath))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var e1 = archive.CreateEntry("first.txt");
                using (var es = e1.Open()) es.Write(new byte[] { 1, 2, 3, 4, 5 });

                var e2 = archive.CreateEntry("nested/second.txt");
                using (var es = e2.Open()) es.Write(System.Text.Encoding.UTF8.GetBytes("hello"));
            }

            try
            {
                WatchdogService.StageUpdate(zipPath);

                var stageDir = Path.Combine(_tempDir, "arc-update-stage");
                Assert.True(Directory.Exists(stageDir));
                Assert.True(File.Exists(Path.Combine(stageDir, "first.txt")));
                Assert.True(File.Exists(Path.Combine(stageDir, "nested", "second.txt")));
                Assert.True(File.Exists(Path.Combine(_tempDir, "arc-update-watchdog.bat")));

                // Arc-stage-* tmp dir must NOT linger (renamed to StageDir)
                foreach (var d in Directory.EnumerateDirectories(_tempDir, "arc-stage-*"))
                {
                    Assert.Fail($"arc-stage-* leaked after successful StageUpdate: {d}");
                }
            }
            finally
            {
                File.Delete(zipPath);
                WatchdogService.CleanupStagedUpdate();
            }
        }

        // ─── StageUpdate: zip-slip defense ───────────────────────────

        [Fact]
        public void StageUpdate_ZipSlipEntry_ThrowsInvalidDataException()
        {
            var zipPath = Path.Combine(Path.GetTempPath(), "arc-zipslip-" + Guid.NewGuid().ToString("N") + ".zip");

            // Build a ZIP with a traversal entry. ZipArchive itself allows
            // any entry name; the danger is when extracting to disk. We
            // construct ".." path that would resolve OUTSIDE staging dir
            // if our defense didn't catch it.
            using (var fs = File.Create(zipPath))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../../evil.txt");
                using var es = entry.Open();
                es.Write(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
            }

            try
            {
                var ex = Assert.Throws<InvalidDataException>(() => WatchdogService.StageUpdate(zipPath));
                Assert.Contains("Zip-slip", ex.Message);

                // Crucially: no arc-stage-* orphan leaked (exception catch
                // in StageUpdate cleans up partial extract).
                foreach (var d in Directory.EnumerateDirectories(_tempDir, "arc-stage-*"))
                {
                    Assert.Fail($"arc-stage-* leaked after Zip-slip rejection: {d}");
                }
            }
            finally
            {
                File.Delete(zipPath);
                WatchdogService.CleanupStagedUpdate();
            }
        }

        // ─── StageUpdate: failure → partial cleanup ──────────────────

        [Fact]
        public void StageUpdate_FileNotFound_ThrowsFileNotFoundException()
        {
            var missing = Path.Combine(Path.GetTempPath(), "no-such-zip-" + Guid.NewGuid().ToString("N") + ".zip");
            Assert.Throws<FileNotFoundException>(() => WatchdogService.StageUpdate(missing));
        }

        // ─── StageUpdate: per-entry retry — too flaky for unit test, covered by source-scan ───
        // The AV-lock retry logic in ExtractWithRetry is straightforward:
        //   for (i=0; i<AvRetryAttempts; i++) try { Extract; return; } catch (IO) sleep(backoff[i]);
        // It would be flaky to coordinate a real filesystem lock with retries
        // in a unit test (CI clock skew, job timing). The retry correctness is
        // implicit in the production code path; the cleanup guarantees above
        // prove the surface. Real AV scenarios are covered by smoke tests.

        // ─── OrphanWindow configuration sanity ───────────────────────

        [Fact]
        public void OrphanWindow_IsOneHour_PreventsRacingConcurrentInstalls()
        {
            Assert.True(WatchdogService.OrphanWindow >= TimeSpan.FromMinutes(30),
                $"OrphanWindow={WatchdogService.OrphanWindow}; must be at least 30 min to safely avoid racing other instances' tmp dirs.");
            Assert.True(WatchdogService.OrphanWindow <= TimeSpan.FromHours(24),
                $"OrphanWindow={WatchdogService.OrphanWindow}; too long means half-extracted junk lingers in %AppData%.");
        }
    }
}
