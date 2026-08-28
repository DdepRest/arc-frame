using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// Окно «Что нового»: показывается после обновления программы и
    /// перезапуска, со списком изменений, добавленных после последней
    /// виденной пользователем версии (см. WhatsNewService).
    /// </summary>
    public partial class WhatsNewWindow : Window
    {
        public WhatsNewWindow(Version version, IEnumerable<UpdateItem> changes)
        {
            InitializeComponent();
            VersionText.Text = $"Версия {version}";

            var items = changes as IReadOnlyCollection<UpdateItem> ?? new List<UpdateItem>(changes);
            if (items.Count == 0)
            {
                ChangelogScroll.Visibility = Visibility.Collapsed;
                NoChangelogText.Visibility = Visibility.Visible;
                return;
            }

            ChangelogViewBuilder.Build(ChangelogPanel, items);
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

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Close();
        }
    }
}
