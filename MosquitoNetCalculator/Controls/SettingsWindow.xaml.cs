using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    public partial class SettingsWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private bool _isInitializing = true;

        public SettingsWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            Owner = _mainWindow;
            InitializeComponent();
            UpdateBackupStatus();
            LoadCurrentSettings();
            _isInitializing = false;
        }

        private void LoadCurrentSettings()
        {
            // Installation settings
            ChkAskInstallationOnAdd.IsChecked = AppSettingsService.LoadAskInstallationModeOnAdd();
            int defMode = AppSettingsService.LoadDefaultInstallationMode();
            CmbDefaultInstallationMode.SelectedIndex = defMode switch
            {
                0 => 1, // Учтён
                2 => 2, // В конструкцию
                1 => 3, // Без монтажа
                _ => 0  // По товару (каталог)
            };

            ChkShowInstallationInTotal.IsChecked = AppSettingsService.LoadShowInstallationInTotal();

            // Theme
            ChkDarkTheme.IsChecked = ThemeService.IsDarkTheme;

            // Prefix
            TxtContractPrefix.Text = AppSettingsService.LoadContractPrefix();

            // Location
            RefreshLocationDisplay();

            // Production copy
            ChkIncludeProductionCopy.IsChecked = AppSettingsService.LoadIncludeProductionCopy();

            // Version status
            string currentVer = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "3.x";
            TxtVersionStatus.Text = $"Текущая версия: {currentVer}";
        }

        private void RefreshLocationDisplay()
        {
            string loc = AppSettingsService.LoadLocationName();
            TxtLocationNameDisplay.Text = string.IsNullOrWhiteSpace(loc) ? "Не указана" : loc;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Close();
        }

        private void ChkAskInstallationOnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppSettingsService.SaveAskInstallationModeOnAdd(ChkAskInstallationOnAdd.IsChecked == true);
        }

        private void CmbDefaultInstallationMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            int mode = CmbDefaultInstallationMode.SelectedIndex switch
            {
                1 => 0, // Учтён
                2 => 2, // В конструкцию
                3 => 1, // Без монтажа
                _ => -1 // По товару
            };
            AppSettingsService.SaveDefaultInstallationMode(mode);
        }

        private void ChkShowInstallationInTotal_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppSettingsService.SaveShowInstallationInTotal(ChkShowInstallationInTotal.IsChecked == true);
        }

        private void ChkDarkTheme_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ThemeService.ToggleTheme();
        }

        private void TxtContractPrefix_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            string prefix = TxtContractPrefix.Text.Trim();
            AppSettingsService.SaveContractPrefix(prefix);
            if (_mainWindow.Sidebar?.TxtPrefix != null)
                _mainWindow.Sidebar.TxtPrefix.Text = prefix;
        }

        private void BtnChangeLocation_Click(object sender, RoutedEventArgs e)
        {
            if (_mainWindow.OpenWelcomeWindow())
            {
                RefreshLocationDisplay();
            }
        }

        private void ChkIncludeProductionCopy_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppSettingsService.SaveIncludeProductionCopy(ChkIncludeProductionCopy.IsChecked == true);
        }

        private void BtnAiSettings_Click(object sender, RoutedEventArgs e)
        {
            if (!AppSettingsService.EnsureAdminUnlocked(this)) return;
            var dlg = new AiApiKeyDialog { Owner = this };
            dlg.ShowDialog();
        }

        private void BtnAdminPanel_Click(object sender, RoutedEventArgs e)
        {
            if (!AppSettingsService.EnsureAdminUnlocked(this)) return;
            _mainWindow.ShowAdminPanel();
            Close();
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtVersionStatus.Text = "Проверка обновлений…";
                await UpdateService.CheckAndApplyAsync(_mainWindow);
                TxtVersionStatus.Text = "Проверка завершена.";
            }
            catch (Exception ex)
            {
                TxtVersionStatus.Text = "Ошибка при проверке обновлений.";
                ToastService.ShowToast($"Не удалось проверить обновления: {ex.Message}", ToastType.Warning);
            }
        }

        private async void BtnManualUpdate_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Выберите архив с обновлением программы",
                Filter = "ZIP-архивы (*.zip)|*.zip|Все файлы (*.*)|*.*",
                CheckFileExists = true
            };

            if (ofd.ShowDialog(this) == true)
            {
                await UpdateService.ApplyManualUpdateAsync(ofd.FileName, this);
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length == 1 &&
                    files[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }

            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length == 1 &&
                    files[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    e.Handled = true;
                    await UpdateService.ApplyManualUpdateAsync(files[0], this);
                }
            }
        }
            private void BtnCreateBackup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var backupService = new OrderBackupService();
                string zipPath = backupService.CreateManualBackup();
                ToastService.ShowToast($"Резервная копия создана: {System.IO.Path.GetFileName(zipPath)}", ToastType.Success);
                UpdateBackupStatus();
            }
            catch (Exception ex)
            {
                ToastService.ShowToast($"Ошибка создания копии: {ex.Message}", ToastType.Error);
            }
        }

        private void BtnOpenBackups_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var backupService = new OrderBackupService();
                backupService.OpenBackupsFolder();
            }
            catch (Exception ex)
            {
                ToastService.ShowToast($"Не удалось открыть папку: {ex.Message}", ToastType.Error);
            }
        }

        private void UpdateBackupStatus()
        {
            if (TxtBackupInfo == null) return;
            var backupService = new OrderBackupService();
            var info = backupService.GetLatestBackupInfo();
            if (info != null)
            {
                double kb = Math.Round(info.Value.SizeBytes / 1024.0, 1);
                TxtBackupInfo.Text = $"Авто-резервирование: активно (14 дней). Последняя копия: {info.Value.Date:dd.MM.yyyy HH:mm} ({kb} КБ).";
            }
            else
            {
                TxtBackupInfo.Text = "Авто-резервирование: активно (14 дней). Резервных копий пока нет.";
            }
        }
    }
}