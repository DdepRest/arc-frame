using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// STA-тесты неназойливого баннера проверки местоположения: контрол должен
    /// собираться на реальных темах приложения, не занимать место, пока он не
    /// нужен, и поднимать ровно по одному событию на каждую кнопку (MainWindow
    /// слушает их, чтобы записывать отметки — молча «проглоченный» клик здесь
    /// означал бы, что баннер завис навсегда).
    /// </summary>
    [Collection("WPF_UI")]
    public class LocationCheckBannerTests
    {
        private static void Click(Button button)
            => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));

        [Fact]
        public void Constructor_StartsCollapsed()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var banner = new LocationCheckBanner();
                Assert.Equal(Visibility.Collapsed, banner.Visibility);
            });
        }

        [Fact]
        public void Show_RendersLocationAndPrefix_AndBecomesVisible()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var banner = new LocationCheckBanner();
                banner.Show("Рудакова 76 — «Компания „Уют”»", "2");

                Assert.Equal(Visibility.Visible, banner.Visibility);
                // Название офиса и префикс разведены: офис — подписью в полосе,
                // префикс — отдельным чипом (в тексте он дублировал бы себя).
                Assert.Equal("Рудакова 76 — «Компания „Уют”»", banner.LocationText);
                Assert.Equal("префикс 2", banner.PrefixText);
                Assert.Equal(
                    "Рудакова 76 — «Компания „Уют”» · префикс 2",
                    banner.DisplayText);
            });
        }

        [Fact]
        public void Show_WithoutSavedName_ShowsPlaceholderInsteadOfBlankLine()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var banner = new LocationCheckBanner();
                banner.Show("", "5");

                // Пустая подпись слева от чипа выглядела бы как сбой вёрстки.
                Assert.Equal("не задано", banner.LocationText);
                Assert.Equal("префикс 5", banner.PrefixText);
                Assert.Equal("префикс договора 5", banner.DisplayText);
            });
        }

        [Fact]
        public void Show_WithoutPrefix_HidesPrefixChip()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var banner = new LocationCheckBanner();
                banner.Show("Офис на Рудакова", "  ");

                Assert.Equal("Офис на Рудакова", banner.LocationText);
                Assert.Equal(string.Empty, banner.PrefixText);
            });
        }

        [Fact]
        public void Show_AnnouncesWholeSummaryToScreenReaders()
        {
            // Диктор обходит полосу одним элементом: без общего имени чип
            // и название читались бы по отдельности, без связи между ними.
            TestAppThemes.RunOnSta(() =>
            {
                var banner = new LocationCheckBanner();
                banner.Show("Рудакова 76", "2");

                Assert.Equal(
                    "Проверка местоположения: Рудакова 76 · префикс 2",
                    System.Windows.Automation.AutomationProperties.GetName(banner));
            });
        }

        [Fact]
        public void ConfirmButton_RaisesConfirmedOnly()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var banner = new LocationCheckBanner();
                int confirmed = 0, changed = 0, snoozed = 0;
                banner.Confirmed += (_, _) => confirmed++;
                banner.ChangeRequested += (_, _) => changed++;
                banner.Snoozed += (_, _) => snoozed++;

                Click(banner.ConfirmButton);

                Assert.Equal(1, confirmed);
                Assert.Equal(0, changed);
                Assert.Equal(0, snoozed);
            });
        }

        [Fact]
        public void ChangeButton_RaisesChangeRequestedOnly()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var banner = new LocationCheckBanner();
                int confirmed = 0, changed = 0, snoozed = 0;
                banner.Confirmed += (_, _) => confirmed++;
                banner.ChangeRequested += (_, _) => changed++;
                banner.Snoozed += (_, _) => snoozed++;

                Click(banner.ChangeButton);

                Assert.Equal(0, confirmed);
                Assert.Equal(1, changed);
                Assert.Equal(0, snoozed);
            });
        }

        [Fact]
        public void SnoozeButton_RaisesSnoozedOnly()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var banner = new LocationCheckBanner();
                int confirmed = 0, changed = 0, snoozed = 0;
                banner.Confirmed += (_, _) => confirmed++;
                banner.ChangeRequested += (_, _) => changed++;
                banner.Snoozed += (_, _) => snoozed++;

                Click(banner.SnoozeButton);

                Assert.Equal(0, confirmed);
                Assert.Equal(0, changed);
                Assert.Equal(1, snoozed);
            });
        }

        [Fact]
        public void Hide_CollapsesAgain()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var banner = new LocationCheckBanner();
                banner.Show("Офис", "1");
                banner.Hide();

                Assert.Equal(Visibility.Collapsed, banner.Visibility);
            });
        }

        [Fact]
        public void Buttons_HaveAccessibleNames()
        {
            // Крестик без текста: диктор и UIA-харнесс находят кнопки только
            // по AutomationProperties.Name, поэтому оно обязательно у всех трёх.
            TestAppThemes.RunOnSta(() =>
            {
                var banner = new LocationCheckBanner();
                Assert.False(string.IsNullOrWhiteSpace(
                    System.Windows.Automation.AutomationProperties.GetName(banner.ConfirmButton)));
                Assert.False(string.IsNullOrWhiteSpace(
                    System.Windows.Automation.AutomationProperties.GetName(banner.ChangeButton)));
                Assert.False(string.IsNullOrWhiteSpace(
                    System.Windows.Automation.AutomationProperties.GetName(banner.SnoozeButton)));
            });
        }
    }
}
