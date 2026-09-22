using System;
using Microsoft.Win32;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Контракт <see cref="DependencyCheckerService"/>: страховка портабельного
    /// запуска. Инсталлятор гарантирует VC++ Redistributable — установленным
    /// пользователям тост не показывается вообще; портабельным — только если
    /// редистрибутива нет. Реестр изолируется в одноразовые кусты HKCU через
    /// инжект открывателей (паттерн FontSelfInstallServiceTests — тест НЕ
    /// оставляет значений в реальном реестре).
    /// </summary>
    public class DependencyCheckerServiceTests : IDisposable
    {
        private readonly Func<RegistryKey?>? _originalUninstallOpener;
        private readonly Func<RegistryKey?>? _originalVCRedistOpener;
        private readonly RegistryKey _fakeUninstallKey;
        private readonly RegistryKey _fakeVCRedistKey;
        private readonly string _uninstallHivePath;
        private readonly string _vcRedistHivePath;

        public DependencyCheckerServiceTests()
        {
            _originalUninstallOpener = DependencyCheckerService.UninstallKeyOpener;
            _originalVCRedistOpener = DependencyCheckerService.VCRedistKeyOpener;

            // КАЖДЫЙ вызов — новый дубликат ключа: сервис оборачивает результат
            // в using и закроет его сразу после чтения (ловушка из GOTCHAS §32).
            _uninstallHivePath = @"Software\MosquitoNetCalculator\Tests\DependencyChecker\Uninstall\"
                + Guid.NewGuid().ToString("N");
            _vcRedistHivePath = @"Software\MosquitoNetCalculator\Tests\DependencyChecker\VCRedist\"
                + Guid.NewGuid().ToString("N");
            _fakeUninstallKey = Registry.CurrentUser.CreateSubKey(_uninstallHivePath, writable: true);
            _fakeVCRedistKey = Registry.CurrentUser.CreateSubKey(_vcRedistHivePath, writable: true);
            WireFakeOpeners();
        }        private void WireFakeOpeners()
        {
            // OpenSubKey, а НЕ CreateSubKey: создатель молча пересоздал бы
            // удалённый куст, и сценарий «ключа нет» подсовывал сервису живой
            // ключ (ловушка §32 с обратным знаком — поймана тестами).
            DependencyCheckerService.UninstallKeyOpener = () =>
                Registry.CurrentUser.OpenSubKey(_uninstallHivePath);
            DependencyCheckerService.VCRedistKeyOpener = () =>
                Registry.CurrentUser.OpenSubKey(_vcRedistHivePath);
        }

        public void Dispose()
        {
            DependencyCheckerService.UninstallKeyOpener = _originalUninstallOpener;
            DependencyCheckerService.VCRedistKeyOpener = _originalVCRedistOpener;
            _fakeUninstallKey.Close();
            _fakeVCRedistKey.Close();
            Registry.CurrentUser.DeleteSubKeyTree(
                @"Software\MosquitoNetCalculator\Tests\DependencyChecker", throwOnMissingSubKey: false);
        }

        // ── IsInstalledInstall: детект установленного приложения ─────────────

        [Fact]
        public void IsInstalledInstall_UninstallKeyPresent_True()
        {
            _fakeUninstallKey.SetValue("DisplayName", "MosquitoNetCalculator");
            Assert.True(DependencyCheckerService.IsInstalledInstall());
        }

        [Fact]
        public void IsInstalledInstall_NoUninstallKey_False()
        {
            Registry.CurrentUser.DeleteSubKeyTree(_uninstallHivePath, throwOnMissingSubKey: false);
            Assert.False(DependencyCheckerService.IsInstalledInstall());
        }

        // ── IsVCRedistInstalled: маркер редистрибутива ───────────────────────

        [Fact]
        public void IsVCRedistInstalled_MarkerSetToOne_TrueWithVersion()
        {
            _fakeVCRedistKey.SetValue("Installed", 1, RegistryValueKind.DWord);
            _fakeVCRedistKey.SetValue("Version", "14.40.33810.00");

            Assert.True(DependencyCheckerService.IsVCRedistInstalled(out var version));
            Assert.Equal("14.40.33810.00", version);
        }

        [Fact]
        public void IsVCRedistInstalled_MarkerZero_False()
        {
            _fakeVCRedistKey.SetValue("Installed", 0, RegistryValueKind.DWord);
            Assert.False(DependencyCheckerService.IsVCRedistInstalled(out var version));
            Assert.Null(version);
        }

        [Fact]
        public void IsVCRedistInstalled_KeyMissing_False()
        {
            Registry.CurrentUser.DeleteSubKeyTree(_vcRedistHivePath, throwOnMissingSubKey: false);
            Assert.False(DependencyCheckerService.IsVCRedistInstalled(out var version));
            Assert.Null(version);
        }

        // ── NotifyIfMissingOnPortable: правило тоста ──────────────────────────
        // Проверяем ЧИСТОЕ правило «показывать или молчать» (NotifyIsNeeded),
        // а не сам тост: он требует живого окна и относится к glue-слою.

        [Fact]
        public void NotifyIsNeeded_PortableWithoutVCRedist_True()
        {
            Registry.CurrentUser.DeleteSubKeyTree(_uninstallHivePath, throwOnMissingSubKey: false);
            Assert.True(DependencyCheckerService.NotifyIsNeeded());
        }

        [Fact]
        public void NotifyIsNeeded_PortableWithVCRedist_False()
        {
            Registry.CurrentUser.DeleteSubKeyTree(_uninstallHivePath, throwOnMissingSubKey: false);
            _fakeVCRedistKey.SetValue("Installed", 1, RegistryValueKind.DWord);
            Assert.False(DependencyCheckerService.NotifyIsNeeded());
        }

        [Fact]
        public void NotifyIsNeeded_InstalledMachine_FalseEvenWithoutVCRedist()
        {
            // Установка гарантировала VC++ при инсталляции; отсутствие маркера
            // сейчас (удалили после установки) — не повод шуметь: тост только
            // для портабельного запуска (согласовано с владельцем).
            _fakeUninstallKey.SetValue("DisplayName", "MosquitoNetCalculator");
            Assert.False(DependencyCheckerService.NotifyIsNeeded());
        }

        [Fact]
        public void NotifyIsNeeded_RegistryAccessThrows_SilentFalse()
        {
            DependencyCheckerService.UninstallKeyOpener = () =>
                throw new System.UnauthorizedAccessException("политика");
            DependencyCheckerService.VCRedistKeyOpener = () =>
                throw new System.UnauthorizedAccessException("политика");
            Assert.False(DependencyCheckerService.NotifyIsNeeded());
        }
    }
}
