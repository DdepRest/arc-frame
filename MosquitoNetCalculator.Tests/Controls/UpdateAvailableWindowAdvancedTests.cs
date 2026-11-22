using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    [Collection("WPF_UI")]
    public class UpdateAvailableWindowAdvancedTests
    {
        [Fact]
        public void UpdateAvailableWindow_ChipsAndText_For0Of3()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new UpdateAvailableWindow("4.0.0", Array.Empty<UpdateItem>(), isAutomatic: true, isForced: false, deferralCount: 0);

                Assert.Equal(Visibility.Visible, window.DeferralPanel.Visibility);
                Assert.Equal("Отложено: 0 из 3. Осталось 3", window.DeferralText.Text);
                Assert.Equal("Установить и перезапустить", window.BtnDownload.Content);
                Assert.NotNull(window.DeferralPanel.ToolTip);
                Assert.NotNull(window.BtnCancel.ToolTip);

                var subtle = (Brush)window.FindResource("SubtleBorder");
                Assert.Equal(subtle, window.Chip1.Background);
                Assert.Equal(subtle, window.Chip2.Background);
                Assert.Equal(subtle, window.Chip3.Background);
            });
        }

        [Fact]
        public void UpdateAvailableWindow_ChipsAndText_For1Of3()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new UpdateAvailableWindow("4.0.0", Array.Empty<UpdateItem>(), isAutomatic: true, isForced: false, deferralCount: 1);

                Assert.Equal("Отложено: 1 из 3. Осталось 2", window.DeferralText.Text);

                var subtle = (Brush)window.FindResource("SubtleBorder");
                var success = (Brush)window.FindResource("Success");
                Assert.Equal(success, window.Chip1.Background);
                Assert.Equal(subtle, window.Chip2.Background);
                Assert.Equal(subtle, window.Chip3.Background);
            });
        }

        [Fact]
        public void UpdateAvailableWindow_ChipsAndText_For2Of3()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new UpdateAvailableWindow("4.0.0", Array.Empty<UpdateItem>(), isAutomatic: true, isForced: false, deferralCount: 2);

                Assert.Equal("Отложено: 2 из 3. Осталась 1 попытка!", window.DeferralText.Text);

                var subtle = (Brush)window.FindResource("SubtleBorder");
                var warning = (Brush)window.FindResource("Warning");
                Assert.Equal(warning, window.Chip1.Background);
                Assert.Equal(warning, window.Chip2.Background);
                Assert.Equal(subtle, window.Chip3.Background);
            });
        }

        [Fact]
        public void UpdateAvailableWindow_ChipsAndText_For3Of3()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new UpdateAvailableWindow("4.0.0", Array.Empty<UpdateItem>(), isAutomatic: true, isForced: false, deferralCount: 3);

                Assert.Equal("Отложено: 3 из 3. На 4-й раз отложить нельзя!", window.DeferralText.Text);

                var danger = (Brush)window.FindResource("Danger");
                Assert.Equal(danger, window.Chip1.Background);
                Assert.Equal(danger, window.Chip2.Background);
                Assert.Equal(danger, window.Chip3.Background);
            });
        }

        [Fact]
        public void UpdateAvailableWindow_ForcedMode_HidesCancelAndClose_ShowsForcedBadge()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new UpdateAvailableWindow("4.0.0", Array.Empty<UpdateItem>(), isAutomatic: true, isForced: true, deferralCount: 3);

                Assert.Equal(Visibility.Collapsed, window.BtnClose.Visibility);
                Assert.Equal(Visibility.Collapsed, window.BtnCancel.Visibility);
                Assert.Equal(Visibility.Collapsed, window.DeferralPanel.Visibility);
                Assert.Equal(Visibility.Visible, window.ForcedBadge.Visibility);
                Assert.Equal("Установить и перезапустить", window.BtnDownload.Content);
            });
        }

        [Fact]
        public void UpdateAvailableWindow_ManualArchiveAndGitHubLink_Exist()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new UpdateAvailableWindow("4.0.0", Array.Empty<UpdateItem>(), isAutomatic: false);

                Assert.Equal(Visibility.Visible, window.BtnManualArchive.Visibility);
                Assert.NotNull(window.LnkGitHubReleases);
            });
        }
    }
}