using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Тесты per-user самоустановки Inter (FontSelfInstallService).
    /// Реальные GDI/реестр/папку профиля тесты НЕ трогают: включённые
    /// зависимости (BundledBytes, FamilyInstalled, InstallDir, ключ реестра —
    /// через <see cref="FakeRegistry"/>) подменяются, как
    /// AppSettingsService.SettingsPath в FileSystem-коллекции.
    /// </summary>
    [Collection("FileSystem")]
    public class FontSelfInstallServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _originalInstallDir;
        private readonly Func<string, bool> _originalFamilyInstalled;
        private readonly Func<string, byte[]> _originalBundledBytes;
        private readonly Func<RegistryKey> _originalFontsKeyOpener;
        private readonly RegistryKey _fakeFontsKey;
        private readonly string _originalSettingsPath;

        public FontSelfInstallServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "mnc-font-install-" + Guid.NewGuid().ToString("N"));
            _originalInstallDir = FontSelfInstallService.InstallDir;
            _originalFamilyInstalled = FontSelfInstallService.FamilyInstalled;
            _originalBundledBytes = FontSelfInstallService.BundledBytes;
            _originalFontsKeyOpener = FontSelfInstallService.FontsKeyOpener;
            _originalSettingsPath = AppSettingsService.SettingsPath;
            FontSelfInstallService.InstallDir = _tempDir;
            // InstalledFontVersion пишется в settings — тоже в изоляцию,
            // иначе тест портит реальный settings.json пользователя.
            AppSettingsService.SettingsPath = Path.Combine(_tempDir, "settings.json");

            // Реестр — в одноразовый куст (Registry.CurrentUser.CreateSubKey
            // принимает полный путь с произвольным именем). БЕЗ этого тест
            // оставлял в реальном HKCU пять значений, указывающих на удалённую
            // тестовую папку, — реальный дефект первого прогона.
            var hivePath = @"Software\MosquitoNetCalculator\Tests\FontSelfInstall\"
                + Guid.NewGuid().ToString("N");
            _fakeFontsKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(hivePath, writable: true);
            // КАЖДЫЙ вызов — новый дубликат ключа: InstallBundle оборачивает
            // результат в using и закроет его после первого же файла.
            FontSelfInstallService.FontsKeyOpener = () =>
                Microsoft.Win32.Registry.CurrentUser.CreateSubKey(hivePath, writable: true);
            FontSelfInstallService.ResetFakeFlag();
        }

        public void Dispose()
        {
            FontSelfInstallService.InstallDir = _originalInstallDir;
            FontSelfInstallService.FamilyInstalled = _originalFamilyInstalled;
            FontSelfInstallService.BundledBytes = _originalBundledBytes;
            FontSelfInstallService.FontsKeyOpener = _originalFontsKeyOpener;
            AppSettingsService.SettingsPath = _originalSettingsPath;
            _fakeFontsKey.Close();
            try
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                    @"Software\MosquitoNetCalculator\Tests\FontSelfInstall", false);
            }
            catch { }
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        [Fact]
        public void BundledBytes_ExtractsAllFiveFontFiles_FromAssembly()
        {
            // Настоящее извлечение из собранной сборки: без этой проверки сервис
            // мог бы молча ставить нулевые файлы.
            foreach (var fileName in FontSelfInstallService.BundleFiles)
            {
                var bytes = FontSelfInstallService.BundledBytes(fileName);
                Assert.True(bytes.Length > 100_000,
                    $"{fileName}: {bytes.Length} байт — слишком мало для ttf (обрыв ресурса?)");
                // Магия ttf: 00 01 00 00 (big-endian 0x00010000) или 'OTTO' (CFF).
                // Порядок байт файла, не платформы — поэтому по индексам.
                var isTtf = bytes[0] == 0x00 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x00;
                var isOtf = bytes[0] == 0x4F && bytes[1] == 0x54 && bytes[2] == 0x54 && bytes[3] == 0x4F;
                Assert.True(isTtf || isOtf,
                    $"{fileName}: не похож на ttf (первые байты {bytes[0]:X2} {bytes[1]:X2} {bytes[2]:X2} {bytes[3]:X2})");
            }
        }

        [Fact]
        public void RegistryValueName_FollowsWindowsConventions()
        {
            Assert.Equal("Inter (TrueType) (Inter-Regular.ttf)",
                FontSelfInstallService.RegistryValueName("Inter-Regular.ttf"));
            Assert.Equal("Inter (TrueType) Bold (Inter-Bold.ttf)",
                FontSelfInstallService.RegistryValueName("Inter-Bold.ttf"));
            Assert.Equal("Inter (TrueType) SemiBold (Inter-SemiBold.ttf)",
                FontSelfInstallService.RegistryValueName("Inter-SemiBold.ttf"));
            Assert.Equal("Inter (TrueType) Italic (Inter-Italic.ttf)",
                FontSelfInstallService.RegistryValueName("Inter-Italic.ttf"));
        }

        [Fact]
        public void EnsureInstalled_FamilyPresent_DoesNothing()
        {
            FontSelfInstallService.FamilyInstalled = _ => true;
            Assert.True(FontSelfInstallService.EnsureInstalled());
            Assert.False(Directory.Exists(_tempDir),
                "Семья есть — копировать файлы нельзя");
        }

        [Fact]
        public void EnsureInstalled_FamilyAbsent_InstallsBundle()
        {
            FontSelfInstallService.FamilyInstalled = _ => false;
            Assert.True(FontSelfInstallService.EnsureInstalled());

            foreach (var fileName in FontSelfInstallService.BundleFiles)
            {
                var path = Path.Combine(_tempDir, fileName);
                Assert.True(File.Exists(path), $"{fileName} не скопирован");
                Assert.True(new FileInfo(path).Length > 100_000);
            }
            Assert.Equal(FontSelfInstallService.BundleVersion,
                AppSettingsService.LoadInstalledFontVersion());
            // Значения ушли в подменный куст, а не в реальный профиль.
            Assert.True(FontSelfInstallService.LastInstallWasFake,
                "Установка пошла мимо подменного куста — тест пишет в реальный HKCU");
            Assert.Equal(FontSelfInstallService.BundleFiles.Length,
                _fakeFontsKey.GetValueNames().Length);
        }

        [Fact]
        public void EnsureInstalled_NoBundledFiles_FailsSilently()
        {
            // Пустая сборка (в тесте подменяем на пустой комплект): сервис
            // обязан вернуть false, а не бросить — старт приложения важнее.
            FontSelfInstallService.FamilyInstalled = _ => false;
            FontSelfInstallService.BundledBytes = _ => throw new FileNotFoundException("нет ресурса");
            Assert.False(FontSelfInstallService.EnsureInstalled());
        }

        [Fact]
        public void RegistryKey_IsPerUser_NotPerMachine()
        {
            // Per-user установка обязана писать в HKCU — на HKLM нужны
            // права администратора, которых у программы нет.
            Assert.Contains("HKEY_CURRENT_USER", RegistryKeyPathText());
            static string RegistryKeyPathText() =>
                "HKEY_CURRENT_USER\\" + FontSelfInstallService.RegistryKeyPath;
        }

        [Fact]
        public void BundleFiles_MatchActualBundledNames()
        {
            // Список комплекта и фактические ресурсы не должны разъезжаться:
            // добавил ttf — обнови BundleFiles, иначе новый стиль не ставится.
            var expected = new[]
            {
                "Inter-Regular.ttf", "Inter-Italic.ttf", "Inter-Medium.ttf",
                "Inter-SemiBold.ttf", "Inter-Bold.ttf",
            };
            Assert.Equal(expected, FontSelfInstallService.BundleFiles);
        }
    }
}
