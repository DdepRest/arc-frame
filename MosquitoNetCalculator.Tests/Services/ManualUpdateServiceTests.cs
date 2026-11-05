using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    [Collection("FileSystem")]
    public class ManualUpdateServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string? _originalDataDir;

        public ManualUpdateServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "arc-manual-update-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _originalDataDir = WatchdogService.UpdateDataDir;
            WatchdogService.UpdateDataDir = _tempDir;
        }

        public void Dispose()
        {
            WatchdogService.UpdateDataDir = _originalDataDir!;
            UpdateService.ShowManualUpdateConfirmOverride = null;
            UpdateService.LaunchWatchdogForTest = null;
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort */ }
        }

        [Fact]
        public void InspectManualArchive_NonExistentFile_ReturnsInvalid()
        {
            string missingPath = Path.Combine(_tempDir, "missing.zip");
            var result = UpdateService.InspectManualArchive(missingPath);

            Assert.False(result.IsValid);
            Assert.Contains("не найден", result.ErrorMessage);
        }

        [Fact]
        public void InspectManualArchive_EmptyOrCorruptedZip_ReturnsInvalid()
        {
            string corruptPath = Path.Combine(_tempDir, "corrupt.zip");
            File.WriteAllBytes(corruptPath, new byte[] { 1, 2, 3, 4, 5 });

            var result = UpdateService.InspectManualArchive(corruptPath);

            Assert.False(result.IsValid);
            Assert.Contains("повреждён", result.ErrorMessage);
        }

        [Fact]
        public void InspectManualArchive_ArchiveWithoutExe_ReturnsInvalid()
        {
            string noExeZip = Path.Combine(_tempDir, "no-exe.zip");
            using (var fs = File.Create(noExeZip))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("readme.txt");
                using var es = entry.Open();
                es.Write(new byte[] { 65, 66, 67 });
            }

            var result = UpdateService.InspectManualArchive(noExeZip);

            Assert.False(result.IsValid);
            Assert.Contains("MosquitoNetCalculator.exe", result.ErrorMessage);
        }

        [Fact]
        public void InspectManualArchive_ArchiveWithZipSlip_ReturnsInvalid()
        {
            string zipSlipZip = Path.Combine(_tempDir, "zipslip.zip");
            using (var fs = File.Create(zipSlipZip))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                archive.CreateEntry("../../evil.exe");
                archive.CreateEntry("MosquitoNetCalculator.exe");
            }

            var result = UpdateService.InspectManualArchive(zipSlipZip);

            Assert.False(result.IsValid);
            Assert.Contains("Zip-Slip", result.ErrorMessage);
        }

        [Fact]
        public void InspectManualArchive_ValidArchive_InspectsVersionFromProbe()
        {
            string validZip = Path.Combine(_tempDir, "valid-update.zip");

            // Copy real built dll or exe as MosquitoNetCalculator.exe
            string sourceBinary = Path.Combine(AppContext.BaseDirectory, "MosquitoNetCalculator.dll");
            if (!File.Exists(sourceBinary))
            {
                sourceBinary = Path.Combine(AppContext.BaseDirectory, "MosquitoNetCalculator.exe");
            }

            using (var fs = File.Create(validZip))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                if (File.Exists(sourceBinary))
                {
                    archive.CreateEntryFromFile(sourceBinary, "MosquitoNetCalculator.exe");
                }
                else
                {
                    // Fallback stub
                    var entry = archive.CreateEntry("MosquitoNetCalculator.exe");
                    using var es = entry.Open();
                    es.Write(new byte[] { 77, 90 }); // MZ header
                }
            }

            var result = UpdateService.InspectManualArchive(validZip);

            Assert.True(result.IsValid, result.ErrorMessage);
            Assert.Equal("MosquitoNetCalculator.exe", result.ExeEntryPath);
            if (File.Exists(sourceBinary))
            {
                Assert.NotNull(result.Version);
                Assert.Equal(3, result.Version!.Major);
            }
        }

        [Fact]
        public async Task ApplyManualUpdateAsync_WhenUserDeclinesConfirm_DoesNotStageOrLaunch()
        {
            string validZip = Path.Combine(_tempDir, "valid.zip");
            using (var fs = File.Create(validZip))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("MosquitoNetCalculator.exe");
                using var es = entry.Open();
                es.Write(new byte[] { 77, 90 });
            }

            bool launched = false;
            UpdateService.ShowManualUpdateConfirmOverride = (_, _) => false; // User clicks cancel
            UpdateService.LaunchWatchdogForTest = _ => launched = true;

            await UpdateService.ApplyManualUpdateAsync(validZip);

            Assert.False(launched);
            Assert.False(Directory.Exists(Path.Combine(_tempDir, "arc-update-stage")));
        }

        [Fact]
        public async Task ApplyManualUpdateAsync_WhenUacDeclined_CleansUpStaging()
        {
            string validZip = Path.Combine(_tempDir, "valid.zip");
            using (var fs = File.Create(validZip))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("MosquitoNetCalculator.exe");
                using var es = entry.Open();
                es.Write(new byte[] { 77, 90 });
            }

            UpdateService.ShowManualUpdateConfirmOverride = (_, _) => true; // User confirms
            UpdateService.LaunchWatchdogForTest = _ => throw new System.ComponentModel.Win32Exception(
                1223, "ERROR_CANCELLED — UAC prompt cancelled by user");

            await UpdateService.ApplyManualUpdateAsync(validZip);

            // CleanupStagedUpdate must have run
            Assert.False(Directory.Exists(Path.Combine(_tempDir, "arc-update-stage")));
            Assert.False(File.Exists(Path.Combine(_tempDir, "arc-update-watchdog.bat")));
        }
    }
}
