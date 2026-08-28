using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
