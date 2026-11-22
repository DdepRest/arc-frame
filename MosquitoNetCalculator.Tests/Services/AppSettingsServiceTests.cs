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

        [Fact]
        public void AdminSession_UnlockAndReset_TracksSessionState()
        {
            AppSettingsService.ResetAdminSession();
            Assert.False(AppSettingsService.IsAdminUnlocked);

            AppSettingsService.UnlockAdminSession();
            Assert.True(AppSettingsService.IsAdminUnlocked);

            // EnsureAdminUnlocked returns true immediately when already unlocked without dialog
            Assert.True(AppSettingsService.EnsureAdminUnlocked(null));

            AppSettingsService.ResetAdminSession();
            Assert.False(AppSettingsService.IsAdminUnlocked);
        }

        // ─────────────────────────────────────────────────────────
        //  Периодическая проверка местоположения (LocationCheckService).
        //  Отметки живут в settings.json рядом с префиксом и должны переживать
        //  перезапись других настроек — иначе 30-дневный счётчик сбрасывался бы
        //  при каждом сохранении темы.
        // ─────────────────────────────────────────────────────────

        [Fact]
        public void LocationConfirmation_MissingInSettings_IsNull()
        {
            Assert.Null(AppSettingsService.LoadLocationConfirmedAtUtc());
            Assert.Null(AppSettingsService.LoadLocationSnoozeUntilUtc());
        }

        [Fact]
        public void LocationConfirmation_OldSettingsFileWithoutFields_IsNull()
        {
            // settings.json от версии без этой функции: полей нет — значит
            // подтверждения не было, а не «давно подтверждено».
            File.WriteAllText(
                AppSettingsService.SettingsPath,
                "{\"ContractPrefix\":\"2\",\"LocationName\":\"Старый офис\"}");
            AppSettingsService.InvalidateCache();

            Assert.Null(AppSettingsService.LoadLocationConfirmedAtUtc());
            Assert.Null(AppSettingsService.LoadLocationSnoozeUntilUtc());
            Assert.Equal("2", AppSettingsService.LoadContractPrefix());
        }

        [Fact]
        public void LocationConfirmedAt_RoundtripsInUtc_AndSurvivesOtherSaves()
        {
            var stamp = new DateTime(2026, 10, 25, 9, 30, 0, DateTimeKind.Utc);
            AppSettingsService.SaveLocationConfirmedAtUtc(stamp);

            var loaded = AppSettingsService.LoadLocationConfirmedAtUtc();
            Assert.NotNull(loaded);
            Assert.Equal(stamp, loaded!.Value);
            Assert.Equal(DateTimeKind.Utc, loaded.Value.Kind);

            // Соседние настройки пишутся тем же объектом — отметка не теряется.
            AppSettingsService.SaveContractPrefix("4");
            Assert.Equal(stamp, AppSettingsService.LoadLocationConfirmedAtUtc());
            Assert.Equal("4", AppSettingsService.LoadContractPrefix());
        }

        [Fact]
        public void MarkLocationConfirmed_StoresNow_AndCountsAsFreshForCheck()
        {
            AppSettingsService.MarkLocationConfirmed();

            var loaded = AppSettingsService.LoadLocationConfirmedAtUtc();
            Assert.NotNull(loaded);
            Assert.True(Math.Abs((DateTime.UtcNow - loaded!.Value).TotalMinutes) < 1);

            // Сразу после подтверждения баннер не показывается — иначе
            // «Всё верно» ничего бы не меняло.
            Assert.False(LocationCheckService.ShouldPrompt(
                loaded, AppSettingsService.LoadLocationSnoozeUntilUtc(), DateTime.UtcNow));
        }

        [Fact]
        public void LocationSnooze_RoundtripsInUtc_AndSuppressesPrompt()
        {
            var until = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
            AppSettingsService.SaveLocationSnoozeUntilUtc(until);

            var loaded = AppSettingsService.LoadLocationSnoozeUntilUtc();
            Assert.NotNull(loaded);
            Assert.Equal(until, loaded!.Value);

            // Срок годности истёк, но отсрочка ещё активна.
            Assert.False(LocationCheckService.ShouldPrompt(
                until.AddDays(-60), loaded, until.AddDays(-1)));
        }

        [Fact]
        public void LocationConfirmation_CorruptedDate_TreatedAsNeverConfirmed()
        {
            // Битая дата не должна ни валить старт, ни молча продлевать тишину.
            File.WriteAllText(
                AppSettingsService.SettingsPath,
                "{\"ContractPrefix\":\"2\",\"LocationConfirmedAtUtc\":\"не дата\"," +
                "\"LocationSnoozeUntilUtc\":\"2026-13-45\"}");
            AppSettingsService.InvalidateCache();

            Assert.Null(AppSettingsService.LoadLocationConfirmedAtUtc());
            Assert.Null(AppSettingsService.LoadLocationSnoozeUntilUtc());
            Assert.Equal("2", AppSettingsService.LoadContractPrefix());
        }

        [Fact]
        public void UpdateDeferral_GetAndIncrement_TrackPerVersion()
        {
            Assert.Equal(0, AppSettingsService.GetUpdateDeferralCount("1.0.0"));
            Assert.Equal(1, AppSettingsService.IncrementUpdateDeferral("1.0.0"));
            Assert.Equal(2, AppSettingsService.IncrementUpdateDeferral("1.0.0"));
            Assert.Equal(2, AppSettingsService.GetUpdateDeferralCount("1.0.0"));

            // New version resets to 0
            Assert.Equal(0, AppSettingsService.GetUpdateDeferralCount("1.1.0"));
            Assert.Equal(1, AppSettingsService.IncrementUpdateDeferral("1.1.0"));
        }

        [Fact]
        public void UpdateDeferral_Reset_SetsToZero()
        {
            AppSettingsService.IncrementUpdateDeferral("1.0.0");
            AppSettingsService.IncrementUpdateDeferral("1.0.0");
            Assert.Equal(2, AppSettingsService.LoadUpdateDeferralCount());

            AppSettingsService.ResetUpdateDeferral();
            Assert.Equal(0, AppSettingsService.LoadUpdateDeferralCount());
        }

        [Fact]
        public void UpdateDeferral_Failure_ResetsCount_AndTriggersCooldown()
        {
            AppSettingsService.IncrementUpdateDeferral("1.0.0");
            AppSettingsService.IncrementUpdateDeferral("1.0.0");
            Assert.Equal(2, AppSettingsService.LoadUpdateDeferralCount());
            Assert.False(AppSettingsService.IsUpdateInCooldown());

            AppSettingsService.RecordUpdateFailure();

            Assert.Equal(0, AppSettingsService.LoadUpdateDeferralCount());
            Assert.True(AppSettingsService.IsUpdateInCooldown());

            // 25 hours later, cooldown expired
            AppSettingsService.SetUpdateLastErrorTimeUtc(DateTime.UtcNow.AddHours(-25));
            Assert.False(AppSettingsService.IsUpdateInCooldown());
        }
    }
}
