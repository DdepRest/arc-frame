using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// v3.45.0 (Phase 4 refactoring): dedicated update-available dialog
    /// with version badge, changelog and download confirmation.
    /// </summary>
    public partial class UpdateAvailableWindow : Window
    {
        /// <summary>True if the user accepted the update.</summary>
        public bool Accepted { get; private set; }

        public UpdateAvailableWindow(string version, IEnumerable<UpdateItem> changelog, bool isAutomatic)
        {
            InitializeComponent();
            VersionText.Text = $"Версия {version}";
            BuildChangelog(changelog);

            if (isAutomatic)
            {
                BtnCancel.Content = "Отложить";
                DeferHintPanel.Visibility = Visibility.Visible;
            }
        }

        private void BuildChangelog(IEnumerable<UpdateItem> changelog)
        {
            var items = changelog as IReadOnlyCollection<UpdateItem> ?? new List<UpdateItem>(changelog);
            if (items.Count == 0)
            {
                ChangelogScroll.Visibility = Visibility.Collapsed;
                NoChangelogText.Visibility = Visibility.Visible;
                return;
            }

            foreach (var item in items)
            {
                ChangelogPanel.Children.Add(CreateVersionHeader(item));

                if (!string.IsNullOrEmpty(item.Title) &&
                    (item.Changes == null || !item.Changes.Contains(item.Title)))
                {
                    ChangelogPanel.Children.Add(CreateBullet(item.Title, isBold: true));
                }

                if (item.Changes?.Count > 0)
                {
                    foreach (var change in item.Changes)
                        ChangelogPanel.Children.Add(CreateBullet(change, isBold: false));
                }
            }
        }

        private UIElement CreateVersionHeader(UpdateItem item)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 10, 0, 4)
            };

            panel.Children.Add(new TextBlock
            {
                Text = $"v{item.Version}",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush?)TryFindResource("TextPrimary") ?? Brushes.Black,
                VerticalAlignment = VerticalAlignment.Center
            });

            var typeBrush = item.Type switch
            {
                "Новинка" => (Brush?)TryFindResource("Success") ?? Brushes.Green,
                "Исправление" => (Brush?)TryFindResource("Danger") ?? Brushes.Red,
                _ => (Brush?)TryFindResource("Warning") ?? Brushes.Orange
            };

            panel.Children.Add(new Border
            {
                Background = typeBrush,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = item.Type,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush?)TryFindResource("OnAccent") ?? Brushes.White
                }
            });

            if (item.Date != default)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = item.Date.ToString("dd.MM.yyyy"),
                    FontSize = 11,
                    Foreground = (Brush?)TryFindResource("TextMuted") ?? Brushes.Gray,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            return panel;
        }

        private UIElement CreateBullet(string text, bool isBold)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(8, 2, 0, 2)
            };

            panel.Children.Add(new TextBlock
            {
                Text = "•",
                FontSize = 12,
                Foreground = (Brush?)TryFindResource("TextMuted") ?? Brushes.Gray,
                Margin = new Thickness(0, 0, 6, 0)
            });

            var tb = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = (Brush?)TryFindResource("TextSecondary") ?? Brushes.DarkSlateGray,
                TextWrapping = TextWrapping.Wrap
            };
            if (isBold)
                tb.FontWeight = FontWeights.SemiBold;

            panel.Children.Add(tb);
            return panel;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Accepted = false;
            Close();
        }

        private void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            Accepted = true;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Accepted = false;
                Close();
            }
        }
    }
}
