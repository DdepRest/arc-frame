using System;
using System.IO;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Forces ALL tests in the "FileSystem" collection to run strictly
    /// serial. Five test classes share <see cref="AppSettingsService.SettingsPath"/>
    /// (a mutable <c>static</c> property) — without this definition, xUnit
    /// can interleave their ctors/dispose across classes, causing flaky
    /// failures like <c>SaveContractPrefix_TrimsWhitespace</c> seeing a
    /// stale SettingsPath from a different class.
    /// </summary>
    [CollectionDefinition("FileSystem", DisableParallelization = true)]
    public class FileSystemTestCollection { }

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

        [Fact]
        public void LoadOrCreateDeviceId_GeneratesOnce_StableAfterwards()
        {
            var first = AppSettingsService.LoadOrCreateDeviceId();
            Assert.False(string.IsNullOrWhiteSpace(first));
            Assert.Equal(32, first.Length); // GUID N-формат

            var second = AppSettingsService.LoadOrCreateDeviceId();
            Assert.Equal(first, second);
        }

        [Fact]
        public void LoadOrCreateDeviceId_DifferentSettingsDirectory_DifferentDevice()
        {
            var a = AppSettingsService.LoadOrCreateDeviceId();

            // Другой каталог настроек = «другое устройство» (другой ПК): ID живёт
            // рядом с settings.json (файл device-id), поэтому устройство = каталог.
            AppSettingsService.SettingsPath = Path.Combine(_tempDir, "device2", "settings.json");
            var b = AppSettingsService.LoadOrCreateDeviceId();

            Assert.NotEqual(a, b);
            AppSettingsService.SettingsPath = Path.Combine(_tempDir, "settings.json");
        }

        [Fact]
        public void LoadOrCreateDeviceId_SameSettingsDirectory_SameDevice_EvenWithoutSavedId()
        {
            // Два «процесса» на одном ПК: settings.json ещё без DeviceId, но файл
            // device-id уже создан первым запуском → второй получает тот же ID
            // (кросс-процессная защита от дублей).
            var first = AppSettingsService.LoadOrCreateDeviceId();

            // Сбросим settings.json (останется только файл device-id): «второй запуск»
            // не увидит DeviceId в настройках и возьмёт ID из файла.
            var file = Path.Combine(_tempDir, "device-id");
            Assert.True(File.Exists(file));
            File.Delete(AppSettingsService.SettingsPath);

            var second = AppSettingsService.LoadOrCreateDeviceId();
            Assert.Equal(first, second); // тот же ID из файла device-id
        }
    }
}
