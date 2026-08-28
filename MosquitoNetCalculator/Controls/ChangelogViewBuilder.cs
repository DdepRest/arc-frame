using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// Общий построитель списка изменений для окон, показывающих changelog
    /// (<see cref="UpdateAvailableWindow"/>, <see cref="WhatsNewWindow"/>):
    /// заголовок версии с бейджем типа и датой + маркированные пункты.
    /// </summary>
    internal static class ChangelogViewBuilder
    {
        public static void Build(Panel target, IEnumerable<UpdateItem> changelog)
        {
            foreach (var item in changelog)
            {
                target.Children.Add(CreateVersionHeader(item));

                if (!string.IsNullOrEmpty(item.Title) &&
                    (item.Changes == null || !item.Changes.Contains(item.Title)))
                {
                    target.Children.Add(CreateBullet(item.Title, isBold: true));
                }

                if (item.Changes?.Count > 0)
                {
                    foreach (var change in item.Changes)
                        target.Children.Add(CreateBullet(change, isBold: false));
                }
            }
        }

        private static UIElement CreateVersionHeader(UpdateItem item)
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
                Foreground = (Brush?)Application.Current?.TryFindResource("TextPrimary") ?? Brushes.Black,
                VerticalAlignment = VerticalAlignment.Center
            });

            var typeBrush = item.Type switch
            {
                "Новинка" => (Brush?)Application.Current?.TryFindResource("Success") ?? Brushes.Green,
                "Исправление" => (Brush?)Application.Current?.TryFindResource("Danger") ?? Brushes.Red,
                _ => (Brush?)Application.Current?.TryFindResource("Warning") ?? Brushes.Orange
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
                    Foreground = (Brush?)Application.Current?.TryFindResource("OnAccent") ?? Brushes.White
                }
            });

            if (item.Date != default)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = item.Date.ToString("dd.MM.yyyy"),
                    FontSize = 11,
                    Foreground = (Brush?)Application.Current?.TryFindResource("TextMuted") ?? Brushes.Gray,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            return panel;
        }

        private static UIElement CreateBullet(string text, bool isBold)
        {
            // Grid (не StackPanel): колонка "*" получает конечную ширину,
            // поэтому TextWrapping=Wrap реально переносит длинный текст.
            // В StackPanel TextBlock измеряется бесконечной шириной и
            // длинная строка обрезается краем ScrollViewer.
            var grid = new Grid
            {
                Margin = new Thickness(8, 2, 0, 2)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var dot = new TextBlock
            {
                Text = "•",
                FontSize = 12,
                Foreground = (Brush?)Application.Current?.TryFindResource("TextMuted") ?? Brushes.Gray,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(dot, 0);
            grid.Children.Add(dot);

            var tb = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = (Brush?)Application.Current?.TryFindResource("TextSecondary") ?? Brushes.DarkSlateGray,
                TextWrapping = TextWrapping.Wrap
            };
            if (isBold)
                tb.FontWeight = FontWeights.SemiBold;
            Grid.SetColumn(tb, 1);
            grid.Children.Add(tb);

            return grid;
        }
    }
}
