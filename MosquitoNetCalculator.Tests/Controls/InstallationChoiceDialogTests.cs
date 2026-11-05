using System.Windows;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    [Collection("WPF_UI")]
    public class InstallationChoiceDialogTests
    {
        [Fact]
        public void Dialog_HasWidth520_AndInitializesCorrectly()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var item = new OrderItem
                {
                    Name = "Anwis",
                    Width = 1000,
                    Height = 1500,
                    Quantity = 2,
                    InstallationMode = 2
                };

                var dlg = new InstallationChoiceDialog(item, initialMode: 2);

                Assert.Equal(520, dlg.Width);
                Assert.Equal(2, dlg.SelectedMode);
                Assert.Equal("Anwis", dlg.TxtItemTitle.Text);
                Assert.Contains("1000×1500 мм", dlg.TxtItemDetails.Text);
                Assert.False(dlg.RememberAsDefault);
            });
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void Dialog_PreselectsSpecifiedMode(int initialMode)
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var item = new OrderItem
                {
                    Name = "Дверная сетка",
                    Width = 800,
                    Height = 2000,
                    Quantity = 1,
                    InstallationMode = initialMode
                };

                var dlg = new InstallationChoiceDialog(item, initialMode);

                Assert.Equal(initialMode, dlg.SelectedMode);
            });
        }

        [Fact]
        public void Dialog_SetsDescriptionsCorrectly_ForStandardNet()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var item = new OrderItem
                {
                    Name = "Anwis",
                    Width = 1000,
                    Height = 1500,
                    Quantity = 1,
                    InstallationMode = 0
                };

                var dlg = new InstallationChoiceDialog(item, initialMode: 0);

                Assert.Contains("Монтаж заложен в стоимость", dlg.TxtMode0Desc.Text);
                Assert.Contains("+400 руб. к ЗП", dlg.TxtMode0Desc.Text);
                Assert.Contains("Сетка ставится в раму при сборке конструкции", dlg.TxtMode2Desc.Text);
                Assert.Contains("+200 руб. к ЗП", dlg.TxtMode2Desc.Text);
                Assert.Contains("Клиент монтирует самостоятельно", dlg.TxtMode1Desc.Text);
                Assert.Contains("−500 руб.", dlg.TxtMode1Desc.Text);
            });
        }

        [Fact]
        public void Dialog_SetsDescriptionsCorrectly_ForLinearMeterItem()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var item = new OrderItem
                {
                    Name = "Отлив",
                    Width = 1200,
                    Height = 200,
                    Quantity = 2,
                    InstallationMode = 0
                };

                var dlg = new InstallationChoiceDialog(item, initialMode: 0);

                Assert.Contains("Монтаж рассчитывается отдельно", dlg.TxtMode0Desc.Text);
                Assert.Contains("руб./м.п.", dlg.TxtMode0Desc.Text);
                Assert.Contains("Клиент монтирует самостоятельно", dlg.TxtMode1Desc.Text);
            });
        }

        [Fact]
        public void Dialog_BtnApply_PicksSelectedModeWithoutException()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var item = new OrderItem { Name = "Anwis", Quantity = 1 };
                var dlg = new InstallationChoiceDialog(item, initialMode: 0);

                // Emulate selecting mode 2 and applying
                dlg.BtnMode2.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal(2, dlg.SelectedMode);

                var ex = Record.Exception(() => dlg.BtnApply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)));
                Assert.Null(ex);
                Assert.Equal(2, dlg.SelectedMode);
            });
        }

        [Fact]
        public void Dialog_InitializesAdjustmentField_AndUpdatesOnModeChange()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var item = new OrderItem { Name = "Anwis", Quantity = 1, InstallationMode = 0 };
                var dlg = new InstallationChoiceDialog(item, initialMode: 0);

                Assert.Equal("0", dlg.TxtAdjustment.Text);

                // Select Mode 1 (Без монтажа) -> should auto-fill default deduction (-500)
                dlg.BtnMode1.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal("-500", dlg.TxtAdjustment.Text);

                // Select Mode 0 (Монтаж включён) -> should auto-fill 0
                dlg.BtnMode0.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal("0", dlg.TxtAdjustment.Text);
            });
        }

        [Fact]
        public void Dialog_CustomAmount_CommitsToModelAndProperty()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var item = new OrderItem { Name = "Anwis", Quantity = 1, InstallationMode = 2 };
                var dlg = new InstallationChoiceDialog(item, initialMode: 2);

                dlg.TxtAdjustment.Text = "+350";
                dlg.BtnApply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                Assert.Equal(350, dlg.CustomAmount);
                Assert.Equal(350, item.CurrentInstallationAmount);
                Assert.Equal(350, item.InstallationSurcharge);
            });
        }

        [Fact]
        public void Dialog_PreviewKeyDown_DoesNotInterceptNumberKeys_WhenAdjustmentFocused()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var item = new OrderItem { Name = "Anwis", Quantity = 1, InstallationMode = 0 };
                var dlg = new InstallationChoiceDialog(item, initialMode: 0);

                dlg.TxtAdjustment.Focus();

                var keyEventArgs = new System.Windows.Input.KeyEventArgs(
                    System.Windows.Input.Keyboard.PrimaryDevice,
                    new System.Windows.Interop.HwndSource(0, 0, 0, 0, 0, "", System.IntPtr.Zero),
                    0,
                    System.Windows.Input.Key.D2)
                {
                    RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent
                };

                dlg.RaiseEvent(keyEventArgs);

                // Mode must NOT have switched to 2!
                Assert.Equal(0, dlg.SelectedMode);
                Assert.False(keyEventArgs.Handled);
            });
        }
    }
}
