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
    public class DraftItemsPickerDialogTests
    {
        private static void Click(Button button)
            => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));

        [Fact]
        public void DraftItemPickerViewModel_ColorSuffix_HasLeadingSpace_WhenColorIsSpecified()
        {
            var item = new OrderItemData
            {
                Name = "Anwis",
                Color = "Белый",
                Width = 600,
                Height = 1400,
                Quantity = 1,
                Price = 1200
            };

            var vm = new DraftItemPickerViewModel(item);

            Assert.Equal("Anwis", vm.Name);
            Assert.Equal(" (Белый)", vm.ColorSuffix);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void DraftItemPickerViewModel_ColorSuffix_IsEmpty_WhenColorIsNullOrWhitespace(string? color)
        {
            var item = new OrderItemData
            {
                Name = "Крыло",
                Color = color!,
                Width = 500,
                Height = 1200,
                Quantity = 2,
                Price = 1500
            };

            var vm = new DraftItemPickerViewModel(item);

            Assert.Equal("Крыло", vm.Name);
            Assert.Equal("", vm.ColorSuffix);
        }

        [Fact]
        public void DraftItemPickerViewModel_Details_FormatsDimensionsAndQuantityCorrectly()
        {
            var itemNet = new OrderItemData
            {
                Name = "Anwis",
                Width = 600,
                Height = 1400,
                Quantity = 2
            };
            var vmNet = new DraftItemPickerViewModel(itemNet);
            Assert.Equal("600×1400 мм · 2 шт.", vmNet.Details);

            var itemSeal = new OrderItemData
            {
                Name = "Уплотнение",
                Width = 0,
                Height = 0,
                Quantity = 10
            };
            var vmSeal = new DraftItemPickerViewModel(itemSeal);
            Assert.Equal("10 м.п.", vmSeal.Details);

            var itemPart = new OrderItemData
            {
                Name = "Ручка",
                Width = 0,
                Height = 0,
                Quantity = 4
            };
            var vmPart = new DraftItemPickerViewModel(itemPart);
            Assert.Equal("4 шт.", vmPart.Details);
        }

        [Fact]
        public void DraftItemPickerViewModel_IsSelected_RaisesPropertyChanged()
        {
            var item = new OrderItemData { Name = "Anwis", Quantity = 1 };
            var vm = new DraftItemPickerViewModel(item);

            Assert.True(vm.IsSelected);

            string? changedProp = null;
            vm.PropertyChanged += (_, e) => changedProp = e.PropertyName;

            vm.IsSelected = false;

            Assert.False(vm.IsSelected);
            Assert.Equal(nameof(DraftItemPickerViewModel.IsSelected), changedProp);
        }

        [Fact]
        public void Dialog_WindowProperties_FollowRequirements()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var draft = new OrderData();
                var dlg = new DraftItemsPickerDialog(draft);

                Assert.Equal(580, dlg.Width);
                Assert.Equal(SizeToContent.Height, dlg.SizeToContent);
                Assert.Equal(520, dlg.MaxHeight);
            });
        }

        [Fact]
        public void Dialog_PopulatesItemsFromDraft_SelectedByDefault()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var draft = new OrderData
                {
                    UpdatedAt = new DateTime(2026, 11, 2, 12, 0, 0),
                    Items = new List<OrderItemData>
                    {
                        new OrderItemData { Name = "Anwis", Color = "Белый", Width = 600, Height = 1400, Quantity = 1 },
                        new OrderItemData { Name = "Дверная", Color = "Коричневый", Width = 800, Height = 2000, Quantity = 2 }
                    }
                };

                var dlg = new DraftItemsPickerDialog(draft);

                Assert.Equal(2, dlg.Items.Count);
                Assert.True(dlg.Items[0].IsSelected);
                Assert.True(dlg.Items[1].IsSelected);
                Assert.Equal("Черновик от 02.11 12:00", dlg.TxtDraftDateInfo.Text);
            });
        }

        [Fact]
        public void Dialog_SelectAllCheckBox_TogglesSelection()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var draft = new OrderData
                {
                    Items = new List<OrderItemData>
                    {
                        new OrderItemData { Name = "Позиция 1", Quantity = 1 },
                        new OrderItemData { Name = "Позиция 2", Quantity = 1 }
                    }
                };

                var dlg = new DraftItemsPickerDialog(draft);

                // Uncheck All
                dlg.ChkSelectAll.IsChecked = false;
                Assert.False(dlg.Items[0].IsSelected);
                Assert.False(dlg.Items[1].IsSelected);

                // Check All
                dlg.ChkSelectAll.IsChecked = true;
                Assert.True(dlg.Items[0].IsSelected);
                Assert.True(dlg.Items[1].IsSelected);
            });
        }

        [Fact]
        public void Dialog_BtnCancel_ClosesWithDialogResultFalse()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var draft = new OrderData
                {
                    Items = new List<OrderItemData> { new OrderItemData { Name = "Item", Quantity = 1 } }
                };

                var dlg = new DraftItemsPickerDialog(draft);
                dlg.Loaded += (_, _) => Click(dlg.BtnCancel);
                bool? result = dlg.ShowDialog();

                Assert.False(result);
                Assert.Empty(dlg.SelectedItems);
            });
        }

        [Fact]
        public void Dialog_BtnAddSelected_CollectsOnlySelectedItems()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var item1 = new OrderItemData { Name = "Позиция 1", Quantity = 1 };
                var item2 = new OrderItemData { Name = "Позиция 2", Quantity = 2 };
                var draft = new OrderData
                {
                    Items = new List<OrderItemData> { item1, item2 }
                };

                var dlg = new DraftItemsPickerDialog(draft);
                // Unselect second item
                dlg.Items[1].IsSelected = false;

                dlg.Loaded += (_, _) => Click(dlg.BtnAddSelected);
                bool? result = dlg.ShowDialog();

                Assert.True(result);
                Assert.Single(dlg.SelectedItems);
                Assert.Same(item1, dlg.SelectedItems[0]);
            });
        }
    }
}
