using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    /// <summary>
    /// Пароль админ-панели — ВШИТЫЙ (одинаковый во всех офисах, без настроек).
    /// Проверяется через AppSettingsService.VerifyAdminPassword.
    /// </summary>
    public class OfficeAdminPasswordTests
    {
        [Fact]
        public void VerifyAdminPassword_AcceptsEmbeddedPassword()
        {
            Assert.True(AppSettingsService.VerifyAdminPassword(AppSettingsService.EmbeddedAdminPassword));
        }

        [Fact]
        public void VerifyAdminPassword_RejectsWrongPassword()
        {
            Assert.False(AppSettingsService.VerifyAdminPassword("неправильный-пароль"));
            Assert.False(AppSettingsService.VerifyAdminPassword(""));
            Assert.False(AppSettingsService.VerifyAdminPassword(null));
        }

        [Fact]
        public void EmbeddedAdminPassword_IsNotEmpty()
        {
            Assert.False(string.IsNullOrWhiteSpace(AppSettingsService.EmbeddedAdminPassword));
        }

        [Fact]
        public void EmbeddedAdminPassword_IsNewOwnerPassword()
        {
            // Владелец сменил пароль на AZ123123Az (2026-08-23).
            Assert.Equal("AZ123123Az", AppSettingsService.EmbeddedAdminPassword);
            Assert.True(AppSettingsService.VerifyAdminPassword("AZ123123Az"));
            Assert.False(AppSettingsService.VerifyAdminPassword("2000200014az")); // старый пароль больше не действует
        }
    }
}
