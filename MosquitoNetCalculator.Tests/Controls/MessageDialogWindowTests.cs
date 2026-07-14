using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Controls
{
    /// <summary>
    /// STA tests for the new XAML-based dialog windows.
    /// </summary>
    public class MessageDialogWindowTests
    {
        [Fact]
        public void MessageDialogWindow_SetsTitleAndMessage()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var buttons = new List<DialogButton<object>>
                {
                    new("OK", true, false, false, "PrimaryButton")
                };

                var window = new MessageDialogWindow("Test Title", "Test Message", buttons);

                Assert.Equal("Test Title", window.TitleText.Text);
                Assert.Equal("Test Message", window.MessageText.Text);
            });
        }

        [Fact]
        public void MessageDialogWindow_ButtonsPanel_ContainsConfiguredButtons()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var buttons = new List<DialogButton<object>>
                {
                    new("No", false, false, true, "GhostButton"),
                    new("Yes", true, true, false, "PrimaryButton")
                };

                var window = new MessageDialogWindow("Confirm", "Are you sure?", buttons);

                // Force layout so ItemsControl generates containers.
                window.Show();
                window.UpdateLayout();

                Assert.Equal(2, window.ButtonsPanel.Items.Count);
                window.Close();
            });
        }

        [Fact]
        public void UpdateAvailableWindow_SetsVersionAndAcceptedFalseByDefault()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var changelog = new List<UpdateItem>
                {
                    new() { Version = "3.45.0", Title = "Test", Type = "Новинка", Date = new DateTime(2026, 7, 12) }
                };

                var window = new UpdateAvailableWindow("3.45.0", changelog, isAutomatic: false);

                Assert.Equal("Версия 3.45.0", window.VersionText.Text);
                Assert.False(window.Accepted);
            });
        }

        [Fact]
        public void UpdateAvailableWindow_AutomaticMode_SetsDeferTextAndShowsHint()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new UpdateAvailableWindow("3.45.0", Array.Empty<UpdateItem>(), isAutomatic: true);

                Assert.Equal("Отложить", window.BtnCancel.Content);
                Assert.Equal(Visibility.Visible, window.DeferHintPanel.Visibility);
            });
        }

        [Fact]
        public void UpdateAvailableWindow_EmptyChangelog_ShowsNoChangelogText()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new UpdateAvailableWindow("3.45.0", Array.Empty<UpdateItem>(), isAutomatic: false);

                Assert.Equal(Visibility.Collapsed, window.ChangelogScroll.Visibility);
                Assert.Equal(Visibility.Visible, window.NoChangelogText.Visibility);
            });
        }

        [Fact]
        public void MessageDialogWindow_ButtonClick_SetsSelectedResultAndCloses()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var buttons = new List<DialogButton<object>>
                {
                    new("No", false, false, true, "GhostButton"),
                    new("Yes", true, true, false, "PrimaryButton")
                };

                var window = new MessageDialogWindow("Confirm", "Are you sure?", buttons);
                window.Show();
                window.UpdateLayout();

                // Find the generated Button in the ItemsControl and invoke its Click event.
                var container = window.ButtonsPanel.ItemContainerGenerator.ContainerFromIndex(1);
                var button = FindVisualChild<Button>(container);
                Assert.NotNull(button);
                button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));

                Assert.True((bool)window.SelectedResult!);
                Assert.False(window.IsVisible);
            });
        }

        [Fact]
        public void UpdateAvailableWindow_DownloadClick_SetsAcceptedTrue()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new UpdateAvailableWindow("3.45.0", Array.Empty<UpdateItem>(), isAutomatic: false);
                window.BtnDownload.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, window.BtnDownload));

                Assert.True(window.Accepted);
            });
        }

        [Fact]
        public void UpdateAvailableWindow_CancelClick_SetsAcceptedFalse()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var window = new UpdateAvailableWindow("3.45.0", Array.Empty<UpdateItem>(), isAutomatic: false);
                window.BtnCancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, window.BtnCancel));

                Assert.False(window.Accepted);
            });
        }

        [Fact]
        public void MessageDialogWindow_CloseButtonClick_ClosesWindow()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var buttons = new List<DialogButton<object>>
                {
                    new("OK", true, false, false, "PrimaryButton")
                };

                var window = new MessageDialogWindow("Title", "Message", buttons);
                window.Show();
                window.UpdateLayout();

                var closeButton = FindVisualChild<Button>(window, b => b.Content is TextBlock { Text: "\uE8BB" });
                Assert.NotNull(closeButton);
                closeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, closeButton));

                Assert.Null(window.SelectedResult);
                Assert.False(window.IsVisible);
            });
        }

        [Fact]
        public void MessageDialogWindow_EscapeKey_ClosesWindow()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var buttons = new List<DialogButton<object>>
                {
                    new("OK", true, false, false, "PrimaryButton")
                };

                var window = new MessageDialogWindow("Title", "Message", buttons);
                window.Show();
                window.UpdateLayout();

                window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape)
                {
                    RoutedEvent = Keyboard.KeyDownEvent
                });

                Assert.Null(window.SelectedResult);
                Assert.False(window.IsVisible);
            });
        }

        private static T? FindVisualChild<T>(DependencyObject? parent, Func<T, bool>? predicate = null) where T : DependencyObject
        {
            if (parent == null) return null;
            if (parent is T typed && (predicate == null || predicate(typed))) return typed;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var result = FindVisualChild(child, predicate);
                if (result != null) return result;
            }
            return null;
        }
    }
}
