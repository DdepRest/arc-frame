using System;
using System.IO;
using System.Linq;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Логика окна «Что нового»: решение о показе и выборка записей changelog
    /// новее последней виденной версии. File-IO изолирован через
    /// redirect SettingsPath (коллекция "FileSystem" — серийно).
    /// </summary>
    [Collection("FileSystem")]
    public class WhatsNewServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _originalSettingsPath;

        public WhatsNewServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "mnc_whatsnew_test_" + Guid.NewGuid().ToString("N"));
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
        public void ShouldShow_NullLastSeen_ReturnsFalse()
        {
            Assert.False(WhatsNewService.ShouldShow(new Version(3, 48, 3), null));
        }

        [Fact]
        public void ShouldShow_SameVersion_ReturnsFalse()
        {
            Assert.False(WhatsNewService.ShouldShow(new Version(3, 48, 3), "3.48.3"));
        }

        [Fact]
        public void ShouldShow_NewerCurrent_ReturnsTrue()
        {
            Assert.True(WhatsNewService.ShouldShow(new Version(3, 48, 3), "3.47.4"));
        }

        [Fact]
        public void ShouldShow_UnparseableLastSeen_ReturnsFalse()
        {
            Assert.False(WhatsNewService.ShouldShow(new Version(3, 48, 3), "not-a-version"));
        }

        [Fact]
        public void GetChanges_WhenUpdated_ReturnsEntriesNewerThanLastSeen()
        {
            var changes = WhatsNewService.GetChanges(new Version(3, 48, 3), "3.47.4");

            Assert.NotEmpty(changes);
            Assert.All(changes, c => Assert.True(
                Version.Parse(c.Version) > new Version(3, 47, 4)));
        }

        [Fact]
        public void GetChanges_NewestFirst()
        {
            var changes = WhatsNewService.GetChanges(new Version(3, 48, 3), "3.40.0");

            Assert.NotEmpty(changes);
            var versions = changes.Select(c => Version.Parse(c.Version)).ToArray();
            Assert.Equal(versions.OrderByDescending(v => v), versions);
        }

        [Fact]
        public void GetChanges_NoUpdate_ReturnsEmpty()
        {
            Assert.Empty(WhatsNewService.GetChanges(new Version(3, 48, 3), "3.48.3"));
            Assert.Empty(WhatsNewService.GetChanges(new Version(3, 48, 3), null));
        }

        [Fact]
        public void SaveAndLoad_LastSeenVersion_Roundtrip()
        {
            Assert.Null(AppSettingsService.LoadLastSeenVersion());

            AppSettingsService.SaveLastSeenVersion("3.48.3");
            Assert.Equal("3.48.3", AppSettingsService.LoadLastSeenVersion());
        }

        [Fact]
        public void SaveLastSeenVersion_PreservesOtherSettings()
        {
            AppSettingsService.SaveContractPrefix("9");
            AppSettingsService.SaveLastSeenVersion("3.48.3");
            Assert.Equal("9", AppSettingsService.LoadContractPrefix());
        }
    }
}
