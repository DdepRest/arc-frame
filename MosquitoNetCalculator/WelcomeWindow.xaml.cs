using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator
{
    public partial class WelcomeWindow : Window
    {
        private LocationOption? _selected;

        // Single delegate instance reused across subscribe/unsubscribe —
        // method-group conversion creates a fresh delegate on every call,
        // so `ThemeService.ThemeChanged -= UpdateThemeIcon` (without an
        // explicit field) would silently no-op and the static event would
        // keep a strong reference to this Window after `Closed`.
        private Action _themeChangedHandler = null!;

        /// <summary>
        /// When true, the user MUST explicitly choose a location before
        /// continuing — the Continue button is disabled and closing via X
        /// shuts down the application. Used on first run only.
        /// </summary>
        public bool IsFirstRun { get; }

        /// <summary>
        /// When true, closing without selecting is forbidden.
        /// The close button is hidden, Alt+F4 and Escape are blocked.
        /// </summary>
        public bool IsMandatory { get; }

        /// <summary>
        /// Contract prefix chosen by the user (e.g. "1"–"5").
        /// Falls back to the default prefix before the user has confirmed.
        /// </summary>
        public string SelectedPrefix => _selected?.Prefix ?? LocationOptions.DefaultPrefix;

        /// <summary>
        /// Human-readable name of the chosen location.
        /// Empty until the user confirms.
        /// </summary>
        public string SelectedLocationName => _selected?.LocationName ?? string.Empty;

        public WelcomeWindow(bool isFirstRun = false, bool isMandatory = false)
        {
            IsFirstRun = isFirstRun;
            IsMandatory = isMandatory;
            InitializeComponent();

            // First-run / mandatory: no auto-selection — user must explicitly pick.
            // Settings flow: pre-select the currently saved location.
            if (!IsFirstRun && !IsMandatory)
            {
                string current = AppSettingsService.LoadContractPrefix();
                _selected = LocationOptions.IsValidPrefix(current)
                    ? LocationOptions.GetByPrefixOrDefault(current)
                    : LocationOptions.All[0];
            }

            if (IsMandatory)
            {
                if (BtnClose != null)
                    BtnClose.Visibility = Visibility.Collapsed;

                if (!IsFirstRun)
                {
                    if (TxtTitle != null)
                        TxtTitle.Text = "Выбор офиса — A.R.C. Frame";
                    if (TxtHeader != null)
                        TxtHeader.Text = "Выберите ваш офис";
                    if (TxtSubHeader != null)
                        TxtSubHeader.Text = "Пожалуйста, подтвердите текущий офис для корректной нумерации договоров и синхронизации.";
                }
            }

            // Keep the theme toggle icon in sync with the current theme.
            // Cache the delegate in _themeChangedHandler so the Closed-time
            // unsubscribe actually removes the same instance we registered.
            UpdateThemeIcon();
            _themeChangedHandler = UpdateThemeIcon;
            ThemeService.ThemeChanged += _themeChangedHandler;
            Closed += (_, _) => ThemeService.ThemeChanged -= _themeChangedHandler;

            // Defer visual sync (and ListBox container generation) to Loaded —
            // touching ListBox.SelectedItem in the ctor races the visual
            // tree before its HWND / template root are wired up.
            Loaded += WelcomeWindow_Loaded;
        }

        private void WelcomeWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_selected != null)
            {
                // Sync the ListBox to the resolved selection. We use SelectedItem
                // (reference assignment) rather than SelectedValue (whose default
                // TwoWay binding mode tries to write back into the data item's
                // Prefix property — that throws XamlParseException during
                // FrameworkTemplate.LoadTemplateXaml because LocationOption.Prefix
                // is immutable). Picked over assigning in the ctor because
                // ItemsControl container generation completes on or after Loaded.
                OptionsList.SelectedItem = _selected;
                UpdatePreview();
            }

            // First-run or mandatory guard: disable Continue until user explicitly picks
            if ((IsFirstRun || IsMandatory) && BtnContinue != null)
            {
                BtnContinue.IsEnabled = false;
            }
        }

        private void OptionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OptionsList.SelectedItem is LocationOption option)
            {
                _selected = option;
                UpdatePreview();

                // Enable Continue on explicit selection (first-run or mandatory guard)
                if ((IsFirstRun || IsMandatory) && BtnContinue != null)
                    BtnContinue.IsEnabled = true;
            }
        }

        private void UpdatePreview()
        {
            if (PreviewContractNumber != null)
                PreviewContractNumber.Text = $"{SelectedPrefix}-1";
        }

        private void BtnContinue_Click(object sender, RoutedEventArgs e)
        {
            // Persist the choice, then close. AppSettingsService swallows its
            // own IO errors (logged to Debug) — no extra try/catch needed here.
            AppSettingsService.SaveContractPrefix(SelectedPrefix);
            AppSettingsService.SaveLocationName(SelectedLocationName);
            // Явный выбор локации = подтверждение для периодической проверки
            // (LocationCheckService): иначе после первого запуска баннер
            // «всё верно?» появился бы сразу, а после смены локации —
            // по старой отметке через пару дней.
            AppSettingsService.MarkLocationConfirmed();
            AppSettingsService.MarkFirstRunComplete();
            AppSettingsService.MarkOfficePromptConfirmed();

            DialogResult = true;
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (IsMandatory)
                return;

            // First run: X closes the dialog with false result.
            // App.xaml.cs checks DialogResult and shuts down the app
            // if the user didn't pick a location — this avoids the
            // Shutdown()-inside-dialog race where App.xaml.cs would
            // continue past ShowDialog() and try to create MainWindow.
            if (IsFirstRun)
            {
                DialogResult = false;
                Close();
                return;
            }

            // Settings flow: user closed via X — discard changes (true cancel).
            // Mark first-run complete so the user isn't stuck in a loop,
            // but do NOT save the location: if this is the settings flow,
            // saving here would create a mismatch between settings.json
            // and the main window's title/prefix until next restart.
            AppSettingsService.MarkFirstRunComplete();

            DialogResult = false;
            Close();
        }

        private void BtnTheme_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.ToggleTheme();
        }

        private void UpdateThemeIcon()
        {
            if (TxtThemeIcon == null) return;
            // Sun icon (☀) when dark theme → click to switch to light
            // Moon icon (☽) when light theme → click to switch to dark
            TxtThemeIcon.Text = ThemeService.IsDarkTheme ? "\uE706" : "\uE708";
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            if (IsMandatory && DialogResult != true)
            {
                e.Cancel = true;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (IsMandatory && e.Key == Key.Escape)
            {
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }
    }
}
