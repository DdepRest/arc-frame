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

        public WelcomeWindow()
        {
            InitializeComponent();

            // Pre-resolve the user's saved location so opening this dialog via
            // the «Сменить» button keeps the active option instead of always
            // resetting to option 1. Falls back to the default for first-run.
            string current = AppSettingsService.LoadContractPrefix();
            _selected = LocationOptions.IsValidPrefix(current)
                ? LocationOptions.GetByPrefixOrDefault(current)
                : LocationOptions.All[0];

            // Keep the theme toggle icon in sync with the current theme
            UpdateThemeIcon();
            ThemeService.ThemeChanged += UpdateThemeIcon;
            Closed += (_, _) => ThemeService.ThemeChanged -= UpdateThemeIcon;

            // Defer visual sync (and ListBox container generation) to Loaded —
            // touching ListBox.SelectedItem in the ctor races the visual
            // tree before its HWND / template root are wired up.
            Loaded += WelcomeWindow_Loaded;
        }

        private void WelcomeWindow_Loaded(object sender, RoutedEventArgs e)
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

        private void OptionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OptionsList.SelectedItem is LocationOption option)
            {
                _selected = option;
                UpdatePreview();
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
            AppSettingsService.MarkFirstRunComplete();

            DialogResult = true;
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            // User closed via X — discard changes (true cancel).
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
    }
}
