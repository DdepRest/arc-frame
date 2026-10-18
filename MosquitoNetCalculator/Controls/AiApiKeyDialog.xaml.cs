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
using MosquitoNetCalculator.Helpers;
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
        private string _savedFreeLlmApiKey = "";
        private bool _apiKeyMaskActive;
        private bool _nvidiaKeyMaskActive;
        private bool _freeLlmKeyMaskActive;
        private bool _isApplyingMask;

        /// <summary>Returns the saved key while the fixed visual mask is selected;
        /// otherwise returns the value the user entered (including an intentional empty value).</summary>
        private string RealPassword(PasswordBox pb)
        {
            if (ReferenceEquals(pb, TxtApiKey))
            {
                if (_apiKeyMaskActive) return _savedApiKey;
                var p = pb.Password?.Trim() ?? "";
                if (string.IsNullOrEmpty(p)) return "";
                if (p == DummyPassword || p.All(c => c == '•')) return _savedApiKey;
                return p.Replace("•", "").Trim();
            }
            if (ReferenceEquals(pb, TxtNvidiaKey))
            {
                if (_nvidiaKeyMaskActive) return _savedNvidiaApiKey;
                var p = pb.Password?.Trim() ?? "";
                if (string.IsNullOrEmpty(p)) return "";
                if (p == DummyPassword || p.All(c => c == '•')) return _savedNvidiaApiKey;
                return p.Replace("•", "").Trim();
            }
            if (ReferenceEquals(pb, TxtFreeLlmKey))
            {
                if (_freeLlmKeyMaskActive) return _savedFreeLlmApiKey;
                var p = pb.Password?.Trim() ?? "";
                if (string.IsNullOrEmpty(p)) return "";
                if (p == DummyPassword || p.All(c => c == '•')) return _savedFreeLlmApiKey;
                return p.Replace("•", "").Trim();
            }
            return pb.Password?.Replace("•", "").Trim() ?? "";
        }

        private static bool IsDummyPassword(string? value) =>
            !string.IsNullOrEmpty(value) && value == DummyPassword;

        private ObservableCollection<AiModelSelectionItem> _modelItems = new();
        private List<AiModelSelectionItem> _allModelItems = new();
        private System.Windows.Threading.DispatcherTimer? _modelRefreshTimer;
        private bool _isLoadingModels;

        private enum ModelFilterCategory { All, FreeLlm, OpenRouter, Nvidia, Vision, Selected }
        private ModelFilterCategory _selectedCategory = ModelFilterCategory.All;
        private AiProvider _activeProviderTab = AiProvider.OpenRouter;

        public AiApiKeyDialog()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            // If the dialog closes mid-test, the pulsing "…" storyboards would
            // otherwise run forever (static registry) — stop them on close.
            Closed += (_, _) => StopAllDotPulses();
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
            _savedFreeLlmApiKey = AppSettingsServiceAi.LoadAiFreeLlmApiKey() ?? "";

            bool hasOr = !string.IsNullOrWhiteSpace(_savedApiKey);
            bool hasNv = !string.IsNullOrWhiteSpace(_savedNvidiaApiKey);
            bool hasFl = !string.IsNullOrWhiteSpace(_savedFreeLlmApiKey);

            // ── Status ───────────────────────────────────────────────
            TxtCurrentKey.Text = hasOr ? "ваш ключ · скрыт" : "ключ не задан";
            TxtCurrentNvidiaKey.Text = hasNv ? "ваш ключ · скрыт" : "ключ не задан";
            TxtCurrentFreeLlmKey.Text = hasFl ? "ваш ключ · скрыт" : "локальный шлюз";

            if (TxtFreeLlmEndpoint != null)
                TxtFreeLlmEndpoint.Text = AppSettingsServiceAi.LoadAiFreeLlmApiEndpoint();
            UpdateFreeLlmEffectiveHint();

            // Only render visual mask if a key actually exists in storage.
            // Empty keys remain empty so placeholders show up and paste doesn't conflict.
            _apiKeyMaskActive = hasOr;
            _nvidiaKeyMaskActive = hasNv;
            _freeLlmKeyMaskActive = hasFl;
            _isApplyingMask = true;
            try
            {
                if (hasOr)
                    TxtApiKey.Password = DummyPassword;
                else
                    TxtApiKey.Password = "";

                if (hasNv)
                    TxtNvidiaKey.Password = DummyPassword;
                else
                    TxtNvidiaKey.Password = "";

                if (hasFl)
                    TxtFreeLlmKey.Password = DummyPassword;
                else
                    TxtFreeLlmKey.Password = "";
            }
            finally
            {
                _isApplyingMask = false;
            }

            if (ApiKeyPlaceholder != null)
                ApiKeyPlaceholder.Visibility = hasOr ? Visibility.Collapsed : Visibility.Visible;
            if (NvidiaKeyPlaceholder != null)
                NvidiaKeyPlaceholder.Visibility = hasNv ? Visibility.Collapsed : Visibility.Visible;
            if (FreeLlmKeyPlaceholder != null)
                FreeLlmKeyPlaceholder.Visibility = hasFl ? Visibility.Collapsed : Visibility.Visible;

            // Load auto-select setting
            bool autoMode = AppSettingsServiceAi.LoadAutoSelectModel();
            TglAutoSelect.IsChecked = autoMode;
            UpdateAutoSelectLabel(autoMode);

            // Load temperature
            double temp = AppSettingsServiceAi.LoadAiTemperature();
            if (SldrTemperature != null)
                SldrTemperature.Value = temp;

            UpdateTabStatusDots();

            // Smart tab selection: default to FreeLLMAPI if configured (or if custom endpoint),
            // else Nvidia if configured, else OpenRouter
            if (hasFl && !hasOr && !hasNv)
                SelectProviderTab(AiProvider.FreeLlmApi);
            else if (hasNv && !hasOr)
                SelectProviderTab(AiProvider.Nvidia);
            else
                SelectProviderTab(AiProvider.OpenRouter);

            Dispatcher.InvokeAsync(async () =>
            {
                await LoadModelsAsync(_savedApiKey, forceRefresh: false);
                StartModelRefreshTimer();
            });
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
                    nvidiaApiKey: RealPassword(TxtNvidiaKey),
                    freeLlmApiKey: RealPassword(TxtFreeLlmKey),
                    freeLlmApiEndpoint: TxtFreeLlmEndpoint?.Text?.Trim())).ToList();

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

                // On an explicit refresh, also probe which free models actually
                // answer right now (auto-analysis). The catalog stays complete; the
                // status line just reports how many models passed the live check.
                if (forceRefresh)
                {
                    try
                    {
                        TxtModelStatus.Text += " • проверка доступности…";
                        var availability = await AiAssistantService.AnalyzeAvailableModelsAsync(
                            models,
                            openRouterApiKey: apiKey,
                            nvidiaApiKey: RealPassword(TxtNvidiaKey),
                            freeLlmApiKey: RealPassword(TxtFreeLlmKey));
                        int ok = availability.Count(a => a.IsAvailable);
                        TxtModelStatus.Text = TxtModelStatus.Text.Replace(
                            " • проверка доступности…",
                            $" • доступно {ok}/{availability.Count}");

                        // Surface an exhausted/invalid key explicitly instead of a
                        // generic "models unavailable" message.
                        var authBlocked = availability
                            .Where(a => !a.IsAvailable && a.StatusCode is 401 or 403)
                            .GroupBy(a => a.Provider)
                            .Select(g =>
                            {
                                var providerName = g.Key switch
                                {
                                    AiProvider.Nvidia => "NVIDIA",
                                    AiProvider.FreeLlmApi => "FreeLLMAPI",
                                    _ => "OpenRouter"
                                };
                                var detail = g.Select(a => a.Detail)
                                    .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)) ?? "";
                                return $"{providerName}: {detail}";
                            })
                            .ToList();
                        if (authBlocked.Count > 0)
                            TxtModelStatus.Text += " • ⚠ " + string.Join("; ", authBlocked);
                    }
                    catch (Exception ex)
                    {
                        TxtModelStatus.Text += $" • проверка не удалась: {ex.Message}";
                    }
                }
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
            var filter = TxtSearch?.Text?.Trim() ?? "";
            if (BtnClearSearch != null)
                BtnClearSearch.Visibility = string.IsNullOrEmpty(filter)
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            IEnumerable<AiModelSelectionItem> query = _allModelItems;

            switch (_selectedCategory)
            {
                case ModelFilterCategory.FreeLlm:
                    query = query.Where(i => i.Model.Provider == AiProvider.FreeLlmApi);
                    break;
                case ModelFilterCategory.OpenRouter:
                    query = query.Where(i => i.Model.Provider == AiProvider.OpenRouter);
                    break;
                case ModelFilterCategory.Nvidia:
                    query = query.Where(i => i.Model.Provider == AiProvider.Nvidia);
                    break;
                case ModelFilterCategory.Vision:
                    query = query.Where(i => i.IsVisionCapable);
                    break;
                case ModelFilterCategory.Selected:
                    query = query.Where(i => i.IsSelected);
                    break;
            }

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(i =>
                    i.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    i.Model.Id.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            _modelItems = new ObservableCollection<AiModelSelectionItem>(query);
            if (LstModels != null)
                LstModels.ItemsSource = _modelItems;
        }

        private void UpdateModelStatus()
        {
            int selected = _allModelItems.Count(i => i.IsSelected);
            int total = _allModelItems.Count;
            int flCount = _allModelItems.Count(i => i.Model.Provider == AiProvider.FreeLlmApi);
            int orCount = _allModelItems.Count(i => i.Model.Provider == AiProvider.OpenRouter);
            int nvCount = _allModelItems.Count(i => i.Model.Provider == AiProvider.Nvidia);
            string providerBreakdown = $" (FL {flCount}, OR {orCount}, NV {nvCount})";
            if (TxtModelStatus != null)
                TxtModelStatus.Text = $"Загружено {total} • Выбрано {selected}{providerBreakdown}";

            UpdateChipsCount();
        }

        private void UpdateChipsCount()
        {
            if (TxtChipAll == null) return;
            TxtChipAll.Text = $"Все ({_allModelItems.Count})";
            if (TxtChipFreeLlm != null)
                TxtChipFreeLlm.Text = $"FreeLLM ({_allModelItems.Count(i => i.Model.Provider == AiProvider.FreeLlmApi)})";
            if (TxtChipOpenRouter != null)
                TxtChipOpenRouter.Text = $"OpenRouter ({_allModelItems.Count(i => i.Model.Provider == AiProvider.OpenRouter)})";
            if (TxtChipNvidia != null)
                TxtChipNvidia.Text = $"NVIDIA ({_allModelItems.Count(i => i.Model.Provider == AiProvider.Nvidia)})";
            if (TxtChipVision != null)
                TxtChipVision.Text = $"📷 Vision ({_allModelItems.Count(i => i.IsVisionCapable)})";
            if (TxtChipSelected != null)
                TxtChipSelected.Text = $"✓ Выбрано ({_allModelItems.Count(i => i.IsSelected)})";
        }

        private void ChipFilter_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string tag)
            {
                _selectedCategory = tag switch
                {
                    "FreeLlm" => ModelFilterCategory.FreeLlm,
                    "OpenRouter" => ModelFilterCategory.OpenRouter,
                    "Nvidia" => ModelFilterCategory.Nvidia,
                    "Vision" => ModelFilterCategory.Vision,
                    "Selected" => ModelFilterCategory.Selected,
                    _ => ModelFilterCategory.All
                };
                UpdateChipsVisual();
                ApplyFilter();
                e.Handled = true;
            }
        }

        private void UpdateChipsVisual()
        {
            SetChipActive(ChipAll, TxtChipAll, _selectedCategory == ModelFilterCategory.All);
            SetChipActive(ChipFreeLlm, TxtChipFreeLlm, _selectedCategory == ModelFilterCategory.FreeLlm);
            SetChipActive(ChipOpenRouter, TxtChipOpenRouter, _selectedCategory == ModelFilterCategory.OpenRouter);
            SetChipActive(ChipNvidia, TxtChipNvidia, _selectedCategory == ModelFilterCategory.Nvidia);
            SetChipActive(ChipVision, TxtChipVision, _selectedCategory == ModelFilterCategory.Vision);
            SetChipActive(ChipSelected, TxtChipSelected, _selectedCategory == ModelFilterCategory.Selected);
        }

        private static void SetChipActive(Border? chip, TextBlock? text, bool isActive)
        {
            if (chip == null || text == null) return;
            if (isActive)
            {
                chip.Background = (Brush)Application.Current.FindResource("Accent");
                text.Foreground = (Brush)Application.Current.FindResource("OnAccent");
                text.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                chip.Background = Brushes.Transparent;
                text.Foreground = (Brush)Application.Current.FindResource("TextPrimary");
                text.FontWeight = FontWeights.Normal;
            }
        }

        private void SelectProviderTab(AiProvider provider)
        {
            _activeProviderTab = provider;

            if (PanelOpenRouter != null)
                PanelOpenRouter.Visibility = provider == AiProvider.OpenRouter ? Visibility.Visible : Visibility.Collapsed;
            if (PanelNvidia != null)
                PanelNvidia.Visibility = provider == AiProvider.Nvidia ? Visibility.Visible : Visibility.Collapsed;
            if (PanelFreeLlm != null)
                PanelFreeLlm.Visibility = provider == AiProvider.FreeLlmApi ? Visibility.Visible : Visibility.Collapsed;

            SetTabActive(TabBtnOpenRouter, provider == AiProvider.OpenRouter);
            SetTabActive(TabBtnNvidia, provider == AiProvider.Nvidia);
            SetTabActive(TabBtnFreeLlm, provider == AiProvider.FreeLlmApi);

            if (provider == AiProvider.FreeLlmApi)
                TxtFreeLlmEndpoint?.Focus();
            else if (provider == AiProvider.Nvidia)
                TxtNvidiaKey?.Focus();
            else
                TxtApiKey?.Focus();
        }

        private static void SetTabActive(Border? tab, bool isActive)
        {
            if (tab == null) return;
            if (isActive)
            {
                tab.Background = (Brush)Application.Current.FindResource("Surface");
                tab.BorderBrush = (Brush)Application.Current.FindResource("Accent");
                tab.BorderThickness = new Thickness(1.5);
            }
            else
            {
                tab.Background = (Brush)Application.Current.FindResource("SectionBg");
                tab.BorderBrush = (Brush)Application.Current.FindResource("Border");
                tab.BorderThickness = new Thickness(1);
            }
        }

        private void TabBtnOpenRouter_Click(object sender, MouseButtonEventArgs e)
        {
            SelectProviderTab(AiProvider.OpenRouter);
            e.Handled = true;
        }

        private void TabBtnNvidia_Click(object sender, MouseButtonEventArgs e)
        {
            SelectProviderTab(AiProvider.Nvidia);
            e.Handled = true;
        }

        private void TabBtnFreeLlm_Click(object sender, MouseButtonEventArgs e)
        {
            SelectProviderTab(AiProvider.FreeLlmApi);
            e.Handled = true;
        }

        private void UpdateTabStatusDots()
        {
            bool hasOr = !string.IsNullOrWhiteSpace(RealPassword(TxtApiKey));
            bool hasNv = !string.IsNullOrWhiteSpace(RealPassword(TxtNvidiaKey));
            bool hasFl = !string.IsNullOrWhiteSpace(RealPassword(TxtFreeLlmKey)) ||
                         !string.IsNullOrWhiteSpace(TxtFreeLlmEndpoint?.Text);

            if (TabDotOr != null)
                TabDotOr.Background = (Brush)Application.Current.FindResource(hasOr ? "Success" : "TextMuted");
            if (TabDotNv != null)
                TabDotNv.Background = (Brush)Application.Current.FindResource(hasNv ? "Success" : "TextMuted");
            if (TabDotFl != null)
                TabDotFl.Background = (Brush)Application.Current.FindResource(hasFl ? "Success" : "TextMuted");
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
        private void PasswordBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox pb)
            {
                bool isMaskActive = ReferenceEquals(pb, TxtApiKey) ? _apiKeyMaskActive : _nvidiaKeyMaskActive;
                if (isMaskActive || IsDummyPassword(pb.Password))
                    pb.SelectAll();
            }
        }

        private void PasswordBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is PasswordBox pb)
            {
                bool isMaskActive = ReferenceEquals(pb, TxtApiKey) ? _apiKeyMaskActive : _nvidiaKeyMaskActive;
                if ((isMaskActive || IsDummyPassword(pb.Password)) && !pb.IsKeyboardFocusWithin)
                {
                    pb.Focus();
                    pb.SelectAll();
                    e.Handled = true;
                }
            }
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

        private void TxtFreeLlmKey_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_isApplyingMask)
                _freeLlmKeyMaskActive = false;
            if (FreeLlmKeyPlaceholder != null)
                FreeLlmKeyPlaceholder.Visibility = string.IsNullOrEmpty(TxtFreeLlmKey.Password)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private async void BtnTestKeys_Click(object sender, RoutedEventArgs e)
        {
            // Resolve all keys: prefer the value the user just typed in the TextBox
            // (so they can see test feedback on a freshly-pasted key without saving first).
            var orKey = RealPassword(TxtApiKey) ?? "";
            var nvKey = RealPassword(TxtNvidiaKey) ?? "";
            var flKey = RealPassword(TxtFreeLlmKey) ?? "";
            var flEp = TxtFreeLlmEndpoint?.Text?.Trim() ?? "";

            BtnTestKeys.IsEnabled = false;
            SetTestStatusPending(OrTestDot, TxtOrTestStatus);
            SetTestStatusPending(NvTestDot, TxtNvTestStatus);
            SetTestStatusPending(FlTestDot, TxtFlTestStatus);

            try
            {
                var orTask = string.IsNullOrWhiteSpace(orKey)
                    ? Task.FromResult(new AiApiKeyTestResult(false, 0, 0, "ключ не задан"))
                    : AiAssistantService.TestApiKeyAsync(AiProvider.OpenRouter, orKey);
                var nvTask = string.IsNullOrWhiteSpace(nvKey)
                    ? Task.FromResult(new AiApiKeyTestResult(false, 0, 0, "ключ не задан"))
                    : AiAssistantService.TestApiKeyAsync(AiProvider.Nvidia, nvKey);

                if (!string.IsNullOrWhiteSpace(flEp))
                {
                    // The field is a BASE url: normalize it so the saved value
                    // carries /v1 (the gateway answers any other path with its
                    // dashboard page and HTTP 200 — a false "healthy" signal).
                    flEp = AiKeyValidator.NormalizeFreeLlmApiBase(flEp);
                    TxtFreeLlmEndpoint!.Text = flEp;
                    AppSettingsServiceAi.SaveAiFreeLlmApiEndpoint(flEp);
                }
                var flTask = AiAssistantService.TestApiKeyAsync(AiProvider.FreeLlmApi, flKey);
                await Task.WhenAll(orTask, nvTask, flTask);

                ApplyTestResult(OrTestDot, TxtOrTestStatus, orTask.Result, AiProvider.OpenRouter);
                ApplyTestResult(NvTestDot, TxtNvTestStatus, nvTask.Result, AiProvider.Nvidia);
                ApplyTestResult(FlTestDot, TxtFlTestStatus, flTask.Result, AiProvider.FreeLlmApi);
            }
            catch (Exception ex)
            {
                // TestApiKeyAsync swallows transport errors and returns a failure
                // result, but guard anyway so the UI never dies.
                ApplyTestResult(OrTestDot, TxtOrTestStatus,
                    new AiApiKeyTestResult(false, 0, 0, $"исключение: {ex.GetType().Name}"), AiProvider.OpenRouter);
                ApplyTestResult(NvTestDot, TxtNvTestStatus,
                    new AiApiKeyTestResult(false, 0, 0, $"исключение: {ex.GetType().Name}"), AiProvider.Nvidia);
                ApplyTestResult(FlTestDot, TxtFlTestStatus,
                    new AiApiKeyTestResult(false, 0, 0, $"исключение: {ex.GetType().Name}"), AiProvider.FreeLlmApi);
            }
            finally
            {
                BtnTestKeys.IsEnabled = true;
                UpdateTabStatusDots();
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
            if (result.Detail == "ключ не задан")
            {
                dot.Background = (Brush)Application.Current.FindResource("TextMuted");
                label.Foreground = (Brush)Application.Current.FindResource("TextMuted");
                label.Text = "ключ не задан";
            }
            else if (result.IsOk)
            {
                dot.Background = (Brush)Application.Current.FindResource("Success");
                label.Foreground = (Brush)Application.Current.FindResource("Success");
                label.Text = provider switch
                {
                    AiProvider.Nvidia => $"✓ сервис доступен · {result.LatencyMs} мс",
                    // Detail carries the model count and the model that answered:
                    // «✓ шлюз доступен · 312 моделей · модель «auto» ответила · 145 мс».
                    // A bare "шлюз доступен" was exactly the misleading part — the
                    // old probe treated any HTTP 200 (including the gateway's
                    // dashboard page) as a healthy gateway.
                    AiProvider.FreeLlmApi => $"✓ {result.Detail} · {result.LatencyMs} мс",
                    _ => $"✓ ключ подтверждён · {result.LatencyMs} мс"
                };
            }
            else
            {
                dot.Background = (Brush)Application.Current.FindResource("Danger");
                label.Foreground = (Brush)Application.Current.FindResource("Danger");
                string suffix = result.LatencyMs > 0 ? $" · {result.LatencyMs} мс" : "";
                label.Text = $"✕ {result.Detail}{suffix}";
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
                // v3.53: 600 мс — вдвое длиннее самого длинного шага шкалы
                // (правило «ни одна анимация не длится дольше 320 мс»).
                Duration = Motion.Emphasized,
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(anim, dot);
            Storyboard.SetTargetProperty(anim, new PropertyPath("Opacity"));
            if (Motion.ReducedMotion)
            {
                // Анимации выключены: пульс не запускаем ВООБЩЕ (сжатие
                // бесконечной анимации дало бы строб вместо спокойной точки),
                // точка остаётся видимой на полной непрозрачности.
                dot.Opacity = 1.0;
                return;
            }

            var sb = new Storyboard();
            sb.Children.Add(anim);
            Motion.Run(sb);
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

        private static void StopAllDotPulses()
        {
            // StopDotPulse mutates the dictionary — iterate over a snapshot.
            foreach (var dot in _dotPulses.Keys.ToArray())
                StopDotPulse(dot);
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
            bool anyFilteredSelected = _modelItems.Any(m => m.IsSelected);
            var target = (_modelItems.Count < _allModelItems.Count && anyFilteredSelected)
                ? (IEnumerable<AiModelSelectionItem>)_modelItems
                : _allModelItems;
            foreach (var item in target)
                item.IsSelected = false;
            UpdateModelStatus();
        }

        private void Row_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindVisualParent<CheckBox>(e.OriginalSource as DependencyObject) != null)
                return;

            if (sender is FrameworkElement { Tag: AiModelSelectionItem item })
            {
                item.IsSelected = !item.IsSelected;
                UpdateModelStatus();
            }
        }

        private void SldrTemperature_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtTemperatureValue != null && TxtStyleDescription != null)
            {
                double val = Math.Round(e.NewValue, 1);
                TxtTemperatureValue.Text = val.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                TxtStyleDescription.Text = val switch
                {
                    <= 0.1 => "Строгий инженерный (0.1) — только точные размеры",
                    <= 0.3 => "Сбалансированный (0.2-0.3) — размеры + пояснения",
                    _ => "Консультант (0.4+) — развернутые советы по замерам"
                };
            }
        }

        private async void BtnPingModels_Click(object sender, RoutedEventArgs e)
        {
            BtnPingModels.IsEnabled = false;
            try
            {
                var targets = _modelItems.Where(m => m.IsSelected).Take(5).ToList();
                if (targets.Count == 0) targets = _modelItems.Take(4).ToList();

                foreach (var item in targets) item.IsTesting = true;

                var tasks = targets.Select(async item =>
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        var probe = await AiAssistantService.ProbeModelAsync(item.Model.Id);
                        sw.Stop();
                        item.LatencyMs = (int)sw.ElapsedMilliseconds;
                        item.IsAvailable = probe.IsAvailable;
                    }
                    catch
                    {
                        item.IsAvailable = false;
                        item.LatencyMs = -1;
                    }
                    finally
                    {
                        item.IsTesting = false;
                    }
                });

                await Task.WhenAll(tasks);
            }
            finally
            {
                BtnPingModels.IsEnabled = true;
            }
        }

        private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild) return typedChild;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
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
            AppSettingsServiceAi.SaveAiFreeLlmApiKey(RealPassword(TxtFreeLlmKey) ?? "");

            var endpoint = AiKeyValidator.NormalizeFreeLlmApiBase(TxtFreeLlmEndpoint?.Text);
            if (TxtFreeLlmEndpoint != null)
                TxtFreeLlmEndpoint.Text = endpoint;
            AppSettingsServiceAi.SaveAiFreeLlmApiEndpoint(endpoint);
            UpdateFreeLlmEffectiveHint();

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
            if (SldrTemperature != null)
                AppSettingsServiceAi.SaveAiTemperature(SldrTemperature.Value);

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

        // UX-14: the close button now uses the shared DialogCloseButton style —
        // hover (red overlay + white glyph) is handled entirely by its template,
        // so the old MouseEnter/MouseLeave handlers are gone.

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.OriginalSource is DependencyObject src && FindVisualParent<Button>(src) != null)
                    return;
                try { DragMove(); } catch { /* ignore */ }
            }
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

        private void LinkNvidia_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://build.nvidia.com",
                    UseShellExecute = true
                });
            }
            catch { /* ignore */ }
        }

        private void TxtFreeLlmEndpoint_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateFreeLlmEffectiveHint();
        }

        /// <summary>
        /// Shows the URL the AI requests will actually use. The endpoint field
        /// holds a BASE address; the "…/v1/chat/completions" suffix is added
        /// automatically, so a missing /v1 is visible here instead of surfacing
        /// as a mysterious "модели вернули пустой ответ".
        /// </summary>
        private void UpdateFreeLlmEffectiveHint()
        {
            if (TxtFreeLlmEffective == null) return;

            var raw = TxtFreeLlmEndpoint?.Text;
            TxtFreeLlmEffective.Text = string.IsNullOrWhiteSpace(raw)
                ? $"запросы пойдут на: {AiKeyValidator.BuildFreeLlmApiChatUrl(null)}"
                : $"запросы пойдут на: {AiKeyValidator.BuildFreeLlmApiChatUrl(raw)}";
        }

        private void TglAutoSelect_Changed(object sender, RoutedEventArgs e)
        {
            UpdateAutoSelectLabel(TglAutoSelect.IsChecked == true);
        }

        private void UpdateAutoSelectLabel(bool enabled)
        {
            TxtAutoLabel.Text = enabled ? "Автовыбор" : "Вручную";
            TxtModelDescription.Text = enabled
                ? "Автовыбор подбирает лучшую модель под задачу (расчёт, эскиз, чат); отмеченные — приоритетный пул"
                : "Используются только отмеченные модели строго по очереди";
        }
    }
}
