using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    [Collection("WPF_UI")]
    public class DraftRecoveryBannerTests
    {
        private static void Click(Button button)
            => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));

        [Fact]
        public void Constructor_StartsCollapsed()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var banner = new DraftRecoveryBanner();
                Assert.Equal(Visibility.Collapsed, banner.Visibility);
                Assert.Null(banner.CurrentDraft);
            });
        }

        [Fact]
        public void DisplayDraft_RendersMetadata_AndBecomesVisible()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var banner = new DraftRecoveryBanner();
                var draft = new OrderData
                {
                    UpdatedAt = new DateTime(2026, 11, 2, 14, 30, 0),
                    ClientName = "Иванов Иван",
                    Items = new List<OrderItemData>
                    {
                        new OrderItemData { Name = "Anwis", Width = 600, Height = 1400, Quantity = 2, Price = 1500 },
                        new OrderItemData { Name = "Дверная", Width = 800, Height = 2000, Quantity = 1, Price = 3000 }
                    }
                };

                banner.DisplayDraft(draft);

                Assert.Equal(Visibility.Visible, banner.Visibility);
                Assert.Same(draft, banner.CurrentDraft);
                Assert.Equal("от 02.11 14:30 (позиций: 2, клиент: Иванов Иван)", banner.TxtDraftInfo.Text);
            });
        }

        [Fact]
        public void DisplayDraft_WithFallbackClientPhoneOrNewOrder()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var banner = new DraftRecoveryBanner();
                var draftWithPhone = new OrderData
                {
                    UpdatedAt = new DateTime(2026, 11, 2, 10, 0, 0),
                    ClientName = "",
                    ClientPhone = "+7 999 123-45-67",
                    Items = new List<OrderItemData>()
                };

                banner.DisplayDraft(draftWithPhone);
                Assert.Equal("от 02.11 10:00 (позиций: 0, клиент: +7 999 123-45-67)", banner.TxtDraftInfo.Text);

                var draftAnonymous = new OrderData
                {
                    UpdatedAt = new DateTime(2026, 11, 2, 10, 0, 0),
                    ClientName = "   ",
                    ClientPhone = "",
                    Items = null!
                };

                banner.DisplayDraft(draftAnonymous);
                Assert.Equal("от 02.11 10:00 (позиций: 0, клиент: Новый заказ)", banner.TxtDraftInfo.Text);
            });
        }

        [Fact]
        public void BtnRestore_RaisesRestoreRequested()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var banner = new DraftRecoveryBanner();
                var draft = new OrderData { UpdatedAt = DateTime.Now };
                banner.DisplayDraft(draft);

                int restoreEvents = 0;
                banner.RestoreRequested += (_, _) => restoreEvents++;

                Click(banner.BtnRestore);

                Assert.Equal(1, restoreEvents);
                Assert.Equal(Visibility.Visible, banner.Visibility);
            });
        }

        [Fact]
        public void BtnSelective_RaisesSelectiveRestoreRequested()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var banner = new DraftRecoveryBanner();
                var draft = new OrderData { UpdatedAt = DateTime.Now };
                banner.DisplayDraft(draft);

                int selectiveEvents = 0;
                banner.SelectiveRestoreRequested += (_, _) => selectiveEvents++;

                Click(banner.BtnSelective);

                Assert.Equal(1, selectiveEvents);
                Assert.Equal(Visibility.Visible, banner.Visibility);
            });
        }

        [Fact]
        public void BtnDiscard_RaisesDiscardRequested_HidesBanner_AndClearsDraft()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var banner = new DraftRecoveryBanner();
                var draft = new OrderData { UpdatedAt = DateTime.Now };
                banner.DisplayDraft(draft);

                int discardEvents = 0;
                banner.DiscardRequested += (_, _) => discardEvents++;

                Click(banner.BtnDiscard);

                Assert.Equal(1, discardEvents);
                Assert.Equal(Visibility.Collapsed, banner.Visibility);
                Assert.Null(banner.CurrentDraft);
            });
        }

        [Fact]
        public void BtnClose_RaisesPostponeRequested_HidesBanner_AndKeepsDraftIntact()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var banner = new DraftRecoveryBanner();
                var draft = new OrderData { UpdatedAt = DateTime.Now };
                banner.DisplayDraft(draft);

                int postponeEvents = 0;
                banner.PostponeRequested += (_, _) => postponeEvents++;

                Click(banner.BtnClose);

                Assert.Equal(1, postponeEvents);
                Assert.Equal(Visibility.Collapsed, banner.Visibility);
                // Важно: закрытие по крестику не удаляет черновик!
                Assert.NotNull(banner.CurrentDraft);
                Assert.Same(draft, banner.CurrentDraft);
            });
        }

        [Fact]
        public void HideBanner_CollapsesBannerVisibility()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var banner = new DraftRecoveryBanner();
                banner.DisplayDraft(new OrderData { UpdatedAt = DateTime.Now });
                Assert.Equal(Visibility.Visible, banner.Visibility);

                banner.HideBanner();
                Assert.Equal(Visibility.Collapsed, banner.Visibility);
            });
        }

        [Fact]
        public void Buttons_HaveConfiguredStyles()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var banner = new DraftRecoveryBanner();
                Assert.NotNull(banner.BtnRestore.Style);
                Assert.NotNull(banner.BtnSelective.Style);
                Assert.NotNull(banner.BtnDiscard.Style);
                Assert.NotNull(banner.BtnClose.Style);

                // BtnSelective uses GhostButton (not nonexistent SecondaryButton)
                var ghostButtonStyle = banner.FindResource("GhostButton") as Style;
                Assert.NotNull(ghostButtonStyle);
                Assert.Equal(ghostButtonStyle, banner.BtnSelective.Style);
                Assert.Equal(ghostButtonStyle, banner.BtnDiscard.Style);

                var primaryButtonStyle = banner.FindResource("PrimaryButton") as Style;
                Assert.NotNull(primaryButtonStyle);
                Assert.Equal(primaryButtonStyle, banner.BtnRestore.Style);

                var dialogCloseButtonStyle = banner.FindResource("DialogCloseButton") as Style;
                Assert.NotNull(dialogCloseButtonStyle);
                Assert.Equal(dialogCloseButtonStyle, banner.BtnClose.Style);
            });
        }
    }
}
