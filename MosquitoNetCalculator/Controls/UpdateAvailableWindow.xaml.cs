using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// Update-available dialog with version badge, changelog, download confirmation,
    /// deferral chip indicator, forced mode, and manual update archive selection.
    /// </summary>
    public partial class UpdateAvailableWindow : Window
    {
        private readonly bool _isForced;
        private readonly string _version;

        /// <summary>True if the user accepted the update.</summary>
        public bool Accepted { get; private set; }

        /// <summary>Compatibility with earlier test references to the deferral hint panel.</summary>
        public FrameworkElement DeferHintPanel => DeferralPanel;

        public UpdateAvailableWindow(
            string version,
            IEnumerable<UpdateItem> changelog,
            bool isAutomatic = false,
            bool isForced = false,
            int deferralCount = 0)
        {
            InitializeComponent();
            _version = version;
            _isForced = isForced;
            VersionText.Text = $"Версия {version}";
            BuildChangelog(changelog);

            if (_isForced)
            {
                BtnClose.Visibility = Visibility.Collapsed;
                BtnCancel.Visibility = Visibility.Collapsed;
                DeferralPanel.Visibility = Visibility.Collapsed;
                ForcedBadge.Visibility = Visibility.Visible;
                BtnDownload.Content = "Установить и перезапустить";
            }
            else
            {
                BtnCancel.Content = isAutomatic ? "Отложить" : "Отмена";
                if (isAutomatic)
                {
                    DeferralPanel.Visibility = Visibility.Visible;
                    UpdateDeferralChips(deferralCount);
                    BtnDownload.Content = "Установить и перезапустить";
                }
                else
                {
                    DeferralPanel.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void UpdateDeferralChips(int count)
        {
            var subtle = (Brush)FindResource("SubtleBorder");
            var success = (Brush)FindResource("Success");
            var warning = (Brush)FindResource("Warning");
            var danger = (Brush)FindResource("Danger");

            switch (count)
            {
                case 0:
                    Chip1.Background = subtle;
                    Chip2.Background = subtle;
                    Chip3.Background = subtle;
                    DeferralText.Text = "Отложено: 0 из 3. Осталось 3";
                    break;
                case 1:
                    Chip1.Background = success;
                    Chip2.Background = subtle;
                    Chip3.Background = subtle;
                    DeferralText.Text = "Отложено: 1 из 3. Осталось 2";
                    break;
                case 2:
                    Chip1.Background = warning;
                    Chip2.Background = warning;
                    Chip3.Background = subtle;
                    DeferralText.Text = "Отложено: 2 из 3. Осталась 1 попытка!";
                    break;
                default: // 3 or more
                    Chip1.Background = danger;
                    Chip2.Background = danger;
                    Chip3.Background = danger;
                    DeferralText.Text = "Отложено: 3 из 3. На 4-й раз отложить нельзя!";
                    break;
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
            if (_isForced) return;
            Accepted = false;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_isForced) return;
            Accepted = false;
            Close();
        }

        private void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            Accepted = true;
            Close();
        }

        private async void BtnManualArchive_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "ZIP архивы (*.zip)|*.zip|Все файлы (*.*)|*.*",
                Title = "Выберите архив обновления"
            };

            if (dlg.ShowDialog(this) == true)
            {
                await UpdateService.ApplyManualUpdateAsync(dlg.FileName, this);
            }
        }

        private void LnkGitHubReleases_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/DdepRest/arc-frame/releases")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateAvailableWindow] Failed to launch releases url: {ex.Message}");
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_isForced)
                {
                    e.Handled = true;
                    return;
                }

                Accepted = false;
                Close();
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (_isForced && !Accepted)
            {
                e.Cancel = true;
            }
        }
    }
}