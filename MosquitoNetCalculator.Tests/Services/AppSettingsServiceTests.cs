using System;
using System.IO;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// File-IO isolation: AppSettingsService.SettingsPath is redirected
    /// to a unique temp directory per test instance (the same pattern
    /// used by ManualChecklistTests). The production settings.json in
    /// %AppData% is never touched — the snapshot is restored in Dispose.
    /// </summary>
    [Collection("FileSystem")]
    public class AppSettingsServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _originalSettingsPath;

        public AppSettingsServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "mosquito_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            _originalSettingsPath = AppSettingsService.SettingsPath;
            AppSettingsService.SettingsPath = Path.Combine(_tempDir, "settings.json");
        }

        public void Dispose()
        {
            AppSettingsService.SettingsPath = _originalSettingsPath;
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        [Fact]
        public void LoadContractPrefix_ReturnsDefault_WhenNoFile()
        {
            Assert.Equal("1", AppSettingsService.LoadContractPrefix());
        }

        [Fact]
        public void SaveContractPrefix_AndLoad_Roundtrip()
        {
            AppSettingsService.SaveContractPrefix("5");
            Assert.Equal("5", AppSettingsService.LoadContractPrefix());
        }

        [Fact]
        public void SaveContractPrefix_PreservesTheme()
        {
            AppSettingsService.SaveTheme("dark");
            AppSettingsService.SaveContractPrefix("3");
            Assert.Equal("dark", AppSettingsService.LoadTheme());
        }

        [Fact]
        public void LoadTheme_ReturnsLight_WhenNoFile()
        {
            Assert.Equal("light", AppSettingsService.LoadTheme());
        }

        [Fact]
        public void SaveTheme_AndLoad_Roundtrip()
        {
            AppSettingsService.SaveTheme("dark");
            Assert.Equal("dark", AppSettingsService.LoadTheme());
        }

        [Fact]
        public void SaveTheme_PreservesContractPrefix()
        {
            AppSettingsService.SaveContractPrefix("7");
            AppSettingsService.SaveTheme("dark");
            Assert.Equal("7", AppSettingsService.LoadContractPrefix());
        }

        [Fact]
        public void SaveContractPrefix_HandlesEmptyString()
        {
            AppSettingsService.SaveContractPrefix("");
            Assert.Equal("1", AppSettingsService.LoadContractPrefix());
        }

        [Fact]
        public void SaveContractPrefix_TrimsWhitespace()
        {
            AppSettingsService.SaveContractPrefix("  3  ");
            Assert.Equal("3", AppSettingsService.LoadContractPrefix());
        }

        [Fact]
        public void SaveTheme_NormalizesCase()
        {
            AppSettingsService.SaveTheme("DARK");
            Assert.Equal("dark", AppSettingsService.LoadTheme());
        }

        [Fact]
        public void SaveTheme_HandlesEmptyString()
        {
            AppSettingsService.SaveTheme("");
            Assert.Equal("light", AppSettingsService.LoadTheme());
        }

        [Fact]
        public void LoadTheme_HandlesCorruptedFile()
        {
            File.WriteAllText(AppSettingsService.SettingsPath, "not valid json{{{");
            Assert.Equal("light", AppSettingsService.LoadTheme());
        }

        [Fact]
        public void LoadContractPrefix_HandlesCorruptedFile()
        {
            File.WriteAllText(AppSettingsService.SettingsPath, "corrupted");
            Assert.Equal("1", AppSettingsService.LoadContractPrefix());
        }

        [Fact]
        public void SaveContractPrefix_NullBecomesDefault()
        {
            AppSettingsService.SaveContractPrefix(null!);
            Assert.Equal("1", AppSettingsService.LoadContractPrefix());
        }
    }
}
