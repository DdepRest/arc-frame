using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    public partial class AiApiKeyDialog : Window
    {
        public string ApiKey { get; private set; } = "";
        public IReadOnlyList<string> SelectedModels { get; private set; } = Array.Empty<string>();

        /// <summary>Fixed visual filler shown in both PasswordBox controls.
        /// It is never used as an API credential; active-mask flags resolve the real value.</summary>
        private const string DummyPassword = "••••••••••••••••";

        /// <summary>
        /// Keep the real saved keys out of the visual controls. This prevents the
        /// PasswordBox from revealing key length and keeps both providers identical.
        /// </summary>
        private string _savedApiKey = "";
        private string _savedNvidiaApiKey = "";
        private bool _apiKeyMaskActive;
        private bool _nvidiaKeyMaskActive;
        private bool _isApplyingMask;

        /// <summary>Returns the saved key while the fixed visual mask is selected;
        /// otherwise returns the value the user entered (including an intentional empty value).</summary>
        private string RealPassword(PasswordBox pb)
        {
            var p = pb.Password?.Trim();
            if (ReferenceEquals(pb, TxtApiKey) && _apiKeyMaskActive)
                return _savedApiKey;
            if (ReferenceEquals(pb, TxtNvidiaKey) && _nvidiaKeyMaskActive)
                return _savedNvidiaApiKey;
            return p ?? "";
        }

        private static bool IsDummyPassword(string? value) =>
            !string.IsNullOrEmpty(value) && value == DummyPassword;

        private ObservableCollection<AiModelSelectionItem> _modelItems = new();
        private List<AiModelSelectionItem> _allModelItems = new();
        private System.Windows.Threading.DispatcherTimer? _modelRefreshTimer;
        private bool _isLoadingModels;

        public AiApiKeyDialog()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            TxtApiKey.GotFocus += PasswordBox_GotFocus;
            TxtNvidiaKey.GotFocus += PasswordBox_GotFocus;
            // Close on Escape at any time
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    DialogResult = false;
                    Close();
                }
            };
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _savedApiKey = AppSettingsServiceAi.LoadAiApiKey() ?? "";
            _savedNvidiaApiKey = AppSettingsServiceAi.LoadAiNvidiaApiKey() ?? "";

            // ── Status ───────────────────────────────────────────────
            // Keep these summaries short enough for the narrow provider column;
            // the PasswordBox itself already communicates that the value is masked.
            TxtCurrentKey.Text = string.IsNullOrEmpty(_savedApiKey)
                ? "встроенный ключ"
                : "ваш ключ · скрыт";
            TxtCurrentNvidiaKey.Text = string.IsNullOrEmpty(_savedNvidiaApiKey)
                ? "встроенный ключ"
                : "ваш ключ · скрыт";

            // Always render the same fixed mask. The actual keys stay only in the
            // private fields above and are never copied into a visual PasswordBox.
            _apiKeyMaskActive = true;
            _nvidiaKeyMaskActive = true;
            _isApplyingMask = true;
            try
            {
                TxtApiKey.Password = DummyPassword;
                TxtNvidiaKey.Password = DummyPassword;
            }
            finally
            {
                _isApplyingMask = false;
            }

            // Load auto-select setting
            bool autoMode = AppSettingsServiceAi.LoadAutoSelectModel();
            TglAutoSelect.IsChecked = autoMode;
            UpdateAutoSelectLabel(autoMode);

            Dispatcher.InvokeAsync(async () =>
            {
                await LoadModelsAsync(_savedApiKey, forceRefresh: false);
                StartModelRefreshTimer();
            });

            // Select the fixed mask so the first typed or pasted character replaces
            // it. Leaving the field untouched preserves the saved key on Save.
            TxtApiKey.Focus();
            TxtApiKey.SelectAll();
        }

        private async Task LoadModelsAsync(string apiKey, bool forceRefresh = false)
        {
            if (_isLoadingModels) return;
            _isLoadingModels = true;
            TxtModelStatus.Text = forceRefresh ? "Обновление списка…" : "Загрузка списка…";
            try
            {
                var models = (await AiAssistantService.FetchAvailableModelsAsync(
                    apiKey,
                    forceRefresh,
                    nvidiaApiKey: RealPassword(TxtNvidiaKey))).ToList();

                var previouslySelected = _allModelItems
                    .Where(i => i.IsSelected)
                    .Select(i => i.Model.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var savedModels = AppSettingsServiceAi.LoadAiFallbackModels();
                foreach (var saved in savedModels)
                    previouslySelected.Add(saved);

                _allModelItems = models
                    .Select(m => new AiModelSelectionItem(m, previouslySelected.Contains(m.Id)))
                    .ToList();

                if (_allModelItems.Count > 0 && !_allModelItems.Any(i => i.IsSelected))
                    _allModelItems[0].IsSelected = true;

                ApplyFilter();
                UpdateModelStatus();
            }
            catch (Exception ex)
            {
                TxtModelStatus.Text = $"Ошибка загрузки: {ex.Message}";
            }
            finally
            {
                _isLoadingModels = false;
            }
        }

        private void StartModelRefreshTimer()
        {
            if (_modelRefreshTimer != null) return;
            _modelRefreshTimer = new System.Windows.Threading.DispatcherTimer
            {
                // Keep an open dialog in sync without creating a request storm.
                Interval = TimeSpan.FromMinutes(30)
            };
            _modelRefreshTimer.Tick += async (_, _) =>
            {
                await LoadModelsAsync(
                    RealPassword(TxtApiKey) ?? "",
                    forceRefresh: true);
            };
            _modelRefreshTimer.Start();
            Closed += (_, _) =>
            {
                _modelRefreshTimer?.Stop();
                _modelRefreshTimer = null;
            };
        }

        private void ApplyFilter()
        {
            var filter = TxtSearch.Text?.Trim() ?? "";
            BtnClearSearch.Visibility = string.IsNullOrEmpty(filter)
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (string.IsNullOrEmpty(filter))
            {
                _modelItems = new ObservableCollection<AiModelSelectionItem>(_allModelItems);
            }
            else
            {
                _modelItems = new ObservableCollection<AiModelSelectionItem>(
                    _allModelItems.Where(i =>
                        i.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        i.Model.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)));
            }
            LstModels.ItemsSource = _modelItems;
        }

        private void UpdateModelStatus()
        {
            int selected = _allModelItems.Count(i => i.IsSelected);
            int total = _allModelItems.Count;
            int orCount = _allModelItems.Count(i => i.Model.Provider == AiProvider.OpenRouter);
            int nvCount = total - orCount;
            string providerBreakdown = nvCount > 0 ? $" (OR {orCount}, NV {nvCount})" : "";
            TxtModelStatus.Text = $"Загружено {total} • Выбрано {selected}{providerBreakdown}";
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            // Enter in search → focus model list so user can navigate with arrows
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                LstModels.Focus();
            }
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtSearch.Text = "";
            TxtSearch.Focus();
        }

        /// <summary>
        /// Select the fixed mask without clearing it. If the user types, pastes,
        /// Backspaces, or presses Delete, WPF replaces the selection; if they only
        /// focus and leave the field untouched, the saved key remains intact.
        /// </summary>
        private static void PasswordBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox pb && IsDummyPassword(pb.Password))
                pb.SelectAll();
        }

        private void TxtApiKey_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_isApplyingMask)
                _apiKeyMaskActive = false;
            if (ApiKeyPlaceholder != null)
                ApiKeyPlaceholder.Visibility = string.IsNullOrEmpty(TxtApiKey.Password)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void TxtNvidiaKey_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_isApplyingMask)
                _nvidiaKeyMaskActive = false;
            if (NvidiaKeyPlaceholder != null)
                NvidiaKeyPlaceholder.Visibility = string.IsNullOrEmpty(TxtNvidiaKey.Password)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private async void BtnTestKeys_Click(object sender, RoutedEventArgs e)
        {
            // Resolve both keys: prefer the value the user just typed in the TextBox
            // (so they can see test feedback on a freshly-pasted key without saving first).
            var orKey = RealPassword(TxtApiKey) ?? "";
            var nvKey = RealPassword(TxtNvidiaKey) ?? "";

            BtnTestKeys.IsEnabled = false;
            SetTestStatusPending(OrTestDot, TxtOrTestStatus);
            SetTestStatusPending(NvTestDot, TxtNvTestStatus);

            try
            {
                var orFallback = string.IsNullOrWhiteSpace(orKey)
                    ? AiAssistantService.EmbeddedOpenRouterApiKey
                    : orKey;
                var nvFallback = string.IsNullOrWhiteSpace(nvKey)
                    ? AiAssistantService.EmbeddedNvidiaApiKey
                    : nvKey;

                var orTask = AiAssistantService.TestApiKeyAsync(AiProvider.OpenRouter, orFallback);
                var nvTask = AiAssistantService.TestApiKeyAsync(AiProvider.Nvidia, nvFallback);
                await Task.WhenAll(orTask, nvTask);

                ApplyTestResult(OrTestDot, TxtOrTestStatus, orTask.Result, AiProvider.OpenRouter);
                ApplyTestResult(NvTestDot, TxtNvTestStatus, nvTask.Result, AiProvider.Nvidia);
            }
            catch (Exception ex)
            {
                // TestApiKeyAsync swallows transport errors and returns a failure
                // result, but guard anyway so the UI never dies.
                ApplyTestResult(OrTestDot, TxtOrTestStatus,
                    new AiApiKeyTestResult(false, 0, 0, $"исключение: {ex.GetType().Name}"), AiProvider.OpenRouter);
                ApplyTestResult(NvTestDot, TxtNvTestStatus,
                    new AiApiKeyTestResult(false, 0, 0, $"исключение: {ex.GetType().Name}"), AiProvider.Nvidia);
            }
            finally
            {
                BtnTestKeys.IsEnabled = true;
            }
        }

        private static void SetTestStatusPending(Border dot, TextBlock label)
        {
            label.Text = "проверка…";
            label.Foreground = (Brush)Application.Current.FindResource("TextMuted");
            dot.Background = (Brush)Application.Current.FindResource("TextMuted");
            StartDotPulse(dot);
        }

        private static void ApplyTestResult(
            Border dot, TextBlock label, AiApiKeyTestResult result, AiProvider provider)
        {
            StopDotPulse(dot);
            if (result.IsOk)
            {
                dot.Background = (Brush)Application.Current.FindResource("Success");
                label.Foreground = (Brush)Application.Current.FindResource("Success");
                label.Text = provider == AiProvider.Nvidia
                    ? $"✓ сервис доступен · {result.LatencyMs} мс"
                    : $"✓ ключ подтверждён · {result.LatencyMs} мс";
            }
            else
            {
                dot.Background = (Brush)Application.Current.FindResource("Danger");
                label.Foreground = (Brush)Application.Current.FindResource("Danger");
                string suffix = result.LatencyMs > 0 ? $" · {result.LatencyMs} мс" : "";
                label.Text = provider == AiProvider.Nvidia
                    ? $"✕ сервис недоступен: {result.Detail}{suffix}"
                    : $"✕ {result.Detail}{suffix}";
            }
        }

        // Subtle pulsing animation while a key is being tested ("…" feedback).
        private static readonly Dictionary<Border, Storyboard> _dotPulses = new();

        private static void StartDotPulse(Border dot)
        {
            StopDotPulse(dot);
            var anim = new DoubleAnimation
            {
                From = 1.0,
                To = 0.35,
                Duration = TimeSpan.FromMilliseconds(600),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(anim, dot);
            Storyboard.SetTargetProperty(anim, new PropertyPath("Opacity"));
            var sb = new Storyboard();
            sb.Children.Add(anim);
            sb.Begin();
            _dotPulses[dot] = sb;
        }

        private static void StopDotPulse(Border dot)
        {
            if (_dotPulses.TryGetValue(dot, out var sb))
            {
                sb.Stop();
                _dotPulses.Remove(dot);
            }
            dot.Opacity = 1.0;
        }

        private async void BtnRefreshModels_Click(object sender, RoutedEventArgs e)
        {
            await LoadModelsAsync(
                RealPassword(TxtApiKey) ?? "", forceRefresh: true);
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _modelItems)
                item.IsSelected = true;
            UpdateModelStatus();
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _allModelItems)
                item.IsSelected = false;
            UpdateModelStatus();
        }

        private void Row_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // The checkbox is the direct selection control. Do not toggle a second
            // time when its mouse event bubbles through the clickable row.
            if (FindVisualParent<CheckBox>(e.OriginalSource as DependencyObject) != null)
                return;

            if (sender is Border { Tag: AiModelSelectionItem item })
            {
                item.IsSelected = !item.IsSelected;
                UpdateModelStatus();
                e.Handled = true;
            }
        }

        private void RowCheck_Click(object sender, RoutedEventArgs e)
        {
            // CheckBox already toggled IsSelected through TwoWay binding.
            UpdateModelStatus();
            e.Handled = true;
        }

        private static T? FindVisualParent<T>(DependencyObject? child)
            where T : DependencyObject
        {
            var current = child;
            while (current != null)
            {
                if (current is T match)
                    return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            ApiKey = RealPassword(TxtApiKey) ?? "";
            AppSettingsServiceAi.SaveAiNvidiaApiKey(RealPassword(TxtNvidiaKey) ?? "");

            var selected = _allModelItems
                .Where(i => i.IsSelected)
                .Select(i => i.Model.Id)
                .Distinct()
                .ToList();

            if (selected.Count == 0 && _allModelItems.Count > 0)
                selected.Add(_allModelItems[0].Model.Id);

            SelectedModels = selected;

            AppSettingsServiceAi.SaveAiApiKey(ApiKey);
            AppSettingsServiceAi.SaveAiFallbackModels(selected);
            AppSettingsServiceAi.SaveAutoSelectModel(TglAutoSelect.IsChecked == true);

            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CloseBtn_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Button b)
            {
                b.Background = (System.Windows.Media.Brush)FindResource("RowHover");
                b.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
            }
        }

        private void CloseBtn_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Button b)
            {
                b.Background = System.Windows.Media.Brushes.Transparent;
                b.Foreground = (System.Windows.Media.Brush)FindResource("TextMuted");
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Allow dragging the borderless window by clicking anywhere except on interactive controls.
            if (e.OriginalSource is DependencyObject src)
            {
                DependencyObject? current = src;
                while (current != null && current != this)
                {
                    if (current is TextBox || current is PasswordBox || current is Button || current is CheckBox || current is ListBoxItem)
                        return;
                    current = System.Windows.Media.VisualTreeHelper.GetParent(current);
                }
            }
            try { DragMove(); } catch { /* ignore */ }
        }

        private void Link_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://openrouter.ai/keys",
                    UseShellExecute = true
                });
            }
            catch { /* ignore */ }
        }

        private void TglAutoSelect_Changed(object sender, RoutedEventArgs e)
        {
            UpdateAutoSelectLabel(TglAutoSelect.IsChecked == true);
        }

        private void UpdateAutoSelectLabel(bool enabled)
        {
            TxtAutoLabel.Text = enabled ? "Автовыбор" : "Вручную";
            TxtModelDescription.Text = enabled
                ? "Автовыбор подбирает модель под задачу; отмеченные — приоритетный резерв"
                : "Используются только отмеченные модели в заданном порядке";
        }
    }
}
