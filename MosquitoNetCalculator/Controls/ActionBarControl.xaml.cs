using System;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;

namespace MosquitoNetCalculator.Controls
{
    public partial class ActionBarControl : UserControl
    {
        public Border CardBarBorder => CardActionBar;
        public Run OrderInfoRun => RunCurrentOrderInfo;
        public Border DirtyChip => DirtyIndicator;
        // Settings (gear) button + dropdown — replaces the legacy BtnThemeToggle
        // button. Theme state surfaces through two IsCheckable radio menu items
        // updated from ThemeChanged; location pipeline lives in the same menu.
        // Public accessors for callers that want to interact with the settings
        // gear button + dropdown. Note: `SettingsMenu` is intentionally NOT
        // re-exposed as a public property because the XAML's `x:Name`
        // already promotes it to an internal field on the partial class —
        // a public property of the same name would collide (CS0102) and the
        // internal field is sufficient for handlers in this class anyway.
        public Button SettingsBtn => BtnSettings;
        public System.Windows.Shapes.Ellipse UpdateBadgeDot => UpdateBadge;
        public MenuItem MenuItemThemeLight => MenuThemeLight;
        public MenuItem MenuItemThemeDark => MenuThemeDark;
        public MenuItem MenuItemFlowToggle => MenuFlowToggle;

        // Single delegate instance reused across subscribe/unsubscribe.
        // Method-group conversion creates a fresh delegate on every call, so
        // `ThemeService.ThemeChanged -= UpdateSettingsMenu` would silently no-op
        // and leave this UserControl pinned alive by the static event forever.
        private Action _themeChangedHandler = null!;

        public ActionBarControl()
        {
            InitializeComponent();
            // Sync radio state once on load; ThemeChanged subscription keeps it
            // accurate when the theme flips via any path (menu, future shortcut,
            // external trigger). Unsubscribed on Unload so the static event does
            // not pin this UserControl alive after MainWindow closes.
            UpdateSettingsMenu();
            _themeChangedHandler = UpdateSettingsMenu;
            ThemeService.ThemeChanged += _themeChangedHandler;
            Unloaded += (_, _) => ThemeService.ThemeChanged -= _themeChangedHandler;

            // Subscribe to download progress — when an update is being
            // downloaded, show the progress bar above the Settings button
            // instead of spamming toast notifications.
            UpdateService.ProgressChanged += OnDownloadProgressChanged;

            // Delayed badge refresh — CheckOnStartupAsync completes a few
            // seconds after launch and saves pending-update version. This
            // one-shot timer picks it up so the badge appears without the
            // user needing to open the settings menu.
            var badgeTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4)
            };
            badgeTimer.Tick += (s, e) =>
            {
                badgeTimer.Stop();
                if (UpdateBadge != null)
                    UpdateBadge.Visibility = UpdateService.HasPendingUpdate()
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            };
            badgeTimer.Start();
        }

        /// <summary>
        /// Syncs the «Тема» radio menu items and Velopack Flow toggle
        /// to their current values from settings.
        /// Public so callers (or future opening flows) can force-refresh.
        /// </summary>
        public void UpdateSettingsMenu()
        {
            bool isDark = ThemeService.IsDarkTheme;
            if (MenuThemeLight != null) MenuThemeLight.IsChecked = !isDark;
            if (MenuThemeDark != null) MenuThemeDark.IsChecked = isDark;

            // Sync Velopack Flow toggle with current settings value
            if (MenuFlowToggle != null)
                MenuFlowToggle.IsChecked = AppSettingsService.IsFlowEnabled();

            // Show/hide update badge based on pending update status
            if (UpdateBadge != null)
                UpdateBadge.Visibility = UpdateService.HasPendingUpdate()
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        /// <summary>
        /// Resolves the parent MainWindow from DataContext, logging a diagnostic if the
        /// DataContext is not a MainWindow. Returns false in that case so callers can
        /// bail out gracefully.
        /// </summary>
        private bool TryGetMainWindow(string handlerName, [NotNullWhen(true)] out MainWindow? mw)
        {
            if (DataContext is MainWindow window)
            {
                mw = window;
                return true;
            }
            System.Diagnostics.Trace.WriteLine($"[ActionBarControl] DataContext is not MainWindow ({DataContext?.GetType().Name ?? "null"}), handler '{handlerName}' skipped.");
            mw = null;
            return false;
        }

        private void BtnPrintKp_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(BtnPrintKp_Click), out var mw)) return;
            var validItems = mw.OrderItems.Where(i => !string.IsNullOrEmpty(i.Name) && i.IsActive && i.Total > 0).ToList();
            if (validItems.Count == 0)
            {
                ToastService.ShowToast("Добавьте хотя бы одну позицию.", ToastType.Warning);
                return;
            }

            double total = validItems.Sum(i => i.TotalWithDeduction);
            string amountInWords = AmountInWordsService.Convert(total);
            string html = mw.PrintService.GenerateKpHtml(validItems, mw.ClientInfo, total, amountInWords);

            if (string.IsNullOrEmpty(html)) return;

            var preview = new PrintPreviewWindow(html) { Owner = mw };
            preview.ShowDialog();
        }

        // Kept `internal` (not `private`) so MainWindow's `CommandBinding Executed="BtnSaveOrder_Click"`
        // for ApplicationCommands.Save (Ctrl+S) can forward to the same save logic.
        internal void BtnSaveOrder_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(BtnSaveOrder_Click), out var mw)) return;
            var allItems = mw.OrderItems.Where(i => !string.IsNullOrEmpty(i.Name) && i.Total > 0).ToList();
            if (allItems.Count == 0)
            {
                ToastService.ShowToast("Добавьте хотя бы одну позицию перед сохранением.", ToastType.Warning);
                return;
            }

            var activeItems = allItems.Where(i => i.IsActive).ToList();
            var orderData = new OrderData
            {
                Id = mw.CurrentOrderId,
                ContractNumber = mw.ClientInfo.ContractNumber,
                ContractDate = mw.ClientInfo.ContractDate,
                ClientName = mw.ClientInfo.ClientName,
                ClientPhone = mw.ClientInfo.ClientPhone,
                ClientAddress = mw.ClientInfo.ClientAddress,
                Notes = mw.ClientInfo.Notes,
                HasAdditionalKp = mw.ClientInfo.HasAdditionalKp,
                AdditionalKpNumber = mw.ClientInfo.AdditionalKps.FirstOrDefault()?.Number ?? "",
                AdditionalKpAmount = mw.ClientInfo.AdditionalKpsTotal,
                AdditionalKps = mw.ClientInfo.AdditionalKps.Select(kp => new AdditionalKpItem
                {
                    Number = kp.Number,
                    Amount = kp.Amount,
                    IsActive = kp.IsActive
                }).ToList(),
                Status = mw.Sidebar.CmbOrderStatus.SelectedItem?.ToString() ?? OrderStatuses.All[0],
                TotalAmount = activeItems.Sum(i => i.TotalWithDeduction) + mw.ClientInfo.AdditionalKpsTotal,
                Items = allItems.Select(i => new OrderItemData
                {
                    Name = i.Name,
                    Color = i.Color,
                    Width = i.Width,
                    Height = i.Height,
                    Quantity = i.Quantity,
                    Price = i.Price,
                    // CalculatedValue and Total are intentionally NOT copied —
                    // they're always recomputed by OrderItem.Recalculate() on load
                    // (see OrderItemData XML doc). They were removed from the DTO
                    // in v3.22.0 as dead bytes.
                    InstallationMode = i.InstallationMode,
                    HasInstallation = i.InstallationMode == 0,
                    InstallationDeduction = i.InstallationDeduction,
                    InstallationSurcharge = i.InstallationSurcharge,
                    IsActive = i.IsActive,
                    // v3.29.2: AnwisSizeMode was missing from the mapper
                    // so saved orders silently reset to ББ 60 on reopen.
                    AnwisSizeMode = (int)i.AnwisSizeMode
                }).ToList()
            };

            mw.OrdersVM.SaveOrder(orderData);
            mw.IsNewOrder = false;
            mw.UpdateCurrentOrderInfo();
            mw.RefreshOrdersList();
            mw.MarkClean();
            mw.UndoRedo.Clear();

            ToastService.ShowToast($"Заказ {orderData.ContractNumber} сохранён!", ToastType.Success);
            if (!mw.SuppressPrefixSave)
                AppSettingsService.SaveContractPrefix(mw.Sidebar.TxtPrefix.Text);
        }

        private void BtnNewOrder_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(BtnNewOrder_Click), out var mw)) return;
            var validItems = mw.OrderItems.Where(i => !string.IsNullOrEmpty(i.Name) && i.Total > 0).ToList();
            if (validItems.Count > 0)
            {
                if (!DialogService.ShowConfirm("У вас есть несохранённые данные. Создать новый заказ?", "Новый заказ", mw)) return;
            }

            mw.StartNewOrder();
            mw.UpdateEmptyState();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            // Manually open the ContextMenu positioned right under the gear button.
            // The button's default right-click handler still works for accessibility.
            UpdateSettingsMenu();
            SettingsMenu.PlacementTarget = BtnSettings;
            SettingsMenu.Placement = PlacementMode.Bottom;
            SettingsMenu.IsOpen = true;
        }

        private void MenuThemeLight_Click(object sender, RoutedEventArgs e)
        {
            // Idempotent: only toggle if we'd actually change state.
            if (ThemeService.IsDarkTheme)
                ThemeService.ToggleTheme();
        }

        private void MenuThemeDark_Click(object sender, RoutedEventArgs e)
        {
            if (!ThemeService.IsDarkTheme)
                ThemeService.ToggleTheme();
        }

        private void MenuChangeLocation_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(MenuChangeLocation_Click), out var mw)) return;
            // Reuse the existing WelcomeWindow flow — it persists prefix + location
            // and refreshes the sidebar / title / contract number on close.
            mw.OpenWelcomeWindow();
        }

        private void MenuFlowToggle_Click(object sender, RoutedEventArgs e)
        {
            // Toggle the Velopack Flow setting. The MenuItem's IsChecked
            // is already flipped by WPF before this handler runs (IsCheckable).
            bool enabled = MenuFlowToggle.IsChecked;
            AppSettingsService.SetFlowEnabled(enabled);

            ToastService.ShowToast(
                enabled
                    ? "Автообновления включены (Velopack Flow)"
                    : "Автообновления отключены",
                ToastType.Info);
        }

        private async void MenuCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(MenuCheckUpdates_Click), out var mw)) return;
            await UpdateService.CheckAndApplyAsync(mw);
            // Refresh badge — pending version was cleared if update applied
            // or not found, set if a new version was found but user cancelled.
            UpdateSettingsMenu();
        }

        private void BtnSendToFactory_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(BtnSendToFactory_Click), out var mw)) return;
            var allItems = mw.OrderItems.Where(i => !string.IsNullOrEmpty(i.Name) && i.Total > 0).ToList();
            if (allItems.Count == 0)
            {
                ToastService.ShowToast("Добавьте хотя бы одну позицию.", ToastType.Warning);
                return;
            }

            string address = mw.ClientInfo.ClientAddress ?? "";
            var additionalKps = mw.ClientInfo.AdditionalKps
                .Where(kp => kp.IsActive)
                .ToList();

            var selectableItems = FactoryTextService.BuildSelectableItems(allItems, additionalKps);

            var window = new SendToFactoryWindow(address, selectableItems)
            {
                Owner = mw
            };
            window.ShowDialog();
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetMainWindow(nameof(BtnClearAll_Click), out var mw)) return;
            if (mw.OrderItems.Count == 0) return;

            if (DialogService.ShowConfirm("Очистить все позиции расчёта?", "Подтверждение", mw))
            {
                mw.PushUndo();
                mw.CalcVM.UnsubscribeAll(mw.UpdateTotal);
                mw.CalcVM.ClearAll();
                mw.UpdateTotal();
                mw.UpdateEmptyState();
            }
        }

        /// <summary>
        /// Handles UpdateService.ProgressChanged — shows/hides the compact
        /// download progress bar above the Settings button during update
        /// downloads, replacing the old toast-spam approach.
        /// </summary>
        private void OnDownloadProgressChanged(object? sender, EventArgs e)
        {
            if (UpdateService.IsDownloading)
            {
                DownloadProgressPanel.Visibility = Visibility.Visible;
                DownloadBar.Value = UpdateService.DownloadProgress;
                DownloadPercentText.Text = $"{UpdateService.DownloadProgress:F0}%";
            }
            else
            {
                DownloadProgressPanel.Visibility = Visibility.Collapsed;
                DownloadBar.Value = 0;
                DownloadPercentText.Text = "0%";
            }
        }
    }
}
